using System;
using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Versioned byte layout for the first parallel NodeBlob prototype.
///
/// V1 currently carries the full card slice, the player scalar/power/orb/pet
/// block, the enemy hot scalar+intent+power+move-state block, and the combat
/// RNG block. This gives us one frozen byte layout for the state slices that
/// already exist in the live snapshot path.
/// </summary>
internal static class CombatSchemaV1
{
    public const int Version = 1;

    public static readonly int SimCardSize = Unsafe.SizeOf<SimCard>();
    public static readonly int SimLocalCostModifierSize = Unsafe.SizeOf<SimLocalCostModifier>();
    public static readonly int SimPowerInternalSize = Unsafe.SizeOf<SimPowerInternal>();
    public static readonly int SimEnemyMoveSMSize = Unsafe.SizeOf<SimEnemyMoveSM>();
    public static readonly int SimPetSize = Unsafe.SizeOf<SimPet>();
    public static readonly int RandomStateSize = Unsafe.SizeOf<RandomState>();
    public static readonly int RandomStateBufferSize = Unsafe.SizeOf<RandomStateBuffer>();
    public static readonly int TotalBytes = Runtime.TotalBytes;

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

        if (SimPowerInternalSize != 14)
        {
            throw new InvalidOperationException(
                $"CombatSchemaV1: expected SimPowerInternal size 14, got {SimPowerInternalSize}. " +
                "Power-internal layout drifted; re-evaluate blob offsets before proceeding.");
        }

        if (SimEnemyMoveSMSize != 25)
        {
            throw new InvalidOperationException(
                $"CombatSchemaV1: expected SimEnemyMoveSM size 25, got {SimEnemyMoveSMSize}. " +
                "Enemy move-state layout drifted; re-evaluate blob offsets before proceeding.");
        }

        if (SimPetSize != 8)
        {
            throw new InvalidOperationException(
                $"CombatSchemaV1: expected SimPet size 8, got {SimPetSize}. " +
                "Pet layout drifted; re-evaluate blob offsets before proceeding.");
        }

        if (RandomStateBufferSize != RandomStateSize * (int)SimRngSlot.Count)
        {
            throw new InvalidOperationException(
                $"CombatSchemaV1: RandomStateBuffer size {RandomStateBufferSize} does not equal {RandomStateSize} * {SimRngSlot.Count}. " +
                "RNG buffer layout drifted; re-evaluate blob offsets before proceeding.");
        }

        if (Player.PlayerPowersBytes != Enemies.EnemyPowersBytes / Enemies.EnemyCap)
        {
            throw new InvalidOperationException(
                "CombatSchemaV1: player/enemy power row widths drifted apart. " +
                "Re-evaluate blob power offsets before proceeding.");
        }
    }

    public static class Cards
    {
        public const int HandCap = CombatSimLayout.HandCap;
        public const int PileCap = CombatSimLayout.PileCap;
        public const int CardInstanceCap = CombatSimLayout.CardInstanceCap;
        public const int CardEnergyModifierCap = CombatSimLayout.CardEnergyModifierCap;

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
        public static readonly int PlayerPowersBytes = CombatSimLayout.PowersPerCre * sizeof(short);
        public static readonly int PlayerPowerInternalBytes = CombatSchemaV1.SimPowerInternalSize;

        public static readonly int RoundOffset;
        public static readonly int PlayerHpOffset;
        public static readonly int PlayerMaxHpOffset;
        public static readonly int PlayerBlockOffset;
        public static readonly int EnergyOffset;
        public static readonly int MaxEnergyOffset;
        public static readonly int PlayerStarsOffset;
        public static readonly int PlayerPowersOffset;
        public static readonly int PlayerPowerInternalOffset;
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

            PlayerPowerInternalOffset = offset;
            offset += PlayerPowerInternalBytes;

            TotalBytes = offset;
        }
    }

    public static class Enemies
    {
        public const int EnemyCap = CombatSimLayout.EnemyCap;

        public static readonly int EnemyHpBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyMaxHpBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyBlockBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyIntentDmgBytes = EnemyCap * sizeof(ushort);
        public static readonly int EnemyIntentHitsBytes = EnemyCap * sizeof(byte);
        public static readonly int EnemyIntentBytes = EnemyCap * sizeof(byte);
        public static readonly int EnemyPowersBytes = EnemyCap * CombatSimLayout.PowersPerCre * sizeof(short);
        public static readonly int EnemyPowerInternalBytes = EnemyCap * CombatSchemaV1.SimPowerInternalSize;
        public static readonly int EnemyMoveSmBytes = EnemyCap * CombatSchemaV1.SimEnemyMoveSMSize;
        public static readonly int EnemyMoveTableHandleBytes = EnemyCap * sizeof(ushort);

        public static readonly int EnemyCountOffset;
        public static readonly int EnemyHpOffset;
        public static readonly int EnemyMaxHpOffset;
        public static readonly int EnemyBlockOffset;
        public static readonly int EnemyIntentDmgOffset;
        public static readonly int EnemyIntentHitsOffset;
        public static readonly int EnemyIntentOffset;
        public static readonly int EnemyPowersOffset;
        public static readonly int EnemyPowerInternalOffset;
        public static readonly int EnemyMoveSmOffset;
        public static readonly int EnemyMoveTableHandleOffset;
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

            offset = AlignUp(offset, sizeof(short));

            EnemyPowersOffset = offset;
            offset += EnemyPowersBytes;

            EnemyPowerInternalOffset = offset;
            offset += EnemyPowerInternalBytes;

            EnemyMoveSmOffset = offset;
            offset += EnemyMoveSmBytes;

            offset = AlignUp(offset, sizeof(ushort));

            EnemyMoveTableHandleOffset = offset;
            offset += EnemyMoveTableHandleBytes;

            TotalBytes = offset;
        }
    }

    public static class Runtime
    {
        public static readonly int OrbSlotsBytes = 10 * sizeof(ushort);
        public static readonly int OstyBytes = CombatSchemaV1.SimPetSize;
        public static readonly int OstyPowersBytes = CombatSimLayout.PowersPerCre * sizeof(short);
        public static readonly int OstyPowerInternalBytes = CombatSchemaV1.SimPowerInternalSize;
        public static readonly int RngBytes = CombatSchemaV1.RandomStateBufferSize;

        public static readonly int OrbSlotsOffset;
        public static readonly int OrbCountOffset;
        public static readonly int OrbCapacityOffset;
        public static readonly int OstyOffset;
        public static readonly int OstyPowersOffset;
        public static readonly int OstyPowerInternalOffset;
        public static readonly int RngOffset;
        public static readonly int TotalBytes;

        static Runtime()
        {
            int offset = Enemies.TotalBytes;

            offset = AlignUp(offset, sizeof(ushort));

            OrbSlotsOffset = offset;
            offset += OrbSlotsBytes;

            OrbCountOffset = offset;
            offset += sizeof(byte);

            OrbCapacityOffset = offset;
            offset += sizeof(byte);

            offset = AlignUp(offset, sizeof(ushort));

            OstyOffset = offset;
            offset += OstyBytes;

            offset = AlignUp(offset, sizeof(short));

            OstyPowersOffset = offset;
            offset += OstyPowersBytes;

            OstyPowerInternalOffset = offset;
            offset += OstyPowerInternalBytes;

            offset = AlignUp(offset, sizeof(int));

            RngOffset = offset;
            offset += RngBytes;

            TotalBytes = offset;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment)
        => (value + alignment - 1) & ~(alignment - 1);
}