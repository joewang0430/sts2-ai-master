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
    public ushort PowerType; // SimPowerType index when Kind == PowerApply; SimSummonTargetId when Kind == Summon
    public short Amount;
}
