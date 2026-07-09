using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Fixed combat-layout caps shared by the blob snapshot and validation paths.
/// These remain as hard bounds after the legacy state container is gone.
/// </summary>
internal static class CombatSimLayout
{
    public const int EnemyCap = 6;
    public const int PotionSlotCap = 10;
    public static readonly int HandCap = CardPile.MaxCardsInHand;
    public const int PileCap = 100;
    public static readonly int CardInstanceCap = HandCap + (PileCap * 3) + 1;
    public const int CardEnergyModifierCap = 256;
    public const int CardTemporaryStarCostCap = 256;
    public const int PowerValueCap = 32;

    /// <summary>Max simultaneously-held relics. No authoritative game-side cap exists (unlike
    /// HandCap); this is a judgment call with generous headroom, backed by a runtime throw-on-
    /// overflow guard in <see cref="CombatNodeBlobSnapshot"/> rather than a startup assertion.
    /// Cost of being generous here is negligible: identity is stored once per combat in
    /// <see cref="CombatRootRelics"/>, not once per search node.</summary>
    public const int RelicCap = 64;

    /// <summary>Max simultaneously-held relics that carry a per-combat counter (see
    /// <see cref="SimRelicDb.HasCounter"/>) — a small fraction of <see cref="RelicCap"/>, since
    /// most relics are static stat modifiers. This one DOES cost space per search node (see
    /// <see cref="CombatNodeBlob.RelicCounters"/>), so it is kept tight relative to RelicCap.</summary>
    public const int RelicCounterCap = 16;

    /// <summary>Max simultaneous numeric effect slots per creature's currently-telegraphed move
    /// (see <see cref="SimMoveEffect"/>). Observed max in practice is 2 (e.g. Axebot's boot-up
    /// move gains block AND Strength); 4 leaves headroom.</summary>
    public const int MoveEffectCap = 4;
}