using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace STS2.Agent.Sim;

/// <summary>
/// Executes a card's effect directly against a <see cref="CombatNodeBlob"/> — mutates the state
/// in place, unlike <see cref="SimMonsterMoveEffects"/> which only peeks without executing. Each
/// card gets its own hand-written function (same per-item division of labor that worked for the
/// ~85 monsters), reading the numbers straight out of the game's own <c>OnPlay</c> body.
///
/// Fail-loud, unlike SimMonsterMoveEffects: an unregistered card THROWS rather than silently doing
/// nothing. A monster's un-captured Buff/Debuff amount only degrades a debug display; an
/// un-captured card effect would let the search engine silently believe a card did nothing,
/// corrupting every downstream result with no visible symptom. A card is only added to the
/// registry once its ENTIRE effect is captured — no partial entries (e.g. Anger's damage-only
/// half without its self-clone-into-discard half is not registered; see the comment at the bottom).
///
/// Known simplification: the real game moves the card being played OUT of Hand (into a transient
/// "Play" pile) before OnPlay runs, so a card that inspects "cards in hand" during its own play
/// never counts itself. This file has no separate Play pile — the card being played is still
/// sitting in HandCards during Apply. Harmless for every card counted so far (Flechettes counts
/// Skill cards while itself being Attack; Impatience checks for Attack cards while itself being
/// Skill — neither ever matches its own type) but would silently over-count by one for a future
/// card that inspects hand-count-of-its-own-type. Watch for this if that ever comes up.
/// </summary>
internal static class SimCardEffects
{
    public delegate void EffectApplier(CombatNodeBlob state, in SimCard card, int targetEnemyIdx);

    private static readonly FrozenDictionary<ushort, EffectApplier> _byId = new Dictionary<ushort, EffectApplier>
    {
        { SimCardId.StrikeIronclad, ApplyStrike },
        { SimCardId.StrikeSilent, ApplyStrike },
        { SimCardId.StrikeDefect, ApplyStrike },
        { SimCardId.StrikeRegent, ApplyStrike },
        { SimCardId.StrikeNecrobinder, ApplyStrike },
        { SimCardId.DefendIronclad, ApplyDefend },
        { SimCardId.DefendSilent, ApplyDefend },
        { SimCardId.DefendDefect, ApplyDefend },
        { SimCardId.DefendRegent, ApplyDefend },
        { SimCardId.DefendNecrobinder, ApplyDefend },
        { SimCardId.Bash, ApplyBash },
        { SimCardId.IronWave, ApplyIronWave },
        { SimCardId.Clash, ApplyClash },
        { SimCardId.Thunderclap, ApplyThunderclap },
        { SimCardId.TwinStrike, ApplyTwinStrike },
        { SimCardId.Anger, ApplyAnger },
        { SimCardId.ShrugItOff, ApplyShrugItOff },
        { SimCardId.BattleTrance, ApplyBattleTrance },
        { SimCardId.PommelStrike, ApplyPommelStrike },
        { SimCardId.DaggerSpray, ApplyDaggerSpray },
        { SimCardId.Deflect, ApplyDeflect },
        { SimCardId.SuckerPunch, ApplySuckerPunch },
        { SimCardId.PoisonedStab, ApplyPoisonedStab },
        { SimCardId.Neutralize, ApplyNeutralize },
        { SimCardId.Backflip, ApplyBackflip },
        { SimCardId.Uppercut, ApplyUppercut },
        { SimCardId.Slimed, ApplySlimed },
        { SimCardId.CloakAndDagger, ApplyCloakAndDagger },
        { SimCardId.BladeDance, ApplyBladeDance },
        { SimCardId.Predator, ApplyPredator },
        { SimCardId.Impatience, ApplyImpatience },
        { SimCardId.InfiniteBlades, ApplyInfiniteBlades },
        { SimCardId.Flechettes, ApplyFlechettes },
        { SimCardId.Barricade, ApplyBarricade },
        { SimCardId.FeelNoPain, ApplyFeelNoPain },
        { SimCardId.Rage, ApplyRage },
        { SimCardId.Bloodletting, ApplyBloodletting },
        { SimCardId.Hemokinesis, ApplyHemokinesis },
        { SimCardId.Rupture, ApplyRupture },
        { SimCardId.Corruption, ApplyCorruption },
        { SimCardId.SecondWind, ApplySecondWind },
        { SimCardId.Entrench, ApplyEntrench },
        { SimCardId.FlameBarrier, ApplyFlameBarrier },
        { SimCardId.FiendFire, ApplyFiendFire },
        { SimCardId.Bludgeon, ApplyBludgeon },
        { SimCardId.Feed, ApplyFeed },
        { SimCardId.Reflex, ApplyReflex },
        { SimCardId.Adrenaline, ApplyAdrenaline },
        { SimCardId.Afterimage, ApplyAfterimage },
        { SimCardId.PiercingWail, ApplyPiercingWail },
        { SimCardId.Caltrops, ApplyCaltrops },
        { SimCardId.FlashOfSteel, ApplyFlashOfSteel },
        { SimCardId.Panache, ApplyPanache },
        { SimCardId.GrandFinale, ApplyGrandFinale },
        { SimCardId.MasterOfStrategy, ApplyMasterOfStrategy },
        { SimCardId.Accelerant, ApplyAccelerant },
        { SimCardId.Accuracy, ApplyAccuracy },
        { SimCardId.Aggression, ApplyAggression },
        { SimCardId.AstralPulse, ApplyAstralPulse },
        { SimCardId.BeaconOfHope, ApplyBeaconOfHope },
        { SimCardId.BlackHole, ApplyBlackHole },
        { SimCardId.Break, ApplyBreak },
        { SimCardId.Breakthrough, ApplyBreakthrough },
        { SimCardId.Buffer, ApplyBuffer },
        { SimCardId.BiasedCognition, ApplyBiasedCognition },
        { SimCardId.Abrasive, ApplyAbrasive },
        { SimCardId.Calamity, ApplyCalamity },
        { SimCardId.Calcify, ApplyCalcify },
        { SimCardId.CallOfTheVoid, ApplyCallOfTheVoid },
        { SimCardId.ByrdSwoop, ApplyByrdSwoop },
        { SimCardId.Bury, ApplyBury },
        { SimCardId.ChildOfTheStars, ApplyChildOfTheStars },
        { SimCardId.CloakOfStars, ApplyCloakOfStars },
        { SimCardId.Coolant, ApplyCoolant },
        { SimCardId.Countdown, ApplyCountdown },
        { SimCardId.CorrosiveWave, ApplyCorrosiveWave },
        { SimCardId.CreativeAi, ApplyCreativeAi },
        { SimCardId.CrimsonMantle, ApplyCrimsonMantle },
        { SimCardId.Cruelty, ApplyCruelty },
        { SimCardId.DanseMacabre, ApplyDanseMacabre },
        { SimCardId.DarkEmbrace, ApplyDarkEmbrace },
        { SimCardId.Defile, ApplyDefile },
        { SimCardId.Defragment, ApplyDefragment },
        { SimCardId.DarkShackles, ApplyDarkShackles },
        { SimCardId.DeadlyPoison, ApplyDeadlyPoison },
        { SimCardId.Deathbringer, ApplyDeathbringer },
        { SimCardId.Debilitate, ApplyDebilitate },
        { SimCardId.CelestialMight, ApplyCelestialMight },
        { SimCardId.Conflagration, ApplyConflagration },
        { SimCardId.Dash, ApplyDash },
        { SimCardId.Devastate, ApplyDevastate },
        { SimCardId.DevourLife, ApplyDevourLife },
        { SimCardId.Dismantle, ApplyDismantle },
        { SimCardId.DodgeAndRoll, ApplyDodgeAndRoll },
        { SimCardId.Envenom, ApplyEnvenom },
        { SimCardId.EternalArmor, ApplyEternalArmor },
        { SimCardId.Feral, ApplyFeral },
        { SimCardId.FlickFlack, ApplyFlickFlack },
        { SimCardId.Footwork, ApplyFootwork },
        { SimCardId.ForbiddenGrimoire, ApplyForbiddenGrimoire },
        { SimCardId.ForegoneConclusion, ApplyForegoneConclusion },
        { SimCardId.Furnace, ApplyFurnace },
        { SimCardId.Fear, ApplyFear },
        { SimCardId.Flanking, ApplyFlanking },
        { SimCardId.Fasten, ApplyFasten },
        { SimCardId.EnfeeblingTouch, ApplyEnfeeblingTouch },
        { SimCardId.GatherLight, ApplyGatherLight },
        { SimCardId.Friendship, ApplyFriendship },
        { SimCardId.FanOfKnives, ApplyFanOfKnives },
        { SimCardId.Genesis, ApplyGenesis },
        { SimCardId.GiantRock, ApplyGiantRock },
        { SimCardId.Hailstorm, ApplyHailstorm },
        { SimCardId.HammerTime, ApplyHammerTime },
        { SimCardId.Haunt, ApplyHaunt },
        { SimCardId.Haze, ApplyHaze },
        { SimCardId.HelloWorld, ApplyHelloWorld },
        { SimCardId.Hellraiser, ApplyHellraiser },
        { SimCardId.HowlFromBeyond, ApplyHowlFromBeyond },
        { SimCardId.Impervious, ApplyImpervious },
        { SimCardId.Inferno, ApplyInferno },
        { SimCardId.Inflame, ApplyInflame },
        { SimCardId.Iteration, ApplyIteration },
        { SimCardId.Juggernaut, ApplyJuggernaut },
        { SimCardId.Juggling, ApplyJuggling },
        { SimCardId.KinglyKick, ApplyKinglyKick },
        { SimCardId.Leap, ApplyLeap },
        { SimCardId.GoForTheEyes, ApplyGoForTheEyes },
        { SimCardId.Hyperbeam, ApplyHyperbeam },
        { SimCardId.Knockdown, ApplyKnockdown },
        { SimCardId.KnowThyPlace, ApplyKnowThyPlace },
        { SimCardId.LeadingStrike, ApplyLeadingStrike },
        { SimCardId.LegSweep, ApplyLegSweep },
        { SimCardId.Lethality, ApplyLethality },
        { SimCardId.LightningRod, ApplyLightningRod },
        { SimCardId.Loop, ApplyLoop },
        { SimCardId.Mangle, ApplyMangle },
        { SimCardId.MasterPlanner, ApplyMasterPlanner },
        { SimCardId.Mayhem, ApplyMayhem },
        { SimCardId.MinionDiveBomb, ApplyMinionDiveBomb },
        { SimCardId.MinionSacrifice, ApplyMinionSacrifice },
        { SimCardId.MinionStrike, ApplyMinionStrike },
        { SimCardId.MomentumStrike, ApplyMomentumStrike },
        { SimCardId.MonarchsGaze, ApplyMonarchsGaze },
        { SimCardId.Monologue, ApplyMonologue },
        { SimCardId.MoltenFist, ApplyMoltenFist },
        { SimCardId.Nostalgia, ApplyNostalgia },
        { SimCardId.NoxiousFumes, ApplyNoxiousFumes },
        { SimCardId.OneTwoPunch, ApplyOneTwoPunch },
        { SimCardId.Orbit, ApplyOrbit },
        { SimCardId.Outbreak, ApplyOutbreak },
        { SimCardId.Outmaneuver, ApplyOutmaneuver },
        { SimCardId.Pagestorm, ApplyPagestorm },
        { SimCardId.PaleBlueDot, ApplyPaleBlueDot },
        { SimCardId.Parry, ApplyParry },
        { SimCardId.Parse, ApplyParse },
        { SimCardId.PhantomBlades, ApplyPhantomBlades },
        { SimCardId.Pillage, ApplyPillage },
        { SimCardId.PillarOfCreation, ApplyPillarOfCreation },
        { SimCardId.Pinpoint, ApplyPinpoint },
        { SimCardId.PrepTime, ApplyPrepTime },
        { SimCardId.Production, ApplyProduction },
        { SimCardId.Prophesize, ApplyProphesize },
        { SimCardId.Prowess, ApplyProwess },
        { SimCardId.Reap, ApplyReap },
        { SimCardId.ReaperForm, ApplyReaperForm },
        { SimCardId.Rebound, ApplyRebound },
        { SimCardId.Reflect, ApplyReflect },
        { SimCardId.RollingBoulder, ApplyRollingBoulder },
        { SimCardId.Royalties, ApplyRoyalties },
        { SimCardId.Salvo, ApplySalvo },
        { SimCardId.Scare, ApplyScare },
        { SimCardId.Scourge, ApplyScourge },
        { SimCardId.SentryMode, ApplySentryMode },
        { SimCardId.SerpentForm, ApplySerpentForm },
        { SimCardId.SetupStrike, ApplySetupStrike },
        { SimCardId.ShadowStep, ApplyShadowStep },
        { SimCardId.Shockwave, ApplyShockwave },
        { SimCardId.SignalBoost, ApplySignalBoost },
        { SimCardId.SolarStrike, ApplySolarStrike },
        { SimCardId.Speedster, ApplySpeedster },
        { SimCardId.Squash, ApplySquash },
        { SimCardId.Stampede, ApplyStampede },
        { SimCardId.Strangle, ApplyStrangle },
        { SimCardId.Sunder, ApplySunder },
        { SimCardId.Supercritical, ApplySupercritical },
        { SimCardId.Suppress, ApplySuppress },
        { SimCardId.Synthesis, ApplySynthesis },
        { SimCardId.Tactician, ApplyTactician },
        { SimCardId.TagTeam, ApplyTagTeam },
        { SimCardId.Taunt, ApplyTaunt },
        { SimCardId.Thunder, ApplyThunder },
        { SimCardId.ToolsOfTheTrade, ApplyToolsOfTheTrade },
        { SimCardId.Tracking, ApplyTracking },
        { SimCardId.TrashToTreasure, ApplyTrashToTreasure },
        { SimCardId.Tremble, ApplyTremble },
        { SimCardId.Tyranny, ApplyTyranny },
        { SimCardId.UltimateDefend, ApplyUltimateDefend },
        { SimCardId.UltimateStrike, ApplyUltimateStrike },
        { SimCardId.Unmovable, ApplyUnmovable },
        { SimCardId.Unrelenting, ApplyUnrelenting },
        { SimCardId.Untouchable, ApplyUntouchable },
        { SimCardId.Veilpiercer, ApplyVeilpiercer },
        { SimCardId.Venerate, ApplyVenerate },
        { SimCardId.Vicious, ApplyVicious },
        { SimCardId.WellLaidPlans, ApplyWellLaidPlans },
        { SimCardId.WraithForm, ApplyWraithForm },
        { SimCardId.Arsenal, ApplyArsenal },
        { SimCardId.Automation, ApplyAutomation },
        { SimCardId.Backstab, ApplyBackstab },
        { SimCardId.BansheesCry, ApplyBansheesCry },
        { SimCardId.BeamCell, ApplyBeamCell },
        { SimCardId.BloodWall, ApplyBloodWall },
        { SimCardId.Blur, ApplyBlur },
        { SimCardId.BootSequence, ApplyBootSequence },
        { SimCardId.BorrowedTime, ApplyBorrowedTime },
        { SimCardId.BubbleBubble, ApplyBubbleBubble },
        { SimCardId.Anticipate, ApplyAnticipate },
        { SimCardId.Apparition, ApplyApparition },
        { SimCardId.CollisionCourse, ApplyCollisionCourse },
        { SimCardId.Debris, ApplyDebris },
        { SimCardId.Demesne, ApplyDemesne },
        { SimCardId.DemonForm, ApplyDemonForm },
        { SimCardId.EchoForm, ApplyEchoForm },
        { SimCardId.Entropy, ApplyEntropy },
        { SimCardId.Equilibrium, ApplyEquilibrium },
        { SimCardId.FeedingFrenzy, ApplyFeedingFrenzy },
        { SimCardId.FocusedStrike, ApplyFocusedStrike },
        { SimCardId.HiddenCache, ApplyHiddenCache },
        { SimCardId.EndOfDays, ApplyEndOfDays },
        { SimCardId.Hang, ApplyHang },
        { SimCardId.EscapePlan, ApplyEscapePlan },
        { SimCardId.MachineLearning, ApplyMachineLearning },
        { SimCardId.NegativePulse, ApplyNegativePulse },
        { SimCardId.NeutronAegis, ApplyNeutronAegis },
        { SimCardId.Oblivion, ApplyOblivion },
        { SimCardId.Pyre, ApplyPyre },
        { SimCardId.Relax, ApplyRelax },
        { SimCardId.Resonance, ApplyResonance },
        { SimCardId.PanicButton, ApplyPanicButton },
        { SimCardId.Putrefy, ApplyPutrefy },
        { SimCardId.Shroud, ApplyShroud },
        { SimCardId.SleightOfFlesh, ApplySleightOfFlesh },
        { SimCardId.Smokestack, ApplySmokestack },
        { SimCardId.SpectrumShift, ApplySpectrumShift },
        { SimCardId.SpiritOfAsh, ApplySpiritOfAsh },
        { SimCardId.Stratagem, ApplyStratagem },
        { SimCardId.Subroutine, ApplySubroutine },
        { SimCardId.StoneArmor, ApplyStoneArmor },
        { SimCardId.Storm, ApplyStorm },
        { SimCardId.SwordSage, ApplySwordSage },
        { SimCardId.TheSealedThrone, ApplyTheSealedThrone },
        { SimCardId.Snakebite, ApplySnakebite },
        { SimCardId.Sow, ApplySow },
        { SimCardId.Stomp, ApplyStomp },
        { SimCardId.Bombardment, ApplyBombardment },
        { SimCardId.BoostAway, ApplyBoostAway },
        { SimCardId.BrightestFlame, ApplyBrightestFlame },
        { SimCardId.ChargeBattery, ApplyChargeBattery },
        { SimCardId.Colossus, ApplyColossus },
        { SimCardId.Convergence, ApplyConvergence },
        { SimCardId.CrushUnder, ApplyCrushUnder },
        { SimCardId.Defy, ApplyDefy },
        { SimCardId.Delay, ApplyDelay },
        { SimCardId.Dominate, ApplyDominate },
        { SimCardId.DrumOfBattle, ApplyDrumOfBattle },
        { SimCardId.EchoingSlash, ApplyEchoingSlash },
        { SimCardId.Exterminate, ApplyExterminate },
        { SimCardId.FightThrough, ApplyFightThrough },
        { SimCardId.GammaBlast, ApplyGammaBlast },
        { SimCardId.Glitterstream, ApplyGlitterstream },
        { SimCardId.Glow, ApplyGlow },
        { SimCardId.GunkUp, ApplyGunkUp },
        { SimCardId.Hegemony, ApplyHegemony },
        { SimCardId.Hotfix, ApplyHotfix },
        { SimCardId.Invoke, ApplyInvoke },
        { SimCardId.KnockoutBlow, ApplyKnockoutBlow },
        { SimCardId.Melancholy, ApplyMelancholy },
        { SimCardId.Overclock, ApplyOverclock },
        { SimCardId.PactsEnd, ApplyPactsEnd },
        { SimCardId.Peck, ApplyPeck },
        { SimCardId.Restlessness, ApplyRestlessness },
        { SimCardId.RocketPunch, ApplyRocketPunch },
        { SimCardId.SevenStars, ApplySevenStars },
        { SimCardId.Shadowmeld, ApplyShadowmeld },
        { SimCardId.SharedFate, ApplySharedFate },
        { SimCardId.Alignment, ApplyAlignment },
        { SimCardId.Bolas, ApplyBolas },
        { SimCardId.MindBlast, ApplyMindBlast },
        { SimCardId.Bully, ApplyBully },
        { SimCardId.AshenStrike, ApplyAshenStrike },
        { SimCardId.TimesUp, ApplyTimesUp },
        { SimCardId.BodySlam, ApplyBodySlam },
        { SimCardId.Assassinate, ApplyAssassinate },
        { SimCardId.Comet, ApplyComet },
        { SimCardId.DramaticEntrance, ApplyDramaticEntrance },
        { SimCardId.DyingStar, ApplyDyingStar },
        { SimCardId.ExpectAFight, ApplyExpectAFight },
        { SimCardId.FallingStar, ApplyFallingStar },
        { SimCardId.FightMe, ApplyFightMe },
        { SimCardId.Finesse, ApplyFinesse },
        { SimCardId.GuidingStar, ApplyGuidingStar },
        { SimCardId.MeteorShower, ApplyMeteorShower },
        { SimCardId.NotYet, ApplyNotYet },
        { SimCardId.Offering, ApplyOffering },
        { SimCardId.Skim, ApplySkim },
        { SimCardId.Slice, ApplySlice },
        { SimCardId.SweepingBeam, ApplySweepingBeam },
        { SimCardId.TheGambit, ApplyTheGambit },
        { SimCardId.Burst, ApplyBurst },
        { SimCardId.Expertise, ApplyExpertise },
        { SimCardId.DoubleEnergy, ApplyDoubleEnergy },
        { SimCardId.Prolong, ApplyProlong },
        { SimCardId.Neurosurge, ApplyNeurosurge },
        { SimCardId.Wisp, ApplyWisp },
        { SimCardId.Fuel, ApplyFuel },
        { SimCardId.Luminesce, ApplyLuminesce },
        { SimCardId.Expose, ApplyExpose },
        { SimCardId.Shiv, ApplyShiv },
        { SimCardId.Soul, ApplySoul },
        { SimCardId.Apotheosis, ApplyApotheosis },
        { SimCardId.Fisticuffs, ApplyFisticuffs },
        { SimCardId.Mirage, ApplyMirage },
        { SimCardId.Misery, ApplyMisery },
        { SimCardId.Omnislice, ApplyOmnislice },
        { SimCardId.BlightStrike, ApplyBlightStrike },
        { SimCardId.CalculatedGamble, ApplyCalculatedGamble },
        { SimCardId.Compact, ApplyCompact },
        { SimCardId.CrashLanding, ApplyCrashLanding },
        { SimCardId.CrescentSpear, ApplyCrescentSpear },
        { SimCardId.Eidolon, ApplyEidolon },
        { SimCardId.NoEscape, ApplyNoEscape },
        { SimCardId.Patter, ApplyPatter },
        { SimCardId.Pounce, ApplyPounce },
        { SimCardId.PreciseCut, ApplyPreciseCut },
        { SimCardId.PrimalForce, ApplyPrimalForce },
        { SimCardId.PerfectedStrike, ApplyPerfectedStrike },
        { SimCardId.RoyalGamble, ApplyRoyalGamble },
        { SimCardId.Scrawl, ApplyScrawl },
        { SimCardId.SoulStorm, ApplySoulStorm },
        { SimCardId.SporeMind, ApplySporeMind },
        { SimCardId.Stack, ApplyStack },
        { SimCardId.StormOfSteel, ApplyStormOfSteel },
        { SimCardId.Terraforming, ApplyTerraforming },
        { SimCardId.Toxic, ApplyToxic },
        { SimCardId.Turbo, ApplyTurbo },
        { SimCardId.Undeath, ApplyUndeath },
        { SimCardId.Eradicate, ApplyEradicate },
        { SimCardId.HeavenlyDrill, ApplyHeavenlyDrill },
        { SimCardId.Skewer, ApplySkewer },
        { SimCardId.Whirlwind, ApplyWhirlwind },
    }.ToFrozenDictionary();

    /// <summary>True if <paramref name="baseCardId"/> has a registered effect. Lets
    /// <see cref="SimCardEffectVerifier"/> shadow-check only the cards we actually implement,
    /// without throwing on every other card the player happens to play.</summary>
    public static bool IsRegistered(ushort baseCardId) => _byId.ContainsKey(baseCardId);

    /// <summary>Applies <paramref name="card"/>'s effect to <paramref name="state"/> in place.
    /// <paramref name="targetEnemyIdx"/> is ignored by self-targeted cards. Throws if this card's
    /// effect hasn't been captured yet — see class doc.</summary>
    public static void Apply(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        if (!_byId.TryGetValue(card.BaseCardId, out EffectApplier? applier))
        {
            throw new InvalidOperationException(
                $"SimCardEffects: card id {card.BaseCardId} has no registered effect yet. " +
                "This registry is intentionally partial (grows one card at a time) but must fail " +
                "loud on a gap rather than let the search silently treat an unimplemented card as a no-op.");
        }
        applier(state, in card, targetEnemyIdx);
    }

    /// <summary>
    /// Full play of the card sitting at <paramref name="handIndex"/> in Hand: pays its Energy cost,
    /// resolves its effect, THEN routes the played card itself to Discard/Exhaust/removed — mirrors
    /// <see cref="MegaCrit.Sts2.Core.Models.CardModel.OnPlayWrapper"/>'s order (resolve cost →
    /// OnPlay → cleanup → pile move). This is the entry point the search engine should call, not
    /// <see cref="Apply"/> alone — calling <c>Apply</c> by itself resolves the effect but neither
    /// pays for the card nor routes it out of Hand, which is wrong for every card, not just a
    /// missing edge case.
    ///
    /// Energy is a hard precondition, not a soft clamp: <see cref="SimCardEnergyOps.GetAmountToSpend"/>
    /// already mirrors the real game's resolved cost (X-cost, local modifiers, temporary
    /// reductions — all snapshotted and verified against <c>CardEnergyCost</c>, see
    /// <see cref="SimCardEnergyOps"/>/<see cref="CombatBlobVerifier"/>). If the caller offers a play
    /// the player couldn't actually afford, that's a bug in the caller (the future search engine),
    /// not something to silently clamp here — fail loud instead.
    /// </summary>
    public static void PlayCard(CombatNodeBlob state, int handIndex, int targetEnemyIdx)
    {
        SimCard card = state.HandCards[handIndex];

        int cost = SimCardEnergyOps.GetAmountToSpend(state, in card);
        if (state.Energy < cost)
        {
            throw new InvalidOperationException(
                $"SimCardEffects.PlayCard: card {card.BaseCardId} (instance {card.InstanceId}) costs " +
                $"{cost} Energy but only {state.Energy} is available — caller offered an illegal play.");
        }
        state.Energy = (ushort)(state.Energy - cost);
        if (card.HasEnergyCostX)
            state.CardEnergyCapturedX[card.InstanceId] = (ushort)cost;

        Apply(state, in card, targetEnemyIdx);
        SimCardEnergyOps.AfterCardPlayedCleanup(state, in card);
        SimCardPileOps.ResolvePlayedCardDestination(state, handIndex);
    }

    // ── Shared primitives ────────────────────────────────────────────────────────────────────

    private static int PlayerStrength(CombatNodeBlob state)
        => SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Strength, out short amt) ? amt : 0;

    private static bool PlayerWeak(CombatNodeBlob state)
        => SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Weak, out _);

    private static bool EnemyVulnerable(CombatNodeBlob state, int idx)
        => SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.Vulnerable, out _);

    /// <summary>The MINIMUM of every applicable ModifyDamageCap on this enemy (Intangible always
    /// caps at 1; HardToKill caps at its own stack Amount) — null if neither is present. Confirmed
    /// against Hook.ModifyDamageInternal: multiple caps combine by taking the smallest, not by
    /// priority order, so Math.Min is the correct combinator if both ever coexist.</summary>
    private static int? EnemyDamageCap(CombatNodeBlob state, int idx)
    {
        int? cap = null;
        if (SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.Intangible, out _))
            cap = 1;
        if (SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.HardToKill, out short hardToKillAmt))
            cap = cap.HasValue ? Math.Min(cap.Value, hardToKillAmt) : hardToKillAmt;
        return cap;
    }

    /// <summary>Mirrors CreatureCmd.Damage's net effect on an enemy: block absorbs first
    /// (min(block, dmg)), the remainder subtracts from HP, floored at 0. Ignores Unblockable —
    /// none of the cards registered so far use it; add a parameter when one does.</summary>
    /// <summary>Returns the total post-modifier damage computed (matches the real game's
    /// <c>DamageResult.TotalDamage + DamageResult.OverkillDamage</c> — the full amount before block/HP
    /// clamping), for callers that need it (e.g. Fisticuffs' block-equal-to-damage-dealt).</summary>
    private static int DealDamageToEnemy(CombatNodeBlob state, int idx, int rawDamage)
    {
        int total = SimDamage.Compute(rawDamage, PlayerStrength(state), EnemyVulnerable(state, idx), PlayerWeak(state), EnemyDamageCap(state, idx));
        return ApplyComputedDamageToEnemy(state, idx, total);
    }

    /// <summary>Deals already-computed damage that skips the Additive/Multiplicative phases
    /// (ValueProp.Unpowered — no Strength/Vulnerable/Weak) but still runs the receiving enemy's own
    /// Cap phase (Intangible/HardToKill are unconditional ModifyDamageCap hooks, not gated on
    /// Powered/Unpowered) and normal Block absorption. Used by Omnislice's unpowered splash hit.</summary>
    private static int DealUnpoweredDamageToEnemy(CombatNodeBlob state, int idx, int rawAmount)
    {
        int total = SimDamage.Compute(rawAmount, 0, targetVulnerable: false, dealerWeak: false, EnemyDamageCap(state, idx));
        return ApplyComputedDamageToEnemy(state, idx, total);
    }

    private static int ApplyComputedDamageToEnemy(CombatNodeBlob state, int idx, int total)
    {
        if (total <= 0) return Math.Max(total, 0);

        ushort block = state.EnemyBlock[idx];
        int absorbed = Math.Min(block, total);
        state.EnemyBlock[idx] = (ushort)(block - absorbed);

        int leftover = total - absorbed;
        if (leftover <= 0) return total;
        ushort hp = state.EnemyHp[idx];
        state.EnemyHp[idx] = (ushort)Math.Max(0, hp - leftover);
        return total;
    }

    /// <summary>Mirrors Creature.GainBlockInternal for the player.</summary>
    /// <summary>Returns the actual post-modifier amount of block gained (0 if none) — most callers
    /// ignore this, but a few cards (e.g. DodgeAndRoll) key a follow-up effect off the real
    /// gained amount rather than the raw requested one.</summary>
    private static int GainPlayerBlock(CombatNodeBlob state, int rawAmount)
    {
        int dexterity = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Dexterity, out short dexAmt) ? dexAmt : 0;
        bool frail = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Frail, out _);
        bool noBlock = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.NoBlock, out _);
        int amount = SimBlock.Compute(rawAmount, dexterity, frail, noBlock);
        if (amount <= 0) return 0;
        state.PlayerBlock = (ushort)Math.Min(999999999, state.PlayerBlock + amount);
        return amount;
    }

    /// <summary>Block gain that bypasses Dexterity/Frail/NoBlock entirely (ValueProp.Unpowered) —
    /// used by cards that explicitly opt out of the standard formula, e.g. Entrench doubling
    /// current Block. Do not use this for a normal card's block gain; see GainPlayerBlock.</summary>
    private static void GainPlayerBlockUnpowered(CombatNodeBlob state, int rawAmount)
    {
        if (rawAmount <= 0) return;
        state.PlayerBlock = (ushort)Math.Min(999999999, state.PlayerBlock + rawAmount);
    }

    /// <summary>Deals <paramref name="rawDamage"/> to <paramref name="idx"/>, <paramref name="hits"/>
    /// separate times — each hit independently absorbed by whatever block remains at that moment,
    /// matching the game's per-hit block interaction (not "compute total then subtract once").</summary>
    private static void DealMultiHitDamageToEnemy(CombatNodeBlob state, int idx, int rawDamage, int hits)
    {
        for (int i = 0; i < hits; i++)
            DealDamageToEnemy(state, idx, rawDamage);
    }


    // ── Card writers ─────────────────────────────────────────────────────────────────────────

    // Strike (all 5 characters — identical formula, confirmed in game_source): 6 damage,
    // +3 when upgraded. Whole effect is the single DamageCmd.Attack call, nothing else.
    private static void ApplyStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 9 : 6;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Defend (all 5 characters — identical formula): 5 block, +3 when upgraded. Whole effect is
    // the single CreatureCmd.GainBlock call, self-targeted (targetEnemyIdx unused).
    private static void ApplyDefend(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 8 : 5;
        GainPlayerBlock(state, block);
    }

    // Bash: 8 damage (+2 upgraded → 10) then 2 Vulnerable (+1 upgraded → 3) to the same target.
    // Whole effect is DamageCmd.Attack + PowerCmd.Apply<VulnerablePower>, nothing else.
    private static void ApplyBash(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 8;
        int vuln = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // Anger: 6 damage (+2 upgraded → 8) to target, then clones itself (same upgrade state) into
    // the bottom of the discard pile. Whole effect is DamageCmd.Attack + CreateClone/AddToCombat.
    private static void ApplyAnger(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 6;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);

        SimCardPileOps.AppendGenerated(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, card.BaseCardId, card.IsUpgraded);
    }

    // IronWave: gains 5 block (+2 upgraded → 7) then deals 5 damage (+2 upgraded → 7) to target.
    // Whole effect is CreatureCmd.GainBlock + DamageCmd.Attack, nothing else.
    private static void ApplyIronWave(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int val = card.IsUpgraded ? 7 : 5;
        GainPlayerBlock(state, val);
        DealDamageToEnemy(state, targetEnemyIdx, val);
    }

    // Clash: 14 damage (+4 upgraded → 18), single hit. Whole effect is one DamageCmd.Attack call.
    // (Clash's hand-must-be-all-attacks restriction is a playability gate, not part of the effect
    // itself — the search layer's legality check is a separate concern from this applier.)
    private static void ApplyClash(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 18 : 14;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Thunderclap: 4 damage (+3 upgraded → 7) AND 1 Vulnerable to EVERY living enemy
    // (Vulnerable amount is not upgradeable — only Damage has an OnUpgrade call). targetEnemyIdx
    // is unused; this hits every living slot instead of a single chosen target.
    private static void ApplyThunderclap(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 7 : 4;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Vulnerable, 1);
        }
    }

    // TwinStrike: 5 damage (+2 upgraded → 7), hits twice, single target. Whole effect is one
    // DamageCmd.Attack(...).WithHitCount(2) call — each hit independently absorbed by whatever
    // block remains, per DealMultiHitDamageToEnemy.
    private static void ApplyTwinStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 7 : 5;
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, 2);
    }

    // ShrugItOff: gains 8 block (+3 upgraded → 11), then draws 1 (draw count never upgrades).
    // Whole effect is CreatureCmd.GainBlock + CardPileCmd.Draw, nothing else.
    private static void ApplyShrugItOff(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 11 : 8;
        GainPlayerBlock(state, block);
        SimCardPileOps.DrawCards(state, 1);
    }

    // BattleTrance: draws 3 (+1 upgraded → 4), then gives itself 1 NoDrawPower (blocks any further
    // draw this turn). Whole effect is CardPileCmd.Draw + PowerCmd.Apply<NoDrawPower>.
    private static void ApplyBattleTrance(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 4 : 3;
        SimCardPileOps.DrawCards(state, draws);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.NoDraw, 1);
    }

    // PommelStrike: 9 damage (+1 upgraded → 10) to target, then draws 1 (draw count never
    // upgrades). Whole effect is DamageCmd.Attack + CardPileCmd.Draw, nothing else.
    private static void ApplyPommelStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 9;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimCardPileOps.DrawCards(state, 1);
    }

    // DaggerSpray: 4 damage (+2 upgraded → 6), 2 hits, to EVERY living enemy (hit count itself
    // never upgrades). targetEnemyIdx unused — hits every living slot instead of one target.
    private static void ApplyDaggerSpray(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 6 : 4;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealMultiHitDamageToEnemy(state, i, dmg, 2);
        }
    }

    // Deflect: gains 4 block (+3 upgraded → 7). Whole effect is the single GainBlock call.
    private static void ApplyDeflect(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 7 : 4;
        GainPlayerBlock(state, block);
    }

    // SuckerPunch: 8 damage (+2 upgraded → 10) then 1 Weak (+1 upgraded → 2) to target. Whole
    // effect is DamageCmd.Attack + PowerCmd.Apply<WeakPower>, nothing else.
    private static void ApplySuckerPunch(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 8;
        int weak = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, weak);
    }

    // PoisonedStab: 6 damage (+2 upgraded → 8) then 3 Poison (+1 upgraded → 4) to target. Whole
    // effect is DamageCmd.Attack + PowerCmd.Apply<PoisonPower>, nothing else.
    private static void ApplyPoisonedStab(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 6;
        int poison = card.IsUpgraded ? 4 : 3;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Poison, poison);
    }

    // Neutralize: 3 damage (+1 upgraded → 4) then 1 Weak (+1 upgraded → 2) to target. Whole
    // effect is DamageCmd.Attack + PowerCmd.Apply<WeakPower>, nothing else.
    private static void ApplyNeutralize(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 4 : 3;
        int weak = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, weak);
    }

    // Backflip: gains 5 block (+3 upgraded → 8), then draws 2 (draw count never upgrades). Whole
    // effect is CreatureCmd.GainBlock + CardPileCmd.Draw, nothing else.
    private static void ApplyBackflip(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 8 : 5;
        GainPlayerBlock(state, block);
        SimCardPileOps.DrawCards(state, 2);
    }

    // Uppercut: 13 damage (fixed — only the Weak/Vulnerable amount upgrades, not damage) then
    // 1 Weak AND 1 Vulnerable (+1 upgraded → 2, same "Power" var drives both) to target.
    private static void ApplyUppercut(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int power = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, 13);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, power);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, power);
    }

    // Slimed (a Status card): draws 1 card (fixed — MaxUpgradeLevel is 0, this card never
    // upgrades). Whole effect is the single CardPileCmd.Draw call; its Exhaust keyword is handled
    // generically by SimCardPileOps.ResolvePlayedCardDestination, not here.
    private static void ApplySlimed(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimCardPileOps.DrawCards(state, 1);
    }

    // CloakAndDagger: gains 6 block (fixed — only the Shiv count upgrades, not block), then
    // generates 1 Shiv (+1 upgraded → 2) directly into HAND (not Discard).
    private static void ApplyCloakAndDagger(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        GainPlayerBlock(state, 6);
        int shivs = card.IsUpgraded ? 2 : 1;
        for (int i = 0; i < shivs; i++)
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Shiv, upgraded: false);
    }

    // BladeDance: generates 3 Shiv (+1 upgraded → 4) directly into Hand. Self-Exhausts (handled
    // generically by SimCardPileOps.ResolvePlayedCardDestination via its Exhaust keyword).
    private static void ApplyBladeDance(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int shivs = card.IsUpgraded ? 4 : 3;
        for (int i = 0; i < shivs; i++)
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Shiv, upgraded: false);
    }

    // Predator: 15 damage (+5 upgraded → 20) to target, then gives itself 2 DrawCardsNextTurn
    // (fixed — not upgradeable). Whole effect is DamageCmd.Attack + PowerCmd.Apply, nothing else.
    private static void ApplyPredator(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 20 : 15;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DrawCardsNextTurn, 2);
    }

    // Impatience: draws 2 (+1 upgraded → 3) ONLY if hand currently has no Attack-type card.
    // Whole effect is the single conditional CardPileCmd.Draw call.
    private static void ApplyImpatience(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int count = state.HandCount;
        for (int i = 0; i < count; i++)
        {
            if (SimCardTypeRegistry.Get(state.HandCards[i].BaseCardId) == SimCardType.Attack)
                return;
        }
        int draws = card.IsUpgraded ? 3 : 2;
        SimCardPileOps.DrawCards(state, draws);
    }

    // InfiniteBlades (a Power card): gives itself 1 InfiniteBladesPower (fixed — upgrading this
    // card only adds the Innate keyword, a static property, not a play-time effect change). Whole
    // effect is the single PowerCmd.Apply call; the card's own Power-type removal from Hand is
    // handled generically by SimCardPileOps.ResolvePlayedCardDestination.
    private static void ApplyInfiniteBlades(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.InfiniteBlades, 1);
    }

    // Flechettes: 5 damage (+2 upgraded → 7), hit count = number of Skill-type cards currently in
    // hand (Flechettes itself is an Attack card, so it never counts itself). Whole effect is the
    // single DamageCmd.Attack(...).WithHitCount(skillCount) call.
    private static void ApplyFlechettes(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 7 : 5;
        int skillCount = 0;
        int handCount = state.HandCount;
        for (int i = 0; i < handCount; i++)
        {
            if (SimCardTypeRegistry.Get(state.HandCards[i].BaseCardId) == SimCardType.Skill)
                skillCount++;
        }
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, skillCount);
    }

    /// <summary>Direct, unmitigated HP loss to the player — no Block interaction, no Strength/
    /// Vulnerable/Weak, matching ValueProp.Unblockable|Unpowered. Does NOT check Intangible/
    /// HardToKill damage caps on the player — both are cap checks with no ValueProp gate in the
    /// real game, so technically they'd still apply here too, but no card registered so far can
    /// combo with the player having either, so this is deferred rather than guessed at.</summary>
    private static void LosePlayerHp(CombatNodeBlob state, int amount)
    {
        if (amount <= 0) return;
        ushort hp = state.PlayerHp;
        state.PlayerHp = (ushort)Math.Max(0, hp - amount);
    }

    /// <summary>Mirrors PlayerCmd.GainEnergy — no cap (current Energy can exceed MaxEnergy within
    /// a turn; MaxEnergy only governs how much you start each turn with).</summary>
    private static void GainPlayerEnergy(CombatNodeBlob state, int amount)
    {
        if (amount <= 0) return;
        state.Energy = (ushort)(state.Energy + amount);
    }

    // Barricade (a Power card): gives itself 1 Barricade (fixed). Whole effect is the single
    // PowerCmd.Apply call; the card's own Power-type removal from Hand is handled generically.
    private static void ApplyBarricade(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Barricade, 1);
    }

    // FeelNoPain (a Power card): gives itself 3 FeelNoPain (+1 upgraded → 4). Whole effect is the
    // single PowerCmd.Apply call.
    private static void ApplyFeelNoPain(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 4 : 3;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FeelNoPain, amount);
    }

    // Rage: gives itself 3 Rage (+2 upgraded → 5). Whole effect is the single PowerCmd.Apply call.
    private static void ApplyRage(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 5 : 3;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Rage, amount);
    }

    // Bloodletting: loses 3 HP (fixed, unblockable+unpowered) then gains 2 Energy (+1 upgraded →
    // 3 — upgrading changes Energy gained, not HP lost). Whole effect is
    // CreatureCmd.Damage(self, unblockable|unpowered) + PlayerCmd.GainEnergy.
    private static void ApplyBloodletting(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        LosePlayerHp(state, 3);
        int energy = card.IsUpgraded ? 3 : 2;
        GainPlayerEnergy(state, energy);
    }

    // Hemokinesis: loses 2 HP (fixed, unblockable+unpowered) then deals 15 damage (+5 upgraded →
    // 20) to target. Whole effect is CreatureCmd.Damage(self) + DamageCmd.Attack.
    private static void ApplyHemokinesis(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        LosePlayerHp(state, 2);
        int dmg = card.IsUpgraded ? 20 : 15;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Rupture (a Power card): gives itself 1 Rupture (+1 upgraded → 2). Whole effect is the single
    // PowerCmd.Apply call — the "PowerVar<StrengthPower>" naming in CanonicalVars is just this
    // var's DISPLAY label (shows a Strength icon in its tooltip), the actual power applied and
    // read at play time is RupturePower, not StrengthPower itself.
    private static void ApplyRupture(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Rupture, amount);
    }

    // Corruption (a Power card): gives itself 1 Corruption (fixed — upgrading this card only
    // reduces its Energy cost, not the Power amount, same shape as Barricade's upgrade).
    private static void ApplyCorruption(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Corruption, 1);
    }

    // SecondWind: Exhausts every non-Attack card currently in Hand, gaining 5 Block (+2 upgraded →
    // 7) per card exhausted. Iterates HandCards back-to-front so each MoveToEnd removal (which
    // shift-compacts everything after the removed index) never invalidates an index we still need
    // to visit. Explicitly skips the SecondWind card instance itself by InstanceId — it is still
    // sitting in HandCards during Apply (see the class-doc "known simplification"), and unlike
    // Flechettes/Impatience, SecondWind's own type (Skill) DOES match its own filter
    // (Type != Attack), so without this guard it would wrongly exhaust/block for itself too.
    private static void ApplySecondWind(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 7 : 5;
        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            SimCard c = state.HandCards[i];
            if (c.InstanceId == card.InstanceId) continue;
            if (SimCardTypeRegistry.Get(c.BaseCardId) == SimCardType.Attack) continue;
            SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.ExhaustCards, ref state.ExhaustCount, CombatSimLayout.PileCap);
            GainPlayerBlock(state, block);
        }
    }

    // Entrench: doubles current Block (gains Block equal to current Block), unpowered — bypasses
    // Dexterity/Frail/NoBlock entirely. No numeric upgrade (OnUpgrade only reduces Energy cost).
    private static void ApplyEntrench(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        GainPlayerBlockUnpowered(state, state.PlayerBlock);
    }

    // FlameBarrier: gains 12 Block (+4 upgraded → 16) then gives itself 4 FlameBarrier (+2
    // upgraded → 6). Whole effect is GainBlock + PowerCmd.Apply, nothing else.
    private static void ApplyFlameBarrier(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 16 : 12;
        int dmgBack = card.IsUpgraded ? 6 : 4;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FlameBarrier, dmgBack);
    }

    // FiendFire: exhausts every OTHER card currently in Hand (deterministic, no choice/RNG
    // involved), hit count = however many cards were exhausted, damage 7 per hit (+3 upgraded →
    // 10). FiendFire itself is still sitting in HandCards during Apply (documented simplification)
    // and is itself Exhaust-keyworded — excluded from both the count and the exhaust loop by
    // InstanceId, same guard as SecondWind, since its own removal is handled generically by
    // SimCardPileOps.ResolvePlayedCardDestination afterward.
    private static void ApplyFiendFire(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 7;
        int cardCount = 0;
        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            SimCard c = state.HandCards[i];
            if (c.InstanceId == card.InstanceId) continue;
            SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.ExhaustCards, ref state.ExhaustCount, CombatSimLayout.PileCap);
            cardCount++;
        }
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, cardCount);
    }

    // Bludgeon: 32 damage (+10 upgraded → 42) to target. No other effect.
    private static void ApplyBludgeon(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 42 : 32;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    /// <summary>Mirrors PowerModel.ShouldOwnerDeathTriggerFatal's default (true) plus its only two
    /// overrides (MinionPower, ReattachPower both return false) — confirmed via grep across every
    /// power in the game, not assumed. Used by Feed to decide whether a kill counts for its Max HP
    /// reward.</summary>
    private static bool ShouldTriggerFatal(CombatNodeBlob state, int idx)
    {
        bool hasMinion = SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.Minion, out _);
        bool hasReattach = SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.Reattach, out _);
        return !hasMinion && !hasReattach;
    }

    /// <summary>Mirrors CreatureCmd.GainMaxHp: raises the cap AND heals the player by the same
    /// amount (GainMaxHp calls Heal internally after SetMaxHp) — not just a ceiling raise.</summary>
    private static void GainPlayerMaxHp(CombatNodeBlob state, int amount)
    {
        if (amount <= 0) return;
        state.PlayerMaxHp = (ushort)Math.Min(999999999, state.PlayerMaxHp + amount);
        state.PlayerHp = (ushort)Math.Min(state.PlayerMaxHp, state.PlayerHp + amount);
    }

    /// <summary>Mirrors CreatureCmd.LoseMaxHp: lowers the cap (floored at 1), and if current HP now
    /// exceeds the new cap, loses the difference as unblockable HP loss to bring it down to the
    /// new cap — not just a ceiling lower with no HP consequence.</summary>
    private static void LosePlayerMaxHp(CombatNodeBlob state, int amount)
    {
        if (amount <= 0) return;
        int newMax = Math.Max(1, state.PlayerMaxHp - amount);
        if (state.PlayerHp > newMax)
            state.PlayerHp = (ushort)newMax;
        state.PlayerMaxHp = (ushort)newMax;
    }

    /// <summary>Mirrors CreatureCmd.Heal: capped so current HP never exceeds Max HP — a heal that
    /// would overflow is simply truncated, not wasted or carried over.</summary>
    private static void HealPlayer(CombatNodeBlob state, int amount)
    {
        if (amount <= 0) return;
        state.PlayerHp = (ushort)Math.Min(state.PlayerMaxHp, state.PlayerHp + amount);
    }

    // Feed: 10 damage (+2 upgraded → 12) to target; if this damage instance actually kills the
    // target AND ShouldTriggerFatal holds, gain 3 Max HP (+1 upgraded → 4), which also heals the
    // player by the same amount (see GainPlayerMaxHp).
    private static void ApplyFeed(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 12 : 10;
        bool shouldTriggerFatal = ShouldTriggerFatal(state, targetEnemyIdx);
        int hpBefore = state.EnemyHp[targetEnemyIdx];
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        bool killed = hpBefore > 0 && state.EnemyHp[targetEnemyIdx] == 0;
        if (shouldTriggerFatal && killed)
        {
            int maxHpGain = card.IsUpgraded ? 4 : 3;
            GainPlayerMaxHp(state, maxHpGain);
        }
    }

    // Reflex: draws 2 (+1 upgraded → 3). Whole effect is the single CardPileCmd.Draw call.
    private static void ApplyReflex(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 3 : 2;
        SimCardPileOps.DrawCards(state, draws);
    }

    // Adrenaline: gains 1 Energy (+1 upgraded → 2) then draws 2 (fixed — only Energy upgrades).
    // Whole effect is PlayerCmd.GainEnergy + CardPileCmd.Draw.
    private static void ApplyAdrenaline(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 2 : 1;
        GainPlayerEnergy(state, energy);
        SimCardPileOps.DrawCards(state, 2);
    }

    // Afterimage (a Power card): gives itself 1 Afterimage (fixed — upgrading this card only adds
    // the Innate keyword, a static property, not a play-time effect change).
    private static void ApplyAfterimage(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Afterimage, 1);
    }

    // PiercingWail: applies 6 PiercingWail (+2 upgraded → 8) to every living enemy. Whole effect is
    // the single PowerCmd.Apply loop over HittableEnemies — same living-enemy-only iteration shape
    // as Thunderclap/DaggerSpray.
    private static void ApplyPiercingWail(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 8 : 6;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.PiercingWail, amount);
        }
    }

    // Caltrops (a Power card): gives itself 3 Thorns (+2 upgraded → 5). Whole effect is the single
    // PowerCmd.Apply call.
    private static void ApplyCaltrops(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 5 : 3;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Thorns, amount);
    }

    // FlashOfSteel: 5 damage (+3 upgraded → 8) to target, then draws 1 (fixed — no upgrade on the
    // Cards var). Whole effect is DamageCmd.Attack + CardPileCmd.Draw.
    private static void ApplyFlashOfSteel(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 5;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimCardPileOps.DrawCards(state, 1);
    }

    // Panache (a Power card): gives itself 10 Panache (+4 upgraded → 14). Whole effect is the
    // single PowerCmd.Apply call.
    private static void ApplyPanache(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 14 : 10;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Panache, amount);
    }

    // GrandFinale: 60 damage (+15 upgraded → 75) to every living enemy. IsPlayable (draw pile must
    // be empty) is a legal-move-generation concern for the search engine, not part of what OnPlay
    // itself does — the effect body is unconditional once played.
    private static void ApplyGrandFinale(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 75 : 60;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // MasterOfStrategy: draws 3 (+1 upgraded → 4). Whole effect is the single CardPileCmd.Draw
    // call; its Exhaust keyword is handled generically by ResolvePlayedCardDestination.
    private static void ApplyMasterOfStrategy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 4 : 3;
        SimCardPileOps.DrawCards(state, draws);
    }

    // Accelerant (a Power card): gives itself 1 Accelerant (+1 upgraded → 2).
    private static void ApplyAccelerant(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Accelerant, amount);
    }

    // Accuracy (a Power card): gives itself 4 Accuracy (+2 upgraded → 6).
    private static void ApplyAccuracy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Accuracy, amount);
    }

    // Aggression (a Power card): gives itself 1 Aggression (fixed — upgrading only adds the
    // Innate keyword, a static property, not a play-time effect change).
    private static void ApplyAggression(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Aggression, 1);
    }

    // AstralPulse: 6 damage (+2 upgraded → 8), hits TWICE, to every living enemy. Whole effect is
    // the single DamageCmd.Attack(...).WithHitCount(2).TargetingAllOpponents call.
    private static void ApplyAstralPulse(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 6;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealMultiHitDamageToEnemy(state, i, dmg, 2);
        }
    }

    // BeaconOfHope (a Power card): gives itself 1 BeaconOfHope (fixed — upgrading only adds the
    // Innate keyword).
    private static void ApplyBeaconOfHope(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.BeaconOfHope, 1);
    }

    // BlackHole (a Power card): gives itself 3 BlackHole (+1 upgraded → 4). BlackHolePower's own
    // downstream orb-related behavior is that power's own concern, not this card's — applying the
    // stack itself needs no orb infrastructure.
    private static void ApplyBlackHole(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 4 : 3;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.BlackHole, amount);
    }

    // Break: 20 damage (+10 upgraded → 30) then 5 Vulnerable (+2 upgraded → 7) to target. Same
    // shape as Bash.
    private static void ApplyBreak(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 30 : 20;
        int vuln = card.IsUpgraded ? 7 : 5;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // Breakthrough: loses 1 HP (fixed, unblockable+unpowered, self) then deals 9 damage (+4
    // upgraded → 13) to every living enemy.
    private static void ApplyBreakthrough(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        LosePlayerHp(state, 1);
        int dmg = card.IsUpgraded ? 13 : 9;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // Buffer (a Power card): gives itself 1 Buffer (+1 upgraded → 2).
    private static void ApplyBuffer(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Buffer, amount);
    }

    // BiasedCognition (a Power card): gives itself 4 Focus (+1 upgraded → 5) then 1
    // BiasedCognition (fixed — only Focus upgrades).
    private static void ApplyBiasedCognition(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int focus = card.IsUpgraded ? 5 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Focus, focus);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.BiasedCognition, 1);
    }

    // Abrasive (a Power card): gives itself 1 Dexterity (fixed) then 4 Thorns (+2 upgraded → 6).
    private static void ApplyAbrasive(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Dexterity, 1);
        int thorns = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Thorns, thorns);
    }

    // Calamity (a Power card): gives itself 1 Calamity (fixed — upgrade only reduces Energy cost).
    private static void ApplyCalamity(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Calamity, 1);
    }

    // Calcify (a Power card): gives itself 4 Calcify (+2 upgraded → 6).
    private static void ApplyCalcify(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Calcify, amount);
    }

    // CallOfTheVoid (a Power card): gives itself 1 CallOfTheVoid (fixed — upgrade only adds the
    // Innate keyword).
    private static void ApplyCallOfTheVoid(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.CallOfTheVoid, 1);
    }

    // ByrdSwoop: 14 damage (+4 upgraded → 18) to target. No other effect.
    private static void ApplyByrdSwoop(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 18 : 14;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Bury: 52 damage (+11 upgraded → 63) to target. No other effect.
    private static void ApplyBury(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 63 : 52;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // ChildOfTheStars (a Power card): gives itself 2 ChildOfTheStars (+1 upgraded → 3).
    private static void ApplyChildOfTheStars(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.ChildOfTheStars, amount);
    }

    // CloakOfStars: gains 7 Block (+3 upgraded → 10). No other effect (its Star-cost is a
    // cost-payment concern, out of scope for effect execution, same as every other card here).
    private static void ApplyCloakOfStars(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 10 : 7;
        GainPlayerBlock(state, block);
    }

    // Coolant (a Power card): gives itself 2 Coolant (+1 upgraded → 3).
    private static void ApplyCoolant(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Coolant, amount);
    }

    // Countdown (a Power card): gives itself 6 Countdown (+3 upgraded → 9).
    private static void ApplyCountdown(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 9 : 6;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Countdown, amount);
    }

    // CorrosiveWave (a Power card): gives itself 2 CorrosiveWave (+1 upgraded → 3).
    private static void ApplyCorrosiveWave(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.CorrosiveWave, amount);
    }

    // CreativeAi (a Power card): gives itself 1 CreativeAi (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyCreativeAi(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.CreativeAi, 1);
    }

    // CrimsonMantle (a Power card): gives itself 8 CrimsonMantle (+2 upgraded → 10). The game also
    // calls IncrementSelfDamage() on the applied power instance, which maintains a hidden internal
    // counter inside CrimsonMantlePower affecting LATER self-damage — same category of gap as
    // SlowPower (see SimDamage_Coverage_Backlog.md), not modeled here; this card's own OnPlay body
    // is fully captured (it's just the one PowerCmd.Apply call), the gap lives in the power's own
    // downstream behavior, not in this registration.
    private static void ApplyCrimsonMantle(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 10 : 8;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.CrimsonMantle, amount);
    }

    // Cruelty (a Power card): gives itself 25 Cruelty (+25 upgraded → 50).
    private static void ApplyCruelty(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 50 : 25;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Cruelty, amount);
    }

    // DanseMacabre (a Power card): gives itself 4 DanseMacabre (+2 upgraded → 6). The declared
    // EnergyVar(2) is not referenced anywhere in OnPlay — it's display-only, not part of the
    // effect.
    private static void ApplyDanseMacabre(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DanseMacabre, amount);
    }

    // DarkEmbrace (a Power card): gives itself 1 DarkEmbrace (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyDarkEmbrace(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DarkEmbrace, 1);
    }

    // Defile: 13 damage (+4 upgraded → 17) to target. Ethereal keyword doesn't affect play-time
    // effect (only end-of-turn exhaust-if-unplayed), handled generically elsewhere.
    private static void ApplyDefile(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 17 : 13;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Defragment (a Power card): gives itself 1 Focus (+1 upgraded → 2).
    private static void ApplyDefragment(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Focus, amount);
    }

    // DarkShackles: applies 9 DarkShackles (+6 upgraded → 15) to target. Whole effect is the
    // single PowerCmd.Apply call.
    private static void ApplyDarkShackles(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 15 : 9;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.DarkShackles, amount);
    }

    // DeadlyPoison: applies 5 Poison (+2 upgraded → 7) to target. Whole effect is the single
    // PowerCmd.Apply call.
    private static void ApplyDeadlyPoison(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 7 : 5;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Poison, amount);
    }

    // Deathbringer: applies 21 Doom (+5 upgraded → 26) then 1 Weak (fixed) to every living enemy.
    private static void ApplyDeathbringer(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int doom = card.IsUpgraded ? 26 : 21;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Doom, doom);
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Weak, 1);
        }
    }

    // Debilitate: 10 damage (+2 upgraded → 12) then 2 Debilitate (+1 upgraded → 3) to target.
    private static void ApplyDebilitate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 12 : 10;
        int debilitate = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Debilitate, debilitate);
    }

    // CelestialMight: 6 damage (fixed — only the hit count upgrades, not damage), hits 3 times
    // (+1 upgraded → 4), single target.
    private static void ApplyCelestialMight(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int hits = card.IsUpgraded ? 4 : 3;
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, 6, hits);
    }

    // Conflagration: 2 damage (fixed — only the hit count upgrades), hits 4 times (+1 upgraded →
    // 5), every living enemy.
    private static void ApplyConflagration(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int hits = card.IsUpgraded ? 5 : 4;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealMultiHitDamageToEnemy(state, i, 2, hits);
        }
    }

    // Dash: gains 10 Block (+3 upgraded → 13) then 10 damage (+3 upgraded → 13) to target.
    private static void ApplyDash(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 13 : 10;
        int dmg = card.IsUpgraded ? 13 : 10;
        GainPlayerBlock(state, block);
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Devastate: 30 damage (+10 upgraded → 40) to target. No other effect.
    private static void ApplyDevastate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 40 : 30;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // DevourLife (a Power card): gives itself 1 DevourLife (+1 upgraded → 2).
    private static void ApplyDevourLife(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DevourLife, amount);
    }

    // Dismantle: 8 damage (+2 upgraded → 10), hits twice if target has Vulnerable, once
    // otherwise.
    private static void ApplyDismantle(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 8;
        int hits = EnemyVulnerable(state, targetEnemyIdx) ? 2 : 1;
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, hits);
    }

    // DodgeAndRoll: gains 4 Block (+2 upgraded → 6), then gives itself BlockNextTurn equal to the
    // ACTUAL amount of block just gained (post-Dexterity/Frail/NoBlock), not the raw requested
    // amount — mirrors the real game using GainBlock's return value.
    private static void ApplyDodgeAndRoll(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 6 : 4;
        int gained = GainPlayerBlock(state, block);
        if (gained > 0)
            SimPowerOps.ApplyPlayerDelta(state, SimPowerType.BlockNextTurn, gained);
    }

    // Envenom (a Power card): gives itself 1 Envenom (+1 upgraded → 2).
    private static void ApplyEnvenom(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Envenom, amount);
    }

    // EternalArmor (a Power card): gives itself 9 Plating (+3 upgraded → 12).
    private static void ApplyEternalArmor(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 12 : 9;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Plating, amount);
    }

    // Feral (a Power card): gives itself 1 Feral (fixed — upgrade only reduces Energy cost).
    private static void ApplyFeral(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Feral, 1);
    }

    // FlickFlack: 6 damage (+2 upgraded → 8) to every living enemy. No other effect.
    private static void ApplyFlickFlack(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 6;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // Footwork (a Power card): gives itself 2 Dexterity (+1 upgraded → 3).
    private static void ApplyFootwork(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Dexterity, amount);
    }

    // ForbiddenGrimoire (a Power card): gives itself 1 ForbiddenGrimoire (fixed — upgrade only
    // reduces Energy cost).
    private static void ApplyForbiddenGrimoire(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.ForbiddenGrimoire, 1);
    }

    // ForegoneConclusion (a Power card): gives itself 2 ForegoneConclusion (+1 upgraded → 3).
    private static void ApplyForegoneConclusion(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.ForegoneConclusion, amount);
    }

    // Furnace (a Power card): gives itself 5 Furnace (+2 upgraded → 7).
    private static void ApplyFurnace(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 7 : 5;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Furnace, amount);
    }

    // Fear: 7 damage (+1 upgraded → 8) then 1 Vulnerable (+1 upgraded → 2) to target. Ethereal
    // keyword doesn't affect play-time effect.
    private static void ApplyFear(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 7;
        int vuln = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // Flanking: applies 2 Flanking (fixed — upgrade only reduces Energy cost) to target.
    private static void ApplyFlanking(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Flanking, 2);
    }

    // Fasten (a Power card): gives itself 4 Fasten (+2 upgraded → 6).
    private static void ApplyFasten(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Fasten, amount);
    }

    // EnfeeblingTouch: applies 8 EnfeeblingTouch (+3 upgraded → 11) to target. Whole effect is the
    // single PowerCmd.Apply call; Ethereal doesn't affect play-time effect.
    private static void ApplyEnfeeblingTouch(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 11 : 8;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.EnfeeblingTouch, amount);
    }

    // GatherLight: gains 8 Block (+3 upgraded → 11) then gains 1 Star (fixed — no upgrade on
    // Stars). Whole effect is GainBlock + PlayerCmd.GainStars.
    private static void ApplyGatherLight(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 11 : 8;
        GainPlayerBlock(state, block);
        state.PlayerStars = (ushort)Math.Min(999999999, state.PlayerStars + 1);
    }

    // Friendship (a Power card): removes 2 Strength (+1 upgraded → 3, i.e. UpgradeValueBy(-1)
    // makes MORE Strength removed) from itself, then gives itself 1 Friendship (fixed — Energy var
    // never upgrades).
    private static void ApplyFriendship(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int strengthLoss = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Strength, -strengthLoss);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Friendship, 1);
    }

    // FanOfKnives (a Power card): gives itself 1 FanOfKnives (fixed), then generates 4 Shivs
    // (+1 upgraded → 5) directly into Hand.
    private static void ApplyFanOfKnives(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FanOfKnives, 1);
        int shivs = card.IsUpgraded ? 5 : 4;
        for (int i = 0; i < shivs; i++)
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Shiv, upgraded: false);
    }

    // Genesis (a Power card): gives itself 2 Genesis (+1 upgraded → 3).
    private static void ApplyGenesis(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Genesis, amount);
    }

    // GiantRock: 16 damage (+4 upgraded → 20) to target. No other effect.
    private static void ApplyGiantRock(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 20 : 16;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Hailstorm (a Power card): gives itself 6 Hailstorm (+2 upgraded → 8).
    private static void ApplyHailstorm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 8 : 6;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Hailstorm, amount);
    }

    // HammerTime (a Power card): gives itself 1 HammerTime (fixed — upgrade only reduces Energy
    // cost). MultiplayerOnly constraint is a legal-move-generation concern, not an effect concern.
    private static void ApplyHammerTime(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.HammerTime, 1);
    }

    // Haunt (a Power card): gives itself 6 Haunt (+2 upgraded → 8).
    private static void ApplyHaunt(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 8 : 6;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Haunt, amount);
    }

    // Haze: applies 4 Poison (+2 upgraded → 6) to every living enemy.
    private static void ApplyHaze(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Poison, amount);
        }
    }

    // HelloWorld (a Power card): gives itself 1 HelloWorld (fixed — upgrade only adds Innate).
    private static void ApplyHelloWorld(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.HelloWorld, 1);
    }

    // Hellraiser (a Power card): gives itself 1 Hellraiser (fixed — no numeric upgrade at all).
    private static void ApplyHellraiser(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Hellraiser, 1);
    }

    // HowlFromBeyond: 16 damage (+5 upgraded → 21) to every living enemy. This card ALSO has a
    // separate AfterAutoPostPlayPhaseEntered hook that auto-replays it from Exhaust — that fires
    // from a different trigger point entirely (entering post-play phase), not from OnPlay itself,
    // so it's outside this function's contract and not modeled here.
    private static void ApplyHowlFromBeyond(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 21 : 16;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // Impervious: gains 30 Block (+10 upgraded → 40). No other effect.
    private static void ApplyImpervious(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 40 : 30;
        GainPlayerBlock(state, block);
    }

    // Inferno (a Power card): gives itself 6 Inferno (+3 upgraded → 9). Same IncrementSelfDamage
    // hidden-counter caveat as CrimsonMantle — see SimDamage_Coverage_Backlog.md.
    private static void ApplyInferno(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 9 : 6;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Inferno, amount);
    }

    // Inflame (a Power card): gives itself 2 Strength (+1 upgraded → 3).
    private static void ApplyInflame(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Strength, amount);
    }

    // Iteration (a Power card): gives itself 2 Iteration (+1 upgraded → 3).
    private static void ApplyIteration(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Iteration, amount);
    }

    // Juggernaut (a Power card): gives itself 6 Juggernaut (+2 upgraded → 8).
    private static void ApplyJuggernaut(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 8 : 6;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Juggernaut, amount);
    }

    // Juggling (a Power card): gives itself 1 Juggling (fixed — upgrade only adds Innate).
    private static void ApplyJuggling(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Juggling, 1);
    }

    // KinglyKick: 27 damage (+8 upgraded → 35) to target. Its AfterCardDrawn cost-reduction hook
    // fires on draw, not on play, and doesn't affect this function.
    private static void ApplyKinglyKick(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 35 : 27;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Leap: gains 9 Block (+3 upgraded → 12). No other effect.
    private static void ApplyLeap(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 12 : 9;
        GainPlayerBlock(state, block);
    }

    // GoForTheEyes: 3 damage (+1 upgraded → 4) to target; if the target's CURRENT intent is Attack
    // or DeathBlow (mirrors MonsterModel.IntendsToAttack exactly — confirmed both are checked, not
    // just Attack), also applies 1 Weak (+1 upgraded → 2).
    private static void ApplyGoForTheEyes(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 4 : 3;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimIntent intent = (SimIntent)state.EnemyIntent[targetEnemyIdx];
        if (intent == SimIntent.Attack || intent == SimIntent.DeathBlow)
        {
            int weak = card.IsUpgraded ? 2 : 1;
            SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, weak);
        }
    }

    // Hyperbeam: 28 damage (+8 upgraded → 36) to every living enemy, then removes ALL of the
    // player's current Focus (reads the live amount and applies its exact negation — "set Focus to
    // 0", not a fixed reduction).
    private static void ApplyHyperbeam(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 36 : 28;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
        if (SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Focus, out short focusAmt) && focusAmt != 0)
            SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Focus, -focusAmt);
    }

    // Knockdown: 10 damage (+4 upgraded → 14) then 2 Knockdown (+1 upgraded → 3) to target.
    // MultiplayerOnly constraint is a legal-move-generation concern, not an effect concern.
    private static void ApplyKnockdown(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 14 : 10;
        int knockdown = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Knockdown, knockdown);
    }

    // KnowThyPlace: applies 1 Weak and 1 Vulnerable (both fixed — upgrade only removes this card's
    // own Exhaust keyword, a static property) to target.
    private static void ApplyKnowThyPlace(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, 1);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, 1);
    }

    // LeadingStrike: 3 damage (+3 upgraded → 6) to target, then generates 2 Shivs (fixed — only
    // Damage upgrades) directly into Hand.
    private static void ApplyLeadingStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 6 : 3;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        for (int i = 0; i < 2; i++)
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Shiv, upgraded: false);
    }

    // LegSweep: gains 11 Block (+3 upgraded → 14) then applies 2 Weak (+1 upgraded → 3) to target.
    private static void ApplyLegSweep(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 14 : 11;
        int weak = card.IsUpgraded ? 3 : 2;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, weak);
    }

    // Lethality (a Power card): gives itself 50 Lethality (+25 upgraded → 75).
    private static void ApplyLethality(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 75 : 50;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Lethality, amount);
    }

    // LightningRod: gains 4 Block (+3 upgraded → 7) then gives itself 2 LightningRod (fixed — no
    // upgrade on the power var itself).
    private static void ApplyLightningRod(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 7 : 4;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.LightningRod, 2);
    }

    // Loop (a Power card): gives itself 1 Loop (+1 upgraded → 2).
    private static void ApplyLoop(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Loop, amount);
    }

    // Mangle: 15 damage (+5 upgraded → 20) then 10 Mangle (+5 upgraded → 15) to target.
    private static void ApplyMangle(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 20 : 15;
        int mangle = card.IsUpgraded ? 15 : 10;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Mangle, mangle);
    }

    // MasterPlanner (a Power card): gives itself 1 MasterPlanner (fixed — upgrade only reduces
    // Energy cost).
    private static void ApplyMasterPlanner(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.MasterPlanner, 1);
    }

    // Mayhem (a Power card): gives itself 1 Mayhem (fixed — upgrade only reduces Energy cost).
    private static void ApplyMayhem(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Mayhem, 1);
    }

    // MinionDiveBomb (a Token card): 13 damage (+3 upgraded → 16) to target. No other effect.
    private static void ApplyMinionDiveBomb(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 16 : 13;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // MinionSacrifice (a Token card): gains 8 Block (+3 upgraded → 11). No other effect.
    private static void ApplyMinionSacrifice(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 11 : 8;
        GainPlayerBlock(state, block);
    }

    // MinionStrike (a Token card): 6 damage (+3 upgraded → 9) to target then draws 1 (fixed).
    private static void ApplyMinionStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 9 : 6;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimCardPileOps.DrawCards(state, 1);
    }

    // MomentumStrike: 10 damage (+3 upgraded → 13) to target. Also sets its own Energy cost to 0
    // for the rest of combat (base.EnergyCost.SetThisCombat(0)) — a cost mutation, out of scope
    // for this function just like KinglyKick/Pinpoint's cost hooks (this system never models
    // Energy cost payment/mutation at all, only card effects).
    private static void ApplyMomentumStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 13 : 10;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // MonarchsGaze (a Power card): gives itself 1 MonarchsGaze (fixed — upgrade only reduces
    // Energy cost).
    private static void ApplyMonarchsGaze(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.MonarchsGaze, 1);
    }

    // Monologue: gives itself 1 Monologue (fixed — upgrade only adds Retain keyword). The real
    // game also writes this card's "Power" var (1, always fixed) into the newly-applied
    // MonologuePower's own internal Strength sub-variable — that's a nested-power-internal-state
    // mechanic our flat SimPowerType→amount model doesn't represent (same category of gap as
    // CrimsonMantle/Inferno's IncrementSelfDamage), not modeled here.
    private static void ApplyMonologue(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Monologue, 1);
    }

    // MoltenFist: 10 damage (+4 upgraded → 14) to target; if the target is still alive and
    // currently has Vulnerable, re-applies that SAME current amount again (doubling it), not a
    // fixed bonus.
    private static void ApplyMoltenFist(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 14 : 10;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        if (state.EnemyHp[targetEnemyIdx] > 0
            && SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Vulnerable, out short vulnAmt)
            && vulnAmt > 0)
        {
            SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vulnAmt);
        }
    }

    // Nostalgia (a Power card): gives itself 1 Nostalgia (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyNostalgia(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Nostalgia, 1);
    }

    // NoxiousFumes (a Power card): gives itself 2 NoxiousFumes (+1 upgraded → 3).
    private static void ApplyNoxiousFumes(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.NoxiousFumes, amount);
    }

    // OneTwoPunch (a Skill card): gives itself 1 OneTwoPunch (+1 upgraded → 2).
    private static void ApplyOneTwoPunch(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.OneTwoPunch, amount);
    }

    // Orbit (a Power card): gives itself 1 Orbit (fixed — upgrade only reduces Energy cost).
    private static void ApplyOrbit(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Orbit, 1);
    }

    // Outbreak (a Power card): gives itself 11 Outbreak (+4 upgraded → 15). The declared
    // RepeatVar(3) is not referenced anywhere in OnPlay — display-only.
    private static void ApplyOutbreak(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 15 : 11;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Outbreak, amount);
    }

    // Outmaneuver: gives itself 2 EnergyNextTurn (+1 upgraded → 3).
    private static void ApplyOutmaneuver(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EnergyNextTurn, amount);
    }

    // Pagestorm (a Power card): gives itself 1 Pagestorm (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyPagestorm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Pagestorm, 1);
    }

    // PaleBlueDot (a Power card): gives itself 1 PaleBlueDot (+1 upgraded → 2). The declared
    // "CardPlay" var(5) is not referenced anywhere in OnPlay — display-only.
    private static void ApplyPaleBlueDot(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.PaleBlueDot, amount);
    }

    // Parry (a Power card): gives itself 10 Parry (+4 upgraded → 14).
    private static void ApplyParry(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 14 : 10;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Parry, amount);
    }

    // Parse: draws 3 (+1 upgraded → 4). Whole effect is the single CardPileCmd.Draw call.
    private static void ApplyParse(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 4 : 3;
        SimCardPileOps.DrawCards(state, draws);
    }

    // PhantomBlades (a Power card): gives itself 9 PhantomBlades (+3 upgraded → 12).
    private static void ApplyPhantomBlades(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 12 : 9;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.PhantomBlades, amount);
    }

    // Pillage: 6 damage (+3 upgraded → 9) to target, then draws cards one at a time for as long as
    // each newly-drawn card is an Attack AND the draw pile still has cards AND hand still has room
    // — mirrors the real do-while exactly (draws at least once if pile+hand permit).
    private static void ApplyPillage(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 9 : 6;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        while (state.DrawCount > 0 && state.HandCount < CombatSimLayout.HandCap)
        {
            SimCard drawn = state.DrawCards[0];
            SimCardType drawnType = SimCardTypeRegistry.Get(drawn.BaseCardId);
            SimCardPileOps.MoveToEnd(state.DrawCards, ref state.DrawCount, 0, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap);
            if (drawnType != SimCardType.Attack) break;
        }
    }

    // PillarOfCreation (a Power card): gives itself 3 PillarOfCreation (+1 upgraded → 4). Despite
    // the CanonicalVars being named/tagged as a BlockVar, OnPlay never calls GainBlock at all —
    // the number is only ever used as this power's applied amount.
    private static void ApplyPillarOfCreation(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 4 : 3;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.PillarOfCreation, amount);
    }

    // Pinpoint: 15 damage (+4 upgraded → 19) to target. Its two AfterCardEnteredCombat/
    // AfterCardPlayed hooks reduce its OWN Energy cost over time based on Skills played — cost
    // mutation, out of scope for this function (see MomentumStrike).
    private static void ApplyPinpoint(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 19 : 15;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // PrepTime (a Power card): gives itself 4 PrepTime (+2 upgraded → 6).
    private static void ApplyPrepTime(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.PrepTime, amount);
    }

    // Production: gains 2 Energy (+1 upgraded → 3). Whole effect is PlayerCmd.GainEnergy.
    private static void ApplyProduction(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 3 : 2;
        GainPlayerEnergy(state, energy);
    }

    // Prophesize: draws 6 (+3 upgraded → 9). Whole effect is the single CardPileCmd.Draw call.
    private static void ApplyProphesize(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 9 : 6;
        SimCardPileOps.DrawCards(state, draws);
    }

    // Prowess (a Power card): gives itself 1 Strength (+1 upgraded → 2) then 1 Dexterity (+1
    // upgraded → 2).
    private static void ApplyProwess(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Strength, amount);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Dexterity, amount);
    }

    // Reap: 27 damage (+6 upgraded → 33) to target. No other effect (Retain keyword doesn't
    // affect play-time effect).
    private static void ApplyReap(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 33 : 27;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // ReaperForm (a Power card): gives itself 1 ReaperForm (fixed — upgrade only adds Retain
    // keyword).
    private static void ApplyReaperForm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.ReaperForm, 1);
    }

    // Rebound: 9 damage (+3 upgraded → 12) to target then gives itself 1 Rebound (fixed).
    private static void ApplyRebound(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 12 : 9;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Rebound, 1);
    }

    // Reflect: gains 15 Block (+5 upgraded → 20) then gives itself 1 Reflect (fixed).
    private static void ApplyReflect(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 20 : 15;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Reflect, 1);
    }

    // RollingBoulder (a Power card): gives itself 5 RollingBoulder (+5 upgraded → 10). The
    // declared "IncrementAmount" var(5) is not referenced anywhere in OnPlay — display-only.
    private static void ApplyRollingBoulder(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 10 : 5;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.RollingBoulder, amount);
    }

    // Royalties (a Power card): gives itself 30 Royalties (+10 upgraded → 40).
    private static void ApplyRoyalties(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 40 : 30;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Royalties, amount);
    }

    // Salvo: 12 damage (+4 upgraded → 16) to target then gives itself 1 RetainHand (fixed).
    private static void ApplySalvo(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 16 : 12;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.RetainHand, 1);
    }

    // Scare: applies 1 Weak (fixed) to every living enemy.
    private static void ApplyScare(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Weak, 1);
        }
    }

    // Scourge: applies 13 Doom (+3 upgraded → 16) to target then draws 1 (+1 upgraded → 2).
    private static void ApplyScourge(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int doom = card.IsUpgraded ? 16 : 13;
        int draws = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Doom, doom);
        SimCardPileOps.DrawCards(state, draws);
    }

    // SentryMode (a Power card): gives itself 1 SentryMode (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplySentryMode(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SentryMode, 1);
    }

    // SerpentForm (a Power card): gives itself 4 SerpentForm (+2 upgraded → 6).
    private static void ApplySerpentForm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SerpentForm, amount);
    }

    // SetupStrike: 7 damage (+2 upgraded → 9) to target then gives itself 2 SetupStrike (+1
    // upgraded → 3).
    private static void ApplySetupStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 9 : 7;
        int amount = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SetupStrike, amount);
    }

    // ShadowStep: discards the entire hand (excluding this card's own instance, which is already
    // conceptually out of Hand during Apply — see class-doc "known simplification"), then gives
    // itself 1 ShadowStep (fixed).
    private static void ApplyShadowStep(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            if (state.HandCards[i].InstanceId == card.InstanceId) continue;
            SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap);
        }
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.ShadowStep, 1);
    }

    // Shockwave: applies 3 Weak (+2 upgraded → 5) and the SAME amount of Vulnerable to every
    // living enemy.
    private static void ApplyShockwave(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 5 : 3;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Weak, amount);
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Vulnerable, amount);
        }
    }

    // SignalBoost (a Power card): gives itself 1 SignalBoost (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplySignalBoost(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SignalBoost, 1);
    }

    // SolarStrike: 9 damage (+1 upgraded → 10) to target then gains 1 Star (+1 upgraded → 2).
    private static void ApplySolarStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 9;
        int stars = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        state.PlayerStars = (ushort)Math.Min(999999999, state.PlayerStars + stars);
    }

    // Speedster (a Power card): gives itself 2 Speedster (fixed — upgrade only adds Innate).
    private static void ApplySpeedster(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Speedster, 2);
    }

    // Squash: 10 damage (+2 upgraded → 12) then 2 Vulnerable (+1 upgraded → 3) to target.
    private static void ApplySquash(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 12 : 10;
        int vuln = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // Stampede (a Power card): gives itself 1 Stampede (fixed — upgrade only reduces Energy cost).
    private static void ApplyStampede(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Stampede, 1);
    }

    // Strangle: 8 damage (+2 upgraded → 10) then 2 Strangle (+1 upgraded → 3) to target.
    private static void ApplyStrangle(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 8;
        int amount = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Strangle, amount);
    }

    // Sunder: 24 damage (+8 upgraded → 32) to target; if this damage instance actually kills the
    // target, gains 3 Energy (fixed).
    private static void ApplySunder(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 32 : 24;
        int hpBefore = state.EnemyHp[targetEnemyIdx];
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        bool killed = hpBefore > 0 && state.EnemyHp[targetEnemyIdx] == 0;
        if (killed) GainPlayerEnergy(state, 3);
    }

    // Supercritical: gains 4 Energy (+2 upgraded → 6). Whole effect is PlayerCmd.GainEnergy.
    private static void ApplySupercritical(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 6 : 4;
        GainPlayerEnergy(state, energy);
    }

    // Suppress: 11 damage (+6 upgraded → 17) then 3 Weak (+2 upgraded → 5) to target. Innate
    // keyword doesn't affect play-time effect.
    private static void ApplySuppress(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 17 : 11;
        int weak = card.IsUpgraded ? 5 : 3;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, weak);
    }

    // Synthesis: 14 damage (+6 upgraded → 20) to target then gives itself 1 FreePower (fixed).
    private static void ApplySynthesis(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 20 : 14;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FreePower, 1);
    }

    // Tactician: gains 1 Energy (+1 upgraded → 2). Whole effect is PlayerCmd.GainEnergy.
    private static void ApplyTactician(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 2 : 1;
        GainPlayerEnergy(state, energy);
    }

    // TagTeam: 11 damage (+4 upgraded → 15) to target then applies 1 TagTeam (fixed).
    // MultiplayerOnly constraint is a legal-move-generation concern, not an effect concern.
    private static void ApplyTagTeam(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 15 : 11;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.TagTeam, 1);
    }

    // Taunt: gains 7 Block (+1 upgraded → 8) then applies 1 Vulnerable (+1 upgraded → 2) to
    // target.
    private static void ApplyTaunt(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 8 : 7;
        int vuln = card.IsUpgraded ? 2 : 1;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // Thunder (a Power card): gives itself 6 Thunder (+2 upgraded → 8).
    private static void ApplyThunder(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 8 : 6;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Thunder, amount);
    }

    // ToolsOfTheTrade (a Power card): gives itself 1 ToolsOfTheTrade (fixed — upgrade only reduces
    // Energy cost).
    private static void ApplyToolsOfTheTrade(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.ToolsOfTheTrade, 1);
    }

    // Tracking (a Power card): gives itself 2 Tracking if it doesn't already have any, else 1 —
    // mirrors the real HasPower check exactly (not a simple "first play vs later" heuristic).
    private static void ApplyTracking(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        bool hasTracking = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Tracking, out _);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Tracking, hasTracking ? 1 : 2);
    }

    // TrashToTreasure (a Power card): gives itself 1 TrashToTreasure (fixed — upgrade only adds
    // Innate).
    private static void ApplyTrashToTreasure(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.TrashToTreasure, 1);
    }

    // Tremble: applies 3 Vulnerable (+1 upgraded → 4) to target. Whole effect is the single
    // PowerCmd.Apply call; Exhaust doesn't affect play-time effect.
    private static void ApplyTremble(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 4 : 3;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, amount);
    }

    // Tyranny (a Power card): gives itself 1 Tyranny (fixed — upgrade only adds Innate).
    private static void ApplyTyranny(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Tyranny, 1);
    }

    // UltimateDefend: gains 11 Block (+4 upgraded → 15). No other effect.
    private static void ApplyUltimateDefend(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 15 : 11;
        GainPlayerBlock(state, block);
    }

    // UltimateStrike: 14 damage (+6 upgraded → 20) to target. No other effect.
    private static void ApplyUltimateStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 20 : 14;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // Unmovable (a Power card): gives itself 1 Unmovable (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyUnmovable(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Unmovable, 1);
    }

    // Unrelenting: 14 damage (+6 upgraded → 20) to target then gives itself 1 FreeAttack (fixed).
    private static void ApplyUnrelenting(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 20 : 14;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FreeAttack, 1);
    }

    // Untouchable: gains 6 Block (+3 upgraded → 9). No other effect.
    private static void ApplyUntouchable(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 9 : 6;
        GainPlayerBlock(state, block);
    }

    // Veilpiercer: 10 damage (+3 upgraded → 13) to target then gives itself 1 Veilpiercer (fixed).
    // Ethereal doesn't affect play-time effect.
    private static void ApplyVeilpiercer(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 13 : 10;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Veilpiercer, 1);
    }

    // Venerate: gains 2 Stars (+1 upgraded → 3). Whole effect is PlayerCmd.GainStars.
    private static void ApplyVenerate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int stars = card.IsUpgraded ? 3 : 2;
        state.PlayerStars = (ushort)Math.Min(999999999, state.PlayerStars + stars);
    }

    // Vicious (a Power card): gives itself 1 Vicious (+1 upgraded → 2).
    private static void ApplyVicious(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Vicious, amount);
    }

    // WellLaidPlans (a Power card): gives itself 1 WellLaidPlans (+1 upgraded → 2).
    private static void ApplyWellLaidPlans(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.WellLaidPlans, amount);
    }

    // WraithForm (a Power card): gives itself 2 Intangible (+1 upgraded → 3) then 1 WraithForm
    // (fixed).
    private static void ApplyWraithForm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int intangible = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Intangible, intangible);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.WraithForm, 1);
    }

    // Arsenal (a Power card): gives itself 1 Arsenal (fixed — upgrade only adds Innate).
    private static void ApplyArsenal(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Arsenal, 1);
    }

    // Automation (a Power card): gives itself 1 Automation (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyAutomation(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Automation, 1);
    }

    // Backstab: 11 damage (+4 upgraded → 15) to target. No other effect.
    private static void ApplyBackstab(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 15 : 11;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // BansheesCry: 33 damage (fixed — upgrade only reduces Energy cost) to every living enemy. Its
    // two cost-reduction hooks (AfterCardEnteredCombat/AfterCardPlayed) are cost mutation, out of
    // scope for this function (see MomentumStrike/Pinpoint).
    private static void ApplyBansheesCry(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, 33);
        }
    }

    // BeamCell: 3 damage (+1 upgraded → 4) then 1 Vulnerable (+1 upgraded → 2) to target.
    private static void ApplyBeamCell(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 4 : 3;
        int vuln = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // BloodWall: loses 2 HP (fixed, unblockable+unpowered, self) then gains 16 Block (+4 upgraded
    // → 20).
    private static void ApplyBloodWall(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        LosePlayerHp(state, 2);
        int block = card.IsUpgraded ? 20 : 16;
        GainPlayerBlock(state, block);
    }

    // Blur: gains 5 Block (+3 upgraded → 8) then gives itself 1 Blur (fixed).
    private static void ApplyBlur(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 8 : 5;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Blur, 1);
    }

    // BootSequence: gains 10 Block (+3 upgraded → 13). No other effect.
    private static void ApplyBootSequence(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 13 : 10;
        GainPlayerBlock(state, block);
    }

    // BorrowedTime: gains 4 Energy (+2 upgraded → 6) then gives itself 1 BorrowedTime (fixed — the
    // "ExtraCost" var never upgrades).
    private static void ApplyBorrowedTime(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 6 : 4;
        GainPlayerEnergy(state, energy);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.BorrowedTime, 1);
    }

    // BubbleBubble: applies 9 Poison (+3 upgraded → 12) to target, but ONLY if the target already
    // has Poison — mirrors the real HasPower<PoisonPower> check exactly.
    private static void ApplyBubbleBubble(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        if (!SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Poison, out _)) return;
        int amount = card.IsUpgraded ? 12 : 9;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Poison, amount);
    }

    // Anticipate: gives itself 2 Dexterity-labeled AnticipatePower (+1 upgraded → 3). The
    // "PowerVar<DexterityPower>" naming is display-only (matches Rupture's precedent) — the power
    // actually applied and stored is AnticipatePower, not DexterityPower itself.
    private static void ApplyAnticipate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Anticipate, amount);
    }

    // Apparition: gives itself 1 Intangible (fixed — upgrade only removes the Ethereal keyword, a
    // static property, not a play-time effect change).
    private static void ApplyApparition(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Intangible, 1);
    }

    // CollisionCourse: 11 damage (+4 upgraded → 15) to target, then generates 1 Debris (a no-op
    // Status card, see ApplyDebris) directly into Hand.
    private static void ApplyCollisionCourse(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 15 : 11;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Debris, upgraded: false);
    }

    // Debris (a Status card): OnPlay body is literally empty in the real game (Task.CompletedTask,
    // no-op) — playing it does nothing except route itself to Exhaust via the generic
    // ResolvePlayedCardDestination (its CanonicalKeywords include Exhaust).
    private static void ApplyDebris(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
    }

    // Demesne (a Power card): gives itself 1 DemesnePower (fixed — the Cards var never upgrades,
    // only Energy cost does).
    private static void ApplyDemesne(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Demesne, 1);
    }

    // DemonForm (a Power card): gives itself 2 DemonForm (+1 upgraded → 3).
    private static void ApplyDemonForm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DemonForm, amount);
    }

    // EchoForm (a Power card): gives itself 1 EchoForm (fixed — upgrade only removes Ethereal).
    private static void ApplyEchoForm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EchoForm, 1);
    }

    // Entropy (a Power card): gives itself 1 Entropy (fixed — upgrade only adds Innate).
    private static void ApplyEntropy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Entropy, 1);
    }

    // Equilibrium: gains 13 Block (+3 upgraded → 16) then gives itself 1 RetainHand (fixed).
    private static void ApplyEquilibrium(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 16 : 13;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.RetainHand, 1);
    }

    // FeedingFrenzy (a Skill card): gives itself 5 FeedingFrenzy (+2 upgraded → 7). The
    // "PowerVar<StrengthPower>" naming is display-only (matches Rupture/Anticipate's precedent) —
    // the power actually applied is FeedingFrenzyPower, not StrengthPower.
    private static void ApplyFeedingFrenzy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 7 : 5;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FeedingFrenzy, amount);
    }

    // FocusedStrike: 9 damage (+2 upgraded → 11) to target then gives itself 1 FocusedStrike
    // (+1 upgraded → 2).
    private static void ApplyFocusedStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 11 : 9;
        int amount = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FocusedStrike, amount);
    }

    // HiddenCache: gains 1 Star (fixed) then gives itself 3 StarNextTurn (+1 upgraded → 4).
    private static void ApplyHiddenCache(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        state.PlayerStars = (ushort)Math.Min(999999999, state.PlayerStars + 1);
        int amount = card.IsUpgraded ? 4 : 3;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.StarNextTurn, amount);
    }

    // EndOfDays: applies 29 Doom (+8 upgraded → 37) to every living enemy, then immediately kills
    // any enemy whose current HP is now <= their (post-application) Doom amount — mirrors
    // DoomPower.IsOwnerDoomed's exact check (CurrentHp <= Amount), confirmed against
    // DoomPower.cs/DoomKill/GetDoomedCreatures.
    private static void ApplyEndOfDays(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int doom = card.IsUpgraded ? 37 : 29;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Doom, doom);
            if (SimPowerOps.TryGetEnemyAmount(state, i, SimPowerType.Doom, out short doomAmt)
                && state.EnemyHp[i] <= doomAmt)
            {
                state.EnemyHp[i] = 0;
            }
        }
    }

    // Hang: 10 damage (+3 upgraded → 13) to target, then applies max(2, target's CURRENT Hang
    // stack) more Hang — i.e. doubles the stack if it's already >= 2, otherwise sets it to at
    // least 2. Mirrors the real Math.Max(2, powerAmount) + overflow-clamp exactly (clamp
    // unreachable at our short-based scale but kept for parity).
    private static void ApplyHang(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 13 : 10;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        int current = SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Hang, out short hangAmt) ? hangAmt : 0;
        int addAmount = Math.Max(2, current);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Hang, addAmount);
    }

    // EscapePlan: draws 1 card; if that card is a Skill, gains 3 Block (+2 upgraded → 5). Peeks
    // the draw pile's top card's type BEFORE drawing (draw removes it), matching the real order
    // (draw first, then check the drawn card).
    private static void ApplyEscapePlan(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        if (state.DrawCount == 0 || state.HandCount >= CombatSimLayout.HandCap) return;
        SimCardType drawnType = SimCardTypeRegistry.Get(state.DrawCards[0].BaseCardId);
        SimCardPileOps.DrawCards(state, 1);
        if (drawnType == SimCardType.Skill)
        {
            int block = card.IsUpgraded ? 5 : 3;
            GainPlayerBlock(state, block);
        }
    }

    // MachineLearning (a Power card): gives itself 1 MachineLearning (fixed — upgrade only adds
    // Innate).
    private static void ApplyMachineLearning(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.MachineLearning, 1);
    }

    // NegativePulse: gains 5 Block (+1 upgraded → 6) then applies 7 Doom (+4 upgraded → 11) to
    // every living enemy.
    private static void ApplyNegativePulse(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 6 : 5;
        int doom = card.IsUpgraded ? 11 : 7;
        GainPlayerBlock(state, block);
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Doom, doom);
        }
    }

    // NeutronAegis (a Power card): gives itself 8 Plating (+3 upgraded → 11).
    private static void ApplyNeutronAegis(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 11 : 8;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Plating, amount);
    }

    // Oblivion: applies 3 OblivionPower (+1 upgraded → 4) to target — a separate power type from
    // Doom despite sharing the "PowerVar<DoomPower>" display label (matches Rupture/Anticipate/
    // FeedingFrenzy precedent: CanonicalVars naming is cosmetic, not the power actually applied).
    private static void ApplyOblivion(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 4 : 3;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Oblivion, amount);
    }

    // Pyre (a Power card): gives itself 1 Pyre (+1 upgraded → 2).
    private static void ApplyPyre(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Pyre, amount);
    }

    // Relax: gains 15 Block (+2 upgraded → 17) then gives itself 2 DrawCardsNextTurn
    // (+1 upgraded → 3) and 2 EnergyNextTurn (+1 upgraded → 3).
    private static void ApplyRelax(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 17 : 15;
        int cards = card.IsUpgraded ? 3 : 2;
        int energy = card.IsUpgraded ? 3 : 2;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DrawCardsNextTurn, cards);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EnergyNextTurn, energy);
    }

    // Resonance: gives itself 1 Strength (+1 upgraded → 2) then removes 1 Strength from every
    // living enemy (fixed — enemy loss amount never upgrades, only the self-gain does).
    private static void ApplyResonance(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int selfGain = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Strength, selfGain);
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Strength, -1);
        }
    }

    // PanicButton: gains 30 Block (+10 upgraded → 40) then gives itself 2 NoBlock (fixed — the
    // "Turns" var never upgrades).
    private static void ApplyPanicButton(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 40 : 30;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.NoBlock, 2);
    }

    // Putrefy: applies 2 Weak (+1 upgraded → 3) and the SAME amount of Vulnerable to target.
    private static void ApplyPutrefy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, amount);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, amount);
    }

    // Shroud (a Power card): gives itself 2 Shroud (+1 upgraded → 3). BlockVar naming is
    // display-only (matches Rupture-family precedent) — the amount drives ShroudPower, not an
    // actual block gain.
    private static void ApplyShroud(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Shroud, amount);
    }

    // SleightOfFlesh (a Power card): gives itself 9 SleightOfFlesh (+4 upgraded → 13).
    private static void ApplySleightOfFlesh(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 13 : 9;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SleightOfFlesh, amount);
    }

    // Smokestack (a Power card): gives itself 5 Smokestack (+2 upgraded → 7).
    private static void ApplySmokestack(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 7 : 5;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Smokestack, amount);
    }

    // SpectrumShift (a Power card): gives itself 1 SpectrumShift (fixed — upgrade only reduces
    // Energy cost).
    private static void ApplySpectrumShift(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SpectrumShift, 1);
    }

    // SpiritOfAsh (a Power card): gives itself 4 SpiritOfAsh (+1 upgraded → 5).
    private static void ApplySpiritOfAsh(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 5 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SpiritOfAsh, amount);
    }

    // Stratagem (a Power card): gives itself 1 Stratagem (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyStratagem(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Stratagem, 1);
    }

    // Subroutine (a Power card): gives itself 1 Subroutine (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplySubroutine(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Subroutine, 1);
    }

    // StoneArmor (a Power card): gives itself 4 Plating (+2 upgraded → 6).
    private static void ApplyStoneArmor(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 6 : 4;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Plating, amount);
    }

    // Storm (a Power card): gives itself 1 Storm (+1 upgraded → 2).
    private static void ApplyStorm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Storm, amount);
    }

    // SwordSage (a Power card): gives itself 1 SwordSage (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplySwordSage(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SwordSage, 1);
    }

    // TheSealedThrone (a Power card): gives itself 1 TheSealedThrone (fixed — upgrade only
    // reduces Energy cost).
    private static void ApplyTheSealedThrone(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.TheSealedThrone, 1);
    }

    // Snakebite: applies 7 Poison (+3 upgraded → 10) to target. Retain keyword doesn't affect
    // play-time effect.
    private static void ApplySnakebite(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 10 : 7;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Poison, amount);
    }

    // Sow: 8 damage (+3 upgraded → 11) to every living enemy. Retain keyword doesn't affect
    // play-time effect.
    private static void ApplySow(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 11 : 8;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // Stomp: 12 damage (+3 upgraded → 15) to every living enemy. Its two cost-reduction hooks
    // (AfterCardEnteredCombat/BeforeCardPlayed) are cost mutation, out of scope for this function
    // (see MomentumStrike/Pinpoint/BansheesCry).
    private static void ApplyStomp(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 15 : 12;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // Bombardment: 18 damage (+6 upgraded → 24) to target. Its auto-play-from-Exhaust hook
    // (AfterAutoPrePlayPhaseEnteredEarly) is a separate deferred trigger point, out of scope for
    // this function (see HowlFromBeyond/DrumOfBattle precedent).
    private static void ApplyBombardment(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 24 : 18;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // BoostAway: gains 6 Block (+3 upgraded → 9), then generates 1 Dazed status card directly into
    // Discard (not Hand — confirmed against CardPileCmd.AddGeneratedCardToCombat(..., PileType.Discard, ...)).
    private static void ApplyBoostAway(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 9 : 6;
        GainPlayerBlock(state, block);
        SimCardPileOps.AppendGenerated(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, SimCardId.Dazed, upgraded: false);
    }

    // BrightestFlame: gains 2 Energy (+1 upgraded → 3), draws 2 (+1 upgraded → 3), then loses 1
    // Max HP (fixed — no upgrade on the MaxHp var), which may also cost current HP if it now
    // exceeds the lowered cap (see LosePlayerMaxHp).
    private static void ApplyBrightestFlame(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 3 : 2;
        int draws = card.IsUpgraded ? 3 : 2;
        GainPlayerEnergy(state, energy);
        SimCardPileOps.DrawCards(state, draws);
        LosePlayerMaxHp(state, 1);
    }

    // ChargeBattery: gains 7 Block (+3 upgraded → 10) then gives itself 1 EnergyNextTurn (fixed).
    private static void ApplyChargeBattery(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 10 : 7;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EnergyNextTurn, 1);
    }

    // Colossus: gains 5 Block (+3 upgraded → 8) then gives itself 1 Colossus (fixed).
    private static void ApplyColossus(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 8 : 5;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Colossus, 1);
    }

    // Convergence: gives itself 1 RetainHand (fixed), 1 EnergyNextTurn (fixed), and 1 StarNextTurn
    // (+1 upgraded → 2).
    private static void ApplyConvergence(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int stars = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.RetainHand, 1);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EnergyNextTurn, 1);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.StarNextTurn, stars);
    }

    // CrushUnder: 7 damage (+1 upgraded → 8) to every living enemy, then applies 1 CrushUnder
    // (+1 upgraded → 2) to every living enemy (the real game passes the whole HittableEnemies list
    // to a single PowerCmd.Apply call — applying per-enemy here is equivalent).
    private static void ApplyCrushUnder(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 7;
        int strengthLoss = card.IsUpgraded ? 2 : 1;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.CrushUnder, strengthLoss);
        }
    }

    // Defy: gains 6 Block (+3 upgraded → 9) then applies 1 Weak (fixed) to target. Ethereal
    // doesn't affect play-time effect.
    private static void ApplyDefy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 9 : 6;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, 1);
    }

    // Delay: gains 11 Block (+2 upgraded → 13) then gives itself 1 EnergyNextTurn (+1 upgraded →
    // 2).
    private static void ApplyDelay(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 13 : 11;
        int energy = card.IsUpgraded ? 2 : 1;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EnergyNextTurn, energy);
    }

    // Dominate: applies 1 Vulnerable (+1 upgraded → 2) to target, then reads the target's
    // resulting TOTAL Vulnerable amount (not just this card's own contribution) and gives the
    // player that much Strength — mirrors GetPower<VulnerablePower>()?.Amount exactly.
    private static void ApplyDominate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int vuln = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
        int totalVuln = SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Vulnerable, out short vulnAmt) ? vulnAmt : 0;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Strength, totalVuln);
    }

    // DrumOfBattle: draws 2 (fixed — the Cards var never upgrades, only the deferred
    // AfterCardExhausted energy-per-play-count hook's Energy var does, and that hook fires from
    // a different trigger point than OnPlay, out of scope for this function).
    private static void ApplyDrumOfBattle(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimCardPileOps.DrawCards(state, 2);
    }

    // EchoingSlash: deals 10 damage (+3 upgraded → 13) to every living enemy; for every enemy
    // killed in that pass, repeats ANOTHER full pass against whatever enemies are still alive —
    // mirrors the real while-loop exactly (attackCount starts at 1, decrements each pass,
    // increments once per kill that pass).
    private static void ApplyEchoingSlash(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 13 : 10;
        int attackCount = 1;
        int count = state.EnemyCount;
        while (attackCount > 0)
        {
            attackCount--;
            for (int i = 0; i < count; i++)
            {
                if (state.EnemyHp[i] == 0) continue;
                int hpBefore = state.EnemyHp[i];
                DealDamageToEnemy(state, i, dmg);
                if (hpBefore > 0 && state.EnemyHp[i] == 0) attackCount++;
            }
        }
    }

    // Exterminate: 3 damage (fixed — only Damage upgrades, hit count Repeat never does), hits 4
    // times, to every living enemy.
    private static void ApplyExterminate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 4 : 3;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealMultiHitDamageToEnemy(state, i, dmg, 4);
        }
    }

    // FightThrough: gains 13 Block (+4 upgraded → 17), then generates 2 Wound status cards
    // directly into Discard (fixed count — no upgrade on the loop count).
    private static void ApplyFightThrough(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 17 : 13;
        GainPlayerBlock(state, block);
        for (int i = 0; i < 2; i++)
            SimCardPileOps.AppendGenerated(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, SimCardId.Wound, upgraded: false);
    }

    // GammaBlast: 13 damage (+5 upgraded → 18) then 2 Weak (fixed) and 2 Vulnerable (fixed) to
    // target — only Damage upgrades.
    private static void ApplyGammaBlast(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 18 : 13;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, 2);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, 2);
    }

    // Glitterstream: gains 11 Block (+2 upgraded → 13), then gives itself BlockNextTurn equal to a
    // SEPARATE 5-damage-formula raw value (+2 upgraded → 7) run through the same
    // Dexterity/Frail/NoBlock pipeline as a normal block gain (confirmed against the real card's
    // explicit Hook.ModifyBlock call on its own "BlockNextTurn" var) — NOT the actual amount of
    // Block just gained from the first GainBlock call, those are two independent numbers.
    private static void ApplyGlitterstream(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 13 : 11;
        int blockNextTurnRaw = card.IsUpgraded ? 7 : 5;
        GainPlayerBlock(state, block);
        int dexterity = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Dexterity, out short dexAmt) ? dexAmt : 0;
        bool frail = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Frail, out _);
        bool noBlock = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.NoBlock, out _);
        int blockNextTurnAmount = SimBlock.Compute(blockNextTurnRaw, dexterity, frail, noBlock);
        if (blockNextTurnAmount > 0)
            SimPowerOps.ApplyPlayerDelta(state, SimPowerType.BlockNextTurn, blockNextTurnAmount);
    }

    // Glow: gains 1 Star (+1 upgraded → 2, Stars var upgrades), draws 1 (fixed — Cards var never
    // upgrades), then gives itself DrawCardsNextTurn equal to that SAME fixed Cards value (1) — one
    // var drives both the immediate draw and the next-turn draw amount, neither tied to Stars.
    private static void ApplyGlow(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int stars = card.IsUpgraded ? 2 : 1;
        state.PlayerStars = (ushort)Math.Min(999999999, state.PlayerStars + stars);
        SimCardPileOps.DrawCards(state, 1);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DrawCardsNextTurn, 1);
    }

    // GunkUp: 4 damage (+1 upgraded → 5), hits 3 times (fixed — Repeat never upgrades), then
    // generates 1 Slimed status card directly into Discard.
    private static void ApplyGunkUp(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 5 : 4;
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, 3);
        SimCardPileOps.AppendGenerated(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, SimCardId.Slimed, upgraded: false);
    }

    // Hegemony: 15 damage (+3 upgraded → 18) to target then gives itself 2 EnergyNextTurn
    // (+1 upgraded → 3).
    private static void ApplyHegemony(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 18 : 15;
        int energy = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EnergyNextTurn, energy);
    }

    // Hotfix: gives itself 2 Hotfix (fixed — upgrade only removes Exhaust keyword). The
    // "PowerVar<FocusPower>" naming is display-only (matches Rupture-family precedent) — the power
    // actually applied is HotfixPower, not FocusPower.
    private static void ApplyHotfix(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Hotfix, 2);
    }

    // Invoke: gives itself 2 SummonNextTurn (+1 upgraded → 3) and 2 EnergyNextTurn (+1 upgraded →
    // 3).
    private static void ApplyInvoke(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.SummonNextTurn, amount);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.EnergyNextTurn, amount);
    }

    // KnockoutBlow: 30 damage (+8 upgraded → 38) to target; if this damage instance actually kills
    // the target, gains 5 Stars (fixed — no upgrade on Stars var).
    private static void ApplyKnockoutBlow(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 38 : 30;
        int hpBefore = state.EnemyHp[targetEnemyIdx];
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        bool killed = hpBefore > 0 && state.EnemyHp[targetEnemyIdx] == 0;
        if (killed) state.PlayerStars = (ushort)Math.Min(999999999, state.PlayerStars + 5);
    }

    // Melancholy: gains 13 Block (+4 upgraded → 17). No other effect (its AfterDeath cost-mutation
    // hook is out of scope, see MomentumStrike/Pinpoint precedent).
    private static void ApplyMelancholy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 17 : 13;
        GainPlayerBlock(state, block);
    }

    // Overclock: draws 2 (+1 upgraded → 3), then generates 1 Burn status card directly into
    // Discard.
    private static void ApplyOverclock(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 3 : 2;
        SimCardPileOps.DrawCards(state, draws);
        SimCardPileOps.AppendGenerated(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, SimCardId.Burn, upgraded: false);
    }

    // PactsEnd: 17 damage (+6 upgraded → 23) to every living enemy, but ONLY if the Exhaust pile
    // currently holds at least 3 cards (fixed threshold — Cards var never upgrades) — mirrors the
    // real CanDealDamage check exactly.
    private static void ApplyPactsEnd(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        if (state.ExhaustCount < 3) return;
        int dmg = card.IsUpgraded ? 23 : 17;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // Peck: 2 damage (fixed), hits 3 times (+1 upgraded → 4, Repeat upgrades).
    private static void ApplyPeck(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int hits = card.IsUpgraded ? 4 : 3;
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, 2, hits);
    }

    // Restlessness: ONLY if this card is currently the sole card in Hand, draws 2 (+1 upgraded →
    // 3) one at a time and gains 2 Energy (+1 upgraded → 3) — mirrors IsOnlyCardInHand exactly.
    // Because the played card is still counted in HandCards during Apply (documented
    // simplification), "sole card in hand" here means HandCount == 1 at the moment of Apply.
    private static void ApplyRestlessness(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        if (state.HandCount != 1) return;
        int draws = card.IsUpgraded ? 3 : 2;
        int energy = card.IsUpgraded ? 3 : 2;
        SimCardPileOps.DrawCards(state, draws);
        GainPlayerEnergy(state, energy);
    }

    // RocketPunch: 13 damage (+1 upgraded → 14) to target then draws 1 (+1 upgraded → 2). Its
    // AfterCardGeneratedForCombat hook (making newly-generated Status cards free) is a separate
    // deferred trigger point, out of scope for this function.
    private static void ApplyRocketPunch(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 14 : 13;
        int draws = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimCardPileOps.DrawCards(state, draws);
    }

    // SevenStars: 7 damage (fixed), hits 7 times (fixed), to every living enemy — upgrade only
    // reduces Star cost, not any play-time number.
    private static void ApplySevenStars(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealMultiHitDamageToEnemy(state, i, 7, 7);
        }
    }

    // Shadowmeld (a Skill card): gives itself 1 Shadowmeld (fixed — upgrade only reduces Energy
    // cost).
    private static void ApplyShadowmeld(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Shadowmeld, 1);
    }

    // SharedFate: removes 2 Strength (fixed) from self, then removes 2 Strength (+1 upgraded → 3)
    // from target.
    private static void ApplySharedFate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int enemyLoss = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Strength, -2);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Strength, -enemyLoss);
    }

    // Alignment: gains 2 Energy (+1 upgraded → 3). Whole effect is the single PlayerCmd.GainEnergy
    // call.
    private static void ApplyAlignment(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 3 : 2;
        GainPlayerEnergy(state, energy);
    }

    // Bolas: 3 damage (+1 upgraded → 4) to target. Its BeforeHandDraw hook (returning itself to
    // hand if it was played last turn) is a separate deferred trigger point tied to full-turn
    // history tracking we don't maintain, out of scope for this function.
    private static void ApplyBolas(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 4 : 3;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // MindBlast: CalculatedDamage = CalculationBase(0, fixed) + ExtraDamage(1, fixed — upgrade
    // only reduces Energy cost) × (number of cards currently in the draw pile) — confirmed against
    // CalculatedVar.Calculate's exact formula (base + extra×multiplier) and MindBlast's own
    // multiplier function (draw pile Cards.Count). Innate keyword doesn't affect play-time effect.
    private static void ApplyMindBlast(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int raw = state.DrawCount;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    // Bully: CalculatedDamage = CalculationBase(4, fixed) + ExtraDamage(2, +1 upgraded → 3) ×
    // (target's current Vulnerable stack amount, 0 if none) — same CalculatedVar formula as
    // MindBlast, multiplier here reads the target's live Vulnerable amount instead of draw pile
    // size.
    private static void ApplyBully(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int extra = card.IsUpgraded ? 3 : 2;
        int vuln = SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Vulnerable, out short vulnAmt) ? vulnAmt : 0;
        int raw = 4 + extra * vuln;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    // AshenStrike: CalculatedDamage = CalculationBase(6, fixed) + ExtraDamage(3, +1 upgraded → 4)
    // × (number of cards currently in the Exhaust pile).
    private static void ApplyAshenStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int extra = card.IsUpgraded ? 4 : 3;
        int raw = 6 + extra * state.ExhaustCount;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    // TimesUp: CalculatedDamage = CalculationBase(0, fixed) + ExtraDamage(1, fixed — upgrade only
    // adds Retain) × (target's current Doom stack amount, 0 if none).
    private static void ApplyTimesUp(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int doom = SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Doom, out short doomAmt) ? doomAmt : 0;
        DealDamageToEnemy(state, targetEnemyIdx, doom);
    }

    // BodySlam: CalculatedDamage = CalculationBase(0, fixed) + ExtraDamage(1, fixed — upgrade only
    // reduces Energy cost) × (player's own CURRENT Block amount, read before this attack resolves).
    private static void ApplyBodySlam(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int raw = state.PlayerBlock;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    // Assassinate: 10 damage (+3 upgraded → 13) then 1 Vulnerable (+1 upgraded → 2) to target.
    private static void ApplyAssassinate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 13 : 10;
        int vuln = card.IsUpgraded ? 2 : 1;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // Comet: 33 damage (+11 upgraded → 44) then 3 Weak (fixed) and 3 Vulnerable (fixed) to target
    // — only Damage upgrades.
    private static void ApplyComet(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 44 : 33;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, 3);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, 3);
    }

    // DramaticEntrance: 11 damage (+4 upgraded → 15) to every living enemy.
    private static void ApplyDramaticEntrance(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 15 : 11;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
    }

    // DyingStar: 9 damage (+2 upgraded → 11) to every living enemy, then applies 9 DyingStar
    // (+2 upgraded → 11) to every living enemy (same amount for both, unlike most AoE+debuff
    // combos where the two numbers differ).
    private static void ApplyDyingStar(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 11 : 9;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.DyingStar, dmg);
        }
    }

    // ExpectAFight: gains Energy = CalculationBase(0, fixed) + CalculationExtra(1, fixed — upgrade
    // only reduces Energy cost) × (number of Attack-type cards currently in Hand, excluding this
    // card's own instance since it's a Skill card and never matches its own filter), then gives
    // itself 1 NoEnergyGain (fixed).
    private static void ApplyExpectAFight(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int attackCount = 0;
        int handCount = state.HandCount;
        for (int i = 0; i < handCount; i++)
        {
            if (SimCardTypeRegistry.Get(state.HandCards[i].BaseCardId) == SimCardType.Attack)
                attackCount++;
        }
        GainPlayerEnergy(state, attackCount);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.NoEnergyGain, 1);
    }

    // FallingStar: 8 damage (+4 upgraded → 12) then 1 Weak (fixed) and 1 Vulnerable (fixed) to
    // target.
    private static void ApplyFallingStar(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 12 : 8;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Weak, 1);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, 1);
    }

    // FightMe: 5 damage (+1 upgraded → 6), hits 2 times (fixed — Repeat never upgrades), then
    // gives itself 3 Strength (+1 upgraded → 4) and gives the target 1 Strength (fixed).
    private static void ApplyFightMe(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 6 : 5;
        int selfStrength = card.IsUpgraded ? 4 : 3;
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, 2);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Strength, selfStrength);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Strength, 1);
    }

    // Finesse: gains 4 Block (+3 upgraded → 7) then draws 1 (fixed).
    private static void ApplyFinesse(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 7 : 4;
        GainPlayerBlock(state, block);
        SimCardPileOps.DrawCards(state, 1);
    }

    // GuidingStar: 12 damage (+1 upgraded → 13) to target then draws 2 (+1 upgraded → 3).
    private static void ApplyGuidingStar(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 13 : 12;
        int draws = card.IsUpgraded ? 3 : 2;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimCardPileOps.DrawCards(state, draws);
    }

    // MeteorShower: 14 damage (+7 upgraded → 21) then 2 Weak (fixed) and 2 Vulnerable (fixed) to
    // every living enemy.
    private static void ApplyMeteorShower(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 21 : 14;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Weak, 2);
            SimPowerOps.ApplyEnemyDelta(state, i, SimPowerType.Vulnerable, 2);
        }
    }

    // NotYet: heals self 10 (+3 upgraded → 13), capped at Max HP.
    private static void ApplyNotYet(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int heal = card.IsUpgraded ? 13 : 10;
        HealPlayer(state, heal);
    }

    // Offering: loses 6 HP (fixed, unblockable+unpowered, self) then gains 2 Energy (fixed) then
    // draws 3 (+2 upgraded → 5).
    private static void ApplyOffering(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        LosePlayerHp(state, 6);
        GainPlayerEnergy(state, 2);
        int draws = card.IsUpgraded ? 5 : 3;
        SimCardPileOps.DrawCards(state, draws);
    }

    // Skim: draws 3 (+1 upgraded → 4). Whole effect is the single CardPileCmd.Draw call.
    private static void ApplySkim(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 4 : 3;
        SimCardPileOps.DrawCards(state, draws);
    }

    // Slice: 6 damage (+3 upgraded → 9) to target. No other effect.
    private static void ApplySlice(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 9 : 6;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
    }

    // SweepingBeam: 6 damage (+3 upgraded → 9) to every living enemy then draws 1 (fixed).
    private static void ApplySweepingBeam(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 9 : 6;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }
        SimCardPileOps.DrawCards(state, 1);
    }

    // TheGambit: gains 50 Block (+25 upgraded → 75) then gives itself 1 TheGambit (fixed).
    private static void ApplyTheGambit(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 75 : 50;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.TheGambit, 1);
    }

    // Burst (a Skill card): gives itself 1 Burst (+1 upgraded → 2).
    private static void ApplyBurst(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int amount = card.IsUpgraded ? 2 : 1;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Burst, amount);
    }

    // Expertise: draws up to 6 (+1 upgraded → 7), i.e. draws max(0, target − current Hand count).
    // Because the played card is still counted in HandCards during Apply (documented
    // simplification), this naturally matches the real "draw until hand reaches N" semantics
    // without needing to special-case excluding this card's own slot.
    private static void ApplyExpertise(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int target = card.IsUpgraded ? 7 : 6;
        int draws = Math.Max(0, target - state.HandCount);
        if (draws > 0) SimCardPileOps.DrawCards(state, draws);
    }

    // DoubleEnergy: gains Energy equal to the player's CURRENT Energy amount (doubling it).
    private static void ApplyDoubleEnergy(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        GainPlayerEnergy(state, state.Energy);
    }

    // Prolong: gives itself BlockNextTurn equal to the player's CURRENT Block amount (read live,
    // not a fixed number).
    private static void ApplyProlong(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.BlockNextTurn, state.PlayerBlock);
    }

    // Neurosurge (a Power card): gains 3 Energy (+1 upgraded → 4), draws 2 (fixed), then gives
    // itself 3 Neurosurge (fixed).
    private static void ApplyNeurosurge(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 4 : 3;
        GainPlayerEnergy(state, energy);
        SimCardPileOps.DrawCards(state, 2);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Neurosurge, 3);
    }

    // Wisp: gains 1 Energy (fixed — upgrade only adds Retain).
    private static void ApplyWisp(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        GainPlayerEnergy(state, 1);
    }

    // Fuel (a Token card): gains 1 Energy (fixed) then draws 1 (+1 upgraded → 2).
    private static void ApplyFuel(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        GainPlayerEnergy(state, 1);
        int draws = card.IsUpgraded ? 2 : 1;
        SimCardPileOps.DrawCards(state, draws);
    }

    // Luminesce (a Token card): gains 2 Energy (+1 upgraded → 3).
    private static void ApplyLuminesce(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 3 : 2;
        GainPlayerEnergy(state, energy);
    }

    // Expose: sets target's Block to 0 (a full removal, not a delta), fully removes Artifact if
    // present (applies a delta equal to the negative of its current amount, which the underlying
    // ApplyDelta clears at exactly 0 — mirrors PowerCmd.Remove exactly, not a -1 decrement), then
    // applies 2 Vulnerable (+1 upgraded → 3).
    private static void ApplyExpose(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        state.EnemyBlock[targetEnemyIdx] = 0;
        if (SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Artifact, out short artifactAmt) && artifactAmt != 0)
            SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Artifact, -artifactAmt);
        int vuln = card.IsUpgraded ? 3 : 2;
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Vulnerable, vuln);
    }

    // Shiv (a Token card): 4 damage (+2 upgraded → 6); targets a single enemy normally, but if the
    // player currently has FanOfKnives, targets EVERY living enemy instead — mirrors the real
    // HasFanOfKnives check exactly (reads the live power, not a fixed target-type choice).
    private static void ApplyShiv(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 6 : 4;
        bool hasFanOfKnives = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.FanOfKnives, out _);
        if (hasFanOfKnives)
        {
            int count = state.EnemyCount;
            for (int i = 0; i < count; i++)
            {
                if (state.EnemyHp[i] == 0) continue;
                DealDamageToEnemy(state, i, dmg);
            }
        }
        else
        {
            DealDamageToEnemy(state, targetEnemyIdx, dmg);
        }
    }

    // Soul (a Token card): draws 2 (+1 upgraded → 3). Whole effect is the single
    // CardPileCmd.Draw call.
    private static void ApplySoul(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int draws = card.IsUpgraded ? 3 : 2;
        SimCardPileOps.DrawCards(state, draws);
    }

    // Apotheosis: upgrades every other upgradable card the player owns (Hand/Draw/Disc/Exhaust —
    // the combat blob's entire "AllCards" equivalent), excluding itself by InstanceId per the real
    // `allCard != this` check. IsUpgradable in the real game is CurrentUpgradeLevel < MaxUpgradeLevel;
    // confirmed by grepping every `MaxUpgradeLevel => 0` override in game_source that this set is
    // exactly the Status/Curse/Quest-type cards (no Attack/Skill/Power card overrides it to 0), so
    // "not already upgraded and not Status/Curse/Quest" is an exact, verified substitute. Upgrading
    // just flips CardId bit 15 — every registered card's Apply already reads IsUpgraded live, so no
    // separate numeric-value bookkeeping is needed.
    private static void ApplyApotheosis(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        UpgradeAllUpgradable(state.HandCards, state.HandCount, card.InstanceId);
        UpgradeAllUpgradable(state.DrawCards, state.DrawCount, card.InstanceId);
        UpgradeAllUpgradable(state.DiscCards, state.DiscCount, card.InstanceId);
        UpgradeAllUpgradable(state.ExhaustCards, state.ExhaustCount, card.InstanceId);
    }

    private static void UpgradeAllUpgradable(Span<SimCard> pile, int count, ushort selfInstanceId)
    {
        for (int i = 0; i < count; i++)
        {
            ref SimCard c = ref pile[i];
            if (c.InstanceId == selfInstanceId) continue;
            if (c.IsUpgraded) continue;
            SimCardType type = SimCardTypeRegistry.Get(c.BaseCardId);
            if (type == SimCardType.Status || type == SimCardType.Curse || type == SimCardType.Quest) continue;
            c.CardId |= 0x8000;
        }
    }

    // Fisticuffs: 7 damage (+2 upgraded → 9) to target, then gains Block equal to the total
    // post-modifier damage actually dealt (DamageResult.TotalDamage + OverkillDamage, which is
    // exactly DealDamageToEnemy's returned "total" before block/HP clamping) — mirrors
    // AttackCommand.Results.Sum(TotalDamage + OverkillDamage) for the single AnyEnemy target, then
    // CreatureCmd.GainBlock (a normal powered gain, i.e. still runs through Dexterity/Frail/NoBlock).
    private static void ApplyFisticuffs(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 9 : 7;
        int dealt = DealDamageToEnemy(state, targetEnemyIdx, dmg);
        GainPlayerBlock(state, dealt);
    }

    // Mirage (a Skill card, Exhaust, self-target): Block = CalculationBase(0) + Extra(1) × the
    // live sum of Poison across every currently-alive enemy (CalculatedBlockVar's multiplier reads
    // combat state directly, not a fixed number). No numeric upgrade — OnUpgrade only reduces
    // Energy cost.
    private static void ApplyMirage(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int sum = 0;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            if (SimPowerOps.TryGetEnemyAmount(state, i, SimPowerType.Poison, out short amt)) sum += amt;
        }
        GainPlayerBlock(state, sum);
    }

    // Misery: 7 damage (+2 upgraded → 9) to target, then clones every Debuff-category power
    // currently on the target (snapshotted BEFORE the attack, matching the real
    // `ClonePreservingMutability` list captured before Execute) onto every OTHER living enemy as an
    // additive delta (mirrors PowerCmd.ModifyAmount/Apply with the snapshotted amount). Uses the
    // static SimPowerCategoryRegistry.IsDebuff (by power TYPE) rather than the real game's dynamic
    // TypeForCurrentAmount (which can flip sign-dependent Counter powers like Strength between
    // Buff/Debuff) — the same simplification SimPowerOps.ApplyDelta's Artifact-block check already
    // relies on elsewhere in this codebase, not a new gap. Does not replicate Misery's
    // ITemporaryPower.IgnoreNextInstance() hack (TemporaryStrength/Focus/Dexterity-only edge case,
    // irrelevant to plain-amount debuffs like Vulnerable/Weak/Poison/Frail).
    private static void ApplyMisery(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        var targetBitmap = state.EnemyPowerBitmaps[targetEnemyIdx];
        Span<short> targetValues = SimPowerOps.GetEnemyValues(state, targetEnemyIdx);
        Span<int> snapType = stackalloc int[SimPowerSet.ValueCap];
        Span<short> snapAmt = stackalloc short[SimPowerSet.ValueCap];
        int snapCount = 0;
        for (int t = 0; t < SimPowerType.Count; t++)
        {
            if (!SimPowerSet.Test(targetBitmap, t)) continue;
            if (!SimPowerCategoryRegistry.IsDebuff(t)) continue;
            SimPowerSet.TryGetAmount(targetBitmap, targetValues, t, out short amt);
            snapType[snapCount] = t;
            snapAmt[snapCount] = amt;
            snapCount++;
        }

        int dmg = card.IsUpgraded ? 9 : 7;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);

        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (i == targetEnemyIdx) continue;
            if (state.EnemyHp[i] == 0) continue;
            for (int s = 0; s < snapCount; s++)
                SimPowerOps.ApplyEnemyDelta(state, i, snapType[s], snapAmt[s]);
        }
    }

    // Omnislice: 8 damage (+3 upgraded → 11) to target (normal Powered hit — Strength/Vulnerable/
    // Weak/Cap all apply), then splashes the exact post-modifier total dealt (TotalDamage +
    // OverkillDamage, i.e. DealDamageToEnemy's returned value, uncapped by the target's own HP) onto
    // every OTHER living enemy as an Unpowered hit (skips Strength/Vulnerable/Weak but still runs
    // each receiver's own damage cap and Block absorption) — previously blocked on
    // DealDamageToEnemy not exposing this intermediate value; now that it does (see Fisticuffs), this
    // unblocks.
    private static void ApplyOmnislice(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 11 : 8;
        int dealt = DealDamageToEnemy(state, targetEnemyIdx, dmg);

        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (i == targetEnemyIdx) continue;
            if (state.EnemyHp[i] == 0) continue;
            DealUnpoweredDamageToEnemy(state, i, dealt);
        }
    }

    // BlightStrike: 8 damage (+2 upgraded → 10) to target, then applies Doom equal to the total
    // post-modifier damage actually dealt (TotalDamage sum — same value DealDamageToEnemy returns)
    // to that same target.
    private static void ApplyBlightStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 8;
        int dealt = DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Doom, dealt);
    }

    // CalculatedGamble: discards the entire hand EXCLUDING the card being played itself (known
    // simplification: the real game already moved this card to a transient Play pile before
    // `cards = Hand.Cards` is captured, so its own discard/draw count never includes itself — same
    // InstanceId-exclusion fix as ShadowStep/SecondWind/FiendFire), then draws back exactly as many
    // cards as were discarded (CardCmd.DiscardAndDraw(cards, cards.Count())).
    private static void ApplyCalculatedGamble(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int toDraw = 0;
        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            if (state.HandCards[i].InstanceId == card.InstanceId) continue;
            SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap);
            toDraw++;
        }
        SimCardPileOps.DrawCards(state, toDraw);
    }

    // Compact: Block 6 (+1 upgraded → 7), then transforms EVERY Status-type card currently in hand
    // into a fresh Fuel (upgraded to match Compact's own upgrade state) — no real player choice
    // involved (it's "all of them", not "pick one"). CardModel.IsTransformable is always true for
    // any card sitting in a combat pile (its only false-producing branch requires the card to be in
    // the permanent Deck pile, which never applies mid-combat), so the only real filter is
    // Type == Status — confirmed by reading IsTransformable's body directly, not assumed.
    private static void ApplyCompact(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 7 : 6;
        GainPlayerBlock(state, block);

        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            ref SimCard c = ref state.HandCards[i];
            if (SimCardTypeRegistry.Get(c.BaseCardId) != SimCardType.Status) continue;
            SimCardPileOps.RemoveAt(state.HandCards, ref state.HandCount, i);
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Fuel, card.IsUpgraded);
        }
    }

    // CrashLanding: 21 damage (+5 upgraded → 26) to every living enemy, then fills the remaining
    // hand slots (MaxCardsInHand - current hand count) with freshly generated Debris cards — fixed,
    // deterministic amount, no choice or RNG involved.
    private static void ApplyCrashLanding(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 26 : 21;
        int enemyCount = state.EnemyCount;
        for (int i = 0; i < enemyCount; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealDamageToEnemy(state, i, dmg);
        }

        // +1: the real game computes MaxCardsInHand - Hand.Cards.Count AFTER this card was already
        // moved to the transient Play pile (known simplification — state.HandCount here still
        // counts this card itself, so the raw subtraction would fill one slot short).
        int toAdd = CombatSimLayout.HandCap - state.HandCount + 1;
        for (int i = 0; i < toAdd; i++)
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Debris, false);
    }

    // CrescentSpear: CalculatedDamage = CalculationBase(8) + ExtraDamage(2, +1 upgraded → 3) ×
    // (count of every card the player owns, across Hand/Draw/Disc/Exhaust, with a star cost —
    // CanonicalStarCost >= 0 OR HasStarCostX). PlayerCombatState.AllCards includes the transient
    // Play pile the real game moves the card being played into, so CrescentSpear DOES count itself
    // (it has CanonicalStarCost == 1) — matches this file's "known simplification" exactly (no
    // separate Play pile here either, card stays in HandCards), so no self-exclusion needed. Only
    // one card in the whole game overrides HasStarCostX (Stardust, whose own CanonicalStarCost stays
    // -1) — special-cased directly rather than building a one-consumer registry for it.
    private static void ApplyCrescentSpear(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int count = CountStarCostCards(state);
        int extra = card.IsUpgraded ? 3 : 2;
        int raw = 8 + extra * count;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    private static int CountStarCostCards(CombatNodeBlob state)
    {
        int n = 0;
        n += CountStarCostInPile(state.HandCards, state.HandCount);
        n += CountStarCostInPile(state.DrawCards, state.DrawCount);
        n += CountStarCostInPile(state.DiscCards, state.DiscCount);
        n += CountStarCostInPile(state.ExhaustCards, state.ExhaustCount);
        return n;
    }

    private static int CountStarCostInPile(Span<SimCard> pile, int count)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            ref SimCard c = ref pile[i];
            if (c.BaseStarCost >= 0 || c.BaseCardId == SimCardId.Stardust) n++;
        }
        return n;
    }

    // Eidolon: exhausts the entire hand EXCLUDING the card being played itself (known
    // simplification — real game already moved it to the transient Play pile before
    // `Hand.Cards.ToList()` snapshots, so it never exhausts itself; same InstanceId-exclusion fix as
    // ShadowStep/SecondWind/FiendFire/CalculatedGamble), then applies 1 Intangible if 9 or more OTHER
    // cards were exhausted this way (exhaustedCount, matching the real game's post-exclusion count).
    private static void ApplyEidolon(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int exhaustedCount = 0;
        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            if (state.HandCards[i].InstanceId == card.InstanceId) continue;
            SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.ExhaustCards, ref state.ExhaustCount, CombatSimLayout.PileCap);
            exhaustedCount++;
        }
        if (exhaustedCount >= 9)
            SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Intangible, 1);
    }

    // NoEscape: applies Doom to target equal to CalculationBase(10, +5 upgraded → 15) +
    // CalculationExtra(5, never upgrades) × floor(target's current Doom amount / DoomThreshold(10,
    // never upgrades)). Doom amount is always non-negative in practice, so integer division matches
    // Math.Floor exactly.
    private static void ApplyNoEscape(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int doomAmt = SimPowerOps.TryGetEnemyAmount(state, targetEnemyIdx, SimPowerType.Doom, out short amt) ? amt : 0;
        int baseVal = card.IsUpgraded ? 15 : 10;
        int raw = baseVal + 5 * (doomAmt / 10);
        SimPowerOps.ApplyEnemyDelta(state, targetEnemyIdx, SimPowerType.Doom, raw);
    }

    // Patter: Block 8 (+2 upgraded → 10), then gives itself 2 Vigor (+1 upgraded → 3). Vigor's own
    // "consumed by next attack" damage-boost effect isn't in SimDamage's coverage yet (see
    // dev_docs/SimDamage_Coverage_Backlog.md) — out of scope for this card's own Apply, same as
    // Strength/Weak were tracked as bare power stacks before the damage pipeline caught up.
    private static void ApplyPatter(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 10 : 8;
        int vigor = card.IsUpgraded ? 3 : 2;
        GainPlayerBlock(state, block);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Vigor, vigor);
    }

    // Pounce: 14 damage (+6 upgraded → 20) to target, then gives itself 1 FreeSkill (fixed).
    // FreeSkill's "next Skill card costs 0" consumption isn't modeled (same unmodeled-cost-mechanic
    // boundary as the temporary-cost-override cards) — out of scope for this card's own Apply.
    private static void ApplyPounce(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 20 : 14;
        DealDamageToEnemy(state, targetEnemyIdx, dmg);
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.FreeSkill, 1);
    }

    // PreciseCut: CalculatedDamage = CalculationBase(13, +3 upgraded → 16) + ExtraDamage(2, never
    // upgrades) × -(hand count excluding itself). The card is always played from Hand (TargetType
    // AnyEnemy), so the real multiplier's `if (pile.Type == Hand) num--` branch always fires —
    // matches this file's "known simplification" exactly (card still counted in HandCards here too).
    private static void ApplyPreciseCut(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int baseVal = card.IsUpgraded ? 16 : 13;
        int handCountExcludingSelf = state.HandCount - 1;
        int raw = baseVal - 2 * handCountExcludingSelf;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    // PrimalForce: transforms EVERY Attack-type card currently in hand into a fresh GiantRock
    // (upgraded to match PrimalForce's own upgrade state) — no real choice, "all of them" like
    // Compact; IsTransformable is always true mid-combat (see Compact's comment).
    private static void ApplyPrimalForce(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            ref SimCard c = ref state.HandCards[i];
            if (SimCardTypeRegistry.Get(c.BaseCardId) != SimCardType.Attack) continue;
            SimCardPileOps.RemoveAt(state.HandCards, ref state.HandCount, i);
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.GiantRock, card.IsUpgraded);
        }
    }

    // PerfectedStrike: CalculatedDamage = CalculationBase(6, never upgrades) + ExtraDamage(2, +1
    // upgraded → 3) × count of every Strike-tagged card the player owns across Hand/Draw/Disc/
    // Exhaust. PerfectedStrike itself carries CardTag.Strike, so (matching AllCards including the
    // transient Play pile, confirmed for CrescentSpear) it counts itself too — no self-exclusion
    // needed, HandCards already includes it during Apply.
    private static void ApplyPerfectedStrike(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int count = CountStrikeTagCards(state);
        int extra = card.IsUpgraded ? 3 : 2;
        int raw = 6 + extra * count;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    private static int CountStrikeTagCards(CombatNodeBlob state)
    {
        int n = 0;
        n += CountStrikeInPile(state.HandCards, state.HandCount);
        n += CountStrikeInPile(state.DrawCards, state.DrawCount);
        n += CountStrikeInPile(state.DiscCards, state.DiscCount);
        n += CountStrikeInPile(state.ExhaustCards, state.ExhaustCount);
        return n;
    }

    private static int CountStrikeInPile(Span<SimCard> pile, int count)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            if (SimCardStrikeTagRegistry.Get(pile[i].BaseCardId)) n++;
        }
        return n;
    }

    // RoyalGamble (a 5-star-cost Skill card): gains 9 Stars (fixed — despite the name, there's no
    // actual randomness in OnPlay; OnUpgrade only adds Retain).
    private static void ApplyRoyalGamble(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        state.PlayerStars = (ushort)Math.Min(999999999, state.PlayerStars + 9);
    }

    // Scrawl: draws to fill the hand completely (MaxCardsInHand - hand count). +1 vs. the naive
    // subtraction for the same reason as CrashLanding: the real game computes hand count AFTER this
    // card was already moved to the transient Play pile, but state.HandCount here still counts it.
    private static void ApplyScrawl(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int toDraw = CombatSimLayout.HandCap - state.HandCount + 1;
        SimCardPileOps.DrawCards(state, toDraw);
    }

    // SoulStorm: CalculatedDamage = CalculationBase(9, never upgrades) + ExtraDamage(2, +1 upgraded
    // → 3) × count of Soul-type cards currently in the Exhaust pile.
    private static void ApplySoulStorm(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int soulCount = 0;
        int count = state.ExhaustCount;
        for (int i = 0; i < count; i++)
        {
            if (state.ExhaustCards[i].BaseCardId == SimCardId.Soul) soulCount++;
        }
        int extra = card.IsUpgraded ? 3 : 2;
        int raw = 9 + extra * soulCount;
        DealDamageToEnemy(state, targetEnemyIdx, raw);
    }

    // SporeMind (a Curse): doesn't override OnPlay at all in the real game — CardModel.OnPlay's base
    // implementation is a pure no-op (`return Task.CompletedTask;`), confirmed by reading it
    // directly. Playing this card genuinely does nothing.
    private static void ApplySporeMind(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
    }

    // Stack: CalculatedBlock = CalculationBase(0, +3 upgraded → 3) + CalculationExtra(1, never
    // upgrades) × current Discard pile count.
    private static void ApplyStack(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int baseVal = card.IsUpgraded ? 3 : 0;
        int block = baseVal + state.DiscCount;
        GainPlayerBlock(state, block);
    }

    // StormOfSteel: discards the entire hand EXCLUDING itself (known-simplification InstanceId
    // exclusion, same as ShadowStep/SecondWind/FiendFire/CalculatedGamble/Eidolon), then creates
    // that many Shivs (upgraded to match StormOfSteel's own upgrade state) directly into Hand. The
    // regenerated Shivs can never overflow HandCap: hand was just fully emptied by the discard loop,
    // and handSize is bounded by the pile's own cap.
    private static void ApplyStormOfSteel(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int handSize = 0;
        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            if (state.HandCards[i].InstanceId == card.InstanceId) continue;
            SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap);
            handSize++;
        }
        for (int i = 0; i < handSize; i++)
            SimCardPileOps.AppendGenerated(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, SimCardId.Shiv, card.IsUpgraded);
    }

    // Terraforming: gives itself 6 Vigor (+2 upgraded → 8). No other effect.
    private static void ApplyTerraforming(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int vigor = card.IsUpgraded ? 8 : 6;
        SimPowerOps.ApplyPlayerDelta(state, SimPowerType.Vigor, vigor);
    }

    // Toxic (a Status card): no OnPlay override in the real game — same as SporeMind, the base
    // implementation is a pure no-op. Its real damage effect (OnTurnEndInHand) only fires if this
    // card is still sitting in hand at end of turn — a separate turn-boundary mechanic outside this
    // file's OnPlay scope, not part of "playing" it.
    private static void ApplyToxic(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
    }

    // Turbo: gains 2 Energy (+1 upgraded → 3), then generates 1 Void card (fixed, never upgraded —
    // Void itself is a Curse with MaxUpgradeLevel 0) directly into Discard.
    private static void ApplyTurbo(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int energy = card.IsUpgraded ? 3 : 2;
        GainPlayerEnergy(state, energy);
        SimCardPileOps.AppendGenerated(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, SimCardId.Void, false);
    }

    // Undeath: Block 7 (+2 upgraded → 9), then clones itself into Discard (fixed — unlike
    // AdaptiveStrike's clone, this one has no cost override, so no unmodeled-cost-mechanic gap).
    private static void ApplyUndeath(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int block = card.IsUpgraded ? 9 : 7;
        GainPlayerBlock(state, block);
        SimCardPileOps.AppendGenerated(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, SimCardId.Undeath, card.IsUpgraded);
    }

    // ── X-cost card writers ──────────────────────────────────────────────────────────────────
    // Unblocked by PlayCard now actually resolving/capturing X (see SimCardEnergyOps). Each of
    // these reads GetCapturedXValue instead of the real game's CardModel.ResolveEnergyXValue() —
    // that method additionally applies X-value-modifying relic hooks (e.g. ChemicalX), which this
    // codebase doesn't model at all yet (no relic layer exists here), so raw captured X is the
    // correct and complete value for the relic-free case this sim currently covers.

    // Eradicate: 11 damage (+3 upgraded → 14), hits captured-X times.
    private static void ApplyEradicate(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 14 : 11;
        int hits = SimCardEnergyOps.GetCapturedXValue(state, in card);
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, hits);
    }

    // HeavenlyDrill: 8 damage (+2 upgraded → 10), hits captured-X times, DOUBLED if captured-X >= 4
    // (EnergyVar(4) never upgrades — only Damage does).
    private static void ApplyHeavenlyDrill(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 10 : 8;
        int hits = SimCardEnergyOps.GetCapturedXValue(state, in card);
        if (hits >= 4) hits *= 2;
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, hits);
    }

    // Skewer: 8 damage (+3 upgraded → 11), hits captured-X times.
    private static void ApplySkewer(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 11 : 8;
        int hits = SimCardEnergyOps.GetCapturedXValue(state, in card);
        DealMultiHitDamageToEnemy(state, targetEnemyIdx, dmg, hits);
    }

    // Whirlwind: 5 damage (+3 upgraded → 8) to EVERY living enemy, captured-X times each.
    private static void ApplyWhirlwind(CombatNodeBlob state, in SimCard card, int targetEnemyIdx)
    {
        int dmg = card.IsUpgraded ? 8 : 5;
        int hits = SimCardEnergyOps.GetCapturedXValue(state, in card);
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DealMultiHitDamageToEnemy(state, i, dmg, hits);
        }
    }
}
