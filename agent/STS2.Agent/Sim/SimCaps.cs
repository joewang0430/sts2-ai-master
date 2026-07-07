using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// One-shot startup invariants check. Asserts that every assumption baked
/// into the blob layout still holds
/// against the live game's content database. Any violation throws and the
/// AI subsystem refuses to run — better a loud crash on boot than silent
/// truncation in the search hot path.
///
/// Cost: O(N) over models, but pays itself off forever — no per-snapshot
/// bounds checks needed downstream.
///
/// Called lazily from live snapshot entry points, NOT from
/// <c>ModEntry.Initialize()</c>:
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
        // ── 1. CardPile.MaxCardsInHand must equal CombatSimLayout.HandCap (10).
        //    The blob hand slice is fixed-size; if the game ever
        //    raised the cap, snapshot would silently truncate.
        if (CardPile.MaxCardsInHand != CombatSimLayout.HandCap)
        {
            throw new InvalidOperationException(
                $"SimCaps: CardPile.MaxCardsInHand={CardPile.MaxCardsInHand} but " +
            $"CombatSimLayout.HandCap={CombatSimLayout.HandCap}. Update HandCap and rebuild.");
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
            if (n > CombatSimLayout.EnemyCap)
            {
                throw new InvalidOperationException(
                    $"SimCaps: encounter '{enc.Id}' has {n} slots but " +
                    $"CombatSimLayout.EnemyCap={CombatSimLayout.EnemyCap}. Raise EnemyCap.");
            }
        }

        // ── 3. Every concrete CardModel must have MaxUpgradeLevel ≤ 1.
        //    The blob card layout encodes upgrade as bit 15 of a ushort card id; only
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

        // ── 6. Every concrete AfflictionModel must be registered in
        //    SimAfflictionRegistry. There is one live affliction slot per card,
        //    so an unknown subtype would silently disappear from snapshot state.
        List<string>? missingAff = null;
        foreach (AfflictionModel aff in ModelDb.DebugAfflictions)
        {
            Type t = aff.GetType();
            string? ns = t.Namespace;
            if (ns != null && ns.EndsWith(".Afflictions.Mocks", StringComparison.Ordinal)) continue;
            if (!SimAfflictionRegistry.TryGetIndex(t, out _))
            {
                missingAff ??= new List<string>();
                missingAff.Add(t.FullName ?? t.Name);
            }
        }
        if (missingAff is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "SimCaps: AfflictionModel subclasses not registered in SimAfflictionRegistry: " +
                string.Join(", ", missingAff) +
                ". Add typeof(...) → SimAfflictionType.Xxx entries and bump SimAfflictionType.Count.");
        }

        // ── 7. Orb queue capacity must equal the blob's OrbSlots10
        //    inline length (10). If the game ever raises maxCapacity, the
        //    snapshot loop would silently truncate the queue.
        if (OrbQueue.maxCapacity != 10)
        {
            throw new InvalidOperationException(
                $"SimCaps: OrbQueue.maxCapacity={OrbQueue.maxCapacity} but " +
            "OrbSlots10 is hard-sized to 10 via [InlineArray(10)]. " +
                "Update OrbSlots10 and rebuild.");
        }

        // ── 8. Every concrete OrbModel subclass must be registered in
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
            // Game source uses *.Orbs.Mock (singular) for orb test fixtures,
            // unlike *.Powers.Mocks / *.Enchantments.Mocks (plural). Match both
            // so a future rename does not silently re-include test orbs.
            if (ns != null && (ns.EndsWith(".Orbs.Mock", StringComparison.Ordinal)
                            || ns.EndsWith(".Orbs.Mocks", StringComparison.Ordinal))) continue;
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

        // ── 8. Force-resolve every reflection FieldInfo in SimPowerInternalReader.
        //    Each field handle is a `static readonly FieldInfo` initialized via
        //    typeof(T).GetField(...) ?? throw — so touching the type's static
        //    ctor verifies that all 13 hidden fields (FeralPower.Data.zeroCostAttacksPlayed,
        //    JugglingPower.Data.attacksPlayedThisTurn, OrbitPower.Data.energySpent,
        //    AutomationPower.Data.cardsLeft, IllusionPower.Data.isReviving,
        //    HardenedShellPower.Data.damageReceivedThisTurn, OutbreakPower.Data.timesPoisoned,
        //    VoidFormPower.Data.cardsPlayedThisTurn, PowerModel._internalData,
        //    TenderPower._cardsPlayedThisTurn, SlothPower._cardsPlayedThisTurn,
        //    NemesisPower._shouldApplyIntangible, RitualPower._wasJustAppliedByEnemy)
        //    are still present in the live game assembly. Without this trigger
        //    the resolution would happen lazily in WritePowerInternals only when
        //    a real instance of the power is encountered, so a shipped game
        //    rename could go undetected for many combats.
        //
        //    `RuntimeHelpers.RunClassConstructor` runs the static ctor exactly
        //    once and propagates any thrown exception to here.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
            typeof(SimPowerInternalReader).TypeHandle);

        // ── 9. Force initialization of RandomStateOps. Its static field
        //    chain resolves MegaRandom's private Xoshiro256** state fields
        //    (_s0/_s1/_s2/_s3) plus the game's Rng._random bridge. Blob snapshot
        //    uses this for all 8 combat RNG streams; if the game-side layout
        //    drifts, fail at startup rather than on first combat diff.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
            typeof(RandomStateOps).TypeHandle);

        // ── 10. Force initialization of CombatNodeBlobSnapshot so the
        //    CardEnergyCost._localModifiers reflection handle is resolved up
        //    front. Blob snapshot depends on this exact field for card-energy
        //    sidecar capture; a rename should crash at startup, not only when
        //    the first combat hand is read.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
            typeof(CombatNodeBlobSnapshot).TypeHandle);

        // ── 11. Force initialization of SimMonsterStateRegistry. Its two
        //    `static readonly FieldInfo` slots resolve
        //    MonsterMoveStateMachine._currentState and ._performedFirstMove
        //    via reflection with `?? throw`. Without an explicit nudge they
        //    only fire on first SnapshotMoveSM call — meaning a game-source
        //    rename of either field would only blow up after the AI starts
        //    snapshotting a real combat. Force-running the cctor here turns
        //    that latent failure into a startup-time crash.
        //
        //    Note: the per-monster-Type table cache (and its History16 /
        //    EverUsedBitset capacity asserts) is still genuinely lazy — those
        //    can only run once a live monster instance is on the field.
        //    First-encounter validation is the best we can do without
        //    instantiating every MonsterModel ourselves.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
            typeof(SimMonsterStateRegistry).TypeHandle);

        // All invariants hold. (Worst-case encounter is informational only.)
        _ = worstEncounter; _ = worstSlots;
    }
}
