using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Afflictions;

namespace STS2.Agent.Sim;

/// <summary>
/// Static Type → SimAfflictionType lookup for card-affliction snapshotting.
/// </summary>
internal static class SimAfflictionRegistry
{
    private static readonly FrozenDictionary<Type, byte> _byType =
        new Dictionary<Type, byte>(SimAfflictionType.Count - 1)
        {
            { typeof(Bound),      SimAfflictionType.Bound },
            { typeof(Entangled),  SimAfflictionType.Entangled },
            { typeof(Galvanized), SimAfflictionType.Galvanized },
            { typeof(Hexed),      SimAfflictionType.Hexed },
            { typeof(Ringing),    SimAfflictionType.Ringing },
            { typeof(Smog),       SimAfflictionType.Smog },
            { typeof(Tainted),    SimAfflictionType.Tainted },
            // Devoured / Weighted: removed from the game; SimAfflictionType
            // slots kept (blob layout stable) but never mapped to a live type.
        }.ToFrozenDictionary();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetIndexOrNone(Type t)
        => _byType.TryGetValue(t, out byte idx) ? idx : SimAfflictionType.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetIndex(Type t, out byte idx) => _byType.TryGetValue(t, out idx);
}