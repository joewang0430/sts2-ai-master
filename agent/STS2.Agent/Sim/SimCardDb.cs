using System.Collections.Generic;

namespace STS2.Agent.Sim;

/// <summary>
/// Static, immutable card database. CardId (int) → fixed card data + Apply().
///
/// Why static and not per-instance:
///   The full STS2 CardModel is heavy (Godot resource refs, audio, vfx, etc.).
///   For simulation we only need: cost, type, target, keyword bitset, an Apply
///   function. Storing these once in a flat array means SimCombatState only
///   carries int CardIds in its piles — copying a pile is a single memcpy.
///
/// This file is the *table layout*. Population (Register(...)) happens later
/// when we wire up individual cards.
/// </summary>
internal static class SimCardDb
{
    /// <summary>Number of distinct cards we have registered.</summary>
    internal static int Count;

    /// <summary>CardId → game's string Id ("Strike_R" etc.) for boundary lookup.</summary>
    internal static readonly List<string> GameId = new();

    /// <summary>CardId → base energy cost (-1 = X-cost, -2 = unplayable).</summary>
    internal static readonly List<int> BaseCost = new();

    /// <summary>Bitset of keyword flags. See SimCardKeyword.</summary>
    internal static readonly List<int> Keywords = new();

    // Apply / target-type / type fields will be added when logic is implemented.
    // Intentionally stubbed: this is layout-only.
}

/// <summary>Bit flags packed into SimCardDb.Keywords[id].</summary>
internal static class SimCardKeyword
{
    public const int Ethereal = 1 << 0;
    public const int Retain   = 1 << 1;
    public const int Exhaust  = 1 << 2;
    public const int Sly      = 1 << 3;
    public const int Innate   = 1 << 4;
}
