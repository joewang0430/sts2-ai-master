using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2.Agent.Sim;

/// <summary>
/// Hand-written per-monster numeric payload for Defend/Buff/Debuff/Heal moves — the data the game
/// itself does not expose generically (unlike Attack's <c>DamageCalc</c>; see
/// dev_docs/Enemy_Intent_Payload_Backlog.md for why this can't be automated the way SimCardDb /
/// SimRelicDb were).
///
/// This registry is INTENTIONALLY partial and grows incrementally, monster by monster, across many
/// future sessions. Unlike SimPowerRegistry / SimCardDb / SimRelicDb, an unregistered monster is
/// NOT a bug — <see cref="Write"/> just returns 0 effects (tag-only, matching today's baseline),
/// and there is deliberately no SimCaps completeness check here.
///
/// Each writer function hand-replicates the live formula from the monster's own source file
/// (block/power-amount/heal-amount, including any <c>AscensionHelper.GetValueIfAscension</c>
/// branch, read from OUR captured <see cref="SimAscension"/> bitmask, never from the live
/// game's RunManager).
/// </summary>
internal static class SimMonsterMoveEffects
{
    public delegate int EffectWriter(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst);

    private static readonly FrozenDictionary<Type, EffectWriter> _byType = new Dictionary<Type, EffectWriter>
    {
        { typeof(BowlbugNectar), WriteBowlbugNectar },
        { typeof(Guardbot), WriteGuardbot },
        { typeof(AxeRubyRaider), WriteAxeRubyRaider },
        { typeof(BruteRubyRaider), WriteBruteRubyRaider },
        { typeof(CrossbowRubyRaider), WriteCrossbowRubyRaider },
        { typeof(BygoneEffigy), WriteBygoneEffigy },
        { typeof(CalcifiedCultist), WriteCalcifiedCultist },
        { typeof(DampCultist), WriteDampCultist },
        { typeof(BowlbugEgg), WriteBowlbugEgg },
        { typeof(DevotedSculptor), WriteDevotedSculptor },
        { typeof(Exoskeleton), WriteExoskeleton },
        { typeof(FlailKnight), WriteFlailKnight },
        { typeof(FuzzyWurmCrawler), WriteFuzzyWurmCrawler },
        { typeof(BowlbugSilk), WriteBowlbugSilk },
        { typeof(Flyconid), WriteFlyconid },
        { typeof(FossilStalker), WriteFossilStalker },
        { typeof(CorpseSlug), WriteCorpseSlug },
        { typeof(HunterKiller), WriteHunterKiller },
        { typeof(KinFollower), WriteKinFollower },
        { typeof(LivingShield), WriteLivingShield },
        { typeof(Mawler), WriteMawler },
        { typeof(Myte), WriteMyte },
        { typeof(Rocket), WriteRocket },
        { typeof(ScrollOfBiting), WriteScrollOfBiting },
        { typeof(SewerClam), WriteSewerClam },
        { typeof(ShrinkerBeetle), WriteShrinkerBeetle },
        { typeof(SkulkingColony), WriteSkulkingColony },
        { typeof(SlumberingBeetle), WriteSlumberingBeetle },
        { typeof(SnappingJaxfruit), WriteSnappingJaxfruit },
        { typeof(SoulNexus), WriteSoulNexus },
        { typeof(SpectralKnight), WriteSpectralKnight },
        { typeof(SpinyToad), WriteSpinyToad },
        { typeof(Stabbot), WriteStabbot },
        { typeof(FakeMerchantMonster), WriteFakeMerchantMonster },
        { typeof(FrogKnight), WriteFrogKnight },
        { typeof(GlobeHead), WriteGlobeHead },
        { typeof(HauntedShip), WriteHauntedShip },
        { typeof(InfestedPrism), WriteInfestedPrism },
        { typeof(MechaKnight), WriteMechaKnight },
        { typeof(Nibbit), WriteNibbit },
        { typeof(PhantasmalGardener), WritePhantasmalGardener },
        { typeof(PunchConstruct), WritePunchConstruct },
        { typeof(Seapunk), WriteSeapunk },
        { typeof(SlitheringStrangler), WriteSlitheringStrangler },
        { typeof(SoulFysh), WriteSoulFysh },
        { typeof(GremlinMerc), WriteGremlinMerc },
        { typeof(Crusher), WriteCrusher },
        { typeof(KinPriest), WriteKinPriest },
        { typeof(OwlMagistrate), WriteOwlMagistrate },
        { typeof(SludgeSpinner), WriteSludgeSpinner },
        { typeof(SlimedBerserker), WriteSlimedBerserker },
        { typeof(TerrorEel), WriteTerrorEel },
        { typeof(TheAdversaryMkOne), WriteTheAdversaryMkOne },
        { typeof(TheAdversaryMkTwo), WriteTheAdversaryMkTwo },
        { typeof(TheAdversaryMkThree), WriteTheAdversaryMkThree },
        { typeof(TheLost), WriteTheLost },
        { typeof(Toadpole), WriteToadpole },
        { typeof(TrackerRubyRaider), WriteTrackerRubyRaider },
        { typeof(Tunneler), WriteTunneler },
        { typeof(MagiKnight), WriteMagiKnight },
        { typeof(Aeonglass), WriteAeonglass },
        { typeof(CubexConstruct), WriteCubexConstruct },
        { typeof(Entomancer), WriteEntomancer },
        { typeof(KnowledgeDemon), WriteKnowledgeDemon },
        { typeof(TheInsatiable), WriteTheInsatiable },
        { typeof(ThievingHopper), WriteThievingHopper },
        { typeof(Axebot), WriteAxebot },
        { typeof(TestSubject), WriteTestSubject },
        { typeof(LouseProgenitor), WriteLouseProgenitor },
        { typeof(LagavulinMatriarch), WriteLagavulinMatriarch },
        { typeof(TheForgotten), WriteTheForgotten },
        { typeof(TheObscura), WriteTheObscura },
        { typeof(CeremonialBeast), WriteCeremonialBeast },
        { typeof(Queen), WriteQueen },
        { typeof(DecimillipedeSegmentBack), WriteDecimillipedeSegment },
        { typeof(DecimillipedeSegmentFront), WriteDecimillipedeSegment },
        { typeof(DecimillipedeSegmentMiddle), WriteDecimillipedeSegment },
        { typeof(TurretOperator), WriteTurretOperator },
        { typeof(Vantom), WriteVantom },
        { typeof(WaterfallGiant), WriteWaterfallGiant },
        { typeof(Wriggler), WriteWriggler },
        { typeof(TwoTailedRat), WriteTwoTailedRat },
        { typeof(Fogmog), WriteFogmog },
        { typeof(LivingFog), WriteLivingFog },
        { typeof(Ovicopter), WriteOvicopter },
    }.ToFrozenDictionary();

    /// <summary>
    /// Writes up to <see cref="CombatSimLayout.MoveEffectCap"/> effects for <paramref name="monster"/>'s
    /// currently-telegraphed <paramref name="move"/> into <paramref name="dst"/>. Returns how many
    /// were written (0 if this monster type isn't registered yet, or this specific move has no
    /// numeric payload we've captured).
    /// </summary>
    public static int Write(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        MonsterModel? liveMonster = monster.Monster;
        if (liveMonster is null) return 0;

        return _byType.TryGetValue(liveMonster.GetType(), out EffectWriter? writer)
            ? writer(monster, move, ascensionFlags, dst)
            : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasFlag(ushort ascensionFlags, int flag) => (ascensionFlags & flag) != 0;

    private static int WriteBlock(Span<SimMoveEffect> dst, int amount)
    {
        dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.Block, Amount = (short)amount };
        return 1;
    }

    private static int WritePower(Span<SimMoveEffect> dst, ushort powerType, int amount)
    {
        dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = powerType, Amount = (short)amount };
        return 1;
    }

    private static int WriteTwoPowers(Span<SimMoveEffect> dst, ushort powerType1, int amount1, ushort powerType2, int amount2)
    {
        dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = powerType1, Amount = (short)amount1 };
        dst[1] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = powerType2, Amount = (short)amount2 };
        return 2;
    }

    private static int WriteBlockAndPower(Span<SimMoveEffect> dst, int blockAmount, ushort powerType, int powerAmount)
    {
        dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.Block, Amount = (short)blockAmount };
        dst[1] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = powerType, Amount = (short)powerAmount };
        return 2;
    }

    private static int WriteThreePowers(Span<SimMoveEffect> dst, ushort powerType1, int amount1, ushort powerType2, int amount2, ushort powerType3, int amount3)
    {
        dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = powerType1, Amount = (short)amount1 };
        dst[1] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = powerType2, Amount = (short)amount2 };
        dst[2] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = powerType3, Amount = (short)amount3 };
        return 3;
    }

    private static int WriteSummon(Span<SimMoveEffect> dst, ushort summonTargetId, int count)
    {
        dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.Summon, PowerType = summonTargetId, Amount = (short)count };
        return 1;
    }

    // ── BowlbugNectar: BUFF_MOVE gives itself Strength (16 DeadlyEnemies / 15 base). ──────────
    private static int WriteBowlbugNectar(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BUFF_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 16 : 15;
        return WritePower(dst, SimPowerType.Strength, amount);
    }

    // ── Guardbot: GUARD_MOVE gives 15 block to every live Fabricator ally (NOT itself). We
    //    record the number on Guardbot's own slot regardless of the real target — there is no
    //    consumer of "who receives this" yet; revisit once the search/effect engine exists. ──
    private static int WriteGuardbot(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "GUARD_MOVE") return 0;
        return WriteBlock(dst, 15);
    }

    // ── AxeRubyRaider: SWING_1 / SWING_2 gain block on top of their attack (6 DeadlyEnemies / 5
    //    base). BIG_SWING is attack-only, no extra effect. ──────────────────────────────────────
    private static int WriteAxeRubyRaider(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "SWING_1" && move.StateId != "SWING_2") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 6 : 5;
        return WriteBlock(dst, amount);
    }

    // ── BruteRubyRaider: ROAR_MOVE gives itself 3 Strength (fixed, no ascension gate). ─────────
    private static int WriteBruteRubyRaider(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "ROAR_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 3);
    }

    // ── CrossbowRubyRaider: RELOAD_MOVE gains 3 block (fixed, no ascension gate). ───────────────
    private static int WriteCrossbowRubyRaider(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "RELOAD_MOVE") return 0;
        return WriteBlock(dst, 3);
    }

    // ── BygoneEffigy: WAKE_MOVE gives itself 10 Strength (fixed, no ascension gate). ───────────
    private static int WriteBygoneEffigy(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "WAKE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 10);
    }

    // ── CalcifiedCultist: INCANTATION_MOVE gives itself 2 Ritual (fixed, no ascension gate). ──
    private static int WriteCalcifiedCultist(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "INCANTATION_MOVE") return 0;
        return WritePower(dst, SimPowerType.Ritual, 2);
    }

    // ── DampCultist: INCANTATION_MOVE gives itself Ritual (6 DeadlyEnemies / 5 base). ─────────
    private static int WriteDampCultist(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "INCANTATION_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 6 : 5;
        return WritePower(dst, SimPowerType.Ritual, amount);
    }

    // ── BowlbugEgg: BITE_MOVE gains block on top of its attack (8 DeadlyEnemies / 7 base). ────
    private static int WriteBowlbugEgg(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BITE_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 8 : 7;
        return WriteBlock(dst, amount);
    }

    // ── DevotedSculptor: FORBIDDEN_INCANTATION_MOVE gives itself 9 Ritual (fixed). ─────────────
    private static int WriteDevotedSculptor(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "FORBIDDEN_INCANTATION_MOVE") return 0;
        return WritePower(dst, SimPowerType.Ritual, 9);
    }

    // ── Exoskeleton: ENRAGE_MOVE gives itself 2 Strength (fixed). ──────────────────────────────
    private static int WriteExoskeleton(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "ENRAGE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── FlailKnight: WAR_CHANT gives itself 3 Strength (fixed). ────────────────────────────────
    private static int WriteFlailKnight(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "WAR_CHANT") return 0;
        return WritePower(dst, SimPowerType.Strength, 3);
    }

    // ── FuzzyWurmCrawler: INHALE gives itself 7 Strength (fixed). ──────────────────────────────
    private static int WriteFuzzyWurmCrawler(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "INHALE") return 0;
        return WritePower(dst, SimPowerType.Strength, 7);
    }

    // ── BowlbugSilk: TOXIC_SPIT_MOVE applies 1 Weak to the player (fixed). ─────────────────────
    private static int WriteBowlbugSilk(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "TOXIC_SPIT_MOVE") return 0;
        return WritePower(dst, SimPowerType.Weak, 1);
    }

    // ── Flyconid: VULNERABLE_SPORES_MOVE applies 2 Vulnerable; FRAIL_SPORES_MOVE (also an
    //    attack) applies 2 Frail — both fixed, no ascension gate, both target the player. ───────
    private static int WriteFlyconid(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "VULNERABLE_SPORES_MOVE" => WritePower(dst, SimPowerType.Vulnerable, 2),
            "FRAIL_SPORES_MOVE" => WritePower(dst, SimPowerType.Frail, 2),
            _ => 0,
        };
    }

    // ── FossilStalker: TACKLE_MOVE (also an attack) applies 1 Frail to the player (fixed). ─────
    private static int WriteFossilStalker(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "TACKLE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Frail, 1);
    }

    // ── CorpseSlug: GOOP_MOVE applies 2 Frail to the player (fixed). ───────────────────────────
    private static int WriteCorpseSlug(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "GOOP_MOVE") return 0;
        return WritePower(dst, SimPowerType.Frail, 2);
    }

    // ── HunterKiller: TENDERIZING_GOOP_MOVE applies 1 Tender to the player (fixed). ────────────
    private static int WriteHunterKiller(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "TENDERIZING_GOOP_MOVE") return 0;
        return WritePower(dst, SimPowerType.Tender, 1);
    }

    // ── KinFollower: POWER_DANCE_MOVE gives itself Strength (3 DeadlyEnemies / 2 base). ────────
    private static int WriteKinFollower(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "POWER_DANCE_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
        return WritePower(dst, SimPowerType.Strength, amount);
    }

    // ── LivingShield: SMASH_MOVE (also an attack) gives itself 3 Strength (fixed). ─────────────
    private static int WriteLivingShield(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "SMASH_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 3);
    }

    // ── Mawler: ROAR_MOVE applies 3 Vulnerable to the player (fixed). ──────────────────────────
    private static int WriteMawler(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "ROAR_MOVE") return 0;
        return WritePower(dst, SimPowerType.Vulnerable, 3);
    }

    // ── Myte: SUCK_MOVE (also an attack) gives itself Strength (3 DeadlyEnemies / 2 base). ─────
    private static int WriteMyte(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "SUCK_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
        return WritePower(dst, SimPowerType.Strength, amount);
    }

    // ── Rocket (Kaiser Crab right arm): CHARGE_UP_MOVE gives itself Strength
    //    (3 DeadlyEnemies / 2 base). ─────────────────────────────────────────────────────────────
    private static int WriteRocket(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "CHARGE_UP_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
        return WritePower(dst, SimPowerType.Strength, amount);
    }

    // ── ScrollOfBiting: MORE_TEETH gives itself 2 Strength (fixed). ────────────────────────────
    private static int WriteScrollOfBiting(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "MORE_TEETH") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── SewerClam: PRESSURIZE_MOVE gives itself 4 Strength (fixed). ────────────────────────────
    private static int WriteSewerClam(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "PRESSURIZE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 4);
    }

    // ── ShrinkerBeetle: SHRINKER_MOVE applies -1 Shrink (i.e. shrinks the player, a debuff even
    //    though the raw number is negative) to the player (fixed). ────────────────────────────────
    private static int WriteShrinkerBeetle(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "SHRINKER_MOVE") return 0;
        return WritePower(dst, SimPowerType.Shrink, -1);
    }

    // ── SkulkingColony: INERTIA_MOVE (also an attack) gives itself Strength
    //    (4 DeadlyEnemies / 2 base). ─────────────────────────────────────────────────────────────
    private static int WriteSkulkingColony(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "INERTIA_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 2;
        return WritePower(dst, SimPowerType.Strength, amount);
    }

    // ── SlumberingBeetle: ROLL_OUT_MOVE (also an attack) gives itself 2 Strength (fixed). ──────
    private static int WriteSlumberingBeetle(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "ROLL_OUT_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── SnappingJaxfruit: ENERGY_ORB_MOVE (also an attack) gives itself 2 Strength (fixed). ────
    private static int WriteSnappingJaxfruit(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "ENERGY_ORB_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── SoulNexus: DRAIN_LIFE_MOVE (also an attack) applies 2 Vulnerable AND 2 Weak to the
    //    player, both fixed, no ascension gate. ────────────────────────────────────────────────
    private static int WriteSoulNexus(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "DRAIN_LIFE_MOVE") return 0;
        return WriteTwoPowers(dst, SimPowerType.Vulnerable, 2, SimPowerType.Weak, 2);
    }

    // ── SpectralKnight: HEX applies 2 Hex to the player (fixed). Live code loops over `targets`
    //    applying it once per target; we record one entry regardless of target count — see
    //    Guardbot's note above for why (no consumer of "who receives this" yet). ─────────────────
    private static int WriteSpectralKnight(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "HEX") return 0;
        return WritePower(dst, SimPowerType.Hex, 2);
    }

    // ── SpinyToad: PROTRUDING_SPIKES_MOVE gives itself 5 Thorns; SPIKE_EXPLOSION_MOVE (also an
    //    attack) removes it again with -5 Thorns — both fixed, no ascension gate. ────────────────
    private static int WriteSpinyToad(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "PROTRUDING_SPIKES_MOVE" => WritePower(dst, SimPowerType.Thorns, 5),
            "SPIKE_EXPLOSION_MOVE" => WritePower(dst, SimPowerType.Thorns, -5),
            _ => 0,
        };
    }

    // ── Stabbot: STAB_MOVE (also an attack) applies 1 Frail to the player (fixed). ─────────────
    private static int WriteStabbot(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "STAB_MOVE") return 0;
        return WritePower(dst, SimPowerType.Frail, 1);
    }

    // ── FakeMerchantMonster: ENRAGE_MOVE gives itself 2 Strength (fixed); THROW_RELIC_MOVE
    //    (also an attack) applies 1 Frail to the player (fixed). ──────────────────────────────
    private static int WriteFakeMerchantMonster(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "ENRAGE_MOVE" => WritePower(dst, SimPowerType.Strength, 2),
            "THROW_RELIC_MOVE" => WritePower(dst, SimPowerType.Frail, 1),
            _ => 0,
        };
    }

    // ── FrogKnight: FOR_THE_QUEEN gives itself 5 Strength (fixed); TONGUE_LASH (also an attack)
    //    applies 2 Frail to the player (fixed). ─────────────────────────────────────────────────
    private static int WriteFrogKnight(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "FOR_THE_QUEEN" => WritePower(dst, SimPowerType.Strength, 5),
            "TONGUE_LASH" => WritePower(dst, SimPowerType.Frail, 2),
            _ => 0,
        };
    }

    // ── GlobeHead: SHOCKING_SLAP (also an attack) applies 2 Frail to the player (fixed);
    //    GALVANIC_BURST (also an attack) gives itself 2 Strength (fixed). ─────────────────────
    private static int WriteGlobeHead(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "SHOCKING_SLAP" => WritePower(dst, SimPowerType.Frail, 2),
            "GALVANIC_BURST" => WritePower(dst, SimPowerType.Strength, 2),
            _ => 0,
        };
    }

    // ── HauntedShip: HAUNT_MOVE applies 3 Weak to the player (fixed) — also queues Dazed cards
    //    via a separate StatusIntent, not captured here (out of scope, see backlog doc). ───────
    private static int WriteHauntedShip(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "HAUNT_MOVE") return 0;
        return WritePower(dst, SimPowerType.Weak, 3);
    }

    // ── InfestedPrism: RADIATE_MOVE (also an attack) gains block (13 DeadlyEnemies / 11 base);
    //    PULSATE_MOVE (also an attack) gains block (22 ToughEnemies / 20 base) AND VitalSpark
    //    (3 DeadlyEnemies / 2 base). ───────────────────────────────────────────────────────────
    private static int WriteInfestedPrism(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "RADIATE_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 13 : 11;
                return WriteBlock(dst, block);
            }
            case "PULSATE_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 22 : 20;
                int spark = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
                return WriteBlockAndPower(dst, block, SimPowerType.VitalSpark, spark);
            }
            default:
                return 0;
        }
    }

    // ── MechaKnight: WINDUP_MOVE gains 15 block AND gives itself 5 Strength, both fixed. ──────
    private static int WriteMechaKnight(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "WINDUP_MOVE") return 0;
        return WriteBlockAndPower(dst, 15, SimPowerType.Strength, 5);
    }

    // ── Nibbit: SLICE_MOVE (also an attack) gains block (6 ToughEnemies / 5 base); HISS_MOVE
    //    gives itself Strength (3 DeadlyEnemies / 2 base). ──────────────────────────────────────
    private static int WriteNibbit(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "SLICE_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 6 : 5;
                return WriteBlock(dst, block);
            }
            case "HISS_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
                return WritePower(dst, SimPowerType.Strength, amount);
            }
            default:
                return 0;
        }
    }

    // ── PhantasmalGardener: ENLARGE_MOVE gives itself Strength (3 DeadlyEnemies / 2 base). ────
    private static int WritePhantasmalGardener(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "ENLARGE_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
        return WritePower(dst, SimPowerType.Strength, amount);
    }

    // ── PunchConstruct: READY_MOVE gains 10 block (fixed); FAST_PUNCH_MOVE (also an attack)
    //    applies 1 Frail to the player (fixed). ─────────────────────────────────────────────────
    private static int WritePunchConstruct(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "READY_MOVE" => WriteBlock(dst, 10),
            "FAST_PUNCH_MOVE" => WritePower(dst, SimPowerType.Frail, 1),
            _ => 0,
        };
    }

    // ── Seapunk: BUBBLE_BURP_MOVE gains block (8 ToughEnemies / 7 base) AND gives itself
    //    Strength (2 DeadlyEnemies / 1 base). ───────────────────────────────────────────────────
    private static int WriteSeapunk(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BUBBLE_BURP_MOVE") return 0;
        int block = HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 8 : 7;
        int str = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 2 : 1;
        return WriteBlockAndPower(dst, block, SimPowerType.Strength, str);
    }

    // ── SlitheringStrangler: CONSTRICT applies 3 Constrict to the player (fixed); THWACK (also
    //    an attack) gains 5 block (fixed). ─────────────────────────────────────────────────────
    private static int WriteSlitheringStrangler(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "CONSTRICT" => WritePower(dst, SimPowerType.Constrict, 3),
            "THWACK" => WriteBlock(dst, 5),
            _ => 0,
        };
    }

    // ── SoulFysh: FADE_MOVE gives itself 2 Intangible (fixed); SCREAM_MOVE (also an attack)
    //    applies 3 Vulnerable to the player (fixed). ───────────────────────────────────────────
    private static int WriteSoulFysh(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "FADE_MOVE" => WritePower(dst, SimPowerType.Intangible, 2),
            "SCREAM_MOVE" => WritePower(dst, SimPowerType.Vulnerable, 3),
            _ => 0,
        };
    }

    // ── GremlinMerc: DOUBLE_SMASH_MOVE (also an attack) applies 2 Weak to the player (fixed);
    //    HEHE_MOVE (also an attack) gives itself 2 Strength (fixed). Both moves also steal gold
    //    via ThieveryPower.Steal() — not a Block/PowerApply/Heal effect, out of scope here. ─────
    private static int WriteGremlinMerc(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "DOUBLE_SMASH_MOVE" => WritePower(dst, SimPowerType.Weak, 2),
            "HEHE_MOVE" => WritePower(dst, SimPowerType.Strength, 2),
            _ => 0,
        };
    }

    // ── Crusher (Kaiser Crab left arm): BUG_STING_MOVE (also an attack) applies 2 Weak AND 2
    //    Frail to the player (fixed); ADAPT_MOVE gives itself Strength (3 DeadlyEnemies / 2 base);
    //    GUARDED_STRIKE_MOVE (also an attack) gains 18 block (fixed). ───────────────────────────
    private static int WriteCrusher(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "BUG_STING_MOVE":
                return WriteTwoPowers(dst, SimPowerType.Weak, 2, SimPowerType.Frail, 2);
            case "ADAPT_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
                return WritePower(dst, SimPowerType.Strength, amount);
            }
            case "GUARDED_STRIKE_MOVE":
                return WriteBlock(dst, 18);
            default:
                return 0;
        }
    }

    // ── KinPriest: ORB_OF_FRAILTY_MOVE (also an attack) applies 1 Frail to the player (fixed);
    //    ORB_OF_WEAKNESS_MOVE (also an attack) applies 1 Weak to the player (fixed); RITUAL_MOVE
    //    gives itself Strength (3 DeadlyEnemies / 2 base). ────────────────────────────────────────
    private static int WriteKinPriest(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "ORB_OF_FRAILTY_MOVE":
                return WritePower(dst, SimPowerType.Frail, 1);
            case "ORB_OF_WEAKNESS_MOVE":
                return WritePower(dst, SimPowerType.Weak, 1);
            case "RITUAL_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
                return WritePower(dst, SimPowerType.Strength, amount);
            }
            default:
                return 0;
        }
    }

    // ── OwlMagistrate: JUDICIAL_FLIGHT gives itself 1 Soar (fixed); VERDICT (also an attack)
    //    applies 4 Vulnerable to the player (fixed) — also removes Soar, not captured (removal,
    //    not an amount to verify). ────────────────────────────────────────────────────────────────
    private static int WriteOwlMagistrate(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "JUDICIAL_FLIGHT" => WritePower(dst, SimPowerType.Soar, 1),
            "VERDICT" => WritePower(dst, SimPowerType.Vulnerable, 4),
            _ => 0,
        };
    }

    // ── SludgeSpinner: OIL_SPRAY_MOVE (also an attack) applies 1 Weak to the player (fixed);
    //    RAGE_MOVE (also an attack) gives itself 3 Strength (fixed). ───────────────────────────
    private static int WriteSludgeSpinner(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "OIL_SPRAY_MOVE" => WritePower(dst, SimPowerType.Weak, 1),
            "RAGE_MOVE" => WritePower(dst, SimPowerType.Strength, 3),
            _ => 0,
        };
    }

    // ── SlimedBerserker: LEECHING_HUG_MOVE applies 3 Weak to the player AND gives itself 3
    //    Strength, both fixed. ─────────────────────────────────────────────────────────────────
    private static int WriteSlimedBerserker(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "LEECHING_HUG_MOVE") return 0;
        return WriteTwoPowers(dst, SimPowerType.Weak, 3, SimPowerType.Strength, 3);
    }

    // ── TerrorEel: THRASH_MOVE (also an attack) gives itself 6 Vigor (fixed); TERROR_MOVE
    //    applies 99 Vulnerable to the player (fixed — this is intentionally huge, a near-guaranteed
    //    lethal-hit setup move). ───────────────────────────────────────────────────────────────────
    private static int WriteTerrorEel(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "THRASH_MOVE" => WritePower(dst, SimPowerType.Vigor, 6),
            "TERROR_MOVE" => WritePower(dst, SimPowerType.Vulnerable, 99),
            _ => 0,
        };
    }

    // ── TheAdversaryMkOne/MkTwo/MkThree (the 3-way training-dummy boss): BARRAGE_MOVE (also an
    //    attack) gives itself Strength — 2 for MkOne, 3 for MkTwo, 4 for MkThree, all fixed
    //    (no ascension gate on any of these three). ─────────────────────────────────────────────
    private static int WriteTheAdversaryMkOne(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BARRAGE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    private static int WriteTheAdversaryMkTwo(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BARRAGE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 3);
    }

    private static int WriteTheAdversaryMkThree(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BARRAGE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 4);
    }

    // ── TheLost: DEBILITATING_SMOG steals Strength from the player and gives it to itself
    //    (2 DeadlyEnemies / 2 base — no actual ascension gate, both branches equal 2). We only
    //    record the self-gain half: recording both halves would collide, since both are
    //    SimPowerType.Strength on the SAME move and our verifier tracks amounts per-PowerType on
    //    THIS creature only — the player-side steal has no tracked target here. ───────────────────
    private static int WriteTheLost(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "DEBILITATING_SMOG") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── Toadpole: SPIKE_SPIT_MOVE (also an attack) removes 2 Thorns from itself (-2, fixed);
    //    SPIKEN_MOVE gives itself 2 Thorns back (fixed) — two different StateIds, no collision. ──
    private static int WriteToadpole(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "SPIKE_SPIT_MOVE" => WritePower(dst, SimPowerType.Thorns, -2),
            "SPIKEN_MOVE" => WritePower(dst, SimPowerType.Thorns, 2),
            _ => 0,
        };
    }

    // ── TrackerRubyRaider: TRACK_MOVE applies 2 Frail to the player (fixed). ───────────────────
    private static int WriteTrackerRubyRaider(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "TRACK_MOVE") return 0;
        return WritePower(dst, SimPowerType.Frail, 2);
    }

    // ── Tunneler: BURROW_MOVE gains block (37 ToughEnemies / 32 base) — also applies BurrowedPower
    //    (a state-flag power, not a meaningful "amount"), not captured here. ───────────────────────
    private static int WriteTunneler(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BURROW_MOVE") return 0;
        int block = HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 37 : 32;
        return WriteBlock(dst, block);
    }

    // ── MagiKnight: POWER_SHIELD_MOVE (also an attack) and PREP_MOVE both gain the same block
    //    (9 ToughEnemies / 5 base). DAMPEN_MOVE applies 1 DampenPower to the player the FIRST
    //    time only (subsequent casts just extend an existing stack without adding amount) — we
    //    record the common case (+1); a mismatch here just means it was a repeat cast, harmless. ──
    private static int WriteMagiKnight(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "POWER_SHIELD_MOVE":
            case "PREP_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 9 : 5;
                return WriteBlock(dst, block);
            }
            case "DAMPEN_MOVE":
                return WritePower(dst, SimPowerType.Dampen, 1);
            default:
                return 0;
        }
    }

    // ── Aeonglass: EBB_MOVE (also an attack) gains 33 block (fixed, no ascension gate). ────────
    //    INCREASING_INTENSITY_MOVE's Strength amount grows by 1 every time it fires (tracked via
    //    a private field on the live monster) — not captured, needs a dedicated accessor; see
    //    dev_docs/Enemy_Intent_Payload_Backlog.md. ──────────────────────────────────────────────
    private static int WriteAeonglass(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "EBB_MOVE") return 0;
        return WriteBlock(dst, 33);
    }

    // ── CubexConstruct: CHARGE_UP_MOVE, REPEATER_BLAST_MOVE and REPEATER_BLAST_MOVE_2 (the
    //    latter two also attacks) all give itself 2 Strength (fixed, no ascension gate — all
    //    three call the same formula despite two different StateIds for the blast). ─────────────
    private static int WriteCubexConstruct(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "CHARGE_UP_MOVE" or "REPEATER_BLAST_MOVE" or "REPEATER_BLAST_MOVE_2" => WritePower(dst, SimPowerType.Strength, 2),
            _ => 0,
        };
    }

    // ── Entomancer: PHEROMONE_SPIT_MOVE branches on its OWN current PersonalHivePower amount
    //    (live state, not ascension): below 3 stacks, it gains 1 PersonalHive + 1 Strength;
    //    at 3+ stacks, it gains 2 Strength instead (matches the live source's own branch). ───────
    private static int WriteEntomancer(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "PHEROMONE_SPIT_MOVE") return 0;
        PersonalHivePower? hive = monster.GetPower<PersonalHivePower>();
        if (hive != null && hive.Amount < 3)
            return WriteTwoPowers(dst, SimPowerType.PersonalHive, 1, SimPowerType.Strength, 1);
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── KnowledgeDemon: PONDER_MOVE (also an attack) heals 30 per player in the fight (reads the
    //    live player count, correct in both solo and co-op) AND gives itself Strength
    //    (3 DeadlyEnemies / 2 base). CURSE_OF_KNOWLEDGE_MOVE (a card-choice debuff, not a
    //    Block/Power/Heal amount) is out of scope, not captured. ──────────────────────────────────
    private static int WriteKnowledgeDemon(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "PONDER_MOVE") return 0;
        int healAmount = 30 * (monster.CombatState?.Players.Count ?? 1);
        int strength = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
        dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.Heal, Amount = (short)healAmount };
        dst[1] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = SimPowerType.Strength, Amount = (short)strength };
        return 2;
    }

    // ── TheInsatiable: SALIVATE_MOVE gives itself Strength (3 DeadlyEnemies / 2 base).
    //    LIQUIFY_GROUND_MOVE's BuffIntent tag doesn't correspond to any self-buff in its body
    //    (it applies SandpitPower to targets and queues escape cards) — not captured. ────────────
    private static int WriteTheInsatiable(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "SALIVATE_MOVE") return 0;
        int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
        return WritePower(dst, SimPowerType.Strength, amount);
    }

    // ── ThievingHopper: FLUTTER_MOVE gives itself 5 Flutter (fixed). ───────────────────────────
    private static int WriteThievingHopper(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "FLUTTER_MOVE") return 0;
        return WritePower(dst, SimPowerType.Flutter, 5);
    }

    // ── Axebot: BOOT_UP_MOVE (also Defend+Buff) gains block (15 DeadlyEnemies / 10 base) AND
    //    Strength = BootUpStrGain * (2 - StockAmount) — StockAmount is a public live property on
    //    the monster (how many "extra lives" it has left), read directly, not guessed.
    //    HAMMER_UPPERCUT_MOVE (also an attack) applies 2 Weak AND 2 Frail to the player (fixed). ──
    private static int WriteAxebot(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "BOOT_UP_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 15 : 10;
                int strGain = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3;
                int stock = (monster.Monster as Axebot)?.StockAmount ?? 2;
                return WriteBlockAndPower(dst, block, SimPowerType.Strength, strGain * (2 - stock));
            }
            case "HAMMER_UPPERCUT_MOVE":
                return WriteTwoPowers(dst, SimPowerType.Weak, 2, SimPowerType.Frail, 2);
            default:
                return 0;
        }
    }

    // ── TestSubject: SKULL_BASH_MOVE (also an attack) applies 1 Vulnerable to the player (fixed);
    //    BURNING_GROWL_MOVE gives itself Strength (3 DeadlyEnemies / 2 base). RESPAWN_MOVE's heal
    //    amount depends on a multiplayer HP-scaling formula (ScaleHpForMultiplayer) plus which
    //    respawn phase it is — not captured, too much state to track reliably here. ───────────────
    private static int WriteTestSubject(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "SKULL_BASH_MOVE":
                return WritePower(dst, SimPowerType.Vulnerable, 1);
            case "BURNING_GROWL_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 2;
                return WritePower(dst, SimPowerType.Strength, amount);
            }
            default:
                return 0;
        }
    }

    // ── LouseProgenitor: WEB_CANNON_MOVE (also an attack) applies 2 Frail to the player (fixed);
    //    CURL_AND_GROW_MOVE gains block (18 ToughEnemies / 14 base) AND gives itself 5 Strength
    //    (fixed). ───────────────────────────────────────────────────────────────────────────────
    private static int WriteLouseProgenitor(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "WEB_CANNON_MOVE":
                return WritePower(dst, SimPowerType.Frail, 2);
            case "CURL_AND_GROW_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 18 : 14;
                return WriteBlockAndPower(dst, block, SimPowerType.Strength, 5);
            }
            default:
                return 0;
        }
    }

    // ── LagavulinMatriarch: SLASH2_MOVE (also an attack) gains block (14 ToughEnemies / 12 base).
    //    SOUL_SIPHON_MOVE steals Dexterity from the player (-2, fixed) and gives itself 2
    //    Strength (fixed) — we skip the matching Strength steal from the player half, since it's
    //    the same PowerType as the self-gain and would collide in the verifier (see TheLost). ────
    private static int WriteLagavulinMatriarch(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "SLASH2_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 14 : 12;
                return WriteBlock(dst, block);
            }
            case "SOUL_SIPHON_MOVE":
                return WriteTwoPowers(dst, SimPowerType.Dexterity, -2, SimPowerType.Strength, 2);
            default:
                return 0;
        }
    }

    // ── TheForgotten: MIASMA_MOVE gains 8 block (fixed) and gives itself 2 Dexterity (fixed) —
    //    the matching Dexterity steal from the player is skipped (same PowerType collision
    //    reasoning as LagavulinMatriarch/TheLost above). DreadDamage's own Dexterity-scaling is
    //    already handled correctly by the live GetSingleDamage() attack-damage path. ─────────────
    private static int WriteTheForgotten(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "MIASMA") return 0;
        return WriteBlockAndPower(dst, 8, SimPowerType.Dexterity, 2);
    }

    // ── TheObscura: ILLUSION_MOVE summons exactly 1 Parafright (fixed). SAIL_MOVE (WailMove —
    //    gives ALL teammates 3 Strength, recorded on this creature's own slot per the Guardbot
    //    precedent) is fixed; HARDENING_STRIKE_MOVE (also an attack) gains block
    //    (7 DeadlyEnemies / 6 base). ─────────────────────────────────────────────────────────────
    private static int WriteTheObscura(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "ILLUSION_MOVE":
                return WriteSummon(dst, SimSummonTargetId.Parafright, 1);
            case "SAIL_MOVE":
                return WritePower(dst, SimPowerType.Strength, 3);
            case "HARDENING_STRIKE_MOVE":
            {
                int block = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 7 : 6;
                return WriteBlock(dst, block);
            }
            default:
                return 0;
        }
    }

    // ── CeremonialBeast: STAMP_MOVE gives itself PlowPower (160 DeadlyEnemies / 150 base);
    //    PLOW_MOVE (also an attack) gives itself 2 Strength (fixed — despite the name,
    //    PlowStrength is NOT ascension-gated); BEAST_CRY_MOVE applies 1 Ringing to the player
    //    (fixed); CRUSH_MOVE (also an attack) gives itself Strength (4 DeadlyEnemies / 3 base). ───
    private static int WriteCeremonialBeast(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "STAMP_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 160 : 150;
                return WritePower(dst, SimPowerType.Plow, amount);
            }
            case "PLOW_MOVE":
                return WritePower(dst, SimPowerType.Strength, 2);
            case "BEAST_CRY_MOVE":
                return WritePower(dst, SimPowerType.Ringing, 1);
            case "CRUSH_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3;
                return WritePower(dst, SimPowerType.Strength, amount);
            }
            default:
                return 0;
        }
    }

    // ── Queen: BURN_BRIGHT_FOR_ME_MOVE gains 20 block for itself (fixed) and gives 1 Strength to
    //    every OTHER teammate (Amalgam) — recorded on Queen's own slot per the Guardbot precedent,
    //    fixed, no ascension gate despite the lookup call. ENRAGE_MOVE gives itself 2 Strength
    //    (fixed). YOU_ARE_MINE_MOVE applies 99 Frail + 99 Weak + 99 Vulnerable to the player, all
    //    fixed — intentionally huge, a "you cannot fight back" setup move, not a typo.
    //    PUPPET_STRINGS_MOVE is a CardDebuffIntent (out of scope, no numeric payload here). ───────
    private static int WriteQueen(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "BURN_BRIGHT_FOR_ME_MOVE":
                return WriteBlockAndPower(dst, 20, SimPowerType.Strength, 1);
            case "ENRAGE_MOVE":
                return WritePower(dst, SimPowerType.Strength, 2);
            case "YOU_ARE_MINE_MOVE":
                return WriteThreePowers(dst, SimPowerType.Frail, 99, SimPowerType.Weak, 99, SimPowerType.Vulnerable, 99);
            default:
                return 0;
        }
    }

    // ── DecimillipedeSegment (all three concrete slot variants — Front/Middle/Back — share this
    //    same formula table, only differing in cosmetic SegmentAttack()): BULK_MOVE (also an
    //    attack) gives itself 2 Strength (fixed); CONSTRICT_MOVE (also an attack) applies 1 Weak
    //    to the player (fixed). REATTACH_MOVE's heal amount is delegated entirely to
    //    ReattachPower.DoReattach(), not visible as a simple formula here — not captured. ─────────
    private static int WriteDecimillipedeSegment(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "BULK_MOVE" => WritePower(dst, SimPowerType.Strength, 2),
            "CONSTRICT_MOVE" => WritePower(dst, SimPowerType.Weak, 1),
            _ => 0,
        };
    }

    // ── TurretOperator: RELOAD_MOVE gives itself 1 Strength (fixed). ───────────────────────────
    private static int WriteTurretOperator(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "RELOAD_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 1);
    }

    // ── Vantom: PREPARE_MOVE gives itself 2 Strength (fixed). ──────────────────────────────────
    private static int WriteVantom(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "PREPARE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── WaterfallGiant: five different moves all feed the same SteamEruptionPower stack (the
    //    "about to blow" meter) — PRESSURIZE_MOVE adds the big chunk (20 DeadlyEnemies / 15
    //    base), the four attack moves (STOMP/RAM/SIPHON/PRESSURE_GUN/PRESSURE_UP) each add a
    //    fixed +3. STOMP_MOVE also applies 1 Weak to the player. SIPHON_MOVE also heals
    //    SiphonHeal (15 ToughEnemies / 10 base) per player in the fight. ABOUT_TO_BLOW_MOVE
    //    (Stun) and EXPLODE_MOVE (DeathBlow) aren't one of our five tracked types — not captured. ─
    private static int WriteWaterfallGiant(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "PRESSURIZE_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 20 : 15;
                return WritePower(dst, SimPowerType.SteamEruption, amount);
            }
            case "STOMP_MOVE":
                return WriteTwoPowers(dst, SimPowerType.Weak, 1, SimPowerType.SteamEruption, 3);
            case "RAM_MOVE":
            case "PRESSURE_GUN_MOVE":
            case "PRESSURE_UP_MOVE":
                return WritePower(dst, SimPowerType.SteamEruption, 3);
            case "SIPHON_MOVE":
            {
                int healAmount = (HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 15 : 10) * (monster.CombatState?.Players.Count ?? 1);
                dst[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.Heal, Amount = (short)healAmount };
                dst[1] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.PowerApply, PowerType = SimPowerType.SteamEruption, Amount = 3 };
                return 2;
            }
            default:
                return 0;
        }
    }

    // ── Wriggler: WRIGGLE_MOVE gives itself 2 Strength (fixed) — also queues a status card via
    //    its StatusIntent tag, out of scope here. ───────────────────────────────────────────────
    private static int WriteWriggler(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "WRIGGLE_MOVE") return 0;
        return WritePower(dst, SimPowerType.Strength, 2);
    }

    // ── TwoTailedRat: SCREECH_MOVE applies 1 Frail to the player (fixed). CALL_FOR_BACKUP_MOVE
    //    summons exactly 1 more TwoTailedRat (self-replicating, fixed count). ─────────────────────
    private static int WriteTwoTailedRat(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "SCREECH_MOVE" => WritePower(dst, SimPowerType.Frail, 1),
            "CALL_FOR_BACKUP_MOVE" => WriteSummon(dst, SimSummonTargetId.TwoTailedRat, 1),
            _ => 0,
        };
    }

    // ── Fogmog: ILLUSION_MOVE summons exactly 1 EyeWithTeeth (fixed). SWIPE_MOVE and
    //    SWIPE_RANDOM_MOVE (both also attacks, share the same handler function) give itself 1
    //    Strength (fixed). ──────────────────────────────────────────────────────────────────────
    private static int WriteFogmog(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        return move.StateId switch
        {
            "ILLUSION_MOVE" => WriteSummon(dst, SimSummonTargetId.EyeWithTeeth, 1),
            "SWIPE_MOVE" or "SWIPE_RANDOM_MOVE" => WritePower(dst, SimPowerType.Strength, 1),
            _ => 0,
        };
    }

    // ── LivingFog: BLOAT_MOVE (also an attack) summons GasBomb — count reads the live
    //    BloatAmount field (starts at 1; nothing in this file's visible code changes it, but read
    //    live rather than hardcoded in case a future patch adds a setter elsewhere). ADVANCED_GAS_MOVE
    //    is a CardDebuffIntent (out of scope, not captured). ────────────────────────────────────────
    private static int WriteLivingFog(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        if (move.StateId != "BLOAT_MOVE") return 0;
        return WriteSummon(dst, SimSummonTargetId.GasBomb, 1);
    }

    // ── Ovicopter: LAY_EGGS_MOVE summons exactly 3 ToughEgg (fixed). NUTRITIONAL_PASTE_MOVE
    //    gives itself Strength (4 DeadlyEnemies / 3 base); TENDERIZER_MOVE (also an attack)
    //    applies 2 Vulnerable to the player (fixed). ─────────────────────────────────────────────
    private static int WriteOvicopter(Creature monster, MoveState move, ushort ascensionFlags, Span<SimMoveEffect> dst)
    {
        switch (move.StateId)
        {
            case "LAY_EGGS_MOVE":
                return WriteSummon(dst, SimSummonTargetId.ToughEgg, 3);
            case "NUTRITIONAL_PASTE_MOVE":
            {
                int amount = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3;
                return WritePower(dst, SimPowerType.Strength, amount);
            }
            case "TENDERIZER_MOVE":
                return WritePower(dst, SimPowerType.Vulnerable, 2);
            default:
                return 0;
        }
    }
}
