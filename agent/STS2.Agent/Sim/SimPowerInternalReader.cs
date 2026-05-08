using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2.Agent.Sim;

/// <summary>
/// One-shot reflection cache for the private hidden state inside the ~12
/// PowerModel subclasses tracked by <see cref="SimPowerInternal"/>.
///
/// Two storage idioms exist in the game source:
///   1. <b>Inner <c>Data</c> class via <see cref="PowerModel"/>'s
///      <c>_internalData</c> field</b> (Feral, Juggling, VoidForm, HardenedShell,
///      Outbreak, Orbit, Automation, Illusion). Each power overrides
///      <c>InitInternalData()</c> to return a private nested
///      <c>class Data { public int / decimal / bool ... }</c>; the runtime
///      stores it in <c>PowerModel._internalData</c> as <c>object</c>.
///   2. <b>Direct private fields</b> (Tender, Sloth, Nemesis, Ritual): no
///      <c>Data</c> wrapper, just <c>private int / bool _foo;</c> on the
///      power itself.
///
/// We resolve every field once at static init via reflection (cost: a few
/// ms at first Snapshot) and cache <see cref="FieldInfo"/> handles. Each
/// <c>Read*</c> helper is a single <c>FieldInfo.GetValue</c> on the hot
/// Snapshot path — Snapshot runs ≤ once per AI Think, never inside DFS.
///
/// If any cached field handle resolves to <c>null</c> (game source layout
/// changed), the static constructor throws and the AI refuses to load.
/// That's the correct fail-loud behavior: silent zero-default would corrupt
/// every sim node.
/// </summary>
internal static class SimPowerInternalReader
{
    // Base PowerModel field — reading the per-instance object payload.
    private static readonly FieldInfo _internalDataField =
        typeof(PowerModel).GetField("_internalData", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "SimPowerInternalReader: PowerModel._internalData not found. Game source layout changed.");

    // ── Inner-Data fields (set #1: read via _internalData → cast Data → field) ──
    private static readonly FieldInfo _feralZeroCostField    = ResolveDataField<FeralPower>("zeroCostAttacksPlayed");
    private static readonly FieldInfo _jugglingAttacksField  = ResolveDataField<JugglingPower>("attacksPlayedThisTurn");
    private static readonly FieldInfo _voidFormCardsField    = ResolveDataField<VoidFormPower>("cardsPlayedThisTurn");
    private static readonly FieldInfo _hardenedShellDmgField = ResolveDataField<HardenedShellPower>("damageReceivedThisTurn");
    private static readonly FieldInfo _outbreakTimesField    = ResolveDataField<OutbreakPower>("timesPoisoned");
    private static readonly FieldInfo _orbitEnergyField      = ResolveDataField<OrbitPower>("energySpent");
    private static readonly FieldInfo _orbitTriggerField     = ResolveDataField<OrbitPower>("triggerCount");
    private static readonly FieldInfo _automationCardsField  = ResolveDataField<AutomationPower>("cardsLeft");
    private static readonly FieldInfo _illusionRevivingField = ResolveDataField<IllusionPower>("isReviving");

    // ── Direct private fields (set #2: read straight off the power) ──────────
    private static readonly FieldInfo _tenderCardsField      = ResolveDirectField<TenderPower>("_cardsPlayedThisTurn");
    private static readonly FieldInfo _slothCardsField       = ResolveDirectField<SlothPower>("_cardsPlayedThisTurn");
    private static readonly FieldInfo _nemesisIntangibleField= ResolveDirectField<NemesisPower>("_shouldApplyIntangible");
    private static readonly FieldInfo _ritualJustAppliedField= ResolveDirectField<RitualPower>("_wasJustAppliedByEnemy");

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Resolve a public field on a power's private nested <c>Data</c> class.</summary>
    private static FieldInfo ResolveDataField<TPower>(string fieldName) where TPower : PowerModel
    {
        Type? data = typeof(TPower).GetNestedType("Data", BindingFlags.NonPublic);
        if (data == null)
            throw new InvalidOperationException(
                $"SimPowerInternalReader: nested Data type not found on {typeof(TPower).Name}.");
        FieldInfo? f = data.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
            throw new InvalidOperationException(
                $"SimPowerInternalReader: field '{fieldName}' not found on {typeof(TPower).Name}.Data.");
        return f;
    }

    /// <summary>Resolve a direct private field on a power class.</summary>
    private static FieldInfo ResolveDirectField<TPower>(string fieldName) where TPower : PowerModel
    {
        FieldInfo? f = typeof(TPower).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null)
            throw new InvalidOperationException(
                $"SimPowerInternalReader: field '{fieldName}' not found on {typeof(TPower).Name}.");
        return f;
    }

    // ── Inner-Data accessors. Each unwraps PowerModel._internalData (may be
    //    null if the power's InitInternalData hasn't been called — defensive
    //    guard returns 0). All ints are returned as int; decimals truncated. ──

    private static object? ReadDataObject(PowerModel power)
        => _internalDataField.GetValue(power);

    public static int ReadFeralZeroCostAttacks(FeralPower p)
    { var d = ReadDataObject(p); return d == null ? 0 : (int)_feralZeroCostField.GetValue(d)!; }

    public static int ReadJugglingAttacks(JugglingPower p)
    { var d = ReadDataObject(p); return d == null ? 0 : (int)_jugglingAttacksField.GetValue(d)!; }

    public static int ReadVoidFormCards(VoidFormPower p)
    { var d = ReadDataObject(p); return d == null ? 0 : (int)_voidFormCardsField.GetValue(d)!; }

    public static int ReadHardenedShellDamage(HardenedShellPower p)
    { var d = ReadDataObject(p); return d == null ? 0 : (int)(decimal)_hardenedShellDmgField.GetValue(d)!; }

    public static int ReadOutbreakTimesPoisoned(OutbreakPower p)
    { var d = ReadDataObject(p); return d == null ? 0 : (int)_outbreakTimesField.GetValue(d)!; }

    public static int ReadOrbitEnergySpent(OrbitPower p)
    { var d = ReadDataObject(p); return d == null ? 0 : (int)_orbitEnergyField.GetValue(d)!; }

    public static int ReadOrbitTriggerCount(OrbitPower p)
    { var d = ReadDataObject(p); return d == null ? 0 : (int)_orbitTriggerField.GetValue(d)!; }

    public static int ReadAutomationCardsLeft(AutomationPower p)
    {
        var d = ReadDataObject(p);
        // AutomationPower.Data initializes cardsLeft = 10 in its field initializer,
        // but if InitInternalData hasn't fired yet we treat the cycle as full.
        return d == null ? 10 : (int)_automationCardsField.GetValue(d)!;
    }

    public static bool ReadIllusionIsReviving(IllusionPower p)
    { var d = ReadDataObject(p); return d != null && (bool)_illusionRevivingField.GetValue(d)!; }

    // ── Direct-field accessors. ──────────────────────────────────────────────

    public static int  ReadTenderCardsThisTurn(TenderPower p)   => (int)_tenderCardsField.GetValue(p)!;
    public static int  ReadSlothCardsThisTurn(SlothPower p)     => (int)_slothCardsField.GetValue(p)!;
    public static bool ReadNemesisShouldApplyIntangible(NemesisPower p)
        => (bool)_nemesisIntangibleField.GetValue(p)!;
    public static bool ReadRitualWasJustAppliedByEnemy(RitualPower p)
        => (bool)_ritualJustAppliedField.GetValue(p)!;
}
