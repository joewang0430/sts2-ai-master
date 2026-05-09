using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2.Agent.Sim;

/// <summary>
/// Per-monster-Type metadata for the move-state-machine simulator. One singleton
/// per concrete <see cref="MonsterModel"/> subclass, built lazily on first
/// snapshot of that monster type and cached for the entire process lifetime.
///
/// Why a Type-keyed cache and not per-instance:
/// <list type="bullet">
///   <item>Every <c>Crusher</c>, every <c>Axebot</c>, etc. shares the exact same
///     state-tree topology — the override of <c>GenerateMoveStateMachine()</c> is
///     deterministic per Type. Building once amortizes the 2-3 µs of dictionary
///     iteration + reflection over thousands of future snapshots.</item>
///   <item>Hot-path readers can address state via <c>byte</c> indices and skip
///     all string compares.</item>
/// </list>
/// </summary>
internal sealed class MonsterStateTable
{
    /// <summary>State indices → original string ids. Sorted ordinal for
    /// determinism across processes (Dictionary key order is otherwise
    /// insertion-order-biased and may differ if game source rearranges states).</summary>
    public readonly string[] StateIds;

    /// <summary>String id → state index lookup. Frozen for O(1) reads with
    /// no allocations; populated once at construction.</summary>
    public readonly FrozenDictionary<string, byte> IdToIdx;

    /// <summary>Per-state mirror of <see cref="MonsterState.IsMove"/>. Lets the
    /// hot path filter "moves only" windows (cooldown rule) without virtual calls.</summary>
    public readonly bool[] IsMove;

    /// <summary>Per-state mirror of <see cref="MonsterState.ShouldAppearInLogs"/>.
    /// Same purpose: branchless filter for the StateLog-append decision.</summary>
    public readonly bool[] ShouldAppearInLogs;

    internal MonsterStateTable(Type monsterType, MonsterMoveStateMachine machine)
    {
        Dictionary<string, MonsterState> states = machine.States;
        int n = states.Count;
        if (n == 0)
        {
            throw new InvalidOperationException(
                $"SimMonsterStateRegistry: monster '{monsterType.FullName}' has empty States dict.");
        }
        if (n > SimEnemyMoveSM.MaxStates)
        {
            throw new InvalidOperationException(
                $"SimMonsterStateRegistry: monster '{monsterType.FullName}' has {n} move states " +
                $"but SimEnemyMoveSM.EverUsedBitset is {SimEnemyMoveSM.MaxStates}-bit. " +
                "Widen the bitset (uint→ulong) or split the state machine.");
        }

        // Sort ids ordinally for deterministic indexing across runs / processes.
        // Dictionary<,>.Keys order is insertion-biased and not contractually stable.
        string[] ids = new string[n];
        int j = 0;
        foreach (string k in states.Keys) ids[j++] = k;
        Array.Sort(ids, StringComparer.Ordinal);
        StateIds = ids;

        IsMove             = new bool[n];
        ShouldAppearInLogs = new bool[n];
        Dictionary<string, byte> tmp = new(n, StringComparer.Ordinal);
        for (byte b = 0; b < n; b++)
        {
            string id = ids[b];
            tmp[id] = b;
            MonsterState st = states[id];
            IsMove[b]             = st.IsMove;
            ShouldAppearInLogs[b] = st.ShouldAppearInLogs;
        }
        IdToIdx = tmp.ToFrozenDictionary(StringComparer.Ordinal);

        // Cooldown / maxTimes invariant: any RandomBranchState rule must fit in
        // the 16-deep history window. Walk every RandomBranchState's StateWeight
        // list once and assert. (Costs ~µs per Type, paid once.)
        VerifyHistoryCaps(monsterType, states);
    }

    private static void VerifyHistoryCaps(Type monsterType, Dictionary<string, MonsterState> states)
    {
        foreach (MonsterState st in states.Values)
        {
            if (st is not RandomBranchState rb) continue;
            foreach (RandomBranchState.StateWeight w in rb.States)
            {
                if (w.cooldown > SimEnemyMoveSM.HistoryCap)
                {
                    throw new InvalidOperationException(
                        $"SimMonsterStateRegistry: '{monsterType.FullName}' state '{rb.Id}' branch " +
                        $"'{w.stateId}' has cooldown={w.cooldown} > HistoryCap={SimEnemyMoveSM.HistoryCap}. " +
                        "Increase MoveSmHistory16 length and rebuild.");
                }
                if (w.repeatType == MoveRepeatType.CanRepeatXTimes && w.maxTimes > SimEnemyMoveSM.HistoryCap)
                {
                    throw new InvalidOperationException(
                        $"SimMonsterStateRegistry: '{monsterType.FullName}' state '{rb.Id}' branch " +
                        $"'{w.stateId}' has maxTimes={w.maxTimes} > HistoryCap={SimEnemyMoveSM.HistoryCap}. " +
                        "Increase MoveSmHistory16 length and rebuild.");
                }
            }
        }
    }
}

/// <summary>
/// Process-wide cache of <see cref="MonsterStateTable"/>s plus reflection
/// handles into the private fields of <see cref="MonsterMoveStateMachine"/>
/// that we need to read at snapshot time.
///
/// Lazy by design: the game's content database is not populated when our mod
/// initializes, and even after it is, we don't want to instantiate every
/// monster ourselves to run <c>GenerateMoveStateMachine</c> (mutates model
/// state). Instead, on first snapshot of a given monster type, the live
/// instance is right there — we just borrow its <c>MoveStateMachine.States</c>
/// dict (read-only iteration) to build the table, then cache forever.
/// </summary>
internal static class SimMonsterStateRegistry
{
    private static readonly ConcurrentDictionary<Type, MonsterStateTable> _tables = new();
    private static readonly object _handleGate = new();
    private static readonly Dictionary<MonsterStateTable, ushort> _handlesByTable = new();
    private static readonly Dictionary<Type, ushort> _handlesByType = new();
    private static readonly List<MonsterStateTable?> _tablesByHandle = new() { null };

    /// <summary>Reflected handle to <c>MonsterMoveStateMachine._currentState</c>.
    /// Resolved at static-ctor time; throws on null so a game-source rename is
    /// caught immediately by <see cref="SimCaps"/>'s force-init.</summary>
    private static readonly FieldInfo _currentStateField =
        typeof(MonsterMoveStateMachine).GetField(
            "_currentState",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "SimMonsterStateRegistry: MonsterMoveStateMachine._currentState not found via reflection. " +
            "Game source field renamed?");

    /// <summary>Reflected handle to <c>MonsterMoveStateMachine._performedFirstMove</c>.</summary>
    private static readonly FieldInfo _performedFirstMoveField =
        typeof(MonsterMoveStateMachine).GetField(
            "_performedFirstMove",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "SimMonsterStateRegistry: MonsterMoveStateMachine._performedFirstMove not found via reflection. " +
            "Game source field renamed?");

    /// <summary>
    /// Get-or-build the cached table for <paramref name="monster"/>'s concrete
    /// type, using its already-constructed <see cref="MonsterMoveStateMachine"/>.
    /// Returns null if the monster has no state machine (not yet set up for combat).
    /// </summary>
    public static MonsterStateTable? GetOrBuild(MonsterModel monster)
    {
        MonsterMoveStateMachine? sm = monster.MoveStateMachine;
        if (sm == null) return null;
        Type t = monster.GetType();
        // ConcurrentDictionary.GetOrAdd may invoke the factory more than once
        // under contention, but the result is idempotent (Type-keyed singleton)
        // and our mod is single-threaded at snapshot time; either way, fine.
        MonsterStateTable table = _tables.GetOrAdd(t, _ => new MonsterStateTable(t, sm));
        EnsureHandle(t, table);
        return table;
    }

    public static ushort GetHandle(MonsterStateTable? table)
    {
        if (table == null)
            return 0;

        lock (_handleGate)
        {
            if (_handlesByTable.TryGetValue(table, out ushort handle))
                return handle;
        }

        throw new InvalidOperationException(
            "SimMonsterStateRegistry: MonsterStateTable handle requested before registration.");
    }

    public static MonsterStateTable? Resolve(ushort handle)
    {
        if (handle == 0)
            return null;

        lock (_handleGate)
        {
            if ((uint)handle >= (uint)_tablesByHandle.Count)
            {
                throw new InvalidOperationException(
                    $"SimMonsterStateRegistry: invalid MonsterStateTable handle {handle}.");
            }

            return _tablesByHandle[handle];
        }
    }

    /// <summary>Reflection read of the private <c>_currentState</c> field.
    /// Snapshot-only — never call from the DFS hot loop.</summary>
    public static MonsterState GetCurrentState(MonsterMoveStateMachine sm)
        => (MonsterState)_currentStateField.GetValue(sm)!;

    /// <summary>Reflection read of the private <c>_performedFirstMove</c> field.</summary>
    public static bool GetPerformedFirstMove(MonsterMoveStateMachine sm)
        => (bool)_performedFirstMoveField.GetValue(sm)!;

    private static void EnsureHandle(Type monsterType, MonsterStateTable table)
    {
        lock (_handleGate)
        {
            if (_handlesByType.TryGetValue(monsterType, out ushort existingHandle))
            {
                if (!_handlesByTable.ContainsKey(table))
                    _handlesByTable.Add(table, existingHandle);
                return;
            }

            if (_tablesByHandle.Count > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    "SimMonsterStateRegistry: MonsterStateTable handle space exhausted.");
            }

            ushort handle = (ushort)_tablesByHandle.Count;
            _handlesByType.Add(monsterType, handle);
            _handlesByTable.Add(table, handle);
            _tablesByHandle.Add(table);
        }
    }
}
