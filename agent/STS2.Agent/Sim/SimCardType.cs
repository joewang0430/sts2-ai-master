namespace STS2.Agent.Sim;

/// <summary>
/// Mirrors the game's <c>MegaCrit.Sts2.Core.Entities.Cards.CardType</c> 1:1, in the same
/// declaration order, so a simple cast — <c>(SimCardType)(byte)card.Type</c> — works when building
/// <see cref="SimCardTypeRegistry"/>, no lookup table needed for the enum mapping itself (same
/// idiom as <see cref="SimCardTargetType"/> for TargetType).
///
/// Like TargetType, CardType is a property of the card TYPE (a per-class virtual getter), never
/// per-instance mutable state — resolved once via <see cref="SimCardTypeRegistry"/>, indexed by
/// <see cref="SimCardId"/>, never touches the per-node blob.
/// </summary>
internal enum SimCardType : byte
{
    None = 0,
    Attack = 1,
    Skill = 2,
    Power = 3,
    Status = 4,
    Curse = 5,
    Quest = 6,
}
