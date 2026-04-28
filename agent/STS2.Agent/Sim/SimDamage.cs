namespace STS2.Agent.Sim;

/// <summary>
/// Pure damage calculation. Mirrors the game's Hook.ModifyDamage order:
///   1. Additive   (Strength contributes +amount when dealer is the attacker)
///   2. Multiplicative (Vulnerable ×1.5 on target; Weak ×0.75 on dealer)
///
/// Source-of-truth references (inspected in game_source):
///   StrengthPower.cs       — additive +Amount when dealer == owner & IsPoweredAttack
///   VulnerablePower.cs     — ×1.5 when target == owner & IsPoweredAttack
///   WeakPower.cs           — ×0.75 when dealer == owner & IsPoweredAttack
///   Hook.cs ModifyDamage   — Additive applied first, then Multiplicative
///
/// All inputs are pre-resolved ints; this is hot-path math, no allocations.
/// </summary>
internal static class SimDamage
{
    /// <summary>
    /// Compute final post-mitigation damage (before block subtraction).
    /// Caller subtracts target's Block separately.
    /// </summary>
    public static int Compute(int raw, int dealerStrength, bool targetVulnerable, bool dealerWeak)
    {
        // Strength can go negative; clamp at 0 here matches game truncation.
        decimal d = raw + dealerStrength;
        if (d < 0m) d = 0m;
        if (targetVulnerable) d *= 1.5m;
        if (dealerWeak)       d *= 0.75m;
        return (int)d; // game truncates decimal → int when applying to HP
    }
}
