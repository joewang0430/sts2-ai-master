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
/// Hand-written per-monster registry resolving an Attack/DeathBlow-classified move's raw per-hit
/// damage and hit count from just <c>(Type monsterType, string stateId, ushort ascensionFlags)</c> —
/// no live <see cref="Creature"/>/<see cref="MoveState"/> needed. This is the sibling of
/// <see cref="SimMonsterMoveEffects"/> (which covers Block/Buff/Debuff/Heal/Summon/CardInject
/// instead), built the same way: hand-replicate each monster's own <c>AttackIntent</c> construction
/// from its game_source file (<c>SingleAttackIntent(int)</c>/<c>SingleAttackIntent(Func&lt;decimal&gt;)</c>,
/// <c>MultiAttackIntent(int,int)</c>/<c>MultiAttackIntent(int,Func&lt;int&gt;)</c>,
/// <c>DeathBlowIntent(Func&lt;decimal&gt;)</c>), using OUR captured <see cref="SimAscension"/>
/// bitmask instead of calling back into the live game's RunManager.
///
/// Exists for forward search: the DFS engine needs "if this enemy's currently-telegraphed move
/// executes, what does it telegraph NEXT, and how much raw damage will THAT move do" — a
/// hypothetical future <c>MoveState</c> that has no live <see cref="Creature"/> backing it yet (see
/// <see cref="SimMonsterStateRegistry"/>'s <c>MonsterStateTable.IntentClass</c>, which already
/// resolves Attack vs DeathBlow vs other intent CLASSES from Type+stateId alone — this registry is
/// only the numeric payload for the states that classify as Attack/DeathBlow).
///
/// <c>RawDamage</c> matches <see cref="CombatNodeBlobSnapshot"/>'s <c>RawAttackDamage</c> semantics
/// exactly: the raw <c>AttackIntent.DamageCalc()</c> value, BEFORE Strength/Weak/Vulnerable/Cap.
/// <c>Hits</c> matches <c>AttackIntent.Repeats</c> directly — already the TOTAL hit count (a plain
/// single-hit attack is 1, not 0 or 2; see <c>CombatNodeBlobSnapshot.AttackHits</c>'s doc comment for
/// the off-by-one this project already hit and fixed once).
///
/// <b>This registry is intentionally partial and grows incrementally</b>, same as
/// <see cref="SimMonsterMoveEffects"/>. BUT unlike that registry (where "not found" harmlessly
/// defaults to 0 effects), <see cref="TryResolve(Creature,MoveState,ushort,out Resolved)"/> /
/// <see cref="TryResolve(Type,string,ushort,out Resolved)"/> return <c>false</c> — not a 0-damage
/// default — when the monster Type isn't registered, or the specific stateId isn't a registered
/// Attack/DeathBlow state for it. A silent 0 for an actual upcoming attack would be a silently-wrong
/// search input, worse than no answer at all. <b>Do not "fix" a false result into a 0-damage
/// default</b> — that decision belongs to the (separate, not-yet-written) caller that consumes this,
/// which can choose to fail loud. This file's only job is "known value, or honestly say unknown."
///
/// The handful of monsters whose damage genuinely depends on live mutable creature state (not just
/// ascension) take the optional <c>Creature? monster</c> parameter, mirroring
/// <see cref="SimMonsterMoveEffects.EffectWriter"/>'s established pattern — see each such function's
/// own comment for its documented null-fallback. Two cases from this pass turned out to need MORE
/// than a null-fallback can honestly provide (a private counter/field with no public accessor and no
/// blob field to read it from) — those specific states are deliberately left unregistered; see
/// dev_docs/Enemy_Intent_Payload_Backlog.md for the exact reasoning, not guessed here.
/// </summary>
internal static class SimMonsterAttackDb
{
    public readonly record struct Resolved(ushort RawDamage, byte Hits);

    public delegate bool AttackResolver(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result);

    private static readonly FrozenDictionary<Type, AttackResolver> _byType = new Dictionary<Type, AttackResolver>
    {
        { typeof(AssassinRubyRaider), WriteAssassinRubyRaider },
        { typeof(AxeRubyRaider), WriteAxeRubyRaider },
        { typeof(Axebot), WriteAxebot },
        { typeof(BruteRubyRaider), WriteBruteRubyRaider },
        { typeof(CrossbowRubyRaider), WriteCrossbowRubyRaider },
        { typeof(TrackerRubyRaider), WriteTrackerRubyRaider },
        { typeof(BowlbugRock), WriteBowlbugRock },
        { typeof(BowlbugEgg), WriteBowlbugEgg },
        { typeof(BowlbugSilk), WriteBowlbugSilk },
        { typeof(BowlbugNectar), WriteBowlbugNectar },
        { typeof(CalcifiedCultist), WriteCalcifiedCultist },
        { typeof(DampCultist), WriteDampCultist },
        { typeof(Aeonglass), WriteAeonglass },
        { typeof(BygoneEffigy), WriteBygoneEffigy },
        { typeof(Chomper), WriteChomper },
        { typeof(CorpseSlug), WriteCorpseSlug },
        { typeof(CeremonialBeast), WriteCeremonialBeast },
        { typeof(Crusher), WriteCrusher },
        { typeof(CubexConstruct), WriteCubexConstruct },
        { typeof(DecimillipedeSegmentBack), WriteDecimillipedeSegment },
        { typeof(DecimillipedeSegmentFront), WriteDecimillipedeSegment },
        { typeof(DecimillipedeSegmentMiddle), WriteDecimillipedeSegment },
        { typeof(DevotedSculptor), WriteDevotedSculptor },
        { typeof(Entomancer), WriteEntomancer },
        { typeof(Exoskeleton), WriteExoskeleton },
        { typeof(Fabricator), WriteFabricator },
        { typeof(FakeMerchantMonster), WriteFakeMerchantMonster },
        { typeof(FlailKnight), WriteFlailKnight },
        { typeof(Flyconid), WriteFlyconid },
        { typeof(Fogmog), WriteFogmog },
        { typeof(FossilStalker), WriteFossilStalker },
        { typeof(FrogKnight), WriteFrogKnight },
        { typeof(FuzzyWurmCrawler), WriteFuzzyWurmCrawler },
        { typeof(GasBomb), WriteGasBomb },
        { typeof(GlobeHead), WriteGlobeHead },
        { typeof(GremlinMerc), WriteGremlinMerc },
        { typeof(HauntedShip), WriteHauntedShip },
        { typeof(HunterKiller), WriteHunterKiller },
        { typeof(InfestedPrism), WriteInfestedPrism },
        { typeof(Inklet), WriteInklet },
        { typeof(KinFollower), WriteKinFollower },
        { typeof(KinPriest), WriteKinPriest },
        { typeof(KnowledgeDemon), WriteKnowledgeDemon },
        { typeof(LagavulinMatriarch), WriteLagavulinMatriarch },
        { typeof(LeafSlimeM), WriteLeafSlimeM },
        { typeof(LeafSlimeS), WriteLeafSlimeS },
        { typeof(LivingFog), WriteLivingFog },
        { typeof(LivingShield), WriteLivingShield },
        { typeof(LouseProgenitor), WriteLouseProgenitor },
        { typeof(MagiKnight), WriteMagiKnight },
        { typeof(Mawler), WriteMawler },
        { typeof(MechaKnight), WriteMechaKnight },
        { typeof(Myte), WriteMyte },
        { typeof(Nibbit), WriteNibbit },
        { typeof(Ovicopter), WriteOvicopter },
        { typeof(OwlMagistrate), WriteOwlMagistrate },
        { typeof(Parafright), WriteParafright },
        { typeof(PhantasmalGardener), WritePhantasmalGardener },
        { typeof(PhrogParasite), WritePhrogParasite },
        { typeof(PunchConstruct), WritePunchConstruct },
        { typeof(Queen), WriteQueen },
        { typeof(Rocket), WriteRocket },
        { typeof(ScrollOfBiting), WriteScrollOfBiting },
        { typeof(Seapunk), WriteSeapunk },
        { typeof(SewerClam), WriteSewerClam },
        { typeof(ShrinkerBeetle), WriteShrinkerBeetle },
        { typeof(SkulkingColony), WriteSkulkingColony },
        { typeof(SlimedBerserker), WriteSlimedBerserker },
        { typeof(SlitheringStrangler), WriteSlitheringStrangler },
        { typeof(SludgeSpinner), WriteSludgeSpinner },
        { typeof(SlumberingBeetle), WriteSlumberingBeetle },
        { typeof(SnappingJaxfruit), WriteSnappingJaxfruit },
        { typeof(SneakyGremlin), WriteSneakyGremlin },
        { typeof(SoulFysh), WriteSoulFysh },
        { typeof(SoulNexus), WriteSoulNexus },
        { typeof(SpectralKnight), WriteSpectralKnight },
        { typeof(SpinyToad), WriteSpinyToad },
        { typeof(Stabbot), WriteStabbot },
        { typeof(TerrorEel), WriteTerrorEel },
        { typeof(TestSubject), WriteTestSubject },
        { typeof(TheAdversaryMkOne), WriteTheAdversaryMkOne },
        { typeof(TheAdversaryMkTwo), WriteTheAdversaryMkTwo },
        { typeof(TheAdversaryMkThree), WriteTheAdversaryMkThree },
        { typeof(TheForgotten), WriteTheForgotten },
        { typeof(TheInsatiable), WriteTheInsatiable },
        { typeof(TheLost), WriteTheLost },
        { typeof(TheObscura), WriteTheObscura },
        { typeof(ThievingHopper), WriteThievingHopper },
        { typeof(Toadpole), WriteToadpole },
        { typeof(TorchHeadAmalgam), WriteTorchHeadAmalgam },
        { typeof(ToughEgg), WriteToughEgg },
        { typeof(Tunneler), WriteTunneler },
        { typeof(TurretOperator), WriteTurretOperator },
        { typeof(TwigSlimeM), WriteTwigSlimeM },
        { typeof(TwigSlimeS), WriteTwigSlimeS },
        { typeof(TwoTailedRat), WriteTwoTailedRat },
        { typeof(Vantom), WriteVantom },
        { typeof(VineShambler), WriteVineShambler },
        { typeof(WaterfallGiant), WriteWaterfallGiant },
        { typeof(Wriggler), WriteWriggler },
        { typeof(Zapbot), WriteZapbot },
        { typeof(Byrdonis), WriteByrdonis },
        { typeof(SingleAttackMoveMonster), WriteSingleAttackMoveMonster },
        { typeof(MultiAttackMoveMonster), WriteMultiAttackMoveMonster },
    }.ToFrozenDictionary();

    /// <summary>Live-object entry point — used by anything that already has a real
    /// <see cref="Creature"/>/<see cref="MoveState"/> (e.g. verifying against
    /// <see cref="CombatNodeBlobSnapshot"/>'s <c>RawAttackDamage</c>/<c>AttackHits</c> at snapshot
    /// time). Forwards to <see cref="TryResolve(Type,string,ushort,out Resolved)"/>.</summary>
    public static bool TryResolve(Creature monster, MoveState move, ushort ascensionFlags, out Resolved result)
    {
        MonsterModel? liveMonster = monster.Monster;
        if (liveMonster is null)
        {
            result = default;
            return false;
        }
        return _byType.TryGetValue(liveMonster.GetType(), out AttackResolver? resolver)
            && resolver(monster, move.StateId, ascensionFlags, out result) || Fail(out result);
    }

    /// <summary>Blob-only entry point — no live <see cref="Creature"/> needed. This is the one the
    /// search engine actually uses: it has a monster <see cref="Type"/> (from
    /// <see cref="MonsterStateTable.MonsterType"/>, resolved via
    /// <see cref="SimMonsterStateRegistry.Resolve"/> off a blob handle) and a target stateId (from
    /// <see cref="MonsterStateTable.StateIds"/>) for a hypothetical future move that has no live
    /// object behind it — the entire reason this file exists instead of just reading
    /// <c>AttackIntent.DamageCalc()</c> off a live object like <see cref="CombatNodeBlobSnapshot"/>
    /// does today.</summary>
    public static bool TryResolve(Type monsterType, string stateId, ushort ascensionFlags, out Resolved result)
        => _byType.TryGetValue(monsterType, out AttackResolver? resolver)
            && resolver(null, stateId, ascensionFlags, out result) || Fail(out result);

    private static bool Fail(out Resolved result)
    {
        result = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasFlag(ushort ascensionFlags, int flag) => (ascensionFlags & flag) != 0;

    private static bool One(int damage, int hits, out Resolved result)
    {
        result = new Resolved((ushort)damage, (byte)hits);
        return true;
    }

    // ── AssassinRubyRaider: KILLSHOT_MOVE (11 DeadlyEnemies / 10 base), single hit. ─────────────
    private static bool WriteAssassinRubyRaider(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "KILLSHOT_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 11 : 10, 1, out result);
        result = default;
        return false;
    }

    // ── AxeRubyRaider: SWING_1/SWING_2 (6 DeadlyEnemies / 5 base); BIG_SWING (13/12). ───────────
    private static bool WriteAxeRubyRaider(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "SWING_1":
            case "SWING_2":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 6 : 5, 1, out result);
            case "BIG_SWING":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 13 : 12, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Axebot: ONE_TWO_MOVE (10 DeadlyEnemies / 9 base, 2 hits) and HAMMER_UPPERCUT_MOVE
    //    (14 DeadlyEnemies / 12 base, 1 hit) are BOTH pure ascension formulas — StockAmount only
    //    affects BOOT_UP_MOVE's Strength gain (a SimMonsterMoveEffects concern, not an attack),
    //    contrary to what might be assumed from that comment; verified directly against
    //    Axebot.cs — OneTwoMove/HammerUppercutMove never read StockAmount. No monster param needed. ──
    private static bool WriteAxebot(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "ONE_TWO_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 10 : 9, 2, out result);
            case "HAMMER_UPPERCUT_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 14 : 12, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── BruteRubyRaider: BEAT_MOVE (8 DeadlyEnemies / 7 base), single hit. ─────────────────────
    private static bool WriteBruteRubyRaider(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "BEAT_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 8 : 7, 1, out result);
        result = default;
        return false;
    }

    // ── CrossbowRubyRaider: FIRE_MOVE (16 DeadlyEnemies / 14 base), single hit. ────────────────
    private static bool WriteCrossbowRubyRaider(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "FIRE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 16 : 14, 1, out result);
        result = default;
        return false;
    }

    // ── TrackerRubyRaider: HOUNDS_MOVE — damage is a flat 1 regardless of ascension (source calls
    //    GetValueIfAscension(1,1), both branches equal), hit count is 9 DeadlyEnemies / 8 base. ───
    private static bool WriteTrackerRubyRaider(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "HOUNDS_MOVE")
            return One(1, HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 9 : 8, out result);
        result = default;
        return false;
    }

    // ── BowlbugRock: HEADBUTT_MOVE (16 DeadlyEnemies / 15 base), single hit. ───────────────────
    private static bool WriteBowlbugRock(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "HEADBUTT_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 16 : 15, 1, out result);
        result = default;
        return false;
    }

    // ── BowlbugEgg: BITE_MOVE (8 DeadlyEnemies / 7 base), single hit. ─────────────────────────
    private static bool WriteBowlbugEgg(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "BITE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 8 : 7, 1, out result);
        result = default;
        return false;
    }

    // ── BowlbugSilk: THRASH_MOVE (5 DeadlyEnemies / 4 base), 2 hits (fixed). ──────────────────
    private static bool WriteBowlbugSilk(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "THRASH_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 5 : 4, 2, out result);
        result = default;
        return false;
    }

    // ── BowlbugNectar: THRASH_MOVE and THRASH2_MOVE both deal a fixed 3, single hit, no
    //    ascension gate (two distinct stateIds, same formula). ───────────────────────────────────
    private static bool WriteBowlbugNectar(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "THRASH_MOVE" || stateId == "THRASH2_MOVE")
            return One(3, 1, out result);
        result = default;
        return false;
    }

    // ── CalcifiedCultist: DARK_STRIKE_MOVE (11 DeadlyEnemies / 9 base), single hit. ───────────
    private static bool WriteCalcifiedCultist(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "DARK_STRIKE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 11 : 9, 1, out result);
        result = default;
        return false;
    }

    // ── DampCultist: DARK_STRIKE_MOVE (3 DeadlyEnemies / 1 base), single hit. ─────────────────
    private static bool WriteDampCultist(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "DARK_STRIKE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 3 : 1, 1, out result);
        result = default;
        return false;
    }

    // ── Aeonglass: EBB_MOVE (32 DeadlyEnemies / 26 base, single hit); EYE_LASERS_MOVE
    //    (12 DeadlyEnemies / 11 base, 2 hits fixed). INCREASING_INTENSITY_MOVE is Buff-only, no
    //    attack — not registered here. ─────────────────────────────────────────────────────────
    private static bool WriteAeonglass(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "EBB_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 32 : 26, 1, out result);
            case "EYE_LASERS_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 12 : 11, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── BygoneEffigy: SLASHES_MOVE (15 DeadlyEnemies / 13 base), single hit. ──────────────────
    private static bool WriteBygoneEffigy(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "SLASHES_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 15 : 13, 1, out result);
        result = default;
        return false;
    }

    // ── Chomper: CLAMP_MOVE (9 DeadlyEnemies / 8 base), 2 hits (fixed). ───────────────────────
    private static bool WriteChomper(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "CLAMP_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 9 : 8, 2, out result);
        result = default;
        return false;
    }

    // ── CorpseSlug: WHIP_SLAP_MOVE (flat 3 damage, no ascension gate, 2 hits fixed);
    //    GLOMP_MOVE (9 DeadlyEnemies / 8 base, single hit). GOOP_MOVE is Debuff-only. ───────────
    private static bool WriteCorpseSlug(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "WHIP_SLAP_MOVE":
                return One(3, 2, out result);
            case "GLOMP_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 9 : 8, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── CeremonialBeast: PLOW_MOVE (20 DeadlyEnemies / 18 base); STOMP_MOVE (17/15);
    //    CRUSH_MOVE (19/17). All single hit. ────────────────────────────────────────────────────
    private static bool WriteCeremonialBeast(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "PLOW_MOVE":
                return One(deadly ? 20 : 18, 1, out result);
            case "STOMP_MOVE":
                return One(deadly ? 17 : 15, 1, out result);
            case "CRUSH_MOVE":
                return One(deadly ? 19 : 17, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Crusher (Kaiser Crab left arm): THRASH_MOVE (14 DeadlyEnemies / 12 base); ────────────
    //    ENLARGING_STRIKE_MOVE (4, both branches equal); BUG_STING_MOVE (7/6, 2 hits fixed);
    //    GUARDED_STRIKE_MOVE (14/12). ──────────────────────────────────────────────────────────
    private static bool WriteCrusher(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "THRASH_MOVE":
                return One(deadly ? 14 : 12, 1, out result);
            case "ENLARGING_STRIKE_MOVE":
                return One(4, 1, out result);
            case "BUG_STING_MOVE":
                return One(deadly ? 7 : 6, 2, out result);
            case "GUARDED_STRIKE_MOVE":
                return One(deadly ? 14 : 12, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── CubexConstruct: REPEATER_BLAST_MOVE and REPEATER_BLAST_MOVE_2 both (8 DeadlyEnemies / 7
    //    base, single hit); EXPEL_MOVE (6/5, 2 hits fixed). ────────────────────────────────────
    private static bool WriteCubexConstruct(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "REPEATER_BLAST_MOVE":
            case "REPEATER_BLAST_MOVE_2":
                return One(deadly ? 8 : 7, 1, out result);
            case "EXPEL_MOVE":
                return One(deadly ? 6 : 5, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── DecimillipedeSegmentBack/Front/Middle share one state graph and one formula set
    //    (only the purely-visual SegmentAttack differs per subclass): WRITHE_MOVE (6 DeadlyEnemies
    //    / 5 base, 2 hits fixed); BULK_MOVE (7/6, single hit); CONSTRICT_MOVE (9/8, single hit). ──
    private static bool WriteDecimillipedeSegment(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "WRITHE_MOVE":
                return One(deadly ? 6 : 5, 2, out result);
            case "BULK_MOVE":
                return One(deadly ? 7 : 6, 1, out result);
            case "CONSTRICT_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── DevotedSculptor: SAVAGE_MOVE (15 DeadlyEnemies / 12 base), single hit. ────────────────
    private static bool WriteDevotedSculptor(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "SAVAGE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 15 : 12, 1, out result);
        result = default;
        return false;
    }

    // ── Entomancer: BEES_MOVE (flat 3 damage, both branches equal; 8 DeadlyEnemies / 7 base
    //    hits); SPEAR_MOVE (20/18, single hit). ────────────────────────────────────────────────
    private static bool WriteEntomancer(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "BEES_MOVE":
                return One(3, HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 8 : 7, out result);
            case "SPEAR_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 20 : 18, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Exoskeleton: SKITTER_MOVE (flat 1 damage, both branches equal; 4 DeadlyEnemies / 3 base
    //    hits); MANDIBLES_MOVE (9/8, single hit). ──────────────────────────────────────────────
    private static bool WriteExoskeleton(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "SKITTER_MOVE":
                return One(1, HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3, out result);
            case "MANDIBLES_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 9 : 8, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Fabricator: FABRICATING_STRIKE_MOVE (21 DeadlyEnemies / 18 base); DISINTEGRATE_MOVE
    //    (13/11). Both single hit. FABRICATE_MOVE is Summon-only. ────────────────────────────
    private static bool WriteFabricator(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "FABRICATING_STRIKE_MOVE":
                return One(deadly ? 21 : 18, 1, out result);
            case "DISINTEGRATE_MOVE":
                return One(deadly ? 13 : 11, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── FakeMerchantMonster: SWIPE_MOVE (15 DeadlyEnemies / 13 base); SPEW_COINS_MOVE (flat
    //    2 damage, 8 hits, both fixed); THROW_RELIC_MOVE (10/9). ─────────────────────────────
    private static bool WriteFakeMerchantMonster(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SWIPE_MOVE":
                return One(deadly ? 15 : 13, 1, out result);
            case "SPEW_COINS_MOVE":
                return One(2, 8, out result);
            case "THROW_RELIC_MOVE":
                return One(deadly ? 10 : 9, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── FlailKnight: FLAIL_MOVE (10 DeadlyEnemies / 9 base, 2 hits fixed); RAM_MOVE (17/15,
    //    single hit). WAR_CHANT is Buff-only. ────────────────────────────────────────────────
    private static bool WriteFlailKnight(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "FLAIL_MOVE":
                return One(deadly ? 10 : 9, 2, out result);
            case "RAM_MOVE":
                return One(deadly ? 17 : 15, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Flyconid: FRAIL_SPORES_MOVE (9 DeadlyEnemies / 8 base); SMASH_MOVE (12/11). Both
    //    single hit. VULNERABLE_SPORES_MOVE is Debuff-only. ──────────────────────────────────
    private static bool WriteFlyconid(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "FRAIL_SPORES_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "SMASH_MOVE":
                return One(deadly ? 12 : 11, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Fogmog: SWIPE_MOVE and SWIPE_RANDOM_MOVE both (9 DeadlyEnemies / 8 base); HEADBUTT_MOVE
    //    (16/14). All single hit. ILLUSION_MOVE is Summon-only. ─────────────────────────────
    private static bool WriteFogmog(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SWIPE_MOVE":
            case "SWIPE_RANDOM_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "HEADBUTT_MOVE":
                return One(deadly ? 16 : 14, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── FossilStalker: TACKLE_MOVE (11 DeadlyEnemies / 9 base); LATCH_MOVE (14/12), both single
    //    hit; LASH_MOVE (4/3, 2 hits fixed). ────────────────────────────────────────────────
    private static bool WriteFossilStalker(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "TACKLE_MOVE":
                return One(deadly ? 11 : 9, 1, out result);
            case "LATCH_MOVE":
                return One(deadly ? 14 : 12, 1, out result);
            case "LASH_MOVE":
                return One(deadly ? 4 : 3, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── FrogKnight: STRIKE_DOWN_EVIL (23 DeadlyEnemies / 21 base); TONGUE_LASH (14/13);
    //    BEETLE_CHARGE (40/35). All single hit. FOR_THE_QUEEN is Buff-only. ────────────────
    private static bool WriteFrogKnight(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "STRIKE_DOWN_EVIL":
                return One(deadly ? 23 : 21, 1, out result);
            case "TONGUE_LASH":
                return One(deadly ? 14 : 13, 1, out result);
            case "BEETLE_CHARGE":
                return One(deadly ? 40 : 35, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── FuzzyWurmCrawler: FIRST_ACID_GOOP and ACID_GOOP both (6 DeadlyEnemies / 4 base),
    //    single hit. INHALE is Buff-only. ────────────────────────────────────────────────────
    private static bool WriteFuzzyWurmCrawler(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "FIRST_ACID_GOOP" || stateId == "ACID_GOOP")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 6 : 4, 1, out result);
        result = default;
        return false;
    }

    // ── GasBomb: EXPLODE_MOVE — DeathBlowIntent, but ExplodeDamage is a plain ascension-gated
    //    int property (9 DeadlyEnemies / 8 base), no live state involved despite the Func syntax.
    //    Single hit (DeathBlowIntent derives from SingleAttackIntent, Repeats == 1). ────────────
    private static bool WriteGasBomb(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "EXPLODE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 9 : 8, 1, out result);
        result = default;
        return false;
    }

    // ── GlobeHead: THUNDER_STRIKE (7 DeadlyEnemies / 6 base, 3 hits fixed); SHOCKING_SLAP
    //    (14/13); GALVANIC_BURST (17/16). Latter two single hit. ─────────────────────────────
    private static bool WriteGlobeHead(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "THUNDER_STRIKE":
                return One(deadly ? 7 : 6, 3, out result);
            case "SHOCKING_SLAP":
                return One(deadly ? 14 : 13, 1, out result);
            case "GALVANIC_BURST":
                return One(deadly ? 17 : 16, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── GremlinMerc: GIMME_MOVE (8 ToughEnemies / 7 base, 2 hits fixed); DOUBLE_SMASH_MOVE
    //    (7/6, 2 hits fixed); HEHE_MOVE (9/8, single hit). Note: ascension gate here is
    //    ToughEnemies, not DeadlyEnemies like almost everything else — verified directly against
    //    GremlinMerc.cs, not a typo. ──────────────────────────────────────────────────────────
    private static bool WriteGremlinMerc(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool tough = HasFlag(ascensionFlags, SimAscension.ToughEnemies);
        switch (stateId)
        {
            case "GIMME_MOVE":
                return One(tough ? 8 : 7, 2, out result);
            case "DOUBLE_SMASH_MOVE":
                return One(tough ? 7 : 6, 2, out result);
            case "HEHE_MOVE":
                return One(tough ? 9 : 8, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── HauntedShip: SWIPE_MOVE (14 DeadlyEnemies / 13 base, single hit); STOMP_MOVE
    //    (5/4, 3 hits fixed). ───────────────────────────────────────────────────────────────
    private static bool WriteHauntedShip(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SWIPE_MOVE":
                return One(deadly ? 14 : 13, 1, out result);
            case "STOMP_MOVE":
                return One(deadly ? 5 : 4, 3, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── HunterKiller: BITE_MOVE (19 DeadlyEnemies / 17 base, single hit); PUNCTURE_MOVE
    //    (8/7, 3 hits fixed). TENDERIZING_GOOP_MOVE is Debuff-only. ────────────────────────
    private static bool WriteHunterKiller(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "BITE_MOVE":
                return One(deadly ? 19 : 17, 1, out result);
            case "PUNCTURE_MOVE":
                return One(deadly ? 8 : 7, 3, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── InfestedPrism: JAB_MOVE (17 DeadlyEnemies / 15 base); RADIATE_MOVE (13/11);
    //    PULSATE_MOVE (10/8). All single hit. WHIRLWIND_MOVE (6/5, 3 hits fixed). ────────────
    private static bool WriteInfestedPrism(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "JAB_MOVE":
                return One(deadly ? 17 : 15, 1, out result);
            case "RADIATE_MOVE":
                return One(deadly ? 13 : 11, 1, out result);
            case "WHIRLWIND_MOVE":
                return One(deadly ? 6 : 5, 3, out result);
            case "PULSATE_MOVE":
                return One(deadly ? 10 : 8, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Inklet: JAB_MOVE (4 DeadlyEnemies / 3 base, single hit); WHIRLWIND_MOVE (3/2, 3 hits
    //    fixed); PIERCING_GAZE_MOVE (11/10, single hit). ───────────────────────────────────
    private static bool WriteInklet(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "JAB_MOVE":
                return One(deadly ? 4 : 3, 1, out result);
            case "WHIRLWIND_MOVE":
                return One(deadly ? 3 : 2, 3, out result);
            case "PIERCING_GAZE_MOVE":
                return One(deadly ? 11 : 10, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── KinFollower: QUICK_SLASH_MOVE (flat 5, both branches equal, single hit);
    //    BOOMERANG_MOVE (flat 2, both branches equal, 2 hits fixed). POWER_DANCE_MOVE is
    //    Buff-only. ────────────────────────────────────────────────────────────────────────
    private static bool WriteKinFollower(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "QUICK_SLASH_MOVE":
                return One(5, 1, out result);
            case "BOOMERANG_MOVE":
                return One(2, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── KinPriest: ORB_OF_FRAILTY_MOVE (9 DeadlyEnemies / 8 base); ORB_OF_WEAKNESS_MOVE (9/8).
    //    Both single hit. BEAM_MOVE (flat 3, both branches equal, 3 hits fixed). ────────────
    private static bool WriteKinPriest(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "ORB_OF_FRAILTY_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "ORB_OF_WEAKNESS_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "BEAM_MOVE":
                return One(3, 3, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── KnowledgeDemon: SLAP_MOVE (18 DeadlyEnemies / 17 base, single hit); KNOWLEDGE_
    //    OVERWHELMING_MOVE (9/8, 3 hits fixed); PONDER_MOVE (13/11, single hit). ─────────────
    private static bool WriteKnowledgeDemon(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SLAP_MOVE":
                return One(deadly ? 18 : 17, 1, out result);
            case "KNOWLEDGE_OVERWHELMING_MOVE":
                return One(deadly ? 9 : 8, 3, out result);
            case "PONDER_MOVE":
                return One(deadly ? 13 : 11, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── LagavulinMatriarch: SLASH_MOVE (21 DeadlyEnemies / 19 base); SLASH2_MOVE (14/12).
    //    Both single hit. DISEMBOWEL_MOVE (10/9, 2 hits fixed). ──────────────────────────────
    private static bool WriteLagavulinMatriarch(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SLASH_MOVE":
                return One(deadly ? 21 : 19, 1, out result);
            case "SLASH2_MOVE":
                return One(deadly ? 14 : 12, 1, out result);
            case "DISEMBOWEL_MOVE":
                return One(deadly ? 10 : 9, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── LeafSlimeM: CLUMP_SHOT (9 DeadlyEnemies / 8 base), single hit. ───────────────────────
    private static bool WriteLeafSlimeM(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "CLUMP_SHOT")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 9 : 8, 1, out result);
        result = default;
        return false;
    }

    // ── LeafSlimeS: TACKLE_MOVE (4 DeadlyEnemies / 3 base), single hit. ──────────────────────
    private static bool WriteLeafSlimeS(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "TACKLE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3, 1, out result);
        result = default;
        return false;
    }

    // ── LivingFog: ADVANCED_GAS_MOVE (9 DeadlyEnemies / 8 base); BLOAT_MOVE (6/5);
    //    SUPER_GAS_BLAST_MOVE (9/8). All single hit. ─────────────────────────────────────────
    private static bool WriteLivingFog(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "ADVANCED_GAS_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "BLOAT_MOVE":
                return One(deadly ? 6 : 5, 1, out result);
            case "SUPER_GAS_BLAST_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── LivingShield: SHIELD_SLAM_MOVE (flat 6, no ascension gate, single hit); SMASH_MOVE
    //    (18 DeadlyEnemies / 16 base, single hit). ─────────────────────────────────────────
    private static bool WriteLivingShield(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "SHIELD_SLAM_MOVE":
                return One(6, 1, out result);
            case "SMASH_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 18 : 16, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── LouseProgenitor: WEB_CANNON_MOVE (10 DeadlyEnemies / 9 base); POUNCE_MOVE (16/14).
    //    Both single hit. ────────────────────────────────────────────────────────────────────
    private static bool WriteLouseProgenitor(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "WEB_CANNON_MOVE":
                return One(deadly ? 10 : 9, 1, out result);
            case "POUNCE_MOVE":
                return One(deadly ? 16 : 14, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── MagiKnight: POWER_SHIELD_MOVE (7 DeadlyEnemies / 6 base); MAGIC_BOMB (40/35);
    //    RAM_MOVE (11/10, function name SpearMove/SpearDamage). All single hit. PREP_MOVE is
    //    Defend-only. ──────────────────────────────────────────────────────────────────────
    private static bool WriteMagiKnight(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "POWER_SHIELD_MOVE":
                return One(deadly ? 7 : 6, 1, out result);
            case "MAGIC_BOMB":
                return One(deadly ? 40 : 35, 1, out result);
            case "RAM_MOVE":
                return One(deadly ? 11 : 10, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Mawler: RIP_AND_TEAR_MOVE (16 DeadlyEnemies / 14 base, single hit); CLAW_MOVE
    //    (5/4, 2 hits fixed). ROAR_MOVE is Debuff-only. ──────────────────────────────────────
    private static bool WriteMawler(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "RIP_AND_TEAR_MOVE":
                return One(deadly ? 16 : 14, 1, out result);
            case "CLAW_MOVE":
                return One(deadly ? 5 : 4, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── MechaKnight: CHARGE_MOVE (30 DeadlyEnemies / 25 base); HEAVY_CLEAVE_MOVE (40/35).
    //    Both single hit. WINDUP_MOVE is Defend+Buff only; FLAMETHROWER_MOVE is StatusIntent
    //    only (verified against source — not an AttackIntent despite dealing status damage). ──
    private static bool WriteMechaKnight(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "CHARGE_MOVE":
                return One(deadly ? 30 : 25, 1, out result);
            case "HEAVY_CLEAVE_MOVE":
                return One(deadly ? 40 : 35, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Myte: BITE_MOVE (15 DeadlyEnemies / 13 base); SUCK_MOVE (6/4). Both single hit.
    //    TOXIC_MOVE is StatusIntent only. ─────────────────────────────────────────────────
    private static bool WriteMyte(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "BITE_MOVE":
                return One(deadly ? 15 : 13, 1, out result);
            case "SUCK_MOVE":
                return One(deadly ? 6 : 4, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Nibbit: BUTT_MOVE (13 DeadlyEnemies / 12 base); SLICE_MOVE (7/6). Both single hit.
    //    HISS_MOVE is Buff-only. ─────────────────────────────────────────────────────────────
    private static bool WriteNibbit(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "BUTT_MOVE":
                return One(deadly ? 13 : 12, 1, out result);
            case "SLICE_MOVE":
                return One(deadly ? 7 : 6, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Ovicopter: SMASH_MOVE (17 DeadlyEnemies / 16 base); TENDERIZER_MOVE (8/7). Both
    //    single hit. LAY_EGGS_MOVE is Summon-only. ──────────────────────────────────────────
    private static bool WriteOvicopter(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SMASH_MOVE":
                return One(deadly ? 17 : 16, 1, out result);
            case "TENDERIZER_MOVE":
                return One(deadly ? 8 : 7, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── OwlMagistrate: MAGISTRATE_SCRUTINY (17 DeadlyEnemies / 16 base, single hit);
    //    PECK_ASSAULT (flat 4, both branches equal, 6 hits fixed); VERDICT (36/33, single hit).
    //    JUDICIAL_FLIGHT is Buff-only. ───────────────────────────────────────────────────────
    private static bool WriteOwlMagistrate(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "MAGISTRATE_SCRUTINY":
                return One(deadly ? 17 : 16, 1, out result);
            case "PECK_ASSAULT":
                return One(4, 6, out result);
            case "VERDICT":
                return One(deadly ? 36 : 33, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Parafright: SLAM_MOVE (17 DeadlyEnemies / 16 base), single hit. ──────────────────────
    private static bool WriteParafright(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "SLAM_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 17 : 16, 1, out result);
        result = default;
        return false;
    }

    // ── PhantasmalGardener: BITE_MOVE (flat 5, both branches equal); LASH_MOVE (flat 7, both
    //    branches equal). Both single hit. FLAIL_MOVE (flat 1, 3 hits, both fixed). ─────────
    private static bool WritePhantasmalGardener(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "BITE_MOVE":
                return One(5, 1, out result);
            case "LASH_MOVE":
                return One(7, 1, out result);
            case "FLAIL_MOVE":
                return One(1, 3, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── PhrogParasite: LASH_MOVE (5 DeadlyEnemies / 4 base, 4 hits fixed). INFECT_MOVE is
    //    StatusIntent only. ───────────────────────────────────────────────────────────────────
    private static bool WritePhrogParasite(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "LASH_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 5 : 4, 4, out result);
        result = default;
        return false;
    }

    // ── PunchConstruct: STRONG_PUNCH_MOVE (16 DeadlyEnemies / 14 base, single hit);
    //    FAST_PUNCH_MOVE (6/5, 2 hits fixed). READY_MOVE is Defend-only. ───────────────────
    private static bool WritePunchConstruct(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "STRONG_PUNCH_MOVE":
                return One(deadly ? 16 : 14, 1, out result);
            case "FAST_PUNCH_MOVE":
                return One(deadly ? 6 : 5, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Queen: OFF_WITH_YOUR_HEAD_MOVE (4 DeadlyEnemies / 3 base, 5 hits fixed); EXECUTION_MOVE
    //    (18/15, single hit). Both are pure ascension formulas with no per-instance state — the
    //    HasAmalgamDied dependency documented elsewhere (dev_docs) is a STATE-ADVANCE concern
    //    (which move comes next), not a numeric-payload concern for these two moves. ──────────
    private static bool WriteQueen(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "OFF_WITH_YOUR_HEAD_MOVE":
                return One(deadly ? 4 : 3, 5, out result);
            case "EXECUTION_MOVE":
                return One(deadly ? 18 : 15, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Rocket (Kaiser Crab right arm): TARGETING_RETICLE_MOVE (4 DeadlyEnemies / 3 base);
    //    PRECISION_BEAM_MOVE (20/18); LASER_MOVE (35/31). All single hit. CHARGE_UP_MOVE is
    //    Buff-only. ────────────────────────────────────────────────────────────────────────
    private static bool WriteRocket(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "TARGETING_RETICLE_MOVE":
                return One(deadly ? 4 : 3, 1, out result);
            case "PRECISION_BEAM_MOVE":
                return One(deadly ? 20 : 18, 1, out result);
            case "LASER_MOVE":
                return One(deadly ? 35 : 31, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── ScrollOfBiting: CHOMP (16 DeadlyEnemies / 14 base, single hit); CHEW (6/5, 2 hits
    //    fixed). ───────────────────────────────────────────────────────────────────────────
    private static bool WriteScrollOfBiting(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "CHOMP":
                return One(deadly ? 16 : 14, 1, out result);
            case "CHEW":
                return One(deadly ? 6 : 5, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Seapunk: SEA_KICK_MOVE (13 DeadlyEnemies / 11 base, single hit); SPINNING_KICK_MOVE
    //    (flat 2, 4 hits, both fixed). BUBBLE_BURP_MOVE is Defend+Buff only, no attack. ──────
    private static bool WriteSeapunk(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "SEA_KICK_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 13 : 11, 1, out result);
            case "SPINNING_KICK_MOVE":
                return One(2, 4, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SewerClam: JET_MOVE (11 DeadlyEnemies / 10 base), single hit. PRESSURIZE_MOVE is
    //    Buff-only. ────────────────────────────────────────────────────────────────────────
    private static bool WriteSewerClam(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "JET_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 11 : 10, 1, out result);
        result = default;
        return false;
    }

    // ── ShrinkerBeetle: CHOMP_MOVE (8 DeadlyEnemies / 7 base); STOMP_MOVE (14/13). Both
    //    single hit. SHRINKER_MOVE is DebuffStrong-only. ───────────────────────────────────
    private static bool WriteShrinkerBeetle(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "CHOMP_MOVE":
                return One(deadly ? 8 : 7, 1, out result);
            case "STOMP_MOVE":
                return One(deadly ? 14 : 13, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SkulkingColony: ZOOM_MOVE and ZOOM_MOVE_2 both (16 DeadlyEnemies / 14 base, single
    //    hit); INERTIA_MOVE (11/9, single hit); PIERCING_STABS_MOVE (8/7, 2 hits fixed). ────
    private static bool WriteSkulkingColony(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "ZOOM_MOVE":
            case "ZOOM_MOVE_2":
                return One(deadly ? 16 : 14, 1, out result);
            case "INERTIA_MOVE":
                return One(deadly ? 11 : 9, 1, out result);
            case "PIERCING_STABS_MOVE":
                return One(deadly ? 8 : 7, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SlimedBerserker: SMOTHER_MOVE (33 DeadlyEnemies / 30 base, single hit);
    //    FURIOUS_PUMMELING_MOVE (5/4, 4 hits fixed). LEECHING_HUG_MOVE is Debuff+Buff only;
    //    VOMIT_ICHOR_MOVE is StatusIntent only. ───────────────────────────────────────────
    private static bool WriteSlimedBerserker(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SMOTHER_MOVE":
                return One(deadly ? 33 : 30, 1, out result);
            case "FURIOUS_PUMMELING_MOVE":
                return One(deadly ? 5 : 4, 4, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SlitheringStrangler: THWACK (8 DeadlyEnemies / 7 base); LASH (13/12). Both single
    //    hit. CONSTRICT is Debuff-only. ─────────────────────────────────────────────────────
    private static bool WriteSlitheringStrangler(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "THWACK":
                return One(deadly ? 8 : 7, 1, out result);
            case "LASH":
                return One(deadly ? 13 : 12, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SludgeSpinner: OIL_SPRAY_MOVE (9 DeadlyEnemies / 8 base); SLAM_MOVE (12/11);
    //    RAGE_MOVE (7/6). All single hit. ─────────────────────────────────────────────────
    private static bool WriteSludgeSpinner(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "OIL_SPRAY_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "SLAM_MOVE":
                return One(deadly ? 12 : 11, 1, out result);
            case "RAGE_MOVE":
                return One(deadly ? 7 : 6, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SlumberingBeetle: ROLL_OUT_MOVE (18 DeadlyEnemies / 16 base), single hit. SNORE_MOVE
    //    is Sleep-only. ────────────────────────────────────────────────────────────────────
    private static bool WriteSlumberingBeetle(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "ROLL_OUT_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 18 : 16, 1, out result);
        result = default;
        return false;
    }

    // ── SnappingJaxfruit: ENERGY_ORB_MOVE (4 DeadlyEnemies / 3 base), single hit. ───────────
    private static bool WriteSnappingJaxfruit(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "ENERGY_ORB_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3, 1, out result);
        result = default;
        return false;
    }

    // ── SneakyGremlin: TACKLE_MOVE (10 DeadlyEnemies / 9 base), single hit. SPAWNED_MOVE is
    //    Stun-only. ─────────────────────────────────────────────────────────────────────────
    private static bool WriteSneakyGremlin(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "TACKLE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 10 : 9, 1, out result);
        result = default;
        return false;
    }

    // ── SoulFysh: DE_GAS_MOVE (17 DeadlyEnemies / 16 base); GAZE_MOVE (8/7, also carries a
    //    StatusIntent card-inject handled by SimMonsterMoveEffects, not here); SCREAM_MOVE
    //    (15/13). All single hit. FADE_MOVE is Buff-only; BECKON_MOVE is StatusIntent only. ──
    private static bool WriteSoulFysh(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "DE_GAS_MOVE":
                return One(deadly ? 17 : 16, 1, out result);
            case "GAZE_MOVE":
                return One(deadly ? 8 : 7, 1, out result);
            case "SCREAM_MOVE":
                return One(deadly ? 15 : 13, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SoulNexus: SOUL_BURN_MOVE (31 DeadlyEnemies / 29 base, single hit); MAELSTROM_MOVE
    //    (7/6, 4 hits both branches equal); DRAIN_LIFE_MOVE (19/18, single hit). ────────────
    private static bool WriteSoulNexus(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SOUL_BURN_MOVE":
                return One(deadly ? 31 : 29, 1, out result);
            case "MAELSTROM_MOVE":
                return One(deadly ? 7 : 6, 4, out result);
            case "DRAIN_LIFE_MOVE":
                return One(deadly ? 19 : 18, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SpectralKnight: SOUL_SLASH (17 DeadlyEnemies / 15 base, single hit); SOUL_FLAME
    //    (4/3, 3 hits fixed). HEX is Debuff-only. ─────────────────────────────────────────
    private static bool WriteSpectralKnight(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SOUL_SLASH":
                return One(deadly ? 17 : 15, 1, out result);
            case "SOUL_FLAME":
                return One(deadly ? 4 : 3, 3, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SpinyToad: SPIKE_EXPLOSION_MOVE (25 DeadlyEnemies / 23 base); TONGUE_LASH_MOVE
    //    (19/17). Both single hit. PROTRUDING_SPIKES_MOVE is Buff-only. ─────────────────────
    private static bool WriteSpinyToad(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SPIKE_EXPLOSION_MOVE":
                return One(deadly ? 25 : 23, 1, out result);
            case "TONGUE_LASH_MOVE":
                return One(deadly ? 19 : 17, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Stabbot: STAB_MOVE (12 DeadlyEnemies / 11 base), single hit. ────────────────────────
    private static bool WriteStabbot(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "STAB_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 12 : 11, 1, out result);
        result = default;
        return false;
    }

    // ── TerrorEel: CRASH_MOVE (18 DeadlyEnemies / 16 base, single hit); THRASH_MOVE (4/3,
    //    3 hits fixed). STUN_MOVE/TERROR_MOVE are Stun/Debuff only, no attack. ─────────────
    private static bool WriteTerrorEel(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "CRASH_MOVE":
                return One(deadly ? 18 : 16, 1, out result);
            case "THRASH_MOVE":
                return One(deadly ? 4 : 3, 3, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TestSubject: BITE_MOVE (22 DeadlyEnemies / 20 base); SKULL_BASH_MOVE (16/14);
    //    BIG_POUNCE (flat 45, no ascension gate). All single hit. PHASE3_LACERATE_MOVE
    //    (11/10, 3 hits fixed). MULTI_CLAW_MOVE's hit count is
    //    <c>BaseMultiClawCount + ExtraMultiClawCount</c> — ExtraMultiClawCount is a PRIVATE
    //    counter (no public accessor, unlike Axebot's StockAmount) incremented on a growth-spurt
    //    trigger with no blob field tracking it; genuinely unresolvable from Type+stateId+ascension
    //    alone, deliberately left unregistered — see dev_docs/Enemy_Intent_Payload_Backlog.md.
    //    RESPAWN_MOVE is Heal+Buff only (no attack). ───────────────────────────────────────
    private static bool WriteTestSubject(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "BITE_MOVE":
                return One(deadly ? 22 : 20, 1, out result);
            case "SKULL_BASH_MOVE":
                return One(deadly ? 16 : 14, 1, out result);
            case "PHASE3_LACERATE_MOVE":
                return One(deadly ? 11 : 10, 3, out result);
            case "BIG_POUNCE":
                return One(45, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TheAdversaryMkOne: SMASH_MOVE (flat 12); BEAM_MOVE (flat 15). Both single hit,
    //    no ascension gate on any Adversary move. BARRAGE_MOVE (flat 8, 2 hits fixed). ─────
    private static bool WriteTheAdversaryMkOne(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "SMASH_MOVE":
                return One(12, 1, out result);
            case "BEAM_MOVE":
                return One(15, 1, out result);
            case "BARRAGE_MOVE":
                return One(8, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TheAdversaryMkTwo: BASH_MOVE (flat 13); FLAME_BEAM_MOVE (flat 16). Both single hit.
    //    BARRAGE_MOVE (flat 9, 2 hits fixed). ─────────────────────────────────────────────
    private static bool WriteTheAdversaryMkTwo(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "BASH_MOVE":
                return One(13, 1, out result);
            case "FLAME_BEAM_MOVE":
                return One(16, 1, out result);
            case "BARRAGE_MOVE":
                return One(9, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TheAdversaryMkThree: CRASH_MOVE (flat 15); FLAME_BEAM_MOVE (flat 18). Both single
    //    hit. BARRAGE_MOVE (flat 10, 2 hits fixed). ───────────────────────────────────────
    private static bool WriteTheAdversaryMkThree(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "CRASH_MOVE":
                return One(15, 1, out result);
            case "FLAME_BEAM_MOVE":
                return One(18, 1, out result);
            case "BARRAGE_MOVE":
                return One(10, 2, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TheForgotten: DREAD — base ascension amount (15 DeadlyEnemies / 13) PLUS the live
    //    creature's current DexterityPower amount (TheForgotten steals Dexterity from the player
    //    into itself every MIASMA cast, growing this over the fight — a genuine per-instance
    //    mutable dependency, same class as Axebot's StockAmount). DexterityPower amount is
    //    readable straight off the live Creature (public API, unlike TestSubject/WaterfallGiant's
    //    private fields below), so a null-monster (blob-only) call falls back to 0 extra Dexterity
    //    — the value at combat/spawn start, before any MIASMA has fired. MIASMA itself is
    //    Debuff+Defend+Buff only, no attack. ─────────────────────────────────────────────────
    private static bool WriteTheForgotten(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId != "DREAD")
        {
            result = default;
            return false;
        }
        int baseDamage = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 15 : 13;
        int dexterity = monster?.GetPowerAmount<DexterityPower>() ?? 0;
        return One(baseDamage + dexterity, 1, out result);
    }

    // ── TheInsatiable: THRASH_MOVE and THRASH_MOVE_2 both (9 DeadlyEnemies / 8 base, 2 hits
    //    fixed); LUNGING_BITE_MOVE (31/28, single hit). LIQUIFY_GROUND_MOVE is Buff+
    //    StatusIntent only. ────────────────────────────────────────────────────────────────
    private static bool WriteTheInsatiable(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "THRASH_MOVE":
            case "THRASH_MOVE_2":
                return One(deadly ? 9 : 8, 2, out result);
            case "LUNGING_BITE_MOVE":
                return One(deadly ? 31 : 28, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TheLost: EYE_LASERS (5 DeadlyEnemies / 4 base, 2 hits fixed). DEBILITATING_SMOG is
    //    Debuff+Buff only (the Strength-steal move), no attack. ─────────────────────────────
    private static bool WriteTheLost(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "EYE_LASERS")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 5 : 4, 2, out result);
        result = default;
        return false;
    }

    // ── TheObscura: PIERCING_GAZE_MOVE (11 DeadlyEnemies / 10 base); HARDENING_STRIKE_MOVE
    //    (7/6). Both single hit. ILLUSION_MOVE is Summon-only; SAIL_MOVE is Buff-only. ────
    private static bool WriteTheObscura(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "PIERCING_GAZE_MOVE":
                return One(deadly ? 11 : 10, 1, out result);
            case "HARDENING_STRIKE_MOVE":
                return One(deadly ? 7 : 6, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── ThievingHopper: THIEVERY_MOVE (19 DeadlyEnemies / 17 base, also CardDebuff — the
    //    steal itself is a separate, unimplemented subsystem, not this registry's concern);
    //    NAB_MOVE (16/14); HAT_TRICK_MOVE (23/21). All single hit. ─────────────────────────
    private static bool WriteThievingHopper(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "THIEVERY_MOVE":
                return One(deadly ? 19 : 17, 1, out result);
            case "NAB_MOVE":
                return One(deadly ? 16 : 14, 1, out result);
            case "HAT_TRICK_MOVE":
                return One(deadly ? 23 : 21, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Toadpole: SPIKE_SPIT_MOVE (4 DeadlyEnemies / 3 base, 3 hits fixed); WHIRL_MOVE
    //    (8/7, single hit). SPIKEN_MOVE is Buff-only. ────────────────────────────────────
    private static bool WriteToadpole(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SPIKE_SPIT_MOVE":
                return One(deadly ? 4 : 3, 3, out result);
            case "WHIRL_MOVE":
                return One(deadly ? 8 : 7, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TorchHeadAmalgam (Queen's companion): TACKLE_MOVE and TACKLE_2_MOVE both
    //    (19 DeadlyEnemies / 18 base); TACKLE_3_MOVE and TACKLE_4_MOVE both (15/14, function
    //    name WeakTackleMove). All single hit. BEAM_MOVE (8, both branches equal, 3 hits fixed). ─
    private static bool WriteTorchHeadAmalgam(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "TACKLE_MOVE":
            case "TACKLE_2_MOVE":
                return One(deadly ? 19 : 18, 1, out result);
            case "TACKLE_3_MOVE":
            case "TACKLE_4_MOVE":
                return One(deadly ? 15 : 14, 1, out result);
            case "BEAM_MOVE":
                return One(8, 3, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── ToughEgg: NIBBLE_MOVE (5 DeadlyEnemies / 4 base), single hit. HATCH_MOVE is
    //    Summon-only. ────────────────────────────────────────────────────────────────────
    private static bool WriteToughEgg(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "NIBBLE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 5 : 4, 1, out result);
        result = default;
        return false;
    }

    // ── Tunneler: BITE_MOVE (15 DeadlyEnemies / 13 base); BELOW_MOVE (26/23). Both single
    //    hit. BURROW_MOVE is Buff+Defend only. ────────────────────────────────────────────
    private static bool WriteTunneler(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "BITE_MOVE":
                return One(deadly ? 15 : 13, 1, out result);
            case "BELOW_MOVE":
                return One(deadly ? 26 : 23, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── TurretOperator: UNLOAD_MOVE and UNLOAD_MOVE_2 both (4 DeadlyEnemies / 3 base,
    //    5 hits fixed). RELOAD_MOVE has no attack. ────────────────────────────────────────
    private static bool WriteTurretOperator(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "UNLOAD_MOVE" || stateId == "UNLOAD_MOVE_2")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3, 5, out result);
        result = default;
        return false;
    }

    // ── TwigSlimeM: POKEY_POUNCE_MOVE (12 DeadlyEnemies / 11 base), single hit. ─────────────
    private static bool WriteTwigSlimeM(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "POKEY_POUNCE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 12 : 11, 1, out result);
        result = default;
        return false;
    }

    // ── TwigSlimeS: TACKLE_MOVE (5 DeadlyEnemies / 4 base), single hit. ─────────────────────
    private static bool WriteTwigSlimeS(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "TACKLE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 5 : 4, 1, out result);
        result = default;
        return false;
    }

    // ── TwoTailedRat: SCRATCH_MOVE (9 DeadlyEnemies / 8 base); DISEASE_BITE_MOVE (7/6). Both
    //    single hit, pure ascension formulas — the documented state-advance gap for this monster
    //    (dynamic RAND weights) does not affect these two moves' own numeric payload. ────────
    private static bool WriteTwoTailedRat(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "SCRATCH_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "DISEASE_BITE_MOVE":
                return One(deadly ? 7 : 6, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Vantom: INK_BLOT_MOVE (8 DeadlyEnemies / 7 base, single hit); INKY_LANCE_MOVE
    //    (7/6, 2 hits fixed); DISMEMBER_MOVE (30/26, single hit; also carries a StatusIntent
    //    card-inject handled elsewhere, not here). ────────────────────────────────────────
    private static bool WriteVantom(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "INK_BLOT_MOVE":
                return One(deadly ? 8 : 7, 1, out result);
            case "INKY_LANCE_MOVE":
                return One(deadly ? 7 : 6, 2, out result);
            case "DISMEMBER_MOVE":
                return One(deadly ? 30 : 26, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── VineShambler: GRASPING_VINES_MOVE (9 DeadlyEnemies / 8 base, single hit; also
    //    CardDebuff, not this registry's concern); SWIPE_MOVE (7/6, 2 hits fixed);
    //    CHOMP_MOVE (18/16, single hit). ──────────────────────────────────────────────────
    private static bool WriteVineShambler(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "GRASPING_VINES_MOVE":
                return One(deadly ? 9 : 8, 1, out result);
            case "SWIPE_MOVE":
                return One(deadly ? 7 : 6, 2, out result);
            case "CHOMP_MOVE":
                return One(deadly ? 18 : 16, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── WaterfallGiant: STOMP_MOVE (16 DeadlyEnemies / 15 base); RAM_MOVE (11/10);
    //    PRESSURE_UP_MOVE (14/13). All single hit, pure ascension formulas — verified safe.
    //    PRESSURE_GUN_MOVE and EXPLODE_MOVE are deliberately NOT registered: contrary to the
    //    "both DeathBlow monsters resolve from ascension alone" assumption going into this pass,
    //    WaterfallGiant.cs shows PRESSURE_GUN_MOVE's damage is a private growing counter
    //    (CurrentPressureGunDamage, +5 every cast starting from BasePressureGunDamage — no public
    //    accessor, no blob field) and EXPLODE_MOVE's DeathBlow damage is a private field
    //    (SteamEruptionDamage) snapshotted from SteamEruptionPower's amount at the moment
    //    ABOUT_TO_BLOW_MOVE fires, with that power immediately removed afterward — by the time
    //    EXPLODE_MOVE is the telegraphed move, the value is gone from both live Creature state
    //    (power already removed) and ascension; genuinely unresolvable here. See
    //    dev_docs/Enemy_Intent_Payload_Backlog.md. ─────────────────────────────────────────
    private static bool WriteWaterfallGiant(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        bool deadly = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies);
        switch (stateId)
        {
            case "STOMP_MOVE":
                return One(deadly ? 16 : 15, 1, out result);
            case "RAM_MOVE":
                return One(deadly ? 11 : 10, 1, out result);
            case "PRESSURE_UP_MOVE":
                return One(deadly ? 14 : 13, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── Wriggler: NASTY_BITE_MOVE (7 DeadlyEnemies / 6 base), single hit. ───────────────────
    private static bool WriteWriggler(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "NASTY_BITE_MOVE")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 7 : 6, 1, out result);
        result = default;
        return false;
    }

    // ── Zapbot: ZAP (15 DeadlyEnemies / 14 base), single hit. ───────────────────────────────
    private static bool WriteZapbot(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "ZAP")
            return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 15 : 14, 1, out result);
        result = default;
        return false;
    }

    // ── Byrdonis: PECK_MOVE (4 DeadlyEnemies / 3 base, 3 hits both branches equal);
    //    SWOOP_MOVE (19/17, single hit). ───────────────────────────────────────────────────
    private static bool WriteByrdonis(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        switch (stateId)
        {
            case "PECK_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 4 : 3, 3, out result);
            case "SWOOP_MOVE":
                return One(HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 19 : 17, 1, out result);
            default:
                result = default;
                return false;
        }
    }

    // ── SingleAttackMoveMonster / MultiAttackMoveMonster: debug "BigDummy"-titled test monsters
    //    (real, instantiable, non-mock MonsterModel subclasses under Models.Monsters — not under
    //    Models.Monsters.Mocks), each a single self-looping "POKE" state. Trivial fixed values,
    //    no ascension gate. ─────────────────────────────────────────────────────────────────
    private static bool WriteSingleAttackMoveMonster(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "POKE")
            return One(1, 1, out result);
        result = default;
        return false;
    }

    private static bool WriteMultiAttackMoveMonster(Creature? monster, string stateId, ushort ascensionFlags, out Resolved result)
    {
        if (stateId == "POKE")
            return One(1, 5, out result);
        result = default;
        return false;
    }
}
