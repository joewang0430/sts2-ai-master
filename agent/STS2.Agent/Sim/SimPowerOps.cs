using System;
using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Shared accessors for the dense per-creature power slices stored in the
/// frozen <see cref="CombatNodeBlob"/>.
/// </summary>
internal static class SimPowerOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref short GetPlayerAmount(CombatNodeBlob blob, int type)
        => ref blob.PlayerPowers[type];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref short GetEnemyAmount(CombatNodeBlob blob, int idx, int type)
        => ref blob.EnemyPower(idx, type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetPlayerRow(CombatNodeBlob blob)
        => blob.PlayerPowers;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetEnemyRow(CombatNodeBlob blob, int idx)
        => blob.EnemyPowers.Slice(idx * CombatSimLayout.PowersPerCre, CombatSimLayout.PowersPerCre);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetPlayerInternal(CombatNodeBlob blob)
        => ref blob.PlayerPowerInternal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetEnemyInternal(CombatNodeBlob blob, int idx)
        => ref blob.EnemyPowerInternal[idx];
}