using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Executes an enemy's currently-telegraphed Attack/DeathBlow intent against the player — the
/// enemy-side mirror of <see cref="SimCardEffects"/>'s player-side damage writers.
///
/// Deliberately recomputes damage fresh from <see cref="CombatNodeBlob.EnemyIntentRawDmg"/> (the
/// pre-modifier per-hit value) through <see cref="SimDamage.Compute"/> using whatever
/// Strength/Weak/Vulnerable/damage-cap state exists AT EXECUTION TIME, not
/// <see cref="CombatNodeBlob.EnemyIntentDmg"/> (the post-modifier value baked in at snapshot time
/// for live-UI verification — see that field's doc comment). A forward-simulating search calls this
/// after some number of hypothetical player turns may have changed either side's power state, so the
/// baked-in value would be stale.
/// </summary>
internal static class SimEnemyAttackOps
{
    /// <summary>Applies every hit of enemy <paramref name="enemyIdx"/>'s current Attack/DeathBlow
    /// intent to the player (Block absorption then HP loss, each hit computed independently — matches
    /// the real game applying multi-hit attacks one hit at a time, not a single combined total).
    /// Throws if the enemy's current intent isn't Attack/DeathBlow — callers must check
    /// <see cref="CombatNodeBlob.EnemyIntent"/> first, same fail-loud contract as
    /// <see cref="SimCardEffects.PlayCard"/> rejecting an unaffordable play.</summary>
    public static void ExecuteAttack(CombatNodeBlob state, int enemyIdx)
    {
        var intent = (SimIntent)state.EnemyIntent[enemyIdx];
        if (intent != SimIntent.Attack && intent != SimIntent.DeathBlow)
        {
            throw new InvalidOperationException(
                $"SimEnemyAttackOps.ExecuteAttack: enemy {enemyIdx} intent is {intent}, not Attack/DeathBlow.");
        }

        int raw = state.EnemyIntentRawDmg[enemyIdx];
        int hits = state.EnemyIntentHits[enemyIdx];
        int strength = SimPowerOps.TryGetEnemyAmount(state, enemyIdx, SimPowerType.Strength, out short strAmt) ? strAmt : 0;
        bool weak = SimPowerOps.TryGetEnemyAmount(state, enemyIdx, SimPowerType.Weak, out _);
        bool vulnerable = SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Vulnerable, out _);
        int? cap = PlayerDamageCap(state);

        for (int i = 0; i < hits; i++)
        {
            int total = SimDamage.Compute(raw, strength, vulnerable, weak, cap);
            ApplyComputedDamageToPlayer(state, total);
        }
    }

    /// <summary>The MINIMUM of every applicable ModifyDamageCap on the player (Intangible always
    /// caps at 1; HardToKill caps at its own stack Amount) — null if neither is present. Mirrors
    /// SimCardEffects.EnemyDamageCap exactly, just read from the player's own power set instead of
    /// an enemy's.</summary>
    private static int? PlayerDamageCap(CombatNodeBlob state)
    {
        int? cap = null;
        if (SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Intangible, out _))
            cap = 1;
        if (SimPowerOps.TryGetPlayerAmount(state, SimPowerType.HardToKill, out short hardToKillAmt))
            cap = cap.HasValue ? Math.Min(cap.Value, hardToKillAmt) : hardToKillAmt;
        return cap;
    }

    private static void ApplyComputedDamageToPlayer(CombatNodeBlob state, int total)
    {
        if (total <= 0) return;

        ushort block = state.PlayerBlock;
        int absorbed = Math.Min(block, total);
        state.PlayerBlock = (ushort)(block - absorbed);

        int leftover = total - absorbed;
        if (leftover <= 0) return;
        ushort hp = state.PlayerHp;
        state.PlayerHp = (ushort)Math.Max(0, hp - leftover);
    }
}
