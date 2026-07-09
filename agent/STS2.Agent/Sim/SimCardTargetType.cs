namespace STS2.Agent.Sim;

/// <summary>
/// Mirrors the game's <c>MegaCrit.Sts2.Core.Entities.Cards.TargetType</c> 1:1, in the same
/// declaration order, so a simple cast — <c>(SimCardTargetType)(byte)card.TargetType</c> — works
/// when building <see cref="SimCardTargetTypeRegistry"/>, with no lookup table needed for the
/// enum mapping itself (same idiom as <see cref="SimIntent"/> for the game's IntentType).
///
/// Unlike per-instance card state (<see cref="SimCard"/>), TargetType is a property of the card
/// TYPE (<c>CardModel.TargetType</c> is a per-class virtual getter, not per-instance mutable
/// state) — it never changes between combats, between copies of the same card, or mid-combat.
/// So it is resolved once via <see cref="SimCardTargetTypeRegistry"/>, indexed by
/// <see cref="SimCardId"/>, and never touches the per-node blob at all.
/// </summary>
internal enum SimCardTargetType : byte
{
    None = 0,
    /// <summary>Can only target self. No target selection.</summary>
    Self = 1,
    /// <summary>Can target any enemy. Target selection determines which one.</summary>
    AnyEnemy = 2,
    /// <summary>Targets all enemies. No target selection.</summary>
    AllEnemies = 3,
    /// <summary>Targets a random enemy. No target selection.</summary>
    RandomEnemy = 4,
    /// <summary>Targets any player (multiplayer only relevant; singleplayer has one player).</summary>
    AnyPlayer = 5,
    /// <summary>Targets any ally other than self (multiplayer only).</summary>
    AnyAlly = 6,
    /// <summary>Targets all allies. No target selection.</summary>
    AllAllies = 7,
    /// <summary>Target selection performed but no creature target expected (e.g. FoulPotion → merchant).</summary>
    TargetedNoCreature = 8,
    /// <summary>Targets the local player's Osty.</summary>
    Osty = 9,
}
