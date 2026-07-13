namespace STS2.Agent.Sim;

/// <summary>Terminal state of a combat blob — the search engine's stop condition.</summary>
internal enum SimCombatOutcome : byte
{
    InProgress = 0,
    PlayerWon = 1,
    PlayerLost = 2,
}

internal static class SimCombatOutcomeOps
{
    /// <summary>Player death takes priority over a simultaneous last-enemy kill (mirrors the real
    /// game: a DeathBlow/Attack that drops the player to 0 ends the fight as a loss even if that
    /// same hit was also lethal to the last enemy — combat resolution doesn't race the two).</summary>
    public static SimCombatOutcome GetOutcome(CombatNodeBlob state)
    {
        if (state.PlayerHp == 0) return SimCombatOutcome.PlayerLost;

        int count = state.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            if (state.EnemyHp[i] != 0) return SimCombatOutcome.InProgress;
        }
        return SimCombatOutcome.PlayerWon;
    }

    public static bool IsCombatOver(CombatNodeBlob state) => GetOutcome(state) != SimCombatOutcome.InProgress;
}
