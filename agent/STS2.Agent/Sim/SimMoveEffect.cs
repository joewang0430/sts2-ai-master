using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>Kind of numeric payload one <see cref="SimMoveEffect"/> slot carries.</summary>
internal enum SimMoveEffectKind : byte
{
    None = 0,
    /// <summary>Block gained. <c>Amount</c> = block; <c>PowerType</c> unused.</summary>
    Block = 1,
    /// <summary>A power applied. <c>PowerType</c> = <see cref="SimPowerType"/> index; <c>Amount</c> = stacks.</summary>
    PowerApply = 2,
    /// <summary>HP healed. <c>Amount</c> = heal amount; <c>PowerType</c> unused.</summary>
    Heal = 3,
    /// <summary>A creature summoned. <c>PowerType</c> field repurposed to hold a
    /// <see cref="SimSummonTargetId"/> instead of a power index; <c>Amount</c> = how many.</summary>
    Summon = 4,
    /// <summary>A fresh Status/Curse card minted into the PLAYER's pile — always the player, never
    /// self (matches every known StatusIntent monster move: <c>CardPileCmd.AddToCombatAndPreview</c>
    /// always targets the move's <c>targets</c> param, i.e. the player side). <c>PowerType</c> field
    /// repurposed to hold a <see cref="SimCardId"/> instead of a power index; <c>Amount</c> = how
    /// many copies. Only covers the fixed-position "append to Discard" shape — moves that insert at
    /// a random pile position or target Hand/Draw instead of Discard aren't covered by this kind yet
    /// (see dev_docs/Enemy_Intent_Payload_Backlog.md).</summary>
    CardInject = 5,
    /// <summary>Same as <see cref="CardInject"/> (fixed count, appended to the bottom) but targets
    /// the player's HAND instead of Discard — a couple of StatusIntent moves
    /// (Myte's TOXIC_MOVE, MechaKnight's FLAMETHROWER_MOVE) call
    /// <c>CardPileCmd.AddToCombatAndPreview&lt;T&gt;(targets, PileType.Hand, count, null)</c> instead
    /// of the far more common <c>PileType.Discard</c>. A separate Kind rather than a third
    /// <see cref="SimMoveEffect"/> field, to avoid growing the struct for a two-monster case.</summary>
    CardInjectHand = 6,
    /// <summary>Same as <see cref="CardInject"/> but at a RANDOM position in the player's DRAW pile
    /// instead of appended to Discard — mirrors <c>CardPilePosition.Random</c>'s game-source
    /// resolution (<c>Rng.Shuffle.NextInt(pile.Count + 1)</c>, re-rolled per card when
    /// <c>Amount</c> &gt; 1). Consumes the <see cref="SimRngSlot.Shuffle"/> stream.</summary>
    CardInjectDrawRandom = 7,
    /// <summary>Same as <see cref="CardInjectDrawRandom"/> but targets Discard instead of Draw
    /// (e.g. TheInsatiable's LIQUIFY_GROUND_MOVE splits into a Draw-random half and a
    /// Discard-random half).</summary>
    CardInjectDiscardRandom = 8,
}

/// <summary>
/// One numeric effect slot for a creature's currently-telegraphed (not-yet-executed) move — the
/// data <c>AttackIntent.DamageCalc</c> already exposes for attacks, hand-replicated per monster
/// for Defend/Buff/Debuff/Heal via <see cref="SimMonsterMoveEffects"/> since the game itself
/// exposes no generic "peek without executing" path for those (see dev_docs/Enemy_Intent_Payload_Backlog.md).
///
/// A single move can carry more than one effect (e.g. Axebot's boot-up move gains block AND
/// Strength in the same turn) — see <see cref="CombatSimLayout.MoveEffectCap"/> for the per-creature
/// slot count.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 5)]
internal struct SimMoveEffect
{
    public byte Kind;        // SimMoveEffectKind
    public ushort PowerType; // SimPowerType index when Kind == PowerApply; SimSummonTargetId when Kind == Summon; SimCardId when Kind == CardInject
    public short Amount;
}
