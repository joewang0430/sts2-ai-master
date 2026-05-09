using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Per-enemy snapshot of <c>MonsterMoveStateMachine</c>'s mutable state — just
/// enough to deterministically replay <c>RollMove</c> inside the DFS hot path
/// without ever touching the live game object graph.
///
/// Layout (Pack=1, total 25 bytes; 6 × 25 = 150 B per <see cref="SimCombatState"/> clone):
/// <code>
/// offset  field                      size  notes
///  0      CurrentStateIdx            1     index into MonsterStateTable.StateIds
///  1      Flags                      1     FlagPerformedFirstMove | FlagHasIllusionFollowUp
///  2      HistoryCount               1     number of valid entries in History (0..16)
///  3      HistoryHead                1     ring-buffer write position (0..15)
///  4      EverUsedBitset             4     bit i = state i ever appeared in StateLog
///  8      IllusionFollowUpIdx        1     IllusionPower.FollowUpStateId index, 0xFF = none
///  9      History[16]                16    ring buffer of state indices, oldest at HistoryHead-Count
/// </code>
///
/// <para>Why these particular fields (and nothing else):</para>
/// <list type="bullet">
///   <item><b>CurrentStateIdx</b> + <b>Flags.PerformedFirstMove</b> mirror the
///     two private fields read by <c>FindNextMoveState</c>'s entry guards.</item>
///   <item><b>EverUsedBitset</b> answers <c>MoveRepeatType.UseOnlyOnce</c> —
///     <c>StateLog.Contains(item)</c> in <c>RandomBranchState.GetStateWeight</c>
///     becomes a 1-cycle <c>(bitset &amp; (1u &lt;&lt; idx)) != 0</c>.</item>
///   <item><b>History[16]</b> answers all "last N" rules:
///     <list type="bullet">
///       <item><c>CannotRepeat</c> — checks the single most recent entry.</item>
///       <item><c>CanRepeatXTimes</c> with maxTimes ≤ 16 — walks the most-recent-first window.</item>
///       <item><c>cooldown N</c> with N ≤ 16 — same window, filtered to <c>IsMove</c> states.</item>
///     </list>
///     The full <c>List&lt;MonsterState&gt;</c> contains older entries but no
///     game-source rule reads beyond the last few. <see cref="SimCaps"/> Check 9
///     enforces this assumption: any monster declaring <c>cooldown &gt; 16</c> or
///     <c>maxTimes &gt; 16</c> trips the assert at startup.</item>
///   <item><b>IllusionFollowUpIdx</b> + flag — when Illusion power triggers, the
///     monster's next state is forced to this id (skipping the random roll).
///     Stored here rather than on <see cref="SimPowerInternal"/> because the
///     consumer is the move-state-machine advance step, and co-locating the
///     payload with the consumer keeps a 25 B struct in one cache line.</item>
/// </list>
///
/// <para>What we deliberately do <i>not</i> store:</para>
/// <list type="bullet">
///   <item><c>States</c> dict — invariant for the monster type, lives in the
///     per-Type <see cref="MonsterStateTable"/> singleton.</item>
///   <item><c>_initialState</c> — same: per-Type immutable, on the table.</item>
///   <item>Older-than-16 history — see Check 9 invariant above.</item>
/// </list>
///
/// <para>Hot path consumer (future Sim Effects layer): given <c>SimEnemyMoveSM</c>
/// + <c>MonsterStateTable</c> + RNG state, simulate <c>FindNextMoveState</c> in
/// branchless ints. Walking the last K entries in the ring buffer:
/// <code>
/// int newest = (head - 1 + 16) &amp; 15;       // most recent write
/// for (int j = 0; j &lt; k &amp;&amp; j &lt; count; j++)
/// {
///     byte stateIdx = history[(newest - j + 16) &amp; 15];
///     ...
/// }
/// </code></para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SimEnemyMoveSM
{
    /// <summary>Index into <see cref="MonsterStateTable.StateIds"/>; 0xFF if uninitialized.</summary>
    public byte CurrentStateIdx;

    /// <summary>Bitfield: see Flag* constants.</summary>
    public byte Flags;

    /// <summary>Number of valid entries in <see cref="History"/> (0..16).</summary>
    public byte HistoryCount;

    /// <summary>Ring-buffer write index (0..15). Next write goes to <c>History[HistoryHead]</c>.</summary>
    public byte HistoryHead;

    /// <summary>Bit i set ⇔ state index i has appeared in StateLog at least once.
    /// Answers <c>MoveRepeatType.UseOnlyOnce</c> in O(1).</summary>
    public uint EverUsedBitset;

    /// <summary><see cref="MonsterStateTable.StateIds"/> index of the override target
    /// installed by <c>IllusionPower.FollowUpStateId</c>, or <see cref="NoFollowUp"/>
    /// if none.</summary>
    public byte IllusionFollowUpIdx;

    /// <summary>Ring buffer of recent (≤ 16) state indices. Oldest at <c>HistoryHead</c>
    /// when full, at index 0 when <c>HistoryCount &lt; 16</c>.</summary>
    public MoveSmHistory16 History;

    // ── Flag bits ──
    public const byte FlagPerformedFirstMove   = 0x01;
    public const byte FlagHasIllusionFollowUp  = 0x02;

    // ── Sentinels / capacities ──
    public const byte NoFollowUp = 0xFF;
    public const int  MaxStates  = 32; // EverUsedBitset width
    public const int  HistoryCap = 16; // History inline-array length

    /// <summary>Test bit <paramref name="stateIdx"/> in <see cref="EverUsedBitset"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool EverUsed(byte stateIdx) => (EverUsedBitset & (1u << stateIdx)) != 0u;
}

/// <summary>
/// Inline 16-byte ring buffer of state indices. Sized to match
/// <see cref="SimEnemyMoveSM.HistoryCap"/>. Compiler lowers <c>history[i]</c>
/// to a single ldelem-equivalent on the embedded byte sequence.
/// </summary>
[InlineArray(SimEnemyMoveSM.HistoryCap)]
internal struct MoveSmHistory16
{
    private byte _element0;
}
