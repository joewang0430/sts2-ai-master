using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Turn-boundary effects for the handful of highest-frequency powers (Weak/Vulnerable/Frail decay,
/// Poison, Ritual, Regen, Plating, Constrict) — called from <see cref="SimTurnOps"/> at each side's
/// turn start/end. Every other turn-triggered power still throws via
/// <see cref="SimTurnOps"/>'s guard; see dev_docs/Turn_Lifecycle_Backlog.md for the full remaining
/// list and per-power game_source citations for the ones implemented here.
/// </summary>
internal static class SimTurnPowerOps
{
    /// <summary>Call right after a Debuff-category power is freshly APPLIED to the player (not from
    /// this file's own decay decrements) — mirrors <c>PowerCmd.Apply</c>'s generic
    /// <c>SkipNextDurationTick=true</c> for a Player-side owner, narrowed to the 3 powers that
    /// actually consume it.</summary>
    public static void SetSkipTickIfApplicable(CombatNodeBlob state, ushort powerType)
    {
        byte flag = powerType switch
        {
            SimPowerType.Weak => SimPowerInternal.FlagSkipWeakTick,
            SimPowerType.Vulnerable => SimPowerInternal.FlagSkipVulnerableTick,
            SimPowerType.Frail => SimPowerInternal.FlagSkipFrailTick,
            _ => (byte)0,
        };
        if (flag == 0) return;
        SimPowerOps.GetPlayerInternal(state).Flags |= flag;
    }

    /// <summary>Fires once per round, at Enemy-turn-end (game_source: Weak/Vulnerable/Frail/Intangible
    /// all override <c>AfterSideTurnEnd</c> gated only on <c>side==CombatSide.Enemy</c>, NOT on which
    /// creature owns the stack — so player and every enemy's stacks tick down together at this one
    /// moment, not each on their own turn boundary). Intangible decays via a direct
    /// <c>PowerCmd.Decrement</c>, NOT <c>TickDownDuration</c> — no skip-tick grace period, unlike
    /// Weak/Vulnerable/Frail.</summary>
    public static void DecayWeakVulnerableFrailOncePerRound(CombatNodeBlob state)
    {
        DecayOne(state, isPlayer: true, enemyIdx: -1);

        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            DecayOne(state, isPlayer: false, enemyIdx: i);
        }
    }

    private static void DecayOne(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        ref SimPowerInternal internalState = ref (isPlayer
            ? ref SimPowerOps.GetPlayerInternal(state)
            : ref SimPowerOps.GetEnemyInternal(state, enemyIdx));

        TickDownIfPresent(state, isPlayer, enemyIdx, SimPowerType.Weak, ref internalState, SimPowerInternal.FlagSkipWeakTick);
        TickDownIfPresent(state, isPlayer, enemyIdx, SimPowerType.Vulnerable, ref internalState, SimPowerInternal.FlagSkipVulnerableTick);
        TickDownIfPresent(state, isPlayer, enemyIdx, SimPowerType.Frail, ref internalState, SimPowerInternal.FlagSkipFrailTick);

        if (GetAmount(state, isPlayer, enemyIdx, SimPowerType.Intangible) > 0)
        {
            ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Intangible, -1);
        }
        if (GetAmount(state, isPlayer, enemyIdx, SimPowerType.NoBlock) > 0)
        {
            ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.NoBlock, -1);
        }
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.DiamondDiadem);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Tainted);
        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.Colossus);
    }

    /// <summary>Fires at the OPPOSITE side's turn end (game_source: <c>Owner.Side != side</c>) — a
    /// player-held FlameBarrier decays when the ENEMY turn ends, an enemy-held one decays when the
    /// PLAYER turn ends. Call <paramref name="endingIsPlayerTurn"/>=true from EndPlayerTurn (removes
    /// every ENEMY's FlameBarrier) and =false from EndEnemyTurn (removes the PLAYER's).</summary>
    public static void RemoveFlameBarrierOnOpponentTurnEnd(CombatNodeBlob state, bool endingIsPlayerTurn)
    {
        if (endingIsPlayerTurn)
        {
            int count = state.EnemyCount;
            for (int i = 0; i < count; i++)
            {
                if (state.EnemyHp[i] == 0) continue;
                RemoveIfPresent(state, isPlayer: false, enemyIdx: i, SimPowerType.FlameBarrier);
            }
        }
        else
        {
            RemoveIfPresent(state, isPlayer: true, enemyIdx: -1, SimPowerType.FlameBarrier);
        }
    }

    /// <summary>Fires at Player-turn-end (game_source: OblivionPower's <c>AfterSideTurnEnd</c> is
    /// gated on <c>side==CombatSide.Player</c> unconditionally, not ownership), full removal for
    /// whoever holds it.</summary>
    public static void RemoveOblivionAtPlayerTurnEnd(CombatNodeBlob state)
    {
        RemoveIfPresent(state, isPlayer: true, enemyIdx: -1, SimPowerType.Oblivion);
        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            RemoveIfPresent(state, isPlayer: false, enemyIdx: i, SimPowerType.Oblivion);
        }
    }

    private static void RemoveIfPresent(CombatNodeBlob state, bool isPlayer, int enemyIdx, int powerType)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, powerType);
        if (amount <= 0) return;
        ApplyDelta(state, isPlayer, enemyIdx, powerType, -amount);
    }

    private static void TickDownIfPresent(CombatNodeBlob state, bool isPlayer, int enemyIdx, int powerType, ref SimPowerInternal internalState, byte skipFlag)
    {
        bool present = isPlayer
            ? SimPowerOps.TryGetPlayerAmount(state, powerType, out _)
            : SimPowerOps.TryGetEnemyAmount(state, enemyIdx, powerType, out _);
        if (!present) return;

        if ((internalState.Flags & skipFlag) != 0)
        {
            internalState.Flags &= unchecked((byte)~skipFlag);
            return;
        }

        if (isPlayer) SimPowerOps.ApplyPlayerDelta(state, powerType, -1);
        else SimPowerOps.ApplyEnemyDelta(state, enemyIdx, powerType, -1);
    }

    /// <summary>Call at the start of <paramref name="isPlayer"/>'s own turn — Poison damage tick
    /// (game_source: <c>AfterSideTurnStart</c>, gated on the owner's own side, damage-then-decrement,
    /// unblockable HP loss; deliberately ignores AccelerantPower's multi-trigger-per-turn effect —
    /// not modeled, TriggerCount is always treated as 1) and Plating's stack decrement (skipped on
    /// Round 1, singleplayer decrement-by-1 only — DynamicVars multiplayer scaling not modeled).</summary>
    public static void AtOwnTurnStart(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        TickPoison(state, isPlayer, enemyIdx);
        if (IsDead(state, isPlayer, enemyIdx)) return;

        TickPlatingDecrement(state, isPlayer, enemyIdx);
        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.Blur);
        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.Reflect);

        int prepTime = GetAmount(state, isPlayer, enemyIdx, SimPowerType.PrepTime);
        if (prepTime > 0) ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Vigor, prepTime);

        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.Clarity);

        int demonForm = GetAmount(state, isPlayer, enemyIdx, SimPowerType.DemonForm);
        if (demonForm > 0) ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Strength, demonForm);

        int wraithForm = GetAmount(state, isPlayer, enemyIdx, SimPowerType.WraithForm);
        if (wraithForm > 0) ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Dexterity, -wraithForm);

        int shadowStep = GetAmount(state, isPlayer, enemyIdx, SimPowerType.ShadowStep);
        if (shadowStep > 0)
        {
            ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.DoubleDamage, shadowStep);
            RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.ShadowStep);
        }
    }

    /// <summary>Call at the end of <paramref name="isPlayer"/>'s own turn — Regen heal-then-decay,
    /// Ritual self-Strength (skipped the turn an ENEMY first receives it —
    /// <see cref="SimPowerInternal.FlagRitualWasJustAppliedByEnemy"/> — no equivalent skip for a
    /// player-owned Ritual per game_source), Plating's Block gain (fixed at its own Amount, fires
    /// before other end-of-turn effects in the real game — order against Regen/Ritual/Constrict here
    /// isn't verified against game_source and may not exactly match), and Constrict's non-decaying
    /// blockable self-damage.</summary>
    public static void AtOwnTurnEnd(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        TickPlatingBlockGain(state, isPlayer, enemyIdx);
        TickRegen(state, isPlayer, enemyIdx);
        if (IsDead(state, isPlayer, enemyIdx)) return;

        TickRitual(state, isPlayer, enemyIdx);
        TickConstrict(state, isPlayer, enemyIdx);
        TickNoDrawExpiry(state, isPlayer, enemyIdx);
        if (IsDead(state, isPlayer, enemyIdx)) return;

        TickDemise(state, isPlayer, enemyIdx);
        if (IsDead(state, isPlayer, enemyIdx)) return;

        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.NoEnergyGain);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Rage);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Burst);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Duplication);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Rebound);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Strangle);
        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.DoubleDamage);
        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.RetainHand);
        TickEscapeArtist(state, isPlayer, enemyIdx);
        TickHighVoltage(state, isPlayer, enemyIdx);
        TickNemesis(state, isPlayer, enemyIdx);
        TickAsleep(state, isPlayer, enemyIdx);
        TickSlumber(state, isPlayer, enemyIdx);

        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.BorrowedTime);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.OneTwoPunch);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Shadowmeld);
        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.Debilitate);

        int territorial = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Territorial);
        if (territorial > 0) ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Strength, territorial);

        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Knockdown);
        RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Flanking);
        DecrementIfPresent(state, isPlayer, enemyIdx, SimPowerType.Conqueror);
    }

    /// <summary>Mirrors two separate hooks combined: <c>BeforeSideTurnEndVeryEarly</c> removes Plating
    /// if Asleep's Amount is already &lt;=1 (checked BEFORE the decrement), then <c>AfterSideTurnEnd</c>
    /// decrements Asleep itself. <c>WakeUpMove</c> (LagavulinMatriarch) is confirmed cosmetic-only —
    /// its only non-visual side effect (removing Plating) is already handled by the Plating-removal
    /// hook, so it's safe to skip entirely.</summary>
    private static void TickAsleep(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Asleep);
        if (amount <= 0) return;

        if (amount <= 1) RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Plating);
        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Asleep, -1);
    }

    /// <summary>Decrements Slumber; if it hits exactly 0, also removes Plating — replicating
    /// SlumberingBeetle.WakeUpMove's one non-cosmetic side effect (its only observable-state change
    /// beyond animation/SFX) without needing to model the wake animation itself.</summary>
    private static void TickSlumber(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Slumber);
        if (amount <= 0) return;

        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Slumber, -1);
        if (amount == 1) RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Plating);
    }

    private static void TickDemise(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Demise);
        if (amount <= 0) return;
        DealUnblockableDamage(state, isPlayer, enemyIdx, amount);
    }

    /// <summary>Decrements only while Amount &gt; 1 — the real game clamps at 1 and never removes
    /// itself via this hook (just a visual pulse at the floor).</summary>
    private static void TickEscapeArtist(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.EscapeArtist);
        if (amount <= 1) return;
        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.EscapeArtist, -1);
    }

    /// <summary>No decay — permanent Strength grant every own-turn-end while held.</summary>
    private static void TickHighVoltage(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.HighVoltage);
        if (amount <= 0) return;
        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Strength, amount);
    }

    /// <summary>Toggles <see cref="SimPowerInternal.FlagNemesisShouldApplyIntangible"/> and
    /// applies/removes Intangible in the same step — the flag has no other consumer in game_source,
    /// so this is pure wiring of state that was already tracked.</summary>
    private static void TickNemesis(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Nemesis);
        if (amount <= 0) return;

        ref SimPowerInternal internalState = ref (isPlayer
            ? ref SimPowerOps.GetPlayerInternal(state)
            : ref SimPowerOps.GetEnemyInternal(state, enemyIdx));
        bool shouldApply = (internalState.Flags & SimPowerInternal.FlagNemesisShouldApplyIntangible) == 0;
        if (shouldApply)
        {
            ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Intangible, 1);
            internalState.Flags |= SimPowerInternal.FlagNemesisShouldApplyIntangible;
        }
        else
        {
            RemoveIfPresent(state, isPlayer, enemyIdx, SimPowerType.Intangible);
            internalState.Flags &= unchecked((byte)~SimPowerInternal.FlagNemesisShouldApplyIntangible);
        }
    }

    private static void DecrementIfPresent(CombatNodeBlob state, bool isPlayer, int enemyIdx, int powerType)
    {
        if (GetAmount(state, isPlayer, enemyIdx, powerType) <= 0) return;
        ApplyDelta(state, isPlayer, enemyIdx, powerType, -1);
    }

    /// <summary>NoDraw is <c>PowerStackType.Single</c> (presence-only, no meaningful stack count) and
    /// fully removes itself — not decrements — at the owner's own turn end (game_source:
    /// <c>PowerCmd.Remove</c>, not <c>Decrement</c>). No <c>Remove</c> primitive exists in
    /// <see cref="SimPowerOps"/>, so this applies a delta of exactly <c>-currentAmount</c> to zero it
    /// out, which triggers the same self-removal <see cref="SimPowerOps.ApplyDelta"/> already does
    /// for any power hitting 0.</summary>
    private static void TickNoDrawExpiry(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.NoDraw);
        if (amount <= 0) return;
        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.NoDraw, -amount);
    }

    private static void TickPoison(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Poison);
        if (amount <= 0) return;

        DealUnblockableDamage(state, isPlayer, enemyIdx, amount);
        if (IsDead(state, isPlayer, enemyIdx)) return;

        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Poison, -1);
    }

    private static void TickRegen(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Regen);
        if (amount <= 0) return;

        HealCreature(state, isPlayer, enemyIdx, amount);
        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Regen, -1);
    }

    private static void TickRitual(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Ritual);
        if (amount <= 0) return;

        if (!isPlayer)
        {
            ref SimPowerInternal internalState = ref SimPowerOps.GetEnemyInternal(state, enemyIdx);
            if ((internalState.Flags & SimPowerInternal.FlagRitualWasJustAppliedByEnemy) != 0)
            {
                internalState.Flags &= unchecked((byte)~SimPowerInternal.FlagRitualWasJustAppliedByEnemy);
                return;
            }
        }

        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Strength, amount);
    }

    private static void TickConstrict(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Constrict);
        if (amount <= 0) return;

        DealBlockableSelfDamage(state, isPlayer, enemyIdx, amount);
    }

    private static void TickPlatingBlockGain(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Plating);
        if (amount <= 0) return;

        GainBlock(state, isPlayer, enemyIdx, amount);
    }

    private static void TickPlatingDecrement(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        if (state.Round <= 1) return;
        int amount = GetAmount(state, isPlayer, enemyIdx, SimPowerType.Plating);
        if (amount <= 0) return;

        ApplyDelta(state, isPlayer, enemyIdx, SimPowerType.Plating, -1);
    }

    // ── Small per-side primitives (mirror SimCardEffects/SimEnemyAttackOps's own equivalents,
    //    kept local since those are private to their own files) ───────────────────────────────────

    private static int GetAmount(CombatNodeBlob state, bool isPlayer, int enemyIdx, int powerType)
    {
        bool present = isPlayer
            ? SimPowerOps.TryGetPlayerAmount(state, powerType, out short amt)
            : SimPowerOps.TryGetEnemyAmount(state, enemyIdx, powerType, out amt);
        return present ? amt : 0;
    }

    private static void ApplyDelta(CombatNodeBlob state, bool isPlayer, int enemyIdx, int powerType, int delta)
    {
        if (isPlayer) SimPowerOps.ApplyPlayerDelta(state, powerType, delta);
        else SimPowerOps.ApplyEnemyDelta(state, enemyIdx, powerType, delta);
    }

    private static bool IsDead(CombatNodeBlob state, bool isPlayer, int enemyIdx)
        => isPlayer ? state.PlayerHp == 0 : state.EnemyHp[enemyIdx] == 0;

    /// <summary>No Block interaction, no Strength/Vulnerable/Weak — matches ValueProp.Unblockable|Unpowered.
    /// Does not check Intangible/HardToKill caps (no ValueProp gate for those in the real game either,
    /// but nothing exercised so far combines Poison with a capped target — same deferral
    /// <see cref="SimCardEffects"/>'s own unblockable-damage helper already documents).</summary>
    private static void DealUnblockableDamage(CombatNodeBlob state, bool isPlayer, int enemyIdx, int amount)
    {
        if (isPlayer)
        {
            state.PlayerHp = (ushort)Math.Max(0, state.PlayerHp - amount);
        }
        else
        {
            state.EnemyHp[enemyIdx] = (ushort)Math.Max(0, state.EnemyHp[enemyIdx] - amount);
        }
    }

    /// <summary>Constrict's self-damage IS subject to Block (confirmed: no Unblockable ValueProp in
    /// game_source), dealer==target==self — reuses ordinary Block-then-HP absorption.</summary>
    private static void DealBlockableSelfDamage(CombatNodeBlob state, bool isPlayer, int enemyIdx, int amount)
    {
        if (isPlayer)
        {
            int absorbed = Math.Min((int)state.PlayerBlock, amount);
            state.PlayerBlock = (ushort)(state.PlayerBlock - absorbed);
            state.PlayerHp = (ushort)Math.Max(0, state.PlayerHp - (amount - absorbed));
        }
        else
        {
            int absorbed = Math.Min((int)state.EnemyBlock[enemyIdx], amount);
            state.EnemyBlock[enemyIdx] = (ushort)(state.EnemyBlock[enemyIdx] - absorbed);
            state.EnemyHp[enemyIdx] = (ushort)Math.Max(0, state.EnemyHp[enemyIdx] - (amount - absorbed));
        }
    }

    private static void HealCreature(CombatNodeBlob state, bool isPlayer, int enemyIdx, int amount)
    {
        if (isPlayer)
        {
            state.PlayerHp = (ushort)Math.Min(state.PlayerMaxHp, state.PlayerHp + amount);
        }
        else
        {
            state.EnemyHp[enemyIdx] = (ushort)Math.Min(state.EnemyMaxHp[enemyIdx], state.EnemyHp[enemyIdx] + amount);
        }
    }

    private static void GainBlock(CombatNodeBlob state, bool isPlayer, int enemyIdx, int amount)
    {
        if (isPlayer)
        {
            state.PlayerBlock = (ushort)Math.Min(999999999, state.PlayerBlock + amount);
        }
        else
        {
            state.EnemyBlock[enemyIdx] = (ushort)Math.Min(999999999, state.EnemyBlock[enemyIdx] + amount);
        }
    }
}
