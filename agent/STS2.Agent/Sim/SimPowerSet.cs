using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// 261-bit membership bitmap: bit i set ⇔ this creature currently has <c>SimPowerType</c> i.
/// Backed by <see cref="SimPowerSet.WordCount"/> × <c>ulong</c> (320 bits, 59 always-zero) so
/// popcount-based position lookups (see <see cref="SimPowerSet.PositionOf"/>) cost one hardware
/// POPCNT per 64-bit word instead of a bit-by-bit scan.
/// </summary>
[InlineArray(SimPowerSet.WordCount)]
internal struct SimPowerBitmap
{
    private ulong _element0;
}

/// <summary>
/// Inline fixed-capacity power-amount list paired with a <see cref="SimPowerBitmap"/>.
/// Capacity is <see cref="SimPowerSet.ValueCap"/>. Slot i holds the amount for whichever power
/// type is the i-th set bit in the paired bitmap (ascending <c>SimPowerType</c> order) — writers
/// must set every bit first, then fill slots by computed position (see
/// <see cref="SimPowerSet.PositionOf"/>); never append in live-list iteration order.
/// </summary>
[InlineArray(SimPowerSet.ValueCap)]
internal struct SimPowerValues
{
    private short _element0;
}

/// <summary>
/// Sparse per-creature power-amount storage. Replaces the old dense <c>short[261]</c>-per-creature
/// layout: only ~12 of 261 power types are ever active on a given creature at once, so a dense
/// array per creature was 97%+ permanently-zero padding (the same lesson <see cref="SimPowerInternal"/>
/// already applied to powers' hidden extra counters, just not yet applied to the base Amount here).
///
/// A creature's power set is a <see cref="SimPowerBitmap"/> (which types are present) plus a
/// <see cref="SimPowerValues"/> list (their amounts, positioned by popcount, not by a stored type
/// tag) — this keeps lookups a small fixed number of POPCNT/mask operations regardless of how many
/// powers are active, instead of a linear scan over a tagged list.
/// </summary>
internal static class SimPowerSet
{
    public const int BitsPerWord = 64;

    /// <summary>Words needed to cover <c>SimPowerType.Count</c> bits (5 for 261).</summary>
    public const int WordCount = (SimPowerType.Count + BitsPerWord - 1) / BitsPerWord;

    /// <summary>Max simultaneously-active power types per creature. Real combats stack a handful;
    /// this leaves generous headroom. Enforced at snapshot time — see
    /// <see cref="CombatNodeBlobSnapshot"/> — with a fail-loud throw on overflow, not silent
    /// truncation.</summary>
    public const int ValueCap = CombatSimLayout.PowerValueCap;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Test(in SimPowerBitmap bitmap, int type)
        => (bitmap[type / BitsPerWord] & (1UL << (type % BitsPerWord))) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Set(ref SimPowerBitmap bitmap, int type)
        => bitmap[type / BitsPerWord] |= 1UL << (type % BitsPerWord);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clear(ref SimPowerBitmap bitmap, int type)
        => bitmap[type / BitsPerWord] &= ~(1UL << (type % BitsPerWord));

    /// <summary>Insert <paramref name="value"/> at <paramref name="pos"/>, shifting the
    /// <paramref name="count"/> existing entries at/after <paramref name="pos"/> up by one.
    /// Caller sets the bitmap bit separately (before or after — insertion only touches values).
    /// Takes the same <c>Span&lt;short&gt;</c> shape <see cref="CombatNodeBlob"/> already exposes
    /// (PlayerPowerValues / a per-enemy slice / OstyPowerValues), not the raw inline-array type.</summary>
    public static void InsertValue(Span<short> values, int pos, int count, short value)
    {
        for (int i = count; i > pos; i--)
            values[i] = values[i - 1];
        values[pos] = value;
    }

    /// <summary>Remove the entry at <paramref name="pos"/>, shifting the remaining entries after it
    /// down by one. Caller clears the bitmap bit separately.</summary>
    public static void RemoveValue(Span<short> values, int pos, int count)
    {
        for (int i = pos; i < count - 1; i++)
            values[i] = values[i + 1];
    }

    /// <summary>
    /// Number of set bits at indices &lt; <paramref name="type"/> — this power's position in the
    /// paired <see cref="SimPowerValues"/> list. Cost: at most <see cref="WordCount"/> POPCNT ops,
    /// independent of how many bits are actually set.
    /// </summary>
    public static int PositionOf(in SimPowerBitmap bitmap, int type)
    {
        int word = type / BitsPerWord;
        int bit = type % BitsPerWord;
        int count = 0;
        for (int w = 0; w < word; w++)
            count += BitOperations.PopCount(bitmap[w]);
        if (bit > 0)
            count += BitOperations.PopCount(bitmap[word] & ((1UL << bit) - 1));
        return count;
    }

    /// <summary>Total number of set bits (= number of active powers on this creature).</summary>
    public static int Count(in SimPowerBitmap bitmap)
    {
        int count = 0;
        for (int w = 0; w < WordCount; w++)
            count += BitOperations.PopCount(bitmap[w]);
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetAmount(in SimPowerBitmap bitmap, ReadOnlySpan<short> values, int type, out short amount)
    {
        if (!Test(bitmap, type))
        {
            amount = 0;
            return false;
        }

        amount = values[PositionOf(bitmap, type)];
        return true;
    }
}
