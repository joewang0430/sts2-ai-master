using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Single contiguous byte buffer for the parallel blob-based combat state path.
///
/// This first prototype exposes the V1-covered slices defined by
/// <see cref="CombatSchemaV1"/>. The old SimCombatState remains the source of
/// truth while we prove that the frozen schema can carry audited hot data
/// losslessly.
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
    public Span<short> PlayerPowers => CastSlice<short>(CombatSchemaV1.Player.PlayerPowersOffset, CombatSchemaV1.Player.PlayerPowersBytes);

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
    public ref byte EnemyCount => ref RefAt<byte>(CombatSchemaV1.Enemies.EnemyCountOffset);

    private Span<T> CastSlice<T>(int offset, int byteLength) where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(_bytes.AsSpan(offset, byteLength));

    private ref T RefAt<T>(int offset) where T : unmanaged
        => ref MemoryMarshal.AsRef<T>(_bytes.AsSpan(offset, Unsafe.SizeOf<T>()));
}