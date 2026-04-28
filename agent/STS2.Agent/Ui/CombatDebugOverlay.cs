using System;
using System.Collections.Generic;
using System.Linq;
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
    private static CombatState? _state;

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
