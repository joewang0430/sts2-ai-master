using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// Which concrete <c>MonsterModel</c> subclass an enemy slot holds — <see cref="CombatNodeBlob.EnemyMonsterKind"/>
/// stores one of these per enemy. Needed wherever a move-state-machine's own logic reads "is a
/// specific OTHER monster type present/alive" (e.g. Queen's REVIVE_BRANCH checks whether her
/// TorchHeadAmalgam companion has died) or a Summon effect needs to uniquify a freshly-rolled HP
/// against other same-type enemies already in the fight (e.g. ToughEgg).
///
/// Deliberately NOT an exhaustive registry over all monster classes (same philosophy as
/// <see cref="SimSummonTargetId"/>) — only the handful of types some piece of Advance/Summon logic
/// actually needs to cross-reference get an entry here. Grows by hand, one line at a time.
/// </summary>
internal static class SimMonsterKind
{
    public const ushort None = 0;
    public const ushort TorchHeadAmalgam = 1;
    public const ushort ToughEgg = 2;
    public const ushort Parafright = 3;
    public const ushort EyeWithTeeth = 4;
    public const ushort GasBomb = 5;
    public const ushort TwoTailedRat = 6;

    /// <summary>Maps a live monster instance to its <see cref="SimMonsterKind"/> value, or
    /// <see cref="None"/> if it isn't one of the handful of types registered here.</summary>
    public static ushort Of(MonsterModel? monster) => monster switch
    {
        MegaCrit.Sts2.Core.Models.Monsters.TorchHeadAmalgam => TorchHeadAmalgam,
        MegaCrit.Sts2.Core.Models.Monsters.ToughEgg => ToughEgg,
        MegaCrit.Sts2.Core.Models.Monsters.Parafright => Parafright,
        MegaCrit.Sts2.Core.Models.Monsters.EyeWithTeeth => EyeWithTeeth,
        MegaCrit.Sts2.Core.Models.Monsters.GasBomb => GasBomb,
        MegaCrit.Sts2.Core.Models.Monsters.TwoTailedRat => TwoTailedRat,
        _ => None,
    };
}
