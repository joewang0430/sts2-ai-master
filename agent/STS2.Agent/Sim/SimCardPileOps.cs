using System;

namespace STS2.Agent.Sim;

/// <summary>
/// Low-level pile mutation primitives shared by every card writer in <see cref="SimCardEffects"/>
/// that draws, discards, exhausts, or generates a card. Order-preserving (shift, not swap-remove):
/// the draw pile's top-to-bottom order is load-bearing (draws always take index 0), so every pile
/// uses the same shift-based move for consistency rather than special-casing "order doesn't matter
/// here" per pile — the extra cost is a handful of struct copies over an at-most-~100-card span,
/// not worth a second code path.
///
/// Fail-loud: every write throws on capacity overflow rather than silently dropping a card.
/// </summary>
internal static class SimCardPileOps
{
    /// <summary>Removes and returns the card at <paramref name="index"/>, shifting everything
    /// after it down by one and decrementing <paramref name="count"/>.</summary>
    public static SimCard RemoveAt(Span<SimCard> pile, ref ushort count, int index)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index), $"SimCardPileOps.RemoveAt: index {index} out of range (count={count}).");

        SimCard removed = pile[index];
        for (int i = index; i < count - 1; i++)
            pile[i] = pile[i + 1];
        count--;
        return removed;
    }

    /// <summary>Inserts <paramref name="card"/> at <paramref name="index"/> (0 = top/front),
    /// shifting everything at/after it up by one and incrementing <paramref name="count"/>.
    /// Pass <c>index == count</c> to append at the end (bottom of the pile).</summary>
    public static void Insert(Span<SimCard> pile, ref ushort count, int capacity, int index, in SimCard card)
    {
        if (index < 0 || index > count)
            throw new ArgumentOutOfRangeException(nameof(index), $"SimCardPileOps.Insert: index {index} out of range (count={count}).");
        if (count >= capacity)
            throw new InvalidOperationException($"SimCardPileOps.Insert: pile is at capacity ({capacity}).");

        for (int i = count; i > index; i--)
            pile[i] = pile[i - 1];
        pile[index] = card;
        count++;
    }

    /// <summary>Moves the card at <paramref name="fromIndex"/> in <paramref name="fromPile"/> to
    /// the end of <paramref name="toPile"/>, preserving its identity (InstanceId, enchantment,
    /// affliction, flags — everything <see cref="SimCard"/> carries).</summary>
    public static void MoveToEnd(
        Span<SimCard> fromPile, ref ushort fromCount, int fromIndex,
        Span<SimCard> toPile, ref ushort toCount, int toCapacity)
    {
        SimCard card = RemoveAt(fromPile, ref fromCount, fromIndex);
        Insert(toPile, ref toCount, toCapacity, toCount, in card);
    }

    /// <summary>
    /// Draws up to <paramref name="count"/> cards from the top of the draw pile into hand,
    /// stopping early (not an error) once hand is full — mirrors CardPileCmd.Draw's
    /// <c>Math.Max(0, MaxCardsInHand - hand.Count)</c> early-stop, not a forced discard.
    ///
    /// Does NOT check <c>NoDrawPower</c> / <c>Hook.ShouldDraw</c> — none of the cards wired up so
    /// far apply NoDraw to themselves before their own draw, so this hasn't mattered yet; a future
    /// caller that needs it should check the power first and skip calling this at all.
    ///
    /// Fail-loud on an empty draw pile: the real game reshuffles the discard pile into the draw
    /// pile at that point, which needs the game's RNG stream (shuffle order) — not replicated yet,
    /// same gap as Fabricator's random summon (see dev_docs/Enemy_Intent_Payload_Backlog.md).
    /// Throws rather than silently drawing fewer cards than the game actually would.
    /// </summary>
    public static void DrawCards(CombatNodeBlob state, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (state.HandCount >= CombatSimLayout.HandCap) return;
            if (state.DrawCount == 0)
            {
                throw new InvalidOperationException(
                    "SimCardPileOps.DrawCards: draw pile is empty — would need a reshuffle, and " +
                    "discard-into-draw shuffle order isn't RNG-replicated yet.");
            }
            MoveToEnd(state.DrawCards, ref state.DrawCount, 0, state.HandCards, ref state.HandCount, CombatSimLayout.HandCap);
        }
    }

    /// <summary>
    /// Decides where a just-played card (still sitting in Hand at <paramref name="handIndex"/>)
    /// ends up, and moves it there. Call this AFTER the card's own effect (SimCardEffects.Apply)
    /// has resolved — SimCardEffects.PlayCard does both in the right order.
    ///
    /// Mirrors CardModel.GetResultPileTypeForCardPlay() (game_source/CardModel.cs) exactly:
    ///   IsDupe or Type==Power → removed from combat entirely, not sent to any pile.
    ///   ExhaustOnNextPlay or HasExhaustKeyword → Exhaust pile (ExhaustOnNextPlay is a one-shot
    ///     flag and gets cleared, matching the game clearing it after consuming it).
    ///   otherwise → Discard pile.
    /// </summary>
    public static void ResolvePlayedCardDestination(CombatNodeBlob state, int handIndex)
    {
        SimCard card = state.HandCards[handIndex];

        if (card.IsDupe || SimCardTypeRegistry.Get(card.BaseCardId) == SimCardType.Power)
        {
            RemoveAt(state.HandCards, ref state.HandCount, handIndex);
            return;
        }

        bool exhausts = (card.Flags & SimCard.FlagExhaustOnNextPlay) != 0 || card.HasExhaustKeyword;
        if (exhausts)
        {
            card.Flags &= unchecked((ushort)~SimCard.FlagExhaustOnNextPlay);
            state.HandCards[handIndex] = card;
            MoveToEnd(state.HandCards, ref state.HandCount, handIndex, state.ExhaustCards, ref state.ExhaustCount, CombatSimLayout.PileCap);
            return;
        }

        MoveToEnd(state.HandCards, ref state.HandCount, handIndex, state.DiscCards, ref state.DiscCount, CombatSimLayout.PileCap);
    }

    /// <summary>
    /// Mints a brand-new <see cref="SimCard"/> instance (fresh InstanceId via
    /// <see cref="CombatNodeBlob.CardInstanceCount"/>, no star cost, no enchantment/affliction, no
    /// flags) and appends it to the end of <paramref name="pile"/>. Mirrors what
    /// <c>CombatState.CreateCard&lt;T&gt;</c> + a pile-add produces for a freshly generated card
    /// (e.g. Anger's self-clone) — NOT a copy of an existing instance's enchantment/affliction/flags,
    /// since a newly generated card starts clean.
    ///
    /// Deliberately does NOT set the IsDupe flag. Confirmed against game_source/CardModel.cs:
    /// <c>CreateClone()</c> (what Anger and similar self-copy cards call) never sets IsDupe — only
    /// <c>CreateDupe()</c> does (a genuinely different, unrelated card-generation path). A clone's
    /// IsDupe stays false, so when Anger's clone is later played, it goes to Discard normally
    /// (and clones ITSELF again) rather than being removed from combat — this is the real,
    /// well-known "Anger chains" behavior, not a bug to fix.
    /// </summary>
    public static void AppendGenerated(CombatNodeBlob state, Span<SimCard> pile, ref ushort count, int capacity, ushort baseCardId, bool upgraded)
        => InsertGeneratedAt(state, pile, ref count, capacity, baseCardId, upgraded, count);

    /// <summary>Same minting as <see cref="AppendGenerated"/> but at an explicit
    /// <paramref name="index"/> instead of always the end — for
    /// <c>CardPilePosition.Random</c> callers (game_source's own resolution:
    /// <c>Rng.Shuffle.NextInt(targetPile.Cards.Count + 1)</c>, i.e. a fresh roll per card in
    /// [0, count] inclusive, evaluated against whatever the pile's count is at that moment —
    /// callers injecting multiple cards must re-roll per card, not compute one index up front).</summary>
    public static void InsertGeneratedAt(CombatNodeBlob state, Span<SimCard> pile, ref ushort count, int capacity, ushort baseCardId, bool upgraded, int index)
    {
        ushort instanceId = ++state.CardInstanceCount;
        SimCard card = new SimCard
        {
            CardId = (ushort)(baseCardId | (upgraded ? 0x8000 : 0)),
            InstanceId = instanceId,
            BaseStarCost = -1,
        };
        Insert(pile, ref count, capacity, index, in card);
    }
}
