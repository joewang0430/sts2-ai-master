namespace STS2.Agent.Sim;

/// <summary>
/// Immutable per-combat snapshot of the player's relic identity — which relics they hold (in
/// <c>Player.Relics</c> order), their wax/melted state and stack count. Built ONCE per combat by
/// <see cref="CombatNodeBlobSnapshot"/> and shared by reference across every search-tree node
/// cloned from that root (see <see cref="CombatNodeBlob.Relics"/> / <see cref="CombatNodeBlob.CopyFrom"/>),
/// instead of being byte-copied into every node like the rest of the blob.
///
/// Why sharing by reference is safe here specifically: relic identity never changes during a
/// single combat — every <c>AddRelicInternal</c> / <c>RemoveRelicInternal</c> / <c>MeltRelicInternal</c>
/// call site in game_source is outside combat scope (events, rest site, shop, treasure). Only the
/// small per-relic counter (for the ~13% of relics that have one) changes turn to turn, and that
/// lives separately in the real per-node blob (<see cref="CombatNodeBlob.RelicCounters"/>), not here.
///
/// All fields are readonly and set once in the constructor. This must never be mutated after
/// construction — every node in a search tree descending from the same root points at the exact
/// same instance, so a mutation here would silently corrupt every one of them at once.
/// </summary>
internal sealed class CombatRootRelics
{
    public const byte FlagIsWax = 1 << 0;
    public const byte FlagIsMelted = 1 << 1;

    /// <summary>Number of relics actually held; only indices [0, Count) of the arrays below are valid.</summary>
    public readonly int Count;

    /// <summary><see cref="SimRelicId"/> per held relic, in <c>Player.Relics</c> order.</summary>
    public readonly ushort[] Ids;

    /// <summary>Per-slot bitfield: <see cref="FlagIsWax"/> | <see cref="FlagIsMelted"/>.</summary>
    public readonly byte[] Flags;

    /// <summary>Per-slot <c>RelicModel.StackCount</c> (Circlet etc.), clamped to byte.</summary>
    public readonly byte[] StackCounts;

    /// <summary>
    /// Per-slot index into <see cref="CombatNodeBlob.RelicCounters"/> for relics where
    /// <see cref="SimRelicDb.HasCounter"/> is true, or -1 for relics with no counter.
    /// </summary>
    public readonly sbyte[] CounterSlot;

    public CombatRootRelics(int count, ushort[] ids, byte[] flags, byte[] stackCounts, sbyte[] counterSlot)
    {
        Count = count;
        Ids = ids;
        Flags = flags;
        StackCounts = stackCounts;
        CounterSlot = counterSlot;
    }
}
