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
internal sealed class CombatNodeBlob : IEquatable<CombatNodeBlob>
{
    private readonly byte[] _bytes = GC.AllocateUninitializedArray<byte>(CombatSchemaV1.TotalBytes);

    /// <summary>
    /// Combat-constant relic identity, shared by reference (not byte-copied) across every node
    /// cloned from the same combat root — see <see cref="CombatRootRelics"/> for why this is safe.
    /// Null until the first <see cref="CombatNodeBlobSnapshot.WriteV1FromCombatState"/> call.
    /// </summary>
    public CombatRootRelics? Relics { get; set; }

    public int ByteLength => _bytes.Length;

    public void Clear() => _bytes.AsSpan().Clear();

    public void CopyFrom(CombatNodeBlob src)
    {
        src._bytes.AsSpan().CopyTo(_bytes);
        Relics = src.Relics;
    }

    /// <summary>Structural equality: a single vectorized <c>SequenceEqual</c> over the raw buffer
    /// (matches <see cref="CopyFrom"/>'s "one memcpy, not N field copies" discipline) plus reference
    /// equality on <see cref="Relics"/>. <see cref="CombatRootRelics"/> is immutable per combat and
    /// shared by reference across every node descended from the same root — within one search tree
    /// two nodes' <c>Relics</c> either ARE the same instance or the nodes belong to different
    /// combats entirely (should never collide in a transposition table), so reference equality is
    /// both correct and O(1); a deep comparison would be pure waste.
    ///
    /// This is the simplest-CORRECT baseline, not yet the fastest possible one — a Zobrist/incremental
    /// hash (updated per-mutation instead of rehashing the whole buffer per lookup) is a follow-up
    /// optimization once a search loop exists to profile against, not something to build blind before
    /// that loop is written.</summary>
    public bool Equals(CombatNodeBlob? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _bytes.AsSpan().SequenceEqual(other._bytes) && ReferenceEquals(Relics, other.Relics);
    }

    public override bool Equals(object? obj) => obj is CombatNodeBlob other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.AddBytes(_bytes);
        hash.Add(Relics is null ? 0 : RuntimeHelpers.GetHashCode(Relics));
        return hash.ToHashCode();
    }

    public Span<SimCard> HandCards => CastSlice<SimCard>(CombatSchemaV1.Cards.HandOffset, CombatSchemaV1.Cards.HandBytes);
    public Span<SimCard> DrawCards => CastSlice<SimCard>(CombatSchemaV1.Cards.DrawOffset, CombatSchemaV1.Cards.DrawBytes);
    public Span<SimCard> DiscCards => CastSlice<SimCard>(CombatSchemaV1.Cards.DiscOffset, CombatSchemaV1.Cards.DiscBytes);
    public Span<SimCard> ExhaustCards => CastSlice<SimCard>(CombatSchemaV1.Cards.ExhaustOffset, CombatSchemaV1.Cards.ExhaustBytes);

    public Span<short> CardEnergyBaseCost => CastSlice<short>(CombatSchemaV1.Cards.CardEnergyBaseOffset, CombatSchemaV1.Cards.EnergyBaseBytes);
    public Span<ushort> CardEnergyCapturedX => CastSlice<ushort>(CombatSchemaV1.Cards.CardEnergyCapturedXOffset, CombatSchemaV1.Cards.EnergyCapturedXBytes);
    public Span<ushort> CardEnergyModifierStart => CastSlice<ushort>(CombatSchemaV1.Cards.CardEnergyModifierStartOffset, CombatSchemaV1.Cards.EnergyModifierStartBytes);
    public Span<ushort> CardEnergyModifierCount => CastSlice<ushort>(CombatSchemaV1.Cards.CardEnergyModifierCountOffset, CombatSchemaV1.Cards.EnergyModifierCountBytes);
    public Span<SimLocalCostModifier> CardEnergyModifiers => CastSlice<SimLocalCostModifier>(CombatSchemaV1.Cards.CardEnergyModifiersOffset, CombatSchemaV1.Cards.EnergyModifierBytes);
    public Span<ushort> CardTemporaryStarCostStart => CastSlice<ushort>(CombatSchemaV1.Cards.CardTemporaryStarCostStartOffset, CombatSchemaV1.Cards.TemporaryStarCostStartBytes);
    public Span<ushort> CardTemporaryStarCostCount => CastSlice<ushort>(CombatSchemaV1.Cards.CardTemporaryStarCostCountOffset, CombatSchemaV1.Cards.TemporaryStarCostCountBytes);
    public Span<SimTemporaryStarCost> CardTemporaryStarCosts => CastSlice<SimTemporaryStarCost>(CombatSchemaV1.Cards.CardTemporaryStarCostsOffset, CombatSchemaV1.Cards.TemporaryStarCostBytes);

    public Span<ushort> EnemyHp => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyHpOffset, CombatSchemaV1.Enemies.EnemyHpBytes);
    public Span<ushort> EnemyMaxHp => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyMaxHpOffset, CombatSchemaV1.Enemies.EnemyMaxHpBytes);
    public Span<ushort> EnemyBlock => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyBlockOffset, CombatSchemaV1.Enemies.EnemyBlockBytes);
    public Span<ushort> EnemyIntentDmg => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyIntentDmgOffset, CombatSchemaV1.Enemies.EnemyIntentDmgBytes);
    public Span<ushort> EnemyIntentRawDmg => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyIntentRawDmgOffset, CombatSchemaV1.Enemies.EnemyIntentRawDmgBytes);
    public Span<byte> EnemyIntentHits => CastSlice<byte>(CombatSchemaV1.Enemies.EnemyIntentHitsOffset, CombatSchemaV1.Enemies.EnemyIntentHitsBytes);
    public Span<byte> EnemyIntent => CastSlice<byte>(CombatSchemaV1.Enemies.EnemyIntentOffset, CombatSchemaV1.Enemies.EnemyIntentBytes);
    public Span<SimPowerBitmap> EnemyPowerBitmaps => CastSlice<SimPowerBitmap>(CombatSchemaV1.Enemies.EnemyPowerBitmapsOffset, CombatSchemaV1.Enemies.EnemyPowerBitmapsBytes);
    public Span<short> EnemyPowerValues => CastSlice<short>(CombatSchemaV1.Enemies.EnemyPowerValuesOffset, CombatSchemaV1.Enemies.EnemyPowerValuesBytes);
    public Span<SimPowerInternal> EnemyPowerInternal => CastSlice<SimPowerInternal>(CombatSchemaV1.Enemies.EnemyPowerInternalOffset, CombatSchemaV1.Enemies.EnemyPowerInternalBytes);
    public Span<SimEnemyMoveSM> EnemyMoveSM => CastSlice<SimEnemyMoveSM>(CombatSchemaV1.Enemies.EnemyMoveSmOffset, CombatSchemaV1.Enemies.EnemyMoveSmBytes);
    public Span<ushort> EnemyMoveTableHandles => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyMoveTableHandleOffset, CombatSchemaV1.Enemies.EnemyMoveTableHandleBytes);
    public Span<SimMoveEffect> EnemyMoveEffects => CastSlice<SimMoveEffect>(CombatSchemaV1.Enemies.EnemyMoveEffectsOffset, CombatSchemaV1.Enemies.EnemyMoveEffectsBytes);
    public Span<byte> EnemyMoveEffectCount => CastSlice<byte>(CombatSchemaV1.Enemies.EnemyMoveEffectCountOffset, CombatSchemaV1.Enemies.EnemyMoveEffectCountBytes);
    public Span<byte> EnemyMoveEffectNonDefaultTarget => CastSlice<byte>(CombatSchemaV1.Enemies.EnemyMoveEffectNonDefaultTargetOffset, CombatSchemaV1.Enemies.EnemyMoveEffectNonDefaultTargetBytes);
    public Span<ushort> EnemyMonsterKind => CastSlice<ushort>(CombatSchemaV1.Enemies.EnemyMonsterKindOffset, CombatSchemaV1.Enemies.EnemyMonsterKindBytes);
    public Span<SimPotionSlot> PlayerPotions => CastSlice<SimPotionSlot>(CombatSchemaV1.Player.PlayerPotionsOffset, CombatSchemaV1.Player.PlayerPotionsBytes);
    public Span<short> PlayerPowerValues => CastSlice<short>(CombatSchemaV1.Player.PlayerPowerValuesOffset, CombatSchemaV1.Player.PlayerPowerValuesBytes);
    public Span<byte> RelicCounters => CastSlice<byte>(CombatSchemaV1.Player.PlayerRelicCountersOffset, CombatSchemaV1.Player.PlayerRelicCountersBytes);
    public Span<ushort> OrbSlots => CastSlice<ushort>(CombatSchemaV1.Runtime.OrbSlotsOffset, CombatSchemaV1.Runtime.OrbSlotsBytes);
    public Span<short> OstyPowerValues => CastSlice<short>(CombatSchemaV1.Runtime.OstyPowerValuesOffset, CombatSchemaV1.Runtime.OstyPowerValuesBytes);
    public Span<RandomState> RngStates => CastSlice<RandomState>(CombatSchemaV1.Runtime.RngOffset, CombatSchemaV1.Runtime.RngBytes);

    public ref ushort HandCount => ref RefAt<ushort>(CombatSchemaV1.Cards.HandCountOffset);
    public ref ushort DrawCount => ref RefAt<ushort>(CombatSchemaV1.Cards.DrawCountOffset);
    public ref ushort DiscCount => ref RefAt<ushort>(CombatSchemaV1.Cards.DiscCountOffset);
    public ref ushort ExhaustCount => ref RefAt<ushort>(CombatSchemaV1.Cards.ExhaustCountOffset);
    public ref ushort CardInstanceCount => ref RefAt<ushort>(CombatSchemaV1.Cards.CardInstanceCountOffset);
    public ref ushort CardEnergyModifierUsed => ref RefAt<ushort>(CombatSchemaV1.Cards.CardEnergyModifierUsedOffset);
    public ref ushort CardTemporaryStarCostUsed => ref RefAt<ushort>(CombatSchemaV1.Cards.CardTemporaryStarCostUsedOffset);

    public ref byte Round => ref RefAt<byte>(CombatSchemaV1.Player.RoundOffset);
    public ref byte CurrentSide => ref RefAt<byte>(CombatSchemaV1.Player.CurrentSideOffset);
    public ref ushort PlayerHp => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerHpOffset);
    public ref ushort PlayerMaxHp => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerMaxHpOffset);
    public ref ushort PlayerBlock => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerBlockOffset);
    public ref ushort Energy => ref RefAt<ushort>(CombatSchemaV1.Player.EnergyOffset);
    public ref ushort MaxEnergy => ref RefAt<ushort>(CombatSchemaV1.Player.MaxEnergyOffset);
    public ref ushort PlayerStars => ref RefAt<ushort>(CombatSchemaV1.Player.PlayerStarsOffset);
    public ref ushort AscensionFlags => ref RefAt<ushort>(CombatSchemaV1.Player.AscensionFlagsOffset);
    public ref SimPowerBitmap PlayerPowerBitmap => ref RefAt<SimPowerBitmap>(CombatSchemaV1.Player.PlayerPowerBitmapOffset);
    public ref SimPowerInternal PlayerPowerInternal => ref RefAt<SimPowerInternal>(CombatSchemaV1.Player.PlayerPowerInternalOffset);
    public ref byte PlayerPotionSlotCount => ref RefAt<byte>(CombatSchemaV1.Player.PlayerPotionSlotCountOffset);
    public ref byte PlayerCanRemovePotions => ref RefAt<byte>(CombatSchemaV1.Player.PlayerCanRemovePotionsOffset);
    public ref byte EnemyCount => ref RefAt<byte>(CombatSchemaV1.Enemies.EnemyCountOffset);
    public ref byte OrbCount => ref RefAt<byte>(CombatSchemaV1.Runtime.OrbCountOffset);
    public ref byte OrbCapacity => ref RefAt<byte>(CombatSchemaV1.Runtime.OrbCapacityOffset);
    public ref SimPet Osty => ref RefAt<SimPet>(CombatSchemaV1.Runtime.OstyOffset);
    public ref SimPowerBitmap OstyPowerBitmap => ref RefAt<SimPowerBitmap>(CombatSchemaV1.Runtime.OstyPowerBitmapOffset);
    public ref SimPowerInternal OstyPowerInternal => ref RefAt<SimPowerInternal>(CombatSchemaV1.Runtime.OstyPowerInternalOffset);
    public ref SimHistoryCounters HistoryCounters => ref RefAt<SimHistoryCounters>(CombatSchemaV1.Runtime.HistoryCountersOffset);
    public ref SimCard HistoryCourseCard => ref RefAt<SimCard>(CombatSchemaV1.Runtime.HistoryCourseCardOffset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref RandomState Rng(SimRngSlot slot)
        => ref RngStates[(int)slot];

    private Span<T> CastSlice<T>(int offset, int byteLength) where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(_bytes.AsSpan(offset, byteLength));

    private ref T RefAt<T>(int offset) where T : unmanaged
        => ref MemoryMarshal.AsRef<T>(_bytes.AsSpan(offset, Unsafe.SizeOf<T>()));
}