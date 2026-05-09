using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Packed mirror of one <see cref="LocalCostModifier"/> entry from
/// <see cref="CardEnergyCost"/>. Stored in a flat sidecar pool keyed by
/// <see cref="SimCard.InstanceId"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 2)]
internal struct SimLocalCostModifier
{
    public sbyte Amount;
    public byte Flags;

    private const byte TypeMask = 0x03;
    private const byte ExpirationMask = 0x1C;
    private const byte ExpirationShift = 2;
    private const byte ExpirationValueMask = ExpirationMask >> ExpirationShift;
    private const byte FlagReduceOnly = 1 << 5;

    public static SimLocalCostModifier From(LocalCostModifier src)
    {
        int amount = src.Amount;
        int type = (int)src.Type;
        int expiration = (int)src.Expiration;

        if (amount < sbyte.MinValue || amount > sbyte.MaxValue)
        {
            throw new InvalidOperationException(
                $"SimLocalCostModifier: LocalCostModifier.Amount={amount} exceeds packed sbyte range [{sbyte.MinValue}, {sbyte.MaxValue}].");
        }

        if ((type & ~TypeMask) != 0)
        {
            throw new InvalidOperationException(
                $"SimLocalCostModifier: LocalCostType value {type} exceeds packed bit budget.");
        }

        if ((expiration & ~ExpirationValueMask) != 0)
        {
            throw new InvalidOperationException(
                $"SimLocalCostModifier: LocalCostModifierExpiration value {expiration} exceeds packed bit budget.");
        }

        return new SimLocalCostModifier
        {
            Amount = (sbyte)amount,
            Flags = (byte)(type
                | ((expiration & ExpirationValueMask) << ExpirationShift)
                | (src.IsReduceOnly ? FlagReduceOnly : 0))
        };
    }

    public readonly LocalCostType Type
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (LocalCostType)(Flags & TypeMask);
    }

    public readonly LocalCostModifierExpiration Expiration
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (LocalCostModifierExpiration)((Flags & ExpirationMask) >> ExpirationShift);
    }

    public readonly bool IsReduceOnly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagReduceOnly) != 0;
    }
}