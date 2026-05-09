using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Pure helper methods for the per-card energy-cost sidecar stored on
/// <see cref="CombatNodeBlob"/>. Keeps the variable-size modifier list logic
/// out of <see cref="SimCard"/> itself.
/// </summary>
internal static class SimCardEnergyOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBaseCost(CombatNodeBlob blob, in SimCard card)
        => blob.CardEnergyBaseCost[card.InstanceId];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCapturedXValue(CombatNodeBlob blob, in SimCard card)
        => blob.CardEnergyCapturedX[card.InstanceId];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetModifierCount(CombatNodeBlob blob, in SimCard card)
        => blob.CardEnergyModifierCount[card.InstanceId];

    /// <summary>
    /// Mirror of CardEnergyCost.GetWithModifiers(CostModifiers.Local).
    /// Global hook modifiers are deliberately excluded here; they live on the
    /// power/relic side and are not yet consumed by the sim execution path.
    /// </summary>
    public static int GetWithLocalModifiers(CombatNodeBlob blob, in SimCard card)
    {
        int cost = GetBaseCost(blob, card);
        if (cost < 0) return cost;
        if (card.HasEnergyCostX) return cost;

        int start = blob.CardEnergyModifierStart[card.InstanceId];
        int count = blob.CardEnergyModifierCount[card.InstanceId];
        for (int i = 0; i < count; i++)
            cost = Apply(blob.CardEnergyModifiers[start + i], cost);

        return cost < 0 ? 0 : cost;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetResolved(CombatNodeBlob blob, in SimCard card)
        => card.HasEnergyCostX ? GetCapturedXValue(blob, card) : GetWithLocalModifiers(blob, card);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetAmountToSpend(CombatNodeBlob blob, in SimCard card)
        => card.HasEnergyCostX ? blob.Energy : GetWithLocalModifiers(blob, card);

    public static bool AfterCardPlayedCleanup(CombatNodeBlob blob, in SimCard card)
        => RemoveModifiers(blob, card.InstanceId, LocalCostModifierExpiration.WhenPlayed);

    public static bool EndOfTurnCleanup(CombatNodeBlob blob, in SimCard card)
        => RemoveModifiers(blob, card.InstanceId, LocalCostModifierExpiration.EndOfTurn);

    private static bool RemoveModifiers(CombatNodeBlob blob, ushort instanceId, LocalCostModifierExpiration expiration)
    {
        int start = blob.CardEnergyModifierStart[instanceId];
        int count = blob.CardEnergyModifierCount[instanceId];
        if (count == 0) return false;

        int write = start;
        int end = start + count;
        for (int read = start; read < end; read++)
        {
            SimLocalCostModifier modifier = blob.CardEnergyModifiers[read];
            if ((modifier.Expiration & expiration) != 0)
                continue;

            blob.CardEnergyModifiers[write++] = modifier;
        }

        int kept = write - start;
        blob.CardEnergyModifierCount[instanceId] = (ushort)kept;
        return kept != count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Apply(SimLocalCostModifier modifier, int currentCost)
    {
        return modifier.Type switch
        {
            LocalCostType.Absolute => modifier.IsReduceOnly ? Min(currentCost, modifier.Amount) : modifier.Amount,
            LocalCostType.Relative => modifier.IsReduceOnly ? Min(currentCost, currentCost + modifier.Amount) : currentCost + modifier.Amount,
            _ => currentCost,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Min(int left, int right) => left < right ? left : right;
}