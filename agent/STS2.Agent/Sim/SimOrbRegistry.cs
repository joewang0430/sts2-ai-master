using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Models.Orbs;

namespace STS2.Agent.Sim;

/// <summary>
/// Type → <see cref="SimOrbType"/> id mapping plus reflection handles to the
/// two orbs that carry private mutable state (DarkOrb._evokeVal, GlassOrb._passiveVal).
/// Looking up the FieldInfo is reflection-cheap once and hot-cached: the
/// snapshot path reads the live value via <c>FieldInfo.GetValue</c>, which
/// is fine because Snapshot runs at most once per AI Think (≤ once per
/// player action), not inside the DFS loop.
///
/// Public OrbModel properties are *focus-modified* (PassiveVal goes through
/// <c>ModifyOrbValue</c> which adds FocusPower amount), so we cannot read
/// the raw mutable state from the property. The private backing fields are
/// the source of truth.
/// </summary>
internal static class SimOrbRegistry
{
    // Type → byte (SimOrbType.*).
    private static readonly FrozenDictionary<Type, byte> _byType = new Dictionary<Type, byte>
    {
        { typeof(LightningOrb), SimOrbType.Lightning },
        { typeof(FrostOrb),     SimOrbType.Frost     },
        { typeof(DarkOrb),      SimOrbType.Dark      },
        { typeof(PlasmaOrb),    SimOrbType.Plasma    },
        { typeof(GlassOrb),     SimOrbType.Glass     },
    }.ToFrozenDictionary();

    // Reflection handles to private mutable backing fields.
    //   DarkOrb._evokeVal   : decimal — accumulates on each Passive trigger.
    //   GlassOrb._passiveVal: decimal — decrements (toward 0) on each Passive trigger.
    // BindingFlags.NonPublic | Instance is required because both fields are private.
    private static readonly FieldInfo _darkEvokeValField =
        typeof(DarkOrb).GetField("_evokeVal", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "SimOrbRegistry: DarkOrb._evokeVal field not found. Game source layout changed.");

    private static readonly FieldInfo _glassPassiveValField =
        typeof(GlassOrb).GetField("_passiveVal", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "SimOrbRegistry: GlassOrb._passiveVal field not found. Game source layout changed.");

    /// <summary>Returns the dense byte id for <paramref name="orbType"/>, or
    /// <see cref="SimOrbType.None"/> (= empty) if the type is not registered.
    /// SimCaps validates registry completeness at startup so a None return at
    /// snapshot time is a code-path bug.</summary>
    public static byte GetIndexOrNone(Type orbType)
        => _byType.TryGetValue(orbType, out byte id) ? id : SimOrbType.None;

    public static bool TryGetIndex(Type orbType, out byte id)
        => _byType.TryGetValue(orbType, out id);

    /// <summary>Read <c>DarkOrb._evokeVal</c> as int (truncated from decimal).
    /// Caller must have verified the orb is a DarkOrb.</summary>
    public static int ReadDarkEvokeVal(DarkOrb orb)
        => (int)(decimal)_darkEvokeValField.GetValue(orb)!;

    /// <summary>Read <c>GlassOrb._passiveVal</c> as int (truncated from decimal).
    /// Caller must have verified the orb is a GlassOrb.</summary>
    public static int ReadGlassPassiveVal(GlassOrb orb)
        => (int)(decimal)_glassPassiveValField.GetValue(orb)!;
}
