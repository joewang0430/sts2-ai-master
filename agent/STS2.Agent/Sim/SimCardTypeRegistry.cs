using System;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// <see cref="SimCardId"/> → <see cref="SimCardType"/> static lookup table. Built lazily
/// (double-checked lock) from <c>ModelDb.AllCards</c> on first use — same pattern, same reasoning,
/// as <see cref="SimCardTargetTypeRegistry"/> (see that file's doc comment).
/// </summary>
internal static class SimCardTypeRegistry
{
    private static readonly object s_gate = new();
    private static SimCardType[]? s_byCardId;

    public static SimCardType Get(ushort cardId)
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
        var table = new SimCardType[SimCardId.Count];
        foreach (CardModel card in ModelDb.AllCards)
        {
            if (!SimCardDb.TryGetId(card.GetType(), out ushort id))
            {
                throw new InvalidOperationException(
                    $"SimCardTypeRegistry: card '{card.GetType().FullName}' is not registered " +
                    "in SimCardDb. Add typeof(...) → SimCardId.Xxx and bump SimCardId.Count.");
            }

            table[id] = (SimCardType)(byte)card.Type;
        }

        s_byCardId = table;
    }
}
