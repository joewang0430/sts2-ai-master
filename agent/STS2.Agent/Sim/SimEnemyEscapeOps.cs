using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Removes enemy <c>index</c> from the blob — the Sim mirror of <c>CreatureCmd.Escape</c> /
/// <c>CombatState.RemoveCreature</c>. Both of those go through <c>List&lt;Creature&gt;.Remove</c>,
/// which shifts every later element down by one — slot indices are NOT stable across an Escape,
/// exactly like <see cref="SimCardPileOps.RemoveAt"/> for card piles. Every parallel <c>Enemy*</c>
/// array in <see cref="CombatNodeBlob"/> (there are 13 scalar/single-struct ones plus 2 that carry
/// a per-enemy sub-block — <see cref="CombatNodeBlob.EnemyPowerValues"/>,
/// <see cref="CombatNodeBlob.EnemyMoveEffects"/>) has to shift in lockstep, or a later enemy's data
/// would silently end up under an earlier enemy's index.
/// </summary>
internal static class SimEnemyEscapeOps
{
    public static void RemoveEnemyAt(CombatNodeBlob state, int index)
    {
        int count = state.EnemyCount;
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index),
                $"SimEnemyEscapeOps.RemoveEnemyAt: index {index} out of range (count={count}).");
        }

        ShiftDown(state.EnemyHp, 1, index, count);
        ShiftDown(state.EnemyMaxHp, 1, index, count);
        ShiftDown(state.EnemyBlock, 1, index, count);
        ShiftDown(state.EnemyIntentDmg, 1, index, count);
        ShiftDown(state.EnemyIntentRawDmg, 1, index, count);
        ShiftDown(state.EnemyIntentHits, 1, index, count);
        ShiftDown(state.EnemyIntent, 1, index, count);
        ShiftDown(state.EnemyPowerBitmaps, 1, index, count);
        ShiftDown(state.EnemyPowerValues, CombatSimLayout.PowerValueCap, index, count);
        ShiftDown(state.EnemyPowerInternal, 1, index, count);
        ShiftDown(state.EnemyMoveSM, 1, index, count);
        ShiftDown(state.EnemyMoveTableHandles, 1, index, count);
        ShiftDown(state.EnemyMoveEffects, CombatSimLayout.MoveEffectCap, index, count);
        ShiftDown(state.EnemyMoveEffectCount, 1, index, count);
        ShiftDown(state.EnemyMoveEffectNonDefaultTarget, 1, index, count);

        state.EnemyCount = (byte)(count - 1);
    }

    /// <summary>Shifts every <paramref name="perEnemy"/>-sized block after <paramref name="fromIndex"/>
    /// down by one block position. <paramref name="perEnemy"/> is 1 for plain per-enemy
    /// scalars/structs, or the sub-slot count (e.g. <see cref="CombatSimLayout.MoveEffectCap"/>) for
    /// arrays that pack several values per enemy.</summary>
    private static void ShiftDown<T>(Span<T> flat, int perEnemy, int fromIndex, int count) where T : struct
    {
        for (int i = fromIndex; i < count - 1; i++)
        {
            flat.Slice((i + 1) * perEnemy, perEnemy).CopyTo(flat.Slice(i * perEnemy, perEnemy));
        }
    }
}
