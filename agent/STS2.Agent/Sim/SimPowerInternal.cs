using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Per-creature mirror of the private mutable counters that ~12 PowerModel
/// subclasses keep beyond their public <c>Amount</c>. The base power matrix
/// (<see cref="SimCombatState.PlayerPowers"/> / <c>EnemyPowers</c> /
/// <c>OstyPowers</c>) only stores <c>Amount</c>; this struct captures the
/// extra hidden state that gates each power's trigger condition.
///
/// Why a sparse named struct instead of a parallel <c>short[259]</c> matrix:
/// only ~12 powers carry hidden state, of 259 total. A parallel matrix
/// would burn 259×2 B = 518 B per creature × 8 creatures = 4144 B, of which
/// 97% is permanently zero. This packed layout is 14 B × 8 = 112 B — a 37×
/// reduction in memory bandwidth on every <c>CopyFrom</c> during DFS.
///
/// Layout convention:
///   • Per-turn-reset fields first (cleared at SideTurnStart by the sim
///     engine when stepping turn boundaries). Cache-locality matters: the
///     turn-end clear loop touches just the first 7 bytes.
///   • Persistent-per-combat fields next (Outbreak / Orbit / Automation).
///   • Bit flags last in <see cref="Flags"/>: Illusion / Nemesis / Ritual
///     are all single bits.
///
/// Mapped fields (one per relevant power):
///   <see cref="FeralZeroCostAttacks"/>     ← FeralPower.Data.zeroCostAttacksPlayed
///   <see cref="JugglingAttacksThisTurn"/>  ← JugglingPower.Data.attacksPlayedThisTurn
///   <see cref="VoidFormCardsThisTurn"/>    ← VoidFormPower.Data.cardsPlayedThisTurn
///   <see cref="TenderCardsThisTurn"/>      ← TenderPower._cardsPlayedThisTurn (direct)
///   <see cref="SlothCardsThisTurn"/>       ← SlothPower._cardsPlayedThisTurn (direct)
///   <see cref="HardenedShellDamageThisTurn"/> ← HardenedShellPower.Data.damageReceivedThisTurn (decimal → ushort)
///   <see cref="OutbreakTimesPoisoned"/>    ← OutbreakPower.Data.timesPoisoned (mod 3)
///   <see cref="OrbitEnergySpent"/>         ← OrbitPower.Data.energySpent
///   <see cref="OrbitTriggerCount"/>        ← OrbitPower.Data.triggerCount
///   <see cref="AutomationCardsLeft"/>      ← AutomationPower.Data.cardsLeft (init 10)
///   <see cref="Flags"/>                    ← Illusion.isReviving | Nemesis._shouldApplyIntangible | Ritual._wasJustAppliedByEnemy
///
/// Explicitly NOT mapped (rationale per field):
///   • SandpitPower._initialAmount / _initialTargetPosition — animation tween
///     captures, no gameplay effect on damage / triggers.
///   • BeaconOfHopePower._hasAlreadyBeenGivenBlock — re-entry guard, lives
///     for one synchronous Block-gain event; sim doesn't recurse so the
///     guard is always false at observable boundaries.
///   • ConfusedPower._testEnergyCostOverride — TestMode-only field.
///   • IllusionPower._followUpStateId — string; ties into MonsterMoveStateMachine
///     which we don't model yet. Belongs in a future MoveState slice.
///   • TemporaryFocusPower / TemporaryStrengthPower / TemporaryDexterityPower
///     <c>_shouldIgnoreNextInstance</c> — TODO: revisit once we wire the
///     PowerCmd.Apply path; current Snapshot path doesn't trigger it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 14)]
internal struct SimPowerInternal
{
    // ── Per-turn reset block (7 bytes) ───────────────────────────────────────
    /// <summary>FeralPower: zero-cost attacks played this turn (uses up Amount quota).</summary>
    public byte   FeralZeroCostAttacks;
    /// <summary>JugglingPower: attacks played this turn; clones at == 3.</summary>
    public byte   JugglingAttacksThisTurn;
    /// <summary>VoidFormPower: cards played this turn; first Amount cards are free.</summary>
    public byte   VoidFormCardsThisTurn;
    /// <summary>TenderPower: cards played this turn; applies -Str/-Dex equal to count at turn end.</summary>
    public byte   TenderCardsThisTurn;
    /// <summary>SlothPower: cards played this turn; gates further plays once == Amount.</summary>
    public byte   SlothCardsThisTurn;
    /// <summary>HardenedShellPower: unblocked damage absorbed this turn (game stores as decimal; clamped to ushort here — far above any realistic single-turn intake).</summary>
    public ushort HardenedShellDamageThisTurn;

    // ── Persistent per-combat block (5 bytes) ────────────────────────────────
    /// <summary>OutbreakPower: poison applications mod 3; triggers AOE every 3rd.</summary>
    public byte   OutbreakTimesPoisoned;
    /// <summary>OrbitPower: cumulative energy spent this combat.</summary>
    public ushort OrbitEnergySpent;
    /// <summary>OrbitPower: number of energy refunds already issued (so we know how many more to fire as energySpent grows).</summary>
    public ushort OrbitTriggerCount;
    /// <summary>AutomationPower: cards remaining in the current 10-draw cycle (cycles back to 10 on hit-zero).</summary>
    public byte   AutomationCardsLeft;

    // ── Bit flags (1 byte; 3 bits used, 5 reserved) ──────────────────────────
    /// <summary>Bit-packed boolean state. See <c>Flag*</c> constants below.</summary>
    public byte   Flags;

    /// <summary>IllusionPower.isReviving — set during AfterDeath, cleared on revive complete; blocks hits while true.</summary>
    public const byte FlagIllusionIsReviving        = 0x01;
    /// <summary>NemesisPower._shouldApplyIntangible — toggles each owner-side turn end; on=apply Intangible, off=remove.</summary>
    public const byte FlagNemesisShouldApplyIntangible = 0x02;
    /// <summary>RitualPower._wasJustAppliedByEnemy — set true on AfterApplied (enemy-owned), cleared on first AfterTurnEnd to suppress same-turn Strength gain.</summary>
    public const byte FlagRitualWasJustAppliedByEnemy = 0x04;
}
