using System;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace STS2.Agent.Sim;

/// <summary>
/// Zero-allocation pile mutation primitives for the four combat piles carried
/// by <see cref="CombatNodeBlob"/>.
///
/// This is deliberately lower-level than game-side CardPileCmd:
/// it preserves ordering exactly and mutates counts/storage in place, but it
/// does not embed higher-level rules such as "hand full redirects to discard"
/// or hook dispatch. Those belong in the future sim execution layer.
/// </summary>
internal static class CombatNodeCardPiles
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count(CombatNodeBlob blob, PileType pileType)
        => CountRef(blob, pileType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Capacity(PileType pileType)
    {
        return pileType switch
        {
            PileType.Hand => CombatSchemaV1.Cards.HandCap,
            PileType.Draw or PileType.Discard or PileType.Exhaust => CombatSchemaV1.Cards.PileCap,
            _ => throw UnsupportedPile(pileType),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<SimCard> Storage(CombatNodeBlob blob, PileType pileType)
    {
        return pileType switch
        {
            PileType.Hand => blob.HandCards,
            PileType.Draw => blob.DrawCards,
            PileType.Discard => blob.DiscCards,
            PileType.Exhaust => blob.ExhaustCards,
            _ => throw UnsupportedPile(pileType),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<SimCard> LiveCards(CombatNodeBlob blob, PileType pileType)
        => Storage(blob, pileType).Slice(0, Count(blob, pileType));

    public static SimCard RemoveAt(CombatNodeBlob blob, PileType pileType, int index)
    {
        Span<SimCard> cards = Storage(blob, pileType);
        ref ushort countRef = ref CountRef(blob, pileType);
        int count = countRef;
        if ((uint)index >= (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"CombatNodeCardPiles.RemoveAt: {pileType} index {index} is outside live count {count}.");

        SimCard removed = cards[index];
        int tail = count - index - 1;
        if (tail > 0)
            cards.Slice(index + 1, tail).CopyTo(cards.Slice(index));

        countRef = (ushort)(count - 1);
        cards[count - 1] = default;
        return removed;
    }

    public static void InsertAt(CombatNodeBlob blob, PileType pileType, int index, in SimCard card)
    {
        Span<SimCard> cards = Storage(blob, pileType);
        ref ushort countRef = ref CountRef(blob, pileType);
        int count = countRef;
        int cap = Capacity(pileType);

        if ((uint)index > (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"CombatNodeCardPiles.InsertAt: {pileType} index {index} is outside insertion window [0, {count}].");
        if (count >= cap)
            throw new InvalidOperationException(
                $"CombatNodeCardPiles.InsertAt: {pileType} count {count} reached capacity {cap}.");

        int tail = count - index;
        if (tail > 0)
            cards.Slice(index, tail).CopyTo(cards.Slice(index + 1));

        cards[index] = card;
        countRef = (ushort)(count + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(CombatNodeBlob blob, PileType pileType, in SimCard card, CardPilePosition position = CardPilePosition.Bottom)
    {
        int count = Count(blob, pileType);
        int index = position switch
        {
            CardPilePosition.Bottom => count,
            CardPilePosition.Top => 0,
            CardPilePosition.Random => throw new InvalidOperationException(
                "CombatNodeCardPiles.Add: Random insertion requires the AddRandom overload with a shuffle RNG."),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };

        InsertAt(blob, pileType, index, in card);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddRandom(CombatNodeBlob blob, PileType pileType, in SimCard card, ref RandomState shuffleRng)
    {
        int count = Count(blob, pileType);
        int index = RandomStateOps.Next(ref shuffleRng, count + 1);
        InsertAt(blob, pileType, index, in card);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimCard Move(CombatNodeBlob blob, PileType fromPile, int fromIndex, PileType toPile, CardPilePosition position = CardPilePosition.Bottom)
    {
        SimCard card = RemoveAt(blob, fromPile, fromIndex);
        Add(blob, toPile, in card, position);
        return card;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimCard MoveRandom(CombatNodeBlob blob, PileType fromPile, int fromIndex, PileType toPile, ref RandomState shuffleRng)
    {
        SimCard card = RemoveAt(blob, fromPile, fromIndex);
        AddRandom(blob, toPile, in card, ref shuffleRng);
        return card;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref ushort CountRef(CombatNodeBlob blob, PileType pileType)
    {
        switch (pileType)
        {
            case PileType.Hand:
                return ref blob.HandCount;
            case PileType.Draw:
                return ref blob.DrawCount;
            case PileType.Discard:
                return ref blob.DiscCount;
            case PileType.Exhaust:
                return ref blob.ExhaustCount;
            default:
                throw UnsupportedPile(pileType);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InvalidOperationException UnsupportedPile(PileType pileType)
        => new($"CombatNodeCardPiles: pile {pileType} is not carried by CombatSchemaV1 card slice.");
}