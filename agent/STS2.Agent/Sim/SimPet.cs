using System;
using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Fixed-size value record for the Necromancer's Osty pet — currently the
/// only pet type the game implements (verified Topic-3 of the Stars/Orbs/Pets
/// inventory). The framework's <c>PlayerCombatState._pets</c> is a
/// <c>List&lt;Creature&gt;</c> capable of holding several pets, but
/// <c>FirstOrDefault&lt;Osty&gt;()</c> at the lookup site enforces "max 1
/// Osty per player". Until any other pet type ships, one slot suffices.
///
/// Why pack stats into a struct (vs. parallel scalar fields):
///   • Co-locates the four hot fields in 8 bytes — one quarter of a cache line.
///   • Lets the blob snapshot/copy path memcpy the whole struct in one
///     instruction instead of four individual loads/stores.
///   • Powers are stored separately as a flat <c>short[259]</c>
///     (<c>OstyPowers</c>) using the same row layout
///     as the Player and Enemy power vectors so a single hook-dispatch helper
///     can address any creature uniformly.
///
/// Persistence:
///   • Within combat: persists across turns; HP carries over until 0.
///   • Across combats: discarded with PlayerCombatState (new combat = no Osty).
///   • At HP 0: <c>Exists</c> stays 1 (corpse retained for revival via
///     OstyCmd.Summon), <c>CurrentHp</c> = 0.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 8)]
internal struct SimPet : IEquatable<SimPet>
{
    /// <summary>Current HP (0 = dead but corpse retained when Exists=1).</summary>
    public ushort CurrentHp;

    /// <summary>Max HP (Osty starts at 1; can grow via GainMaxHp).</summary>
    public ushort MaxHp;

    /// <summary>Block (decays per turn, mirrors Creature.Block).</summary>
    public ushort Block;

    /// <summary>1 if Osty has ever been summoned this combat (corpse-or-alive),
    /// 0 if never summoned. <c>OstyCmd.Summon</c> re-uses the same Creature
    /// instance for revival, so this flag stays sticky once set.</summary>
    public byte Exists;

    private byte _pad; // explicit pad to keep Size=8 and word-aligned.

    public readonly bool Equals(SimPet other)
        => CurrentHp == other.CurrentHp
        && MaxHp == other.MaxHp
        && Block == other.Block
        && Exists == other.Exists;

    public override readonly bool Equals(object? obj)
        => obj is SimPet other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(CurrentHp, MaxHp, Block, Exists);

    public override readonly string ToString()
        => $"hp={CurrentHp} max={MaxHp} blk={Block} ex={Exists}";
}
