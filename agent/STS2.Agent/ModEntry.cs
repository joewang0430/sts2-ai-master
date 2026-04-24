using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using STS2.Agent.Ui;

namespace STS2.Agent;

/// <summary>
/// Entry point for the STS2 AI Agent mod.
///
/// STS2's ModManager reflects on this assembly at startup, finds any class decorated
/// with [ModInitializer], and invokes the declared static method. This happens
/// synchronously during game boot, before any scene is loaded.
///
/// All subsystem initialization (Harmony patches, UI injection, AI engine setup)
/// must be wired up from Initialize().
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        // Apply all [HarmonyPatch] classes in this assembly.
        new Harmony("sts2_ai_agent").PatchAll();

        // Wire up the combat debug overlay (event subscriptions live for the
        // entire process lifetime; the Label node is created/destroyed per combat).
        CombatDebugOverlay.Initialize();
    }
}
