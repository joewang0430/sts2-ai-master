using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Transitional bridge: copies the currently-audited V1 slices from the legacy
/// <see cref="SimCombatState"/> into the first blob schema.
///
/// The goal of this step is not to replace SimCombatState yet. The goal is to
/// freeze offsets/caps in code and prove that one contiguous blob can carry the
/// exact hot data we have already audited.
/// </summary>
internal static class CombatNodeBlobSnapshot
{
    public static void WriteV1FromSim(SimCombatState src, CombatNodeBlob dst)
    {
        dst.Clear();

        dst.Round = src.Round;
        dst.PlayerHp = src.PlayerHp;
        dst.PlayerMaxHp = src.PlayerMaxHp;
        dst.PlayerBlock = src.PlayerBlock;
        dst.Energy = src.Energy;
        dst.MaxEnergy = src.MaxEnergy;
        dst.PlayerStars = src.PlayerStars;
        dst.PlayerPowerInternal = src.PlayerPowerInternal;
        dst.EnemyCount = CheckedCountByte(src.EnemyCount, CombatSchemaV1.Enemies.EnemyCap, nameof(src.EnemyCount));
        src.PlayerPowers.AsSpan(0, SimCombatState.PowersPerCre).CopyTo(dst.PlayerPowers);

        dst.HandCount = CheckedCount(src.HandCount, CombatSchemaV1.Cards.HandCap, nameof(src.HandCount));
        dst.DrawCount = CheckedCount(src.DrawCount, CombatSchemaV1.Cards.PileCap, nameof(src.DrawCount));
        dst.DiscCount = CheckedCount(src.DiscCount, CombatSchemaV1.Cards.PileCap, nameof(src.DiscCount));
        dst.ExhaustCount = CheckedCount(src.ExhaustCount, CombatSchemaV1.Cards.PileCap, nameof(src.ExhaustCount));
        dst.CardInstanceCount = CheckedCount(src.CardInstanceCount, CombatSchemaV1.Cards.CardInstanceCap, nameof(src.CardInstanceCount));
        dst.CardEnergyModifierUsed = CheckedCount(src.CardEnergyModifierUsed, CombatSchemaV1.Cards.CardEnergyModifierCap, nameof(src.CardEnergyModifierUsed));

        if (src.HandCount > 0) src.Hand.AsSpan(0, src.HandCount).CopyTo(dst.HandCards);
        if (src.DrawCount > 0) src.Draw.AsSpan(0, src.DrawCount).CopyTo(dst.DrawCards);
        if (src.DiscCount > 0) src.Disc.AsSpan(0, src.DiscCount).CopyTo(dst.DiscCards);
        if (src.ExhaustCount > 0) src.Exhaust.AsSpan(0, src.ExhaustCount).CopyTo(dst.ExhaustCards);

        int enemyN = src.EnemyCount;
        if (enemyN > 0)
        {
            src.EnemyHp.AsSpan(0, enemyN).CopyTo(dst.EnemyHp);
            src.EnemyMaxHp.AsSpan(0, enemyN).CopyTo(dst.EnemyMaxHp);
            src.EnemyBlock.AsSpan(0, enemyN).CopyTo(dst.EnemyBlock);
            src.EnemyIntentDmg.AsSpan(0, enemyN).CopyTo(dst.EnemyIntentDmg);
            src.EnemyIntentHits.AsSpan(0, enemyN).CopyTo(dst.EnemyIntentHits);
            src.EnemyIntent.AsSpan(0, enemyN).CopyTo(dst.EnemyIntent);
            src.EnemyPowers.AsSpan(0, enemyN * SimCombatState.PowersPerCre).CopyTo(dst.EnemyPowers);
            src.EnemyPowerInternal.AsSpan(0, enemyN).CopyTo(dst.EnemyPowerInternal);
        }

        int cardSidecarLength = src.CardInstanceCount + 1; // keep index 0 sentinel aligned with legacy arrays
        if (cardSidecarLength > 0)
        {
            src.CardEnergyBaseCost.AsSpan(0, cardSidecarLength).CopyTo(dst.CardEnergyBaseCost);
            src.CardEnergyCapturedX.AsSpan(0, cardSidecarLength).CopyTo(dst.CardEnergyCapturedX);
            src.CardEnergyModifierStart.AsSpan(0, cardSidecarLength).CopyTo(dst.CardEnergyModifierStart);
            src.CardEnergyModifierCount.AsSpan(0, cardSidecarLength).CopyTo(dst.CardEnergyModifierCount);
        }

        if (src.CardEnergyModifierUsed > 0)
            src.CardEnergyModifiers.AsSpan(0, src.CardEnergyModifierUsed).CopyTo(dst.CardEnergyModifiers);
    }

    private static byte CheckedCountByte(int value, int cap, string name)
    {
        if ((uint)value > (uint)cap)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: {name}={value} exceeds schema cap {cap}. " +
                "Schema and legacy snapshot drifted; re-evaluate CombatSchemaV1 before writing blob data.");
        }

        return (byte)value;
    }

    private static ushort CheckedCount(int value, int cap, string name)
    {
        if ((uint)value > (uint)cap)
        {
            throw new InvalidOperationException(
                $"CombatNodeBlobSnapshot: {name}={value} exceeds schema cap {cap}. " +
                "Schema and legacy snapshot drifted; re-evaluate CombatSchemaV1 before writing blob data.");
        }

        return (ushort)value;
    }
}