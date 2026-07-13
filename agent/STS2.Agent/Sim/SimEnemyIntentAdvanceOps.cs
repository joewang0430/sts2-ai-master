using System;

namespace STS2.Agent.Sim;

/// <summary>
/// After an enemy's currently-telegraphed move executes, advances its <see cref="SimEnemyMoveSM"/>
/// to the move it will telegraph NEXT and re-populates every blob field that describes an intent
/// (<see cref="CombatNodeBlob.EnemyIntent"/>/<c>EnemyIntentRawDmg</c>/<c>EnemyIntentHits</c>/
/// <c>EnemyMoveEffects</c>/<c>EnemyMoveEffectCount</c>) to match. This is what makes multi-turn
/// forward search possible at all — without it, an enemy would just keep re-performing whatever
/// move it had telegraphed at snapshot time forever, since nothing else in this codebase computes
/// "what does this monster do after the move it's currently showing".
///
/// Three registries have to agree for a given enemy to resolve cleanly, none built by this file:
///   <see cref="SimMonsterStateRegistry"/>  — MonsterStateTable.MonsterType + IntentClass per state.
///   <see cref="SimMonsterMoveAdvance"/>    — hand-copied state-graph transition per monster Type.
///   <see cref="SimMonsterMoveEffects"/>    — hand-copied Block/Buff/Debuff/Heal/Summon/CardInject payload.
///   <see cref="SimMonsterAttackDb"/>       — hand-copied Attack/DeathBlow raw damage + hit count.
/// The last two are INTENTIONALLY partial registries (unregistered = 0 effects, harmless). The first
/// two are NOT: an unregistered monsterType, or an Attack/DeathBlow state with no
/// <see cref="SimMonsterAttackDb"/> entry, throws here rather than leave stale intent fields sitting
/// next to an already-advanced CurrentStateIdx — that combination (SM says "I'm about to do X", intent
/// fields still describe the previous move) is exactly the plausible-wrong result this codebase's
/// fail-loud philosophy exists to prevent.
/// </summary>
internal static class SimEnemyIntentAdvanceOps
{
    /// <summary>Advances every living enemy's move SM by one step and re-resolves its intent fields.
    /// Call once per enemy turn, after <see cref="SimEnemyTurnOps.ExecuteEnemyTurn"/> has performed
    /// whatever move was telegraphed going in.</summary>
    public static void AdvanceAll(CombatNodeBlob state)
    {
        ushort ascensionFlags = state.AscensionFlags;
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            AdvanceOne(state, i, ascensionFlags);
        }
    }

    private static void AdvanceOne(CombatNodeBlob state, int enemyIdx, ushort ascensionFlags)
    {
        ushort handle = state.EnemyMoveTableHandles[enemyIdx];
        MonsterStateTable? table = SimMonsterStateRegistry.Resolve(handle);
        if (table == null)
        {
            throw new InvalidOperationException(
                $"SimEnemyIntentAdvanceOps: enemy {enemyIdx} has no MonsterStateTable handle (handle={handle}).");
        }

        ref SimEnemyMoveSM sm = ref state.EnemyMoveSM[enemyIdx];
        ref RandomState rng = ref state.Rng(SimRngSlot.MonsterAi);
        if (!SimMonsterMoveAdvance.TryAdvance(table.MonsterType, state, enemyIdx, ref sm, table, ref rng, ascensionFlags))
        {
            throw new InvalidOperationException(
                $"SimEnemyIntentAdvanceOps: monster '{table.MonsterType.FullName}' has no registered " +
                "SimMonsterMoveAdvance entry — see dev_docs/Enemy_Intent_Payload_Backlog.md.");
        }

        byte stateIdx = sm.CurrentStateIdx;
        string stateId = table.StateIds[stateIdx];
        SimIntent intent = table.IntentClass[stateIdx];
        state.EnemyIntent[enemyIdx] = (byte)intent;

        if (intent == SimIntent.Attack || intent == SimIntent.DeathBlow)
        {
            if (!SimMonsterAttackDb.TryResolve(table.MonsterType, stateId, ascensionFlags, out SimMonsterAttackDb.Resolved resolved))
            {
                throw new InvalidOperationException(
                    $"SimEnemyIntentAdvanceOps: monster '{table.MonsterType.FullName}' state '{stateId}' is " +
                    $"{intent} but has no registered SimMonsterAttackDb entry — see dev_docs/Enemy_Intent_Payload_Backlog.md.");
            }

            state.EnemyIntentRawDmg[enemyIdx] = resolved.RawDamage;
            // No live Strength/Weak/Vulnerable to bake in for a not-yet-live future state — matches
            // the SimEnemySummonOps fresh-spawn precedent (raw == post-modifier until execution time,
            // when SimEnemyAttackOps recomputes fresh from EnemyIntentRawDmg anyway).
            state.EnemyIntentDmg[enemyIdx] = resolved.RawDamage;
            state.EnemyIntentHits[enemyIdx] = resolved.Hits;
        }
        else
        {
            state.EnemyIntentRawDmg[enemyIdx] = 0;
            state.EnemyIntentDmg[enemyIdx] = 0;
            state.EnemyIntentHits[enemyIdx] = 0;
        }

        Span<SimMoveEffect> effectSlots = state.EnemyMoveEffects.Slice(enemyIdx * CombatSimLayout.MoveEffectCap, CombatSimLayout.MoveEffectCap);
        int effectCount = SimMonsterMoveEffects.Write(table.MonsterType, stateId, ascensionFlags, effectSlots);
        state.EnemyMoveEffectCount[enemyIdx] = (byte)effectCount;
        state.EnemyMoveEffectNonDefaultTarget[enemyIdx] = (byte)(SimMonsterMoveEffects.HasNonDefaultTarget(table.MonsterType) ? 1 : 0);
    }
}
