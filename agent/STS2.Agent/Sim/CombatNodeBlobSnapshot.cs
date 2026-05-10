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
/// Direct live-combat snapshot writer for the audited V1 blob slice.
/// Writes the current hot combat state straight into <see cref="CombatNodeBlob"/>
/// with no intermediate legacy container.
/// </summary>
internal static class CombatNodeBlobSnapshot
{
    private static readonly FieldInfo s_cardEnergyLocalModifiersField =
        typeof(CardEnergyCost).GetField("_localModifiers", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatNodeBlobSnapshot: CardEnergyCost._localModifiers field not found.");
    private static readonly FieldInfo s_cardTemporaryStarCostsField =
        typeof(CardModel).GetField("_temporaryStarCosts", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatNodeBlobSnapshot: CardModel._temporaryStarCosts field not found.");

    private static readonly Dictionary<CardModel, ushort> s_cardPlayHistoryFlags = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardModel, ushort> s_cardInstanceIds = new(ReferenceEqualityComparer.Instance);

    public static void WriteV1FromCombatState(CombatState combat, CombatNodeBlob dst, int playerIdx = 0, CoopMode coop = CoopMode.SoloRoot)
    {
        SimCaps.EnsureVerified();
        dst.Clear();

        _ = coop;
        Creature playerCreature = FindPlayerCreature(combat, playerIdx);
        Player player = playerCreature.Player
            ?? throw new InvalidOperationException("CombatNodeBlobSnapshot: player creature has null Player.");
        PlayerCombatState pcs = player.PlayerCombatState
            ?? throw new InvalidOperationException("CombatNodeBlobSnapshot: player.PlayerCombatState is null (combat not started?).");

        dst.Round = (byte)combat.RoundNumber;
        dst.CurrentSide = (byte)combat.CurrentSide;
        dst.PlayerHp = ClampU16(playerCreature.CurrentHp);
        dst.PlayerMaxHp = ClampU16(playerCreature.MaxHp);
        dst.PlayerBlock = ClampU16(playerCreature.Block);
        dst.Energy = ClampU16(pcs.Energy);
        dst.MaxEnergy = ClampU16(pcs.MaxEnergy);
        dst.PlayerStars = ClampU16(pcs.Stars);

        WritePowers(playerCreature.Powers, dst.PlayerPowers);
        SimPowerInternal playerPowerInternal = default;
        WritePowerInternals(playerCreature.Powers, ref playerPowerInternal);
        dst.PlayerPowerInternal = playerPowerInternal;
        SnapshotOrbs(pcs.OrbQueue, dst);
        SnapshotOsty(pcs, dst);
        SimHistoryCounters historyCounters = default;
        SimHistoryCounterOps.CaptureLive(combat, player, ref historyCounters);
        dst.HistoryCounters = historyCounters;
        SimHistoryCounterOps.CaptureCardPlayFlags(combat, player, s_cardPlayHistoryFlags);
        s_cardInstanceIds.Clear();

        ushort nextCardInstanceId = 1;
        dst.HandCount = SnapshotPile(pcs.Hand, dst.HandCards, dst, s_cardPlayHistoryFlags, s_cardInstanceIds, ref nextCardInstanceId);
        dst.DrawCount = SnapshotPile(pcs.DrawPile, dst.DrawCards, dst, s_cardPlayHistoryFlags, s_cardInstanceIds, ref nextCardInstanceId);
        dst.DiscCount = SnapshotPile(pcs.DiscardPile, dst.DiscCards, dst, s_cardPlayHistoryFlags, s_cardInstanceIds, ref nextCardInstanceId);
        dst.ExhaustCount = SnapshotPile(pcs.ExhaustPile, dst.ExhaustCards, dst, s_cardPlayHistoryFlags, s_cardInstanceIds, ref nextCardInstanceId);

        CardModel? historyCourseSource = SimHistoryCounterOps.ReadHistoryCourseSourceCard(combat, player);
        if (historyCourseSource != null)
        {
            ushort sharedInstanceId = s_cardInstanceIds.GetValueOrDefault(historyCourseSource);
            dst.HistoryCourseCard = SnapshotCard(historyCourseSource, dst, s_cardPlayHistoryFlags, ref nextCardInstanceId, sharedInstanceId);
        }

        dst.CardInstanceCount = (ushort)(nextCardInstanceId - 1);

        var enemies = combat.Enemies;
        int enemyN = enemies.Count;
        if (enemyN > CombatSchemaV1.Enemies.EnemyCap)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: combat has {enemyN} enemies > EnemyCap={CombatSchemaV1.Enemies.EnemyCap}. " +
                "Encounter exceeds capacity — SimCaps.Verify should have caught this at startup.");
        }

        dst.EnemyCount = (byte)enemyN;
        for (int i = 0; i < enemyN; i++)
        {
            Creature enemy = enemies[i];
            dst.EnemyHp[i] = ClampU16(enemy.CurrentHp);
            dst.EnemyMaxHp[i] = ClampU16(enemy.MaxHp);
            dst.EnemyBlock[i] = ClampU16(enemy.Block);

            int rowBase = i * CombatSimLayout.PowersPerCre;
            WritePowersRow(enemy.Powers, dst.EnemyPowers, rowBase);

            SimPowerInternal enemyPowerInternal = default;
            WritePowerInternals(enemy.Powers, ref enemyPowerInternal);
            dst.EnemyPowerInternal[i] = enemyPowerInternal;

            SnapshotMoveSM(enemy, ref dst.EnemyMoveSM[i], ref dst.EnemyMoveTableHandles[i]);
            CaptureIntent(enemy, i, dst);
        }

        var rngSet = combat.RunState.Rng;
        RandomStateOps.CaptureFromRng(rngSet.Shuffle, ref dst.Rng(SimRngSlot.Shuffle));
        RandomStateOps.CaptureFromRng(rngSet.CombatTargets, ref dst.Rng(SimRngSlot.CombatTargets));
        RandomStateOps.CaptureFromRng(rngSet.CombatCardGeneration, ref dst.Rng(SimRngSlot.CombatCardGeneration));
        RandomStateOps.CaptureFromRng(rngSet.CombatCardSelection, ref dst.Rng(SimRngSlot.CombatCardSelection));
        RandomStateOps.CaptureFromRng(rngSet.CombatEnergyCosts, ref dst.Rng(SimRngSlot.CombatEnergyCosts));
        RandomStateOps.CaptureFromRng(rngSet.CombatOrbGeneration, ref dst.Rng(SimRngSlot.CombatOrbGeneration));
        RandomStateOps.CaptureFromRng(rngSet.MonsterAi, ref dst.Rng(SimRngSlot.MonsterAi));
        RandomStateOps.CaptureFromRng(rngSet.Niche, ref dst.Rng(SimRngSlot.Niche));
    }

    private static Creature FindPlayerCreature(CombatState combat, int nth)
    {
        var all = combat.Creatures;
        int seen = 0;
        for (int i = 0, n = all.Count; i < n; i++)
        {
            Creature creature = all[i];
            if (!creature.IsPlayer) continue;
            if (seen == nth) return creature;
            seen++;
        }

        throw new InvalidOperationException(
            $"CombatNodeBlobSnapshot: no player creature at index {nth} (have {seen}).");
    }

    private static void WritePowers(IReadOnlyList<PowerModel> powers, Span<short> dst)
    {
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            PowerModel power = powers[i];
            if (SimPowerRegistry.TryGetIndex(power.GetType(), out int idx))
                dst[idx] = ClampS16(power.Amount);
        }
    }

    private static void WritePowersRow(IReadOnlyList<PowerModel> powers, Span<short> flat, int rowBase)
    {
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            PowerModel power = powers[i];
            if (SimPowerRegistry.TryGetIndex(power.GetType(), out int idx))
                flat[rowBase + idx] = ClampS16(power.Amount);
        }
    }

    private static ushort SnapshotPile(
        CardPile pile,
        Span<SimCard> dst,
        CombatNodeBlob blob,
        Dictionary<CardModel, ushort> cardHistoryFlags,
        Dictionary<CardModel, ushort> cardInstanceIds,
        ref ushort nextCardInstanceId)
    {
        var cards = pile.Cards;
        int count = cards.Count;
        if (count > dst.Length)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: pile has {count} cards > capacity {dst.Length}.");
        }

        for (int i = 0; i < count; i++)
        {
            CardModel card = cards[i];
            SimCard simCard = SnapshotCard(card, blob, cardHistoryFlags, ref nextCardInstanceId);
            cardInstanceIds[card] = simCard.InstanceId;
            dst[i] = simCard;
        }

        return (ushort)count;
    }

    private static SimCard SnapshotCard(
        CardModel card,
        CombatNodeBlob blob,
        Dictionary<CardModel, ushort> cardHistoryFlags,
        ref ushort nextCardInstanceId,
        ushort existingInstanceId = 0)
    {
        ushort id = SimCardDb.GetId(card.GetType());
        if (card.IsUpgraded) id |= 0x8000;

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
        if (card.IsDupe)                                  flags |= SimCard.FlagIsDupe;
        if (cardHistoryFlags.TryGetValue(card, out ushort historyFlags))
            flags |= historyFlags;

        byte encId = 0;
        byte encAmt = 0;
        EnchantmentModel? enchantment = card.Enchantment;
        if (enchantment != null)
        {
            encId = SimEnchantmentRegistry.GetIndexOrNone(enchantment.GetType());
            encAmt = ClampU8(enchantment.Amount);
            if (enchantment.Status == EnchantmentStatus.Disabled)
                flags |= SimCard.FlagEnchantmentDisabled;
        }

        byte affId = 0;
        byte affAmt = 0;
        AfflictionModel? affliction = card.Affliction;
        if (affliction != null)
        {
            affId = SimAfflictionRegistry.GetIndexOrNone(affliction.GetType());
            affAmt = ClampU8(affliction.Amount);

            if (affliction is Devoured devoured && devoured.AppliedExhaust)
                flags |= SimCard.FlagAfflictionAppliedExhaust;
            if (affliction is Hexed hexed && hexed.AppliedEthereal)
                flags |= SimCard.FlagAfflictionAppliedEthereal;
        }

        ushort instanceId = existingInstanceId != 0 ? existingInstanceId : AllocateCardInstanceId(ref nextCardInstanceId);
        if (existingInstanceId == 0)
        {
            SnapshotCardEnergyState(card, instanceId, blob);
            SnapshotCardTemporaryStarCostState(card, instanceId, blob);
        }

        return new SimCard
        {
            CardId = id,
            InstanceId = instanceId,
            BaseStarCost = ClampS8(card.BaseStarCost),
            LastStarsSpent = ClampU8(card.LastStarsSpent),
            BaseReplayCount = ClampU8(card.BaseReplayCount),
            Flags = flags,
            EnchantmentId = encId,
            EnchantmentAmount = encAmt,
            AfflictionId = affId,
            AfflictionAmount = affAmt,
        };
    }

    private static void SnapshotCardEnergyState(CardModel card, ushort instanceId, CombatNodeBlob blob)
    {
        if (instanceId == 0 || instanceId > CombatSchemaV1.Cards.CardInstanceCap)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: card instance id {instanceId} exceeds CardInstanceCap={CombatSchemaV1.Cards.CardInstanceCap}.");
        }

        CardEnergyCost energy = card.EnergyCost;
        blob.CardEnergyBaseCost[instanceId] = ClampS16(energy.GetWithModifiers(CostModifiers.None));
        blob.CardEnergyCapturedX[instanceId] = energy.CostsX ? ClampU16(energy.CapturedXValue) : (ushort)0;

        ref ushort modifierUsed = ref blob.CardEnergyModifierUsed;
        blob.CardEnergyModifierStart[instanceId] = modifierUsed;

        List<LocalCostModifier> localModifiers =
            (List<LocalCostModifier>?)s_cardEnergyLocalModifiersField.GetValue(energy)
            ?? throw new InvalidOperationException("CombatNodeBlobSnapshot: CardEnergyCost._localModifiers was null.");

        int count = localModifiers.Count;
        if (modifierUsed + count > CombatSchemaV1.Cards.CardEnergyModifierCap)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: card energy modifiers overflowed CardEnergyModifierCap={CombatSchemaV1.Cards.CardEnergyModifierCap}. " +
                $"Used={modifierUsed}, incoming={count}, card={card.Id.Entry}.");
        }

        blob.CardEnergyModifierCount[instanceId] = (ushort)count;
        for (int i = 0; i < count; i++)
            blob.CardEnergyModifiers[modifierUsed++] = SimLocalCostModifier.From(localModifiers[i]);
    }

    private static void SnapshotCardTemporaryStarCostState(CardModel card, ushort instanceId, CombatNodeBlob blob)
    {
        if (instanceId == 0 || instanceId > CombatSchemaV1.Cards.CardInstanceCap)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: card instance id {instanceId} exceeds CardInstanceCap={CombatSchemaV1.Cards.CardInstanceCap}.");
        }

        ref ushort used = ref blob.CardTemporaryStarCostUsed;
        blob.CardTemporaryStarCostStart[instanceId] = used;

        List<TemporaryCardCost> temporaryStarCosts =
            (List<TemporaryCardCost>?)s_cardTemporaryStarCostsField.GetValue(card)
            ?? throw new InvalidOperationException("CombatNodeBlobSnapshot: CardModel._temporaryStarCosts was null.");

        int count = temporaryStarCosts.Count;
        if (used + count > CombatSchemaV1.Cards.CardTemporaryStarCostCap)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: temporary star costs overflowed CardTemporaryStarCostCap={CombatSchemaV1.Cards.CardTemporaryStarCostCap}. " +
                $"Used={used}, incoming={count}, card={card.Id.Entry}.");
        }

        blob.CardTemporaryStarCostCount[instanceId] = (ushort)count;
        for (int i = 0; i < count; i++)
            blob.CardTemporaryStarCosts[used++] = SimTemporaryStarCost.From(temporaryStarCosts[i]);
    }

    private static ushort AllocateCardInstanceId(ref ushort nextCardInstanceId)
    {
        ushort id = nextCardInstanceId;
        if (id == 0)
            throw new InvalidOperationException("CombatNodeBlobSnapshot: card instance id overflowed ushort range.");

        nextCardInstanceId++;
        return id;
    }

    private static void WritePowerInternals(IReadOnlyList<PowerModel> powers, ref SimPowerInternal dst)
    {
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            switch (powers[i])
            {
                case FeralPower fp:
                    dst.FeralZeroCostAttacks = ClampU8(SimPowerInternalReader.ReadFeralZeroCostAttacks(fp));
                    break;
                case JugglingPower jp:
                    dst.JugglingAttacksThisTurn = ClampU8(SimPowerInternalReader.ReadJugglingAttacks(jp));
                    break;
                case VoidFormPower vp:
                    dst.VoidFormCardsThisTurn = ClampU8(SimPowerInternalReader.ReadVoidFormCards(vp));
                    break;
                case TenderPower tp:
                    dst.TenderCardsThisTurn = ClampU8(SimPowerInternalReader.ReadTenderCardsThisTurn(tp));
                    break;
                case SlothPower sp:
                    dst.SlothCardsThisTurn = ClampU8(SimPowerInternalReader.ReadSlothCardsThisTurn(sp));
                    break;
                case HardenedShellPower hp:
                    dst.HardenedShellDamageThisTurn = ClampU16(SimPowerInternalReader.ReadHardenedShellDamage(hp));
                    break;
                case OutbreakPower op:
                    dst.OutbreakTimesPoisoned = ClampU8(SimPowerInternalReader.ReadOutbreakTimesPoisoned(op));
                    break;
                case OrbitPower orbit:
                    dst.OrbitEnergySpent = ClampU16(SimPowerInternalReader.ReadOrbitEnergySpent(orbit));
                    dst.OrbitTriggerCount = ClampU16(SimPowerInternalReader.ReadOrbitTriggerCount(orbit));
                    break;
                case AutomationPower ap:
                    dst.AutomationCardsLeft = ClampU8(SimPowerInternalReader.ReadAutomationCardsLeft(ap));
                    break;
                case IllusionPower ip:
                    if (SimPowerInternalReader.ReadIllusionIsReviving(ip)) dst.Flags |= SimPowerInternal.FlagIllusionIsReviving;
                    break;
                case NemesisPower np:
                    if (SimPowerInternalReader.ReadNemesisShouldApplyIntangible(np)) dst.Flags |= SimPowerInternal.FlagNemesisShouldApplyIntangible;
                    break;
                case RitualPower rp:
                    if (SimPowerInternalReader.ReadRitualWasJustAppliedByEnemy(rp)) dst.Flags |= SimPowerInternal.FlagRitualWasJustAppliedByEnemy;
                    break;
            }
        }
    }

    private static void CaptureIntent(Creature enemy, int idx, CombatNodeBlob dst)
    {
        var move = enemy.Monster?.NextMove;
        if (move == null || move.Intents.Count == 0)
        {
            dst.EnemyIntent[idx] = (byte)SimIntent.Unknown;
            return;
        }

        AbstractIntent first = move.Intents[0];
        switch (first)
        {
            case DeathBlowIntent dbi:
                dst.EnemyIntent[idx] = (byte)SimIntent.DeathBlow;
                dst.EnemyIntentDmg[idx] = AttackDamage(dbi);
                dst.EnemyIntentHits[idx] = AttackHits(dbi);
                break;
            case AttackIntent ai:
                dst.EnemyIntent[idx] = (byte)SimIntent.Attack;
                dst.EnemyIntentDmg[idx] = AttackDamage(ai);
                dst.EnemyIntentHits[idx] = AttackHits(ai);
                break;
            case BuffIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Buff;
                break;
            case CardDebuffIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.CardDebuff;
                break;
            case DebuffIntent dbi:
                dst.EnemyIntent[idx] = (byte)(dbi.IntentType == IntentType.DebuffStrong ? SimIntent.DebuffStrong : SimIntent.Debuff);
                break;
            case DefendIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Defend;
                break;
            case EscapeIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Escape;
                break;
            case HealIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Heal;
                break;
            case HiddenIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Hidden;
                break;
            case SleepIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Sleep;
                break;
            case StatusIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.StatusCard;
                break;
            case StunIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Stun;
                break;
            case SummonIntent:
                dst.EnemyIntent[idx] = (byte)SimIntent.Summon;
                break;
            default:
                dst.EnemyIntent[idx] = (byte)SimIntent.Unknown;
                break;
        }
    }

    private static void SnapshotMoveSM(Creature enemy, ref SimEnemyMoveSM dst, ref ushort tableHandle)
    {
        MonsterModel? monster = enemy.Monster;
        if (monster == null)
            return;

        MonsterStateTable? table = SimMonsterStateRegistry.GetOrBuild(monster);
        if (table == null)
            return;

        tableHandle = SimMonsterStateRegistry.GetHandle(table);

        MonsterMoveStateMachine sm = monster.MoveStateMachine!;
        dst = default;

        MonsterState current = SimMonsterStateRegistry.GetCurrentState(sm);
        if (!table.IdToIdx.TryGetValue(current.Id, out byte currentIdx))
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: monster '{monster.GetType().FullName}' current state id '{current.Id}' not present in cached MonsterStateTable.");
        }

        dst.CurrentStateIdx = currentIdx;
        if (SimMonsterStateRegistry.GetPerformedFirstMove(sm))
            dst.Flags |= SimEnemyMoveSM.FlagPerformedFirstMove;

        var log = sm.StateLog;
        int logCount = log.Count;
        uint bitset = 0u;
        for (int i = 0; i < logCount; i++)
        {
            if (table.IdToIdx.TryGetValue(log[i].Id, out byte stateIdx))
                bitset |= 1u << stateIdx;
        }
        dst.EverUsedBitset = bitset;

        int keep = logCount < SimEnemyMoveSM.HistoryCap ? logCount : SimEnemyMoveSM.HistoryCap;
        int start = logCount - keep;
        for (int i = 0; i < keep; i++)
        {
            if (table.IdToIdx.TryGetValue(log[start + i].Id, out byte stateIdx))
                dst.History[i] = stateIdx;
            else
                dst.History[i] = 0xFF;
        }
        dst.HistoryCount = (byte)keep;
        dst.HistoryHead = (byte)(keep == SimEnemyMoveSM.HistoryCap ? 0 : keep);

        dst.IllusionFollowUpIdx = SimEnemyMoveSM.NoFollowUp;
        var powers = enemy.Powers;
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            if (powers[i] is not IllusionPower illusion)
                continue;

            string? followUpId = illusion.FollowUpStateId;
            if (followUpId != null && table.IdToIdx.TryGetValue(followUpId, out byte followUpIdx))
            {
                dst.IllusionFollowUpIdx = followUpIdx;
                dst.Flags |= SimEnemyMoveSM.FlagHasIllusionFollowUp;
            }
            break;
        }
    }

    private static ushort AttackDamage(AttackIntent intent)
    {
        var calc = intent.DamageCalc;
        if (calc == null) return 0;
        decimal raw = calc();
        if (raw < 0m) return 0;
        if (raw > 65535m) return 65535;
        return (ushort)raw;
    }

    private static byte AttackHits(AttackIntent intent)
    {
        int hits = intent.Repeats + 1;
        if (hits < 1) hits = 1;
        if (hits > 255) hits = 255;
        return (byte)hits;
    }

    private static short ClampS16(int value)
        => value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : (short)value;

    private static ushort ClampU16(int value)
        => value < 0 ? (ushort)0 : value > 65535 ? (ushort)65535 : (ushort)value;

    private static byte ClampU8(int value)
        => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

    private static sbyte ClampS8(int value)
        => value < sbyte.MinValue ? sbyte.MinValue : value > sbyte.MaxValue ? sbyte.MaxValue : (sbyte)value;

    private static void SnapshotOrbs(OrbQueue queue, CombatNodeBlob dst)
    {
        var orbs = queue.Orbs;
        int count = orbs.Count;
        if (count > 10)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: orb queue has {count} orbs > maxCapacity 10. " +
                "OrbQueue.maxCapacity changed in game source — update OrbSlots10 accordingly.");
        }

        dst.OrbCount = (byte)count;
        dst.OrbCapacity = ClampU8(queue.Capacity);

        for (int i = 0; i < count; i++)
        {
            OrbModel orb = orbs[i];
            byte type = SimOrbRegistry.GetIndexOrNone(orb.GetType());
            int mut = orb switch
            {
                DarkOrb dark => SimOrbRegistry.ReadDarkEvokeVal(dark),
                GlassOrb glass => SimOrbRegistry.ReadGlassPassiveVal(glass),
                _ => 0,
            };
            dst.OrbSlots[i] = SimOrb.Pack(type, mut);
        }
    }

    private static void SnapshotOsty(PlayerCombatState pcs, CombatNodeBlob dst)
    {
        Creature? pet = pcs.GetPet<Osty>();
        if (pet == null)
            return;

        dst.Osty = new SimPet
        {
            CurrentHp = ClampU16(pet.CurrentHp),
            MaxHp = ClampU16(pet.MaxHp),
            Block = ClampU16(pet.Block),
            Exists = 1,
        };

        WritePowers(pet.Powers, dst.OstyPowers);
        SimPowerInternal ostyPowerInternal = default;
        WritePowerInternals(pet.Powers, ref ostyPowerInternal);
        dst.OstyPowerInternal = ostyPowerInternal;
    }
}