using System;
using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Versioned byte layout for the first parallel NodeBlob prototype.
///
/// V1 currently carries the full card slice, the player hot scalar block, and
/// the enemy hot scalar+intent block. This gives us one precise, frozen layout
/// for the highest-traffic state we need first without forcing an all-at-once
/// rewrite of the rest of SimCombatState.
/// </summary>
internal static class CombatSchemaV1
{
    public const int Version = 1;

    public static readonly int SimCardSize = Unsafe.SizeOf<SimCard>();
    public static readonly int SimLocalCostModifierSize = Unsafe.SizeOf<SimLocalCostModifier>();
    public static readonly int TotalBytes = Enemies.TotalBytes;

    static CombatSchemaV1()
    {
        if (SimCardSize != 13)
        {
            throw new InvalidOperationException(
                $"CombatSchemaV1: expected SimCard size 13, got {SimCardSize}. " +
                "Card hot-core layout drifted; re-evaluate blob offsets before proceeding.");
        }

        if (SimLocalCostModifierSize != 2)
        {
            throw new InvalidOperationException(
            $"CombatSchemaV1: expected SimLocalCostModifier size 2, got {SimLocalCostModifierSize}. " +
                "Energy sidecar layout drifted; re-evaluate blob offsets before proceeding.");
        }
    }

    public static class Cards
    {
        public const int HandCap = SimCombatState.HandCap;
        public const int PileCap = SimCombatState.PileCap;
        public const int CardInstanceCap = SimCombatState.CardInstanceCap;
        public const int CardEnergyModifierCap = SimCombatState.CardEnergyModifierCap;

        public static readonly int HandBytes = HandCap * CombatSchemaV1.SimCardSize;
        public static readonly int DrawBytes = PileCap * CombatSchemaV1.SimCardSize;
        public static readonly int DiscBytes = PileCap * CombatSchemaV1.SimCardSize;
        public static readonly int ExhaustBytes = PileCap * CombatSchemaV1.SimCardSize;

        public static readonly int CountsBytes = sizeof(ushort) * 6; // hand/draw/disc/exhaust + instanceCount + modifierUsed
        public static readonly int EnergyBaseBytes = (CardInstanceCap + 1) * sizeof(short);
        public static readonly int EnergyCapturedXBytes = (CardInstanceCap + 1) * sizeof(ushort);
        public static readonly int EnergyModifierStartBytes = (CardInstanceCap + 1) * sizeof(ushort);
        public static readonly int EnergyModifierCountBytes = (CardInstanceCap + 1) * sizeof(ushort);
        public static readonly int EnergyModifierBytes = CardEnergyModifierCap * CombatSchemaV1.SimLocalCostModifierSize;

        public static readonly int HandOffset;
        public static readonly int DrawOffset;
        public static readonly int DiscOffset;
        public static readonly int ExhaustOffset;
        public static readonly int HandCountOffset;
        public static readonly int DrawCountOffset;
        public static readonly int DiscCountOffset;
        public static readonly int ExhaustCountOffset;
        public static readonly int CardInstanceCountOffset;
        public static readonly int CardEnergyModifierUsedOffset;
        public static readonly int CardEnergyBaseOffset;
        public static readonly int CardEnergyCapturedXOffset;
        public static readonly int CardEnergyModifierStartOffset;
        public static readonly int CardEnergyModifierCountOffset;
        public static readonly int CardEnergyModifiersOffset;
        public static readonly int TotalBytes;

        static Cards()
        {
            int offset = 0;

            HandOffset = offset;
            offset += HandBytes;

            DrawOffset = offset;
            offset += DrawBytes;

            DiscOffset = offset;
            offset += DiscBytes;

            ExhaustOffset = offset;
            offset += ExhaustBytes;

            offset = AlignUp(offset, sizeof(ushort));

            HandCountOffset = offset;
            offset += sizeof(ushort);

            DrawCountOffset = offset;
            offset += sizeof(ushort);

            DiscCountOffset = offset;
            offset += sizeof(ushort);

            ExhaustCountOffset = offset;
            offset += sizeof(ushort);

            CardInstanceCountOffset = offset;
            offset += sizeof(ushort);

            CardEnergyModifierUsedOffset = offset;
            offset += sizeof(ushort);

            CardEnergyBaseOffset = offset;
            offset += EnergyBaseBytes;

            CardEnergyCapturedXOffset = offset;
            offset += EnergyCapturedXBytes;

            CardEnergyModifierStartOffset = offset;
            offset += EnergyModifierStartBytes;

            CardEnergyModifierCountOffset = offset;
            offset += EnergyModifierCountBytes;

            CardEnergyModifiersOffset = offset;
            offset += EnergyModifierBytes;

            TotalBytes = offset;
        }
    }

    public static class Player
    {
        public static readonly int PlayerPowersBytes = SimCombatState.PowersPerCre * sizeof(short);

        public static readonly int RoundOffset;
        public static readonly int PlayerHpOffset;
        public static readonly int PlayerMaxHpOffset;
        public static readonly int PlayerBlockOffset;
        public static readonly int EnergyOffset;
        public static readonly int MaxEnergyOffset;
        public static readonly int PlayerStarsOffset;
        public static readonly int PlayerPowersOffset;
        public static readonly int TotalBytes;

        static Player()
        {
            int offset = Cards.TotalBytes;

            RoundOffset = offset;
            offset += sizeof(byte);

            offset = AlignUp(offset, sizeof(ushort));

            PlayerHpOffset = offset;
            offset += sizeof(ushort);

            PlayerMaxHpOffset = offset;
            offset += sizeof(ushort);

            PlayerBlockOffset = offset;
            offset += sizeof(ushort);

            EnergyOffset = offset;
            offset += sizeof(ushort);

            MaxEnergyOffset = offset;
            offset += sizeof(ushort);

            PlayerStarsOffset = offset;
            offset += sizeof(ushort);

            PlayerPowersOffset = offset;
            offset += PlayerPowersBytes;

            TotalBytes = offset;
        }
    }

    public static class Enemies
    {
        public const int EnemyCap = SimCombatState.EnemyCap;

        public static readonly int EnemyHpBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyMaxHpBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyBlockBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyIntentDmgBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyIntentHitsBytes = EnemyCap * sizeof(byte);
        public static readonly int EnemyIntentBytes = EnemyCap * sizeof(byte);

        public static readonly int EnemyCountOffset;
        public static readonly int EnemyHpOffset;
        public static readonly int EnemyMaxHpOffset;
        public static readonly int EnemyBlockOffset;
        public static readonly int EnemyIntentDmgOffset;
        public static readonly int EnemyIntentHitsOffset;
        public static readonly int EnemyIntentOffset;
        public static readonly int TotalBytes;

        static Enemies()
        {
            int offset = Player.TotalBytes;

            EnemyCountOffset = offset;
            offset += sizeof(byte);

            offset = AlignUp(offset, sizeof(ushort));

            EnemyHpOffset = offset;
            offset += EnemyHpBytes;

            EnemyMaxHpOffset = offset;
            offset += EnemyMaxHpBytes;

            EnemyBlockOffset = offset;
            offset += EnemyBlockBytes;

            EnemyIntentDmgOffset = offset;
            offset += EnemyIntentDmgBytes;

            EnemyIntentHitsOffset = offset;
            offset += EnemyIntentHitsBytes;

            EnemyIntentOffset = offset;
            offset += EnemyIntentBytes;

            TotalBytes = offset;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment)
        => (value + alignment - 1) & ~(alignment - 1);
}