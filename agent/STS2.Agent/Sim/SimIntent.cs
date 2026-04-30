namespace STS2.Agent.Sim;

/// <summary>
/// One-byte enemy intent kind, stored in <c>SimCombatState.EnemyIntent[i]</c>.
///
/// Mirrors the game's <c>MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType</c>
/// (verified 2026-04). Values are explicit and 1:1 with the game enum so a
/// simple cast — <c>(byte)gameIntent.IntentType</c> — works during FromReal
/// snapshot, with no lookup table.
///
/// Backing type is <c>byte</c> so the per-combat array is 6 bytes (one cache
/// line shared with surrounding intent fields). Today only Attack and
/// DeathBlow carry numeric data (in <c>EnemyIntentDmg</c> / <c>EnemyIntentHits</c>);
/// the rest are tag-only and their numeric payloads (block amount, buff stacks,
/// debuff stacks, heal amount) are NOT yet captured. See SimCombatState
/// docs and the staged plan for the monster-move database that will fill those
/// in later.
/// </summary>
internal enum SimIntent : byte
{
    /// <summary>Default / no intent visible (matches IntentType.Unknown).</summary>
    Unknown      = 0,

    /// <summary>SingleAttackIntent / MultiAttackIntent. <see cref="SimCombatState.EnemyIntentDmg"/> + <see cref="SimCombatState.EnemyIntentHits"/> are valid.</summary>
    Attack       = 1,

    /// <summary>BuffIntent: enemy buffs itself. Buff type & stacks not yet captured.</summary>
    Buff         = 2,

    /// <summary>DebuffIntent (non-strong): enemy debuffs the player. Stacks not yet captured.</summary>
    Debuff       = 3,

    /// <summary>DebuffIntent with strong=true. Stacks not yet captured.</summary>
    DebuffStrong = 4,

    /// <summary>DefendIntent: enemy gains block. Block amount not yet captured.</summary>
    Defend       = 5,

    /// <summary>EscapeIntent: enemy will leave combat next action.</summary>
    Escape       = 6,

    /// <summary>HealIntent: enemy heals self. Heal amount not yet captured.</summary>
    Heal         = 7,

    /// <summary>HiddenIntent: deliberately obscured (UI shows ?). Treat as Unknown.</summary>
    Hidden       = 8,

    /// <summary>SummonIntent: spawns one or more new creatures. Identity & count not yet captured.</summary>
    Summon       = 9,

    /// <summary>SleepIntent: skip-turn marker (typically with AsleepPower).</summary>
    Sleep        = 10,

    /// <summary>StunIntent: skip-turn marker (different visual from Sleep).</summary>
    Stun         = 11,

    /// <summary>StatusIntent: shuffles a status card (Slimed/etc.) into the player's pile.</summary>
    StatusCard   = 12,

    /// <summary>CardDebuffIntent: shuffles a curse card into the player's pile.</summary>
    CardDebuff   = 13,

    /// <summary>DeathBlowIntent: subclass of attack that triggers special on-kill logic. <see cref="SimCombatState.EnemyIntentDmg"/> + <see cref="SimCombatState.EnemyIntentHits"/> are valid.</summary>
    DeathBlow    = 14,
}
