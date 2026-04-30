namespace STS2.Agent.Sim;

/// <summary>
/// Indicates whether a snapshot taken from a single-player run should treat
/// the player as the "root" actor (the one our DFS searches for) or as a
/// teammate in a coop layout. Today both modes behave identically — a single
/// player with index 0 — but the parameter is plumbed through Snapshot()
/// from the start so coop support can drop in without touching call sites.
/// </summary>
internal enum CoopMode : byte
{
    /// <summary>Single-player run. <c>playerIdx</c> always 0.</summary>
    SoloRoot   = 0,

    /// <summary>Coop run, but we are searching from the perspective of the
    /// player at <c>playerIdx</c>; the other player is treated as part of
    /// the environment (their actions are not part of our action space).</summary>
    SoloAsCoop = 1,
}
