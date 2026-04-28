using System.Collections.Generic;

namespace STS2.Agent.Sim;

/// <summary>
/// Hot data for one combat node. Designed for cheap deep-copy during DFS:
///   - All collections are List&lt;int&gt; / int[]: contiguous memory, no GC pressure.
///   - No references to game objects (CardModel/Creature/etc.). Card behavior
///     is dispatched through SimCardDb keyed by int CardId.
///   - Powers are fixed-size int[] indexed by SimPowerType.* constants —
///     single L1 lookup per query, vs ~20-50 cycles for Dictionary.
///
/// Layout-only at this stage. No methods beyond field initialization.
/// </summary>
internal sealed class SimCombatState
{
    // ── Turn / timing ─────────────────────────────────────────────────────────
    public int Round;

    // ── Player hot stats ──────────────────────────────────────────────────────
    public int PlayerHp;
    public int PlayerMaxHp;
    public int PlayerBlock;
    public int Energy;
    public int MaxEnergy;

    /// <summary>Indexed by SimPowerType.*. Size = SimPowerType.Count.</summary>
    public readonly int[] PlayerPowers = new int[SimPowerType.Count];

    // ── Enemies (parallel arrays — cheaper than List<SimCreature>) ───────────
    /// <summary>Number of valid entries in the enemy arrays.</summary>
    public int EnemyCount;

    public readonly int[]   EnemyHp        = new int[8];   // STS2 max enemies/room
    public readonly int[]   EnemyMaxHp     = new int[8];
    public readonly int[]   EnemyBlock     = new int[8];
    public readonly int[][] EnemyPowers    = new int[8][]; // each = int[SimPowerType.Count]

    /// <summary>Pre-resolved attack damage this enemy will deal this turn (0 if non-attack).</summary>
    public readonly int[]   EnemyIntentDmg = new int[8];
    /// <summary>Number of hits in the attack intent (1 = single).</summary>
    public readonly int[]   EnemyIntentHits = new int[8];

    // ── Card piles (CardId ints; behavior is in SimCardDb) ────────────────────
    public readonly List<int> Hand    = new(16);
    public readonly List<int> Draw    = new(40);
    public readonly List<int> Disc    = new(40);
    public readonly List<int> Exhaust = new(20);

    // ── RNG (cloned from real game's Rng.Shuffle) ─────────────────────────────
    public uint RngSeed;
    public int  RngCounter;

    public SimCombatState()
    {
        for (int i = 0; i < EnemyPowers.Length; i++)
            EnemyPowers[i] = new int[SimPowerType.Count];
    }
}
