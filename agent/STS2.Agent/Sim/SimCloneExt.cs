using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Deep-copy helpers. Kept as extension methods so the data class
/// (SimCombatState) stays pure-data and the copy strategy can evolve
/// independently (e.g. pool allocator later).
/// </summary>
internal static class SimCloneExt
{
    /// <summary>
    /// Deep copy. Hot path during DFS — every node expansion calls this.
    /// Implementation will be filled in when we start writing search.
    /// Stubbed now to keep the layout-only commit compiling.
    /// </summary>
    public static SimCombatState Clone(this SimCombatState src)
    {
        // TODO: implement after SimCmd / search loop is in place.
        // Will be: scalar copy + Buffer.BlockCopy on int[] + List.AddRange.
        throw new NotImplementedException("SimCloneExt.Clone — fill in with search loop.");
    }
}
