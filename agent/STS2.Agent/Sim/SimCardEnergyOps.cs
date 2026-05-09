using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Pure helper methods for the per-card energy-cost sidecar stored on
/// <see cref="SimCombatState"/>. Keeps the variable-size modifier list logic
/// out of <see cref="SimCard"/> itself.
/// </summary>
internal static class SimCardEnergyOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBaseCost(SimCombatState state, in SimCard card)
        => state.CardEnergyBaseCost[card.InstanceId];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCapturedXValue(SimCombatState state, in SimCard card)
        => state.CardEnergyCapturedX[card.InstanceId];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetModifierCount(SimCombatState state, in SimCard card)
        => state.CardEnergyModifierCount[card.InstanceId];

    /// <summary>
    /// Mirror of CardEnergyCost.GetWithModifiers(CostModifiers.Local).
    /// Global hook modifiers are deliberately excluded here; they live on the
    /// power/relic side and are not yet consumed by the sim execution path.
    /// </summary>
    public static int GetWithLocalModifiers(SimCombatState state, in SimCard card)
    {
        int cost = GetBaseCost(state, card);
        if (cost < 0) return cost;
        if (card.HasEnergyCostX) return cost;

        int start = state.CardEnergyModifierStart[card.InstanceId];
        int count = state.CardEnergyModifierCount[card.InstanceId];
        for (int i = 0; i < count; i++)
            cost = Apply(state.CardEnergyModifiers[start + i], cost);

        return cost < 0 ? 0 : cost;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetResolved(SimCombatState state, in SimCard card)
        => card.HasEnergyCostX ? GetCapturedXValue(state, card) : GetWithLocalModifiers(state, card);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetAmountToSpend(SimCombatState state, in SimCard card)
        => card.HasEnergyCostX ? state.Energy : GetWithLocalModifiers(state, card);

    public static bool AfterCardPlayedCleanup(SimCombatState state, in SimCard card)
        => RemoveModifiers(state, card.InstanceId, LocalCostModifierExpiration.WhenPlayed);

    public static bool EndOfTurnCleanup(SimCombatState state, in SimCard card)
        => RemoveModifiers(state, card.InstanceId, LocalCostModifierExpiration.EndOfTurn);

    private static bool RemoveModifiers(SimCombatState state, ushort instanceId, LocalCostModifierExpiration expiration)
    {
        int start = state.CardEnergyModifierStart[instanceId];
        int count = state.CardEnergyModifierCount[instanceId];
        if (count == 0) return false;

        int write = start;
        int end = start + count;
        for (int read = start; read < end; read++)
        {
            SimLocalCostModifier modifier = state.CardEnergyModifiers[read];
            if ((modifier.Expiration & expiration) != 0)
                continue;

            state.CardEnergyModifiers[write++] = modifier;
        }

        int kept = write - start;
        state.CardEnergyModifierCount[instanceId] = (ushort)kept;
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