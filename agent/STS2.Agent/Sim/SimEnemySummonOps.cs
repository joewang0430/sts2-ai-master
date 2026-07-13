using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Appends a brand-new enemy to the blob for a <see cref="SimMoveEffectKind.Summon"/> effect — the
/// Sim mirror of <c>CreatureCmd.Add&lt;T&gt;</c>. Unlike every other writer in this codebase, there is
/// no live game <c>Creature</c> to read from (nothing has actually spawned), so every field below is
/// hand-derived from the summoned monster's own source file instead of captured off a live object —
/// same discipline as <see cref="SimMonsterMoveEffects"/>, just applied to a creature's ENTIRE
/// starting snapshot instead of one move's payload.
///
/// Only covers summon targets whose starting HP is fixed (no ascension-independent random range) and
/// whose <c>GenerateMoveStateMachine</c> initial state is a plain <c>MoveState</c> with no branch to
/// resolve (SlotName/counter-gated initial picks would need machinery this class doesn't have) — see
/// dev_docs/Enemy_Intent_Payload_Backlog.md for which <see cref="SimSummonTargetId"/> values are
/// covered and which still throw.
///
/// Fail-loud on the one thing that can't be avoided even for a fully-covered target: the summoned
/// type's <see cref="MonsterStateTable"/> must already exist (built once this process from a REAL
/// live sighting of that monster — see <see cref="SimMonsterStateRegistry"/>'s own doc comment on
/// why it deliberately never constructs a monster model itself). If this process has never seen a
/// live instance of the target type, there's no table to resolve an initial state against, and this
/// throws rather than guessing one.
/// </summary>
internal static class SimEnemySummonOps
{
    public static void ExecuteSummonEffect(CombatNodeBlob state, in SimMoveEffect effect, ushort ascensionFlags)
    {
        ushort summonTargetId = effect.PowerType;
        int count = effect.Amount;
        for (int i = 0; i < count; i++)
        {
            AddEnemy(state, summonTargetId, ascensionFlags);
        }
    }

    private static void AddEnemy(CombatNodeBlob state, ushort summonTargetId, ushort ascensionFlags)
    {
        if (state.EnemyCount >= CombatSimLayout.EnemyCap)
        {
            throw new InvalidOperationException(
                $"SimEnemySummonOps: enemy array is already at EnemyCap ({CombatSimLayout.EnemyCap}) — can't summon another.");
        }

        int idx = state.EnemyCount;
        ClearSlot(state, idx);

        switch (summonTargetId)
        {
            case SimSummonTargetId.Parafright:
                SpawnParafright(state, idx, ascensionFlags);
                break;
            case SimSummonTargetId.EyeWithTeeth:
                SpawnEyeWithTeeth(state, idx);
                break;
            case SimSummonTargetId.GasBomb:
                SpawnGasBomb(state, idx, ascensionFlags);
                break;
            case SimSummonTargetId.ToughEgg:
                throw new InvalidOperationException(
                    "SimEnemySummonOps: ToughEgg's starting HP is a genuine random range " +
                    "(14-19 depending on ascension, not fixed) — needs an RNG roll this class " +
                    "doesn't perform yet, see dev_docs/Enemy_Intent_Payload_Backlog.md.");
            case SimSummonTargetId.TwoTailedRat:
                throw new InvalidOperationException(
                    "SimEnemySummonOps: TwoTailedRat's own initial-move resolution depends on " +
                    "TurnsUntilSummonable/CallForBackupCount, an already-documented gap — see " +
                    "dev_docs/Enemy_Intent_Payload_Backlog.md.");
            default:
                throw new InvalidOperationException(
                    $"SimEnemySummonOps: no spawn data registered for SimSummonTargetId {summonTargetId}.");
        }

        state.EnemyCount = (byte)(idx + 1);
    }

    /// <summary>Zeroes every parallel Enemy* array slot for <paramref name="idx"/> before use — a
    /// slot beyond the previous EnemyCount can hold stale data from an enemy that occupied it before
    /// an earlier Escape shifted things down (see <see cref="SimEnemyEscapeOps"/>, which deliberately
    /// doesn't clear the vacated tail slot). Power bitmaps especially must start at zero: a leftover
    /// garbage bit would make <see cref="SimPowerOps.ApplyEnemyDelta"/> think a power is already
    /// present.</summary>
    private static void ClearSlot(CombatNodeBlob state, int idx)
    {
        state.EnemyHp[idx] = 0;
        state.EnemyMaxHp[idx] = 0;
        state.EnemyBlock[idx] = 0;
        state.EnemyIntentDmg[idx] = 0;
        state.EnemyIntentRawDmg[idx] = 0;
        state.EnemyIntentHits[idx] = 0;
        state.EnemyIntent[idx] = 0;
        state.EnemyPowerBitmaps[idx] = default;
        state.EnemyPowerValues.Slice(idx * CombatSimLayout.PowerValueCap, CombatSimLayout.PowerValueCap).Clear();
        state.EnemyPowerInternal[idx] = default;
        state.EnemyMoveSM[idx] = default;
        state.EnemyMoveTableHandles[idx] = 0;
        state.EnemyMoveEffects.Slice(idx * CombatSimLayout.MoveEffectCap, CombatSimLayout.MoveEffectCap).Clear();
        state.EnemyMoveEffectCount[idx] = 0;
        state.EnemyMoveEffectNonDefaultTarget[idx] = 0;
        state.EnemyMonsterKind[idx] = SimMonsterKind.None;
    }

    /// <summary>Common setup once HP/intent/move-effects are decided: resolves and stores the
    /// MonsterStateTable handle + fresh <see cref="SimEnemyMoveSM"/> for <paramref name="initialMoveId"/>.
    /// Mirrors <c>MonsterMoveStateMachine</c>'s constructor: <c>CurrentStateIdx</c> = the initial
    /// state, <c>Flags</c> has <see cref="SimEnemyMoveSM.FlagPerformedFirstMove"/> UNSET (the real
    /// constructor sets <c>_performedFirstMove = false</c> — it only flips true after the move is
    /// actually performed, not at spawn), <c>History</c> empty (nothing performed yet), and
    /// <c>EverUsedBitset</c> has the initial state's bit set only if <c>ShouldAppearInLogs</c> is
    /// true — matching the constructor's <c>if (_currentState.ShouldAppearInLogs) StateLog.Add(...)</c>.</summary>
    private static void SetInitialMoveState<TMonster>(CombatNodeBlob state, int idx, string initialMoveId)
    {
        if (!SimMonsterStateRegistry.TryGetExistingHandle(typeof(TMonster), out ushort handle))
        {
            throw new InvalidOperationException(
                $"SimEnemySummonOps: no MonsterStateTable registered yet for {typeof(TMonster).Name} — " +
                "this process has never seen a live instance of it, so there's no table to resolve " +
                "an initial state against. Can't summon one until the real game has spawned it at " +
                "least once this session.");
        }

        MonsterStateTable table = SimMonsterStateRegistry.Resolve(handle)!;
        byte initialIdx = table.IdToIdx[initialMoveId];

        state.EnemyMoveTableHandles[idx] = handle;

        SimEnemyMoveSM sm = default;
        sm.CurrentStateIdx = initialIdx;
        sm.IllusionFollowUpIdx = SimEnemyMoveSM.NoFollowUp;
        if (table.ShouldAppearInLogs[initialIdx])
        {
            sm.EverUsedBitset = 1u << initialIdx;
        }
        state.EnemyMoveSM[idx] = sm;
    }

    // ── Parafright (TheObscura's ILLUSION_MOVE summon target): fixed 21 HP, IllusionPower x1,
    //    initial/only move SLAM_MOVE (SingleAttackIntent, self-looping). ─────────────────────────
    private static void SpawnParafright(CombatNodeBlob state, int idx, ushort ascensionFlags)
    {
        const ushort hp = 21;
        state.EnemyHp[idx] = hp;
        state.EnemyMaxHp[idx] = hp;
        state.EnemyMonsterKind[idx] = SimMonsterKind.Parafright;

        SimPowerOps.ApplyEnemyDelta(state, idx, SimPowerType.Illusion, 1);

        int slamDamage = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 17 : 16;
        state.EnemyIntent[idx] = (byte)SimIntent.Attack;
        state.EnemyIntentRawDmg[idx] = (ushort)slamDamage;
        state.EnemyIntentDmg[idx] = (ushort)slamDamage; // no Strength/Weak yet on a fresh spawn — raw == post-modifier
        state.EnemyIntentHits[idx] = 1;

        SetInitialMoveState<MegaCrit.Sts2.Core.Models.Monsters.Parafright>(state, idx, "SLAM_MOVE");
    }

    // ── EyeWithTeeth (Fogmog's ILLUSION_MOVE summon target, id "illusion"): fixed 6 HP,
    //    IllusionPower x1, initial/only move DISTRACT_MOVE (StatusCard: 3 Dazed to the player's
    //    Discard, self-looping — matches SimMonsterMoveEffects.WriteEyeWithTeeth). ────────────────
    private static void SpawnEyeWithTeeth(CombatNodeBlob state, int idx)
    {
        const ushort hp = 6;
        state.EnemyHp[idx] = hp;
        state.EnemyMaxHp[idx] = hp;
        state.EnemyMonsterKind[idx] = SimMonsterKind.EyeWithTeeth;

        SimPowerOps.ApplyEnemyDelta(state, idx, SimPowerType.Illusion, 1);

        state.EnemyIntent[idx] = (byte)SimIntent.StatusCard;
        Span<SimMoveEffect> effects = state.EnemyMoveEffects.Slice(idx * CombatSimLayout.MoveEffectCap, CombatSimLayout.MoveEffectCap);
        effects[0] = new SimMoveEffect { Kind = (byte)SimMoveEffectKind.CardInject, PowerType = SimCardId.Dazed, Amount = 3 };
        state.EnemyMoveEffectCount[idx] = 1;

        SetInitialMoveState<MegaCrit.Sts2.Core.Models.Monsters.EyeWithTeeth>(state, idx, "DISTRACT_MOVE");
    }

    // ── GasBomb (LivingFog's BLOAT_MOVE summon target): fixed 8/7 HP (DeadlyEnemies), MinionPower
    //    x1, initial/only move EXPLODE_MOVE (DeathBlowIntent, no FollowUpState at all — it kills
    //    itself on execution). ─────────────────────────────────────────────────────────────────────
    private static void SpawnGasBomb(CombatNodeBlob state, int idx, ushort ascensionFlags)
    {
        ushort hp = (ushort)(HasFlag(ascensionFlags, SimAscension.ToughEnemies) ? 8 : 7);
        state.EnemyHp[idx] = hp;
        state.EnemyMaxHp[idx] = hp;
        state.EnemyMonsterKind[idx] = SimMonsterKind.GasBomb;

        SimPowerOps.ApplyEnemyDelta(state, idx, SimPowerType.Minion, 1);

        int explodeDamage = HasFlag(ascensionFlags, SimAscension.DeadlyEnemies) ? 9 : 8;
        state.EnemyIntent[idx] = (byte)SimIntent.DeathBlow;
        state.EnemyIntentRawDmg[idx] = (ushort)explodeDamage;
        state.EnemyIntentDmg[idx] = (ushort)explodeDamage;
        state.EnemyIntentHits[idx] = 1;

        SetInitialMoveState<MegaCrit.Sts2.Core.Models.Monsters.GasBomb>(state, idx, "EXPLODE_MOVE");
    }

    private static bool HasFlag(ushort ascensionFlags, int flag) => (ascensionFlags & flag) != 0;
}
