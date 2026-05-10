using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2.Agent.Sim;

internal static class SimHistoryCounterOps
{
    public static void CaptureLive(CombatState combat, Player player, ref SimHistoryCounters dst)
    {
        dst = default;

        Creature actor = player.Creature;
        foreach (var entry in CombatManager.Instance.History.Entries)
        {
            switch (entry)
            {
                case EnergySpentEntry energy when energy.Actor == actor && energy.HappenedThisTurn(combat):
                    dst.EnergySpentThisTurn = ClampAdd(dst.EnergySpentThisTurn, energy.Amount);
                    break;

                case CardPlayStartedEntry started when started.Actor == actor && started.HappenedThisTurn(combat):
                    dst.CardsStartedThisTurn = ClampInc(dst.CardsStartedThisTurn);
                    if (started.CardPlay.IsFirstInSeries)
                        dst.FirstSeriesCardsStartedThisTurn = ClampInc(dst.FirstSeriesCardsStartedThisTurn);

                    if (started.CardPlay.Card.Type == CardType.Attack)
                    {
                        dst.AttacksStartedThisTurn = ClampInc(dst.AttacksStartedThisTurn);
                        if (started.CardPlay.Resources.EnergyValue == 0)
                            dst.ZeroCostAttacksStartedThisTurn = ClampInc(dst.ZeroCostAttacksStartedThisTurn);
                    }
                    break;

                case CardPlayFinishedEntry finished when finished.Actor == actor && finished.HappenedThisTurn(combat):
                    dst.CardsFinishedThisTurn = ClampInc(dst.CardsFinishedThisTurn);
                    if (finished.CardPlay.Card.Type == CardType.Attack)
                        dst.AttacksFinishedThisTurn = ClampInc(dst.AttacksFinishedThisTurn);
                    else if (finished.CardPlay.Card.Type == CardType.Skill)
                        dst.SkillsFinishedThisTurn = ClampInc(dst.SkillsFinishedThisTurn);
                    break;

                case CardDrawnEntry drawn when drawn.Actor == actor && drawn.HappenedThisTurn(combat):
                    if (!drawn.FromHandDraw)
                        dst.NonHandDrawsThisTurn = ClampInc(dst.NonHandDrawsThisTurn);
                    if (drawn.Card.Type == CardType.Status)
                        dst.StatusDrawsThisTurn = ClampInc(dst.StatusDrawsThisTurn);
                    break;

                case CardExhaustedEntry exhausted when exhausted.Card.Owner == player && exhausted.HappenedThisTurn(combat):
                    dst.CardsExhaustedThisTurn = ClampInc(dst.CardsExhaustedThisTurn);
                    break;

                case CardDiscardedEntry discarded when discarded.Card.Owner == player && discarded.HappenedThisTurn(combat):
                    dst.CardsDiscardedThisTurn = ClampInc(dst.CardsDiscardedThisTurn);
                    break;

                case StarsModifiedEntry stars when stars.Actor == actor && stars.HappenedThisTurn(combat) && stars.Amount > 0:
                    dst.PositiveStarsGainedThisTurn = ClampAdd(dst.PositiveStarsGainedThisTurn, stars.Amount);
                    break;

                case CardAfflictedEntry afflicted when afflicted.Actor == actor && afflicted.HappenedThisTurn(combat) && afflicted.Affliction is Bound:
                    dst.BoundAfflictionsThisTurn = ClampInc(dst.BoundAfflictionsThisTurn);
                    break;

                case PowerReceivedEntry power when power.Applier == actor && power.HappenedThisTurn(combat) && power.Power is DoomPower:
                    dst.DoomAppliedByPlayerThisTurn = ClampInc(dst.DoomAppliedByPlayerThisTurn);
                    break;

                case DamageReceivedEntry damage when damage.Receiver == actor:
                    if (damage.HappenedThisTurn(combat) && damage.Result.UnblockedDamage > 0)
                        dst.Flags |= SimHistoryCounters.FlagPlayerLostHpThisTurn;

                    if (!damage.Result.WasFullyBlocked && damage.RoundNumber + 1 == combat.RoundNumber)
                        dst.Flags |= SimHistoryCounters.FlagPlayerLostHpPreviousRound;
                    break;
            }
        }
    }

    private static ushort ClampInc(ushort value)
        => value == ushort.MaxValue ? ushort.MaxValue : (ushort)(value + 1);

    private static ushort ClampAdd(ushort value, int delta)
    {
        if (delta <= 0)
            return value;

        int sum = value + delta;
        return sum >= ushort.MaxValue ? ushort.MaxValue : (ushort)sum;
    }
}