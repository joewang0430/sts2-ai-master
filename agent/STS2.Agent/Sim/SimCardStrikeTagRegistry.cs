using System;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// <see cref="SimCardId"/> → "does this card have <see cref="CardTag.Strike"/>" static lookup.
/// Built lazily (double-checked lock) from <c>ModelDb.AllCards</c> on first use — same pattern as
/// <see cref="SimCardTypeRegistry"/>. Sole current consumer: PerfectedStrike's CalculatedDamage
/// multiplier ("count every Strike-tagged card the player owns"). Kept single-purpose (bool, not a
/// general tag-set table) since Strike is the only <see cref="CardTag"/> any registered card effect
/// currently needs to query.
/// </summary>
internal static class SimCardStrikeTagRegistry
{
    private static readonly object s_gate = new();
    private static bool[]? s_byCardId;

    public static bool Get(ushort cardId)
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
        var table = new bool[SimCardId.Count];
        foreach (CardModel card in ModelDb.AllCards)
        {
            if (!SimCardDb.TryGetId(card.GetType(), out ushort id))
            {
                throw new InvalidOperationException(
                    $"SimCardStrikeTagRegistry: card '{card.GetType().FullName}' is not registered " +
                    "in SimCardDb. Add typeof(...) → SimCardId.Xxx and bump SimCardId.Count.");
            }

            table[id] = card.Tags.Contains(CardTag.Strike);
        }

        s_byCardId = table;
    }
}
