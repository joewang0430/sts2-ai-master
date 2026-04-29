namespace STS2.Agent.Sim;

/// <summary>
/// Compact int indices for powers we model in the simulator.
/// Stored densely as <c>sbyte[Count]</c> per creature in <see cref="SimCombatState"/>;
/// the index maps to the game's string-based PowerType id via a startup-built table.
///
/// Count = 200 covers every PowerModel subclass currently in the game (~200 files in
/// MegaCrit.Sts2.Core.Models.Powers as of 2026-04). Verified at mod startup by
/// SimCaps.Verify(); if a future game update pushes the count past 200, edit this
/// constant manually.
/// </summary>
internal static class SimPowerType
{
    // Hand-pinned indices used by hot damage/block math. The remainder of the index
    // space (6..Count-1) is filled at startup by reflection over PowerModel subclasses.
    public const int Strength   = 0;
    public const int Dexterity  = 1;
    public const int Vulnerable = 2;  // taker takes +50% attack damage
    public const int Weak       = 3;  // dealer deals -25% attack damage
    public const int Frail      = 4;  // taker gains -25% block
    public const int Block      = 5;  // not a power; reserved slot

    public const int Count      = 200;
}

