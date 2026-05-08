using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// One orb queue slot, packed into a single 16-bit word so the queue lives
/// in 20 bytes inline (<see cref="OrbSlots10"/>). Layout:
/// <code>
///   bits  0..2  : OrbType   (0 = None / empty, 1..5 = real orb)
///   bits  3..15 : MutVal    (13 bit unsigned, 0..8191)
/// </code>
///
/// MutVal captures the only per-instance mutable state any concrete orb
/// carries:
///   • <c>DarkOrb._evokeVal</c>   — accumulates +PassiveVal each Passive trigger.
///   • <c>GlassOrb._passiveVal</c>— decrements by 1 each Passive trigger (clamped 0).
/// Lightning / Frost / Plasma carry no per-instance state; their MutVal is 0.
///
/// Why 13 bits is enough: DarkOrb starts at 6 and grows by O(20) per turn in
/// the absolute worst case (Focus-stacked, 20 trigger combat); ceiling well
/// under 1000. GlassOrb is bounded at 4. 8191 is two orders of magnitude of
/// headroom — overflow would itself signal a sim bug.
///
/// The struct is a thin <c>ushort</c> wrapper: it does not own a field
/// directly because <see cref="OrbSlots10"/> is an [InlineArray] of ushort
/// (the only primitive InlineArray treats as a packed contiguous block).
/// All helpers are static encoders/decoders so the JIT inlines them into
/// the call site as a few mov + and + or instructions.
/// </summary>
internal static class SimOrb
{
    public const int   TypeBits   = 3;
    public const ushort TypeMask  = 0x0007;     // bits 0..2
    public const int   ValShift   = 3;
    public const ushort ValMask   = 0x1FF8;     // bits 3..15 (after shift, 13 bits)
    public const int   MaxMutVal  = 0x1FFF;     // 8191

    /// <summary>Encode (type, mutVal) into a packed word. <paramref name="mutVal"/>
    /// is clamped to <see cref="MaxMutVal"/>; negative becomes 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Pack(byte type, int mutVal)
    {
        if (mutVal < 0) mutVal = 0;
        else if (mutVal > MaxMutVal) mutVal = MaxMutVal;
        return (ushort)((type & TypeMask) | (mutVal << ValShift));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetType(ushort slot) => (byte)(slot & TypeMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMutVal(ushort slot) => (slot >> ValShift) & MaxMutVal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort WithMutVal(ushort slot, int mutVal)
        => Pack(GetType(slot), mutVal);
}

/// <summary>
/// Inline 10-element ushort array — one entry per orb queue slot. The
/// game's <c>OrbQueue.maxCapacity</c> is 10 and SimCaps asserts that on
/// boot, so the array length is hard-locked.
///
/// 10 × 2 B = 20 B contiguous, fits in one cache line with room to spare.
/// Indexed via <c>state.OrbSlots[i]</c>; the C# compiler lowers that to a
/// pointer-arithmetic access on the inline storage with zero indirection.
/// </summary>
[InlineArray(10)]
internal struct OrbSlots10
{
    private ushort _element0;
}
