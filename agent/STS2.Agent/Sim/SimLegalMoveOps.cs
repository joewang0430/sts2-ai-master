using System.Collections.Generic;

namespace STS2.Agent.Sim;

/// <summary>One legal (card, target) action available this turn. <see cref="TargetEnemyIdx"/> is
/// -1 for any card whose <see cref="SimCardTargetType"/> doesn't need a chosen enemy (Self,
/// AllEnemies, RandomEnemy, AnyPlayer, AnyAlly, AllAllies, TargetedNoCreature, Osty) — the applier
/// resolves the real target(s) internally and ignores this field, same contract
/// <see cref="SimCardEffects.Apply"/> already documents.</summary>
internal readonly record struct SimLegalPlay(int HandIndex, int TargetEnemyIdx);

/// <summary>Enumerates every legal card play from the current hand — the search tree's branching
/// factor at a player-turn node. Deliberately does NOT enumerate potions (unmodeled in the Sim
/// layer, see dev_docs) or an "end turn" pseudo-action (that's a search-driver concern, not a
/// state-query one).</summary>
internal static class SimLegalMoveOps
{
    public static void GetLegalPlays(CombatNodeBlob state, List<SimLegalPlay> dst)
    {
        dst.Clear();

        int handCount = state.HandCount;
        for (int i = 0; i < handCount; i++)
        {
            SimCard card = state.HandCards[i];
            if (!SimCardEffects.IsRegistered(card.BaseCardId)) continue;
            if (SimCardEnergyOps.GetAmountToSpend(state, in card) > state.Energy) continue;
            if (!PassesCardSpecificGate(state, in card, handCount)) continue;

            switch (SimCardTargetTypeRegistry.Get(card.BaseCardId))
            {
                case SimCardTargetType.AnyEnemy:
                {
                    int enemyCount = state.EnemyCount;
                    for (int e = 0; e < enemyCount; e++)
                    {
                        if (state.EnemyHp[e] != 0) dst.Add(new SimLegalPlay(i, e));
                    }
                    break;
                }
                case SimCardTargetType.Self:
                case SimCardTargetType.AllEnemies:
                case SimCardTargetType.RandomEnemy:
                case SimCardTargetType.AnyPlayer:
                case SimCardTargetType.AnyAlly:
                case SimCardTargetType.AllAllies:
                case SimCardTargetType.TargetedNoCreature:
                case SimCardTargetType.Osty:
                    dst.Add(new SimLegalPlay(i, -1));
                    break;
                case SimCardTargetType.None:
                default:
                    throw new System.InvalidOperationException(
                        $"SimLegalMoveOps: card {card.BaseCardId} is registered playable but has no " +
                        "SimCardTargetType — SimCardTargetTypeRegistry should be exhaustive over all game cards.");
            }
        }
    }

    /// <summary>Card-specific extra playability preconditions beyond energy/target — currently just
    /// Clash's "every other card in hand must be an Attack" restriction (called out as unimplemented
    /// in <see cref="SimCardEffects"/>'s own doc comment). A single known case, handled inline
    /// rather than building an extensibility hook for one entry — grows by hand like every other
    /// partial registry in this codebase if a second case shows up.</summary>
    private static bool PassesCardSpecificGate(CombatNodeBlob state, in SimCard card, int handCount)
    {
        if (card.BaseCardId != SimCardId.Clash) return true;

        for (int j = 0; j < handCount; j++)
        {
            SimCard other = state.HandCards[j];
            if (other.InstanceId == card.InstanceId) continue;
            if (SimCardTypeRegistry.Get(other.BaseCardId) != SimCardType.Attack) return false;
        }
        return true;
    }
}
