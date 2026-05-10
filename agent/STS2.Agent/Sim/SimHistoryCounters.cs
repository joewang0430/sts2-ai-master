using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// First history tranche: compact counters and flags derived from live
/// CombatHistory that are queried frequently by cards, powers, and relics.
///
/// This is intentionally not a full event log. It captures the audited
/// turn-scoped summary surface cheaply in the blob while leaving
/// identity-sensitive history for a later slice.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 32)]
internal struct SimHistoryCounters
{
    public ushort CardsStartedThisTurn;
    public ushort FirstSeriesCardsStartedThisTurn;
    public ushort AttacksStartedThisTurn;
    public ushort ZeroCostAttacksStartedThisTurn;
    public ushort CardsFinishedThisTurn;
    public ushort AttacksFinishedThisTurn;
    public ushort SkillsFinishedThisTurn;
    public ushort EnergySpentThisTurn;
    public ushort NonHandDrawsThisTurn;
    public ushort StatusDrawsThisTurn;
    public ushort CardsExhaustedThisTurn;
    public ushort CardsDiscardedThisTurn;
    public ushort PositiveStarsGainedThisTurn;
    public ushort BoundAfflictionsThisTurn;
    public ushort DoomAppliedByPlayerThisTurn;
    public byte Flags;
    public byte Reserved;

    public const byte FlagPlayerLostHpThisTurn = 1 << 0;
    public const byte FlagPlayerLostHpPreviousRound = 1 << 1;
}