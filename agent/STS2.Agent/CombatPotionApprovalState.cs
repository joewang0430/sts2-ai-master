using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent;

internal readonly record struct CombatPotionApprovalKey(ulong PlayerNetId, byte SlotIndex, string PotionId);

internal static class CombatPotionApprovalState
{
    private static readonly HashSet<CombatPotionApprovalKey> s_allowed = new();

    public static void Clear() => s_allowed.Clear();

    public static CombatPotionApprovalKey CreateKey(Player player, int slotIndex, PotionModel potion)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(potion);

        if ((uint)slotIndex > byte.MaxValue)
        {
            throw new InvalidOperationException(
                $"CombatPotionApprovalState: slotIndex={slotIndex} exceeds byte range.");
        }

        return new CombatPotionApprovalKey(player.NetId, (byte)slotIndex, potion.Id.Entry);
    }

    public static bool IsAllowed(in CombatPotionApprovalKey key)
        => s_allowed.Contains(key);

    public static bool IsAllowed(Player player, int slotIndex, PotionModel potion)
        => IsAllowed(CreateKey(player, slotIndex, potion));

    public static bool Toggle(in CombatPotionApprovalKey key)
    {
        if (s_allowed.Remove(key))
            return false;

        s_allowed.Add(key);
        return true;
    }

    public static void SyncToPlayer(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        HashSet<CombatPotionApprovalKey> alive = new();
        IReadOnlyList<PotionModel?> slots = player.PotionSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            PotionModel? potion = slots[i];
            if (potion is null)
                continue;

            alive.Add(CreateKey(player, i, potion));
        }

        s_allowed.RemoveWhere(key => key.PlayerNetId == player.NetId && !alive.Contains(key));
    }
}