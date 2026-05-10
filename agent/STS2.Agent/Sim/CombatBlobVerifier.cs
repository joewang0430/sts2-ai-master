using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Random;

namespace STS2.Agent.Sim;

internal readonly record struct CombatBlobVerificationLine(string Text, bool Pairable);

internal sealed class CombatBlobVerificationSection(string title)
{
    public string Title { get; } = title;
    public bool AllOk { get; set; }
    public List<CombatBlobVerificationLine> Lines { get; } = new();
}

internal sealed class CombatBlobVerificationReport
{
    public CombatBlobVerificationSection Sim { get; } = new("── BLOB VS LIVE ─────────");
    public CombatBlobVerificationSection Blob { get; } = new("── BLOB CHECKS ─────────");
}

/// <summary>
/// Builds human-readable blob verification output for a live combat state.
/// This is kept separate from the UI layer so the same verification logic can
/// be reused outside CombatDebugOverlay.
/// </summary>
internal static class CombatBlobVerifier
{
    private static readonly CombatNodeBlob s_blob = new();
    private static readonly CombatNodeBlob s_blobScratch = new();
    private static readonly FieldInfo s_cardEnergyLocalModifiersField =
        typeof(CardEnergyCost).GetField("_localModifiers", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatBlobVerifier: CardEnergyCost._localModifiers field not found.");

    private static Dictionary<ushort, string>? s_cardNameById;

    public static CombatBlobVerificationReport BuildSnapshotReport(CombatState state)
    {
        CombatBlobVerificationReport report = new();
        CombatBlobVerificationSection sim = report.Sim;
        CombatBlobVerificationSection blob = report.Blob;

        bool simAllOk = true;
        bool blobAllOk = true;

        try
        {
            CombatNodeBlobSnapshot.WriteV1FromCombatState(state, s_blob);
        }
        catch (Exception ex)
        {
            simAllOk = false;
            blobAllOk = false;
            AddStandalone(sim, $"✗ Blob.LiveWrite threw: {ex.Message}");
            AddStandalone(blob, $"✗ Blob.LiveWrite threw: {ex.Message}");
            sim.AllOk = simAllOk;
            blob.AllOk = blobAllOk;
            return report;
        }

        Player? me = LocalContext.GetMe(state);
        if (me is null)
        {
            AddStandalone(sim, "(no local player)");
            DiffBlobInternalChecks(blob, ref blobAllOk);
            sim.AllOk = simAllOk;
            blob.AllOk = blobAllOk;
            return report;
        }

        Creature pc = me.Creature;
        PlayerCombatState? pcs = me.PlayerCombatState;

        void Cmp(string name, object blobVal, object liveVal)
        {
            bool ok = blobVal.ToString() == liveVal.ToString();
            if (!ok) simAllOk = false;
            if (ok) AddData(sim, $"✓ {name}={blobVal}");
            else AddStandalone(sim, $"✗ {name}: blob={blobVal} live={liveVal}");
        }

        Cmp("Round", s_blob.Round, state.RoundNumber);
        Cmp("Side", s_blob.CurrentSide, (byte)state.CurrentSide);
        Cmp("HP", s_blob.PlayerHp, pc.CurrentHp);
        Cmp("MaxHP", s_blob.PlayerMaxHp, pc.MaxHp);
        Cmp("Block", s_blob.PlayerBlock, pc.Block);

        if (pcs is not null)
        {
            int simHandCount = s_blob.HandCount;
            int simDrawCount = s_blob.DrawCount;
            int simDiscCount = s_blob.DiscCount;
            int simExhaustCount = s_blob.ExhaustCount;
            Cmp("Energy", s_blob.Energy, pcs.Energy);
            Cmp("MaxEnergy", s_blob.MaxEnergy, pcs.MaxEnergy);
            Cmp("Stars", s_blob.PlayerStars, pcs.Stars);
            Cmp("HandN", simHandCount, pcs.Hand.Cards.Count);
            Cmp("DrawN", simDrawCount, pcs.DrawPile.Cards.Count);
            Cmp("DiscN", simDiscCount, pcs.DiscardPile.Cards.Count);
            Cmp("ExhN", simExhaustCount, pcs.ExhaustPile.Cards.Count);
            DiffHistoryCounters(sim, state, me, ref simAllOk);
            DiffOrbs(sim, pcs, ref simAllOk);
            DiffOsty(sim, pcs, ref simAllOk);

            Span<SimCard> blobHand = s_blob.HandCards;
            int hn = Math.Min(simHandCount, pcs.Hand.Cards.Count);
            for (int i = 0; i < hn; i++)
            {
                SimCard sc = blobHand[i];
                CardModel liveCard = pcs.Hand.Cards[i];
                bool simU = sc.IsUpgraded;
                bool livU = liveCard.IsUpgraded;
                ushort sid = sc.BaseCardId;
                string simN = ReverseCardName(sid);
                string livN = liveCard.GetType().Name;

                int simLocalCost = SimCardEnergyOps.GetWithLocalModifiers(s_blob, sc);
                bool simHasLocal = SimCardEnergyOps.GetModifierCount(s_blob, sc) > 0;
                bool simX = sc.HasEnergyCostX;
                int simCapturedX = !simX ? 0 : SimCardEnergyOps.GetCapturedXValue(s_blob, sc);
                bool idOk = simN == livN && simU == livU;
                bool energyOk = BlobCardEnergyMatchesLive(s_blob, sc, liveCard, out string? energyReason);
                bool historyOk = BlobCardHistoryFlagsMatchLive(state, sc, liveCard, out string? historyReason);

                bool ok = idOk && energyOk && historyOk;
                if (!ok) simAllOk = false;
                if (ok)
                {
                    AddData(sim,
                        $"✓ Hand[{i}]={simN}{(simU ? "+" : "")}" +
                        $" costL={simLocalCost}{(simHasLocal ? "*" : "")}{(simX ? $" X={simCapturedX}" : string.Empty)}");
                }
                else
                {
                    AddStandalone(sim,
                        $"✗ Hand[{i}]: sim={simN}{(simU ? "+" : "")} live={livN}{(livU ? "+" : "")}" +
                        (energyOk ? string.Empty : $" energy={energyReason}") +
                        (historyOk ? string.Empty : $" hist={historyReason}"));
                }
            }

            DiffPile(sim, state, "Draw", s_blob.DrawCards.Slice(0, simDrawCount), pcs.DrawPile.Cards, ref simAllOk);
            DiffPile(sim, state, "Disc", s_blob.DiscCards.Slice(0, simDiscCount), pcs.DiscardPile.Cards, ref simAllOk);
            DiffPile(sim, state, "Exh", s_blob.ExhaustCards.Slice(0, simExhaustCount), pcs.ExhaustPile.Cards, ref simAllOk);
        }

        DiffPowers(sim, "P.Pwr", pc.Powers, SimPowerOps.GetPlayerRow(s_blob), ref simAllOk);
        DiffValue(sim, "P.PwrI", s_blob.PlayerPowerInternal, ReadLivePowerInternal(pc.Powers), ref simAllOk);

        int simEnemyCount = s_blob.EnemyCount;
        Cmp("EnemyN", simEnemyCount, state.Enemies.Count);
        int en = Math.Min(simEnemyCount, state.Enemies.Count);
        for (int i = 0; i < en; i++)
        {
            Creature e = state.Enemies[i];
            Cmp($"E{i}.HP", s_blob.EnemyHp[i], e.CurrentHp);
            Cmp($"E{i}.MaxHP", s_blob.EnemyMaxHp[i], e.MaxHp);
            Cmp($"E{i}.Block", s_blob.EnemyBlock[i], e.Block);

            DiffIntent(sim, i, e, ref simAllOk);

            DiffPowers(sim, $"E{i}.Pwr", e.Powers, SimPowerOps.GetEnemyRow(s_blob, i), ref simAllOk);
            DiffValue(sim, $"E{i}.PwrI", s_blob.EnemyPowerInternal[i], ReadLivePowerInternal(e.Powers), ref simAllOk);

            CaptureLiveMoveSm(e, out SimEnemyMoveSM liveMoveSm, out ushort liveMoveTableHandle);
            DiffValue(sim, $"E{i}.Move", s_blob.EnemyMoveSM[i], liveMoveSm, ref simAllOk);
            Cmp($"E{i}.Tbl", s_blob.EnemyMoveTableHandles[i], liveMoveTableHandle);
        }

        DiffAllRng(sim, state, ref simAllOk);
        DiffBlobInternalChecks(blob, ref blobAllOk);

        sim.AllOk = simAllOk;
        blob.AllOk = blobAllOk;
        return report;
    }

    public static void BuildSnapshotDiffTexts(CombatState state, out string simText, out string blobText)
    {
        CombatBlobVerificationReport report = BuildSnapshotReport(state);
        simText = FormatSection(report.Sim);
        blobText = FormatSection(report.Blob);
    }

    public static string FormatSection(CombatBlobVerificationSection section)
    {
        const int CellW = 22;

        var sb = new StringBuilder(256);
        sb.AppendLine();
        sb.AppendLine(section.Title);
        if (section.AllOk)
            sb.AppendLine("✓ ALL OK");

        string? pending = null;

        for (int i = 0; i < section.Lines.Count; i++)
        {
            CombatBlobVerificationLine line = section.Lines[i];

            if (!line.Pairable)
            {
                if (pending != null)
                {
                    sb.AppendLine(pending);
                    pending = null;
                }
                sb.AppendLine(line.Text);
                continue;
            }

            if (pending == null) pending = line.Text;
            else
            {
                sb.Append(pending);
                int pad = CellW - pending.Length;
                if (pad > 0) sb.Append(' ', pad);
                sb.Append(" │ ").AppendLine(line.Text);
                pending = null;
            }
        }

        if (pending != null) sb.AppendLine(pending);
        return sb.ToString();
    }

    private static void AddData(CombatBlobVerificationSection section, string text)
        => section.Lines.Add(new CombatBlobVerificationLine(text, Pairable: true));

    private static void AddStandalone(CombatBlobVerificationSection section, string text)
        => section.Lines.Add(new CombatBlobVerificationLine(text, Pairable: false));

    private static void AddBufferedStandaloneLines(CombatBlobVerificationSection section, StringBuilder lines)
    {
        if (lines.Length == 0)
            return;

        string[] split = lines.ToString().Split('\n');
        for (int i = 0; i < split.Length; i++)
        {
            string line = split[i].TrimEnd('\r');
            if (line.Length == 0)
                continue;

            AddStandalone(section, line);
        }
    }

    private static void DiffPile(
        CombatBlobVerificationSection section, CombatState state, string tag,
        ReadOnlySpan<SimCard> simSlice,
        IReadOnlyList<CardModel> live,
        ref bool allOk)
    {
        int simCount = simSlice.Length;
        int n = Math.Min(simCount, live.Count);
        int diffs = 0;
        var firstMismatches = new StringBuilder(64);
        for (int i = 0; i < n; i++)
        {
            SimCard sc = simSlice[i];
            bool simU = sc.IsUpgraded;
            ushort sid = sc.BaseCardId;
            string simN = ReverseCardName(sid);
            string livN = live[i].GetType().Name;
            bool livU = live[i].IsUpgraded;
            bool idOk = simN == livN && simU == livU;
            bool energyOk = BlobCardEnergyMatchesLive(s_blob, sc, live[i], out string? energyReason);
            bool historyOk = BlobCardHistoryFlagsMatchLive(state, sc, live[i], out string? historyReason);
            if (!idOk || !energyOk || !historyOk)
            {
                if (diffs < 3)
                    firstMismatches.AppendLine(
                        $"  ✗ {tag}[{i}]: sim={simN}{(simU ? "+" : "")} live={livN}{(livU ? "+" : "")}" +
                        (energyOk ? string.Empty : $" energy={energyReason}") +
                        (historyOk ? string.Empty : $" hist={historyReason}"));
                diffs++;
            }
        }
        if (diffs == 0 && simCount == live.Count)
        {
            AddData(section, $"✓ {tag}({n})");
        }
        else
        {
            allOk = false;
            AddStandalone(section, $"✗ {tag}: {diffs} mismatch(es), simN={simCount} liveN={live.Count}");
            AddBufferedStandaloneLines(section, firstMismatches);
        }
    }

    private static void DiffBlobInternalChecks(CombatBlobVerificationSection section, ref bool allOk)
    {
        CheckBlobCondition(section, "Bytes", s_blob.ByteLength == CombatSchemaV1.TotalBytes,
            $"blob={s_blob.ByteLength} schema={CombatSchemaV1.TotalBytes}", ref allOk);
        CheckBlobCondition(section, "EnemyN", s_blob.EnemyCount <= CombatSimLayout.EnemyCap,
            $"count={s_blob.EnemyCount} cap={CombatSimLayout.EnemyCap}", ref allOk);
        CheckBlobCondition(section, "OrbCap", s_blob.OrbCount <= s_blob.OrbCapacity && s_blob.OrbCapacity <= 10,
            $"count={s_blob.OrbCount} cap={s_blob.OrbCapacity}", ref allOk);
        CheckBlobCondition(section, "CardInst", s_blob.CardInstanceCount <= CombatSimLayout.CardInstanceCap,
            $"count={s_blob.CardInstanceCount} cap={CombatSimLayout.CardInstanceCap}", ref allOk);
        CheckBlobCondition(section, "ModUsed", s_blob.CardEnergyModifierUsed <= CombatSchemaV1.Cards.CardEnergyModifierCap,
            $"used={s_blob.CardEnergyModifierUsed} cap={CombatSchemaV1.Cards.CardEnergyModifierCap}", ref allOk);
        DiffBlobMoveTableHandles(section, ref allOk);
        DiffBlobCardEnergySlices(section, ref allOk);
        DiffBlobCleanupMutations(section, ref allOk);
    }

    private static void DiffBlobCleanupMutations(CombatBlobVerificationSection section, ref bool allOk)
    {
        int handCount = s_blob.HandCount;
        DiffBlobCleanupMutationSet(section, "B.PlayCln", handCount, endOfTurn: false, ref allOk);
        DiffBlobCleanupMutationSet(section, "B.TurnCln", handCount, endOfTurn: true, ref allOk);
    }

    private static void DiffBlobCleanupMutationSet(
        CombatBlobVerificationSection section,
        string tag,
        int handCount,
        bool endOfTurn,
        ref bool allOk)
    {
        if (handCount == 0)
        {
            AddData(section, $"✓ {tag}(0)");
            return;
        }

        int diffs = 0;
        var firstMismatches = new StringBuilder(96);
        for (int i = 0; i < handCount; i++)
        {
            s_blobScratch.CopyFrom(s_blob);

            SimCard blobCard = s_blobScratch.HandCards[i];
            ushort beforeCount = s_blobScratch.CardEnergyModifierCount[blobCard.InstanceId];
            bool blobChanged = endOfTurn
                ? SimCardEnergyOps.EndOfTurnCleanup(s_blobScratch, blobCard)
                : SimCardEnergyOps.AfterCardPlayedCleanup(s_blobScratch, blobCard);
            ushort afterCount = s_blobScratch.CardEnergyModifierCount[blobCard.InstanceId];
            bool secondPassChanged = endOfTurn
                ? SimCardEnergyOps.EndOfTurnCleanup(s_blobScratch, blobCard)
                : SimCardEnergyOps.AfterCardPlayedCleanup(s_blobScratch, blobCard);

            string? reason = null;
            bool ok = afterCount <= beforeCount
                && !secondPassChanged
                && BlobCardEnergyInstanceCheck(s_blobScratch, blobCard, out reason);
            if (ok)
                continue;

            diffs++;
            if (diffs <= 3)
            {
                firstMismatches.AppendLine(
                    $"  ✗ {tag}[{i}]: changed={blobChanged} second={secondPassChanged} before={beforeCount} after={afterCount} " +
                    $"card={ReverseCardName(blobCard.BaseCardId)}#{blobCard.InstanceId} {reason}");
            }
        }

        if (diffs == 0)
        {
            AddData(section, $"✓ {tag}({handCount})");
            return;
        }

        allOk = false;
        AddStandalone(section, $"✗ {tag}: {diffs} diff(s)");
        AddBufferedStandaloneLines(section, firstMismatches);
    }

    private static void DiffOrbs(CombatBlobVerificationSection section, PlayerCombatState pcs, ref bool allOk)
    {
        var queue = pcs.OrbQueue;
        var orbs = queue.Orbs;
        Span<ushort> liveSlots = stackalloc ushort[10];
        liveSlots.Clear();
        for (int i = 0; i < orbs.Count; i++)
        {
            var orb = orbs[i];
            byte type = SimOrbRegistry.GetIndexOrNone(orb.GetType());
            int mut = orb switch
            {
                DarkOrb dark => SimOrbRegistry.ReadDarkEvokeVal(dark),
                GlassOrb glass => SimOrbRegistry.ReadGlassPassiveVal(glass),
                _ => 0,
            };
            liveSlots[i] = SimOrb.Pack(type, mut);
        }

        DiffValue(section, "OrbN", s_blob.OrbCount, (byte)orbs.Count, ref allOk);
        DiffValue(section, "OrbCap", s_blob.OrbCapacity, (byte)queue.Capacity, ref allOk);
        DiffSpan<ushort>(section, "Orbs", s_blob.OrbSlots, liveSlots, ref allOk);
    }

    private static void DiffOsty(CombatBlobVerificationSection section, PlayerCombatState pcs, ref bool allOk)
    {
        Creature? livePet = pcs.GetPet<Osty>();
        SimPet liveOsty = default;
        IReadOnlyList<PowerModel> livePowers = Array.Empty<PowerModel>();
        if (livePet is not null)
        {
            liveOsty = new SimPet
            {
                CurrentHp = ClampU16(livePet.CurrentHp),
                MaxHp = ClampU16(livePet.MaxHp),
                Block = ClampU16(livePet.Block),
                Exists = 1,
            };
            livePowers = livePet.Powers;
        }

        DiffValue(section, "Osty", s_blob.Osty, liveOsty, ref allOk);
        DiffPowers(section, "Osty.Pwr", livePowers, s_blob.OstyPowers, ref allOk);
        DiffValue(section, "Osty.PwrI", s_blob.OstyPowerInternal, ReadLivePowerInternal(livePowers), ref allOk);
    }

    private static void DiffHistoryCounters(CombatBlobVerificationSection section, CombatState state, Player me, ref bool allOk)
    {
        SimHistoryCounters live = default;
        SimHistoryCounterOps.CaptureLive(state, me, ref live);
        ref SimHistoryCounters blob = ref s_blob.HistoryCounters;

        DiffValue(section, "Hist.StartN", blob.CardsStartedThisTurn, live.CardsStartedThisTurn, ref allOk);
        DiffValue(section, "Hist.Start1st", blob.FirstSeriesCardsStartedThisTurn, live.FirstSeriesCardsStartedThisTurn, ref allOk);
        DiffValue(section, "Hist.StartAtk", blob.AttacksStartedThisTurn, live.AttacksStartedThisTurn, ref allOk);
        DiffValue(section, "Hist.StartAtk0", blob.ZeroCostAttacksStartedThisTurn, live.ZeroCostAttacksStartedThisTurn, ref allOk);
        DiffValue(section, "Hist.FinN", blob.CardsFinishedThisTurn, live.CardsFinishedThisTurn, ref allOk);
        DiffValue(section, "Hist.FinAtk", blob.AttacksFinishedThisTurn, live.AttacksFinishedThisTurn, ref allOk);
        DiffValue(section, "Hist.FinSkl", blob.SkillsFinishedThisTurn, live.SkillsFinishedThisTurn, ref allOk);
        DiffValue(section, "Hist.E", blob.EnergySpentThisTurn, live.EnergySpentThisTurn, ref allOk);
        DiffValue(section, "Hist.DrawNH", blob.NonHandDrawsThisTurn, live.NonHandDrawsThisTurn, ref allOk);
        DiffValue(section, "Hist.DrawSt", blob.StatusDrawsThisTurn, live.StatusDrawsThisTurn, ref allOk);
        DiffValue(section, "Hist.Exh", blob.CardsExhaustedThisTurn, live.CardsExhaustedThisTurn, ref allOk);
        DiffValue(section, "Hist.Disc", blob.CardsDiscardedThisTurn, live.CardsDiscardedThisTurn, ref allOk);
        DiffValue(section, "Hist.Star+", blob.PositiveStarsGainedThisTurn, live.PositiveStarsGainedThisTurn, ref allOk);
        DiffValue(section, "Hist.Bound", blob.BoundAfflictionsThisTurn, live.BoundAfflictionsThisTurn, ref allOk);
        DiffValue(section, "Hist.Doom", blob.DoomAppliedByPlayerThisTurn, live.DoomAppliedByPlayerThisTurn, ref allOk);
        DiffValue(section, "Hist.Flags", blob.Flags, live.Flags, ref allOk);
    }

    private static bool SimLocalCostModifierEquals(in SimLocalCostModifier left, in SimLocalCostModifier right)
        => left.Amount == right.Amount
        && left.Flags == right.Flags;

    private static void DiffValue<T>(CombatBlobVerificationSection section, string tag, T blob, T live, ref bool allOk)
        where T : struct, IEquatable<T>
    {
        if (blob.Equals(live))
        {
            AddData(section, $"✓ {tag}={blob}");
            return;
        }

        allOk = false;
        AddStandalone(section, $"✗ {tag}: blob={blob} live={live}");
    }

    private static void DiffSpan<T>(CombatBlobVerificationSection section, string tag, ReadOnlySpan<T> blob, ReadOnlySpan<T> live, ref bool allOk)
        where T : unmanaged, IEquatable<T>
    {
        if (blob.Length == live.Length
            && MemoryMarshal.AsBytes(blob).SequenceEqual(MemoryMarshal.AsBytes(live)))
        {
            AddData(section, $"✓ {tag}({blob.Length})");
            return;
        }

        allOk = false;
        int diffs = blob.Length == live.Length ? 0 : 1;
        var msgs = new StringBuilder(96);
        int n = Math.Min(blob.Length, live.Length);
        for (int i = 0; i < n; i++)
        {
            if (blob[i].Equals(live[i]))
                continue;

            diffs++;
            if (diffs <= 3)
                msgs.AppendLine($"  ✗ {tag}[{i}]: blob={blob[i]} live={live[i]}");
        }

        AddStandalone(section, $"✗ {tag}: {diffs} diff(s), blobN={blob.Length} liveN={live.Length}");
        AddBufferedStandaloneLines(section, msgs);
    }

    private static SimPowerInternal ReadLivePowerInternal(IReadOnlyList<PowerModel> powers)
    {
        SimPowerInternal dst = default;
        for (int i = 0, n = powers.Count; i < n; i++)
        {
            switch (powers[i])
            {
                case FeralPower fp:
                    dst.FeralZeroCostAttacks = ClampU8(SimPowerInternalReader.ReadFeralZeroCostAttacks(fp));
                    break;
                case JugglingPower jp:
                    dst.JugglingAttacksThisTurn = ClampU8(SimPowerInternalReader.ReadJugglingAttacks(jp));
                    break;
                case VoidFormPower vp:
                    dst.VoidFormCardsThisTurn = ClampU8(SimPowerInternalReader.ReadVoidFormCards(vp));
                    break;
                case TenderPower tp:
                    dst.TenderCardsThisTurn = ClampU8(SimPowerInternalReader.ReadTenderCardsThisTurn(tp));
                    break;
                case SlothPower sp:
                    dst.SlothCardsThisTurn = ClampU8(SimPowerInternalReader.ReadSlothCardsThisTurn(sp));
                    break;
                case HardenedShellPower hp:
                    dst.HardenedShellDamageThisTurn = ClampU16(SimPowerInternalReader.ReadHardenedShellDamage(hp));
                    break;
                case OutbreakPower op:
                    dst.OutbreakTimesPoisoned = ClampU8(SimPowerInternalReader.ReadOutbreakTimesPoisoned(op));
                    break;
                case OrbitPower orbit:
                    dst.OrbitEnergySpent = ClampU16(SimPowerInternalReader.ReadOrbitEnergySpent(orbit));
                    dst.OrbitTriggerCount = ClampU16(SimPowerInternalReader.ReadOrbitTriggerCount(orbit));
                    break;
                case AutomationPower ap:
                    dst.AutomationCardsLeft = ClampU8(SimPowerInternalReader.ReadAutomationCardsLeft(ap));
                    break;
                case IllusionPower ip:
                    if (SimPowerInternalReader.ReadIllusionIsReviving(ip)) dst.Flags |= SimPowerInternal.FlagIllusionIsReviving;
                    break;
                case NemesisPower np:
                    if (SimPowerInternalReader.ReadNemesisShouldApplyIntangible(np)) dst.Flags |= SimPowerInternal.FlagNemesisShouldApplyIntangible;
                    break;
                case RitualPower rp:
                    if (SimPowerInternalReader.ReadRitualWasJustAppliedByEnemy(rp)) dst.Flags |= SimPowerInternal.FlagRitualWasJustAppliedByEnemy;
                    break;
            }
        }

        return dst;
    }

    private static void CaptureLiveMoveSm(Creature enemy, out SimEnemyMoveSM move, out ushort tableHandle)
    {
        move = default;
        tableHandle = 0;

        MonsterModel? monster = enemy.Monster;
        if (monster == null)
            return;

        MonsterStateTable? table = SimMonsterStateRegistry.GetOrBuild(monster);
        if (table == null)
            return;

        tableHandle = SimMonsterStateRegistry.GetHandle(table);

        var sm = monster.MoveStateMachine!;
        var current = SimMonsterStateRegistry.GetCurrentState(sm);
        if (!table.IdToIdx.TryGetValue(current.Id, out byte currentIdx))
            return;

        move.CurrentStateIdx = currentIdx;
        if (SimMonsterStateRegistry.GetPerformedFirstMove(sm))
            move.Flags |= SimEnemyMoveSM.FlagPerformedFirstMove;

        var log = sm.StateLog;
        uint bitset = 0u;
        for (int i = 0; i < log.Count; i++)
        {
            if (table.IdToIdx.TryGetValue(log[i].Id, out byte stateIdx))
                bitset |= 1u << stateIdx;
        }
        move.EverUsedBitset = bitset;

        int keep = log.Count < SimEnemyMoveSM.HistoryCap ? log.Count : SimEnemyMoveSM.HistoryCap;
        int start = log.Count - keep;
        for (int i = 0; i < keep; i++)
        {
            if (table.IdToIdx.TryGetValue(log[start + i].Id, out byte stateIdx))
                move.History[i] = stateIdx;
            else
                move.History[i] = 0xFF;
        }
        move.HistoryCount = (byte)keep;
        move.HistoryHead = (byte)(keep == SimEnemyMoveSM.HistoryCap ? 0 : keep);
        move.IllusionFollowUpIdx = SimEnemyMoveSM.NoFollowUp;

        for (int i = 0, n = enemy.Powers.Count; i < n; i++)
        {
            if (enemy.Powers[i] is not IllusionPower illusion)
                continue;

            string? followUpId = illusion.FollowUpStateId;
            if (followUpId != null && table.IdToIdx.TryGetValue(followUpId, out byte followUpIdx))
            {
                move.IllusionFollowUpIdx = followUpIdx;
                move.Flags |= SimEnemyMoveSM.FlagHasIllusionFollowUp;
            }
            break;
        }
    }

    private static ushort ClampU16(int value)
        => value < 0 ? (ushort)0 : value > 65535 ? (ushort)65535 : (ushort)value;

    private static byte ClampU8(int value)
        => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

    private static short ClampS16(int value)
        => value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : (short)value;

    private static void DiffBlobMoveTableHandles(CombatBlobVerificationSection section, ref bool allOk)
    {
        int diffs = 0;
        var msgs = new StringBuilder(96);
        for (int i = 0; i < s_blob.EnemyCount; i++)
        {
            ushort handle = s_blob.EnemyMoveTableHandles[i];
            ref SimEnemyMoveSM move = ref s_blob.EnemyMoveSM[i];
            MonsterStateTable? table = SimMonsterStateRegistry.Resolve(handle);
            bool hasState = move.CurrentStateIdx != 0xFF;
            bool ok = !hasState || (table is not null && move.CurrentStateIdx < table.StateIds.Length);
            if (ok)
                continue;

            diffs++;
            if (diffs <= 3)
                msgs.AppendLine($"  ✗ B.ETbl[{i}]: handle={handle} cur={move.CurrentStateIdx}");
        }

        if (diffs == 0)
        {
            AddData(section, $"✓ B.ETbl({s_blob.EnemyCount})");
            return;
        }

        allOk = false;
        AddStandalone(section, $"✗ B.ETbl: {diffs} diff(s)");
        AddBufferedStandaloneLines(section, msgs);
    }

    private static void DiffBlobCardEnergySlices(CombatBlobVerificationSection section, ref bool allOk)
    {
        int checkedCards = 0;
        int diffs = 0;
        var msgs = new StringBuilder(96);
        CheckBlobCards(s_blob.HandCards.Slice(0, s_blob.HandCount), ref checkedCards, ref diffs, msgs);
        CheckBlobCards(s_blob.DrawCards.Slice(0, s_blob.DrawCount), ref checkedCards, ref diffs, msgs);
        CheckBlobCards(s_blob.DiscCards.Slice(0, s_blob.DiscCount), ref checkedCards, ref diffs, msgs);
        CheckBlobCards(s_blob.ExhaustCards.Slice(0, s_blob.ExhaustCount), ref checkedCards, ref diffs, msgs);

        if (diffs == 0)
        {
            AddData(section, $"✓ B.EnergySlices({checkedCards})");
            return;
        }

        allOk = false;
        AddStandalone(section, $"✗ B.EnergySlices: {diffs} diff(s)");
        AddBufferedStandaloneLines(section, msgs);
    }

    private static void CheckBlobCards(
        ReadOnlySpan<SimCard> cards,
        ref int checkedCards,
        ref int diffs,
        StringBuilder msgs)
    {
        for (int i = 0; i < cards.Length; i++)
        {
            checkedCards++;
            if (BlobCardEnergyInstanceCheck(s_blob, cards[i], out string? reason))
                continue;

            diffs++;
            if (diffs <= 3)
                msgs.AppendLine($"  ✗ B.Card[{i}]: {reason}");
        }
    }

    private static bool BlobCardEnergyInstanceCheck(CombatNodeBlob blobState, in SimCard blobCard, out string? reason)
    {
        ushort instanceId = blobCard.InstanceId;
        if (instanceId == 0 || instanceId > blobState.CardInstanceCount)
        {
            reason = $"iid={instanceId} instN={blobState.CardInstanceCount}";
            return false;
        }

        ushort blobStart = blobState.CardEnergyModifierStart[instanceId];
        ushort blobCount = blobState.CardEnergyModifierCount[instanceId];
        if (blobStart + blobCount > blobState.CardEnergyModifierUsed)
        {
            reason = $"start={blobStart} count={blobCount} used={blobState.CardEnergyModifierUsed}";
            return false;
        }

        for (int offset = 0; offset < blobCount; offset++)
        {
            ref SimLocalCostModifier blobModifier = ref blobState.CardEnergyModifiers[blobStart + offset];
            if (IsValidLocalCostModifier(in blobModifier))
                continue;

            reason = $"mod[{offset}] invalid {DescribeModifier(in blobModifier)}";
            return false;
        }

        _ = SimCardEnergyOps.GetWithLocalModifiers(blobState, blobCard);
        reason = null;
        return true;
    }

    private static bool BlobCardEnergyMatchesLive(CombatNodeBlob blobState, in SimCard blobCard, CardModel liveCard, out string? reason)
    {
        if (!BlobCardEnergyInstanceCheck(blobState, blobCard, out reason))
            return false;

        ushort instanceId = blobCard.InstanceId;
        CardEnergyCost energy = liveCard.EnergyCost;
        short liveBaseCost = ClampS16(energy.GetWithModifiers(CostModifiers.None));
        short blobBaseCost = blobState.CardEnergyBaseCost[instanceId];
        if (blobBaseCost != liveBaseCost)
        {
            reason = $"base blob={blobBaseCost} live={liveBaseCost}";
            return false;
        }

        ushort liveCapturedX = energy.CostsX ? ClampU16(energy.CapturedXValue) : (ushort)0;
        ushort blobCapturedX = blobState.CardEnergyCapturedX[instanceId];
        if (blobCapturedX != liveCapturedX)
        {
            reason = $"capX blob={blobCapturedX} live={liveCapturedX}";
            return false;
        }

        List<LocalCostModifier>? liveModifiers =
            (List<LocalCostModifier>?)s_cardEnergyLocalModifiersField.GetValue(energy);
        if (liveModifiers == null)
        {
            reason = "live modifiers=null";
            return false;
        }

        ushort blobStart = blobState.CardEnergyModifierStart[instanceId];
        ushort blobCount = blobState.CardEnergyModifierCount[instanceId];
        if (blobCount != liveModifiers.Count)
        {
            reason = $"modN blob={blobCount} live={liveModifiers.Count}";
            return false;
        }

        for (int i = 0; i < liveModifiers.Count; i++)
        {
            SimLocalCostModifier liveModifier = SimLocalCostModifier.From(liveModifiers[i]);
            ref SimLocalCostModifier blobModifier = ref blobState.CardEnergyModifiers[blobStart + i];
            if (SimLocalCostModifierEquals(in blobModifier, in liveModifier))
                continue;

            reason = $"mod[{i}] blob={DescribeModifier(in blobModifier)} live={DescribeModifier(in liveModifier)}";
            return false;
        }

        int blobLocalCost = SimCardEnergyOps.GetWithLocalModifiers(blobState, blobCard);
        int liveLocalCost = energy.GetWithModifiers(CostModifiers.Local);
        if (blobLocalCost != liveLocalCost)
        {
            reason = $"costL blob={blobLocalCost} live={liveLocalCost}";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool BlobCardHistoryFlagsMatchLive(CombatState state, in SimCard blobCard, CardModel liveCard, out string? reason)
    {
        const ushort mask = SimCard.FlagIsDupe | SimCard.FlagPlayedThisTurn | SimCard.FlagPlayedPreviousRound;

        ushort blobFlags = (ushort)(blobCard.Flags & mask);
        ushort liveFlags = SimHistoryCounterOps.ReadLiveCardFlags(state, liveCard);
        if (blobFlags == liveFlags)
        {
            reason = null;
            return true;
        }

        reason = $"flags=0x{blobFlags:X4}/0x{liveFlags:X4}";
        return false;
    }

    private static string DescribeModifier(in SimLocalCostModifier modifier)
        => $"amt={modifier.Amount} type={modifier.Type} exp={modifier.Expiration} reduce={modifier.IsReduceOnly}";

    private static bool IsValidLocalCostModifier(in SimLocalCostModifier modifier)
    {
        LocalCostType type = modifier.Type;
        if (type != LocalCostType.None && type != LocalCostType.Absolute && type != LocalCostType.Relative)
            return false;

        int expiration = (int)modifier.Expiration;
        return (expiration & 0x01) == 0;
    }

    private static void CheckBlobCondition(CombatBlobVerificationSection section, string tag, bool ok, string detail, ref bool allOk)
    {
        if (ok)
        {
            AddData(section, $"✓ {tag}");
            return;
        }

        allOk = false;
        AddStandalone(section, $"✗ {tag}: {detail}");
    }

    private static void DiffPowers(
        CombatBlobVerificationSection section, string tag,
        IReadOnlyList<PowerModel> live, ReadOnlySpan<short> simRow,
        ref bool allOk)
    {
        int diffs = 0;
        var msgs = new StringBuilder(64);
        int liveCount = live.Count;
        for (int i = 0; i < liveCount; i++)
        {
            PowerModel p = live[i];
            Type t = p.GetType();
            if (!SimPowerRegistry.TryGetIndex(t, out int idx))
            {
                diffs++;
                if (diffs <= 3) msgs.AppendLine($"  ✗ {tag}: unregistered {t.Name}");
                continue;
            }
            short simAmt = simRow[idx];
            int liveAmt = p.Amount;
            if (simAmt != liveAmt)
            {
                diffs++;
                if (diffs <= 3) msgs.AppendLine($"  ✗ {tag}.{t.Name}: sim={simAmt} live={liveAmt}");
            }
        }

        for (int idx = 0; idx < CombatSimLayout.PowersPerCre; idx++)
        {
            short simAmt = simRow[idx];
            if (simAmt == 0) continue;
            bool found = false;
            for (int i = 0; i < liveCount; i++)
            {
                if (SimPowerRegistry.TryGetIndex(live[i].GetType(), out int liveIdx) && liveIdx == idx)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                diffs++;
                if (diffs <= 3) msgs.AppendLine($"  ✗ {tag}: phantom slot[{idx}]={simAmt}");
            }
        }

        if (diffs == 0)
        {
            int activeCount = 0;
            for (int i = 0; i < liveCount; i++)
                if (SimPowerRegistry.TryGetIndex(live[i].GetType(), out _)) activeCount++;
            AddData(section, $"✓ {tag}({activeCount})");
        }
        else
        {
            allOk = false;
            AddStandalone(section, $"✗ {tag}: {diffs} diff(s)");
            AddBufferedStandaloneLines(section, msgs);
        }
    }

    private static void DiffIntent(CombatBlobVerificationSection section, int i, Creature e, ref bool allOk)
    {
        var move = e.Monster?.NextMove;
        SimIntent liveKind = SimIntent.Unknown;
        ushort liveDmg = 0;
        byte liveHits = 0;
        if (move != null && move.Intents.Count > 0)
        {
            switch (move.Intents[0])
            {
                case DeathBlowIntent dbi:
                    liveKind = SimIntent.DeathBlow;
                    liveDmg = AttackDamageFor(dbi);
                    liveHits = AttackHitsFor(dbi);
                    break;
                case AttackIntent ai:
                    liveKind = SimIntent.Attack;
                    liveDmg = AttackDamageFor(ai);
                    liveHits = AttackHitsFor(ai);
                    break;
                case BuffIntent:
                    liveKind = SimIntent.Buff;
                    break;
                case CardDebuffIntent:
                    liveKind = SimIntent.CardDebuff;
                    break;
                case DebuffIntent dbi:
                    liveKind = dbi.IntentType == IntentType.DebuffStrong
                        ? SimIntent.DebuffStrong : SimIntent.Debuff;
                    break;
                case DefendIntent:
                    liveKind = SimIntent.Defend;
                    break;
                case EscapeIntent:
                    liveKind = SimIntent.Escape;
                    break;
                case HealIntent:
                    liveKind = SimIntent.Heal;
                    break;
                case HiddenIntent:
                    liveKind = SimIntent.Hidden;
                    break;
                case SleepIntent:
                    liveKind = SimIntent.Sleep;
                    break;
                case StatusIntent:
                    liveKind = SimIntent.StatusCard;
                    break;
                case StunIntent:
                    liveKind = SimIntent.Stun;
                    break;
                case SummonIntent:
                    liveKind = SimIntent.Summon;
                    break;
            }
        }

        SimIntent simKind = (SimIntent)s_blob.EnemyIntent[i];
        bool kindOk = simKind == liveKind;
        if (!kindOk) allOk = false;
        if (kindOk) AddData(section, $"✓ E{i}.Intent={simKind}");
        else AddStandalone(section, $"✗ E{i}.Intent: blob={simKind} live={liveKind}");

        if (liveKind == SimIntent.Attack || liveKind == SimIntent.DeathBlow)
        {
            ushort simDmg = s_blob.EnemyIntentDmg[i];
            byte simHits = s_blob.EnemyIntentHits[i];
            bool dmgOk = simDmg == liveDmg;
            bool hitsOk = simHits == liveHits;
            if (!dmgOk) { allOk = false; AddStandalone(section, $"✗ E{i}.Dmg: blob={simDmg} live={liveDmg}"); }
            else { AddData(section, $"✓ E{i}.Dmg={simDmg}"); }
            if (!hitsOk) { allOk = false; AddStandalone(section, $"✗ E{i}.Hits: blob={simHits} live={liveHits}"); }
            else { AddData(section, $"✓ E{i}.Hits={simHits}"); }
        }
    }

    private static ushort AttackDamageFor(AttackIntent ai)
    {
        var calc = ai.DamageCalc;
        if (calc == null) return 0;
        decimal raw = calc();
        if (raw < 0m) return 0;
        if (raw > 65535m) return 65535;
        return (ushort)raw;
    }

    private static byte AttackHitsFor(AttackIntent ai)
    {
        int hits = ai.Repeats + 1;
        if (hits < 1) hits = 1;
        if (hits > 255) hits = 255;
        return (byte)hits;
    }

    private static unsafe void DiffAllRng(CombatBlobVerificationSection section, CombatState state, ref bool allOk)
    {
        var rngSet = state.RunState.Rng;
        DiffOneRng(section, "RngShuf", rngSet.Shuffle, SimRngSlot.Shuffle, ref allOk);
        DiffOneRng(section, "RngTgt", rngSet.CombatTargets, SimRngSlot.CombatTargets, ref allOk);
        DiffOneRng(section, "RngCardGen", rngSet.CombatCardGeneration, SimRngSlot.CombatCardGeneration, ref allOk);
        DiffOneRng(section, "RngCardSel", rngSet.CombatCardSelection, SimRngSlot.CombatCardSelection, ref allOk);
        DiffOneRng(section, "RngEnergy", rngSet.CombatEnergyCosts, SimRngSlot.CombatEnergyCosts, ref allOk);
        DiffOneRng(section, "RngOrb", rngSet.CombatOrbGeneration, SimRngSlot.CombatOrbGeneration, ref allOk);
        DiffOneRng(section, "RngMonAi", rngSet.MonsterAi, SimRngSlot.MonsterAi, ref allOk);
        DiffOneRng(section, "RngNiche", rngSet.Niche, SimRngSlot.Niche, ref allOk);
    }

    private static unsafe void DiffOneRng(
        CombatBlobVerificationSection section, string tag, Rng liveRng, SimRngSlot slot, ref bool allOk)
    {
        try
        {
            RandomState live = default;
            RandomStateOps.CaptureFromRng(liveRng, ref live);
            RandomState sim = s_blob.Rng(slot);

            bool ok = sim.INext == live.INext && sim.INextp == live.INextp;
            if (ok)
            {
                for (int k = 0; k < RandomState.ArrLen; k++)
                {
                    if (sim.Arr[k] != live.Arr[k])
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (ok) AddData(section, $"✓ {tag}");
            else
            {
                allOk = false;
                AddStandalone(section,
                    $"✗ {tag}: iN blob={sim.INext} live={live.INext} " +
                    $"iNp blob={sim.INextp} live={live.INextp}");
            }
        }
        catch (Exception ex)
        {
            allOk = false;
            AddStandalone(section, $"✗ {tag} threw: {ex.Message}");
        }
    }

    private static string ReverseCardName(ushort id)
    {
        if (s_cardNameById is null)
        {
            var fld = typeof(SimCardDb).GetField("_byType", BindingFlags.Static | BindingFlags.NonPublic);
            s_cardNameById = new Dictionary<ushort, string>();
            if (fld?.GetValue(null) is System.Collections.Frozen.FrozenDictionary<Type, ushort> map)
            {
                foreach (var kv in map) s_cardNameById[kv.Value] = kv.Key.Name;
            }
        }
        return s_cardNameById.TryGetValue(id, out string? n) ? n : $"id{id}?";
    }
}