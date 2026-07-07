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
}