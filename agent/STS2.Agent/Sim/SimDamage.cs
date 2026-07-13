namespace STS2.Agent.Sim;

/// <summary>
/// Pure damage calculation. Mirrors the game's Hook.ModifyDamage order — confirmed by reading
/// Hook.ModifyDamageInternal directly (game_source/Hooks/Hook.cs), not assumed:
///   1. Additive   (Strength contributes +amount when dealer is the attacker)
///   2. Multiplicative (Vulnerable ×1.5 on target; Weak ×0.75 on dealer — multiple multiplicative
///      hooks combine by sequential `num *= factor`, so combining order never matters, only which
///      factors apply)
///   3. Cap (the MINIMUM of every applicable ModifyDamageCap value wins — see below)
///
/// Source-of-truth references (inspected in game_source):
///   StrengthPower.cs       — additive +Amount when dealer == owner & IsPoweredAttack
///   VulnerablePower.cs     — ×1.5 when target == owner & IsPoweredAttack
///   WeakPower.cs           — ×0.75 when dealer == owner & IsPoweredAttack
///   IntangiblePower.cs     — caps damage to target at exactly 1, always (ModifyDamageCap)
///   HardToKillPower.cs     — caps damage to target at the power's own Amount (ModifyDamageCap)
///   Hook.cs ModifyDamageInternal — confirms the three-phase order above, and that the Cap phase
///     takes the MINIMUM cap across every hook listener that returns one (not first-match, not a
///     priority order) — matters if a target ever has both Intangible AND HardToKill at once.
///
/// Only 2 of the 27 damage-modifying powers in the game are covered here (the 3 above plus these
/// 2 cap ones) — see dev_docs/SimDamage_Coverage_Backlog.md for the rest, most of which are
/// boss/card-specific rather than universal.
///
/// All inputs are pre-resolved ints; this is hot-path math, no allocations.
/// </summary>
internal static class SimDamage
{
    /// <summary>
    /// Compute final post-mitigation damage (before block subtraction).
    /// Caller subtracts target's Block separately. <paramref name="damageCap"/> is the MINIMUM of
    /// every applicable ModifyDamageCap value already resolved by the caller (e.g. via
    /// <c>Math.Min</c> across Intangible/HardToKill) — pass null if neither is present.
    /// </summary>
    public static int Compute(int raw, int dealerStrength, bool targetVulnerable, bool dealerWeak, int? damageCap = null)
    {
        // Strength can go negative; clamp at 0 here matches game truncation.
        decimal d = raw + dealerStrength;
        if (d < 0m) d = 0m;
        if (targetVulnerable) d *= 1.5m;
        if (dealerWeak)       d *= 0.75m;
        if (damageCap.HasValue && d > damageCap.Value) d = damageCap.Value;
        return (int)d; // game truncates decimal → int when applying to HP
    }
}
