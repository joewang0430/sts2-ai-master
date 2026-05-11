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
    public const int HandCap = CardPile.maxCardsInHand;
    public const int PileCap = 200;
    public const int CardInstanceCap = HandCap + (PileCap * 3) + 1;
    public const int CardEnergyModifierCap = 2048 + 32;
    public const int CardTemporaryStarCostCap = 2048;
    public const int PowersPerCre = SimPowerType.Count;
}