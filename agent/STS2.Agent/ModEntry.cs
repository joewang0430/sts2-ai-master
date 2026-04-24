using MegaCrit.Sts2.Core.Modding;

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
        // TODO: wire up subsystems as they are built:
        //   1. Harmony patches (UI button injection into combat screen)
        //   2. AI engine initialization (search state allocators, etc.)
    }
}
