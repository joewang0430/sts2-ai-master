using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// Self-checking safety net for <see cref="SimMonsterMoveEffects"/>: there is no live game field
/// to diff a prediction against BEFORE a move fires (that is the whole reason
/// SimMonsterMoveEffects has to hand-replicate each monster's formula in the first place — see
/// dev_docs/Enemy_Intent_Payload_Backlog.md). But AFTER a predicted move actually resolves, its
/// real effect becomes observable (Block/Power values changed by some real amount) — so instead of
/// verifying "before it happens" (impossible without the full simulation engine), this verifies
/// "after it happened, did the prediction come true".
///
/// Mechanism: remembers, per enemy, the prediction made for whatever move it was telegraphing last
/// time we looked, plus its Block/power amounts at that moment. Next time we look, if the enemy's
/// <c>NextMove.StateId</c> has changed (meaning the previously-telegraphed move actually fired and
/// the state machine advanced), compute the real delta and compare it to what was predicted.
///
/// Deliberately NOT wired into SimCaps or CombatBlobVerifier's pass/fail gate — a mismatch here
/// means "one of the ~85 SimMonsterMoveEffects formulas is wrong", not "the mod is broken"; it
/// should surface as a visible, readable warning, not a startup crash.
/// </summary>
internal static class SimMoveEffectVerifier
{
    private sealed class Tracked
    {
        public string StateId = "";
        public int Block;
        public readonly Dictionary<ushort, short> PowerAmounts = new();
        public SimMoveEffect[] Effects = Array.Empty<SimMoveEffect>();
        public int EffectCount;

        /// <summary>
        /// The verdict text for "how did the move that got us INTO StateId turn out" — computed
        /// once, the moment StateId is first observed, then held here and re-returned by every
        /// subsequent Observe() call until StateId changes again. Without this, the verdict only
        /// existed for the exact one UI refresh where the transition was first detected — often
        /// well under a second, invisible in practice (this was a real bug, caught 2026-07-08: the
        /// prediction was correct and the check ran, but nobody could ever see the result flash by).
        /// </summary>
        public string LastReport = "";
    }

    private static readonly Dictionary<Creature, Tracked> s_tracked = new(ReferenceEqualityComparer.Instance);

    /// <summary>Call on combat start/end so dead creatures from a previous fight never linger.</summary>
    public static void Clear() => s_tracked.Clear();

    /// <summary>
    /// Call once per enemy per UI refresh, with THIS round's freshly-computed prediction (from
    /// <see cref="SimMonsterMoveEffects.Write"/>). Returns "Verify: ..." lines reporting on
    /// whatever was predicted for the PREVIOUS telegraphed move, now that it has resolved — and
    /// keeps returning that same verdict on every subsequent call (not just once) until the enemy's
    /// state actually moves on again. Returns an empty string only when there has never been a
    /// resolved, checkable prediction yet for this enemy.
    /// </summary>
    public static string Observe(Creature enemy, string currentStateId, ReadOnlySpan<SimMoveEffect> currentEffects, int currentEffectCount)
    {
        if (s_tracked.TryGetValue(enemy, out Tracked? prev) && prev.StateId == currentStateId)
        {
            // Nothing new happened since last look — keep showing whatever verdict we already have.
            return prev.LastReport;
        }

        string report = (prev != null && prev.EffectCount > 0) ? CheckPrediction(enemy, prev) : (prev?.LastReport ?? "");

        Tracked next = prev ?? new Tracked();
        next.StateId = currentStateId;
        next.Block = enemy.Block;
        next.EffectCount = currentEffectCount;
        if (next.Effects.Length < currentEffectCount)
            next.Effects = new SimMoveEffect[currentEffectCount];
        for (int i = 0; i < currentEffectCount; i++)
            next.Effects[i] = currentEffects[i];

        next.PowerAmounts.Clear();
        for (int i = 0; i < currentEffectCount; i++)
        {
            if (currentEffects[i].Kind == (byte)SimMoveEffectKind.PowerApply)
                next.PowerAmounts[currentEffects[i].PowerType] = ReadLivePowerAmount(enemy, currentEffects[i].PowerType);
        }

        next.LastReport = report;
        s_tracked[enemy] = next;
        return report;
    }

    private static string CheckPrediction(Creature enemy, Tracked prev)
    {
        var sb = new StringBuilder(64);
        for (int i = 0; i < prev.EffectCount; i++)
        {
            SimMoveEffect e = prev.Effects[i];
            switch ((SimMoveEffectKind)e.Kind)
            {
                case SimMoveEffectKind.Block:
                {
                    int actual = enemy.Block - prev.Block;
                    AppendVerdict(sb, "Block", e.Amount, actual);
                    break;
                }
                case SimMoveEffectKind.PowerApply:
                {
                    int before = prev.PowerAmounts.GetValueOrDefault(e.PowerType);
                    int after = ReadLivePowerAmount(enemy, e.PowerType);
                    int actual = after - before;
                    AppendVerdict(sb, PowerTypeName(e.PowerType), e.Amount, actual);
                    break;
                }
                case SimMoveEffectKind.Heal:
                    // Not verified: HP also changes from unrelated damage taken in the same window,
                    // so a simple before/after delta is too noisy to trust. Revisit if this becomes
                    // a real problem once a Heal-using monster is implemented.
                    break;
                case SimMoveEffectKind.Summon:
                    // Not verified: would need to track enemy-slot-count deltas, a different
                    // mechanism than the Block/Power before/after tracking this class does.
                    break;
                case SimMoveEffectKind.CardInject:
                case SimMoveEffectKind.CardInjectHand:
                case SimMoveEffectKind.CardInjectDrawRandom:
                case SimMoveEffectKind.CardInjectDiscardRandom:
                    // Not verified: would need to track player pile-content deltas, a different
                    // mechanism than the Block/Power before/after tracking this class does.
                    break;
            }
        }
        return sb.ToString();
    }

    private static void AppendVerdict(StringBuilder sb, string label, int predicted, int actual)
    {
        bool ok = predicted == actual;
        sb.AppendLine(ok
            ? $"  Verify: ✓ {label}+{predicted} confirmed"
            : $"  Verify: ✗ {label} predicted+{predicted} actual{(actual >= 0 ? "+" : "")}{actual}");
    }

    private static short ReadLivePowerAmount(Creature enemy, ushort powerType)
    {
        foreach (PowerModel p in enemy.Powers)
        {
            if (SimPowerRegistry.TryGetIndex(p.GetType(), out int idx) && idx == powerType)
                return (short)p.Amount;
        }
        return 0;
    }

    private static string PowerTypeName(ushort powerType) => SimPowerTypeNames.GetName(powerType);
}
