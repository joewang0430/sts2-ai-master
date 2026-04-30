using System;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Hot data for one combat node. Designed for thousands of clones per second
/// during DFS:
///
///   • Pure value-type fields. No reference to any game object (CardModel,
///     Creature, PowerModel). Cards are <c>ushort</c>s; behavior lives in
///     <see cref="SimCardDb"/>.
///   • Card id encoding (ushort): bit 15 = upgrade flag (0 = base, 1 = +1),
///     bits 0–14 = the alphabetic id from <see cref="SimCardId"/> (current
///     range 0–576, hard cap 32767). STS2 today caps every card at
///     <c>MaxUpgradeLevel == 1</c> (verified by SimCaps.Verify at startup);
///     a future patch adding multi-level upgrades will trip the assert.
///   • All variable-length data uses fixed-capacity arrays + count instead of
///     <c>List&lt;T&gt;</c>. Reasons:
///       – No bounds-check thunk through a property getter (List.Count is
///         a property; array.Length is a single ldlen).
///       – No hidden internal array swap on Add (List uses _items field).
///       – Lets CopyFrom use <see cref="Array.Copy"/> which compiles down to
///         a vectorized memmove.
///   • Per-creature powers are stored in a single flat <c>short[EnemyCap * PowersPerCre]</c>
///     row-major. <c>short</c> (not sbyte) because Poison / Strength / Dex / Thorns /
///     RollingBoulder routinely exceed sbyte's 127 cap in real builds. short fits
///     [-32768, 32767] which exceeds any conceivable in-game stack. Storage:
///     6 creatures × 259 × 2 B = 3108 B (49 cache lines), still in L1d. Jagged
///     arrays would cost an extra pointer dereference per access; the flat form
///     keeps every creature's 518-byte power vector contiguous.
///   • RNG state is a 228-byte unsafe struct mirroring System.Random's internal
///     56-int Knuth subtractive state, so cloning is a single memcpy with zero
///     heap allocation and full bit-exact reproduction of the game's shuffle.
///   • Card-id piles are <c>ushort[]</c> because the game has 577 card classes
///     plus the upgrade flag bit; byte's 256-value range is too small.
///
/// Capacities (compile-time constants — see field doc-comments for rationale):
///   <see cref="EnemyCap"/>     = 6   (max EncounterModel.Slots count across all encounters)
///   <see cref="HandCap"/>      = 10  (mirrors CardPile.maxCardsInHand)
///   <see cref="PileCap"/>      = 200 (empirical ceiling for draw/disc/exhaust each)
///   <see cref="PowersPerCre"/> = 259 (one slot per concrete PowerModel subclass)
/// Exceeding any cap is a programmer error and asserts via index out-of-range.
/// </summary>
internal sealed class SimCombatState
{
    // ── Capacity constants ────────────────────────────────────────────────────
    // EnemyCap: No central constant in game source. Bounded by EncounterModel.Slots
    //   (CreatureCmd.Add scans Encounter.Slots.LastOrDefault for a free slot before
    //   summoning, so total enemies can never exceed Slots.Count). Surveyed all 20
    //   encounters with explicit Slots overrides; max = 6:
    //     LivingFogNormal:   bomb1..bomb5 + livingFog  (6)
    //     OvicopterNormal:   egg1..egg5 + ovicopter   (6)
    //   No headroom: this is a hard upper bound from game data, not an estimate.
    //   SimCaps.Verify() asserts at mod startup that no loaded encounter exceeds 6.
    public const int EnemyCap     = 6;

    // HandCap: Mirrors the game's own constant CardPile.maxCardsInHand = 10 (CardPile.cs:18).
    //   The reference is direct so a game update to that constant propagates here automatically.
    public const int HandCap      = CardPile.maxCardsInHand;

    // PileCap: No corresponding constant in game source; draw/discard/exhaust use List<CardModel>
    //   (unbounded). The deck grows mid-combat via Discovery, Genesis, Hatch, status/curse adds,
    //   etc., on top of the run deck which can already exceed 50. 200 is a hard ceiling chosen
    //   empirically — almost no real run reaches it. If a future game update makes large-deck
    //   builds common, edit this constant manually.
    public const int PileCap      = 200;

    // PowersPerCre: Mirrors SimPowerType.Count = 259, exactly one slot per concrete
    //   PowerModel subclass in the game (verified 2026-04). Each slot stores a layer
    //   count as short. Storage: 6 creatures * 259 * 2 B = 3108 B, ~49 cache lines,
    //   all in L1d. If a future game patch adds a new PowerModel subclass, both this
    //   constant and SimPowerRegistry must be updated together (the registry's
    //   typeof() entries will fail to compile, which is the intended canary).
    public const int PowersPerCre = SimPowerType.Count;   // 259

    // ── Turn / timing ─────────────────────────────────────────────────────────
    public byte Round;

    // ── Player hot stats ──────────────────────────────────────────────────────
    // ushort: HP/Block cap well below 65535; Energy/MaxEnergy are single digits.
    public ushort PlayerHp;
    public ushort PlayerMaxHp;
    public ushort PlayerBlock;
    public ushort Energy;
    public ushort MaxEnergy;

    /// <summary>
    /// Indexed by SimPowerType.*. Layer count as short: signed for Strength-Down
    /// (negative Strength) and wide enough for Poison / Thorns / RollingBoulder
    /// stacking far beyond sbyte's 127 cap.
    /// </summary>
    public readonly short[] PlayerPowers = new short[PowersPerCre];

    // ── Enemies (parallel arrays of length EnemyCap; valid range [0, EnemyCount)) ─
    public int EnemyCount;
    public readonly ushort[] EnemyHp         = new ushort[EnemyCap]; // 0..65535; boss HP well below
    public readonly ushort[] EnemyMaxHp      = new ushort[EnemyCap];
    public readonly ushort[] EnemyBlock      = new ushort[EnemyCap];
    public readonly ushort[] EnemyIntentDmg  = new ushort[EnemyCap]; // base damage from intent.DamageCalc(); Str/Vuln/Weak applied at sim time
    public readonly byte[]   EnemyIntentHits = new byte[EnemyCap];   // 0..~20

    /// <summary>
    /// Per-enemy intent kind (cast of <see cref="SimIntent"/>). Distinguishes
    /// Attack / Defend / Buff / Debuff / Heal / etc. <see cref="EnemyIntentDmg"/>
    /// and <see cref="EnemyIntentHits"/> are only meaningful when this is
    /// <c>SimIntent.Attack</c> or <c>SimIntent.DeathBlow</c>. Other intent kinds
    /// (block amount / buff stacks / etc.) are not yet captured — see SimIntent
    /// docs for the staged plan.
    /// </summary>
    public readonly byte[] EnemyIntent = new byte[EnemyCap];

    /// <summary>
    /// Flat row-major power matrix. Enemy <c>i</c>'s power <c>p</c> is at
    /// <c>EnemyPowers[i * PowersPerCre + p]</c>. Use <see cref="EnemyPower"/>
    /// for typed access (the JIT inlines it).
    /// </summary>
    public readonly short[] EnemyPowers = new short[EnemyCap * PowersPerCre];

    // ── Card piles (CardId ushorts; behavior in SimCardDb) ──────────────────────────
    // ushort: 15-bit id + upgrade flag in bit 15; byte (256) is too small.
    // *Count stays int because loop bounds are JIT-friendlier as int.
    public readonly ushort[] Hand    = new ushort[HandCap];   public int HandCount;
    public readonly ushort[] Draw    = new ushort[PileCap];   public int DrawCount;
    public readonly ushort[] Disc    = new ushort[PileCap];   public int DiscCount;
    public readonly ushort[] Exhaust = new ushort[PileCap];   public int ExhaustCount;

    // ── RNG (bit-exact mirror of game's System.Random; cloned by struct copy) ─
    public RandomState RngState;

    // ── Typed accessors (JIT-inlined: same codegen as raw indexing) ───────────

    /// <summary>By-ref access to enemy <paramref name="idx"/>'s power slot.
    /// Use as: <c>state.EnemyPower(0, SimPowerType.Vulnerable) += 2;</c></summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref short EnemyPower(int idx, int type)
        => ref EnemyPowers[idx * PowersPerCre + type];

    // ── Clone / reset ─────────────────────────────────────────────────────────

    /// <summary>
    /// Deep-copy <paramref name="src"/>'s state INTO this instance. Pool-friendly:
    /// reuses this object's already-allocated arrays. Hot-path; no allocations.
    ///
    /// Only the *valid prefix* of each pile is copied (length = *Count). Stale
    /// trailing entries past *Count are not zeroed — they are unreachable and
    /// reading them would be a bug regardless of contents.
    /// </summary>
    public void CopyFrom(SimCombatState src)
    {
        // Scalar fields.
        Round         = src.Round;
        PlayerHp      = src.PlayerHp;
        PlayerMaxHp   = src.PlayerMaxHp;
        PlayerBlock   = src.PlayerBlock;
        Energy        = src.Energy;
        MaxEnergy     = src.MaxEnergy;
        EnemyCount    = src.EnemyCount;
        HandCount     = src.HandCount;
        DrawCount     = src.DrawCount;
        DiscCount     = src.DiscCount;
        ExhaustCount  = src.ExhaustCount;

        // RNG state: 228-byte struct copy (single memcpy, no allocation).
        RngState = src.RngState;

        // Fixed-length player power vector — full copy (259 shorts = 518 B).
        Array.Copy(src.PlayerPowers, PlayerPowers, PowersPerCre);

        // Enemy parallel arrays: only copy the valid prefix.
        int n = src.EnemyCount;
        if (n > 0)
        {
            Array.Copy(src.EnemyHp,         EnemyHp,         n);
            Array.Copy(src.EnemyMaxHp,      EnemyMaxHp,      n);
            Array.Copy(src.EnemyBlock,      EnemyBlock,      n);
            Array.Copy(src.EnemyIntentDmg,  EnemyIntentDmg,  n);
            Array.Copy(src.EnemyIntentHits, EnemyIntentHits, n);
            Array.Copy(src.EnemyIntent,     EnemyIntent,     n);
            // Flat power matrix: copy n rows of PowersPerCre shorts contiguously.
            Array.Copy(src.EnemyPowers, EnemyPowers, n * PowersPerCre);
        }

        // Piles.
        if (src.HandCount    > 0) Array.Copy(src.Hand,    Hand,    src.HandCount);
        if (src.DrawCount    > 0) Array.Copy(src.Draw,    Draw,    src.DrawCount);
        if (src.DiscCount    > 0) Array.Copy(src.Disc,    Disc,    src.DiscCount);
        if (src.ExhaustCount > 0) Array.Copy(src.Exhaust, Exhaust, src.ExhaustCount);
    }

    /// <summary>
    /// Zero counts, the player-power vector, all enemy-power rows, the enemy
    /// intent vector, and the RNG state. Used by <c>Snapshot</c> at the start
    /// of a fresh capture, and by the state pool when recycling. Pile arrays
    /// past the (now-zero) counts are unreachable, so we deliberately do not
    /// Array.Clear them.
    /// </summary>
    public void Reset()
    {
        Round = 0;
        PlayerHp = PlayerMaxHp = PlayerBlock = 0;
        Energy = MaxEnergy = 0;
        EnemyCount = 0;
        HandCount = DrawCount = DiscCount = ExhaustCount = 0;
        RngState = default;
        Array.Clear(PlayerPowers, 0, PowersPerCre);
        Array.Clear(EnemyPowers,  0, EnemyCap * PowersPerCre);
        Array.Clear(EnemyIntent,  0, EnemyCap);
    }
}
