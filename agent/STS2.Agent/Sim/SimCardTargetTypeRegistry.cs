using System;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// <see cref="SimCardId"/> → <see cref="SimCardTargetType"/> static lookup table. Built lazily
/// (double-checked lock) from <c>ModelDb.AllCards</c> on first use, same pattern as
/// <see cref="SimPotionRegistry"/> — <c>ModelDb</c> is not populated when mod initializers run.
///
/// Purely static game-content data (which target type a card TYPE has never changes), so unlike
/// everything under the "Sim Blob" umbrella, this table has nothing to do with combat state or
/// snapshotting: it is built once for the process lifetime and queried directly by
/// <see cref="SimCardId"/>, no <see cref="CombatNodeBlob"/> involvement at all.
/// </summary>
internal static class SimCardTargetTypeRegistry
{
    private static readonly object s_gate = new();
    private static SimCardTargetType[]? s_byCardId;

    public static SimCardTargetType Get(ushort cardId)
    {
        EnsureBuilt();
        return s_byCardId![cardId];
    }

    private static void EnsureBuilt()
    {
        if (s_byCardId is not null) return;
        lock (s_gate)
        {
            if (s_byCardId is not null) return;
            Build();
        }
    }

    private static void Build()
    {
        var table = new SimCardTargetType[SimCardId.Count];
        foreach (CardModel card in ModelDb.AllCards)
        {
            // Fail loud rather than silently defaulting to None: an unregistered card here means
            // SimCardDb itself is out of date (missed by the card-count canary at compile time
            // only if the game removed a card; a newly ADDED card would compile fine but silently
            // resolve to index 0 here without this check).
            if (!SimCardDb.TryGetId(card.GetType(), out ushort id))
            {
                throw new InvalidOperationException(
                    $"SimCardTargetTypeRegistry: card '{card.GetType().FullName}' is not registered " +
                    "in SimCardDb. Add typeof(...) → SimCardId.Xxx and bump SimCardId.Count.");
            }

            table[id] = (SimCardTargetType)(byte)card.TargetType;
        }

        s_byCardId = table;
    }
}
