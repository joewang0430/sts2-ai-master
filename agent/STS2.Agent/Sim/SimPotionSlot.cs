using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 3)]
internal struct SimPotionSlot : IEquatable<SimPotionSlot>
{
    private const byte FlagAllowAi = 1 << 0;

    public ushort PotionId;
    public byte Flags;

    public static SimPotionSlot From(PotionModel potion, bool allowAi)
    {
        ushort potionId = SimPotionRegistry.GetIndex(potion);
        return new SimPotionSlot
        {
            PotionId = potionId,
            Flags = allowAi ? FlagAllowAi : (byte)0,
        };
    }

    public readonly bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => PotionId == 0;
    }

    public readonly bool AllowAi
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagAllowAi) != 0;
    }

    public readonly bool Equals(SimPotionSlot other)
        => PotionId == other.PotionId && Flags == other.Flags;

    public override readonly bool Equals(object? obj)
        => obj is SimPotionSlot other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(PotionId, Flags);
}