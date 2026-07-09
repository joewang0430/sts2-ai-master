namespace STS2.Agent.Sim;

/// <summary>
/// Which monster type a Summon-tagged move creates — <see cref="SimMoveEffect.PowerType"/> holds
/// one of these when <c>Kind == SimMoveEffectKind.Summon</c>.
///
/// Deliberately NOT an exhaustive registry over all monster classes (unlike SimCardId/SimRelicId):
/// only the handful of monster types that some Summon move in <see cref="SimMonsterMoveEffects"/>
/// actually references need an entry here. Grows by hand, one line at a time, alongside whichever
/// summon-type monster gets implemented next — see dev_docs/Enemy_Intent_Payload_Backlog.md.
/// </summary>
internal static class SimSummonTargetId
{
    public const ushort None = 0;
    public const ushort EyeWithTeeth = 1;
    public const ushort GasBomb = 2;
    public const ushort ToughEgg = 3;
    public const ushort Parafright = 4;
    public const ushort TwoTailedRat = 5;
}
