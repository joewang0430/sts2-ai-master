using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Packed mirror of one <see cref="LocalCostModifier"/> entry from
/// <see cref="CardEnergyCost"/>. Stored in a flat sidecar pool keyed by
/// <see cref="SimCard.InstanceId"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 3)]
internal struct SimLocalCostModifier
{
    public short Amount;
    public byte Flags;

    private const byte TypeMask = 0x03;
    private const byte ExpirationMask = 0x1C;
    private const byte ExpirationShift = 2;
    private const byte FlagReduceOnly = 1 << 5;

    public static SimLocalCostModifier From(LocalCostModifier src)
    {
        return new SimLocalCostModifier
        {
            Amount = src.Amount < short.MinValue ? short.MinValue
                 : src.Amount > short.MaxValue ? short.MaxValue
                 : (short)src.Amount,
            Flags = (byte)((int)src.Type
                | (((int)src.Expiration & 0x07) << ExpirationShift)
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