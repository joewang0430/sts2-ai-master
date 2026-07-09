using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Runs;

namespace STS2.Agent.Sim;

/// <summary>
/// Bit-packed mirror of the run's active <c>AscensionLevel</c> flags. Ascension is a run-level
/// setting (chosen at run start, never changes mid-run, let alone mid-combat), and there are only
/// 10 real flags — so this is captured as a single <c>ushort</c> bitmask directly in the per-node
/// blob (not worth a separate "combat-constant, shared by reference" object like
/// <see cref="CombatRootRelics"/>; at 2 bytes, the per-node copy cost is already negligible).
///
/// Exists because ~400 call sites across monster source (<c>AscensionHelper.GetValueIfAscension</c>)
/// pick between two hardcoded numbers based on these flags — <see cref="SimMonsterMoveEffects"/>
/// needs to replicate that branch using OUR captured state, not by calling back into the live
/// game's <c>RunManager</c>.
/// </summary>
internal static class SimAscension
{
    public const int SwarmingElites = 1 << 0;
    public const int WearyTraveler  = 1 << 1;
    public const int Poverty        = 1 << 2;
    public const int TightBelt      = 1 << 3;
    public const int AscendersBane  = 1 << 4;
    public const int Inflation      = 1 << 5;
    public const int Scarcity       = 1 << 6;
    public const int ToughEnemies   = 1 << 7;
    public const int DeadlyEnemies  = 1 << 8;
    public const int DoubleBoss     = 1 << 9;

    /// <summary>Reads all 10 flags from the live <see cref="RunManager"/> into a packed bitmask.</summary>
    public static ushort CaptureLive()
    {
        ushort flags = 0;
        if (RunManager.Instance.HasAscension(AscensionLevel.SwarmingElites)) flags |= SwarmingElites;
        if (RunManager.Instance.HasAscension(AscensionLevel.WearyTraveler)) flags |= WearyTraveler;
        if (RunManager.Instance.HasAscension(AscensionLevel.Poverty)) flags |= Poverty;
        if (RunManager.Instance.HasAscension(AscensionLevel.TightBelt)) flags |= TightBelt;
        if (RunManager.Instance.HasAscension(AscensionLevel.AscendersBane)) flags |= AscendersBane;
        if (RunManager.Instance.HasAscension(AscensionLevel.Inflation)) flags |= Inflation;
        if (RunManager.Instance.HasAscension(AscensionLevel.Scarcity)) flags |= Scarcity;
        if (RunManager.Instance.HasAscension(AscensionLevel.ToughEnemies)) flags |= ToughEnemies;
        if (RunManager.Instance.HasAscension(AscensionLevel.DeadlyEnemies)) flags |= DeadlyEnemies;
        if (RunManager.Instance.HasAscension(AscensionLevel.DoubleBoss)) flags |= DoubleBoss;
        return flags;
    }
}
