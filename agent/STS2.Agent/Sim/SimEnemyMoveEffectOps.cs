using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Executes enemy <c>enemyIdx</c>'s currently-predicted non-attack move effects (the
/// Block/PowerApply/Heal payloads <see cref="SimMonsterMoveEffects"/> already computed and
/// <see cref="CombatNodeBlobSnapshot"/> already stored in <see cref="CombatNodeBlob.EnemyMoveEffects"/>)
/// against the blob — the enemy-side mirror of <see cref="SimCardEffects"/>'s player-side writers,
/// same as <see cref="SimEnemyAttackOps"/> is for Attack/DeathBlow intents.
///
/// Target is NOT stored per-effect (see <see cref="SimMonsterMoveEffects.HasNonDefaultTarget"/> for
/// why) — for <see cref="SimMoveEffectKind.PowerApply"/> it's derived per-slot from
/// <see cref="SimPowerCategoryRegistry.IsDebuff"/> (Debuff-category power → player, Buff-category →
/// self). This is NOT the same as reading the move's overall <see cref="SimIntent"/>: a move whose
/// FIRST game-side Intent is Attack (so <see cref="CombatNodeBlobSnapshot"/> classifies the whole
/// move's <see cref="SimIntent"/> as Attack) can still carry a second Debuff/Buff effect — e.g.
/// SludgeSpinner's OIL_SPRAY_MOVE is "Attack, also applies Weak to the player" — so targeting must be
/// decided per power, not once per move. Enemies flagged
/// <see cref="CombatNodeBlob.EnemyMoveEffectNonDefaultTarget"/> don't follow the debuff/buff rule at
/// all and throw rather than apply a guessed target. Summon effects route to
/// <see cref="SimEnemySummonOps"/>, which throws for the summon targets it doesn't cover yet.
/// </summary>
internal static class SimEnemyMoveEffectOps
{
    public static void ExecuteMoveEffects(CombatNodeBlob state, int enemyIdx)
    {
        int count = state.EnemyMoveEffectCount[enemyIdx];
        if (count == 0) return;

        if (state.EnemyMoveEffectNonDefaultTarget[enemyIdx] != 0)
        {
            throw new InvalidOperationException(
                $"SimEnemyMoveEffectOps: enemy {enemyIdx} has non-default-target move effects — " +
                "not yet executable (see SimMonsterMoveEffects.HasNonDefaultTarget).");
        }

        Span<SimMoveEffect> effects = state.EnemyMoveEffects.Slice(enemyIdx * CombatSimLayout.MoveEffectCap, CombatSimLayout.MoveEffectCap);
        for (int i = 0; i < count; i++)
        {
            SimMoveEffect effect = effects[i];
            switch ((SimMoveEffectKind)effect.Kind)
            {
                case SimMoveEffectKind.Block:
                    GainEnemyBlock(state, enemyIdx, effect.Amount);
                    break;
                case SimMoveEffectKind.PowerApply:
                    if (SimPowerCategoryRegistry.IsDebuff(effect.PowerType))
                    {
                        SimPowerOps.ApplyPlayerDelta(state, effect.PowerType, effect.Amount);
                        SimTurnPowerOps.SetSkipTickIfApplicable(state, effect.PowerType);
                    }
                    else
                        SimPowerOps.ApplyEnemyDelta(state, enemyIdx, effect.PowerType, effect.Amount);
                    break;
                case SimMoveEffectKind.Heal:
                    HealEnemy(state, enemyIdx, effect.Amount);
                    break;
                case SimMoveEffectKind.Summon:
                    SimEnemySummonOps.ExecuteSummonEffect(state, in effect, state.AscensionFlags);
                    break;
                case SimMoveEffectKind.CardInject:
                    InjectPlayerCard(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, effect.PowerType, effect.Amount);
                    break;
                case SimMoveEffectKind.CardInjectHand:
                    InjectPlayerCard(state, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap, effect.PowerType, effect.Amount);
                    break;
                case SimMoveEffectKind.CardInjectDrawRandom:
                    InjectPlayerCardRandom(state, state.DrawCards, ref state.DrawCount, CombatSimLayout.PileCap, effect.PowerType, effect.Amount);
                    break;
                case SimMoveEffectKind.CardInjectDiscardRandom:
                    InjectPlayerCardRandom(state, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap, effect.PowerType, effect.Amount);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"SimEnemyMoveEffectOps: enemy {enemyIdx} move effect slot {i} has unrecognized Kind={effect.Kind}.");
            }
        }
    }

    /// <summary>Mirrors SimCardEffects.GainPlayerBlock, just reading the ENEMY's own
    /// Dexterity/Frail/NoBlock instead of the player's — a monster gaining block off its own Defend
    /// move is subject to the same modifier pipeline as the player would be.</summary>
    private static void GainEnemyBlock(CombatNodeBlob state, int idx, int rawAmount)
    {
        int dexterity = SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.Dexterity, out short dexAmt) ? dexAmt : 0;
        bool frail = SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.Frail, out _);
        bool noBlock = SimPowerOps.TryGetEnemyAmount(state, idx, SimPowerType.NoBlock, out _);
        int amount = SimBlock.Compute(rawAmount, dexterity, frail, noBlock);
        if (amount <= 0) return;
        state.EnemyBlock[idx] = (ushort)Math.Min(999999999, state.EnemyBlock[idx] + amount);
    }

    private static void HealEnemy(CombatNodeBlob state, int idx, int amount)
    {
        if (amount <= 0) return;
        ushort maxHp = state.EnemyMaxHp[idx];
        state.EnemyHp[idx] = (ushort)Math.Min(maxHp, state.EnemyHp[idx] + amount);
    }

    /// <summary>Mints <paramref name="count"/> fresh copies of <paramref name="cardId"/> onto the
    /// end of <paramref name="pile"/> (Discard for <see cref="SimMoveEffectKind.CardInject"/>, Hand
    /// for <see cref="SimMoveEffectKind.CardInjectHand"/>) — the only shape covered so far (fixed
    /// count, fixed target pile, appended not randomly positioned; see
    /// <see cref="SimMonsterMoveEffects.WriteCardInject"/> / <c>WriteCardInjectHand</c>). Always the
    /// player: no enemy move that carries either kind targets itself.</summary>
    private static void InjectPlayerCard(CombatNodeBlob state, Span<SimCard> pile, ref ushort pileCount, int capacity, ushort cardId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            SimCardPileOps.AppendGenerated(state, pile, ref pileCount, capacity, cardId, upgraded: false);
        }
    }

    /// <summary>Mints <paramref name="count"/> fresh copies of <paramref name="cardId"/> into
    /// <paramref name="pile"/> at a random position each — mirrors <c>CardPilePosition.Random</c>'s
    /// <c>Rng.Shuffle.NextInt(pile.Count + 1)</c>, re-rolled against the CURRENT count before each
    /// card (matches the real game looping one <c>AddGeneratedCardToCombat</c> call per card, not
    /// computing one index for the whole batch).</summary>
    private static void InjectPlayerCardRandom(CombatNodeBlob state, Span<SimCard> pile, ref ushort pileCount, int capacity, ushort cardId, int count)
    {
        ref RandomState rng = ref state.Rng(SimRngSlot.Shuffle);
        for (int i = 0; i < count; i++)
        {
            int index = RandomStateOps.Next(ref rng, pileCount + 1);
            SimCardPileOps.InsertGeneratedAt(state, pile, ref pileCount, capacity, cardId, upgraded: false, index);
        }
    }
}
