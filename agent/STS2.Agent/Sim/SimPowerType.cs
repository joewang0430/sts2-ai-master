namespace STS2.Agent.Sim;

/// <summary>
/// Compact int indices for powers we model in the simulator.
/// Keep small and contiguous so SimCombatState can use int[Count] arrays
/// (one L1 cache line per ~16 powers — much faster than Dictionary lookup).
///
/// Mapping to the game's string-based PowerType ID is done at the boundary
/// (SimCombatState.FromReal / a side table). This file is pure data layout.
/// </summary>
internal static class SimPowerType
{
    // Numeric layer (apply on damage/block calc each turn).
    public const int Strength   = 0;
    public const int Dexterity  = 1;
    public const int Vulnerable = 2;  // taker takes +50% attack damage
    public const int Weak       = 3;  // dealer deals -25% attack damage
    public const int Frail      = 4;  // taker gains -25% block

    // Block-related.
    public const int Block      = 5;  // not a power; placeholder slot if ever needed

    // Reserve room for additions without re-indexing.
    public const int Count      = 32; // size of int[] in SimCreature.Powers
}
