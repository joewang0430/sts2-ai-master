using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Packed mirror of one <see cref="TemporaryCardCost"/> entry from
/// <see cref="MegaCrit.Sts2.Core.Models.CardModel"/>. Stored in an ordered flat
/// sidecar pool keyed by <see cref="SimCard.InstanceId"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
internal struct SimTemporaryStarCost
{
    private const int MaxPackedCost = 0x3F;
    private const byte CostMask = MaxPackedCost;
    private const byte FlagClearsWhenTurnEnds = 1 << 6;
    private const byte FlagClearsWhenCardIsPlayed = 1 << 7;

    private byte _packed;

    public static SimTemporaryStarCost From(TemporaryCardCost src)
    {
        int cost = src.Cost;
        if ((uint)cost > MaxPackedCost)
        {
            throw new InvalidOperationException(
                $"SimTemporaryStarCost: TemporaryCardCost.Cost={cost} exceeds packed 6-bit range [0, {MaxPackedCost}].");
        }

        byte packed = (byte)cost;
        if (src.ClearsWhenTurnEnds)
            packed |= FlagClearsWhenTurnEnds;
        if (src.ClearsWhenCardIsPlayed)
            packed |= FlagClearsWhenCardIsPlayed;

        return new SimTemporaryStarCost
        {
            _packed = packed
        };
    }

    public readonly int Cost
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _packed & CostMask;
    }

    public readonly byte Flags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)(_packed & ~CostMask);
    }

    public readonly bool ClearsWhenTurnEnds
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_packed & FlagClearsWhenTurnEnds) != 0;
    }

    public readonly bool ClearsWhenCardIsPlayed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_packed & FlagClearsWhenCardIsPlayed) != 0;
    }
}