using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using STS2.Agent.Sim;

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
    private static Label?     _labelRight;
    private static ColorRect? _bgRight;
    private static Label?     _labelPredict;
    private static ColorRect? _bgPredict;
    private static Label?     _labelDmg;
    private static ColorRect? _bgDmg;
    private static Label?     _labelSim;
    private static ColorRect? _bgSim;
    private static Label?     _labelBlob;
    private static ColorRect? _bgBlob;
    private static CombatState? _state;

    // ── Snapshot verification ─────────────────────────────────────────────────
    // Reused across Refresh() calls; CopyFrom-style snapshot never allocates.
    private static readonly SimCombatState _sim = new();
    private static readonly CombatNodeBlob _blob = new();
    private static readonly SimCombatState _simScratch = new();
    private static readonly CombatNodeBlob _blobScratch = new();

    // ── Next-turn-hand prediction state ───────────────────────────────────────
    // The prediction is computed during every Refresh() for state.RoundNumber + 1.
    // To verify it, we keep the prediction made in the *previous* Refresh and,
    // when state.RoundNumber increments, freeze it alongside the new actual hand
    // so the user can see a side-by-side diff.
    private static List<string>? _liveFromLastRefresh;
    private static int           _roundFromLastRefresh = -1;
    private static List<string>? _frozenPrediction;
    private static List<string>? _frozenActual;
    private static int           _frozenForRound = -1;
    // Peak hand count observed in the frozen round so far. The actual snapshot
    // is updated whenever a new peak is reached, which naturally locks onto the
    // post-draw state once the player starts playing cards (count drops).
    private static int           _frozenActualPeakCount;

    // ── Potion-selection state ────────────────────────────────────────────────
    // _potionLayer: a dedicated CanvasLayer with a high Layer value. Children
    // of a CanvasLayer bypass sibling draw/input ordering, so our interactive
    // buttons reliably receive mouse clicks even when the game adds full-screen
    // Control overlays (targeting arrow, card drag, etc.) after _Ready.
    private static CanvasLayer?   _potionLayer;
    private static ColorRect?     _bgPotions;
    private static VBoxContainer? _potionButtonBox;
    private static Label?         _potionApprovedLabel;

    // Authoritative toggle state: keys are PotionModel.Id.Entry.
    // Written only by button Pressed handlers; read by the AI layer via AllowedPotionIds.
    // Cleared on every OnCombatEnded so each fight starts with a fresh selection.
    private static readonly HashSet<string>            _allowedPotionIds = new();
    // Id.Entry → Button node (for text updates on toggle and on potion consumption).
    private static readonly Dictionary<string, Button> _potionButtons    = new();
    // Id.Entry → localized display title (stable within a combat).
    private static readonly Dictionary<string, string> _potionTitles     = new();

    /// <summary>
    /// Potion IDs the player has approved for AI use this combat.
    /// Read by the AI search layer; never modified by it.
    /// </summary>
    internal static IReadOnlySet<string> AllowedPotionIds => _allowedPotionIds;

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

        // Right column — relics
        _bgRight = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.65f),
            Position    = new Vector2(360f, 8f),
            Size        = new Vector2(180f, 480f),
            ZIndex      = 99,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _labelRight = new Label
        {
            Position     = new Vector2(364f, 12f),
            Size         = new Vector2(172f, 472f),
            ZIndex       = 100,
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _labelRight.AddThemeFontSizeOverride("font_size", 13);

        room.AddChild(_bg);
        room.AddChild(_label);
        room.AddChild(_bgRight);
        room.AddChild(_labelRight);

        // ── Fourth column — next-turn hand prediction & verification ──────────
        // No interactivity, so a plain ColorRect + Label suffices (no CanvasLayer).
        _bgPredict = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.65f),
            Position    = new Vector2(776f, 8f),
            Size        = new Vector2(220f, 480f),
            ZIndex      = 99,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _labelPredict = new Label
        {
            Position     = new Vector2(780f, 12f),
            Size         = new Vector2(212f, 472f),
            ZIndex       = 100,
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _labelPredict.AddThemeFontSizeOverride("font_size", 12);
        room.AddChild(_bgPredict);
        room.AddChild(_labelPredict);

        // ── Fifth column — Sim damage preview (VERIFY tool for SimDamage) ─────
        // Layout note: the screen is typically 1280–1920 wide. We start at
        // x=1004 (right after column 4 ends at 996) and use a 270 px wide panel,
        // which fits comfortably on a 1280-wide window.
        _bgDmg = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.65f),
            Position    = new Vector2(1004f, 8f),
            Size        = new Vector2(270f, 480f),
            ZIndex      = 99,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _labelDmg = new Label
        {
            Position     = new Vector2(1008f, 12f),
            Size         = new Vector2(262f, 472f),
            ZIndex       = 100,
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _labelDmg.AddThemeFontSizeOverride("font_size", 12);
        room.AddChild(_bgDmg);
        room.AddChild(_labelDmg);

        // ── Sixth column — SIM DIFF (legacy sim vs live state) ────────────────
        // Separated from dmg column to avoid crowding. x=1282 = 1004+270+8.
        _bgSim = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.65f),
            Position    = new Vector2(1282f, 8f),
            Size        = new Vector2(290f, 560f),
            ZIndex      = 99,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _labelSim = new Label
        {
            Position     = new Vector2(1286f, 12f),
            Size         = new Vector2(282f, 552f),
            ZIndex       = 100,
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _labelSim.AddThemeFontSizeOverride("font_size", 11);
        room.AddChild(_bgSim);
        room.AddChild(_labelSim);

        // ── Seventh column — BLOB CARD SLICE (blob vs legacy sim) ───────────
        // x=1580 = 1282+290+8. Separate panel keeps the hot card-slice bridge
        // readable without diluting the live-state diff column.
        _bgBlob = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.65f),
            Position    = new Vector2(1580f, 8f),
            Size        = new Vector2(290f, 560f),
            ZIndex      = 99,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _labelBlob = new Label
        {
            Position     = new Vector2(1584f, 12f),
            Size         = new Vector2(282f, 552f),
            ZIndex       = 100,
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _labelBlob.AddThemeFontSizeOverride("font_size", 11);
        room.AddChild(_bgBlob);
        room.AddChild(_labelBlob);

        // ── Third column — potion selection ───────────────────────────────────
        // Parent the interactive widgets to a CanvasLayer so their input is
        // processed AFTER (i.e. ABOVE) every Control in the game's scene tree.
        // Layer 100 is well above any layer the base game uses for combat UI.
        _potionLayer = new CanvasLayer { Layer = 100 };

        _bgPotions = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.65f),
            Position    = new Vector2(548f, 8f),
            Size        = new Vector2(220f, 480f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        // VBoxContainer auto-stacks children vertically; MouseFilter=Pass so
        // clicks pass through the container itself but stop on the Button children.
        _potionButtonBox = new VBoxContainer
        {
            Position          = new Vector2(552f, 12f),
            CustomMinimumSize = new Vector2(212f, 0f),
            MouseFilter       = Control.MouseFilterEnum.Pass,
        };

        // Summary label showing which potions the AI is permitted to use.
        _potionApprovedLabel = new Label
        {
            Position     = new Vector2(552f, 340f),
            Size         = new Vector2(212f, 140f),
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _potionApprovedLabel.AddThemeFontSizeOverride("font_size", 12);

        // CanvasLayer must be added to the scene tree first; then widgets become
        // its children. It is attached to the room so it is freed with the scene.
        room.AddChild(_potionLayer);
        _potionLayer.AddChild(_bgPotions);
        _potionLayer.AddChild(_potionButtonBox);
        _potionLayer.AddChild(_potionApprovedLabel);

        // CombatSetUp always fires before _Ready, so _state is already populated.
        if (_state is not null)
            RebuildPotionButtons(_state);

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
        _state               = null;
        _label               = null;
        _bg                  = null;
        _labelRight          = null;
        _bgRight             = null;
        _labelPredict        = null;
        _bgPredict           = null;
        _labelDmg            = null;
        _bgDmg               = null;
        _labelSim            = null;
        _bgSim               = null;
        _labelBlob           = null;
        _bgBlob              = null;
        _bgPotions           = null;
        _potionButtonBox     = null;
        _potionApprovedLabel = null;
        _potionLayer         = null;
        _allowedPotionIds.Clear();
        _potionButtons.Clear();
        _potionTitles.Clear();
        _liveFromLastRefresh   = null;
        _roundFromLastRefresh  = -1;
        _frozenPrediction      = null;
        _frozenActual          = null;
        _frozenForRound        = -1;
        _frozenActualPeakCount = 0;
        // The actual Godot nodes are freed automatically when the scene unloads.
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private static void Refresh()
    {
        if (_label is null || !GodotObject.IsInstanceValid(_label)) return;

        if (_state is null)
        {
            _label.Text = string.Empty;
            if (_labelRight is not null && GodotObject.IsInstanceValid(_labelRight))
                _labelRight.Text = string.Empty;
            if (_labelPredict is not null && GodotObject.IsInstanceValid(_labelPredict))
                _labelPredict.Text = string.Empty;
            if (_labelDmg is not null && GodotObject.IsInstanceValid(_labelDmg))
                _labelDmg.Text = string.Empty;
            if (_labelSim is not null && GodotObject.IsInstanceValid(_labelSim))
                _labelSim.Text = string.Empty;
            if (_labelBlob is not null && GodotObject.IsInstanceValid(_labelBlob))
                _labelBlob.Text = string.Empty;
            if (_potionApprovedLabel is not null && GodotObject.IsInstanceValid(_potionApprovedLabel))
                _potionApprovedLabel.Text = string.Empty;
            return;
        }

        try
        {
            _label.Text = BuildMainText(_state);
            if (_labelRight is not null && GodotObject.IsInstanceValid(_labelRight))
                _labelRight.Text = BuildRelicText(_state);
            if (_labelPredict is not null && GodotObject.IsInstanceValid(_labelPredict))
                _labelPredict.Text = BuildPredictionText(_state);
            if (_labelDmg is not null && GodotObject.IsInstanceValid(_labelDmg))
                _labelDmg.Text = BuildDmgPreviewText(_state);

            bool hasSimPanel = _labelSim is not null && GodotObject.IsInstanceValid(_labelSim);
            bool hasBlobPanel = _labelBlob is not null && GodotObject.IsInstanceValid(_labelBlob);
            if (hasSimPanel || hasBlobPanel)
            {
                BuildSnapshotDiffTexts(_state, out string simDiffText, out string blobDiffText);
                if (hasSimPanel)
                    _labelSim!.Text = simDiffText;
                if (hasBlobPanel)
                    _labelBlob!.Text = blobDiffText;
            }

            // Detect divergence between our tracked potions and the live list:
            //   - CombatSetUp may fire before _Ready, or Player.Potions may be
            //     unpopulated at that early moment.
            //   - A potion may be gained mid-combat (some relics do this).
            // A consumed potion is NOT divergence — RefreshPotionButtons disables
            // it in place so the user can still see what was available.
            Player? me   = LocalContext.GetMe(_state);
            var     live = me?.Potions
                               .Where(p => p.Usage == PotionUsage.CombatOnly || p.Usage == PotionUsage.AnyTime)
                               .Select(p => p.Id.Entry)
                               .ToHashSet()
                           ?? new HashSet<string>();

            bool needsRebuild = _potionButtons.Count == 0
                ? live.Count > 0
                : live.Any(id => !_potionButtons.ContainsKey(id));

            if (needsRebuild)
                RebuildPotionButtons(_state);
            else
                RefreshPotionButtons(_state);
        }
        catch (Exception ex)
        {
            _label.Text = $"[OverlayError]\n{ex.Message}";
        }
    }

    // ── Text builders ─────────────────────────────────────────────────────────

    private static string BuildMainText(CombatState state)
    {
        var sb = new StringBuilder(512);

        // ── Player ────────────────────────────────────────────────────────────
        Player? me = LocalContext.GetMe(state);
        if (me is not null)
        {
            Creature           pc  = me.Creature;
            PlayerCombatState? pcs = me.PlayerCombatState;

            sb.AppendLine($"── {me.Character.Id.Entry}  T{state.RoundNumber} ──────────────");
            sb.AppendLine($"HP: {pc.CurrentHp}/{pc.MaxHp}  Block: {pc.Block}");

            if (pcs is not null)
            {
                sb.AppendLine($"Energy: {pcs.Energy}/{pcs.MaxEnergy}  Stars: {pcs.Stars}");
                sb.AppendLine($"Hand:{pcs.Hand.Cards.Count}  Draw:{pcs.DrawPile.Cards.Count}  Disc:{pcs.DiscardPile.Cards.Count}  Exh:{pcs.ExhaustPile.Cards.Count}");

                foreach (CardModel card in pcs.Hand.Cards)
                {
                    // For X-cost cards, "effective cost" equals all remaining energy.
                    int    cost    = card.EnergyCost.CostsX
                        ? pcs.Energy
                        : card.EnergyCost.GetWithModifiers(CostModifiers.All);
                    string costStr = card.EnergyCost.CostsX ? $"X={cost}" : cost.ToString();

                    // Tier-1 AI data: playability, card type, target type.
                    bool canPlay  = card.CanPlay();
                    char typeChar = card.Type switch
                    {
                        CardType.Attack => 'A',
                        CardType.Skill  => 'S',
                        CardType.Power  => 'P',
                        CardType.Curse  => 'C',
                        CardType.Quest  => 'Q',
                        _               => '?',   // Status / None
                    };
                    string tgtStr = card.TargetType switch
                    {
                        TargetType.AnyEnemy           => "E",
                        TargetType.AllEnemies         => "*E",
                        TargetType.RandomEnemy        => "?E",
                        TargetType.Self               => "@",
                        TargetType.AnyPlayer          => "Ply",
                        TargetType.AnyAlly            => "Aly",
                        TargetType.AllAllies          => "*A",
                        TargetType.TargetedNoCreature => "obj",
                        TargetType.Osty               => "Ost",
                        _                             => "-",
                    };
                    // Tier-3: upgrade marker and urgency keywords.
                    // [+]  = card is upgraded
                    // [Eth] = Ethereal (exhausts at turn end if unplayed → holding it has a cost)
                    // [Ret] = Retain   (stays in hand next turn → low urgency)
                    // [Exh] = Exhaust keyword (consumed on play, not just Ethereal)
                    // [Sly] = Sly      (can be played while Dazed)
                    string upgradeMark = card.IsUpgraded ? "[+]" : string.Empty;

                    var kw = card.Keywords;
                    // Use ShouldRetainThisTurn so single-turn Retain is also caught.
                    bool isEthereal = kw.Contains(CardKeyword.Ethereal);
                    bool isRetain   = card.ShouldRetainThisTurn;
                    bool isExhaust  = kw.Contains(CardKeyword.Exhaust);
                    bool isSly      = card.IsSlyThisTurn;

                    // Build a compact flags string; empty when no flags apply.
                    string flags = string.Concat(
                        upgradeMark,
                        isEthereal ? "[Eth]" : string.Empty,
                        isRetain   ? "[Ret]" : string.Empty,
                        isExhaust  ? "[Exh]" : string.Empty,
                        isSly      ? "[Sly]" : string.Empty);

                    // Bracket shows '!' prefix when the card cannot currently be played.
                    string costBracket = canPlay ? $"[{costStr}]" : $"[!{costStr}]";
                    sb.AppendLine($"  {costBracket} {typeChar}:{tgtStr} {card.Title}{flags}");
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

    /// <summary>
    /// Sim-layer damage prediction (VERIFY column). For every attack card in
    /// hand, computes what <see cref="SimDamage.Compute"/> says the card would
    /// hit each living enemy for. The user compares this against the in-game
    /// tooltip to validate the formula.
    ///
    /// Hit count detection: STS2 multi-hit cards (Peck, Quadcast, BouncingFlask…)
    /// expose a <c>Repeat</c> DynamicVar that holds the per-cast hit count.
    /// Cards without it are assumed to hit once. Some special cards (e.g.
    /// MadScience's "Violence" rider) encode hit count in card-specific vars
    /// and are NOT detected here — those will need per-card data once they
    /// enter the SimCardDb whitelist.
    /// </summary>
    private static string BuildDmgPreviewText(CombatState state)
    {
        var sb = new StringBuilder(256);
        sb.AppendLine("── DMG PREVIEW ─────────");

        Player? me = LocalContext.GetMe(state);
        if (me?.PlayerCombatState is not { } pcs) return sb.ToString();

        Creature dealer   = me.Creature;
        int      strength = SimReader.Strength(dealer);
        bool     dealerWk = SimReader.Weak(dealer);

        sb.AppendLine($"Str:{strength}  Weak:{(dealerWk ? "Y" : "N")}");

        var liveEnemies = state.Enemies.Where(e => e.IsAlive).ToList();
        if (liveEnemies.Count == 0)
        {
            sb.AppendLine("(no enemies)");
            return sb.ToString();
        }

        bool any = false;
        foreach (CardModel card in pcs.Hand.Cards)
        {
            if (card.Type != CardType.Attack)                            continue;
            if (!card.DynamicVars.TryGetValue("Damage", out var dv))     continue;
            int rawDmg = (int)dv.BaseValue;

            // Repeat var = total hit count (Peck=3, Quadcast=4, …). Cards without
            // it hit once. See class doc above for the multi-hit caveat.
            int hits = 1;
            if (card.DynamicVars.TryGetValue("Repeat", out var rv))
                hits = Math.Max(1, (int)rv.BaseValue);

            any = true;
            string hitsTag = hits > 1 ? $" ×{hits}" : string.Empty;
            sb.AppendLine($"{card.Title} ({rawDmg}{hitsTag})");

            foreach (Creature en in liveEnemies)
            {
                bool vuln       = SimReader.Vulnerable(en);
                int  perHit     = SimDamage.Compute(rawDmg, strength, vuln, dealerWk);
                int  totalRaw   = perHit * hits;
                // Block applies once per hit (game subtracts block on each hit
                // and updates remaining block between hits). Approximate here:
                // first hit eats Block, subsequent hits hit naked HP.
                int totalAfter = Math.Max(0, perHit - en.Block) + perHit * (hits - 1);

                // Build a compact human-readable formula breakdown so the user
                // can verify which branches fired without doing math in their
                // head. Examples: "(8+1)×1.5×0.75=10", "(8+1)=9".
                var formula = new StringBuilder(32);
                formula.Append('(').Append(rawDmg);
                if (strength != 0) formula.Append(strength >= 0 ? "+" : "").Append(strength);
                formula.Append(')');
                if (vuln)     formula.Append("×1.5");
                if (dealerWk) formula.Append("×0.75");
                formula.Append('=').Append(perHit);

                string nameShort = en.Name;
                string line      = hits > 1
                    ? $"  {nameShort}: {formula}×{hits}={totalRaw}"
                    : $"  {nameShort}: {formula}";
                if (en.Block > 0) line += $" −blk{en.Block}={totalAfter}";
                sb.AppendLine(line);
            }
        }

        if (!any) sb.AppendLine("(no attack cards)");
        return sb.ToString();
    }

    /// <summary>
    /// Runs <see cref="SimCombatState.Snapshot"/> on the current live state and
    /// compares every field that is directly readable from the overlay against
    /// the corresponding sim value. Appended to the DmgPreview column so it is
    /// always visible without extra UI. One line per field: "✓" when sim==live,
    /// "✗ sim=X live=Y" when not. Shows "SIM OK" header when all pass.
    /// </summary>
    private static void BuildSnapshotDiffTexts(CombatState state, out string simText, out string blobText)
    {
        var simSb = new StringBuilder(512);
        var blobSb = new StringBuilder(256);
        simSb.AppendLine();
        simSb.AppendLine("── SIM DIFF ─────────────");
        blobSb.AppendLine();
        blobSb.AppendLine("── BLOB HOT SLICE ──────");

        bool simAllOk = true;
        bool blobAllOk = true;
        bool blobReady = false;

        try
        {
            _sim.Snapshot(state);
        }
        catch (Exception ex)
        {
            simSb.AppendLine($"Snapshot() threw:\n{ex.Message}");
            blobSb.AppendLine($"Snapshot() blocked blob:\n{ex.Message}");
            simText = simSb.ToString();
            blobText = blobSb.ToString();
            return;
        }

        try
        {
            CombatNodeBlobSnapshot.WriteV1FromSim(_sim, _blob);
            blobReady = true;
        }
        catch (Exception ex)
        {
            blobAllOk = false;
            blobSb.AppendLine($"✗ Blob.Write threw: {ex.Message}");
        }

        Player? me = LocalContext.GetMe(state);
        if (me is null)
        {
            simSb.AppendLine("(no local player)");
            if (blobReady)
                DiffBlobHotSlice(blobSb, ref blobAllOk);
            if (simAllOk) simSb.Insert(simSb.ToString().IndexOf('\n', simSb.ToString().IndexOf("SIM DIFF")) + 1, "✓ ALL OK\n");
            if (blobAllOk) blobSb.Insert(blobSb.ToString().IndexOf('\n', blobSb.ToString().IndexOf("BLOB HOT SLICE")) + 1, "✓ ALL OK\n");
            simText = PackTwoPerLine(simSb.ToString());
            blobText = PackTwoPerLine(blobSb.ToString());
            return;
        }

        Creature pc = me.Creature;
        PlayerCombatState? pcs = me.PlayerCombatState;

        // Helper: append one comparison line, update allOk flag.
        void Cmp(string name, object simVal, object liveVal)
        {
            bool ok = simVal.ToString() == liveVal.ToString();
            if (!ok) simAllOk = false;
            simSb.AppendLine(ok
                ? $"✓ {name}={simVal}"
                : $"✗ {name}: sim={simVal} live={liveVal}");
        }

        Cmp("Round",   blobReady ? _blob.Round : _sim.Round, state.RoundNumber);
        Cmp("HP",      blobReady ? _blob.PlayerHp : _sim.PlayerHp, pc.CurrentHp);
        Cmp("MaxHP",   blobReady ? _blob.PlayerMaxHp : _sim.PlayerMaxHp, pc.MaxHp);
        Cmp("Block",   blobReady ? _blob.PlayerBlock : _sim.PlayerBlock, pc.Block);

        if (pcs is not null)
        {
            int simHandCount = blobReady ? _blob.HandCount : _sim.HandCount;
            int simDrawCount = blobReady ? _blob.DrawCount : _sim.DrawCount;
            int simDiscCount = blobReady ? _blob.DiscCount : _sim.DiscCount;
            int simExhaustCount = blobReady ? _blob.ExhaustCount : _sim.ExhaustCount;
            Cmp("Energy",    blobReady ? _blob.Energy : _sim.Energy, pcs.Energy);
            Cmp("MaxEnergy", blobReady ? _blob.MaxEnergy : _sim.MaxEnergy, pcs.MaxEnergy);
            Cmp("HandN",     simHandCount,      pcs.Hand.Cards.Count);
            Cmp("DrawN",     simDrawCount,      pcs.DrawPile.Cards.Count);
            Cmp("DiscN",     simDiscCount,      pcs.DiscardPile.Cards.Count);
            Cmp("ExhN",      simExhaustCount,   pcs.ExhaustPile.Cards.Count);

            // First blob consumption path: read hand card hot data from the
            // frozen blob when it is available, otherwise fall back to legacy.
            Span<SimCard> blobHand = _blob.HandCards;
            int hn = Math.Min(simHandCount, pcs.Hand.Cards.Count);
            for (int i = 0; i < hn; i++)
            {
                SimCard sc = blobReady ? blobHand[i] : _sim.Hand[i];
                CardModel liveCard = pcs.Hand.Cards[i];
                bool   simU = sc.IsUpgraded;
                bool   livU = liveCard.IsUpgraded;
                ushort sid  = sc.BaseCardId;
                string simN = ReverseCardName(sid);
                string livN = liveCard.GetType().Name;

                int  simLocalCost = blobReady
                    ? SimCardEnergyOps.GetWithLocalModifiers(_blob, sc)
                    : SimCardEnergyOps.GetWithLocalModifiers(_sim, sc);
                int  liveLocalCost = liveCard.EnergyCost.GetWithModifiers(CostModifiers.Local);
                bool simHasLocal = (blobReady
                    ? SimCardEnergyOps.GetModifierCount(_blob, sc)
                    : SimCardEnergyOps.GetModifierCount(_sim, sc)) > 0;
                bool liveHasLocal = liveCard.EnergyCost.HasLocalModifiers;
                bool simX = sc.HasEnergyCostX;
                bool liveX = liveCard.EnergyCost.CostsX;
                int  simCapturedX = !simX ? 0
                    : blobReady
                        ? SimCardEnergyOps.GetCapturedXValue(_blob, sc)
                        : SimCardEnergyOps.GetCapturedXValue(_sim, sc);
                int  liveCapturedX = liveX ? liveCard.EnergyCost.CapturedXValue : 0;

                bool ok = simN == livN
                             && simU == livU
                             && simLocalCost == liveLocalCost
                             && simHasLocal == liveHasLocal
                             && simX == liveX
                             && simCapturedX == liveCapturedX;
                if (!ok) simAllOk = false;
                simSb.AppendLine(ok
                    ? $"✓ Hand[{i}]={simN}{(simU ? "+" : "")}" +
                        $" costL={simLocalCost}{(simHasLocal ? "*" : "")}{(simX ? $" X={simCapturedX}" : string.Empty)}"
                    : $"✗ Hand[{i}]: sim={simN}{(simU ? "+" : "")} live={livN}{(livU ? "+" : "")}" +
                        $" costL sim={simLocalCost}{(simHasLocal ? "*" : "")}" +
                        $" live={liveLocalCost}{(liveHasLocal ? "*" : "")}" +
                        $" X sim={simCapturedX} live={liveCapturedX}");
            }

            // Bulk pile diff: scan every card, summarize. A full per-card dump
            // would blow past column height for 50+ card decks; instead show
            // total mismatch count and the first few offenders.
            DiffPile(simSb, "Draw",
                blobReady ? _blob.DrawCards.Slice(0, simDrawCount) : _sim.Draw.AsSpan(0, simDrawCount),
                pcs.DrawPile.Cards,
                ref simAllOk);
            DiffPile(simSb, "Disc",
                blobReady ? _blob.DiscCards.Slice(0, simDiscCount) : _sim.Disc.AsSpan(0, simDiscCount),
                pcs.DiscardPile.Cards,
                ref simAllOk);
            DiffPile(simSb, "Exh",
                blobReady ? _blob.ExhaustCards.Slice(0, simExhaustCount) : _sim.Exhaust.AsSpan(0, simExhaustCount),
                pcs.ExhaustPile.Cards,
                ref simAllOk);
        }

        // ── Player powers: walk live list, look up sim slot via registry,
        //    compare amount. Powers not in the registry (e.g. game-data drift,
        //    or *.Powers.Mocks if any leak) are flagged once.
        DiffPowers(simSb, "P.Pwr", pc.Powers,
            blobReady ? SimPowerOps.GetPlayerRow(_blob) : SimPowerOps.GetPlayerRow(_sim),
            ref simAllOk);

        // Enemy counts + HP/Block/Intent for each.
        int simEnemyCount = blobReady ? _blob.EnemyCount : _sim.EnemyCount;
        Cmp("EnemyN", simEnemyCount, state.Enemies.Count);
        int en = Math.Min(simEnemyCount, state.Enemies.Count);
        for (int i = 0; i < en; i++)
        {
            Creature e = state.Enemies[i];
            Cmp($"E{i}.HP",    blobReady ? _blob.EnemyHp[i] : _sim.EnemyHp[i], e.CurrentHp);
            Cmp($"E{i}.MaxHP", blobReady ? _blob.EnemyMaxHp[i] : _sim.EnemyMaxHp[i], e.MaxHp);
            Cmp($"E{i}.Block", blobReady ? _blob.EnemyBlock[i] : _sim.EnemyBlock[i], e.Block);

            // Intent kind / damage / hits.
            DiffIntent(simSb, i, e, blobReady, ref simAllOk);

            // Per-enemy power row.
            DiffPowers(simSb, $"E{i}.Pwr", e.Powers,
                blobReady ? SimPowerOps.GetEnemyRow(_blob, i) : SimPowerOps.GetEnemyRow(_sim, i),
                ref simAllOk);
        }

        // ── RNG: re-capture every in-combat stream and byte-compare to
        //    the sim's stored copy. Equality proves Snapshot wrote each
        //    Knuth state (56-int seedArray + 2 cursors) bit-exact, which is
        //    the precondition for offline replay during DFS search.
        DiffAllRng(simSb, state, ref simAllOk);

        if (blobReady)
        {
            DiffBlobHotSlice(blobSb, ref blobAllOk);
            DiffBlobCleanupMutations(blobSb, ref blobAllOk);
        }

        if (simAllOk) simSb.Insert(simSb.ToString().IndexOf('\n', simSb.ToString().IndexOf("SIM DIFF")) + 1, "✓ ALL OK\n");
        if (blobAllOk) blobSb.Insert(blobSb.ToString().IndexOf('\n', blobSb.ToString().IndexOf("BLOB HOT SLICE")) + 1, "✓ ALL OK\n");

        simText = PackTwoPerLine(simSb.ToString());
        blobText = PackTwoPerLine(blobSb.ToString());
    }

    /// <summary>
    /// Post-process the SIM DIFF text: pair consecutive single-data lines so
    /// each terminal row shows two entries separated by " │ ". Lines that are
    /// indented (detail/mismatch entries starting with "  "), empty, the
    /// header, or "ALL OK" stay on their own row to preserve readability.
    /// Halves vertical height of the panel without losing any information.
    /// </summary>
    private static string PackTwoPerLine(string raw)
    {
        // Pad to a fixed cell width so the second column lines up vertically.
        // 22 chars covers "✓ MaxEnergy=N" / "✓ E5.MaxHP=NNNN" comfortably.
        const int CellW = 22;

        var lines  = raw.Split('\n');
        var sb     = new StringBuilder(raw.Length);
        string?    pending = null;

        bool IsStandalone(string s) =>
            s.Length == 0
            || s.StartsWith("  ", StringComparison.Ordinal)   // indented detail
            || s.StartsWith("──", StringComparison.Ordinal)   // header rule
            || s.Contains("SIM DIFF")
            || s.Contains("ALL OK")
            || s.StartsWith("✗ ", StringComparison.Ordinal)   // keep mismatches full-width
            || s.StartsWith("(", StringComparison.Ordinal);   // "(no local player)" etc.

        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i].TrimEnd('\r');

            if (IsStandalone(l))
            {
                // Flush any half-pair before emitting a standalone line.
                if (pending != null) { sb.AppendLine(pending); pending = null; }
                sb.AppendLine(l);
                continue;
            }

            if (pending == null) pending = l;
            else
            {
                // Pair: left padded to fixed width, then separator + right.
                sb.Append(pending);
                int pad = CellW - pending.Length;
                if (pad > 0) sb.Append(' ', pad);
                sb.Append(" │ ").AppendLine(l);
                pending = null;
            }
        }
        if (pending != null) sb.AppendLine(pending);
        return sb.ToString();
    }

    // ── SIM DIFF helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Compare a sim pile slice to a live <see cref="CardModel"/> list. Output
    /// is one summary line per pile plus up to 3 mismatch lines (capping output
    /// keeps the column readable for 100+ card decks).
    /// </summary>
    private static void DiffPile(
        StringBuilder sb, string tag,
        ReadOnlySpan<SimCard> simSlice,
        IReadOnlyList<CardModel> live,
        ref bool allOk)
    {
        int simCount = simSlice.Length;
        int n = Math.Min(simCount, live.Count);
        int diffs = 0;
        var firstMismatches = new System.Text.StringBuilder(64);
        for (int i = 0; i < n; i++)
        {
            SimCard sc = simSlice[i];
            bool   simU = sc.IsUpgraded;
            ushort sid  = sc.BaseCardId;
            string simN = ReverseCardName(sid);
            string livN = live[i].GetType().Name;
            bool   livU = live[i].IsUpgraded;
            if (simN != livN || simU != livU)
            {
                if (diffs < 3)
                    firstMismatches.AppendLine(
                        $"  ✗ {tag}[{i}]: sim={simN}{(simU ? "+" : "")} live={livN}{(livU ? "+" : "")}");
                diffs++;
            }
        }
        if (diffs == 0 && simCount == live.Count)
        {
            sb.AppendLine($"✓ {tag}({n})");
        }
        else
        {
            allOk = false;
            sb.AppendLine($"✗ {tag}: {diffs} mismatch(es), simN={simCount} liveN={live.Count}");
            if (firstMismatches.Length > 0) sb.Append(firstMismatches);
        }
    }

    private static void DiffBlobHotSlice(StringBuilder sb, ref bool allOk)
    {
        DiffBlobScalar(sb, "B.Round", _sim.Round, _blob.Round, ref allOk);
        DiffBlobScalar(sb, "B.HP", _sim.PlayerHp, _blob.PlayerHp, ref allOk);
        DiffBlobScalar(sb, "B.MaxHP", _sim.PlayerMaxHp, _blob.PlayerMaxHp, ref allOk);
        DiffBlobScalar(sb, "B.Block", _sim.PlayerBlock, _blob.PlayerBlock, ref allOk);
        DiffBlobScalar(sb, "B.Energy", _sim.Energy, _blob.Energy, ref allOk);
        DiffBlobScalar(sb, "B.MaxEn", _sim.MaxEnergy, _blob.MaxEnergy, ref allOk);
        DiffBlobScalar(sb, "B.Stars", _sim.PlayerStars, _blob.PlayerStars, ref allOk);
        DiffBlobSpan<short>(sb, "B.PPwr",
            SimPowerOps.GetPlayerRow(_sim),
            SimPowerOps.GetPlayerRow(_blob),
            ref allOk);
        DiffBlobSpan<SimPowerInternal>(sb, "B.PPwrI",
            MemoryMarshal.CreateReadOnlySpan(ref SimPowerOps.GetPlayerInternal(_sim), 1),
            MemoryMarshal.CreateReadOnlySpan(ref SimPowerOps.GetPlayerInternal(_blob), 1),
            ref allOk);

        DiffBlobScalar(sb, "B.EnemyN", (byte)_sim.EnemyCount, _blob.EnemyCount, ref allOk);
        DiffBlobSpan<ushort>(sb, "B.EHP",
            _sim.EnemyHp.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyHp.Slice(0, _blob.EnemyCount),
            ref allOk);
        DiffBlobSpan<ushort>(sb, "B.EMax",
            _sim.EnemyMaxHp.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyMaxHp.Slice(0, _blob.EnemyCount),
            ref allOk);
        DiffBlobSpan<ushort>(sb, "B.EBlk",
            _sim.EnemyBlock.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyBlock.Slice(0, _blob.EnemyCount),
            ref allOk);
        DiffBlobSpan<ushort>(sb, "B.EDmg",
            _sim.EnemyIntentDmg.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyIntentDmg.Slice(0, _blob.EnemyCount),
            ref allOk);
        DiffBlobSpan<byte>(sb, "B.EHits",
            _sim.EnemyIntentHits.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyIntentHits.Slice(0, _blob.EnemyCount),
            ref allOk);
        DiffBlobSpan<byte>(sb, "B.EKind",
            _sim.EnemyIntent.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyIntent.Slice(0, _blob.EnemyCount),
            ref allOk);
        DiffBlobSpan<short>(sb, "B.EPwr",
            _sim.EnemyPowers.AsSpan(0, _sim.EnemyCount * SimCombatState.PowersPerCre),
            _blob.EnemyPowers.Slice(0, _blob.EnemyCount * SimCombatState.PowersPerCre),
            ref allOk);
        DiffBlobSpan<SimPowerInternal>(sb, "B.EPwrI",
            _sim.EnemyPowerInternal.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyPowerInternal.Slice(0, _blob.EnemyCount),
            ref allOk);
        DiffBlobSpan<SimEnemyMoveSM>(sb, "B.EMove",
            _sim.EnemyMoveSM.AsSpan(0, _sim.EnemyCount),
            _blob.EnemyMoveSM.Slice(0, _blob.EnemyCount),
            ref allOk);

        DiffBlobPile(sb, "B.Hand", _sim.Hand, _sim.HandCount, _blob.HandCards, _blob.HandCount, ref allOk);
        DiffBlobPile(sb, "B.Draw", _sim.Draw, _sim.DrawCount, _blob.DrawCards, _blob.DrawCount, ref allOk);
        DiffBlobPile(sb, "B.Disc", _sim.Disc, _sim.DiscCount, _blob.DiscCards, _blob.DiscCount, ref allOk);
        DiffBlobPile(sb, "B.Exh", _sim.Exhaust, _sim.ExhaustCount, _blob.ExhaustCards, _blob.ExhaustCount, ref allOk);

        DiffBlobScalar(sb, "B.InstN", _sim.CardInstanceCount, _blob.CardInstanceCount, ref allOk);
        DiffBlobScalar(sb, "B.ModUsed", _sim.CardEnergyModifierUsed, _blob.CardEnergyModifierUsed, ref allOk);

        int cardSidecarLength = _sim.CardInstanceCount + 1;
        DiffBlobSpan<short>(sb, "B.EBase",
            _sim.CardEnergyBaseCost.AsSpan(0, cardSidecarLength),
            _blob.CardEnergyBaseCost.Slice(0, cardSidecarLength),
            ref allOk);
        DiffBlobSpan<ushort>(sb, "B.EX",
            _sim.CardEnergyCapturedX.AsSpan(0, cardSidecarLength),
            _blob.CardEnergyCapturedX.Slice(0, cardSidecarLength),
            ref allOk);
        DiffBlobSpan<ushort>(sb, "B.EStart",
            _sim.CardEnergyModifierStart.AsSpan(0, cardSidecarLength),
            _blob.CardEnergyModifierStart.Slice(0, cardSidecarLength),
            ref allOk);
        DiffBlobSpan<ushort>(sb, "B.ECount",
            _sim.CardEnergyModifierCount.AsSpan(0, cardSidecarLength),
            _blob.CardEnergyModifierCount.Slice(0, cardSidecarLength),
            ref allOk);
        DiffBlobModifierSpan(sb, "B.EMod",
            _sim.CardEnergyModifiers.AsSpan(0, _sim.CardEnergyModifierUsed),
            _blob.CardEnergyModifiers.Slice(0, _blob.CardEnergyModifierUsed),
            ref allOk);
    }

    private static void DiffBlobCleanupMutations(StringBuilder sb, ref bool allOk)
    {
        int handCount = Math.Min(_sim.HandCount, _blob.HandCount);
        DiffBlobCleanupMutationSet(sb, "B.PlayCln", handCount, endOfTurn: false, ref allOk);
        DiffBlobCleanupMutationSet(sb, "B.TurnCln", handCount, endOfTurn: true, ref allOk);
    }

    private static void DiffBlobCleanupMutationSet(
        StringBuilder sb,
        string tag,
        int handCount,
        bool endOfTurn,
        ref bool allOk)
    {
        if (handCount == 0)
        {
            sb.AppendLine($"✓ {tag}(0)");
            return;
        }

        int diffs = 0;
        var firstMismatches = new StringBuilder(96);
        for (int i = 0; i < handCount; i++)
        {
            _simScratch.CopyFrom(_sim);
            _blobScratch.CopyFrom(_blob);

            SimCard legacyCard = _simScratch.Hand[i];
            SimCard blobCard = _blobScratch.HandCards[i];
            bool legacyChanged = endOfTurn
                ? SimCardEnergyOps.EndOfTurnCleanup(_simScratch, legacyCard)
                : SimCardEnergyOps.AfterCardPlayedCleanup(_simScratch, legacyCard);
            bool blobChanged = endOfTurn
                ? SimCardEnergyOps.EndOfTurnCleanup(_blobScratch, blobCard)
                : SimCardEnergyOps.AfterCardPlayedCleanup(_blobScratch, blobCard);

            string? reason = null;
            bool ok = legacyCard.InstanceId == blobCard.InstanceId
                && legacyChanged == blobChanged
                && BlobCardEnergyInstanceEquals(_simScratch, legacyCard, _blobScratch, blobCard, out reason);
            if (ok)
                continue;

            diffs++;
            if (diffs <= 3)
            {
                firstMismatches.AppendLine(
                    $"  ✗ {tag}[{i}]: changed sim={legacyChanged} blob={blobChanged} " +
                    $"card={ReverseCardName(legacyCard.BaseCardId)}#{legacyCard.InstanceId} {reason}");
            }
        }

        if (diffs == 0)
        {
            sb.AppendLine($"✓ {tag}({handCount})");
            return;
        }

        allOk = false;
        sb.AppendLine($"✗ {tag}: {diffs} diff(s)");
        if (firstMismatches.Length > 0) sb.Append(firstMismatches);
    }

    private static void DiffBlobPile(
        StringBuilder sb, string tag,
        SimCard[] legacySlice, int legacyCount,
        Span<SimCard> blobSlice, int blobCount,
        ref bool allOk)
    {
        ReadOnlySpan<SimCard> legacy = legacySlice.AsSpan(0, legacyCount);
        ReadOnlySpan<SimCard> blob = blobSlice.Slice(0, blobCount);
        if (legacyCount == blobCount
            && MemoryMarshal.AsBytes(legacy).SequenceEqual(MemoryMarshal.AsBytes(blob)))
        {
            sb.AppendLine($"✓ {tag}({legacyCount})");
            return;
        }

        allOk = false;
        int diffs = legacyCount == blobCount ? 0 : 1;
        var firstMismatches = new StringBuilder(64);
        int n = Math.Min(legacyCount, blobCount);
        for (int i = 0; i < n; i++)
        {
            if (SimCardEquals(in legacy[i], in blob[i]))
                continue;

            if (diffs < 3)
            {
                firstMismatches.AppendLine(
                    $"  ✗ {tag}[{i}]: sim={DescribeSimCard(in legacy[i])} blob={DescribeSimCard(in blob[i])}");
            }
            diffs++;
        }

        sb.AppendLine($"✗ {tag}: {diffs} diff(s), simN={legacyCount} blobN={blobCount}");
        if (firstMismatches.Length > 0) sb.Append(firstMismatches);
    }

    private static void DiffBlobScalar<T>(StringBuilder sb, string tag, T legacy, T blob, ref bool allOk)
        where T : struct, IEquatable<T>
    {
        if (legacy.Equals(blob))
        {
            sb.AppendLine($"✓ {tag}={legacy}");
            return;
        }

        allOk = false;
        sb.AppendLine($"✗ {tag}: sim={legacy} blob={blob}");
    }

    private static void DiffBlobSpan<T>(
        StringBuilder sb,
        string tag,
        ReadOnlySpan<T> legacy,
        ReadOnlySpan<T> blob,
        ref bool allOk)
        where T : unmanaged, IEquatable<T>
    {
        if (legacy.Length == blob.Length
            && MemoryMarshal.AsBytes(legacy).SequenceEqual(MemoryMarshal.AsBytes(blob)))
        {
            sb.AppendLine($"✓ {tag}({legacy.Length})");
            return;
        }

        allOk = false;
        int diffs = legacy.Length == blob.Length ? 0 : 1;
        var firstMismatches = new StringBuilder(64);
        int n = Math.Min(legacy.Length, blob.Length);
        for (int i = 0; i < n; i++)
        {
            if (legacy[i].Equals(blob[i]))
                continue;

            if (diffs < 3)
                firstMismatches.AppendLine($"  ✗ {tag}[{i}]: sim={legacy[i]} blob={blob[i]}");
            diffs++;
        }

        sb.AppendLine($"✗ {tag}: {diffs} diff(s), simN={legacy.Length} blobN={blob.Length}");
        if (firstMismatches.Length > 0) sb.Append(firstMismatches);
    }

    private static void DiffBlobModifierSpan(
        StringBuilder sb,
        string tag,
        ReadOnlySpan<SimLocalCostModifier> legacy,
        ReadOnlySpan<SimLocalCostModifier> blob,
        ref bool allOk)
    {
        if (legacy.Length == blob.Length
            && MemoryMarshal.AsBytes(legacy).SequenceEqual(MemoryMarshal.AsBytes(blob)))
        {
            sb.AppendLine($"✓ {tag}({legacy.Length})");
            return;
        }

        allOk = false;
        int diffs = legacy.Length == blob.Length ? 0 : 1;
        var firstMismatches = new StringBuilder(64);
        int n = Math.Min(legacy.Length, blob.Length);
        for (int i = 0; i < n; i++)
        {
            if (SimLocalCostModifierEquals(in legacy[i], in blob[i]))
                continue;

            if (diffs < 3)
            {
                firstMismatches.AppendLine(
                    $"  ✗ {tag}[{i}]: sim={DescribeModifier(in legacy[i])} blob={DescribeModifier(in blob[i])}");
            }
            diffs++;
        }

        sb.AppendLine($"✗ {tag}: {diffs} diff(s), simN={legacy.Length} blobN={blob.Length}");
        if (firstMismatches.Length > 0) sb.Append(firstMismatches);
    }

    private static bool SimCardEquals(in SimCard left, in SimCard right)
        => left.CardId == right.CardId
        && left.InstanceId == right.InstanceId
        && left.BaseStarCost == right.BaseStarCost
        && left.LastStarsSpent == right.LastStarsSpent
        && left.BaseReplayCount == right.BaseReplayCount
        && left.Flags == right.Flags
        && left.EnchantmentId == right.EnchantmentId
        && left.EnchantmentAmount == right.EnchantmentAmount
        && left.AfflictionId == right.AfflictionId
        && left.AfflictionAmount == right.AfflictionAmount;

    private static bool SimLocalCostModifierEquals(in SimLocalCostModifier left, in SimLocalCostModifier right)
        => left.Amount == right.Amount
        && left.Flags == right.Flags;

    private static bool BlobCardEnergyInstanceEquals(
        SimCombatState legacyState,
        in SimCard legacyCard,
        CombatNodeBlob blobState,
        in SimCard blobCard,
        out string? reason)
    {
        ushort instanceId = legacyCard.InstanceId;
        if (instanceId != blobCard.InstanceId)
        {
            reason = $"iid sim={instanceId} blob={blobCard.InstanceId}";
            return false;
        }

        if (legacyState.CardEnergyBaseCost[instanceId] != blobState.CardEnergyBaseCost[instanceId])
        {
            reason = $"base sim={legacyState.CardEnergyBaseCost[instanceId]} blob={blobState.CardEnergyBaseCost[instanceId]}";
            return false;
        }

        if (legacyState.CardEnergyCapturedX[instanceId] != blobState.CardEnergyCapturedX[instanceId])
        {
            reason = $"x sim={legacyState.CardEnergyCapturedX[instanceId]} blob={blobState.CardEnergyCapturedX[instanceId]}";
            return false;
        }

        ushort legacyStart = legacyState.CardEnergyModifierStart[instanceId];
        ushort blobStart = blobState.CardEnergyModifierStart[instanceId];
        if (legacyStart != blobStart)
        {
            reason = $"start sim={legacyStart} blob={blobStart}";
            return false;
        }

        ushort legacyCount = legacyState.CardEnergyModifierCount[instanceId];
        ushort blobCount = blobState.CardEnergyModifierCount[instanceId];
        if (legacyCount != blobCount)
        {
            reason = $"count sim={legacyCount} blob={blobCount}";
            return false;
        }

        for (int offset = 0; offset < legacyCount; offset++)
        {
            ref SimLocalCostModifier legacyModifier = ref legacyState.CardEnergyModifiers[legacyStart + offset];
            ref SimLocalCostModifier blobModifier = ref blobState.CardEnergyModifiers[blobStart + offset];
            if (SimLocalCostModifierEquals(in legacyModifier, in blobModifier))
                continue;

            reason = $"mod[{offset}] sim={DescribeModifier(in legacyModifier)} blob={DescribeModifier(in blobModifier)}";
            return false;
        }

        int legacyCost = SimCardEnergyOps.GetWithLocalModifiers(legacyState, legacyCard);
        int blobCost = SimCardEnergyOps.GetWithLocalModifiers(blobState, blobCard);
        if (legacyCost != blobCost)
        {
            reason = $"cost sim={legacyCost} blob={blobCost}";
            return false;
        }

        reason = null;
        return true;
    }

    private static string DescribeSimCard(in SimCard card)
        => $"{ReverseCardName(card.BaseCardId)}{(card.IsUpgraded ? "+" : string.Empty)}" +
           $"#iid={card.InstanceId} star={card.BaseStarCost} spent={card.LastStarsSpent} rep={card.BaseReplayCount} " +
           $"flags=0x{card.Flags:X} enc={card.EnchantmentId}:{card.EnchantmentAmount} aff={card.AfflictionId}:{card.AfflictionAmount}";

    private static string DescribeModifier(in SimLocalCostModifier modifier)
        => $"amt={modifier.Amount} type={modifier.Type} exp={modifier.Expiration} reduce={modifier.IsReduceOnly}";

    /// <summary>
    /// Compare a live <see cref="PowerModel"/> list to the corresponding dense
    /// sim row.
    /// </summary>
    private static void DiffPowers(
        StringBuilder sb, string tag,
        IReadOnlyList<PowerModel> live, ReadOnlySpan<short> simRow,
        ref bool allOk)
    {
        // Live → sim direction: every live power must be reflected in the sim row.
        int diffs = 0;
        var msgs = new System.Text.StringBuilder(64);
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
            short simAmt  = simRow[idx];
            int   liveAmt = p.Amount;
            if (simAmt != liveAmt)
            {
                diffs++;
                if (diffs <= 3) msgs.AppendLine($"  ✗ {tag}.{t.Name}: sim={simAmt} live={liveAmt}");
            }
        }
        // Sim → live direction: any non-zero sim slot whose power is not in live
        // means we wrote a phantom power. Cheap to detect; iterates 259 ints.
        for (int idx = 0; idx < SimCombatState.PowersPerCre; idx++)
        {
            short simAmt = simRow[idx];
            if (simAmt == 0) continue;
            // Look for matching live power.
            bool found = false;
            for (int i = 0; i < liveCount; i++)
            {
                if (SimPowerRegistry.TryGetIndex(live[i].GetType(), out int liveIdx) && liveIdx == idx)
                { found = true; break; }
            }
            if (!found)
            {
                diffs++;
                if (diffs <= 3) msgs.AppendLine($"  ✗ {tag}: phantom slot[{idx}]={simAmt}");
            }
        }

        if (diffs == 0)
        {
            // Compact: count only living powers (non-zero) to keep noise low.
            int activeCount = 0;
            for (int i = 0; i < liveCount; i++)
                if (SimPowerRegistry.TryGetIndex(live[i].GetType(), out _)) activeCount++;
            sb.AppendLine($"✓ {tag}({activeCount})");
        }
        else
        {
            allOk = false;
            sb.AppendLine($"✗ {tag}: {diffs} diff(s)");
            if (msgs.Length > 0) sb.Append(msgs);
        }
    }

    /// <summary>
    /// Reclassify the live enemy's intent and compare to the captured
    /// <see cref="SimCombatState.EnemyIntent"/> byte; for Attack/DeathBlow,
    /// also verify base damage and hit count match what Snapshot computed.
    /// </summary>
    private static void DiffIntent(StringBuilder sb, int i, Creature e, bool blobReady, ref bool allOk)
    {
        // Mirror the classification logic in SimCombatState.Snapshot.CaptureIntent.
        var move  = e.Monster?.NextMove;
        SimIntent liveKind = SimIntent.Unknown;
        ushort liveDmg     = 0;
        byte   liveHits    = 0;
        if (move != null && move.Intents.Count > 0)
        {
            switch (move.Intents[0])
            {
                case DeathBlowIntent dbi:
                    liveKind = SimIntent.DeathBlow;
                    liveDmg  = AttackDamageFor(dbi);
                    liveHits = AttackHitsFor(dbi);
                    break;
                case AttackIntent ai:
                    liveKind = SimIntent.Attack;
                    liveDmg  = AttackDamageFor(ai);
                    liveHits = AttackHitsFor(ai);
                    break;
                case BuffIntent:       liveKind = SimIntent.Buff; break;
                case CardDebuffIntent: liveKind = SimIntent.CardDebuff; break;
                case DebuffIntent dbi:
                    liveKind = dbi.IntentType == IntentType.DebuffStrong
                        ? SimIntent.DebuffStrong : SimIntent.Debuff;
                    break;
                case DefendIntent: liveKind = SimIntent.Defend; break;
                case EscapeIntent: liveKind = SimIntent.Escape; break;
                case HealIntent:   liveKind = SimIntent.Heal; break;
                case HiddenIntent: liveKind = SimIntent.Hidden; break;
                case SleepIntent:  liveKind = SimIntent.Sleep; break;
                case StatusIntent: liveKind = SimIntent.StatusCard; break;
                case StunIntent:   liveKind = SimIntent.Stun; break;
                case SummonIntent: liveKind = SimIntent.Summon; break;
            }
        }

        SimIntent simKind = (SimIntent)(blobReady ? _blob.EnemyIntent[i] : _sim.EnemyIntent[i]);
        bool kindOk = simKind == liveKind;
        if (!kindOk) allOk = false;
        sb.AppendLine(kindOk
            ? $"✓ E{i}.Intent={simKind}"
            : $"✗ E{i}.Intent: sim={simKind} live={liveKind}");

        if (liveKind == SimIntent.Attack || liveKind == SimIntent.DeathBlow)
        {
            ushort simDmg  = blobReady ? _blob.EnemyIntentDmg[i] : _sim.EnemyIntentDmg[i];
            byte   simHits = blobReady ? _blob.EnemyIntentHits[i] : _sim.EnemyIntentHits[i];
            bool   dmgOk   = simDmg  == liveDmg;
            bool   hitsOk  = simHits == liveHits;
            if (!dmgOk)  { allOk = false; sb.AppendLine($"✗ E{i}.Dmg: sim={simDmg} live={liveDmg}"); }
            else         {                 sb.AppendLine($"✓ E{i}.Dmg={simDmg}"); }
            if (!hitsOk) { allOk = false; sb.AppendLine($"✗ E{i}.Hits: sim={simHits} live={liveHits}"); }
            else         {                 sb.AppendLine($"✓ E{i}.Hits={simHits}"); }
        }
    }

    private static ushort AttackDamageFor(AttackIntent ai)
    {
        var calc = ai.DamageCalc;
        if (calc == null) return 0;
        decimal raw = calc();
        if (raw < 0m)     return 0;
        if (raw > 65535m) return 65535;
        return (ushort)raw;
    }

    private static byte AttackHitsFor(AttackIntent ai)
    {
        int hits = ai.Repeats + 1;
        if (hits < 1)   hits = 1;
        if (hits > 255) hits = 255;
        return (byte)hits;
    }

    /// <summary>
    /// Re-capture every in-combat live RNG stream and byte-compare to the
    /// sim's stored slot. Stack-allocated scratch — zero heap. One ✓/✗ line
    /// per stream; mismatches show the cursor pair sim vs live.
    /// </summary>
    private static unsafe void DiffAllRng(StringBuilder sb, CombatState state, ref bool allOk)
    {
        var rngSet = state.RunState.Rng;
        DiffOneRng(sb, "RngShuf",     rngSet.Shuffle,              SimRngSlot.Shuffle,              ref allOk);
        DiffOneRng(sb, "RngTgt",      rngSet.CombatTargets,        SimRngSlot.CombatTargets,        ref allOk);
        DiffOneRng(sb, "RngCardGen",  rngSet.CombatCardGeneration, SimRngSlot.CombatCardGeneration, ref allOk);
        DiffOneRng(sb, "RngCardSel",  rngSet.CombatCardSelection,  SimRngSlot.CombatCardSelection,  ref allOk);
        DiffOneRng(sb, "RngEnergy",   rngSet.CombatEnergyCosts,    SimRngSlot.CombatEnergyCosts,    ref allOk);
        DiffOneRng(sb, "RngOrb",      rngSet.CombatOrbGeneration,  SimRngSlot.CombatOrbGeneration,  ref allOk);
        DiffOneRng(sb, "RngMonAi",    rngSet.MonsterAi,            SimRngSlot.MonsterAi,            ref allOk);
        DiffOneRng(sb, "RngNiche",    rngSet.Niche,                SimRngSlot.Niche,                ref allOk);
    }

    private static unsafe void DiffOneRng(
        StringBuilder sb, string tag, Rng liveRng, SimRngSlot slot, ref bool allOk)
    {
        try
        {
            RandomState live = default;
            RandomStateOps.CaptureFromRng(liveRng, ref live);
            ref RandomState sim = ref _sim.Rng(slot);

            bool ok = sim.INext == live.INext && sim.INextp == live.INextp;
            if (ok)
            {
                for (int k = 0; k < RandomState.ArrLen; k++)
                {
                    if (sim.Arr[k] != live.Arr[k]) { ok = false; break; }
                }
            }

            if (ok) sb.AppendLine($"✓ {tag}");
            else
            {
                allOk = false;
                sb.AppendLine(
                    $"✗ {tag}: iN sim={sim.INext} live={live.INext} " +
                    $"iNp sim={sim.INextp} live={live.INextp}");
            }
        }
        catch (Exception ex)
        {
            allOk = false;
            sb.AppendLine($"✗ {tag} threw: {ex.Message}");
        }
    }

    // Lazy reverse map ushort id → type name, built once on first diff.
    private static System.Collections.Generic.Dictionary<ushort, string>? _cardNameById;
    private static string ReverseCardName(ushort id)
    {
        if (_cardNameById is null)
        {
            var fld = typeof(SimCardDb).GetField("_byType",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            _cardNameById = new System.Collections.Generic.Dictionary<ushort, string>();
            if (fld?.GetValue(null) is System.Collections.Frozen.FrozenDictionary<Type, ushort> map)
                foreach (var kv in map) _cardNameById[kv.Value] = kv.Key.Name;
        }
        return _cardNameById.TryGetValue(id, out string? n) ? n : $"id{id}?";
    }

    private static string BuildRelicText(CombatState state)
    {
        var sb = new StringBuilder(256);
        Player? me = LocalContext.GetMe(state);
        if (me is null) return string.Empty;

        var relics = me.Relics.Where(r => !r.IsMelted).ToList();
        sb.AppendLine($"── RELICS ({relics.Count}) ──");
        foreach (RelicModel relic in relics)
        {
            string statusTag = relic.Status == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active   ? " *" :
                               relic.Status == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Disabled ? " !" : "";
            sb.AppendLine($"{relic.Id.Entry}{statusTag}");
        }
        return sb.ToString();
    }

    // ── Next-turn prediction (V0) ─────────────────────────────────────────────
    //
    // V0 deliberately ignores all Hooks (ModifyHandDraw, ModifyShuffleOrder, ...).
    // The point is to use the diff against reality to discover which hooks must
    // eventually be modeled. The simulation only does what we can prove from
    // first principles by reading the game source:
    //
    //   1. End of player turn:
    //        non-Retain non-Ethereal hand cards → DiscardPile
    //        Ethereal hand cards               → ExhaustPile (irrelevant for draw)
    //        Retain hand cards                 → stay in hand for next turn
    //   2. Start of next turn: draw 5 cards (hardcoded V0).
    //   3. If DrawPile empties mid-draw, replicate `CardPileCmd.Shuffle`:
    //        - all DiscardPile cards become the new DrawPile
    //        - StableShuffle = list.Sort() + list.UnstableShuffle(rng)
    //        - the rng we use is a clone of `Player.RunState.Rng.Shuffle`
    //          via `new Rng(seed, counter)`.
    //
    // The clone preserves the exact System.Random state, so future NextInt()
    // calls produce the same sequence as the live game would.

    private static List<CardModel> ComputePredictedHand(CombatState state, out int handDrawCount)
    {
        handDrawCount = 5; // V0
        Player? me = LocalContext.GetMe(state);
        if (me?.PlayerCombatState is not { } pcs) return new List<CardModel>();

        // Cards that survive turn-end into the next turn's hand.
        var retained  = pcs.Hand.Cards.Where(c => c.ShouldRetainThisTurn).ToList();
        var toDiscard = pcs.Hand.Cards
            .Where(c => !c.ShouldRetainThisTurn && !c.Keywords.Contains(CardKeyword.Ethereal))
            .ToList();

        var simDraw    = pcs.DrawPile.Cards.ToList();
        var simDiscard = pcs.DiscardPile.Cards.ToList();
        simDiscard.AddRange(toDiscard);

        // Snapshot the RNG state so we don't disturb the live game.
        Rng liveRng = me.RunState.Rng.Shuffle;
        var simRng  = new Rng(liveRng.Seed, liveRng.Counter);

        var result = new List<CardModel>(retained);
        int needed = Math.Max(0, handDrawCount - retained.Count);

        for (int i = 0; i < needed; i++)
        {
            if (simDraw.Count == 0)
            {
                if (simDiscard.Count == 0) break;
                // Replicate ListExtensions.StableShuffle without its
                // `T : IComparable<T>` constraint (CardModel only implements
                // IComparable<AbstractModel>). The algorithm is identical:
                // canonicalize order via Sort, then Fisher-Yates with the rng.
                simDiscard.Sort((a, b) => a.CompareTo(b));
                simDiscard.UnstableShuffle(simRng);
                simDraw    = simDiscard;
                simDiscard = new List<CardModel>();
            }
            result.Add(simDraw[0]);
            simDraw.RemoveAt(0);
        }
        return result;
    }

    /// <summary>Compact one-line label per card: "Strike" or "Strike[+]".</summary>
    private static string CardLabel(CardModel c) => c.IsUpgraded ? $"{c.Title}[+]" : c.Title;

    private static string BuildPredictionText(CombatState state)
    {
        var sb = new StringBuilder(512);

        Player? me = LocalContext.GetMe(state);
        if (me?.PlayerCombatState is null)
            return string.Empty;

        // ── Round-change detection (decoupled two-stage) ─────────────────────
        // Stage 1: the frame RoundNumber increments, lock in the prediction
        //          built at the end of the previous round and reset the
        //          actual-capture peak tracker.
        if (_roundFromLastRefresh > 0
            && state.RoundNumber > _roundFromLastRefresh
            && _liveFromLastRefresh is not null)
        {
            _frozenPrediction      = _liveFromLastRefresh;
            _frozenForRound        = state.RoundNumber;
            _frozenActual          = null;
            _frozenActualPeakCount = 0;
        }

        // Stage 2: monotonic peak tracking. Drawing happens card-by-card across
        //          many _Process frames, so the hand grows 0 → 1 → 2 → … → N.
        //          Once the player plays a card, count drops and we stop
        //          updating, which locks the snapshot at the post-draw state.
        //          This is independent of the prediction in every way.
        if (_frozenForRound == state.RoundNumber
            && me.PlayerCombatState is { } pcsCapture
            && pcsCapture.Hand.Cards.Count > _frozenActualPeakCount)
        {
            _frozenActual          = pcsCapture.Hand.Cards.Select(CardLabel).ToList();
            _frozenActualPeakCount = pcsCapture.Hand.Cards.Count;
        }

        // Compute fresh prediction for the *next* turn.
        var predicted = ComputePredictedHand(state, out int drawCount);
        var liveLabels = predicted.Select(CardLabel).ToList();

        Rng rng = me.RunState.Rng.Shuffle;
        sb.AppendLine($"── PREDICT T{state.RoundNumber + 1} ──");
        sb.AppendLine($" rng s:{rng.Seed} c:{rng.Counter}");
        int retainedCount = predicted.Count > 0
            ? predicted.TakeWhile(c => c.ShouldRetainThisTurn).Count()
            : 0;
        sb.AppendLine($" retain:{retainedCount} draw:{drawCount - retainedCount}");
        sb.AppendLine();
        for (int i = 0; i < liveLabels.Count; i++)
        {
            string mark = i < retainedCount ? "[R]" : "   ";
            sb.AppendLine($" {mark} {liveLabels[i]}");
        }

        // ── Verification block ────────────────────────────────────────────────
        if (_frozenPrediction is not null && _frozenActual is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"── VERIFY T{_frozenForRound} ──");
            int max = Math.Max(_frozenPrediction.Count, _frozenActual.Count);
            int hits = 0;
            for (int i = 0; i < max; i++)
            {
                string p = i < _frozenPrediction.Count ? _frozenPrediction[i] : "(none)";
                string a = i < _frozenActual.Count     ? _frozenActual[i]     : "(none)";
                bool ok = p == a;
                if (ok) hits++;
                sb.AppendLine(ok ? $" v {p}" : $" x {p}");
                if (!ok) sb.AppendLine($"   -> {a}");
            }
            sb.AppendLine();
            sb.AppendLine($" {hits}/{max} match");
        }

        // Cache for next refresh's round-change detection.
        _liveFromLastRefresh  = liveLabels;
        _roundFromLastRefresh = state.RoundNumber;

        return sb.ToString();
    }

    // ── Potion helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Wipes and rebuilds the potion button column from scratch.
    /// Called once per combat, from OnCombatRoomReady after the scene tree is ready.
    /// </summary>
    private static void RebuildPotionButtons(CombatState state)
    {
        if (_potionButtonBox is null || !GodotObject.IsInstanceValid(_potionButtonBox)) return;

        // Destroy all previous children (header + buttons from last combat if any).
        foreach (Node child in _potionButtonBox.GetChildren())
            child.QueueFree();
        _potionButtons.Clear();
        _potionTitles.Clear();

        Player? me = LocalContext.GetMe(state);
        if (me is null) return;

        // Only potions that can be actively triggered by the player during combat.
        var combatPotions = me.Potions
            .Where(p => p.Usage == PotionUsage.CombatOnly || p.Usage == PotionUsage.AnyTime)
            .ToList();

        // Non-interactive header label.
        var header = new Label { Text = $"── POTIONS ({combatPotions.Count}) ──" };
        header.AddThemeFontSizeOverride("font_size", 13);
        header.MouseFilter = Control.MouseFilterEnum.Ignore;
        _potionButtonBox.AddChild(header);

        foreach (PotionModel potion in combatPotions)
        {
            string id    = potion.Id.Entry;
            string title = potion.Title.GetFormattedText();
            _potionTitles[id] = title;

            var btn = new Button
            {
                Text              = GetPotionButtonText(id),
                CustomMinimumSize = new Vector2(200f, 24f),
                ZIndex            = 100,
                // MouseFilter defaults to Stop: button consumes mouse events,
                // so clicks do NOT reach the game's card/targeting layer.
            };
            btn.AddThemeFontSizeOverride("font_size", 12);

            string capturedId = id;
            btn.Pressed += () =>
            {
                // Toggle the authoritative selection set.
                if (!_allowedPotionIds.Remove(capturedId))
                    _allowedPotionIds.Add(capturedId);

                // Update only this button's label — no full scene Refresh needed.
                if (_potionButtons.TryGetValue(capturedId, out Button? b)
                    && GodotObject.IsInstanceValid(b))
                {
                    b.Text = GetPotionButtonText(capturedId);
                }
                UpdateApprovedLabel();
            };

            _potionButtonBox.AddChild(btn);
            _potionButtons[id] = btn;
        }

        if (combatPotions.Count == 0)
        {
            var none = new Label { Text = "  (none)" };
            none.MouseFilter = Control.MouseFilterEnum.Ignore;
            _potionButtonBox.AddChild(none);
        }

        UpdateApprovedLabel();
    }

    /// <summary>
    /// Lightweight update called on every CombatStateChanged.
    /// Marks consumed potions as disabled and removes them from the allowed set.
    /// Does NOT rebuild button nodes.
    /// </summary>
    private static void RefreshPotionButtons(CombatState state)
    {
        if (_potionButtons.Count == 0) return;

        Player? me = LocalContext.GetMe(state);
        var alive = me?.Potions.Select(p => p.Id.Entry).ToHashSet()
                    ?? new HashSet<string>();

        // If a potion was consumed mid-combat, evict it from the allowed set
        // so the AI never operates on a stale approval.
        _allowedPotionIds.IntersectWith(alive);

        foreach (var (id, btn) in _potionButtons)
        {
            if (!GodotObject.IsInstanceValid(btn)) continue;
            bool consumed = !alive.Contains(id);
            btn.Disabled = consumed;
            btn.Text = consumed
                ? $"[x] {(_potionTitles.TryGetValue(id, out string? t) ? t : id)}"
                : GetPotionButtonText(id);
        }

        UpdateApprovedLabel();
    }

    /// <summary>Returns the toggle-prefix + localized title for a potion button.</summary>
    private static string GetPotionButtonText(string id)
    {
        string title = _potionTitles.TryGetValue(id, out string? t) ? t : id;
        return (_allowedPotionIds.Contains(id) ? "[v] " : "[ ] ") + title;
    }

    /// <summary>
    /// Refreshes the "AI approved" summary label below the toggle buttons.
    /// Always reads from _state and _allowedPotionIds; safe to call at any time.
    /// </summary>
    private static void UpdateApprovedLabel()
    {
        if (_potionApprovedLabel is null || !GodotObject.IsInstanceValid(_potionApprovedLabel)) return;
        if (_state is null) { _potionApprovedLabel.Text = string.Empty; return; }

        Player? me = LocalContext.GetMe(_state);
        var alive = me?.Potions.Select(p => p.Id.Entry).ToHashSet()
                    ?? new HashSet<string>();

        var approved = _allowedPotionIds.Where(id => alive.Contains(id)).ToList();
        _potionApprovedLabel.Text = approved.Count == 0
            ? "── AI POTIONS ──\n  (none)"
            : "── AI POTIONS ──\n" + string.Join("\n", approved.Select(id =>
                $"  {(_potionTitles.TryGetValue(id, out string? t) ? t : id)}"));
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
