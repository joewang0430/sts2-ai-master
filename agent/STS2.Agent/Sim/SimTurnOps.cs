using System;

namespace STS2.Agent.Sim;

/// <summary>Mirrors the game's <c>CombatSide</c> enum (None/Player/Enemy) 1:1 — same idiom as
/// <see cref="SimIntent"/>/<see cref="SimCardTargetType"/>. <see cref="CombatNodeBlob.CurrentSide"/>
/// is a raw <c>(byte)combat.CurrentSide</c> cast at snapshot time (see
/// <see cref="CombatNodeBlobSnapshot"/>), so these values must stay 1:1 with the game's own.</summary>
internal enum SimCombatSide : byte
{
    None = 0,
    Player = 1,
    Enemy = 2,
}

/// <summary>
/// Wires the already-built, previously-uncalled <see cref="SimCardEffects.PlayCard"/> and
/// <see cref="SimEnemyTurnOps.ExecuteEnemyTurn"/> into an actual player-turn ↔ enemy-turn cycle —
/// their first real caller. <see cref="SimTurnPowerOps"/> covers the highest-frequency
/// turn-boundary powers (Weak/Vulnerable/Frail decay, Poison, Ritual, Regen, Plating, Constrict);
/// everything else — 83 power types, see dev_docs/Turn_Lifecycle_Backlog.md for the full list —
/// still has no turn-boundary effect implemented. <see cref="AssertNoUnhandledTurnTriggerPowers"/>
/// throws rather than silently skip any of those — same as an unregistered monster already throws
/// elsewhere in this codebase for a gap that hasn't been filled in yet. That's accurate, not a bug:
/// the turn-cycle SHAPE is right, the remaining per-turn power-effect coverage just isn't built out
/// yet, and growing that registry is future work tracked in the same backlog doc.
///
/// <see cref="StartEnemyTurn"/> calls <see cref="SimEnemyTurnOps.ExecuteEnemyTurn"/> to perform
/// whatever move was already telegraphed, then <see cref="SimEnemyIntentAdvanceOps.AdvanceAll"/> to
/// advance each living enemy's move SM and re-resolve the intent fields describing what it will do
/// NEXT — see that file for the blob-only (no live <c>Creature</c>) translation layer this depends on.
/// </summary>
internal static class SimTurnOps
{
    private const int BaseDrawCount = 5;

    public static void StartPlayerTurn(CombatNodeBlob state)
    {
        AssertNoUnhandledTurnTriggerPowers(state);

        ResetBlockUnlessPrevented(state, isPlayer: true, enemyIdx: -1);
        ClearTurnStartResetFields(ref SimPowerOps.GetPlayerInternal(state));
        SimTurnPowerOps.AtOwnTurnStart(state, isPlayer: true, enemyIdx: -1);
        if (state.PlayerHp == 0) return;

        int drawCount = BaseDrawCount;
        if (SimPowerOps.TryGetPlayerAmount(state, SimPowerType.DrawCardsNextTurn, out short extraDraw) && extraDraw > 0)
        {
            drawCount += extraDraw;
            SimPowerOps.ApplyPlayerDelta(state, SimPowerType.DrawCardsNextTurn, -extraDraw);
        }

        state.Energy = state.MaxEnergy;
        SimCardPileOps.DrawCards(state, drawCount);
    }

    /// <summary>Hand disposition per card: Retain (persistent keyword OR the transient
    /// single-turn flag, cleared here as a one-shot exactly like <c>FlagExhaustOnNextPlay</c> is
    /// cleared on play) → stays in Hand; Ethereal → Exhaust; otherwise → Discard. Iterates hand
    /// back-to-front since removing/moving index <c>i</c> shifts every later index down.</summary>
    public static void EndPlayerTurn(CombatNodeBlob state)
    {
        AssertNoUnhandledTurnTriggerPowers(state);

        SimTurnPowerOps.AtOwnTurnEnd(state, isPlayer: true, enemyIdx: -1);
        ClearTurnEndResetFields(ref SimPowerOps.GetPlayerInternal(state));
        if (state.PlayerHp == 0) return;

        SimTurnPowerOps.RemoveOblivionAtPlayerTurnEnd(state);
        SimTurnPowerOps.RemoveFlameBarrierOnOpponentTurnEnd(state, endingIsPlayerTurn: true);

        for (int i = state.HandCount - 1; i >= 0; i--)
        {
            SimCard card = state.HandCards[i];
            bool retains = card.HasRetainKeyword || (card.Flags & SimCard.FlagShouldRetainThisTurn) != 0;
            if (retains)
            {
                if ((card.Flags & SimCard.FlagShouldRetainThisTurn) != 0)
                {
                    card.Flags &= unchecked((ushort)~SimCard.FlagShouldRetainThisTurn);
                    state.HandCards[i] = card;
                }
                continue;
            }

            if (card.HasEtherealKeyword)
                SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.ExhaustCards, ref state.ExhaustCount, CombatSimLayout.PileCap);
            else
                SimCardPileOps.MoveToEnd(state.HandCards, ref state.HandCount, i, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap);
        }

        state.CurrentSide = (byte)SimCombatSide.Enemy;
    }

    public static void StartEnemyTurn(CombatNodeBlob state)
    {
        AssertNoUnhandledTurnTriggerPowers(state);

        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            ResetBlockUnlessPrevented(state, isPlayer: false, enemyIdx: i);
            ClearTurnStartResetFields(ref SimPowerOps.GetEnemyInternal(state, i));
            SimTurnPowerOps.AtOwnTurnStart(state, isPlayer: false, enemyIdx: i);
        }

        SimEnemyTurnOps.ExecuteEnemyTurn(state);
        SimEnemyIntentAdvanceOps.AdvanceAll(state);
    }

    public static void EndEnemyTurn(CombatNodeBlob state)
    {
        AssertNoUnhandledTurnTriggerPowers(state);

        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            SimTurnPowerOps.AtOwnTurnEnd(state, isPlayer: false, enemyIdx: i);
            ClearTurnEndResetFields(ref SimPowerOps.GetEnemyInternal(state, i));
        }
        if (state.PlayerHp != 0)
        {
            SimTurnPowerOps.DecayWeakVulnerableFrailOncePerRound(state);
        }
        SimTurnPowerOps.RemoveFlameBarrierOnOpponentTurnEnd(state, endingIsPlayerTurn: false);

        state.CurrentSide = (byte)SimCombatSide.Player;
        state.Round++;
    }

    /// <summary>Mirrors <c>Creature.ClearBlock()</c>'s <c>Hook.ShouldClearBlock</c> gate: Barricade
    /// and Burrowed unconditionally prevent the clear; Blur also prevents it (and separately has its
    /// own un-implemented turn-boundary decay — see the turn-trigger guard). Does NOT check the
    /// SturdyClamp relic override (relic-driven gameplay effects aren't modeled as a queryable
    /// per-state flag in this codebase yet — a pre-existing, separate gap, not new to this function).</summary>
    private static void ResetBlockUnlessPrevented(CombatNodeBlob state, bool isPlayer, int enemyIdx)
    {
        bool prevented = isPlayer
            ? SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Barricade, out _)
                || SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Burrowed, out _)
                || SimPowerOps.TryGetPlayerAmount(state, SimPowerType.Blur, out _)
            : SimPowerOps.TryGetEnemyAmount(state, enemyIdx, SimPowerType.Barricade, out _)
                || SimPowerOps.TryGetEnemyAmount(state, enemyIdx, SimPowerType.Burrowed, out _)
                || SimPowerOps.TryGetEnemyAmount(state, enemyIdx, SimPowerType.Blur, out _);
        if (prevented) return;

        if (isPlayer) state.PlayerBlock = 0;
        else state.EnemyBlock[enemyIdx] = 0;
    }

    /// <summary>Fields that reset at the OWNER's own turn START: FeralPower (AfterSideTurnStart),
    /// VoidFormPower (BeforeSideTurnStart), SlothPower (BeforeSideTurnStart), HardenedShellPower
    /// (BeforeSideTurnStart, unconditional on side — resets regardless of ownership, but since we
    /// call this from both StartPlayerTurn and StartEnemyTurn for whoever's slot, the net effect is
    /// the same). None of these 4 counters are ever actually incremented anywhere in this codebase
    /// yet (no AfterCardPlayed/AfterAttackPlayed hook tracks them) — clearing an always-zero field is
    /// currently a no-op either way, but the reset POINT is still correct for whenever those
    /// increment hooks get built.</summary>
    private static void ClearTurnStartResetFields(ref SimPowerInternal internalState)
    {
        internalState.FeralZeroCostAttacks = 0;
        internalState.VoidFormCardsThisTurn = 0;
        internalState.SlothCardsThisTurn = 0;
        internalState.HardenedShellDamageThisTurn = 0;
    }

    /// <summary>Fields that reset at the OWNER's own turn END: JugglingPower (AfterSideTurnEnd) and
    /// TenderPower (AfterSideTurnEnd — real game also grants back +CardsPlayedThisTurn Str/Dex right
    /// before resetting, not modeled since the counter is never incremented, see
    /// <see cref="ClearTurnStartResetFields"/>'s note). Previously both were incorrectly cleared at
    /// turn START in the same pass as Feral/VoidForm/Sloth/HardenedShell — corrected here. Same
    /// "always zero right now" caveat applies.</summary>
    private static void ClearTurnEndResetFields(ref SimPowerInternal internalState)
    {
        internalState.JugglingAttacksThisTurn = 0;
        internalState.TenderCardsThisTurn = 0;
    }

    /// <summary>83 remaining power types have a turn-boundary hook in the real game with no
    /// implemented effect here (see dev_docs/Turn_Lifecycle_Backlog.md for the full inventory and how
    /// it was derived) — <see cref="SimTurnPowerOps"/> covers Weak/Vulnerable/Frail/Poison/Ritual/
    /// Regen/Plating/Constrict, everything else in the list still throws. Rather than silently proceed
    /// as if a turn without them passed correctly, this throws naming the first one found on the
    /// player, any living enemy, or Osty. <see cref="SimPowerType.DrawCardsNextTurn"/> is deliberately
    /// excluded — <see cref="StartPlayerTurn"/> already implements its actual effect.</summary>
    private static void AssertNoUnhandledTurnTriggerPowers(CombatNodeBlob state)
    {
        if (TryFindUnhandledPower(SimPowerOps.TryGetPlayerAmount, state, out int playerType))
        {
            throw new InvalidOperationException(
                $"SimTurnOps: player holds unimplemented turn-boundary power {playerType} — " +
                "see dev_docs/Turn_Lifecycle_Backlog.md.");
        }

        int enemyCount = state.EnemyCount;
        for (int i = 0; i < enemyCount; i++)
        {
            if (state.EnemyHp[i] == 0) continue;
            int idx = i;
            if (TryFindUnhandledPower((CombatNodeBlob s, int t, out short a) => SimPowerOps.TryGetEnemyAmount(s, idx, t, out a), state, out int enemyType))
            {
                throw new InvalidOperationException(
                    $"SimTurnOps: enemy {i} holds unimplemented turn-boundary power {enemyType} — " +
                    "see dev_docs/Turn_Lifecycle_Backlog.md.");
            }
        }

        if (TryFindUnhandledPower(SimPowerOps.TryGetOstyAmount, state, out int ostyType))
        {
            throw new InvalidOperationException(
                $"SimTurnOps: Osty holds unimplemented turn-boundary power {ostyType} — " +
                "see dev_docs/Turn_Lifecycle_Backlog.md.");
        }
    }

    private delegate bool TryGetAmount(CombatNodeBlob state, int type, out short amount);

    private static bool TryFindUnhandledPower(TryGetAmount tryGet, CombatNodeBlob state, out int foundType)
    {
        foreach (int type in UnhandledTurnTriggerPowerTypes)
        {
            if (tryGet(state, type, out _))
            {
                foundType = type;
                return true;
            }
        }
        foundType = 0;
        return false;
    }

    private static readonly int[] UnhandledTurnTriggerPowerTypes =
    {
        SimPowerType.Aggression, SimPowerType.BiasedCognition,
        SimPowerType.ChainsOfBinding,
        SimPowerType.ConsumingShadow,
        SimPowerType.Coolant, SimPowerType.CorrosiveWave, SimPowerType.Countdown, SimPowerType.Covered,
        SimPowerType.CrimsonMantle, SimPowerType.DarkEmbrace,
        SimPowerType.Disintegration, SimPowerType.Doom,
        SimPowerType.Entropy,
        SimPowerType.Furnace, SimPowerType.Gravity, SimPowerType.Hailstorm,
        SimPowerType.Hatch, SimPowerType.Hellraiser, SimPowerType.Inferno,
        SimPowerType.Intercept,
        SimPowerType.Loop, SimPowerType.MagicBomb, SimPowerType.Monologue,
        SimPowerType.Neurosurge,
        SimPowerType.NoxiousFumes, SimPowerType.Panache,
        SimPowerType.Rampart,
        SimPowerType.Ringing, SimPowerType.RollingBoulder,
        SimPowerType.Sandpit, SimPowerType.Shrink,
        SimPowerType.SicEm, SimPowerType.Skittish, SimPowerType.Slow,
        SimPowerType.Smoggy, SimPowerType.SummonNextTurn,
        SimPowerType.Tangled,
        SimPowerType.TheBomb, SimPowerType.ToolsOfTheTrade, SimPowerType.Tyranny,
    };
}
