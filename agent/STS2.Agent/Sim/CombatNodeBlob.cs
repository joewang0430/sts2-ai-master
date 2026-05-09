using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Single contiguous byte buffer for the parallel blob-based combat state path.
///
/// Exposes the V1-covered slices defined by <see cref="CombatSchemaV1"/> and is
/// populated directly from live combat by <see cref="CombatNodeBlobSnapshot"/>.
/// </summary>
internal sealed class CombatNodeBlob
{
    private readonly byte[] _bytes = GC.AllocateUninitializedArray<byte>(CombatSchemaV1.TotalBytes);

    public int ByteLength => _bytes.Length;

    public void Clear() => _bytes.AsSpan().Clear();

    public void CopyFrom(CombatNodeBlob src) => src._bytes.AsSpan().CopyTo(_bytes);

    public Span<SimCard> HandCards => CastSlice<SimCard>(CombatSchemaV1.Cards.HandOffset, CombatSchemaV1.Cards.HandBytes);
    public Span<SimCard> DrawCards => CastSlice<SimCard>(CombatSchemaV1.Cards.DrawOffset, CombatSchemaV1.Cards.DrawBytes);
    public Span<SimCard> DiscCards => CastSlice<SimCard>(CombatSchemaV1.Cards.DiscOffset, CombatSchemaV1.Cards.DiscBytes);
    public Span<SimCard> ExhaustCards => CastSlice<SimCard>(CombatSchemaV1.Cards.ExhaustOffset, CombatSchemaV1.Cards.ExhaustBytes);

    public Span<short> CardEnergyBaseCost => CastSlice<short>(CombatSchemaV1.Cards.CardEnergyBaseOffset, CombatSchemaV1.Cards.EnergyBaseBytes);
    public Span<ushort> CardEnergyCapturedX => CastSlice<ushort>(CombatSchemaV1.Cards.CardEnergyCapturedXOffset, CombatSchemaV1.Cards.EnergyCapturedXBytes);
    public Span<ushort> CardEnergyModifierStart => CastSlice<ushort>(CombatSchemaV1.Cards.CardEnergyModifierStartOffset, CombatSchemaV1.Cards.EnergyModifierStartBytes);
    public Span<ushort> CardEnergyModifierCount => CastSlice<ushort>(CombatSchemaV1.Cards.CardEnergyModifierCountOffset, CombatSchemaV1.Cards.EnergyModifierCountBytes);
    public Span<SimLocalCostModifier> CardEnergyModifiers => CastSlice<SimLocalCostModifier>(CombatSchemaV1.Cards.CardEnergyModifiersOffset, CombatSchemaV1.Cards.EnergyModifierBytes);

    public Span<ushort> EnemyHp => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyHpOffset, CombatSchemaV1.Enemies.EnemyHpBytes);
    public Span<ushort> EnemyMaxHp => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyMaxHpOffset, CombatSchemaV1.Enemies.EnemyMaxHpBytes);
    public Span<ushort> EnemyBlock => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyBlockOffset, CombatSchemaV1.Enemies.EnemyBlockBytes);
    public Span<ushort> EnemyIntentDmg => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyIntentDmgOffset, CombatSchemaV1.Enemies.EnemyIntentDmgBytes);
    public Span<byte> EnemyIntentHits => CastSlice<byte>(CombatSchemaV1.Enemies.EnemyIntentHitsOffset, CombatSchemaV1.Enemies.EnemyIntentHitsBytes);
    public Span<byte> EnemyIntent => CastSlice<byte>(CombatSchemaV1.Enemies.EnemyIntentOffset, CombatSchemaV1.Enemies.EnemyIntentBytes);
    public Span<short> EnemyPowers => CastSlice<short>(CombatSchemaV1.Enemies.EnemyPowersOffset, CombatSchemaV1.Enemies.EnemyPowersBytes);
    public Span<SimPowerInternal> EnemyPowerInternal => CastSlice<SimPowerInternal>(CombatSchemaV1.Enemies.EnemyPowerInternalOffset, CombatSchemaV1.Enemies.EnemyPowerInternalBytes);
    public Span<SimEnemyMoveSM> EnemyMoveSM => CastSlice<SimEnemyMoveSM>(CombatSchemaV1.Enemies.EnemyMoveSmOffset, CombatSchemaV1.Enemies.EnemyMoveSmBytes);
    public Span<ushort> EnemyMoveTableHandles => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyMoveTableHandleOffset, CombatSchemaV1.Enemies.EnemyMoveTableHandleBytes);
    public Span<short> PlayerPowers => CastSlice<short>(CombatSchemaV1.Player.PlayerPowersOffset, CombatSchemaV1.Player.PlayerPowersBytes);
    public Span<ushort> OrbSlots => CastSlice<ushort>(CombatSchemaV1.Runtime.OrbSlotsOffset, CombatSchemaV1.Runtime.OrbSlotsBytes);
    public Span<short> OstyPowers => CastSlice<short>(CombatSchemaV1.Runtime.OstyPowersOffset, CombatSchemaV1.Runtime.OstyPowersBytes);
    public Span<RandomState> RngStates => CastSlice<RandomState>(CombatSchemaV1.Runtime.RngOffset, CombatSchemaV1.Runtime.RngBytes);

    public ref ushort HandCount => ref RefAt<ushort>(CombatSchemaV1.Cards.HandCountOffset);
    public ref ushort DrawCount => ref RefAt<ushort>(CombatSchemaV1.Cards.DrawCountOffset);
    public ref ushort DiscCount => ref RefAt<ushort>(CombatSchemaV1.Cards.DiscCountOffset);
    public ref ushort ExhaustCount => ref RefAt<ushort>(CombatSchemaV1.Cards.ExhaustCountOffset);
    public ref ushort CardInstanceCount => ref RefAt<ushort>(CombatSchemaV1.Cards.CardInstanceCountOffset);
    public ref ushort CardEnergyModifierUsed => ref RefAt<ushort>(CombatSchemaV1.Cards.CardEnergyModifierUsedOffset);

    public ref byte Round => ref RefAt<byte>(CombatSchemaV1.Player.RoundOffset);
    public ref ushort PlayerHp => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerHpOffset);
    public ref ushort PlayerMaxHp => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerMaxHpOffset);
    public ref ushort PlayerBlock => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerBlockOffset);
    public ref ushort Energy => ref RefAt<ushort>(CombatSchemaV1.Player.EnergyOffset);
    public ref ushort MaxEnergy => ref RefAt<ushort>(CombatSchemaV1.Player.MaxEnergyOffset);
    public ref ushort PlayerStars => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerStarsOffset);
    public ref SimPowerInternal PlayerPowerInternal => ref RefAt<SimPowerInternal>(CombatSchemaV1.Player.PlayerPowerInternalOffset);
    public ref byte EnemyCount => ref RefAt<byte>(CombatSchemaV1.Enemies.EnemyCountOffset);
    public ref byte OrbCount => ref RefAt<byte>(CombatSchemaV1.Runtime.OrbCountOffset);
    public ref byte OrbCapacity => ref RefAt<byte>(CombatSchemaV1.Runtime.OrbCapacityOffset);
    public ref SimPet Osty => ref RefAt<SimPet>(CombatSchemaV1.Runtime.OstyOffset);
    public ref SimPowerInternal OstyPowerInternal => ref RefAt<SimPowerInternal>(CombatSchemaV1.Runtime.OstyPowerInternalOffset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref short EnemyPower(int idx, int type)
        => ref EnemyPowers[(idx * CombatSimLayout.PowersPerCre) + type];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref RandomState Rng(SimRngSlot slot)
        => ref RngStates[(int)slot];

    private Span<T> CastSlice<T>(int offset, int byteLength) where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(_bytes.AsSpan(offset, byteLength));

    private ref T RefAt<T>(int offset) where T : unmanaged
        => ref MemoryMarshal.AsRef<T>(_bytes.AsSpan(offset, Unsafe.SizeOf<T>()));
}