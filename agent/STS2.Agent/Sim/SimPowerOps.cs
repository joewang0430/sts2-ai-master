using System;
using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Shared accessors for the dense per-creature power slices stored on either
/// the legacy <see cref="SimCombatState"/> or the frozen <see cref="CombatNodeBlob"/>.
/// Keeps row math and by-ref access centralized so read paths can switch
/// between the two layouts without re-encoding offsets.
/// </summary>
internal static class SimPowerOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref short GetPlayerAmount(SimCombatState state, int type)
        => ref state.PlayerPowers[type];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref short GetPlayerAmount(CombatNodeBlob blob, int type)
        => ref blob.PlayerPower(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref short GetEnemyAmount(SimCombatState state, int idx, int type)
        => ref state.EnemyPower(idx, type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref short GetEnemyAmount(CombatNodeBlob blob, int idx, int type)
        => ref blob.EnemyPower(idx, type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetPlayerRow(SimCombatState state)
        => state.PlayerPowers;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetPlayerRow(CombatNodeBlob blob)
        => blob.PlayerPowers;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetEnemyRow(SimCombatState state, int idx)
        => state.EnemyPowers.AsSpan(idx * SimCombatState.PowersPerCre, SimCombatState.PowersPerCre);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetEnemyRow(CombatNodeBlob blob, int idx)
        => blob.EnemyPowers.Slice(idx * SimCombatState.PowersPerCre, SimCombatState.PowersPerCre);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetPlayerInternal(SimCombatState state)
        => ref state.PlayerPowerInternal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetPlayerInternal(CombatNodeBlob blob)
        => ref blob.PlayerPowerInternal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetEnemyInternal(SimCombatState state, int idx)
        => ref state.EnemyPowerInternal[idx];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetEnemyInternal(CombatNodeBlob blob, int idx)
        => ref blob.EnemyPowerState(idx);
}