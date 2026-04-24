using System;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;

namespace STS2.Agent.Ui;

/// <summary>
/// Injects a semi-transparent debug overlay into the combat scene.
///
/// Displays in real-time:
///   - Player HP / Block / Energy / Stars / pile counts
///   - Every hand card with its effective energy cost
///   - Every living enemy's HP / Block and next intent (damage value for attacks)
///
/// Purpose: validate all data-reading paths (CombatState chain) AND the Godot node
/// injection mechanism (Harmony Postfix on NCombatRoom._Ready) in one pass before
/// writing any AI logic.
///
/// Lifetime model:
///   Initialize()  — called once at mod boot; subscribes to CombatManager events
///                   for the lifetime of the process.
///   _Ready patch  — runs each time a new NCombatRoom enters the scene tree;
///                   creates the Label node and calls Refresh().
///   Refresh()     — idempotent; safe to call at any time.
///   CombatEnded   — nulls the node references; the nodes are freed by Godot
///                   when the scene unloads.
/// </summary>
internal static class CombatDebugOverlay
{
    // ── State ─────────────────────────────────────────────────────────────────

    private static Label?     _label;
    private static ColorRect? _bg;
    private static CombatState? _state;

    // ── Initialization ────────────────────────────────────────────────────────

    internal static void Initialize()
    {
        CombatManager.Instance.CombatSetUp           += OnCombatSetUp;
        CombatManager.Instance.StateTracker.CombatStateChanged += OnCombatStateChanged;
        CombatManager.Instance.CombatEnded           += OnCombatEnded;
    }

    // ── Combat-room node setup (called by Harmony patch below) ────────────────

    internal static void OnCombatRoomReady(NCombatRoom room)
    {
        // Semi-transparent background so the text is readable over any scene art.
        _bg = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.65f),
            Position    = new Vector2(8f, 8f),
            Size        = new Vector2(345f, 480f),
            ZIndex      = 99,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        _label = new Label
        {
            Position     = new Vector2(12f, 12f),
            Size         = new Vector2(337f, 472f),
            ZIndex       = 100,
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _label.AddThemeFontSizeOverride("font_size", 13);

        room.AddChild(_bg);
        room.AddChild(_label);

        // CombatSetUp always fires before _Ready, so _state is already populated.
        Refresh();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private static void OnCombatSetUp(CombatState state)
    {
        _state = state;
        Refresh();
    }

    private static void OnCombatStateChanged(CombatState state)
    {
        _state = state;
        Refresh();
    }

    private static void OnCombatEnded(CombatRoom _)
    {
        _state = null;
        _label = null;
        _bg    = null;
        // The actual Godot nodes are freed automatically when the scene unloads.
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private static void Refresh()
    {
        if (_label is null || !GodotObject.IsInstanceValid(_label)) return;

        if (_state is null)
        {
            _label.Text = string.Empty;
            return;
        }

        try
        {
            _label.Text = BuildText(_state);
        }
        catch (Exception ex)
        {
            _label.Text = $"[OverlayError]\n{ex.Message}";
        }
    }

    // ── Text builder ──────────────────────────────────────────────────────────

    private static string BuildText(CombatState state)
    {
        var sb = new StringBuilder(512);

        // ── Player ────────────────────────────────────────────────────────────
        Player? me = LocalContext.GetMe(state);
        if (me is not null)
        {
            Creature           pc  = me.Creature;
            PlayerCombatState? pcs = me.PlayerCombatState;

            sb.AppendLine("── PLAYER ──────────────");
            sb.AppendLine($"HP: {pc.CurrentHp}/{pc.MaxHp}  Block: {pc.Block}");

            if (pcs is not null)
            {
                sb.AppendLine($"Energy: {pcs.Energy}/{pcs.MaxEnergy}  Stars: {pcs.Stars}");
                sb.AppendLine($"Hand:{pcs.Hand.Cards.Count}  Draw:{pcs.DrawPile.Cards.Count}  Disc:{pcs.DiscardPile.Cards.Count}");
                sb.AppendLine();

                foreach (CardModel card in pcs.Hand.Cards)
                {
                    // For X-cost cards, "effective cost" equals all remaining energy.
                    int    cost    = card.EnergyCost.CostsX
                        ? pcs.Energy
                        : card.EnergyCost.GetWithModifiers(CostModifiers.All);
                    string costStr = card.EnergyCost.CostsX ? $"X={cost}" : cost.ToString();
                    sb.AppendLine($"  [{costStr}] {card.Title}");
                }

                if (pc.Powers.Count > 0)
                {
                    sb.AppendLine("  Powers:");
                    foreach (PowerModel power in pc.Powers)
                        sb.AppendLine($"    {power.Title.GetFormattedText()} {power.Amount} [{power.Type}]");
                }
            }
        }

        // ── Enemies ───────────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("── ENEMIES ─────────────");

        foreach (Creature enemy in state.Enemies)
        {
            if (!enemy.IsAlive) continue;

            sb.AppendLine($"{enemy.Name}");
            sb.AppendLine($"  HP:{enemy.CurrentHp}/{enemy.MaxHp}  Block:{enemy.Block}");

            if (enemy.Powers.Count > 0)
            {
                foreach (PowerModel power in enemy.Powers)
                    sb.AppendLine($"  {power.Title.GetFormattedText()} {power.Amount} [{power.Type}]");
            }

            if (enemy.IsStunned)
            {
                sb.AppendLine("  Intent: STUNNED");
                continue;
            }

            if (enemy.Monster is { } mon)
            {
                foreach (AbstractIntent intent in mon.NextMove.Intents)
                {
                    if (intent is AttackIntent atk)
                    {
                        int    dmg     = (int)(atk.DamageCalc?.Invoke() ?? 0m);
                        // Repeats=0 means "single hit"; Repeats=N means N+1 total hits.
                        string repsStr = atk.Repeats > 0 ? $" ×{atk.Repeats + 1}" : string.Empty;
                        sb.AppendLine($"  Intent: ATTACK {dmg}{repsStr}");
                    }
                    else
                    {
                        sb.AppendLine($"  Intent: {intent.IntentType}");
                    }
                }
            }
        }

        return sb.ToString();
    }

    // ── Harmony patch ─────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(NCombatRoom), "_Ready")]
    private static class Patch_NCombatRoom_Ready
    {
        [HarmonyPostfix]
        private static void Postfix(NCombatRoom __instance)
        {
            // Only inject into live combat; skip replays and post-combat screens.
            if (__instance.Mode != CombatRoomMode.ActiveCombat) return;

            try
            {
                OnCombatRoomReady(__instance);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[STS2.Agent] CombatDebugOverlay._Ready patch failed: {ex}");
            }
        }
    }
}
