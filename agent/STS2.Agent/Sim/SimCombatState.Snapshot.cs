using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2.Agent.Sim;

/// <summary>
/// Snapshot path: takes a live <see cref="CombatState"/> from the running
/// game and writes a complete pure-data picture into this
/// <see cref="SimCombatState"/>. Hot path target: ≤ 5 µs typical; called
/// once per "Think" request (DFS root), then never again until the player
/// takes an action and we re-snapshot.
///
/// Design rules (enforced by code, not just convention):
///   • No allocations. Every loop is a plain for/foreach over already-allocated
///     game collections; we never call Linq, never call <c>.ToList()</c>, never
///     box value types.
///   • No reference to game objects survives this method. The output is a
///     pure-data state safe to clone for thousands of DFS nodes.
///   • Bounds checked at SimCaps.EnsureVerified() (called on entry); this
///     method itself uses raw indexing trusting those invariants.
/// </summary>
internal sealed partial class SimCombatState
{
    private static readonly FieldInfo s_cardEnergyLocalModifiersField =
        typeof(CardEnergyCost).GetField("_localModifiers", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("SimCombatState.Snapshot: CardEnergyCost._localModifiers field not found.");

    /// <summary>
    /// Capture the full state of <paramref name="combat"/> into this instance.
    /// </summary>
    /// <param name="combat">Live game combat state.</param>
    /// <param name="playerIdx">Which player creature to root the snapshot on
    /// (always 0 in single-player; coop reserved for later).</param>
    /// <param name="coop">Single-player vs coop framing — see <see cref="CoopMode"/>.</param>
    public void Snapshot(CombatState combat, int playerIdx = 0, CoopMode coop = CoopMode.SoloRoot)
    {
        // 1) Enforce capacity invariants exactly once per process.
        SimCaps.EnsureVerified();

        // 2) Wipe scalars / counts / power vectors / RNG buffer.
        Reset();

        // 3) Round number (CombatState.RoundNumber is a 1-based int; byte is plenty).
        Round = (byte)combat.RoundNumber;

        // 4) Player creature & combat state.
        //    combat.PlayerCreatures allocates a fresh List on every access (LINQ
        //    Where().ToList()), so we walk combat.Creatures manually and pick the
        //    Nth IsPlayer creature. This stays alloc-free and gives identical
        //    semantics. _ = coop is reserved for future coop work.
        _ = coop;
        Creature playerCreature = FindPlayerCreature(combat, playerIdx);
        Player player = playerCreature.Player
            ?? throw new InvalidOperationException("SimCombatState.Snapshot: player creature has null Player.");
        PlayerCombatState pcs = player.PlayerCombatState
            ?? throw new InvalidOperationException("SimCombatState.Snapshot: player.PlayerCombatState is null (combat not started?).");

        PlayerHp     = ClampU16(playerCreature.CurrentHp);
        PlayerMaxHp  = ClampU16(playerCreature.MaxHp);
        PlayerBlock  = ClampU16(playerCreature.Block);
        Energy       = ClampU16(pcs.Energy);
        MaxEnergy    = ClampU16(pcs.MaxEnergy);
        PlayerStars  = ClampU16(pcs.Stars);

        // 5) Player powers — write Amount into the dense slot. Unknown power
        //    types would be a SimCaps violation, so we trust the registry.
        WritePowers(playerCreature.Powers, PlayerPowers);

        // 5a) Player power-internal counters (Feral / Juggling / VoidForm /
        //     Tender / Sloth / HardenedShell / Outbreak / Orbit / Automation /
        //     Illusion / Nemesis / Ritual). Sparse 14-byte struct.
        WritePowerInternals(playerCreature.Powers, ref PlayerPowerInternal);

        // 5b) Orb queue (Defect only; queue exists but stays empty for other characters).
        SnapshotOrbs(pcs.OrbQueue);

        // 5c) Pet (Necromancer only; cleared to default for other characters).
        SnapshotOsty(pcs);

        // 6) Player piles. CardPile.Cards is IReadOnlyList<CardModel>; backed by
        //    a List internally, so for-loop indexing is JIT-friendly and alloc-free.
        ushort nextCardInstanceId = 1;
        HandCount    = SnapshotPile(pcs.Hand,        Hand,    ref nextCardInstanceId);
        DrawCount    = SnapshotPile(pcs.DrawPile,    Draw,    ref nextCardInstanceId);
        DiscCount    = SnapshotPile(pcs.DiscardPile, Disc,    ref nextCardInstanceId);
        ExhaustCount = SnapshotPile(pcs.ExhaustPile, Exhaust, ref nextCardInstanceId);
        CardInstanceCount = (ushort)(nextCardInstanceId - 1);

        // 7) Enemies.
        var enemies = combat.Enemies;
        int enemyN = enemies.Count;
        if (enemyN > EnemyCap)
            throw new InvalidOperationException(
                $"SimCombatState.Snapshot: combat has {enemyN} enemies > EnemyCap={EnemyCap}. " +
                "Encounter exceeds capacity — SimCaps.Verify should have caught this at startup.");
        EnemyCount = enemyN;

        for (int i = 0; i < enemyN; i++)
        {
            Creature e = enemies[i];
            EnemyHp[i]    = ClampU16(e.CurrentHp);
            EnemyMaxHp[i] = ClampU16(e.MaxHp);
            EnemyBlock[i] = ClampU16(e.Block);

            // Enemy's row in the flat power matrix.
            int rowBase = i * PowersPerCre;
            WritePowersRow(e.Powers, EnemyPowers, rowBase);

            // Power-internal counters for this enemy (Nemesis/Ritual/Illusion
            // mostly, plus HardenedShell on hardened bosses).
            WritePowerInternals(e.Powers, ref EnemyPowerInternal[i]);

            // Move-state-machine: current node, first-move flag, log-derived
            // ever-used bitset, last-16 history, IllusionPower follow-up.
            SnapshotMoveSM(e, i);

            // Intent: pick the FIRST AbstractIntent in NextMove.Intents and
            // classify it. Most monsters only have one intent per move; for
            // multi-intent moves (e.g. attack-then-buff), the primary intent
            // by game convention is index 0, which is what the UI renders.
            CaptureIntent(e, i);
        }

        // 8) RNG: capture all 8 in-combat streams. Each is a separate
        //    System.Random behind the game's Rng wrapper; the captured Knuth
        //    state lets the DFS engine replay every roll bit-exact without
        //    touching the live game RNG. Slot order matches SimRngSlot.
        var rngSet = combat.RunState.Rng;
        RandomStateOps.CaptureFromRng(rngSet.Shuffle,              ref Rng(SimRngSlot.Shuffle));
        RandomStateOps.CaptureFromRng(rngSet.CombatTargets,        ref Rng(SimRngSlot.CombatTargets));
        RandomStateOps.CaptureFromRng(rngSet.CombatCardGeneration, ref Rng(SimRngSlot.CombatCardGeneration));
        RandomStateOps.CaptureFromRng(rngSet.CombatCardSelection,  ref Rng(SimRngSlot.CombatCardSelection));
        RandomStateOps.CaptureFromRng(rngSet.CombatEnergyCosts,    ref Rng(SimRngSlot.CombatEnergyCosts));
        RandomStateOps.CaptureFromRng(rngSet.CombatOrbGeneration,  ref Rng(SimRngSlot.CombatOrbGeneration));
        RandomStateOps.CaptureFromRng(rngSet.MonsterAi,            ref Rng(SimRngSlot.MonsterAi));
        RandomStateOps.CaptureFromRng(rngSet.Niche,                ref Rng(SimRngSlot.Niche));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Walk combat.Creatures, return the <paramref name="nth"/> IsPlayer creature.</summary>
    private static Creature FindPlayerCreature(CombatState combat, int nth)
    {
        // combat.Creatures is also a LINQ-backed property that allocates each
        // call. Cache to local then iterate by index — one allocation only.
        var all = combat.Creatures;
        int seen = 0;
        for (int i = 0, n = all.Count; i < n; i++)
        {
            Creature c = all[i];
            if (!c.IsPlayer) continue;
            if (seen == nth) return c;
            seen++;
        }
        throw new InvalidOperationException(
            $"SimCombatState.Snapshot: no player creature at index {nth} (have {seen}).");
    }

    /// <summary>Write every recognized PowerModel's Amount into the dense vector.</summary>
    private static void WritePowers(System.Collections.Generic.IReadOnlyList<PowerModel> powers, short[] dst)
    {
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            PowerModel p = powers[i];
            if (SimPowerRegistry.TryGetIndex(p.GetType(), out int idx))
                dst[idx] = ClampS16(p.Amount);
        }
    }

    /// <summary>Same as WritePowers but into row <paramref name="rowBase"/> of the flat enemy power matrix.</summary>
    private static void WritePowersRow(System.Collections.Generic.IReadOnlyList<PowerModel> powers, short[] flat, int rowBase)
    {
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            PowerModel p = powers[i];
            if (SimPowerRegistry.TryGetIndex(p.GetType(), out int idx))
                flat[rowBase + idx] = ClampS16(p.Amount);
        }
    }

    /// <summary>
    /// Encode every <see cref="CardModel"/> in <paramref name="pile"/> as a
    /// <see cref="SimCard"/> (CardId ushort with bit15=upgraded plus the seven
    /// mutable per-instance fields) and write into <paramref name="dst"/>.
    /// Returns the number of cards written.
    /// </summary>
    private int SnapshotPile(CardPile pile, SimCard[] dst, ref ushort nextCardInstanceId)
    {
        var cards = pile.Cards;
        int n = cards.Count;
        if (n > dst.Length)
            throw new InvalidOperationException(
                $"SimCombatState.Snapshot: pile has {n} cards > capacity {dst.Length}.");
        for (int i = 0; i < n; i++)
        {
            CardModel card = cards[i];

            // CardId: 15-bit SimCardId + bit-15 upgrade flag.
            ushort id = SimCardDb.GetId(card.GetType());
            if (card.IsUpgraded) id |= 0x8000;

            // Flags: ExhaustOnNextPlay is the only public dynamic-flag getter
            // we need; ShouldRetainThisTurn / IsSlyThisTurn collapse the
            // (per-instance _hasSingleTurnXxx OR static Keywords) source — for
            // sim outcomes this combined truth is what matters. Enchantment
            // disabled-state is packed here as well so one-shot enchantments
            // like Vigorous do not reactivate when the card moves between piles.
            ushort flags = 0;
            if (card.ExhaustOnNextPlay)    flags |= SimCard.FlagExhaustOnNextPlay;
            if (card.ShouldRetainThisTurn) flags |= SimCard.FlagShouldRetainThisTurn;
            if (card.IsSlyThisTurn)        flags |= SimCard.FlagIsSlyThisTurn;
            if (card.Keywords.Contains(CardKeyword.Exhaust))  flags |= SimCard.FlagHasExhaustKeyword;
            if (card.Keywords.Contains(CardKeyword.Retain))   flags |= SimCard.FlagHasRetainKeyword;
            if (card.Keywords.Contains(CardKeyword.Innate))   flags |= SimCard.FlagHasInnateKeyword;
            if (card.Keywords.Contains(CardKeyword.Eternal))  flags |= SimCard.FlagHasEternalKeyword;
            if (card.Keywords.Contains(CardKeyword.Ethereal)) flags |= SimCard.FlagHasEtherealKeyword;
            if (card.EnergyCost.CostsX)                       flags |= SimCard.FlagHasEnergyCostX;

            // Enchantment: 0 = none. Unknown EnchantmentModel subclasses fall
            // through to None (SimCaps validates the registry at startup, so
            // this branch is defensive — should never hit at runtime).
            byte encId = 0;
            byte encAmt = 0;
            EnchantmentModel? ench = card.Enchantment;
            if (ench != null)
            {
                encId  = SimEnchantmentRegistry.GetIndexOrNone(ench.GetType());
                encAmt = ClampU8(ench.Amount);
                if (ench.Status == EnchantmentStatus.Disabled)
                    flags |= SimCard.FlagEnchantmentDisabled;
            }

            byte affId = 0;
            byte affAmt = 0;
            AfflictionModel? aff = card.Affliction;
            if (aff != null)
            {
                affId = SimAfflictionRegistry.GetIndexOrNone(aff.GetType());
                affAmt = ClampU8(aff.Amount);

                if (aff is Devoured devoured && devoured.AppliedExhaust)
                    flags |= SimCard.FlagAfflictionAppliedExhaust;
                if (aff is Hexed hexed && hexed.AppliedEthereal)
                    flags |= SimCard.FlagAfflictionAppliedEthereal;
            }

            ushort instanceId = AllocateCardInstanceId(ref nextCardInstanceId);
            SnapshotCardEnergyState(card, instanceId);

            dst[i] = new SimCard
            {
                CardId            = id,
                InstanceId        = instanceId,
                BaseStarCost      = ClampS8(card.BaseStarCost),
                LastStarsSpent    = ClampU8(card.LastStarsSpent),
                BaseReplayCount   = ClampU8(card.BaseReplayCount),
                Flags             = flags,
                EnchantmentId     = encId,
                EnchantmentAmount = encAmt,
                AfflictionId      = affId,
                AfflictionAmount  = affAmt,
            };
        }
        return n;
    }

    private void SnapshotCardEnergyState(CardModel card, ushort instanceId)
    {
        if (instanceId == 0 || instanceId > CardInstanceCap)
            throw new InvalidOperationException(
                $"SimCombatState.Snapshot: card instance id {instanceId} exceeds CardInstanceCap={CardInstanceCap}.");

        CardEnergyCost energy = card.EnergyCost;
        CardEnergyBaseCost[instanceId] = ClampS16(energy.GetWithModifiers(CostModifiers.None));
        CardEnergyCapturedX[instanceId] = energy.CostsX ? ClampU16(energy.CapturedXValue) : (ushort)0;
        CardEnergyModifierStart[instanceId] = CardEnergyModifierUsed;

        List<LocalCostModifier> localModifiers =
            (List<LocalCostModifier>?)s_cardEnergyLocalModifiersField.GetValue(energy)
            ?? throw new InvalidOperationException("SimCombatState.Snapshot: CardEnergyCost._localModifiers was null.");

        int count = localModifiers.Count;
        if (CardEnergyModifierUsed + count > CardEnergyModifierCap)
        {
            throw new InvalidOperationException(
                $"SimCombatState.Snapshot: card energy modifiers overflowed CardEnergyModifierCap={CardEnergyModifierCap}. " +
                $"Used={CardEnergyModifierUsed}, incoming={count}, card={card.Id.Entry}.");
        }

        CardEnergyModifierCount[instanceId] = (ushort)count;
        for (int i = 0; i < count; i++)
            CardEnergyModifiers[CardEnergyModifierUsed++] = SimLocalCostModifier.From(localModifiers[i]);
    }

    private static ushort AllocateCardInstanceId(ref ushort nextCardInstanceId)
    {
        ushort id = nextCardInstanceId;
        if (id == 0)
            throw new InvalidOperationException("SimCombatState.Snapshot: card instance id overflowed ushort range.");
        nextCardInstanceId++;
        return id;
    }

    private static short ClampS16(int v) => v < short.MinValue ? short.MinValue
                                           : v > short.MaxValue ? short.MaxValue : (short)v;

    private static byte  ClampU8(int v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
    private static sbyte ClampS8(int v) => v < sbyte.MinValue ? sbyte.MinValue
                                          : v > sbyte.MaxValue ? sbyte.MaxValue : (sbyte)v;

    /// <summary>
    /// Pack the live <see cref="OrbQueue"/> into <see cref="OrbSlots"/>.
    /// Each orb becomes one ushort: low 3 bits = SimOrbType, high 13 bits =
    /// the per-instance mutable state (raw <c>_evokeVal</c> for DarkOrb,
    /// raw <c>_passiveVal</c> for GlassOrb, 0 otherwise). Reflection reads
    /// the private backing fields because the public PassiveVal/EvokeVal
    /// properties already apply Focus, which is power-side state we capture
    /// separately on PlayerPowers.
    ///
    /// Slots beyond <see cref="OrbCount"/> are left at 0 (None) by Reset(),
    /// so DFS never reads past the live prefix.
    /// </summary>
    private void SnapshotOrbs(OrbQueue queue)
    {
        var orbs = queue.Orbs;
        int n = orbs.Count;
        if (n > 10)
            throw new InvalidOperationException(
                $"SimCombatState.Snapshot: orb queue has {n} orbs > maxCapacity 10. " +
                "OrbQueue.maxCapacity changed in game source — update OrbSlots10 accordingly.");

        OrbCount    = (byte)n;
        OrbCapacity = ClampU8(queue.Capacity);

        for (int i = 0; i < n; i++)
        {
            OrbModel orb = orbs[i];
            byte type = SimOrbRegistry.GetIndexOrNone(orb.GetType());
            int  mut  = orb switch
            {
                DarkOrb  d => SimOrbRegistry.ReadDarkEvokeVal(d),
                GlassOrb g => SimOrbRegistry.ReadGlassPassiveVal(g),
                _          => 0,
            };
            OrbSlots[i] = SimOrb.Pack(type, mut);
        }
    }

    /// <summary>
    /// Capture the Necromancer's Osty pet — the only pet currently shipped.
    /// <c>PlayerCombatState.GetPet&lt;Osty&gt;()</c> is FirstOrDefault, so at
    /// most one is returned. If no Osty has ever been summoned this combat,
    /// the SimPet stays at default (Exists=0); if it died, Exists stays 1
    /// and CurrentHp=0 (corpse retained for OstyCmd.Summon revival).
    /// </summary>
    private void SnapshotOsty(PlayerCombatState pcs)
    {
        Creature? pet = pcs.GetPet<Osty>();
        if (pet == null) return;   // never summoned this combat — leave default.

        Osty = new SimPet
        {
            CurrentHp = ClampU16(pet.CurrentHp),
            MaxHp     = ClampU16(pet.MaxHp),
            Block     = ClampU16(pet.Block),
            Exists    = 1,
        };
        WritePowers(pet.Powers, OstyPowers);
        WritePowerInternals(pet.Powers, ref OstyPowerInternal);
    }

    /// <summary>
    /// Walk a creature's power list once and copy any tracked private
    /// counter into <paramref name="dst"/>. Pattern-matched switch dispatches
    /// directly to the typed reflection helpers; the JIT lowers each case
    /// to a class-tag compare + a single virtual-call-free method invocation.
    ///
    /// Snapshot path only — each helper does one or two FieldInfo.GetValue
    /// calls and is fine here. Never call this from the DFS hot loop.
    /// </summary>
    private static void WritePowerInternals(System.Collections.Generic.IReadOnlyList<PowerModel> powers, ref SimPowerInternal dst)
    {
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            switch (powers[i])
            {
                case FeralPower         fp: dst.FeralZeroCostAttacks       = ClampU8(SimPowerInternalReader.ReadFeralZeroCostAttacks(fp)); break;
                case JugglingPower      jp: dst.JugglingAttacksThisTurn    = ClampU8(SimPowerInternalReader.ReadJugglingAttacks(jp));      break;
                case VoidFormPower      vp: dst.VoidFormCardsThisTurn      = ClampU8(SimPowerInternalReader.ReadVoidFormCards(vp));        break;
                case TenderPower        tp: dst.TenderCardsThisTurn        = ClampU8(SimPowerInternalReader.ReadTenderCardsThisTurn(tp));  break;
                case SlothPower         sp: dst.SlothCardsThisTurn         = ClampU8(SimPowerInternalReader.ReadSlothCardsThisTurn(sp));   break;
                case HardenedShellPower hp: dst.HardenedShellDamageThisTurn= ClampU16(SimPowerInternalReader.ReadHardenedShellDamage(hp));break;
                case OutbreakPower      op: dst.OutbreakTimesPoisoned      = ClampU8(SimPowerInternalReader.ReadOutbreakTimesPoisoned(op));break;
                case OrbitPower         orb:
                    dst.OrbitEnergySpent  = ClampU16(SimPowerInternalReader.ReadOrbitEnergySpent(orb));
                    dst.OrbitTriggerCount = ClampU16(SimPowerInternalReader.ReadOrbitTriggerCount(orb));
                    break;
                case AutomationPower    ap: dst.AutomationCardsLeft        = ClampU8(SimPowerInternalReader.ReadAutomationCardsLeft(ap)); break;
                case IllusionPower      ip:
                    if (SimPowerInternalReader.ReadIllusionIsReviving(ip)) dst.Flags |= SimPowerInternal.FlagIllusionIsReviving;
                    break;
                case NemesisPower       np:
                    if (SimPowerInternalReader.ReadNemesisShouldApplyIntangible(np)) dst.Flags |= SimPowerInternal.FlagNemesisShouldApplyIntangible;
                    break;
                case RitualPower        rp:
                    if (SimPowerInternalReader.ReadRitualWasJustAppliedByEnemy(rp)) dst.Flags |= SimPowerInternal.FlagRitualWasJustAppliedByEnemy;
                    break;
            }
        }
    }

    /// <summary>
    /// Inspect <paramref name="enemy"/>.Monster.NextMove.Intents[0] and write
    /// the classified <see cref="SimIntent"/> kind into <see cref="EnemyIntent"/>[idx].
    /// For attacks, also fills <see cref="EnemyIntentDmg"/> and
    /// <see cref="EnemyIntentHits"/> with the displayed base damage and hit
    /// count (Str/Vuln/Weak modifiers are applied at sim time, not snapshot time —
    /// this is Method D from the design discussion: cheap base capture +
    /// integer arithmetic in the hot path).
    /// </summary>
    private void CaptureIntent(Creature enemy, int idx)
    {
        var move = enemy.Monster?.NextMove;
        if (move == null || move.Intents.Count == 0)
        {
            EnemyIntent[idx] = (byte)SimIntent.Unknown;
            return;
        }

        AbstractIntent first = move.Intents[0];
        switch (first)
        {
            case DeathBlowIntent dbi:
                EnemyIntent[idx]     = (byte)SimIntent.DeathBlow;
                EnemyIntentDmg[idx]  = AttackDamage(dbi);
                EnemyIntentHits[idx] = AttackHits(dbi);
                break;
            case AttackIntent ai:
                EnemyIntent[idx]     = (byte)SimIntent.Attack;
                EnemyIntentDmg[idx]  = AttackDamage(ai);
                EnemyIntentHits[idx] = AttackHits(ai);
                break;
            case BuffIntent:       EnemyIntent[idx] = (byte)SimIntent.Buff; break;
            case CardDebuffIntent: EnemyIntent[idx] = (byte)SimIntent.CardDebuff; break;
            case DebuffIntent dbi:
                EnemyIntent[idx] = (byte)(dbi.IntentType == IntentType.DebuffStrong
                    ? SimIntent.DebuffStrong : SimIntent.Debuff);
                break;
            case DefendIntent:     EnemyIntent[idx] = (byte)SimIntent.Defend; break;
            case EscapeIntent:     EnemyIntent[idx] = (byte)SimIntent.Escape; break;
            case HealIntent:       EnemyIntent[idx] = (byte)SimIntent.Heal; break;
            case HiddenIntent:     EnemyIntent[idx] = (byte)SimIntent.Hidden; break;
            case SleepIntent:      EnemyIntent[idx] = (byte)SimIntent.Sleep; break;
            case StatusIntent:     EnemyIntent[idx] = (byte)SimIntent.StatusCard; break;
            case StunIntent:       EnemyIntent[idx] = (byte)SimIntent.Stun; break;
            case SummonIntent:     EnemyIntent[idx] = (byte)SimIntent.Summon; break;
            default:               EnemyIntent[idx] = (byte)SimIntent.Unknown; break;
        }
    }

    /// <summary>Resolve AttackIntent.DamageCalc into a clamped ushort base damage.</summary>
    private static ushort AttackDamage(AttackIntent ai)
    {
        var calc = ai.DamageCalc;
        if (calc == null) return 0;
        decimal raw = calc();
        if (raw < 0m)     return 0;
        if (raw > 65535m) return 65535;
        return (ushort)raw;
    }

    /// <summary>AttackIntent.Repeats is hits-after-the-first; the displayed total is Repeats+1, min 1.</summary>
    private static byte AttackHits(AttackIntent ai)
    {
        int hits = ai.Repeats + 1;
        if (hits < 1)   hits = 1;
        if (hits > 255) hits = 255;
        return (byte)hits;
    }

    private static ushort ClampU16(int v) => v < 0 ? (ushort)0 : v > 65535 ? (ushort)65535 : (ushort)v;

    /// <summary>
    /// Capture the live <c>MonsterMoveStateMachine</c> state for enemy slot
    /// <paramref name="idx"/> into <see cref="EnemyMoveSM"/>[idx], and stash
    /// the per-Type metadata table in <see cref="EnemyMoveTables"/>[idx].
    /// Snapshot path only: does reflection reads + a dictionary build on the
    /// first encounter of each monster type, then plain field reads forever.
    ///
    /// <para>Layout-derived design choices:</para>
    /// <list type="bullet">
    ///   <item>Full <c>StateLog</c> is reduced to a 32-bit <c>EverUsedBitset</c>
    ///     (one bit per state) plus the last 16 entries in a ring buffer. The
    ///     <see cref="MonsterStateTable"/> ctor verifies no rule needs deeper
    ///     history (cooldown / maxTimes ≤ 16); a violation throws here, before
    ///     the silently-truncated state ever reaches the search engine.</item>
    ///   <item><c>IllusionPower.FollowUpStateId</c> (a string) is resolved
    ///     against the table once and stored as a <c>byte</c> index; the
    ///     associated flag bit lets the hot path branch without re-reading
    ///     the sentinel <c>0xFF</c>.</item>
    /// </list>
    /// </summary>
    private void SnapshotMoveSM(Creature enemy, int idx)
    {
        var monster = enemy.Monster;
        if (monster == null) return;

        MonsterStateTable? table = SimMonsterStateRegistry.GetOrBuild(monster);
        if (table == null) return; // monster has no state machine (not yet set up)

        EnemyMoveTables[idx] = table;

        // Live state machine — already non-null because GetOrBuild returned a table.
        MonsterMoveStateMachine sm = monster.MoveStateMachine!;

        ref SimEnemyMoveSM dst = ref EnemyMoveSM[idx];
        dst = default; // wipe any stale slot data left by Reset()'s slab clear

        // ── Current state ─────────────────────────────────────────────────
        MonsterState cur = SimMonsterStateRegistry.GetCurrentState(sm);
        if (!table.IdToIdx.TryGetValue(cur.Id, out byte curIdx))
        {
            // States dict was built from this same machine — a missing key
            // means the game mutated the dict mid-combat (it doesn't, today).
            // Surface as a hard error rather than silently storing 0.
            throw new InvalidOperationException(
                $"SimCombatState.SnapshotMoveSM: monster '{monster.GetType().FullName}' " +
                $"current state id '{cur.Id}' not present in cached MonsterStateTable. " +
                "States dict mutated post-construction?");
        }
        dst.CurrentStateIdx = curIdx;

        // ── First-move flag ──────────────────────────────────────────────
        if (SimMonsterStateRegistry.GetPerformedFirstMove(sm))
            dst.Flags |= SimEnemyMoveSM.FlagPerformedFirstMove;

        // ── Full StateLog → EverUsedBitset (UseOnlyOnce O(1)) ─────────────
        var log = sm.StateLog;
        int logN = log.Count;
        uint bitset = 0u;
        for (int j = 0; j < logN; j++)
        {
            // Use TryGet: future game patches may add transient states the
            // table doesn't cover; we silently drop them rather than throw,
            // since they wouldn't be valid lookup targets in any rule either.
            if (table.IdToIdx.TryGetValue(log[j].Id, out byte b))
                bitset |= 1u << b;
        }
        dst.EverUsedBitset = bitset;

        // ── Last min(16, logN) entries → ring buffer ──────────────────────
        // Snapshot writes oldest-first into slots 0..keep-1. HistoryHead points
        // to the next free slot, which is the oldest-relative "start" once full.
        int keep  = logN < SimEnemyMoveSM.HistoryCap ? logN : SimEnemyMoveSM.HistoryCap;
        int start = logN - keep;
        for (int j = 0; j < keep; j++)
        {
            if (table.IdToIdx.TryGetValue(log[start + j].Id, out byte b))
                dst.History[j] = b;
            else
                dst.History[j] = 0xFF; // unknown state — sentinel; no rule will match it
        }
        dst.HistoryCount = (byte)keep;
        // When count < 16: head == count (next write goes to slot `count`).
        // When count == 16: head == 0 (next write overwrites the oldest).
        dst.HistoryHead  = (byte)(keep == SimEnemyMoveSM.HistoryCap ? 0 : keep);

        // ── IllusionPower.FollowUpStateId → byte index + flag ─────────────
        // FollowUpStateId is a public property; no reflection needed. Direct
        // field on IllusionPower (sibling of the IsReviving Data class), so
        // we re-walk the power list rather than entangle this with the
        // existing WritePowerInternals switch.
        dst.IllusionFollowUpIdx = SimEnemyMoveSM.NoFollowUp;
        var powers = enemy.Powers;
        for (int j = 0, m = powers.Count; j < m; j++)
        {
            if (powers[j] is IllusionPower ip)
            {
                string? fid = ip.FollowUpStateId;
                if (fid != null && table.IdToIdx.TryGetValue(fid, out byte fidx))
                {
                    dst.IllusionFollowUpIdx = fidx;
                    dst.Flags |= SimEnemyMoveSM.FlagHasIllusionFollowUp;
                }
                break; // PowerStackType.Single — one Illusion per creature.
            }
        }
    }
}
