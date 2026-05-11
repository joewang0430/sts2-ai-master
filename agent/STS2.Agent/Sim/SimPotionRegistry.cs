using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

internal static class SimPotionRegistry
{
    private readonly record struct PotionInfo(string Entry, PotionUsage Usage, TargetType TargetType);

    private static readonly object s_gate = new();

    private static Dictionary<string, ushort>? s_indexByEntry;
    private static PotionInfo[]? s_infoByIndex;

    public static ushort GetIndex(PotionModel potion)
    {
        EnsureBuilt();

        string entry = potion.Id.Entry;
        if (s_indexByEntry!.TryGetValue(entry, out ushort index))
            return index;

        throw new InvalidOperationException(
            $"SimPotionRegistry: potion '{entry}' is not registered in ModelDb.AllPotions.");
    }

    public static string ReversePotionName(ushort index)
    {
        EnsureBuilt();

        if (index == 0)
            return "<empty>";

        if ((uint)index >= s_infoByIndex!.Length)
            return $"p{index}?";

        string? entry = s_infoByIndex[index].Entry;
        return string.IsNullOrEmpty(entry) ? $"p{index}?" : entry;
    }

    public static bool IsCombatUsable(ushort index)
    {
        EnsureBuilt();

        if (index == 0 || (uint)index >= s_infoByIndex!.Length)
            return false;

        PotionUsage usage = s_infoByIndex[index].Usage;
        return usage == PotionUsage.CombatOnly || usage == PotionUsage.AnyTime;
    }

    public static TargetType GetTargetType(ushort index)
    {
        EnsureBuilt();

        if (index == 0 || (uint)index >= s_infoByIndex!.Length)
            return TargetType.None;

        return s_infoByIndex[index].TargetType;
    }

    private static void EnsureBuilt()
    {
        if (s_infoByIndex is not null)
            return;

        lock (s_gate)
        {
            if (s_infoByIndex is not null)
                return;

            Build();
        }
    }

    private static void Build()
    {
        List<PotionModel> potions = ModelDb.AllPotions
            .OrderBy(static potion => potion.Id.Entry, StringComparer.Ordinal)
            .ToList();

        if (potions.Count > ushort.MaxValue - 1)
        {
            throw new InvalidOperationException(
                $"SimPotionRegistry: potion count {potions.Count} exceeds ushort id capacity.");
        }

        Dictionary<string, ushort> indexByEntry = new(potions.Count, StringComparer.Ordinal);
        PotionInfo[] infoByIndex = new PotionInfo[potions.Count + 1];

        ushort nextIndex = 1;
        foreach (PotionModel potion in potions)
        {
            string entry = potion.Id.Entry;
            if (!indexByEntry.TryAdd(entry, nextIndex))
            {
                throw new InvalidOperationException(
                    $"SimPotionRegistry: duplicate potion id '{entry}' in ModelDb.AllPotions.");
            }

            infoByIndex[nextIndex] = new PotionInfo(entry, potion.Usage, potion.TargetType);
            nextIndex++;
        }

        s_indexByEntry = indexByEntry;
        s_infoByIndex = infoByIndex;
    }
}