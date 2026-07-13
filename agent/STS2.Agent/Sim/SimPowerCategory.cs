namespace STS2.Agent.Sim;

/// <summary>
/// Mirrors the game's <c>MegaCrit.Sts2.Core.Entities.Powers.PowerType</c> 1:1, in the same
/// declaration order — same idiom as <see cref="SimCardType"/> for CardType. Used by
/// <see cref="SimPowerCategoryRegistry"/> to know which power types Artifact can block (see
/// <see cref="SimPowerOps"/>).
/// </summary>
internal enum SimPowerCategory : byte
{
    None = 0,
    Buff = 1,
    Debuff = 2,
}
