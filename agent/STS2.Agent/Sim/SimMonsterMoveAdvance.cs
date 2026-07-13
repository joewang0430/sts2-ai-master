using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace STS2.Agent.Sim;

/// <summary>
/// Hand-written per-monster replication of <c>MonsterMoveStateMachine.FindNextMoveState</c> — the
/// game itself has no generic "peek the next move without performing this one" path, so (same
/// situation as <see cref="SimMonsterMoveEffects"/>) each monster's specific state graph
/// (<c>MoveState.FollowUpState</c> chains, <c>ConditionalBranchState</c> conditions,
/// <c>RandomBranchState</c> weights) has to be hand-copied from that monster's own
/// <c>GenerateMoveStateMachine()</c> override.
///
/// This registry is INTENTIONALLY partial and grows incrementally, monster by monster, across many
/// future sessions — same rules as SimMonsterMoveEffects: an unregistered monster is not a bug,
/// <see cref="TryAdvance"/> just returns false; there is no SimCaps completeness check.
///
/// Building blocks already in place and reused here (see their own files for how each was verified):
///   RandomState / RandomStateOps — bit-exact Xoshiro256** replication of the game's per-combat RNG
///     streams (RandomBranchState rolls consume SimRngSlot.MonsterAi).
///   SimEnemyMoveSM — per-enemy History ring buffer + EverUsedBitset, sized exactly to answer every
///     MoveRepeatType/cooldown rule RandomBranchState.GetStateWeight checks.
///   MonsterStateTable — per-monster-Type StateIds/IdToIdx, built once and cached process-lifetime.
/// </summary>
internal static class SimMonsterMoveAdvance
{
    /// <summary>Computes the next MoveState's <c>MonsterStateTable</c> index and writes it into
    /// <paramref name="sm"/> (advancing History/EverUsedBitset/CurrentStateIdx along the way, one
    /// call per intermediate Conditional/Random branch node crossed — mirrors FindNextMoveState's
    /// do/while walking through non-move states until it lands on one). <paramref name="state"/> +
    /// <paramref name="enemyIdx"/> are here for the (rarer) monsters whose ConditionalBranchState
    /// condition depends on live tracked state — e.g. SlumberingBeetle checking its own SlumberPower
    /// — most Advance functions never touch them.</summary>
    public delegate void AdvanceFn(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags);

    private static readonly FrozenDictionary<Type, AdvanceFn> _byType = new Dictionary<Type, AdvanceFn>
    {
        { typeof(BowlbugNectar), AdvanceBowlbugNectar },
        { typeof(CalcifiedCultist), AdvanceCalcifiedCultist },
        { typeof(DampCultist), AdvanceDampCultist },
        { typeof(Guardbot), AdvanceGuardbot },
        { typeof(AxeRubyRaider), AdvanceAxeRubyRaider },
        { typeof(BruteRubyRaider), AdvanceBruteRubyRaider },
        { typeof(CrossbowRubyRaider), AdvanceCrossbowRubyRaider },
        { typeof(FossilStalker), AdvanceFossilStalker },
        { typeof(DevotedSculptor), AdvanceDevotedSculptor },
        { typeof(FuzzyWurmCrawler), AdvanceFuzzyWurmCrawler },
        { typeof(FlailKnight), AdvanceFlailKnight },
        { typeof(BowlbugSilk), AdvanceBowlbugSilk },
        { typeof(CorpseSlug), AdvanceCorpseSlug },
        { typeof(HunterKiller), AdvanceHunterKiller },
        { typeof(KinFollower), AdvanceKinFollower },
        { typeof(Mawler), AdvanceMawler },
        { typeof(Myte), AdvanceMyte },
        { typeof(ScrollOfBiting), AdvanceScrollOfBiting },
        { typeof(SewerClam), AdvanceSewerClam },
        { typeof(ShrinkerBeetle), AdvanceShrinkerBeetle },
        { typeof(SkulkingColony), AdvanceSkulkingColony },
        { typeof(SlumberingBeetle), AdvanceSlumberingBeetle },
        { typeof(SnappingJaxfruit), AdvanceSnappingJaxfruit },
        { typeof(SoulNexus), AdvanceSoulNexus },
        { typeof(SpectralKnight), AdvanceSpectralKnight },
        { typeof(SpinyToad), AdvanceSpinyToad },
        { typeof(Stabbot), AdvanceStabbot },
        { typeof(FrogKnight), AdvanceFrogKnight },
        { typeof(MechaKnight), AdvanceMechaKnight },
        { typeof(Nibbit), AdvanceNibbit },
        { typeof(PhantasmalGardener), AdvancePhantasmalGardener },
        { typeof(PunchConstruct), AdvancePunchConstruct },
        { typeof(BygoneEffigy), AdvanceBygoneEffigy },
        { typeof(BowlbugEgg), AdvanceBowlbugEgg },
        { typeof(Flyconid), AdvanceFlyconid },
        { typeof(LivingShield), AdvanceLivingShield },
        { typeof(Rocket), AdvanceRocket },
        { typeof(GlobeHead), AdvanceGlobeHead },
        { typeof(HauntedShip), AdvanceHauntedShip },
        { typeof(InfestedPrism), AdvanceInfestedPrism },
        { typeof(Seapunk), AdvanceSeapunk },
        { typeof(SlitheringStrangler), AdvanceSlitheringStrangler },
        { typeof(OwlMagistrate), AdvanceOwlMagistrate },
        { typeof(SludgeSpinner), AdvanceSludgeSpinner },
        { typeof(KinPriest), AdvanceKinPriest },
        { typeof(Crusher), AdvanceCrusher },
        { typeof(SoulFysh), AdvanceSoulFysh },
        { typeof(GremlinMerc), AdvanceGremlinMerc },
        { typeof(Aeonglass), AdvanceAeonglass },
        { typeof(TerrorEel), AdvanceTerrorEel },
        { typeof(SlimedBerserker), AdvanceSlimedBerserker },
        { typeof(Toadpole), AdvanceToadpole },
        { typeof(CubexConstruct), AdvanceCubexConstruct },
        { typeof(TrackerRubyRaider), AdvanceTrackerRubyRaider },
        { typeof(Tunneler), AdvanceTunneler },
        { typeof(MagiKnight), AdvanceMagiKnight },
        { typeof(Entomancer), AdvanceEntomancer },
        { typeof(TheInsatiable), AdvanceTheInsatiable },
        { typeof(Axebot), AdvanceAxebot },
        { typeof(ThievingHopper), AdvanceThievingHopper },
        { typeof(KnowledgeDemon), AdvanceKnowledgeDemon },
        { typeof(TurretOperator), AdvanceTurretOperator },
        { typeof(LouseProgenitor), AdvanceLouseProgenitor },
        { typeof(Wriggler), AdvanceWriggler },
        { typeof(TheObscura), AdvanceTheObscura },
        { typeof(CeremonialBeast), AdvanceCeremonialBeast },
        { typeof(TheForgotten), AdvanceTheForgotten },
        { typeof(LagavulinMatriarch), AdvanceLagavulinMatriarch },
        { typeof(Vantom), AdvanceVantom },
        { typeof(WaterfallGiant), AdvanceWaterfallGiant },
        { typeof(TheLost), AdvanceTheLost },
        { typeof(TheAdversaryMkOne), AdvanceTheAdversaryMkOne },
        { typeof(TheAdversaryMkTwo), AdvanceTheAdversaryMkTwo },
        { typeof(TheAdversaryMkThree), AdvanceTheAdversaryMkThree },
        { typeof(DecimillipedeSegmentBack), AdvanceDecimillipedeSegment },
        { typeof(DecimillipedeSegmentFront), AdvanceDecimillipedeSegment },
        { typeof(DecimillipedeSegmentMiddle), AdvanceDecimillipedeSegment },
        { typeof(Exoskeleton), AdvanceExoskeleton },
        { typeof(Ovicopter), AdvanceOvicopter },
        { typeof(FakeMerchantMonster), AdvanceFakeMerchantMonster },
        { typeof(Fogmog), AdvanceFogmog },
        { typeof(LivingFog), AdvanceLivingFog },
        { typeof(Queen), AdvanceQueen },
    }.ToFrozenDictionary();

    /// <summary>True (and <paramref name="sm"/> updated) if <paramref name="monsterType"/> has a
    /// registered advance function; false (no-op) if not registered yet.</summary>
    public static bool TryAdvance(Type monsterType, CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        if (!_byType.TryGetValue(monsterType, out AdvanceFn? fn)) return false;
        fn(state, enemyIdx, ref sm, table, ref rng, ascensionFlags);
        return true;
    }

    /// <summary>Records a transition into <paramref name="nextIdx"/>: pushes it onto the History
    /// ring buffer (capped at <see cref="SimEnemyMoveSM.HistoryCap"/>, oldest entry falls off), sets
    /// its EverUsedBitset bit, moves CurrentStateIdx, and marks FlagPerformedFirstMove (by the time
    /// any transition happens, the state machine's initial move has necessarily already been
    /// telegraphed at least once — matches <c>_performedFirstMove</c> only ever being read, never
    /// unset, in the real MonsterMoveStateMachine).</summary>
    private static void TransitionTo(ref SimEnemyMoveSM sm, byte nextIdx)
    {
        sm.History[sm.HistoryHead] = nextIdx;
        sm.HistoryHead = (byte)((sm.HistoryHead + 1) % SimEnemyMoveSM.HistoryCap);
        if (sm.HistoryCount < SimEnemyMoveSM.HistoryCap) sm.HistoryCount++;
        sm.EverUsedBitset |= 1u << nextIdx;
        sm.CurrentStateIdx = nextIdx;
        sm.Flags |= SimEnemyMoveSM.FlagPerformedFirstMove;
    }

    // ── BowlbugNectar: THRASH_MOVE → BUFF_MOVE → THRASH2_MOVE → THRASH2_MOVE (self-loop forever).
    //    Fixed MoveState.FollowUpState chain, no RandomBranchState/ConditionalBranchState anywhere
    //    in its GenerateMoveStateMachine() — no RNG consumed, no ascension/history gating. ─────────
    private static void AdvanceBowlbugNectar(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte thrash = table.IdToIdx["THRASH_MOVE"];
        byte buff = table.IdToIdx["BUFF_MOVE"];
        byte thrash2 = table.IdToIdx["THRASH2_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == thrash) next = buff;
        else if (sm.CurrentStateIdx == buff) next = thrash2;
        else if (sm.CurrentStateIdx == thrash2) next = thrash2;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceBowlbugNectar: current state idx {sm.CurrentStateIdx} isn't THRASH_MOVE/BUFF_MOVE/THRASH2_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── CalcifiedCultist / DampCultist: INCANTATION_MOVE → DARK_STRIKE_MOVE → DARK_STRIKE_MOVE
    //    (self-loop forever). Same state IDs, same shape, two different monster Types. ────────────
    private static void AdvanceCalcifiedCultist(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
        => AdvanceIncantationDarkStrikeShape(ref sm, table);

    private static void AdvanceDampCultist(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
        => AdvanceIncantationDarkStrikeShape(ref sm, table);

    private static void AdvanceIncantationDarkStrikeShape(ref SimEnemyMoveSM sm, MonsterStateTable table)
    {
        byte incantation = table.IdToIdx["INCANTATION_MOVE"];
        byte darkStrike = table.IdToIdx["DARK_STRIKE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == incantation) next = darkStrike;
        else if (sm.CurrentStateIdx == darkStrike) next = darkStrike;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceIncantationDarkStrikeShape: current state idx {sm.CurrentStateIdx} isn't INCANTATION_MOVE/DARK_STRIKE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Guardbot: GUARD_MOVE → GUARD_MOVE (self-loop, single state — always the same move). ──────
    private static void AdvanceGuardbot(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte guard = table.IdToIdx["GUARD_MOVE"];
        if (sm.CurrentStateIdx != guard)
        {
            throw new InvalidOperationException(
                $"AdvanceGuardbot: current state idx {sm.CurrentStateIdx} isn't GUARD_MOVE.");
        }
        TransitionTo(ref sm, guard);
    }

    // ── AxeRubyRaider: SWING_1 → SWING_2 → BIG_SWING → SWING_1 (3-cycle). ───────────────────────
    private static void AdvanceAxeRubyRaider(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte swing1 = table.IdToIdx["SWING_1"];
        byte swing2 = table.IdToIdx["SWING_2"];
        byte bigSwing = table.IdToIdx["BIG_SWING"];

        byte next;
        if (sm.CurrentStateIdx == swing1) next = swing2;
        else if (sm.CurrentStateIdx == swing2) next = bigSwing;
        else if (sm.CurrentStateIdx == bigSwing) next = swing1;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceAxeRubyRaider: current state idx {sm.CurrentStateIdx} isn't SWING_1/SWING_2/BIG_SWING.");
        }

        TransitionTo(ref sm, next);
    }

    // ── BruteRubyRaider: BEAT_MOVE → ROAR_MOVE → BEAT_MOVE (2-cycle). ───────────────────────────
    private static void AdvanceBruteRubyRaider(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte beat = table.IdToIdx["BEAT_MOVE"];
        byte roar = table.IdToIdx["ROAR_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == beat) next = roar;
        else if (sm.CurrentStateIdx == roar) next = beat;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceBruteRubyRaider: current state idx {sm.CurrentStateIdx} isn't BEAT_MOVE/ROAR_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── CrossbowRubyRaider: RELOAD_MOVE → FIRE_MOVE → RELOAD_MOVE (2-cycle, initial=RELOAD_MOVE). ─
    private static void AdvanceCrossbowRubyRaider(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte reload = table.IdToIdx["RELOAD_MOVE"];
        byte fire = table.IdToIdx["FIRE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == reload) next = fire;
        else if (sm.CurrentStateIdx == fire) next = reload;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceCrossbowRubyRaider: current state idx {sm.CurrentStateIdx} isn't RELOAD_MOVE/FIRE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── FossilStalker: LATCH_MOVE (initial) / TACKLE_MOVE / LASH_MOVE all → a RandomBranchState
    //    ("RAND") that rolls equally among all three (weight 1 each), each gated
    //    CanRepeatXTimes(maxTimes=2, cooldown=0) — a move drops out of the roll only once it was
    //    the last 2 consecutive picks in a row. First monster with real RNG in the mix: consumes
    //    SimRngSlot.MonsterAi via the caller-supplied rng. ─────────────────────────────────────────
    private static void AdvanceFossilStalker(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte tackle = table.IdToIdx["TACKLE_MOVE"];
        byte latch = table.IdToIdx["LATCH_MOVE"];
        byte lash = table.IdToIdx["LASH_MOVE"];

        Span<byte> candidates = stackalloc byte[3] { latch, tackle, lash };
        Span<float> weights = stackalloc float[3];
        for (int i = 0; i < candidates.Length; i++)
            weights[i] = BranchWeight(in sm, candidates[i], baseWeight: 1f, MoveRepeatKind.CanRepeatXTimes, maxTimes: 2);

        byte next = RollWeighted(candidates, weights, ref rng);
        TransitionTo(ref sm, next);
    }

    // ── DevotedSculptor: FORBIDDEN_INCANTATION_MOVE → SAVAGE_MOVE → SAVAGE_MOVE (self-loop). ────
    private static void AdvanceDevotedSculptor(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte incantation = table.IdToIdx["FORBIDDEN_INCANTATION_MOVE"];
        byte savage = table.IdToIdx["SAVAGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == incantation) next = savage;
        else if (sm.CurrentStateIdx == savage) next = savage;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceDevotedSculptor: current state idx {sm.CurrentStateIdx} isn't FORBIDDEN_INCANTATION_MOVE/SAVAGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── FuzzyWurmCrawler: FIRST_ACID_GOOP (initial) → INHALE → ACID_GOOP → FIRST_ACID_GOOP
    //    (3-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceFuzzyWurmCrawler(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte firstAcidGoop = table.IdToIdx["FIRST_ACID_GOOP"];
        byte inhale = table.IdToIdx["INHALE"];
        byte acidGoop = table.IdToIdx["ACID_GOOP"];

        byte next;
        if (sm.CurrentStateIdx == firstAcidGoop) next = inhale;
        else if (sm.CurrentStateIdx == inhale) next = acidGoop;
        else if (sm.CurrentStateIdx == acidGoop) next = firstAcidGoop;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceFuzzyWurmCrawler: current state idx {sm.CurrentStateIdx} isn't FIRST_ACID_GOOP/INHALE/ACID_GOOP.");
        }

        TransitionTo(ref sm, next);
    }

    // ── FlailKnight: WAR_CHANT / FLAIL_MOVE / RAM_MOVE (initial) all → "RAND", weight 1 each —
    //    WAR_CHANT is CannotRepeat, FLAIL_MOVE and RAM_MOVE are both CanRepeatXTimes(maxTimes=2). ──
    private static void AdvanceFlailKnight(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte warChant = table.IdToIdx["WAR_CHANT"];
        byte flail = table.IdToIdx["FLAIL_MOVE"];
        byte ram = table.IdToIdx["RAM_MOVE"];

        Span<byte> candidates = stackalloc byte[3] { warChant, flail, ram };
        Span<float> weights = stackalloc float[3];
        weights[0] = BranchWeight(in sm, warChant, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
        weights[1] = BranchWeight(in sm, flail, baseWeight: 1f, MoveRepeatKind.CanRepeatXTimes, maxTimes: 2);
        weights[2] = BranchWeight(in sm, ram, baseWeight: 1f, MoveRepeatKind.CanRepeatXTimes, maxTimes: 2);

        byte next = RollWeighted(candidates, weights, ref rng);
        TransitionTo(ref sm, next);
    }

    // ── BowlbugSilk: THRASH_MOVE ↔ TOXIC_SPIT_MOVE (2-cycle, initial=TOXIC_SPIT_MOVE — the
    //    initial-state pick itself doesn't affect this function, only which state the live snapshot
    //    reports as CurrentStateIdx to start from). ─────────────────────────────────────────────────
    private static void AdvanceBowlbugSilk(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte thrash = table.IdToIdx["THRASH_MOVE"];
        byte spit = table.IdToIdx["TOXIC_SPIT_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == thrash) next = spit;
        else if (sm.CurrentStateIdx == spit) next = thrash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceBowlbugSilk: current state idx {sm.CurrentStateIdx} isn't THRASH_MOVE/TOXIC_SPIT_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── CorpseSlug: WHIP_SLAP_MOVE → GLOMP_MOVE → GOOP_MOVE → WHIP_SLAP_MOVE (3-cycle). Initial
    //    state is StarterMoveIdx % 3 (randomized per-instance at encounter setup so multiple
    //    CorpseSlugs in the same fight don't sync up) — irrelevant here, only affects which state
    //    the live snapshot starts CurrentStateIdx at. ────────────────────────────────────────────────
    private static void AdvanceCorpseSlug(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte whipSlap = table.IdToIdx["WHIP_SLAP_MOVE"];
        byte glomp = table.IdToIdx["GLOMP_MOVE"];
        byte goop = table.IdToIdx["GOOP_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == whipSlap) next = glomp;
        else if (sm.CurrentStateIdx == glomp) next = goop;
        else if (sm.CurrentStateIdx == goop) next = whipSlap;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceCorpseSlug: current state idx {sm.CurrentStateIdx} isn't WHIP_SLAP_MOVE/GLOMP_MOVE/GOOP_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── HunterKiller: TENDERIZING_GOOP_MOVE (initial, never revisited — nothing's FollowUpState
    //    points back to it and it's not itself a RAND branch) / BITE_MOVE / PUNCTURE_MOVE all →
    //    "RAND", which only offers BITE_MOVE(CannotRepeat) / PUNCTURE_MOVE(CanRepeatXTimes=2) —
    //    so every advance call resolves the same two-way roll regardless of current state. ─────────
    private static void AdvanceHunterKiller(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte bite = table.IdToIdx["BITE_MOVE"];
        byte puncture = table.IdToIdx["PUNCTURE_MOVE"];

        Span<byte> candidates = stackalloc byte[2] { bite, puncture };
        Span<float> weights = stackalloc float[2];
        weights[0] = BranchWeight(in sm, bite, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
        weights[1] = BranchWeight(in sm, puncture, baseWeight: 1f, MoveRepeatKind.CanRepeatXTimes, maxTimes: 2);

        byte next = RollWeighted(candidates, weights, ref rng);
        TransitionTo(ref sm, next);
    }

    // ── KinFollower: QUICK_SLASH_MOVE → BOOMERANG_MOVE → POWER_DANCE_MOVE → QUICK_SLASH_MOVE
    //    (3-cycle). Initial state depends on StartsWithDance (external, set before combat) —
    //    irrelevant here, same as CorpseSlug's StarterMoveIdx. ──────────────────────────────────────
    private static void AdvanceKinFollower(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte quickSlash = table.IdToIdx["QUICK_SLASH_MOVE"];
        byte boomerang = table.IdToIdx["BOOMERANG_MOVE"];
        byte powerDance = table.IdToIdx["POWER_DANCE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == quickSlash) next = boomerang;
        else if (sm.CurrentStateIdx == boomerang) next = powerDance;
        else if (sm.CurrentStateIdx == powerDance) next = quickSlash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceKinFollower: current state idx {sm.CurrentStateIdx} isn't QUICK_SLASH_MOVE/BOOMERANG_MOVE/POWER_DANCE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Mawler: RIP_AND_TEAR_MOVE (initial) / ROAR_MOVE / CLAW_MOVE all → "RAND", weight 1 each —
    //    RIP_AND_TEAR_MOVE and CLAW_MOVE are CannotRepeat, ROAR_MOVE is UseOnlyOnce (fires at most
    //    once per combat — first monster using that repeat kind, exercises EverUsedBitset). ────────
    private static void AdvanceMawler(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte ripAndTear = table.IdToIdx["RIP_AND_TEAR_MOVE"];
        byte roar = table.IdToIdx["ROAR_MOVE"];
        byte claw = table.IdToIdx["CLAW_MOVE"];

        Span<byte> candidates = stackalloc byte[3] { ripAndTear, roar, claw };
        Span<float> weights = stackalloc float[3];
        weights[0] = BranchWeight(in sm, ripAndTear, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
        weights[1] = BranchWeight(in sm, roar, baseWeight: 1f, MoveRepeatKind.UseOnlyOnce);
        weights[2] = BranchWeight(in sm, claw, baseWeight: 1f, MoveRepeatKind.CannotRepeat);

        byte next = RollWeighted(candidates, weights, ref rng);
        TransitionTo(ref sm, next);
    }

    // ── Myte: TOXIC_MOVE → BITE_MOVE → SUCK_MOVE → TOXIC_MOVE (3-cycle). Initial state is a
    //    ConditionalBranchState gated on Creature.SlotName ("first"/"second") — same
    //    not-yet-tracked-data situation as Exoskeleton, but (like CorpseSlug/KinFollower's random/
    //    external initial pick) it only affects which state combat starts in, not this function. ──
    private static void AdvanceMyte(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte toxic = table.IdToIdx["TOXIC_MOVE"];
        byte bite = table.IdToIdx["BITE_MOVE"];
        byte suck = table.IdToIdx["SUCK_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == toxic) next = bite;
        else if (sm.CurrentStateIdx == bite) next = suck;
        else if (sm.CurrentStateIdx == suck) next = toxic;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceMyte: current state idx {sm.CurrentStateIdx} isn't TOXIC_MOVE/BITE_MOVE/SUCK_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── ScrollOfBiting: CHOMP → MORE_TEETH → CHEW → "rand"(CHOMP CannotRepeat / CHEW
    //    CanRepeatXTimes=2) → ... — CHOMP is only reachable via a fresh combat start or the RAND
    //    roll landing on it; CHEW can loop directly back into RAND without revisiting MORE_TEETH.
    //    Initial state is StarterMoveIdx%3 (external, irrelevant here per the CorpseSlug pattern). ──
    private static void AdvanceScrollOfBiting(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte chomp = table.IdToIdx["CHOMP"];
        byte moreTeeth = table.IdToIdx["MORE_TEETH"];
        byte chew = table.IdToIdx["CHEW"];

        if (sm.CurrentStateIdx == chomp)
        {
            TransitionTo(ref sm, moreTeeth);
            return;
        }
        if (sm.CurrentStateIdx == moreTeeth)
        {
            TransitionTo(ref sm, chew);
            return;
        }
        if (sm.CurrentStateIdx == chew)
        {
            Span<byte> candidates = stackalloc byte[2] { chomp, chew };
            Span<float> weights = stackalloc float[2];
            weights[0] = BranchWeight(in sm, chomp, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            weights[1] = BranchWeight(in sm, chew, baseWeight: 1f, MoveRepeatKind.CanRepeatXTimes, maxTimes: 2);
            TransitionTo(ref sm, RollWeighted(candidates, weights, ref rng));
            return;
        }

        throw new InvalidOperationException(
            $"AdvanceScrollOfBiting: current state idx {sm.CurrentStateIdx} isn't CHOMP/MORE_TEETH/CHEW.");
    }

    // ── SewerClam: PRESSURIZE_MOVE ↔ JET_MOVE (2-cycle, initial=JET_MOVE). ──────────────────────
    private static void AdvanceSewerClam(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte pressurize = table.IdToIdx["PRESSURIZE_MOVE"];
        byte jet = table.IdToIdx["JET_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == pressurize) next = jet;
        else if (sm.CurrentStateIdx == jet) next = pressurize;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSewerClam: current state idx {sm.CurrentStateIdx} isn't PRESSURIZE_MOVE/JET_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── ShrinkerBeetle: SHRINKER_MOVE (initial, never revisited) → CHOMP_MOVE ↔ STOMP_MOVE. ─────
    private static void AdvanceShrinkerBeetle(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte shrinker = table.IdToIdx["SHRINKER_MOVE"];
        byte chomp = table.IdToIdx["CHOMP_MOVE"];
        byte stomp = table.IdToIdx["STOMP_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == shrinker) next = chomp;
        else if (sm.CurrentStateIdx == chomp) next = stomp;
        else if (sm.CurrentStateIdx == stomp) next = chomp;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceShrinkerBeetle: current state idx {sm.CurrentStateIdx} isn't SHRINKER_MOVE/CHOMP_MOVE/STOMP_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── SkulkingColony: ZOOM_MOVE → ZOOM_MOVE_2 → INERTIA_MOVE → PIERCING_STABS_MOVE → ZOOM_MOVE
    //    (4-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceSkulkingColony(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte zoom = table.IdToIdx["ZOOM_MOVE"];
        byte zoom2 = table.IdToIdx["ZOOM_MOVE_2"];
        byte inertia = table.IdToIdx["INERTIA_MOVE"];
        byte piercingStabs = table.IdToIdx["PIERCING_STABS_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == zoom) next = zoom2;
        else if (sm.CurrentStateIdx == zoom2) next = inertia;
        else if (sm.CurrentStateIdx == inertia) next = piercingStabs;
        else if (sm.CurrentStateIdx == piercingStabs) next = zoom;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSkulkingColony: current state idx {sm.CurrentStateIdx} isn't ZOOM_MOVE/ZOOM_MOVE_2/INERTIA_MOVE/PIERCING_STABS_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── SlumberingBeetle: SNORE_MOVE → ConditionalBranchState("SNORE_NEXT") checking
    //    Creature.HasPower&lt;SlumberPower&gt;() — still has it → back to SNORE_MOVE; lost it →
    //    ROLL_OUT_MOVE (self-loop forever after). First monster whose branch condition needs live
    //    tracked state (SlumberPower is a normal power we already read via SimPowerOps), not just
    //    History — this is why AdvanceFn carries state/enemyIdx at all. ─────────────────────────────
    private static void AdvanceSlumberingBeetle(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte snore = table.IdToIdx["SNORE_MOVE"];
        byte rollOut = table.IdToIdx["ROLL_OUT_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == snore)
        {
            bool stillSlumbering = SimPowerOps.TryGetEnemyAmount(state, enemyIdx, SimPowerType.Slumber, out _);
            next = stillSlumbering ? snore : rollOut;
        }
        else if (sm.CurrentStateIdx == rollOut) next = rollOut;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSlumberingBeetle: current state idx {sm.CurrentStateIdx} isn't SNORE_MOVE/ROLL_OUT_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── SnappingJaxfruit: ENERGY_ORB_MOVE → ENERGY_ORB_MOVE (self-loop, single state). ──────────
    private static void AdvanceSnappingJaxfruit(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte energyOrb = table.IdToIdx["ENERGY_ORB_MOVE"];
        if (sm.CurrentStateIdx != energyOrb)
        {
            throw new InvalidOperationException(
                $"AdvanceSnappingJaxfruit: current state idx {sm.CurrentStateIdx} isn't ENERGY_ORB_MOVE.");
        }
        TransitionTo(ref sm, energyOrb);
    }

    // ── SoulNexus: SOUL_BURN_MOVE (initial) / MAELSTROM_MOVE / DRAIN_LIFE_MOVE all → "RAND",
    //    weight 1 each, all three CannotRepeat. ──────────────────────────────────────────────────
    private static void AdvanceSoulNexus(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte soulBurn = table.IdToIdx["SOUL_BURN_MOVE"];
        byte maelstrom = table.IdToIdx["MAELSTROM_MOVE"];
        byte drainLife = table.IdToIdx["DRAIN_LIFE_MOVE"];

        Span<byte> candidates = stackalloc byte[3] { soulBurn, maelstrom, drainLife };
        Span<float> weights = stackalloc float[3];
        weights[0] = BranchWeight(in sm, soulBurn, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
        weights[1] = BranchWeight(in sm, maelstrom, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
        weights[2] = BranchWeight(in sm, drainLife, baseWeight: 1f, MoveRepeatKind.CannotRepeat);

        byte next = RollWeighted(candidates, weights, ref rng);
        TransitionTo(ref sm, next);
    }

    // ── SpectralKnight: HEX (initial) → SOUL_SLASH → "RAND"(SOUL_SLASH CanRepeatXTimes=2 /
    //    SOUL_FLAME CannotRepeat) → ... — SOUL_FLAME loops directly back into RAND without
    //    revisiting HEX/SOUL_SLASH, same shape as ScrollOfBiting's CHEW. ────────────────────────────
    private static void AdvanceSpectralKnight(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte hex = table.IdToIdx["HEX"];
        byte soulSlash = table.IdToIdx["SOUL_SLASH"];
        byte soulFlame = table.IdToIdx["SOUL_FLAME"];

        if (sm.CurrentStateIdx == hex)
        {
            TransitionTo(ref sm, soulSlash);
            return;
        }
        if (sm.CurrentStateIdx == soulSlash || sm.CurrentStateIdx == soulFlame)
        {
            Span<byte> candidates = stackalloc byte[2] { soulSlash, soulFlame };
            Span<float> weights = stackalloc float[2];
            weights[0] = BranchWeight(in sm, soulSlash, baseWeight: 1f, MoveRepeatKind.CanRepeatXTimes, maxTimes: 2);
            weights[1] = BranchWeight(in sm, soulFlame, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            TransitionTo(ref sm, RollWeighted(candidates, weights, ref rng));
            return;
        }

        throw new InvalidOperationException(
            $"AdvanceSpectralKnight: current state idx {sm.CurrentStateIdx} isn't HEX/SOUL_SLASH/SOUL_FLAME.");
    }

    // ── SpinyToad: PROTRUDING_SPIKES_MOVE → SPIKE_EXPLOSION_MOVE → TONGUE_LASH_MOVE →
    //    PROTRUDING_SPIKES_MOVE (3-cycle, fully deterministic). ──────────────────────────────────
    private static void AdvanceSpinyToad(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte spikes = table.IdToIdx["PROTRUDING_SPIKES_MOVE"];
        byte explosion = table.IdToIdx["SPIKE_EXPLOSION_MOVE"];
        byte lash = table.IdToIdx["TONGUE_LASH_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == spikes) next = explosion;
        else if (sm.CurrentStateIdx == explosion) next = lash;
        else if (sm.CurrentStateIdx == lash) next = spikes;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSpinyToad: current state idx {sm.CurrentStateIdx} isn't PROTRUDING_SPIKES_MOVE/SPIKE_EXPLOSION_MOVE/TONGUE_LASH_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Stabbot: STAB_MOVE → STAB_MOVE (self-loop, single state). ───────────────────────────────
    private static void AdvanceStabbot(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte stab = table.IdToIdx["STAB_MOVE"];
        if (sm.CurrentStateIdx != stab)
        {
            throw new InvalidOperationException(
                $"AdvanceStabbot: current state idx {sm.CurrentStateIdx} isn't STAB_MOVE.");
        }
        TransitionTo(ref sm, stab);
    }

    // ── FrogKnight: TONGUE_LASH (initial) → STRIKE_DOWN_EVIL → FOR_THE_QUEEN → ConditionalBranchState
    //    ("HALF_HEALTH") checking `HasBeetleCharged || CurrentHp >= MaxHp/2` → TONGUE_LASH, else
    //    (never charged AND below half HP) → BEETLE_CHARGE → TONGUE_LASH. HasBeetleCharged is a
    //    private per-instance bool that only ever flips true once BEETLE_CHARGE performs — which is
    //    exactly what EverUsedBitset already answers, no new tracked state needed. First monster
    //    whose branch condition reads live HP (state.EnemyHp/EnemyMaxHp). ─────────────────────────────
    private static void AdvanceFrogKnight(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte tongueLash = table.IdToIdx["TONGUE_LASH"];
        byte strikeDownEvil = table.IdToIdx["STRIKE_DOWN_EVIL"];
        byte forTheQueen = table.IdToIdx["FOR_THE_QUEEN"];
        byte beetleCharge = table.IdToIdx["BEETLE_CHARGE"];

        byte next;
        if (sm.CurrentStateIdx == tongueLash) next = strikeDownEvil;
        else if (sm.CurrentStateIdx == strikeDownEvil) next = forTheQueen;
        else if (sm.CurrentStateIdx == beetleCharge) next = tongueLash;
        else if (sm.CurrentStateIdx == forTheQueen)
        {
            bool hasBeetleCharged = sm.EverUsed(beetleCharge);
            bool halfHpOrAbove = state.EnemyHp[enemyIdx] >= state.EnemyMaxHp[enemyIdx] / 2;
            next = (hasBeetleCharged || halfHpOrAbove) ? tongueLash : beetleCharge;
        }
        else
        {
            throw new InvalidOperationException(
                $"AdvanceFrogKnight: current state idx {sm.CurrentStateIdx} isn't TONGUE_LASH/STRIKE_DOWN_EVIL/FOR_THE_QUEEN/BEETLE_CHARGE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── MechaKnight: CHARGE_MOVE (initial, never revisited) → FLAMETHROWER_MOVE → WINDUP_MOVE →
    //    HEAVY_CLEAVE_MOVE → FLAMETHROWER_MOVE (3-cycle after the opening charge). ─────────────────
    private static void AdvanceMechaKnight(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte charge = table.IdToIdx["CHARGE_MOVE"];
        byte flamethrower = table.IdToIdx["FLAMETHROWER_MOVE"];
        byte windup = table.IdToIdx["WINDUP_MOVE"];
        byte heavyCleave = table.IdToIdx["HEAVY_CLEAVE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == charge) next = flamethrower;
        else if (sm.CurrentStateIdx == flamethrower) next = windup;
        else if (sm.CurrentStateIdx == windup) next = heavyCleave;
        else if (sm.CurrentStateIdx == heavyCleave) next = flamethrower;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceMechaKnight: current state idx {sm.CurrentStateIdx} isn't CHARGE_MOVE/FLAMETHROWER_MOVE/WINDUP_MOVE/HEAVY_CLEAVE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Nibbit: BUTT_MOVE → SLICE_MOVE → HISS_MOVE → BUTT_MOVE (3-cycle). Initial state depends on
    //    IsAlone/IsFront (external, position-in-fight flags) — irrelevant here, same pattern as
    //    Exoskeleton/Myte/PhantasmalGardener's SlotName-gated initial pick. ───────────────────────────
    private static void AdvanceNibbit(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte butt = table.IdToIdx["BUTT_MOVE"];
        byte slice = table.IdToIdx["SLICE_MOVE"];
        byte hiss = table.IdToIdx["HISS_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == butt) next = slice;
        else if (sm.CurrentStateIdx == slice) next = hiss;
        else if (sm.CurrentStateIdx == hiss) next = butt;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceNibbit: current state idx {sm.CurrentStateIdx} isn't BUTT_MOVE/SLICE_MOVE/HISS_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── PhantasmalGardener: BITE_MOVE → LASH_MOVE → FLAIL_MOVE → ENLARGE_MOVE → BITE_MOVE
    //    (4-cycle). Initial state is SlotName-gated (four-way, like Exoskeleton) — irrelevant here. ──
    private static void AdvancePhantasmalGardener(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte bite = table.IdToIdx["BITE_MOVE"];
        byte lash = table.IdToIdx["LASH_MOVE"];
        byte flail = table.IdToIdx["FLAIL_MOVE"];
        byte enlarge = table.IdToIdx["ENLARGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == bite) next = lash;
        else if (sm.CurrentStateIdx == lash) next = flail;
        else if (sm.CurrentStateIdx == flail) next = enlarge;
        else if (sm.CurrentStateIdx == enlarge) next = bite;
        else
        {
            throw new InvalidOperationException(
                $"AdvancePhantasmalGardener: current state idx {sm.CurrentStateIdx} isn't BITE_MOVE/LASH_MOVE/FLAIL_MOVE/ENLARGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── PunchConstruct: READY_MOVE (or FAST_PUNCH_MOVE if StartsWithFastPunch, external/initial
    //    only) → FAST_PUNCH_MOVE → STRONG_PUNCH_MOVE → READY_MOVE (3-cycle). ───────────────────────
    private static void AdvancePunchConstruct(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte ready = table.IdToIdx["READY_MOVE"];
        byte fastPunch = table.IdToIdx["FAST_PUNCH_MOVE"];
        byte strongPunch = table.IdToIdx["STRONG_PUNCH_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == ready) next = fastPunch;
        else if (sm.CurrentStateIdx == fastPunch) next = strongPunch;
        else if (sm.CurrentStateIdx == strongPunch) next = ready;
        else
        {
            throw new InvalidOperationException(
                $"AdvancePunchConstruct: current state idx {sm.CurrentStateIdx} isn't READY_MOVE/FAST_PUNCH_MOVE/STRONG_PUNCH_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── BygoneEffigy: SLEEP_MOVE (initial) → WAKE_MOVE → SLASHES_MOVE → SLASHES_MOVE (self-loop).
    //    SLEEP_MOVE_2 is registered in the state list but no FollowUpState edge ever transitions INTO
    //    it from this graph — included anyway (→ SLASHES_MOVE, matching its own FollowUpState) in
    //    case something outside GenerateMoveStateMachine ever sets CurrentStateIdx there directly. ──
    private static void AdvanceBygoneEffigy(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte sleep = table.IdToIdx["SLEEP_MOVE"];
        byte wake = table.IdToIdx["WAKE_MOVE"];
        byte sleep2 = table.IdToIdx["SLEEP_MOVE_2"];
        byte slashes = table.IdToIdx["SLASHES_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == sleep) next = wake;
        else if (sm.CurrentStateIdx == wake) next = slashes;
        else if (sm.CurrentStateIdx == sleep2) next = slashes;
        else if (sm.CurrentStateIdx == slashes) next = slashes;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceBygoneEffigy: current state idx {sm.CurrentStateIdx} isn't SLEEP_MOVE/WAKE_MOVE/SLEEP_MOVE_2/SLASHES_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── BowlbugEgg: BITE_MOVE, single state, self-loop. ─────────────────────────────────────────
    private static void AdvanceBowlbugEgg(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte bite = table.IdToIdx["BITE_MOVE"];

        if (sm.CurrentStateIdx != bite)
        {
            throw new InvalidOperationException(
                $"AdvanceBowlbugEgg: current state idx {sm.CurrentStateIdx} isn't BITE_MOVE.");
        }

        TransitionTo(ref sm, bite);
    }

    // ── Flyconid: initial roll ("INITIAL" branch, FRAIL_SPORES_MOVE/SMASH_MOVE only) never shows up
    //    here — the live snapshot's CurrentStateIdx always already resolved past branch nodes to a
    //    real MoveState by the time we read it. All three real moves point to the same "RAND" branch:
    //    VULNERABLE_SPORES_MOVE (weight 3), FRAIL_SPORES_MOVE (weight 2), SMASH_MOVE (weight 1),
    //    all three CannotRepeat. ───────────────────────────────────────────────────────────────────
    private static void AdvanceFlyconid(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte vulnerableSpores = table.IdToIdx["VULNERABLE_SPORES_MOVE"];
        byte frailSpores = table.IdToIdx["FRAIL_SPORES_MOVE"];
        byte smash = table.IdToIdx["SMASH_MOVE"];

        Span<byte> candidates = stackalloc byte[3] { vulnerableSpores, frailSpores, smash };
        Span<float> weights = stackalloc float[3];
        weights[0] = BranchWeight(in sm, vulnerableSpores, baseWeight: 3f, MoveRepeatKind.CannotRepeat);
        weights[1] = BranchWeight(in sm, frailSpores, baseWeight: 2f, MoveRepeatKind.CannotRepeat);
        weights[2] = BranchWeight(in sm, smash, baseWeight: 1f, MoveRepeatKind.CannotRepeat);

        byte next = RollWeighted(candidates, weights, ref rng);
        TransitionTo(ref sm, next);
    }

    // ── LivingShield: SHIELD_SLAM_MOVE (initial) → ConditionalBranchState "SHIELD_SLAM_BRANCH":
    //    ally count > 0 → SHIELD_SLAM_MOVE again, else → SMASH_MOVE (which then self-loops forever —
    //    no edge ever leads back out of it). First monster whose branch condition needs a LIVING
    //    ALLY COUNT rather than own HP/powers — counts other same-side enemies with Hp > 0. ────────
    private static void AdvanceLivingShield(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte shieldSlam = table.IdToIdx["SHIELD_SLAM_MOVE"];
        byte smash = table.IdToIdx["SMASH_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == shieldSlam)
        {
            int allyCount = 0;
            for (int i = 0; i < state.EnemyCount; i++)
            {
                if (i != enemyIdx && state.EnemyHp[i] > 0) allyCount++;
            }
            next = allyCount > 0 ? shieldSlam : smash;
        }
        else if (sm.CurrentStateIdx == smash) next = smash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceLivingShield: current state idx {sm.CurrentStateIdx} isn't SHIELD_SLAM_MOVE/SMASH_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Rocket: TARGETING_RETICLE_MOVE → PRECISION_BEAM_MOVE → CHARGE_UP_MOVE → LASER_MOVE →
    //    RECHARGE_MOVE → TARGETING_RETICLE_MOVE (5-cycle, fully deterministic). ─────────────────────
    private static void AdvanceRocket(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte reticle = table.IdToIdx["TARGETING_RETICLE_MOVE"];
        byte precisionBeam = table.IdToIdx["PRECISION_BEAM_MOVE"];
        byte chargeUp = table.IdToIdx["CHARGE_UP_MOVE"];
        byte laser = table.IdToIdx["LASER_MOVE"];
        byte recharge = table.IdToIdx["RECHARGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == reticle) next = precisionBeam;
        else if (sm.CurrentStateIdx == precisionBeam) next = chargeUp;
        else if (sm.CurrentStateIdx == chargeUp) next = laser;
        else if (sm.CurrentStateIdx == laser) next = recharge;
        else if (sm.CurrentStateIdx == recharge) next = reticle;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceRocket: current state idx {sm.CurrentStateIdx} isn't TARGETING_RETICLE_MOVE/PRECISION_BEAM_MOVE/CHARGE_UP_MOVE/LASER_MOVE/RECHARGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── GlobeHead: SHOCKING_SLAP (initial) → THUNDER_STRIKE → GALVANIC_BURST → SHOCKING_SLAP
    //    (3-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceGlobeHead(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte shockingSlap = table.IdToIdx["SHOCKING_SLAP"];
        byte thunderStrike = table.IdToIdx["THUNDER_STRIKE"];
        byte galvanicBurst = table.IdToIdx["GALVANIC_BURST"];

        byte next;
        if (sm.CurrentStateIdx == shockingSlap) next = thunderStrike;
        else if (sm.CurrentStateIdx == thunderStrike) next = galvanicBurst;
        else if (sm.CurrentStateIdx == galvanicBurst) next = shockingSlap;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceGlobeHead: current state idx {sm.CurrentStateIdx} isn't SHOCKING_SLAP/THUNDER_STRIKE/GALVANIC_BURST.");
        }

        TransitionTo(ref sm, next);
    }

    // ── HauntedShip: HAUNT_MOVE (initial, only reachable once — nothing transitions back into it) →
    //    SWIPE_MOVE → STOMP_MOVE → SWIPE_MOVE (2-cycle thereafter). ─────────────────────────────────
    private static void AdvanceHauntedShip(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte haunt = table.IdToIdx["HAUNT_MOVE"];
        byte swipe = table.IdToIdx["SWIPE_MOVE"];
        byte stomp = table.IdToIdx["STOMP_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == haunt) next = swipe;
        else if (sm.CurrentStateIdx == swipe) next = stomp;
        else if (sm.CurrentStateIdx == stomp) next = swipe;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceHauntedShip: current state idx {sm.CurrentStateIdx} isn't HAUNT_MOVE/SWIPE_MOVE/STOMP_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── InfestedPrism: JAB_MOVE → RADIATE_MOVE → WHIRLWIND_MOVE → PULSATE_MOVE → JAB_MOVE
    //    (4-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceInfestedPrism(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte jab = table.IdToIdx["JAB_MOVE"];
        byte radiate = table.IdToIdx["RADIATE_MOVE"];
        byte whirlwind = table.IdToIdx["WHIRLWIND_MOVE"];
        byte pulsate = table.IdToIdx["PULSATE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == jab) next = radiate;
        else if (sm.CurrentStateIdx == radiate) next = whirlwind;
        else if (sm.CurrentStateIdx == whirlwind) next = pulsate;
        else if (sm.CurrentStateIdx == pulsate) next = jab;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceInfestedPrism: current state idx {sm.CurrentStateIdx} isn't JAB_MOVE/RADIATE_MOVE/WHIRLWIND_MOVE/PULSATE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Seapunk: SEA_KICK_MOVE → SPINNING_KICK_MOVE → BUBBLE_BURP_MOVE → SEA_KICK_MOVE
    //    (3-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceSeapunk(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte seaKick = table.IdToIdx["SEA_KICK_MOVE"];
        byte spinningKick = table.IdToIdx["SPINNING_KICK_MOVE"];
        byte bubbleBurp = table.IdToIdx["BUBBLE_BURP_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == seaKick) next = spinningKick;
        else if (sm.CurrentStateIdx == spinningKick) next = bubbleBurp;
        else if (sm.CurrentStateIdx == bubbleBurp) next = seaKick;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSeapunk: current state idx {sm.CurrentStateIdx} isn't SEA_KICK_MOVE/SPINNING_KICK_MOVE/BUBBLE_BURP_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── SlitheringStrangler: CONSTRICT (initial) → "rand": THWACK / LASH, equal weight 1 each,
    //    both CanRepeatForever (no repeat restriction at all — weight never zeroed) → both feed back
    //    into CONSTRICT. So the real cadence is CONSTRICT, then an unrestricted coin flip, forever. ──
    private static void AdvanceSlitheringStrangler(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte constrict = table.IdToIdx["CONSTRICT"];
        byte thwack = table.IdToIdx["THWACK"];
        byte lash = table.IdToIdx["LASH"];

        byte next;
        if (sm.CurrentStateIdx == constrict)
        {
            Span<byte> candidates = stackalloc byte[2] { thwack, lash };
            Span<float> weights = stackalloc float[2];
            weights[0] = BranchWeight(in sm, thwack, baseWeight: 1f, MoveRepeatKind.CanRepeatForever);
            weights[1] = BranchWeight(in sm, lash, baseWeight: 1f, MoveRepeatKind.CanRepeatForever);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else if (sm.CurrentStateIdx == thwack || sm.CurrentStateIdx == lash) next = constrict;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSlitheringStrangler: current state idx {sm.CurrentStateIdx} isn't CONSTRICT/THWACK/LASH.");
        }

        TransitionTo(ref sm, next);
    }

    // ── OwlMagistrate: MAGISTRATE_SCRUTINY → PECK_ASSAULT → JUDICIAL_FLIGHT → VERDICT →
    //    MAGISTRATE_SCRUTINY (4-cycle, fully deterministic). ─────────────────────────────────────────
    private static void AdvanceOwlMagistrate(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte scrutiny = table.IdToIdx["MAGISTRATE_SCRUTINY"];
        byte peckAssault = table.IdToIdx["PECK_ASSAULT"];
        byte judicialFlight = table.IdToIdx["JUDICIAL_FLIGHT"];
        byte verdict = table.IdToIdx["VERDICT"];

        byte next;
        if (sm.CurrentStateIdx == scrutiny) next = peckAssault;
        else if (sm.CurrentStateIdx == peckAssault) next = judicialFlight;
        else if (sm.CurrentStateIdx == judicialFlight) next = verdict;
        else if (sm.CurrentStateIdx == verdict) next = scrutiny;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceOwlMagistrate: current state idx {sm.CurrentStateIdx} isn't MAGISTRATE_SCRUTINY/PECK_ASSAULT/JUDICIAL_FLIGHT/VERDICT.");
        }

        TransitionTo(ref sm, next);
    }

    // ── SludgeSpinner: OIL_SPRAY_MOVE (initial) all three moves → "RAND", equal weight 1 each,
    //    all CannotRepeat. ────────────────────────────────────────────────────────────────────────
    private static void AdvanceSludgeSpinner(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte oilSpray = table.IdToIdx["OIL_SPRAY_MOVE"];
        byte slam = table.IdToIdx["SLAM_MOVE"];
        byte rage = table.IdToIdx["RAGE_MOVE"];

        Span<byte> candidates = stackalloc byte[3] { oilSpray, slam, rage };
        Span<float> weights = stackalloc float[3];
        for (int i = 0; i < candidates.Length; i++)
            weights[i] = BranchWeight(in sm, candidates[i], baseWeight: 1f, MoveRepeatKind.CannotRepeat);

        byte next = RollWeighted(candidates, weights, ref rng);
        TransitionTo(ref sm, next);
    }

    // ── KinPriest: ORB_OF_FRAILTY_MOVE → ORB_OF_WEAKNESS_MOVE → BEAM_MOVE → RITUAL_MOVE →
    //    ORB_OF_FRAILTY_MOVE (4-cycle, fully deterministic). ─────────────────────────────────────────
    private static void AdvanceKinPriest(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte frailty = table.IdToIdx["ORB_OF_FRAILTY_MOVE"];
        byte weakness = table.IdToIdx["ORB_OF_WEAKNESS_MOVE"];
        byte beam = table.IdToIdx["BEAM_MOVE"];
        byte ritual = table.IdToIdx["RITUAL_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == frailty) next = weakness;
        else if (sm.CurrentStateIdx == weakness) next = beam;
        else if (sm.CurrentStateIdx == beam) next = ritual;
        else if (sm.CurrentStateIdx == ritual) next = frailty;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceKinPriest: current state idx {sm.CurrentStateIdx} isn't ORB_OF_FRAILTY_MOVE/ORB_OF_WEAKNESS_MOVE/BEAM_MOVE/RITUAL_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Crusher: THRASH_MOVE → ENLARGING_STRIKE_MOVE → BUG_STING_MOVE → ADAPT_MOVE →
    //    GUARDED_STRIKE_MOVE → THRASH_MOVE (5-cycle, fully deterministic). ─────────────────────────
    private static void AdvanceCrusher(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte thrash = table.IdToIdx["THRASH_MOVE"];
        byte enlargingStrike = table.IdToIdx["ENLARGING_STRIKE_MOVE"];
        byte bugSting = table.IdToIdx["BUG_STING_MOVE"];
        byte adapt = table.IdToIdx["ADAPT_MOVE"];
        byte guardedStrike = table.IdToIdx["GUARDED_STRIKE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == thrash) next = enlargingStrike;
        else if (sm.CurrentStateIdx == enlargingStrike) next = bugSting;
        else if (sm.CurrentStateIdx == bugSting) next = adapt;
        else if (sm.CurrentStateIdx == adapt) next = guardedStrike;
        else if (sm.CurrentStateIdx == guardedStrike) next = thrash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceCrusher: current state idx {sm.CurrentStateIdx} isn't THRASH_MOVE/ENLARGING_STRIKE_MOVE/BUG_STING_MOVE/ADAPT_MOVE/GUARDED_STRIKE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── SoulFysh: BECKON_MOVE → DE_GAS_MOVE → GAZE_MOVE → FADE_MOVE → SCREAM_MOVE → BECKON_MOVE
    //    (5-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceSoulFysh(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte beckon = table.IdToIdx["BECKON_MOVE"];
        byte deGas = table.IdToIdx["DE_GAS_MOVE"];
        byte gaze = table.IdToIdx["GAZE_MOVE"];
        byte fade = table.IdToIdx["FADE_MOVE"];
        byte scream = table.IdToIdx["SCREAM_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == beckon) next = deGas;
        else if (sm.CurrentStateIdx == deGas) next = gaze;
        else if (sm.CurrentStateIdx == gaze) next = fade;
        else if (sm.CurrentStateIdx == fade) next = scream;
        else if (sm.CurrentStateIdx == scream) next = beckon;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSoulFysh: current state idx {sm.CurrentStateIdx} isn't BECKON_MOVE/DE_GAS_MOVE/GAZE_MOVE/FADE_MOVE/SCREAM_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── GremlinMerc: GIMME_MOVE → DOUBLE_SMASH_MOVE → HEHE_MOVE → GIMME_MOVE (3-cycle, fully
    //    deterministic). ──────────────────────────────────────────────────────────────────────────
    private static void AdvanceGremlinMerc(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte gimme = table.IdToIdx["GIMME_MOVE"];
        byte doubleSmash = table.IdToIdx["DOUBLE_SMASH_MOVE"];
        byte hehe = table.IdToIdx["HEHE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == gimme) next = doubleSmash;
        else if (sm.CurrentStateIdx == doubleSmash) next = hehe;
        else if (sm.CurrentStateIdx == hehe) next = gimme;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceGremlinMerc: current state idx {sm.CurrentStateIdx} isn't GIMME_MOVE/DOUBLE_SMASH_MOVE/HEHE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Aeonglass: EBB_MOVE → EYE_LASERS_MOVE → INCREASING_INTENSITY_MOVE → EBB_MOVE
    //    (3-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceAeonglass(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte ebb = table.IdToIdx["EBB_MOVE"];
        byte eyeLasers = table.IdToIdx["EYE_LASERS_MOVE"];
        byte increasingIntensity = table.IdToIdx["INCREASING_INTENSITY_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == ebb) next = eyeLasers;
        else if (sm.CurrentStateIdx == eyeLasers) next = increasingIntensity;
        else if (sm.CurrentStateIdx == increasingIntensity) next = ebb;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceAeonglass: current state idx {sm.CurrentStateIdx} isn't EBB_MOVE/EYE_LASERS_MOVE/INCREASING_INTENSITY_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TerrorEel: CRASH_MOVE ↔ THRASH_MOVE (2-cycle) is the normal cadence. STUN_MOVE/TERROR_MOVE
    //    are never reached via any FollowUpState edge in this graph — ShriekPower forces the state
    //    machine there directly once enough damage has been absorbed (outside GenerateMoveStateMachine).
    //    Handled anyway (STUN_MOVE → TERROR_MOVE → CRASH_MOVE) in case the live snapshot ever reports
    //    CurrentStateIdx sitting there. ─────────────────────────────────────────────────────────────
    private static void AdvanceTerrorEel(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte crash = table.IdToIdx["CRASH_MOVE"];
        byte thrash = table.IdToIdx["THRASH_MOVE"];
        byte stun = table.IdToIdx["STUN_MOVE"];
        byte terror = table.IdToIdx["TERROR_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == crash) next = thrash;
        else if (sm.CurrentStateIdx == thrash) next = crash;
        else if (sm.CurrentStateIdx == stun) next = terror;
        else if (sm.CurrentStateIdx == terror) next = crash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTerrorEel: current state idx {sm.CurrentStateIdx} isn't CRASH_MOVE/THRASH_MOVE/STUN_MOVE/TERROR_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── SlimedBerserker: VOMIT_ICHOR_MOVE → FURIOUS_PUMMELING_MOVE → LEECHING_HUG_MOVE →
    //    SMOTHER_MOVE → VOMIT_ICHOR_MOVE (4-cycle, fully deterministic). ────────────────────────────
    private static void AdvanceSlimedBerserker(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte vomitIchor = table.IdToIdx["VOMIT_ICHOR_MOVE"];
        byte furiousPummeling = table.IdToIdx["FURIOUS_PUMMELING_MOVE"];
        byte leechingHug = table.IdToIdx["LEECHING_HUG_MOVE"];
        byte smother = table.IdToIdx["SMOTHER_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == vomitIchor) next = furiousPummeling;
        else if (sm.CurrentStateIdx == furiousPummeling) next = leechingHug;
        else if (sm.CurrentStateIdx == leechingHug) next = smother;
        else if (sm.CurrentStateIdx == smother) next = vomitIchor;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceSlimedBerserker: current state idx {sm.CurrentStateIdx} isn't VOMIT_ICHOR_MOVE/FURIOUS_PUMMELING_MOVE/LEECHING_HUG_MOVE/SMOTHER_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Toadpole: "INIT_MOVE" ConditionalBranchState only picks the opening move (IsFront gated) —
    //    irrelevant here. Ongoing cycle: WHIRL_MOVE → SPIKEN_MOVE → SPIKE_SPIT_MOVE → WHIRL_MOVE
    //    (3-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceToadpole(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte whirl = table.IdToIdx["WHIRL_MOVE"];
        byte spiken = table.IdToIdx["SPIKEN_MOVE"];
        byte spikeSpit = table.IdToIdx["SPIKE_SPIT_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == whirl) next = spiken;
        else if (sm.CurrentStateIdx == spiken) next = spikeSpit;
        else if (sm.CurrentStateIdx == spikeSpit) next = whirl;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceToadpole: current state idx {sm.CurrentStateIdx} isn't WHIRL_MOVE/SPIKEN_MOVE/SPIKE_SPIT_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── CubexConstruct: CHARGE_UP_MOVE (initial, only reachable once) → REPEATER_BLAST_MOVE →
    //    REPEATER_BLAST_MOVE_2 → EXPEL_MOVE → REPEATER_BLAST_MOVE (3-cycle thereafter). ─────────────
    private static void AdvanceCubexConstruct(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte chargeUp = table.IdToIdx["CHARGE_UP_MOVE"];
        byte blast1 = table.IdToIdx["REPEATER_BLAST_MOVE"];
        byte blast2 = table.IdToIdx["REPEATER_BLAST_MOVE_2"];
        byte expel = table.IdToIdx["EXPEL_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == chargeUp) next = blast1;
        else if (sm.CurrentStateIdx == blast1) next = blast2;
        else if (sm.CurrentStateIdx == blast2) next = expel;
        else if (sm.CurrentStateIdx == expel) next = blast1;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceCubexConstruct: current state idx {sm.CurrentStateIdx} isn't CHARGE_UP_MOVE/REPEATER_BLAST_MOVE/REPEATER_BLAST_MOVE_2/EXPEL_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TrackerRubyRaider: TRACK_MOVE (initial, only once) → HOUNDS_MOVE → HOUNDS_MOVE
    //    (self-loop). ───────────────────────────────────────────────────────────────────────────
    private static void AdvanceTrackerRubyRaider(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte track = table.IdToIdx["TRACK_MOVE"];
        byte hounds = table.IdToIdx["HOUNDS_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == track) next = hounds;
        else if (sm.CurrentStateIdx == hounds) next = hounds;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTrackerRubyRaider: current state idx {sm.CurrentStateIdx} isn't TRACK_MOVE/HOUNDS_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Tunneler: BITE_MOVE (initial) → BURROW_MOVE → BELOW_MOVE → BELOW_MOVE (self-loop).
    //    DIZZY_MOVE is never reached via a FollowUpState edge — a Stun effect forces the state
    //    machine there directly (mirrors TerrorEel's STUN_MOVE). Handled anyway: DIZZY_MOVE →
    //    BITE_MOVE, matching its own registered FollowUpState. ──────────────────────────────────────
    private static void AdvanceTunneler(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte bite = table.IdToIdx["BITE_MOVE"];
        byte burrow = table.IdToIdx["BURROW_MOVE"];
        byte below = table.IdToIdx["BELOW_MOVE"];
        byte dizzy = table.IdToIdx["DIZZY_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == bite) next = burrow;
        else if (sm.CurrentStateIdx == burrow) next = below;
        else if (sm.CurrentStateIdx == below) next = below;
        else if (sm.CurrentStateIdx == dizzy) next = bite;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTunneler: current state idx {sm.CurrentStateIdx} isn't BITE_MOVE/BURROW_MOVE/BELOW_MOVE/DIZZY_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── MagiKnight: POWER_SHIELD_MOVE (initial, only once) → DAMPEN_MOVE → RAM_MOVE → PREP_MOVE →
    //    MAGIC_BOMB → RAM_MOVE (3-cycle thereafter). ─────────────────────────────────────────────────
    private static void AdvanceMagiKnight(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte powerShield = table.IdToIdx["POWER_SHIELD_MOVE"];
        byte dampen = table.IdToIdx["DAMPEN_MOVE"];
        byte ram = table.IdToIdx["RAM_MOVE"];
        byte prep = table.IdToIdx["PREP_MOVE"];
        byte magicBomb = table.IdToIdx["MAGIC_BOMB"];

        byte next;
        if (sm.CurrentStateIdx == powerShield) next = dampen;
        else if (sm.CurrentStateIdx == dampen) next = ram;
        else if (sm.CurrentStateIdx == ram) next = prep;
        else if (sm.CurrentStateIdx == prep) next = magicBomb;
        else if (sm.CurrentStateIdx == magicBomb) next = ram;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceMagiKnight: current state idx {sm.CurrentStateIdx} isn't POWER_SHIELD_MOVE/DAMPEN_MOVE/RAM_MOVE/PREP_MOVE/MAGIC_BOMB.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Entomancer: BEES_MOVE (initial) → SPEAR_MOVE → PHEROMONE_SPIT_MOVE → BEES_MOVE
    //    (3-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceEntomancer(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte bees = table.IdToIdx["BEES_MOVE"];
        byte spear = table.IdToIdx["SPEAR_MOVE"];
        byte pheromoneSpit = table.IdToIdx["PHEROMONE_SPIT_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == bees) next = spear;
        else if (sm.CurrentStateIdx == spear) next = pheromoneSpit;
        else if (sm.CurrentStateIdx == pheromoneSpit) next = bees;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceEntomancer: current state idx {sm.CurrentStateIdx} isn't BEES_MOVE/SPEAR_MOVE/PHEROMONE_SPIT_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TheInsatiable: LIQUIFY_GROUND_MOVE (initial, only reachable once) → THRASH_MOVE →
    //    LUNGING_BITE_MOVE → SALIVATE_MOVE → THRASH_MOVE_2 → THRASH_MOVE (4-cycle thereafter). ──────
    private static void AdvanceTheInsatiable(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte liquify = table.IdToIdx["LIQUIFY_GROUND_MOVE"];
        byte thrash = table.IdToIdx["THRASH_MOVE"];
        byte lungingBite = table.IdToIdx["LUNGING_BITE_MOVE"];
        byte salivate = table.IdToIdx["SALIVATE_MOVE"];
        byte thrash2 = table.IdToIdx["THRASH_MOVE_2"];

        byte next;
        if (sm.CurrentStateIdx == liquify) next = thrash;
        else if (sm.CurrentStateIdx == thrash) next = lungingBite;
        else if (sm.CurrentStateIdx == lungingBite) next = salivate;
        else if (sm.CurrentStateIdx == salivate) next = thrash2;
        else if (sm.CurrentStateIdx == thrash2) next = thrash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTheInsatiable: current state idx {sm.CurrentStateIdx} isn't LIQUIFY_GROUND_MOVE/THRASH_MOVE/LUNGING_BITE_MOVE/SALIVATE_MOVE/THRASH_MOVE_2.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Axebot: initial is BOOT_UP_MOVE or HAMMER_UPPERCUT_MOVE depending on an externally-set
    //    "stock override" (only affects the opening pick) → HAMMER_UPPERCUT_MOVE → ONE_TWO_MOVE →
    //    HAMMER_UPPERCUT_MOVE (2-cycle thereafter). ──────────────────────────────────────────────
    private static void AdvanceAxebot(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte bootUp = table.IdToIdx["BOOT_UP_MOVE"];
        byte hammerUppercut = table.IdToIdx["HAMMER_UPPERCUT_MOVE"];
        byte oneTwo = table.IdToIdx["ONE_TWO_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == bootUp) next = hammerUppercut;
        else if (sm.CurrentStateIdx == hammerUppercut) next = oneTwo;
        else if (sm.CurrentStateIdx == oneTwo) next = hammerUppercut;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceAxebot: current state idx {sm.CurrentStateIdx} isn't BOOT_UP_MOVE/HAMMER_UPPERCUT_MOVE/ONE_TWO_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── ThievingHopper: THIEVERY_MOVE (initial) → FLUTTER_MOVE → HAT_TRICK_MOVE → NAB_MOVE →
    //    ESCAPE_MOVE → ESCAPE_MOVE (self-loop — matches SimIntent.Escape, which
    //    SimEnemyTurnOps.ExecuteMove still can't execute, but the state graph itself is fully
    //    deterministic). ─────────────────────────────────────────────────────────────────────────
    private static void AdvanceThievingHopper(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte thievery = table.IdToIdx["THIEVERY_MOVE"];
        byte flutter = table.IdToIdx["FLUTTER_MOVE"];
        byte hatTrick = table.IdToIdx["HAT_TRICK_MOVE"];
        byte nab = table.IdToIdx["NAB_MOVE"];
        byte escape = table.IdToIdx["ESCAPE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == thievery) next = flutter;
        else if (sm.CurrentStateIdx == flutter) next = hatTrick;
        else if (sm.CurrentStateIdx == hatTrick) next = nab;
        else if (sm.CurrentStateIdx == nab) next = escape;
        else if (sm.CurrentStateIdx == escape) next = escape;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceThievingHopper: current state idx {sm.CurrentStateIdx} isn't THIEVERY_MOVE/FLUTTER_MOVE/HAT_TRICK_MOVE/NAB_MOVE/ESCAPE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── KnowledgeDemon: CURSE_OF_KNOWLEDGE_MOVE (initial) → SLAP_MOVE → KNOWLEDGE_OVERWHELMING_MOVE
    //    → PONDER_MOVE → ConditionalBranchState: curseOfKnowledgeCounter < 3 → CURSE_OF_KNOWLEDGE_MOVE
    //    again, else → SLAP_MOVE forever. The counter isn't a plain "ever used" bool — it caps at
    //    exactly 3 (the move throws if cast a 4th time) and CURSE_OF_KNOWLEDGE_MOVE becomes
    //    permanently unreachable once the branch flips, so counting its occurrences in the History
    //    ring buffer is safe (it can only ever appear up to 3 times, always within the last dozen
    //    moves — this monster's whole graph is a single 4-state loop with no side branch to hide it
    //    off the back of the buffer). ──────────────────────────────────────────────────────────────
    private static void AdvanceKnowledgeDemon(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte curse = table.IdToIdx["CURSE_OF_KNOWLEDGE_MOVE"];
        byte slap = table.IdToIdx["SLAP_MOVE"];
        byte overwhelming = table.IdToIdx["KNOWLEDGE_OVERWHELMING_MOVE"];
        byte ponder = table.IdToIdx["PONDER_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == curse) next = slap;
        else if (sm.CurrentStateIdx == slap) next = overwhelming;
        else if (sm.CurrentStateIdx == overwhelming) next = ponder;
        else if (sm.CurrentStateIdx == ponder)
        {
            int curseCount = 0;
            for (int j = 0; j < sm.HistoryCount; j++)
            {
                if (MostRecent(in sm, j) == curse) curseCount++;
            }
            next = curseCount < 3 ? curse : slap;
        }
        else
        {
            throw new InvalidOperationException(
                $"AdvanceKnowledgeDemon: current state idx {sm.CurrentStateIdx} isn't CURSE_OF_KNOWLEDGE_MOVE/SLAP_MOVE/KNOWLEDGE_OVERWHELMING_MOVE/PONDER_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TurretOperator: UNLOAD_MOVE → UNLOAD_MOVE_2 → RELOAD_MOVE → UNLOAD_MOVE (3-cycle, fully
    //    deterministic). ───────────────────────────────────────────────────────────────────────────
    private static void AdvanceTurretOperator(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte unload1 = table.IdToIdx["UNLOAD_MOVE"];
        byte unload2 = table.IdToIdx["UNLOAD_MOVE_2"];
        byte reload = table.IdToIdx["RELOAD_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == unload1) next = unload2;
        else if (sm.CurrentStateIdx == unload2) next = reload;
        else if (sm.CurrentStateIdx == reload) next = unload1;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTurretOperator: current state idx {sm.CurrentStateIdx} isn't UNLOAD_MOVE/UNLOAD_MOVE_2/RELOAD_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── LouseProgenitor: WEB_CANNON_MOVE (initial) → CURL_AND_GROW_MOVE → POUNCE_MOVE →
    //    WEB_CANNON_MOVE (3-cycle, fully deterministic). ──────────────────────────────────────────
    private static void AdvanceLouseProgenitor(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte webCannon = table.IdToIdx["WEB_CANNON_MOVE"];
        byte curlAndGrow = table.IdToIdx["CURL_AND_GROW_MOVE"];
        byte pounce = table.IdToIdx["POUNCE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == webCannon) next = curlAndGrow;
        else if (sm.CurrentStateIdx == curlAndGrow) next = pounce;
        else if (sm.CurrentStateIdx == pounce) next = webCannon;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceLouseProgenitor: current state idx {sm.CurrentStateIdx} isn't WEB_CANNON_MOVE/CURL_AND_GROW_MOVE/POUNCE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Wriggler: NASTY_BITE_MOVE ↔ WRIGGLE_MOVE (2-cycle) is the normal cadence — the "INIT_MOVE"
    //    ConditionalBranchState that picks between them is SlotName-gated but only matters for the
    //    very first pick, same as Toadpole/Myte/Exoskeleton. SPAWNED_MOVE is different: it has no
    //    incoming edge in this graph (an external Stun effect forces the SM there directly, the usual
    //    "external force" pattern), but its OWN FollowUpState is that same SlotName-gated branch —
    //    and this time SlotName genuinely decides the next REAL move, not just the opening one. We
    //    don't track SlotName in the blob, so this is a real gap: throw rather than guess. ───────────
    private static void AdvanceWriggler(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte nastyBite = table.IdToIdx["NASTY_BITE_MOVE"];
        byte wriggle = table.IdToIdx["WRIGGLE_MOVE"];
        byte spawned = table.IdToIdx["SPAWNED_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == nastyBite) next = wriggle;
        else if (sm.CurrentStateIdx == wriggle) next = nastyBite;
        else if (sm.CurrentStateIdx == spawned)
        {
            throw new InvalidOperationException(
                "AdvanceWriggler: current state is SPAWNED_MOVE — its follow-up is a SlotName-gated " +
                "ConditionalBranchState (\"wriggler1\"/\"wriggler2\"/\"wriggler3\"/\"wriggler4\"), and " +
                "SlotName isn't tracked in the blob. Unlike other monsters' SlotName-gated initial " +
                "picks, this one decides a real mid-fight transition, not just the opening move.");
        }
        else
        {
            throw new InvalidOperationException(
                $"AdvanceWriggler: current state idx {sm.CurrentStateIdx} isn't NASTY_BITE_MOVE/WRIGGLE_MOVE/SPAWNED_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TheObscura: ILLUSION_MOVE (initial, Summon intent, never revisited) → "RAND":
    //    PIERCING_GAZE_MOVE / SAIL_MOVE / HARDENING_STRIKE_MOVE, equal weight 1 each, all
    //    CannotRepeat. ──────────────────────────────────────────────────────────────────────────────
    private static void AdvanceTheObscura(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte illusion = table.IdToIdx["ILLUSION_MOVE"];
        byte piercingGaze = table.IdToIdx["PIERCING_GAZE_MOVE"];
        byte sail = table.IdToIdx["SAIL_MOVE"];
        byte hardeningStrike = table.IdToIdx["HARDENING_STRIKE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == illusion)
        {
            Span<byte> candidates = stackalloc byte[3] { piercingGaze, sail, hardeningStrike };
            Span<float> weights = stackalloc float[3];
            for (int i = 0; i < candidates.Length; i++)
                weights[i] = BranchWeight(in sm, candidates[i], baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else if (sm.CurrentStateIdx == piercingGaze || sm.CurrentStateIdx == sail || sm.CurrentStateIdx == hardeningStrike)
        {
            Span<byte> candidates = stackalloc byte[3] { piercingGaze, sail, hardeningStrike };
            Span<float> weights = stackalloc float[3];
            for (int i = 0; i < candidates.Length; i++)
                weights[i] = BranchWeight(in sm, candidates[i], baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTheObscura: current state idx {sm.CurrentStateIdx} isn't ILLUSION_MOVE/PIERCING_GAZE_MOVE/SAIL_MOVE/HARDENING_STRIKE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── CeremonialBeast: STAMP_MOVE (initial, once) → PLOW_MOVE → PLOW_MOVE (self-loop) is the
    //    first-phase cadence. STUN_MOVE has no incoming edge in this graph — an external
    //    SetStunned() call (triggered when the player breaks the Plow shield) forces the SM there
    //    directly, same "external force" pattern as TerrorEel/Tunneler. From there it's a fixed
    //    second-phase cycle: STUN_MOVE → BEAST_CRY_MOVE → STOMP_MOVE → CRUSH_MOVE → BEAST_CRY_MOVE. ──
    private static void AdvanceCeremonialBeast(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte stamp = table.IdToIdx["STAMP_MOVE"];
        byte plow = table.IdToIdx["PLOW_MOVE"];
        byte stun = table.IdToIdx["STUN_MOVE"];
        byte beastCry = table.IdToIdx["BEAST_CRY_MOVE"];
        byte stomp = table.IdToIdx["STOMP_MOVE"];
        byte crush = table.IdToIdx["CRUSH_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == stamp) next = plow;
        else if (sm.CurrentStateIdx == plow) next = plow;
        else if (sm.CurrentStateIdx == stun) next = beastCry;
        else if (sm.CurrentStateIdx == beastCry) next = stomp;
        else if (sm.CurrentStateIdx == stomp) next = crush;
        else if (sm.CurrentStateIdx == crush) next = beastCry;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceCeremonialBeast: current state idx {sm.CurrentStateIdx} isn't STAMP_MOVE/PLOW_MOVE/STUN_MOVE/BEAST_CRY_MOVE/STOMP_MOVE/CRUSH_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TheForgotten: MIASMA (initial) → DREAD → MIASMA (2-cycle, fully deterministic). ─────────────
    private static void AdvanceTheForgotten(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte miasma = table.IdToIdx["MIASMA"];
        byte dread = table.IdToIdx["DREAD"];

        byte next;
        if (sm.CurrentStateIdx == miasma) next = dread;
        else if (sm.CurrentStateIdx == dread) next = miasma;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTheForgotten: current state idx {sm.CurrentStateIdx} isn't MIASMA/DREAD.");
        }

        TransitionTo(ref sm, next);
    }

    // ── LagavulinMatriarch: SLEEP_MOVE (initial) → ConditionalBranchState "SLEEP_BRANCH": still has
    //    AsleepPower → SLEEP_MOVE again, else → SLASH_MOVE → DISEMBOWEL_MOVE → SLASH2_MOVE →
    //    SOUL_SIPHON_MOVE → SLASH_MOVE (4-cycle once awake). Same shape as SlumberingBeetle's
    //    Slumber check, just a different power. ──────────────────────────────────────────────────────
    private static void AdvanceLagavulinMatriarch(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte sleep = table.IdToIdx["SLEEP_MOVE"];
        byte slash = table.IdToIdx["SLASH_MOVE"];
        byte disembowel = table.IdToIdx["DISEMBOWEL_MOVE"];
        byte slash2 = table.IdToIdx["SLASH2_MOVE"];
        byte soulSiphon = table.IdToIdx["SOUL_SIPHON_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == sleep)
        {
            bool stillAsleep = SimPowerOps.TryGetEnemyAmount(state, enemyIdx, SimPowerType.Asleep, out _);
            next = stillAsleep ? sleep : slash;
        }
        else if (sm.CurrentStateIdx == slash) next = disembowel;
        else if (sm.CurrentStateIdx == disembowel) next = slash2;
        else if (sm.CurrentStateIdx == slash2) next = soulSiphon;
        else if (sm.CurrentStateIdx == soulSiphon) next = slash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceLagavulinMatriarch: current state idx {sm.CurrentStateIdx} isn't SLEEP_MOVE/SLASH_MOVE/DISEMBOWEL_MOVE/SLASH2_MOVE/SOUL_SIPHON_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Vantom: INK_BLOT_MOVE → INKY_LANCE_MOVE → DISMEMBER_MOVE → PREPARE_MOVE → INK_BLOT_MOVE
    //    (4-cycle, fully deterministic). ──────────────────────────────────────────────────────────
    private static void AdvanceVantom(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte inkBlot = table.IdToIdx["INK_BLOT_MOVE"];
        byte inkyLance = table.IdToIdx["INKY_LANCE_MOVE"];
        byte dismember = table.IdToIdx["DISMEMBER_MOVE"];
        byte prepare = table.IdToIdx["PREPARE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == inkBlot) next = inkyLance;
        else if (sm.CurrentStateIdx == inkyLance) next = dismember;
        else if (sm.CurrentStateIdx == dismember) next = prepare;
        else if (sm.CurrentStateIdx == prepare) next = inkBlot;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceVantom: current state idx {sm.CurrentStateIdx} isn't INK_BLOT_MOVE/INKY_LANCE_MOVE/DISMEMBER_MOVE/PREPARE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── WaterfallGiant: PRESSURIZE_MOVE (initial, once) → STOMP_MOVE → RAM_MOVE → SIPHON_MOVE →
    //    PRESSURE_GUN_MOVE → PRESSURE_UP_MOVE → STOMP_MOVE (5-cycle thereafter). ABOUT_TO_BLOW_MOVE
    //    has no incoming edge in this graph — an external TriggerAboutToBlowState() call forces the
    //    SM there directly (same "external force" pattern), then → EXPLODE_MOVE → EXPLODE_MOVE
    //    (self-loop, matches its DeathBlowIntent — ends the fight). ─────────────────────────────────
    private static void AdvanceWaterfallGiant(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte pressurize = table.IdToIdx["PRESSURIZE_MOVE"];
        byte stomp = table.IdToIdx["STOMP_MOVE"];
        byte ram = table.IdToIdx["RAM_MOVE"];
        byte siphon = table.IdToIdx["SIPHON_MOVE"];
        byte pressureGun = table.IdToIdx["PRESSURE_GUN_MOVE"];
        byte pressureUp = table.IdToIdx["PRESSURE_UP_MOVE"];
        byte aboutToBlow = table.IdToIdx["ABOUT_TO_BLOW_MOVE"];
        byte explode = table.IdToIdx["EXPLODE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == pressurize) next = stomp;
        else if (sm.CurrentStateIdx == stomp) next = ram;
        else if (sm.CurrentStateIdx == ram) next = siphon;
        else if (sm.CurrentStateIdx == siphon) next = pressureGun;
        else if (sm.CurrentStateIdx == pressureGun) next = pressureUp;
        else if (sm.CurrentStateIdx == pressureUp) next = stomp;
        else if (sm.CurrentStateIdx == aboutToBlow) next = explode;
        else if (sm.CurrentStateIdx == explode) next = explode;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceWaterfallGiant: current state idx {sm.CurrentStateIdx} isn't PRESSURIZE_MOVE/STOMP_MOVE/RAM_MOVE/SIPHON_MOVE/PRESSURE_GUN_MOVE/PRESSURE_UP_MOVE/ABOUT_TO_BLOW_MOVE/EXPLODE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TheLost: DEBILITATING_SMOG ↔ EYE_LASERS (2-cycle, fully deterministic). ─────────────────────
    private static void AdvanceTheLost(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte smog = table.IdToIdx["DEBILITATING_SMOG"];
        byte eyeLasers = table.IdToIdx["EYE_LASERS"];

        byte next;
        if (sm.CurrentStateIdx == smog) next = eyeLasers;
        else if (sm.CurrentStateIdx == eyeLasers) next = smog;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTheLost: current state idx {sm.CurrentStateIdx} isn't DEBILITATING_SMOG/EYE_LASERS.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TheAdversaryMkOne: SMASH_MOVE → BEAM_MOVE → BARRAGE_MOVE → SMASH_MOVE (3-cycle, fully
    //    deterministic). ──────────────────────────────────────────────────────────────────────────
    private static void AdvanceTheAdversaryMkOne(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte smash = table.IdToIdx["SMASH_MOVE"];
        byte beam = table.IdToIdx["BEAM_MOVE"];
        byte barrage = table.IdToIdx["BARRAGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == smash) next = beam;
        else if (sm.CurrentStateIdx == beam) next = barrage;
        else if (sm.CurrentStateIdx == barrage) next = smash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTheAdversaryMkOne: current state idx {sm.CurrentStateIdx} isn't SMASH_MOVE/BEAM_MOVE/BARRAGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TheAdversaryMkTwo: BASH_MOVE → FLAME_BEAM_MOVE → BARRAGE_MOVE → BASH_MOVE (3-cycle, fully
    //    deterministic). ──────────────────────────────────────────────────────────────────────────
    private static void AdvanceTheAdversaryMkTwo(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte bash = table.IdToIdx["BASH_MOVE"];
        byte flameBeam = table.IdToIdx["FLAME_BEAM_MOVE"];
        byte barrage = table.IdToIdx["BARRAGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == bash) next = flameBeam;
        else if (sm.CurrentStateIdx == flameBeam) next = barrage;
        else if (sm.CurrentStateIdx == barrage) next = bash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTheAdversaryMkTwo: current state idx {sm.CurrentStateIdx} isn't BASH_MOVE/FLAME_BEAM_MOVE/BARRAGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── TheAdversaryMkThree: CRASH_MOVE → FLAME_BEAM_MOVE → BARRAGE_MOVE → CRASH_MOVE (3-cycle,
    //    fully deterministic). ────────────────────────────────────────────────────────────────────
    private static void AdvanceTheAdversaryMkThree(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte crash = table.IdToIdx["CRASH_MOVE"];
        byte flameBeam = table.IdToIdx["FLAME_BEAM_MOVE"];
        byte barrage = table.IdToIdx["BARRAGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == crash) next = flameBeam;
        else if (sm.CurrentStateIdx == flameBeam) next = barrage;
        else if (sm.CurrentStateIdx == barrage) next = crash;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceTheAdversaryMkThree: current state idx {sm.CurrentStateIdx} isn't CRASH_MOVE/FLAME_BEAM_MOVE/BARRAGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── DecimillipedeSegment (Back/Front/Middle share one base class and one identical state graph —
    //    only SegmentAttack's visual differs): WRITHE_MOVE → CONSTRICT_MOVE → BULK_MOVE → WRITHE_MOVE
    //    is the fixed 3-cycle (initial pick is StarterMoveIdx%3-gated, irrelevant here). DEAD_MOVE has
    //    no incoming edge in this graph (external force when the segment's HP hits 0 but
    //    ReattachPower keeps it from truly dying, the usual "external force" pattern) → REATTACH_MOVE
    //    → "RAND" (WRITHE/BULK/CONSTRICT, equal weight, all CannotRepeat) → rejoins the fixed cycle
    //    from wherever it landed. ───────────────────────────────────────────────────────────────────
    private static void AdvanceDecimillipedeSegment(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte writhe = table.IdToIdx["WRITHE_MOVE"];
        byte bulk = table.IdToIdx["BULK_MOVE"];
        byte constrict = table.IdToIdx["CONSTRICT_MOVE"];
        byte dead = table.IdToIdx["DEAD_MOVE"];
        byte reattach = table.IdToIdx["REATTACH_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == writhe) next = constrict;
        else if (sm.CurrentStateIdx == constrict) next = bulk;
        else if (sm.CurrentStateIdx == bulk) next = writhe;
        else if (sm.CurrentStateIdx == dead) next = reattach;
        else if (sm.CurrentStateIdx == reattach)
        {
            Span<byte> candidates = stackalloc byte[3] { writhe, bulk, constrict };
            Span<float> weights = stackalloc float[3];
            for (int i = 0; i < candidates.Length; i++)
                weights[i] = BranchWeight(in sm, candidates[i], baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else
        {
            throw new InvalidOperationException(
                $"AdvanceDecimillipedeSegment: current state idx {sm.CurrentStateIdx} isn't WRITHE_MOVE/CONSTRICT_MOVE/BULK_MOVE/DEAD_MOVE/REATTACH_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Exoskeleton: the "INIT_MOVE" ConditionalBranchState is SlotName-gated four ways, but that
    //    ONLY decides the opening move (same as Toadpole/Myte) — turns out this one was wrongly
    //    deferred earlier as a SlotName gap; the ONGOING edges never touch SlotName at all.
    //    SKITTER_MOVE → "RAND" (SKITTER/MANDIBLES, equal weight, CannotRepeat); MANDIBLES_MOVE →
    //    ENRAGE_MOVE → "RAND" (same two-way roll). ─────────────────────────────────────────────────
    private static void AdvanceExoskeleton(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte skitter = table.IdToIdx["SKITTER_MOVE"];
        byte mandibles = table.IdToIdx["MANDIBLES_MOVE"];
        byte enrage = table.IdToIdx["ENRAGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == skitter || sm.CurrentStateIdx == enrage)
        {
            Span<byte> candidates = stackalloc byte[2] { skitter, mandibles };
            Span<float> weights = stackalloc float[2];
            weights[0] = BranchWeight(in sm, skitter, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            weights[1] = BranchWeight(in sm, mandibles, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else if (sm.CurrentStateIdx == mandibles) next = enrage;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceExoskeleton: current state idx {sm.CurrentStateIdx} isn't SKITTER_MOVE/MANDIBLES_MOVE/ENRAGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Ovicopter: LAY_EGGS_MOVE (initial, Summon intent) → SMASH_MOVE → TENDERIZER_MOVE →
    //    ConditionalBranchState "SUMMON_BRANCH_STATE": CanLay (living-teammate count, INCLUDING
    //    self, <= 3) → LAY_EGGS_MOVE again, else → NUTRITIONAL_PASTE_MOVE → SMASH_MOVE. Unlike
    //    LivingShield's ally count, CanLay does NOT exclude self (matches the live game's own
    //    `GetTeammatesOf(...).Count(IsAlive)` call, which counts the creature itself too). ──────────
    private static void AdvanceOvicopter(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte layEggs = table.IdToIdx["LAY_EGGS_MOVE"];
        byte smash = table.IdToIdx["SMASH_MOVE"];
        byte tenderizer = table.IdToIdx["TENDERIZER_MOVE"];
        byte nutritionalPaste = table.IdToIdx["NUTRITIONAL_PASTE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == layEggs) next = smash;
        else if (sm.CurrentStateIdx == nutritionalPaste) next = smash;
        else if (sm.CurrentStateIdx == smash) next = tenderizer;
        else if (sm.CurrentStateIdx == tenderizer)
        {
            int livingCount = 0;
            for (int i = 0; i < state.EnemyCount; i++)
            {
                if (state.EnemyHp[i] > 0) livingCount++;
            }
            next = livingCount <= 3 ? layEggs : nutritionalPaste;
        }
        else
        {
            throw new InvalidOperationException(
                $"AdvanceOvicopter: current state idx {sm.CurrentStateIdx} isn't LAY_EGGS_MOVE/SMASH_MOVE/TENDERIZER_MOVE/NUTRITIONAL_PASTE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── FakeMerchantMonster: SWIPE_MOVE (initial) / SPEW_COINS_MOVE / ENRAGE_MOVE all → "RAND_MOVE"
    //    (SWIPE weight 1, SPEW_COINS weight 1, THROW_RELIC weight 1, ENRAGE weight 3, all
    //    CannotRepeat). THROW_RELIC_MOVE instead → "RAND_ATTACK_MOVE" (same three non-Enrage
    //    branches, still equal weight 1 each, CannotRepeat — Enrage is excluded from this one). ─────
    private static void AdvanceFakeMerchantMonster(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte swipe = table.IdToIdx["SWIPE_MOVE"];
        byte spewCoins = table.IdToIdx["SPEW_COINS_MOVE"];
        byte throwRelic = table.IdToIdx["THROW_RELIC_MOVE"];
        byte enrage = table.IdToIdx["ENRAGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == swipe || sm.CurrentStateIdx == spewCoins || sm.CurrentStateIdx == enrage)
        {
            Span<byte> candidates = stackalloc byte[4] { swipe, spewCoins, throwRelic, enrage };
            Span<float> weights = stackalloc float[4];
            weights[0] = BranchWeight(in sm, swipe, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            weights[1] = BranchWeight(in sm, spewCoins, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            weights[2] = BranchWeight(in sm, throwRelic, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            weights[3] = BranchWeight(in sm, enrage, baseWeight: 3f, MoveRepeatKind.CannotRepeat);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else if (sm.CurrentStateIdx == throwRelic)
        {
            Span<byte> candidates = stackalloc byte[3] { swipe, spewCoins, throwRelic };
            Span<float> weights = stackalloc float[3];
            weights[0] = BranchWeight(in sm, swipe, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            weights[1] = BranchWeight(in sm, spewCoins, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            weights[2] = BranchWeight(in sm, throwRelic, baseWeight: 1f, MoveRepeatKind.CannotRepeat);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else
        {
            throw new InvalidOperationException(
                $"AdvanceFakeMerchantMonster: current state idx {sm.CurrentStateIdx} isn't SWIPE_MOVE/SPEW_COINS_MOVE/THROW_RELIC_MOVE/ENRAGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Fogmog: ILLUSION_MOVE (initial, Summon intent, once) → SWIPE_MOVE → "BRANCH" (weight 0.4
    //    SWIPE_RANDOM_MOVE / weight 0.6 HEADBUTT_MOVE, both CannotRepeat). SWIPE_RANDOM_MOVE's own
    //    FollowUpState bypasses the branch and goes straight to HEADBUTT_MOVE; HEADBUTT_MOVE's
    //    FollowUpState goes back to SWIPE_MOVE (not the branch either) — the branch is only ever
    //    entered from SWIPE_MOVE. ───────────────────────────────────────────────────────────────────
    private static void AdvanceFogmog(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte illusion = table.IdToIdx["ILLUSION_MOVE"];
        byte swipe = table.IdToIdx["SWIPE_MOVE"];
        byte swipeRandom = table.IdToIdx["SWIPE_RANDOM_MOVE"];
        byte headbutt = table.IdToIdx["HEADBUTT_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == illusion) next = swipe;
        else if (sm.CurrentStateIdx == swipe)
        {
            Span<byte> candidates = stackalloc byte[2] { swipeRandom, headbutt };
            Span<float> weights = stackalloc float[2];
            weights[0] = BranchWeight(in sm, swipeRandom, baseWeight: 0.4f, MoveRepeatKind.CannotRepeat);
            weights[1] = BranchWeight(in sm, headbutt, baseWeight: 0.6f, MoveRepeatKind.CannotRepeat);
            next = RollWeighted(candidates, weights, ref rng);
        }
        else if (sm.CurrentStateIdx == swipeRandom) next = headbutt;
        else if (sm.CurrentStateIdx == headbutt) next = swipe;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceFogmog: current state idx {sm.CurrentStateIdx} isn't ILLUSION_MOVE/SWIPE_MOVE/SWIPE_RANDOM_MOVE/HEADBUTT_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── LivingFog: ADVANCED_GAS_MOVE (initial, once) → BLOAT_MOVE → SUPER_GAS_BLAST_MOVE →
    //    BLOAT_MOVE (2-cycle thereafter). ──────────────────────────────────────────────────────────
    private static void AdvanceLivingFog(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte advancedGas = table.IdToIdx["ADVANCED_GAS_MOVE"];
        byte bloat = table.IdToIdx["BLOAT_MOVE"];
        byte superGasBlast = table.IdToIdx["SUPER_GAS_BLAST_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == advancedGas) next = bloat;
        else if (sm.CurrentStateIdx == bloat) next = superGasBlast;
        else if (sm.CurrentStateIdx == superGasBlast) next = bloat;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceLivingFog: current state idx {sm.CurrentStateIdx} isn't ADVANCED_GAS_MOVE/BLOAT_MOVE/SUPER_GAS_BLAST_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    // ── Queen: PUPPET_STRINGS_MOVE (initial) → YOU_ARE_MINE_MOVE → ConditionalBranchState
    //    "YOURE_MINE_NOW_BRANCH": !HasAmalgamDied → BURN_BRIGHT_FOR_ME_MOVE, HasAmalgamDied →
    //    OFF_WITH_YOUR_HEAD_MOVE. BURN_BRIGHT_FOR_ME_MOVE → "BURN_BRIGHT_FOR_ME_BRANCH": same
    //    check, !HasAmalgamDied loops back to itself. OFF_WITH_YOUR_HEAD_MOVE → EXECUTION_MOVE →
    //    ENRAGE_MOVE → OFF_WITH_YOUR_HEAD_MOVE (3-cycle once Amalgam is down). HasAmalgamDied is
    //    "no living enemy with SimMonsterKind.TorchHeadAmalgam" — now answerable via
    //    CombatNodeBlob.EnemyMonsterKind (added specifically to unblock this).
    //
    //    NOT replicated: AfterDeath's reactive override — if Amalgam dies at the exact moment
    //    Queen's CurrentStateIdx is ALREADY BURN_BRIGHT_FOR_ME_MOVE (i.e. she's mid-telegraph, not
    //    yet executed it), the real game force-jumps her straight to ENRAGE_MOVE via
    //    SetMoveImmediate, bypassing OFF_WITH_YOUR_HEAD_MOVE/EXECUTION_MOVE entirely. That's a
    //    reactive hook fired by a DIFFERENT creature's death event, not something an Advance
    //    function (which only runs after Queen's OWN move finishes) can express — would need a
    //    general "on any enemy death, check for forced-transition rules" hook this codebase
    //    doesn't have yet. This function is accurate for "Queen's move just finished, what's next,"
    //    just not for that one instant-interrupt edge case. ─────────────────────────────────────────
    private static void AdvanceQueen(CombatNodeBlob state, int enemyIdx, ref SimEnemyMoveSM sm, MonsterStateTable table, ref RandomState rng, ushort ascensionFlags)
    {
        byte puppetStrings = table.IdToIdx["PUPPET_STRINGS_MOVE"];
        byte youAreMine = table.IdToIdx["YOU_ARE_MINE_MOVE"];
        byte burnBright = table.IdToIdx["BURN_BRIGHT_FOR_ME_MOVE"];
        byte offWithYourHead = table.IdToIdx["OFF_WITH_YOUR_HEAD_MOVE"];
        byte execution = table.IdToIdx["EXECUTION_MOVE"];
        byte enrage = table.IdToIdx["ENRAGE_MOVE"];

        byte next;
        if (sm.CurrentStateIdx == puppetStrings) next = youAreMine;
        else if (sm.CurrentStateIdx == youAreMine || sm.CurrentStateIdx == burnBright)
        {
            next = HasAmalgamDied(state) ? offWithYourHead : burnBright;
        }
        else if (sm.CurrentStateIdx == offWithYourHead) next = execution;
        else if (sm.CurrentStateIdx == execution) next = enrage;
        else if (sm.CurrentStateIdx == enrage) next = offWithYourHead;
        else
        {
            throw new InvalidOperationException(
                $"AdvanceQueen: current state idx {sm.CurrentStateIdx} isn't PUPPET_STRINGS_MOVE/YOU_ARE_MINE_MOVE/BURN_BRIGHT_FOR_ME_MOVE/OFF_WITH_YOUR_HEAD_MOVE/EXECUTION_MOVE/ENRAGE_MOVE.");
        }

        TransitionTo(ref sm, next);
    }

    private static bool HasAmalgamDied(CombatNodeBlob state)
    {
        for (int i = 0; i < state.EnemyCount; i++)
        {
            if (state.EnemyMonsterKind[i] == SimMonsterKind.TorchHeadAmalgam && state.EnemyHp[i] > 0)
                return false;
        }
        return true;
    }

    // ── Shared RandomBranchState replication ────────────────────────────────────────────────────
    // Mirrors RandomBranchState.GetStateWeight/GetNextState exactly (game_source confirmed, not
    // guessed): the weight-lambda itself (any ascension/HP-dependent multiplier) is per-monster and
    // gets passed in as baseWeight; everything below is the repeat-type/cooldown gating, which is
    // identical machinery for every monster using RandomBranchState.

    private enum MoveRepeatKind : byte { CanRepeatForever, CannotRepeat, CanRepeatXTimes, UseOnlyOnce }

    /// <summary>0 if <paramref name="candidateIdx"/> is currently excluded by its repeat rule or
    /// cooldown, else <paramref name="baseWeight"/>.</summary>
    private static float BranchWeight(in SimEnemyMoveSM sm, byte candidateIdx, float baseWeight, MoveRepeatKind repeat, int maxTimes = 0, int cooldown = 0)
    {
        if (repeat == MoveRepeatKind.UseOnlyOnce)
        {
            if (sm.EverUsed(candidateIdx)) return 0f;
        }
        else if (repeat != MoveRepeatKind.CanRepeatForever)
        {
            int limit = repeat == MoveRepeatKind.CannotRepeat ? 1 : maxTimes;
            if (AllOfLastNAre(in sm, limit, candidateIdx)) return 0f;
        }

        if (cooldown > 0 && WithinLastN(in sm, cooldown, candidateIdx)) return 0f;

        return baseWeight;
    }

    /// <summary>True iff the most recent <paramref name="n"/> History entries all exist
    /// (HistoryCount >= n) and are all equal to <paramref name="idx"/> — CannotRepeat/CanRepeatXTimes
    /// both reduce to this with n = 1 / n = maxTimes respectively.</summary>
    private static bool AllOfLastNAre(in SimEnemyMoveSM sm, int n, byte idx)
    {
        if (sm.HistoryCount < n) return false;
        for (int j = 0; j < n; j++)
        {
            if (MostRecent(in sm, j) != idx) return false;
        }
        return true;
    }

    /// <summary>True iff <paramref name="idx"/> appears anywhere in the most recent
    /// <paramref name="n"/> History entries — the cooldown check.</summary>
    private static bool WithinLastN(in SimEnemyMoveSM sm, int n, byte idx)
    {
        int count = Math.Min(n, sm.HistoryCount);
        for (int j = 0; j < count; j++)
        {
            if (MostRecent(in sm, j) == idx) return true;
        }
        return false;
    }

    /// <summary>History entry <paramref name="j"/> writes back from the most recent one
    /// (j = 0 → most recent). Caller must ensure j &lt; HistoryCount.</summary>
    private static byte MostRecent(in SimEnemyMoveSM sm, int j)
    {
        int pos = (sm.HistoryHead - 1 - j + SimEnemyMoveSM.HistoryCap * 2) % SimEnemyMoveSM.HistoryCap;
        return sm.History[pos];
    }

    /// <summary>Mirrors RandomBranchState.GetNextState: sum the weights, roll
    /// <c>rng.NextFloat(sum)</c> — which the real Rng.NextFloat implements as
    /// <c>(float)(MegaRandom.NextDouble() * (max - min) + min)</c>, i.e. exactly
    /// <see cref="RandomStateOps.NextDouble"/> scaled — then walk candidates subtracting weight
    /// until the running total drops to/below 0.</summary>
    private static byte RollWeighted(ReadOnlySpan<byte> candidates, ReadOnlySpan<float> weights, ref RandomState rng)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++) total += weights[i];

        float roll = (float)(RandomStateOps.NextDouble(ref rng) * total);
        for (int i = 0; i < candidates.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return candidates[i];
        }

        throw new InvalidOperationException("RollWeighted: no candidate had positive weight — all branches excluded simultaneously?");
    }
}
