using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

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

        // 5) Player powers — write Amount into the dense slot. Unknown power
        //    types would be a SimCaps violation, so we trust the registry.
        WritePowers(playerCreature.Powers, PlayerPowers);

        // 6) Player piles. CardPile.Cards is IReadOnlyList<CardModel>; backed by
        //    a List internally, so for-loop indexing is JIT-friendly and alloc-free.
        HandCount    = SnapshotPile(pcs.Hand,        Hand);
        DrawCount    = SnapshotPile(pcs.DrawPile,    Draw);
        DiscCount    = SnapshotPile(pcs.DiscardPile, Disc);
        ExhaustCount = SnapshotPile(pcs.ExhaustPile, Exhaust);

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

            // Intent: pick the FIRST AbstractIntent in NextMove.Intents and
            // classify it. Most monsters only have one intent per move; for
            // multi-intent moves (e.g. attack-then-buff), the primary intent
            // by game convention is index 0, which is what the UI renders.
            CaptureIntent(e, i);
        }

        // 8) RNG: capture only the Shuffle stream today; the other 7 slots
        //    in Rngs stay zero-default until their consuming effects are
        //    implemented in the sim.
        var rngSet = combat.RunState.Rng;
        RandomStateOps.CaptureFromRng(rngSet.Shuffle, ref Rng(SimRngSlot.Shuffle));
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
    /// Encode every CardModel in <paramref name="pile"/> as ushort
    /// (bit15 = upgraded, bits0-14 = SimCardId) and write into
    /// <paramref name="dst"/>. Returns the number of cards written.
    /// </summary>
    private static int SnapshotPile(CardPile pile, ushort[] dst)
    {
        var cards = pile.Cards;
        int n = cards.Count;
        if (n > dst.Length)
            throw new InvalidOperationException(
                $"SimCombatState.Snapshot: pile has {n} cards > capacity {dst.Length}.");
        for (int i = 0; i < n; i++)
        {
            CardModel card = cards[i];
            ushort id = SimCardDb.GetId(card.GetType());
            if (card.IsUpgraded) id |= 0x8000;
            dst[i] = id;
        }
        return n;
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
    private static short  ClampS16(int v) => v < short.MinValue ? short.MinValue : v > short.MaxValue ? short.MaxValue : (short)v;
}
