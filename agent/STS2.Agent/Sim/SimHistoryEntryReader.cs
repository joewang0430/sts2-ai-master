using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat.History;

namespace STS2.Agent.Sim;

/// <summary>
/// Reflection accessor for <see cref="CombatHistoryEntry"/>'s <c>RoundNumber</c>
/// property, which the game made private (it was public prior to this patch).
/// <see cref="CombatHistoryEntry.HappenedThisTurn"/> / <c>HappenedLastPlayerTurn</c>
/// don't expose an arbitrary round-number comparison (e.g. "happened exactly one
/// round ago"), which <see cref="SimHistoryCounterOps"/> needs.
/// </summary>
internal static class SimHistoryEntryReader
{
    private static readonly PropertyInfo _roundNumberProperty =
        typeof(CombatHistoryEntry).GetProperty("RoundNumber", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "SimHistoryEntryReader: CombatHistoryEntry.RoundNumber not found. Game source layout changed.");

    public static int ReadRoundNumber(CombatHistoryEntry entry)
        => (int)_roundNumberProperty.GetValue(entry)!;
}
