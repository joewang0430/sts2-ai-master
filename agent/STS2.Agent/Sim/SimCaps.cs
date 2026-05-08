using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// One-shot startup invariants check. Asserts that every assumption baked
/// into <see cref="SimCombatState"/>'s fixed-capacity layout still holds
/// against the live game's content database. Any violation throws and the
/// AI subsystem refuses to run — better a loud crash on boot than silent
/// truncation in the search hot path.
///
/// Cost: O(N) over models, but pays itself off forever — no per-snapshot
/// bounds checks needed downstream.
///
/// Called lazily from <see cref="SimCombatState.Snapshot"/> the first time
/// the AI snapshots a real combat, NOT from <c>ModEntry.Initialize()</c>:
/// ModelDb is populated by the game's content-loading pipeline, which runs
/// AFTER mod initializers. Probing it too early returns empty enumerables.
/// </summary>
internal static class SimCaps
{
    private static bool _verified;
    private static readonly object _gate = new();

    /// <summary>
    /// Runs the full battery of capacity checks exactly once. Subsequent
    /// calls are a single volatile-read no-op. Thread-safe via double-check.
    /// </summary>
    public static void EnsureVerified()
    {
        if (_verified) return;
        lock (_gate)
        {
            if (_verified) return;
            VerifyAll();
            _verified = true;
        }
    }

    private static void VerifyAll()
    {
        // ── 1. CardPile.maxCardsInHand must equal SimCombatState.HandCap (10).
        //    SimCombatState.Hand is a fixed-size ushort[10]; if the game ever
        //    raised the cap, snapshot would silently truncate.
        if (CardPile.maxCardsInHand != SimCombatState.HandCap)
        {
            throw new InvalidOperationException(
                $"SimCaps: CardPile.maxCardsInHand={CardPile.maxCardsInHand} but " +
                $"SimCombatState.HandCap={SimCombatState.HandCap}. Update HandCap and rebuild.");
        }

        // ── 2. Every encounter must fit in EnemyCap=6 monster slots.
        //    Use EncounterModel.Slots (static design data, safe on canonical
        //    models). MonstersWithSlots is run-time only — it asserts mutable
        //    and would throw CanonicalModelException here.
        int worstSlots = 0;
        EncounterModel? worstEncounter = null;
        foreach (EncounterModel enc in ModelDb.AllEncounters)
        {
            int n = enc.Slots.Count;
            if (n > worstSlots) { worstSlots = n; worstEncounter = enc; }
            if (n > SimCombatState.EnemyCap)
            {
                throw new InvalidOperationException(
                    $"SimCaps: encounter '{enc.Id}' has {n} slots but " +
                    $"SimCombatState.EnemyCap={SimCombatState.EnemyCap}. Raise EnemyCap.");
            }
        }

        // ── 3. Every concrete CardModel must have MaxUpgradeLevel ≤ 1.
        //    SimCombatState encodes upgrade as bit 15 of a ushort card id; only
        //    one upgrade level is representable.
        foreach (CardModel card in ModelDb.AllCards)
        {
            if (card.MaxUpgradeLevel > 1)
            {
                throw new InvalidOperationException(
                    $"SimCaps: card '{card.Id}' has MaxUpgradeLevel={card.MaxUpgradeLevel}; " +
                    "ushort id bit-15 upgrade flag supports only 0 or 1.");
            }
        }

        // ── 4. Every concrete PowerModel must be registered in SimPowerRegistry.
        //    EnemyPowers / PlayerPowers are dense short[259] arrays; an unknown
        //    power type would have nowhere to land during snapshot.
        //    Mock* powers under *.Powers.Mocks are unit-test fixtures registered
        //    into ModelDb but never instantiated in real combat — skip them.
        List<string>? missing = null;
        foreach (PowerModel pwr in ModelDb.AllPowers)
        {
            Type t = pwr.GetType();
            string? ns = t.Namespace;
            if (ns != null && ns.EndsWith(".Powers.Mocks", StringComparison.Ordinal)) continue;
            if (!SimPowerRegistry.TryGetIndex(t, out _))
            {
                missing ??= new List<string>();
                missing.Add(t.FullName ?? t.Name);
            }
        }
        if (missing is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "SimCaps: PowerModel subclasses not registered in SimPowerRegistry: " +
                string.Join(", ", missing) +
                ". Add typeof(...) → SimPowerType.Xxx entries and bump SimPowerType.Count.");
        }

        // ── 5. Every concrete EnchantmentModel must be registered in
        //    SimEnchantmentRegistry. Mirrors the Power check. *.Enchantments.Mocks
        //    namespace is skipped (test fixtures only). DeprecatedEnchantment is
        //    explicitly registered so no live save can leak a "0 = None" id for
        //    a card that actually has an enchantment.
        List<string>? missingEnch = null;
        foreach (EnchantmentModel ench in ModelDb.DebugEnchantments)
        {
            Type t = ench.GetType();
            string? ns = t.Namespace;
            if (ns != null && ns.EndsWith(".Enchantments.Mocks", StringComparison.Ordinal)) continue;
            if (!SimEnchantmentRegistry.TryGetIndex(t, out _))
            {
                missingEnch ??= new List<string>();
                missingEnch.Add(t.FullName ?? t.Name);
            }
        }
        if (missingEnch is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "SimCaps: EnchantmentModel subclasses not registered in SimEnchantmentRegistry: " +
                string.Join(", ", missingEnch) +
                ". Add typeof(...) → SimEnchantmentType.Xxx entries and bump SimEnchantmentType.Count.");
        }

        // ── 6. Orb queue capacity must equal SimCombatState's OrbSlots10
        //    inline length (10). If the game ever raises maxCapacity, the
        //    snapshot loop would silently truncate the queue.
        if (OrbQueue.maxCapacity != 10)
        {
            throw new InvalidOperationException(
                $"SimCaps: OrbQueue.maxCapacity={OrbQueue.maxCapacity} but " +
                "SimCombatState.OrbSlots is hard-sized to 10 via [InlineArray(10)]. " +
                "Update OrbSlots10 and rebuild.");
        }

        // ── 7. Every concrete OrbModel subclass must be registered in
        //    SimOrbRegistry. Snapshot reads orbs through the registry; an
        //    unknown subclass would land as SimOrbType.None (= empty),
        //    silently dropping the orb from the simulation.
        //    We iterate AllAbstractModelSubtypes (not ModelDb.Orbs) because
        //    the curated Orbs list only ships canonical content; experimental
        //    types like GlassOrb are present in the assembly but not yet
        //    listed there. Mocks namespace is skipped (test fixtures only).
        List<string>? missingOrb = null;
        foreach (Type t in ModelDb.AllAbstractModelSubtypes)
        {
            if (!t.IsSubclassOf(typeof(OrbModel))) continue;
            if (t.IsAbstract) continue;
            string? ns = t.Namespace;
            if (ns != null && ns.EndsWith(".Orbs.Mocks", StringComparison.Ordinal)) continue;
            if (!SimOrbRegistry.TryGetIndex(t, out _))
            {
                missingOrb ??= new List<string>();
                missingOrb.Add(t.FullName ?? t.Name);
            }
        }
        if (missingOrb is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "SimCaps: OrbModel subclasses not registered in SimOrbRegistry: " +
                string.Join(", ", missingOrb) +
                ". Add typeof(...) → SimOrbType.Xxx entries and bump SimOrbType.Count.");
        }

        // All invariants hold. (Worst-case encounter is informational only.)
        _ = worstEncounter; _ = worstSlots;
    }
}
