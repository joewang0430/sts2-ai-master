using System;
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
/// array lives directly inside <see cref="SimCombatState"/> with zero heap
/// indirection. Indexed with <see cref="SimRngSlot"/>:
/// <c>ref RandomState s = ref state.Rngs[(int)SimRngSlot.Shuffle];</c>
///
/// Only <see cref="SimRngSlot.Shuffle"/> is captured today (Day-2
/// implementation); the other 7 slots stay zero-default until the rest of
/// the streams are wired up. CopyFrom does a single struct-assign that
/// copies all 8 in one memcpy regardless of which are populated.
/// </summary>
[System.Runtime.CompilerServices.InlineArray((int)SimRngSlot.Count)]
internal struct RandomStateBuffer
{
    private RandomState _element0;
}

/// <summary>
/// Bit-exact mirror of <see cref="System.Random"/>'s internal Knuth-subtractive
/// state. Stored as an inline <c>unsafe struct</c> with a <c>fixed</c> 56-int
/// buffer so cloning is one ~228-byte memcpy with zero heap allocation —
/// required because every DFS search node owns its own RNG, and 100k+ clones
/// per think tick must not pressure the GC.
///
/// Layout mirrors System.Random+CompatPrng (verified .NET 9, 2026-04):
///   int[56] _seedArray  →  fixed int Arr[56]
///   int     _inext      →  int INext
///   int     _inextp     →  int INextp
/// </summary>
internal unsafe struct RandomState
{
    public const int ArrLen = 56;

    /// <summary>Mirrors System.Random+CompatPrng._seedArray (length 56).</summary>
    public fixed int Arr[ArrLen];

    /// <summary>Mirrors System.Random+CompatPrng._inext.</summary>
    public int INext;

    /// <summary>Mirrors System.Random+CompatPrng._inextp.</summary>
    public int INextp;
}

/// <summary>
/// One-shot capture from a live <see cref="System.Random"/> + bit-exact
/// reproduction of its <c>Next()</c> sequence directly on a
/// <see cref="RandomState"/>. Used by FromReal to snapshot the game's shuffle
/// RNG, and by the sim card-effect code (Discovery / draw / etc.) to draw
/// future cards on the cloned state without touching the real game.
/// </summary>
internal static unsafe class RandomStateOps
{
    // Reflection chain to reach the Knuth state inside System.Random. .NET 9 wraps
    // it in an internal hierarchy (Random._impl → Net5CompatSeedImpl._prng →
    // CompatPrng._seedArray/_inext/_inextp). All FieldInfos cached at type init;
    // a layout change in a future .NET update will throw at startup with a clear
    // message rather than corrupt state silently.
    private static readonly FieldInfo F_impl;
    private static readonly FieldInfo F_prng;
    private static readonly FieldInfo F_seedArr;
    private static readonly FieldInfo F_inext;
    private static readonly FieldInfo F_inextp;

    /// <summary>Path from game's <see cref="Rng"/> wrapper down to its private System.Random.</summary>
    private static readonly FieldInfo F_rngRandom;

    static RandomStateOps()
    {
        const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
        var probe = new System.Random(0);

        F_impl = typeof(System.Random).GetField("_impl", BF)
            ?? throw Err("System.Random._impl");
        var implObj = F_impl.GetValue(probe)!;

        F_prng = implObj.GetType().GetField("_prng", BF)
            ?? throw Err($"{implObj.GetType().FullName}._prng (need seeded Random; xoshiro impls are unsupported)");
        var prngObj = F_prng.GetValue(implObj)!;
        var prngT = prngObj.GetType();

        F_seedArr = prngT.GetField("_seedArray", BF) ?? throw Err($"{prngT.FullName}._seedArray");
        F_inext   = prngT.GetField("_inext",     BF) ?? throw Err($"{prngT.FullName}._inext");
        F_inextp  = prngT.GetField("_inextp",    BF) ?? throw Err($"{prngT.FullName}._inextp");

        F_rngRandom = typeof(Rng).GetField("_random", BF)
            ?? throw Err("MegaCrit.Sts2.Core.Random.Rng._random");
    }

    private static InvalidOperationException Err(string field) => new(
        $"RandomStateOps: reflection chain broken at '{field}'. " +
        ".NET internal Random layout changed; update RandomStateOps.");

    /// <summary>
    /// Snapshot <paramref name="src"/>'s current internal state into
    /// <paramref name="dst"/>. After this call, calling
    /// <see cref="Next(ref RandomState)"/> on <paramref name="dst"/> produces
    /// the exact same sequence that <c>src.Next()</c> would.
    /// </summary>
    public static void Capture(System.Random src, ref RandomState dst)
    {
        // F_prng.GetValue returns a *boxed copy* of the struct — fine, we only read.
        object impl = F_impl.GetValue(src) ?? throw new InvalidOperationException("Random._impl is null.");
        object prng = F_prng.GetValue(impl) ?? throw new InvalidOperationException("Random._impl._prng is null.");

        var arr = (int[])F_seedArr.GetValue(prng)!;
        if (arr.Length != RandomState.ArrLen)
            throw new InvalidOperationException(
                $"RandomStateOps: _seedArray length {arr.Length} != {RandomState.ArrLen}.");

        fixed (int* p = dst.Arr)
        {
            for (int i = 0; i < RandomState.ArrLen; i++) p[i] = arr[i];
        }
        dst.INext  = (int)F_inext.GetValue(prng)!;
        dst.INextp = (int)F_inextp.GetValue(prng)!;
    }

    /// <summary>
    /// Convenience: capture the underlying <see cref="System.Random"/> out of
    /// the game's <see cref="Rng"/> wrapper. One reflection hop (cached) +
    /// the same Knuth-state copy as <see cref="Capture(System.Random, ref RandomState)"/>.
    /// </summary>
    public static void CaptureFromRng(Rng src, ref RandomState dst)
    {
        var sysRandom = (System.Random?)F_rngRandom.GetValue(src)
            ?? throw new InvalidOperationException("Rng._random is null.");
        Capture(sysRandom, ref dst);
    }

    /// <summary>
    /// Bit-exact replication of System.Random+CompatPrng.InternalSample()
    /// (the seeded legacy Knuth subtractive generator).
    /// Returns a non-negative int in [0, int.MaxValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(ref RandomState s)
    {
        int locINext  = s.INext;
        int locINextp = s.INextp;

        // 56-element ring with index 0 unused; indices stay in [1, 55].
        if (++locINext  >= 56) locINext  = 1;
        if (++locINextp >= 56) locINextp = 1;

        int retVal = s.Arr[locINext] - s.Arr[locINextp];
        if (retVal == int.MaxValue) retVal--;
        if (retVal < 0) retVal += int.MaxValue;

        s.Arr[locINext] = retVal;
        s.INext  = locINext;
        s.INextp = locINextp;
        return retVal;
    }

    /// <summary>
    /// Mirrors <see cref="System.Random.Next(int)"/> for the seeded legacy
    /// impl: <c>(int)(InternalSample() * (1.0 / int.MaxValue) * maxValue)</c>.
    /// Returns 0 if <paramref name="maxExclusive"/> &lt;= 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(ref RandomState s, int maxExclusive)
    {
        if (maxExclusive <= 1) return 0;
        return (int)(Next(ref s) * (1.0 / int.MaxValue) * maxExclusive);
    }
}
