using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Random;

namespace STS2.Agent.Sim;

/// <summary>
/// Identifies one of the per-combat RNG streams owned by the game's
/// <c>RunRngSet</c>. Used to index into <see cref="RandomStateBuffer"/>.
///
/// The game has 12 streams; we only mirror the 8 that can fire DURING a
/// combat (the other 4 — UpFront / UnknownMapPoint / CombatPotionGeneration
/// / TreasureRoomRelics — only consume randomness in map / reward screens).
///
/// Values are explicit, dense, and start at 0 so they double as array
/// indices. <see cref="Count"/> is the buffer length and must stay in sync
/// with <see cref="RandomStateBuffer"/>'s [InlineArray] attribute.
/// </summary>
internal enum SimRngSlot : int
{
    /// <summary>RunRngSet.Shuffle — discard→draw reshuffles, draw order. Highest DFS impact.</summary>
    Shuffle              = 0,
    /// <summary>RunRngSet.CombatTargets — random-target attacks (e.g. Sword Boomerang).</summary>
    CombatTargets        = 1,
    /// <summary>RunRngSet.CombatCardGeneration — Havoc / random-card effects.</summary>
    CombatCardGeneration = 2,
    /// <summary>RunRngSet.CombatCardSelection — Calculated Gamble etc.</summary>
    CombatCardSelection  = 3,
    /// <summary>RunRngSet.CombatEnergyCosts — random energy-cost cards (rare).</summary>
    CombatEnergyCosts    = 4,
    /// <summary>RunRngSet.CombatOrbGeneration — random orb generation (Defender).</summary>
    CombatOrbGeneration  = 5,
    /// <summary>RunRngSet.MonsterAi — monsters' next-move selection.</summary>
    MonsterAi            = 6,
    /// <summary>RunRngSet.Niche — edge-case effects.</summary>
    Niche                = 7,

    /// <summary>Total slot count. Must match RandomStateBuffer's [InlineArray(N)].</summary>
    Count                = 8,
}

/// <summary>
/// Inline 8-element buffer of <see cref="RandomState"/>. Sized via
/// <see cref="InlineArrayAttribute"/> so the entire 8 × 228 = 1824-byte
/// array lives directly inside the blob-backed combat snapshot with zero heap
/// indirection. Indexed with <see cref="SimRngSlot"/>:
/// <c>ref RandomState s = ref state.Rngs[(int)SimRngSlot.Shuffle];</c>
///
/// Only <see cref="SimRngSlot.Shuffle"/> through <see cref="SimRngSlot.Niche"/>
/// (all 8 in-combat streams) are captured by Snapshot. CopyFrom does a single
/// struct-assign that copies all 8 in one memcpy.
/// </summary>
[System.Runtime.CompilerServices.InlineArray((int)SimRngSlot.Count)]
internal struct RandomStateBuffer
{
    private RandomState _element0;
}

/// <summary>
/// Bit-exact mirror of <see cref="MegaCrit.Sts2.Core.Random.MegaRandom"/>'s
/// internal Xoshiro256** state. Stored as an inline <c>struct</c> of four
/// <c>ulong</c>s (32 bytes total) so cloning is one small memcpy with zero
/// heap allocation — required because every DFS search node owns its own RNG,
/// and 100k+ clones per think tick must not pressure the GC.
///
/// Layout mirrors MegaRandom (verified game v0.107.1, 2026-06-18):
///   ulong _s0, _s1, _s2, _s3  →  ulong S0, S1, S2, S3
/// </summary>
internal struct RandomState : IEquatable<RandomState>
{
    public ulong S0;
    public ulong S1;
    public ulong S2;
    public ulong S3;

    public readonly bool Equals(RandomState other)
        => S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;

    public override readonly bool Equals(object? obj)
        => obj is RandomState other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(S0, S1, S2, S3);

    public override readonly string ToString()
        => $"s0={S0:x} s1={S1:x} s2={S2:x} s3={S3:x}";
}

/// <summary>
/// One-shot capture from a live <see cref="MegaRandom"/> + bit-exact
/// reproduction of its <c>Xoshiro256**</c> sequence directly on a
/// <see cref="RandomState"/>. Used by FromReal to snapshot the game's shuffle
/// RNG, and by the sim card-effect code (Discovery / draw / etc.) to draw
/// future cards on the cloned state without touching the real game.
/// </summary>
internal static class RandomStateOps
{
    private const double Incr = 1.1102230246251565E-16; // 2^-53, matches MegaRandom._incrDouble

    // Reflection chain to reach MegaRandom's private Xoshiro256** state fields,
    // plus the game's Rng wrapper's private MegaRandom instance. All FieldInfos
    // cached at type init; a layout change in a future game update will throw
    // at startup with a clear message rather than corrupt state silently.
    private static readonly FieldInfo F_s0;
    private static readonly FieldInfo F_s1;
    private static readonly FieldInfo F_s2;
    private static readonly FieldInfo F_s3;

    /// <summary>Path from game's <see cref="Rng"/> wrapper down to its private MegaRandom.</summary>
    private static readonly FieldInfo F_rngRandom;

    static RandomStateOps()
    {
        const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

        F_s0 = typeof(MegaRandom).GetField("_s0", BF) ?? throw Err("MegaRandom._s0");
        F_s1 = typeof(MegaRandom).GetField("_s1", BF) ?? throw Err("MegaRandom._s1");
        F_s2 = typeof(MegaRandom).GetField("_s2", BF) ?? throw Err("MegaRandom._s2");
        F_s3 = typeof(MegaRandom).GetField("_s3", BF) ?? throw Err("MegaRandom._s3");

        F_rngRandom = typeof(Rng).GetField("_random", BF)
            ?? throw Err("MegaCrit.Sts2.Core.Random.Rng._random");
    }

    private static InvalidOperationException Err(string field) => new(
        $"RandomStateOps: reflection chain broken at '{field}'. " +
        "MegaRandom/Rng internal layout changed; update RandomStateOps.");

    /// <summary>
    /// Snapshot <paramref name="src"/>'s current internal state into
    /// <paramref name="dst"/>. After this call, calling <see cref="Next"/> on
    /// <paramref name="dst"/> produces the exact same sequence that
    /// <c>src.NextInt()</c>/<c>NextDouble()</c> would.
    /// </summary>
    public static void Capture(MegaRandom src, ref RandomState dst)
    {
        dst.S0 = (ulong)F_s0.GetValue(src)!;
        dst.S1 = (ulong)F_s1.GetValue(src)!;
        dst.S2 = (ulong)F_s2.GetValue(src)!;
        dst.S3 = (ulong)F_s3.GetValue(src)!;
    }

    /// <summary>
    /// Convenience: capture the underlying <see cref="MegaRandom"/> out of
    /// the game's <see cref="Rng"/> wrapper. One reflection hop (cached) +
    /// the same state copy as <see cref="Capture(MegaRandom, ref RandomState)"/>.
    /// </summary>
    public static void CaptureFromRng(Rng src, ref RandomState dst)
    {
        var megaRandom = (MegaRandom?)F_rngRandom.GetValue(src)
            ?? throw new InvalidOperationException("Rng._random is null.");
        Capture(megaRandom, ref dst);
    }

    /// <summary>
    /// Bit-exact replication of MegaRandom.NextULongInner() (Xoshiro256**
    /// state transition + scrambler). Advances <paramref name="s"/> in place.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong NextULongInner(ref RandomState s)
    {
        ulong s0 = s.S0, s1 = s.S1, s2 = s.S2, s3 = s.S3;

        ulong result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
        ulong t = s1 << 17;

        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= t;
        s3 = BitOperations.RotateLeft(s3, 45);

        s.S0 = s0; s.S1 = s1; s.S2 = s2; s.S3 = s3;
        return result;
    }

    /// <summary>Mirrors <see cref="MegaRandom.NextDouble"/>: top 53 bits scaled to [0, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double NextDouble(ref RandomState s)
        => (double)(NextULongInner(ref s) >> 11) * Incr;

    /// <summary>
    /// Mirrors <see cref="Rng.NextInt(int)"/> → <c>MegaRandom.Next(int)</c> →
    /// <c>(int)(NextDouble() * maxValue)</c>. Returns 0 if
    /// <paramref name="maxExclusive"/> &lt;= 0 (defensive; the real
    /// <see cref="MegaRandom"/> throws instead, which the sim must not do).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(ref RandomState s, int maxExclusive)
    {
        if (maxExclusive <= 0) return 0;
        return (int)(NextDouble(ref s) * maxExclusive);
    }
}
