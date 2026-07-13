using System;
using System.Runtime.CompilerServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Shared accessors for the sparse per-creature power bitmap+values pairs stored in the
/// frozen <see cref="CombatNodeBlob"/>. See <see cref="SimPowerSet"/> for the lookup scheme.
/// </summary>
internal static class SimPowerOps
{
    /// <summary>Apply <paramref name="delta"/> to the player's power <paramref name="type"/>,
    /// creating or removing the entry as needed. See <see cref="ApplyDelta"/> for the exact rule.</summary>
    public static void ApplyPlayerDelta(CombatNodeBlob blob, int type, int delta)
        => ApplyDelta(ref blob.PlayerPowerBitmap, blob.PlayerPowerValues, type, delta);

    /// <summary>Apply <paramref name="delta"/> to enemy <paramref name="idx"/>'s power
    /// <paramref name="type"/>. See <see cref="ApplyDelta"/> for the exact rule.</summary>
    public static void ApplyEnemyDelta(CombatNodeBlob blob, int idx, int type, int delta)
        => ApplyDelta(ref blob.EnemyPowerBitmaps[idx], GetEnemyValues(blob, idx), type, delta);

    /// <summary>Apply <paramref name="delta"/> to Osty's power <paramref name="type"/>.
    /// See <see cref="ApplyDelta"/> for the exact rule.</summary>
    public static void ApplyOstyDelta(CombatNodeBlob blob, int type, int delta)
        => ApplyDelta(ref blob.OstyPowerBitmap, blob.OstyPowerValues, type, delta);

    /// <summary>
    /// Mirrors the net effect of <c>PowerCmd.Apply</c> / <c>PowerCmd.ModifyAmount</c> on a single
    /// power's stored amount — NOT the full pipeline (no Hook.ModifyPowerAmountGiven/Received, no
    /// multiplayer scaling; those are the caller's responsibility, same division of labor as
    /// <see cref="SimDamage.Compute"/> vs. the card-level damage pipeline).
    ///
    /// Confirmed against game_source/MegaCrit.Sts2.Core.Commands/PowerCmd.cs and
    /// MegaCrit.Sts2.Core.Models/PowerModel.cs:
    ///  - First-time application (power not currently present): created with amount = delta
    ///    directly, with NO removal check applied even if delta is negative — PowerModel.ApplyInternal
    ///    (the fresh-instance path) never calls ShouldRemoveDueToAmount, only SetAmount. The one
    ///    exception is delta == 0, a no-op, matching PowerCmd.Apply(PowerModel,...)'s
    ///    `amount == 0m` early-return.
    ///  - Power already present: newAmount = existing + delta; then removed if
    ///    PowerModel.ShouldRemoveDueToAmount() says so, i.e.
    ///    <see cref="SimPowerAllowNegative.Get"/>(type) ? newAmount == 0 : newAmount &lt;= 0.
    ///
    /// Fail-loud: throws if a brand-new power would overflow <see cref="SimPowerSet.ValueCap"/>,
    /// or if the resulting amount doesn't fit in the blob's <c>short</c> slot — never silently
    /// truncates or drops the write.
    ///
    /// ArtifactPower check (confirmed against ArtifactPower.cs's TryModifyPowerAmountReceived):
    /// if this target has Artifact and <paramref name="type"/> is a Debuff and <paramref name="delta"/>
    /// is positive, the entire application is blocked and one Artifact stack is consumed instead —
    /// this applies regardless of source (card or monster move), so it lives here centrally rather
    /// than in each card/monster writer.
    ///
    /// CAUTION for callers building a synthetic/seed blob (e.g. SimCardEffectVerifier's scratch
    /// state): this function represents a real game EVENT ("apply this delta right now"), not a
    /// "set this creature's existing amount" primitive. If a caller seeds Artifact onto a target
    /// and THEN seeds a pre-existing Debuff stack via this same function, the Debuff seed will be
    /// incorrectly blocked and silently consume the seeded Artifact. Nothing in this codebase does
    /// that today (no caller seeds Artifact at all yet), but if the verifier is ever extended to
    /// seed Artifact too, seed debuffs BEFORE Artifact, or add a raw non-blocking seed path instead.
    /// </summary>
    private static void ApplyDelta(ref SimPowerBitmap bitmap, Span<short> values, int type, int delta)
    {
        if (delta > 0 && SimPowerCategoryRegistry.IsDebuff(type)
            && SimPowerSet.TryGetAmount(bitmap, values, SimPowerType.Artifact, out short artifactAmt) && artifactAmt > 0)
        {
            ApplyDelta(ref bitmap, values, SimPowerType.Artifact, -1);
            return;
        }

        bool exists = SimPowerSet.Test(bitmap, type);
        int pos = SimPowerSet.PositionOf(bitmap, type);

        if (!exists)
        {
            if (delta == 0) return;
            int count = SimPowerSet.Count(bitmap);
            if (count >= SimPowerSet.ValueCap)
            {
                throw new InvalidOperationException(
                    $"SimPowerOps: power-set overflow (cap={SimPowerSet.ValueCap}) applying new type {type}.");
            }
            if (delta < short.MinValue || delta > short.MaxValue)
            {
                throw new InvalidOperationException(
                    $"SimPowerOps: initial amount {delta} for type {type} doesn't fit in a short slot.");
            }
            SimPowerSet.Set(ref bitmap, type);
            SimPowerSet.InsertValue(values, pos, count, (short)delta);
            return;
        }

        int newAmount = values[pos] + delta;
        bool shouldRemove = SimPowerAllowNegative.Get(type) ? newAmount == 0 : newAmount <= 0;
        if (shouldRemove)
        {
            int count = SimPowerSet.Count(bitmap);
            SimPowerSet.RemoveValue(values, pos, count);
            SimPowerSet.Clear(ref bitmap, type);
            return;
        }
        if (newAmount < short.MinValue || newAmount > short.MaxValue)
        {
            throw new InvalidOperationException(
                $"SimPowerOps: resulting amount {newAmount} for type {type} doesn't fit in a short slot.");
        }
        values[pos] = (short)newAmount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetPlayerAmount(CombatNodeBlob blob, int type, out short amount)
        => SimPowerSet.TryGetAmount(blob.PlayerPowerBitmap, blob.PlayerPowerValues, type, out amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetEnemyAmount(CombatNodeBlob blob, int idx, int type, out short amount)
        => SimPowerSet.TryGetAmount(blob.EnemyPowerBitmaps[idx], GetEnemyValues(blob, idx), type, out amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetOstyAmount(CombatNodeBlob blob, int type, out short amount)
        => SimPowerSet.TryGetAmount(blob.OstyPowerBitmap, blob.OstyPowerValues, type, out amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<short> GetEnemyValues(CombatNodeBlob blob, int idx)
        => blob.EnemyPowerValues.Slice(idx * SimPowerSet.ValueCap, SimPowerSet.ValueCap);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetPlayerInternal(CombatNodeBlob blob)
        => ref blob.PlayerPowerInternal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref SimPowerInternal GetEnemyInternal(CombatNodeBlob blob, int idx)
        => ref blob.EnemyPowerInternal[idx];
}
