namespace STS2.Agent.Sim;

/// <summary>
/// Mirrors <c>PowerModel.AllowNegative</c> (game_source) — controls when a power auto-removes
/// after a delta (see <see cref="SimPowerOps"/>): most powers remove once their amount drops to
/// &lt;= 0, but the handful that allow negative amounts (Strength keeps going negative, etc.)
/// only remove at exactly 0.
///
/// Grep-verified against game_source/MegaCrit.Sts2.Core.Models.Powers for
/// "AllowNegative => true" / "override bool AllowNegative" — exactly 5 hits, listed below.
/// Deliberately a small hand-maintained table, not reflection-built: unlike SimCardId/SimRelicId,
/// nothing here needs to be exhaustive over all 261 power types — only these 5 differ from the
/// PowerModel base class's `virtual bool AllowNegative => false`.
/// </summary>
internal static class SimPowerAllowNegative
{
    public static bool Get(int type) => type switch
    {
        SimPowerType.Strength => true,
        SimPowerType.Dexterity => true,
        SimPowerType.Focus => true,
        SimPowerType.Shriek => true,
        SimPowerType.Shrink => true,
        _ => false,
    };
}
