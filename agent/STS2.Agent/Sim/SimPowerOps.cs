using System;
using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Shared accessors for the sparse per-creature power bitmap+values pairs stored in the
/// frozen <see cref="CombatNodeBlob"/>. See <see cref="SimPowerSet"/> for the lookup scheme.
/// </summary>
internal static class SimPowerOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetPlayerAmount(CombatNodeBlob blob, int type, out short amount)
        => SimPowerSet.TryGetAmount(blob.PlayerPowerBitmap, blob.PlayerPowerValues, type, out amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetEnemyAmount(CombatNodeBlob blob, int idx, int type, out short amount)
        => SimPowerSet.TryGetAmount(blob.EnemyPowerBitmaps[idx], GetEnemyValues(blob, idx), type, out amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetOstyAmount(CombatNodeBlob blob, int type, out short amount)
        => SimPowerSet.TryGetAmount(blob.OstyPowerBitmap, blob.OstyPowerValues, type, out amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetEnemyValues(CombatNodeBlob blob, int idx)
        => blob.EnemyPowerValues.Slice(idx * SimPowerSet.ValueCap, SimPowerSet.ValueCap);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetPlayerInternal(CombatNodeBlob blob)
        => ref blob.PlayerPowerInternal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetEnemyInternal(CombatNodeBlob blob, int idx)
        => ref blob.EnemyPowerInternal[idx];
}
