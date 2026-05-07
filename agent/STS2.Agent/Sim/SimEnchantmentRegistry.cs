using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace STS2.Agent.Sim;

/// <summary>
/// Static <c>Type → SimEnchantmentType (byte)</c> lookup. Mirrors
/// <see cref="SimPowerRegistry"/> but for enchantments. Frozen at class init
/// for read-only hot-path lookups in <c>Snapshot</c>.
///
/// Index 0 (None) is NOT in the table — callers detect "no enchantment" by
/// the source <c>CardModel.Enchantment == null</c> and store 0 directly. The
/// registry only maps real types to their non-zero indices.
/// </summary>
internal static class SimEnchantmentRegistry
{
    private static readonly FrozenDictionary<Type, byte> _byType =
        new Dictionary<Type, byte>(SimEnchantmentType.Count - 1)
        {
            { typeof(Adroit),            SimEnchantmentType.Adroit },
            { typeof(Clone),             SimEnchantmentType.Clone },
            { typeof(Corrupted),         SimEnchantmentType.Corrupted },
            { typeof(DeprecatedEnchantment), SimEnchantmentType.Deprecated },
            { typeof(Glam),              SimEnchantmentType.Glam },
            { typeof(Goopy),             SimEnchantmentType.Goopy },
            { typeof(Imbued),            SimEnchantmentType.Imbued },
            { typeof(Inky),              SimEnchantmentType.Inky },
            { typeof(Instinct),          SimEnchantmentType.Instinct },
            { typeof(Momentum),          SimEnchantmentType.Momentum },
            { typeof(Nimble),            SimEnchantmentType.Nimble },
            { typeof(PerfectFit),        SimEnchantmentType.PerfectFit },
            { typeof(RoyallyApproved),   SimEnchantmentType.RoyallyApproved },
            { typeof(Sharp),             SimEnchantmentType.Sharp },
            { typeof(Slither),           SimEnchantmentType.Slither },
            { typeof(SlumberingEssence), SimEnchantmentType.SlumberingEssence },
            { typeof(SoulsPower),        SimEnchantmentType.SoulsPower },
            { typeof(Sown),              SimEnchantmentType.Sown },
            { typeof(Spiral),            SimEnchantmentType.Spiral },
            { typeof(Steady),            SimEnchantmentType.Steady },
            { typeof(Swift),             SimEnchantmentType.Swift },
            { typeof(TezcatarasEmber),   SimEnchantmentType.TezcatarasEmber },
            { typeof(Vigorous),          SimEnchantmentType.Vigorous },
        }.ToFrozenDictionary();

    /// <summary>Return registered index for <paramref name="t"/> or 0 (None) if unknown.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetIndexOrNone(Type t)
        => _byType.TryGetValue(t, out byte idx) ? idx : SimEnchantmentType.None;

    /// <summary>Try-pattern lookup for <see cref="SimCaps"/>'s startup audit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetIndex(Type t, out byte idx) => _byType.TryGetValue(t, out idx);
}
