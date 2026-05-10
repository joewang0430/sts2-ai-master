using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Helpers for the ordered temporary star-cost sidecar stored on
/// <see cref="CombatNodeBlob"/>.
/// </summary>
internal static class SimCardStarCostOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCount(CombatNodeBlob blob, in SimCard card)
        => blob.CardTemporaryStarCostCount[card.InstanceId];

    public static int GetCurrent(CombatNodeBlob blob, in SimCard card)
    {
        int count = GetCount(blob, card);
        if (count == 0)
            return card.BaseStarCost;

        ushort start = blob.CardTemporaryStarCostStart[card.InstanceId];
        int cost = blob.CardTemporaryStarCosts[start + count - 1].Cost;
        return cost == 0 && card.BaseStarCost < 0 ? card.BaseStarCost : cost;
    }

    public static int GetThisCombat(CombatNodeBlob blob, in SimCard card)
    {
        ushort start = blob.CardTemporaryStarCostStart[card.InstanceId];
        int count = GetCount(blob, card);
        for (int i = 0; i < count; i++)
        {
            SimTemporaryStarCost cost = blob.CardTemporaryStarCosts[start + i];
            if (!cost.ClearsWhenTurnEnds && !cost.ClearsWhenCardIsPlayed)
                return cost.Cost;
        }

        return card.BaseStarCost;
    }

    public static bool AfterCardPlayedCleanup(CombatNodeBlob blob, in SimCard card)
        => RemoveCosts(blob, card.InstanceId, removeTurnEnd: false, removePlayed: true);

    public static bool EndOfTurnCleanup(CombatNodeBlob blob, in SimCard card)
        => RemoveCosts(blob, card.InstanceId, removeTurnEnd: true, removePlayed: false);

    private static bool RemoveCosts(CombatNodeBlob blob, ushort instanceId, bool removeTurnEnd, bool removePlayed)
    {
        int start = blob.CardTemporaryStarCostStart[instanceId];
        int count = blob.CardTemporaryStarCostCount[instanceId];
        if (count == 0)
            return false;

        int write = start;
        int end = start + count;
        for (int read = start; read < end; read++)
        {
            SimTemporaryStarCost cost = blob.CardTemporaryStarCosts[read];
            if ((removeTurnEnd && cost.ClearsWhenTurnEnds) || (removePlayed && cost.ClearsWhenCardIsPlayed))
                continue;

            blob.CardTemporaryStarCosts[write++] = cost;
        }

        int kept = write - start;
        blob.CardTemporaryStarCostCount[instanceId] = (ushort)kept;
        return kept != count;
    }
}