using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Single entry point for "execute enemy <c>enemyIdx</c>'s currently-telegraphed move against the
/// blob" — routes to <see cref="SimEnemyAttackOps"/> or <see cref="SimEnemyMoveEffectOps"/> by
/// <see cref="SimIntent"/>, matching <see cref="SimCardEffects.PlayCard"/>'s role as the one place
/// the future search engine calls into rather than reaching into the per-kind writers directly.
///
/// Fail-loud coverage, not partial: every <see cref="SimIntent"/> value is handled here, either by
/// executing it, by an explicit correct no-op (Sleep/Stun — the move genuinely does nothing), or by
/// throwing for the kinds this codebase can't execute yet (CardDebuff, Hidden, Unknown) — never a
/// silent do-nothing for a kind that should have had an effect. StatusCard and Summon both route
/// through <see cref="SimEnemyMoveEffectOps"/> like Buff/Debuff/Defend/Heal, but only monsters/summon
/// targets registered in <see cref="SimMonsterMoveEffects"/> / <see cref="SimEnemySummonOps"/>
/// actually do anything — an unregistered StatusCard monster silently does nothing (same accepted
/// "tag-only" baseline as an unregistered Buff/Debuff monster, see that registry's own doc comment),
/// while an unregistered Summon TARGET throws loudly instead (see SimEnemySummonOps — a missing
/// MonsterStateTable can't be papered over the way a missing numeric amount can).
/// </summary>
internal static class SimEnemyTurnOps
{
    /// <summary>Executes every living enemy's currently-telegraphed move, in slot order, stopping
    /// early if the player dies partway through (no point simulating further attacks against a dead
    /// player — the combat outcome is already decided). This only resolves the moves ALREADY
    /// telegraphed on entry; it does not advance any enemy's move-state-machine to pick what they
    /// telegraph next turn — that's a separate, not-yet-built piece (needs RNG replication for
    /// probabilistic monster AI branches).
    ///
    /// <paramref name="state"/>'s <see cref="CombatNodeBlob.EnemyCount"/> can change mid-loop now
    /// that Escape is executable: <see cref="SimEnemyEscapeOps.RemoveEnemyAt"/> shifts every later
    /// enemy down by one slot, same as <c>CombatState.RemoveCreature</c> does live. <c>scheduled</c>
    /// is captured once, up front — it's how many enemies actually get a turn this round, matching
    /// the real game's fixed initiative order; a Summon that appends a new enemy mid-turn must NOT
    /// let that enemy also act this same turn. When the enemy at <c>i</c> escapes, EnemyCount drops
    /// and the next enemy slides into slot <c>i</c> — don't advance <c>i</c> in that case or it would
    /// be skipped.</summary>
    public static void ExecuteEnemyTurn(CombatNodeBlob state)
    {
        int scheduled = state.EnemyCount;
        int i = 0;
        int processed = 0;
        while (processed < scheduled && i < state.EnemyCount)
        {
            if (state.PlayerHp == 0) break;
            if (state.EnemyHp[i] == 0) { i++; processed++; continue; }

            int countBefore = state.EnemyCount;
            ExecuteMove(state, i);
            processed++;
            if (state.EnemyCount >= countBefore) i++;
        }
    }

    public static void ExecuteMove(CombatNodeBlob state, int enemyIdx)
    {
        var intent = (SimIntent)state.EnemyIntent[enemyIdx];
        switch (intent)
        {
            case SimIntent.Attack:
            case SimIntent.DeathBlow:
                SimEnemyAttackOps.ExecuteAttack(state, enemyIdx);
                // A move's SimIntent is whichever game-side Intent object came first in the
                // MoveState constructor (see CombatNodeBlobSnapshot.CaptureIntent) — Attack often
                // wins that race even when the same move ALSO buffs/debuffs/defends (e.g.
                // SludgeSpinner's OIL_SPRAY_MOVE: "Attack, also applies Weak to the player"). Any
                // such secondary effect still lives in EnemyMoveEffects regardless of which intent
                // got the SimIntent byte, so it must still run here.
                SimEnemyMoveEffectOps.ExecuteMoveEffects(state, enemyIdx);
                break;

            case SimIntent.Buff:
            case SimIntent.Debuff:
            case SimIntent.DebuffStrong:
            case SimIntent.Defend:
            case SimIntent.Heal:
            case SimIntent.StatusCard:
            case SimIntent.Summon:
                SimEnemyMoveEffectOps.ExecuteMoveEffects(state, enemyIdx);
                break;

            case SimIntent.Sleep:
            case SimIntent.Stun:
                // Correct no-op: a skip-turn marker means the move really does nothing.
                break;

            case SimIntent.Escape:
                SimEnemyEscapeOps.RemoveEnemyAt(state, enemyIdx);
                break;

            case SimIntent.CardDebuff:
                throw new InvalidOperationException(
                    $"SimEnemyTurnOps: enemy {enemyIdx} intends CardDebuff — despite the name, none of " +
                    "the 4 known CardDebuff-tagged moves (VineShambler/LivingFog/Queen/ThievingHopper) " +
                    "actually shuffle in a curse card; they apply a Power that afflicts existing cards, " +
                    "or steal one — a different mechanism from StatusCard's card-injection, and one the " +
                    "Sim blob has no card-affliction/steal machinery for yet.");

            case SimIntent.Hidden:
            case SimIntent.Unknown:
            default:
                throw new InvalidOperationException(
                    $"SimEnemyTurnOps: enemy {enemyIdx} has intent {intent} — no telegraphed move to execute.");
        }
    }
}
