using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Infrastructure.Persistence;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private bool _routeSubview;
    private bool _routeEditingCombat, _routeDefaultQueued;
    private PredictorRun? _routeRuntime;
    private string _routeRuntimeKey = "", _routeMessage = "";
    private string _routeAnalysisKey = "";
    private MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState? _routePredictionUnlocks;
    private readonly Dictionary<int, ImmutableArray<MapPosition>> _routeEdits = [];
    private string RouteSavePath => System.IO.Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state", "routes",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_routeRuntimeKey))) + ".json");
    private string RText(string key) => Text("predictor.runtime." + key);
    private string RGold(PredictorReward reward) => reward.GoldAmountUnspecified
        ? RText("gold_recovered_unexpanded") : RText("gold") + " · " + reward.GoldAmount;
    private string RName(ModelKey key) => _contentNames!.Resolve(key, key.Category switch
    { "CARD" => GameContentKind.Card, "POTION" => GameContentKind.Potion, "CHARACTER" => GameContentKind.Character,
      "ENCOUNTER" => GameContentKind.Encounter, "EVENT" => GameContentKind.Event, "ACT" => GameContentKind.Act, _ => GameContentKind.Relic });
    private string RCard(PredictorCard card) => RName(card.Key) + (card.UpgradeLevel > 0 ? " +" + card.UpgradeLevel : "") +
        (card.Key.Entry == "MAD_SCIENCE" ? " · " + RText("option." + card.TinkerTimeType.ToString().ToUpperInvariant()) +
            " · " + RText("option." + card.TinkerTimeRider.ToString().ToUpperInvariant()) : "") + (card.Id > 0 ? " · #" + card.Id : "");
    private Button RButton(Node parent, string text, Action click, bool enabled = true)
    {
        var renderedRun = _routeRuntime;
        string renderedContext = _routeAnalysisKey;
        long? renderedRevision = renderedRun?.Revision;
        var b = _palette.Button(text); b.Disabled = !enabled;
        b.ClipText = true; b.TooltipText = text;
        b.CustomMinimumSize = new(128, 40);
        b.SizeFlagsHorizontal = SizeFlags.Fill;
        b.Pressed += () => Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(this) && GodotObject.IsInstanceValid(b) && b.IsInsideTree() && !b.IsQueuedForDeletion() &&
                IsInsideTree() && IsVisibleInTree() && _routeSubview && renderedContext == _routeAnalysisKey &&
                ReferenceEquals(renderedRun, _routeRuntime) && renderedRevision == _routeRuntime?.Revision) click();
        }).CallDeferred();
        parent.AddChild(b); return b;
    }
    private HFlowContainer RFlow(Node parent)
    { var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; parent.AddChild(flow); return flow; }
    private void RouteAction(Action action)
    {
        try
        {
            action();
            if (_routeRuntime!.Phase == PredictorPhase.Complete) _routeEditingCombat = false;
            ApplyRouteCombatDefault();
            _routeMessage = ""; SaveRouteBoundary();
        }
        catch (Exception ex)
        {
            _routeMessage = ex.Message == "PredictorSealOfGoldBudgetParameterUnassigned" ? RText("seal_pending") : RText("action_failed");
            RolltheSpire2.Bootstrap.RuntimeLog.Error("PredictorRuntimeUi: " + ex);
        }
        RenderWorkbench();
    }
    private void RouteSubmit(PredictorRequest request, PredictorInput input) => RouteAction(() =>
    {
        var remembered = RememberRouteInput(_routeRuntime!, input);
        var result = _routeRuntime!.Submit(request, input);
        // TooFull opens replacement; NotAllowed can settle a real failed
        // procurement (e.g. Sozu). Neither is an invalid authoring input.
        if (result is PredictorInputResult.Invalid or PredictorInputResult.Stale)
            throw new InvalidOperationException("PredictorInput:" + result);
        if (result == PredictorInputResult.Accepted && remembered != null) _routeRuntime.SetPlannedNode(remembered);
        if (input is AdjustCurrentCombat) _routeEditingCombat = true;
        else if (input is ResolveCombat) _routeEditingCombat = false;
    });
    private void ApplyRouteCombatDefault()
    {
        if (!_routeEditingCombat && _routeRuntime?.DefaultCombatInput() is { } input)
        {
            var result = _routeRuntime.Submit(_routeRuntime.Request!, input);
            if (result != PredictorInputResult.Accepted)
                throw new InvalidOperationException("PredictorDefaultCombat:" + result);
        }
        if (!_routeEditingCombat && _routeRuntime is { AutomaticRewardPickup: true } run)
            while (PredictorRewardPickupPolicy.Plan(run).NextInput is { } pickup)
            {
                var result = run.Submit(run.Request!, pickup);
                if (result != PredictorInputResult.Accepted)
                    throw new InvalidOperationException("PredictorDefaultPickup:" + result);
            }
    }
    private void SaveRouteBoundary() => PredictorRunStore.Save(RouteSavePath, _routeRuntime!);
    private void BindRouteContext(SeedPredictionRequest request, SeedPredictionDocument document,
        MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState? unlocks)
    {
        var authority = request.Authority;
        string facts = System.Text.Json.JsonSerializer.Serialize(new
        {
            Epochs = unlocks?.UnlockedEpochs.Order(StringComparer.Ordinal).ToArray(),
            Encounters = unlocks?.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry).Serialized).Order(StringComparer.Ordinal).ToArray(),
            Runs = unlocks?.NumberOfRuns,
            DiscoveredActs = authority.WorldAuthority?.Beta109Generation?.ActSelectionGroups.SelectMany(g => g.Candidates)
                .Where(c => c.DiscoveredInSingleplayer).Select(c => c.ActKey.Serialized).Distinct().Order(StringComparer.Ordinal).ToArray()
        });
        string key = string.Join("/", document.CanonicalSeed, request.Character.CharacterKey.Serialized,
            request.Ascension, request.PlayersCount, request.PlayerSlotIndex, authority.ProfileId, authority.GameVersion,
            authority.CatalogFingerprint, authority.UnlockSnapshotFingerprint, authority.WorldSnapshotFingerprint, facts);
        if (_routeAnalysisKey != key)
        {
            _routeRuntime = null; _routeRuntimeKey = ""; _routeMessage = "";
            _routeEditingCombat = _routeDefaultQueued = false; _routeEdits.Clear();
        }
        _routeAnalysisKey = key;
        _routePredictionUnlocks = unlocks is null ? null : LobbyUnlockReadout.Copy(unlocks);
    }
    private bool RouteContextDiffers(PredictorContext context)
    {
        var current = _routePredictionUnlocks;
        var world = LastRequest?.Authority.WorldAuthority?.Beta109Generation;
        return current is null || world is null || context.NumberOfRuns != current.NumberOfRuns ||
            !context.RevealedEpochs.ToHashSet(StringComparer.Ordinal).SetEquals(current.UnlockedEpochs) ||
            !context.SeenEncounters.ToHashSet().SetEquals(current.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry))) ||
            !context.DiscoveredActs.ToHashSet().SetEquals(world.ActSelectionGroups.SelectMany(g => g.Candidates)
                .Where(c => c.DiscoveredInSingleplayer).Select(c => c.ActKey));
    }
    private void EnsureRouteRuntime()
    {
        var request = LastRequest!;
        string key = LastDocument!.CanonicalSeed + "/" + request.Character.CharacterKey.Serialized + "/" + request.Ascension;
        if (_routeRuntime != null && key == _routeRuntimeKey) return;
        // Never silently overwrite a failed/mismatched saved run.
        string path = System.IO.Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state", "routes",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
        PredictorRun created;
        if (System.IO.File.Exists(path))
        {
            var frozen = PredictorRunStore.ReadFrozenContext(path);
            if (frozen.Seed != LastDocument.CanonicalSeed || frozen.Character != request.Character.CharacterKey || frozen.Ascension != request.Ascension)
                throw new InvalidDataException("PredictorSavedContextMismatch");
            // Account progress can advance between sessions. Revalidate the
            // saved frozen facts against this game catalog, not a newer account.
            var probe = PredictorSeedCapture.CreateRun(frozen);
            created = PredictorRunStore.Load(path, probe.Context.CatalogFingerprint, probe.Context.UnlockFingerprint);
        }
        else created = PredictorSeedCapture.CreateFromPrediction(request, _routePredictionUnlocks!);
        if (created.Context.Crystal is not null) throw new InvalidDataException("PredictorRouteContextIsLocalCrystal");
        _routeRuntimeKey = key; _routeRuntime = created;
        _routeEditingCombat = created.Project().CombatInputForEditing != null;
        _routeEdits.Clear(); _routeMessage = "";
    }
    private void RenderRouteRuntime(int act)
    {
        if (LastRequest!.PlayersCount != 1)
        { ShowMapRail(false); _wbCenter.AddChild(WLabel(RText("singleplayer"))); return; }
        if (LastRequest.Authority.ProfileId != RuntimeProfileId.Beta111)
        { ShowMapRail(false); _wbCenter.AddChild(WLabel(RText("profile"))); return; }
        if (_routePredictionUnlocks is null)
        { ShowMapRail(false); _wbCenter.AddChild(WLabel(RText("context_missing"))); return; }
        try { EnsureRouteRuntime(); }
        catch (Exception ex)
        {
            ShowMapRail(false); _wbCenter.AddChild(WLabel(RText("load_failed")));
            RenderRouteRestart(_wbCenter);
            RolltheSpire2.Bootstrap.RuntimeLog.Error("PredictorRuntimeInitialize: " + ex); return;
        }
        var run = _routeRuntime!;
        var view = run.Project();
        if (!_routeEditingCombat && !_routeDefaultQueued && _routeMessage.Length == 0 &&
            (run.DefaultCombatInput() != null || run.AutomaticRewardPickup && PredictorRewardPickupPolicy.Plan(run).NextInput != null))
        {
            var expected = run.Request;
            _routeDefaultQueued = true;
            Callable.From(() =>
            {
                _routeDefaultQueued = false;
                if (GodotObject.IsInstanceValid(this) && IsInsideTree() && IsVisibleInTree() && _routeSubview && ReferenceEquals(run, _routeRuntime) && run.Request == expected)
                    RouteAction(() => { });
            }).CallDeferred();
        }
        RenderRouteInventory(view);
        var heading = RFlow(_wbCenter);
        RenderRouteCombatDefaultsButton(heading);
        heading.AddChild(_palette.Label(string.Format(RText("position"), view.Position.Act, view.Position.Floor), 18));
        RenderRouteRestart(heading);
        RButton(heading, RText("resume_boundary"), () => RouteAction(() =>
        {
            _routeRuntime = PredictorRunStore.Load(RouteSavePath, run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
            _routeEditingCombat = _routeRuntime.Project().CombatInputForEditing != null;
        }), System.IO.File.Exists(RouteSavePath));
        if (_routeMessage.Length > 0) _wbCenter.AddChild(WLabel(_routeMessage));
        _wbCenter.AddChild(WLabel(RText("from_seed")));
        _wbCenter.AddChild(WLabel(RText("premise")));
        if (RouteContextDiffers(run.Context))
            _wbCenter.AddChild(WLabel(RText("saved_context")));
        _wbCenter.AddChild(WLabel(RText("saved_boundary")));
        var node = WColumn(); node.Name = "RouteCurrentNode"; _wbCenter.AddChild(node);
        var source = view.CurrentObtainSource ?? view.CurrentEvent ?? view.CurrentAncient ?? view.CurrentNodeModel;
        node.AddChild(WLabel(source is { } identity ? RName(identity) : RText("current_node"), true));
        if (view.Phase == PredictorPhase.CombatFacts) RenderRouteCombatPreview(node);
        RenderRouteChoices(node, view);
        foreach (var health in view.CurrentHealthEffects)
            node.AddChild(WLabel(string.Format(RText("health_effect"),
                health.SourceKey.Category == "MODIFIER" ? RText("terminal") : RName(health.SourceKey), health.MaxHpDelta, health.HealRequested)));
        foreach (var input in view.CurrentNodeInputs)
        {
            foreach (var health in input.EventHealthConsequences)
                node.AddChild(WLabel(string.Format(RText("health_result"), health.HpDelta, health.MaxHpDelta)));
            foreach (var health in input.HealthConsequences)
                node.AddChild(WLabel(string.Format(RText("max_hp_result"), health.MaxHpDelta)));
        }
        RenderRouteHistory(view);
        RenderRouteContinuation();
        RenderRouteMap(act, view);
    }
    private void RenderRouteRestart(Node parent)
    {
        RButton(parent, RText("new_run"), () =>
        {
            var request = LastRequest!;
            var unlocks = LobbyUnlockReadout.Copy(_routePredictionUnlocks!);
            string analysisKey = _routeAnalysisKey;
            var dialog = new ConfirmationDialog { DialogText = RText("new_run_confirm"), Title = Text("predictor.route") };
            AddChild(dialog);
            dialog.Confirmed += () =>
            {
                dialog.QueueFree();
                if (!IsInsideTree() || !IsVisibleInTree() || !_routeSubview || _routeAnalysisKey != analysisKey) return;
                RouteAction(() =>
                {
                    var fresh = PredictorSeedCapture.CreateFromPrediction(request, unlocks);
                    _routeRuntimeKey = fresh.Context.Seed + "/" + request.Character.CharacterKey.Serialized + "/" + request.Ascension;
                    _routeRuntime = fresh; _routeEdits.Clear(); _routeEditingCombat = false;
                });
            };
            dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered();
        });

    }
    private void RoutePopup(string title, Action<VBoxContainer, AcceptDialog> populate)
    {
        var dialog = new AcceptDialog { Title = title };
        AddChild(dialog);
        var scroll = WScroll(); scroll.CustomMinimumSize = new(540, 300);
        dialog.AddChild(scroll);
        var body = WColumn(); scroll.AddChild(body);
        populate(body, dialog);
        dialog.Confirmed += () => dialog.QueueFree();
        dialog.Canceled += () => dialog.QueueFree();
        dialog.PopupCentered();
    }
    private TextureRect RouteObjectIcon(ModelKey key, GameContentKind kind, int size = 32)
    {
        var icon = new TextureRect { Texture = _icons.Resolve(key, kind, IconVariant.Small).Texture,
            CustomMinimumSize = new(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        BindWorkbenchTooltip(icon, key, kind, RName(key));
        return icon;
    }
    private void RenderRouteInventory(PredictorRuntimeView view)
    {
        var row = RFlow(_wbCenter); row.Name = "RouteResourceBar";
        row.AddThemeConstantOverride("h_separation", 16);
        var health = _palette.Label(RText("health_untracked"), 18);
        health.TooltipText = RText("health_untracked_hint"); row.AddChild(health);
        var gold = RButton(row, RText("gold") + "  " + view.Inventory.GoldBudget, () =>
        {
            if (view.Request is not { } request) return;
            RoutePopup(RText("correct_gold"), (body, popup) =>
            {
                var amount = new SpinBox { MinValue = 0, MaxValue = int.MaxValue,
                    Value = view.Inventory.GoldBudget, CustomMinimumSize = new(140, 36) };
                body.AddChild(amount);
                RButton(body, RText("correct_gold"), () => { popup.QueueFree(); RouteSubmit(request, new CorrectGoldBalance((int)amount.Value)); });
            });
        }, view.Request != null);
        gold.TooltipText = RText("correct_gold");
        foreach (var potion in view.Potions)
        {
            var button = RButton(row, potion.Potion is null ? "—" : "", () =>
            {
                if (potion.Potion is not { } held || view.Request is not { } request) return;
                if (potion.CanDiscard && !potion.CanUseOutsideCombat)
                {
                    RouteSubmit(request, new DiscardPotion(potion.Slot, held.Id));
                    return;
                }
                RoutePopup(RName(held.Key), (body, popup) =>
                {
                    body.AddChild(WCompactIdentity(held.Key, GameContentKind.Potion, RName(held.Key)));
                    if (potion.CanUseOutsideCombat) RButton(body, RText("use"), () =>
                    { popup.QueueFree(); RouteSubmit(request, new UseOutOfCombatPotion(potion.Slot, held.Id)); });
                    if (potion.CanDiscard) RButton(body, RText("discard"), () =>
                    { popup.QueueFree(); RouteSubmit(request, new DiscardPotion(potion.Slot, held.Id)); });
                });
            }, potion.Potion != null && (potion.CanUseOutsideCombat || potion.CanDiscard));
            button.CustomMinimumSize = new(40, 40);
            if (potion.Potion is { } held) button.Icon = _icons.Resolve(held.Key, GameContentKind.Potion, IconVariant.Small).Texture;
            button.ExpandIcon = true; button.AddThemeConstantOverride("icon_max_width", 30);
            button.TooltipText = (potion.Slot + 1) + " · " + (potion.Potion is { } p ? RName(p.Key) : RText("empty_slot"));
        }
        RButton(row, string.Format(RText("deck"), view.Inventory.Deck.Length), () =>
            RoutePopup(string.Format(RText("deck"), view.Inventory.Deck.Length), (body, _) =>
            {
                foreach (var card in view.Inventory.Deck)
                    body.AddChild(WCompactIdentity(card.Key, GameContentKind.Card, RCard(card)));
            }));
        var relics = RFlow(_wbCenter); relics.Name = "RouteRelicBar";
        foreach (var relic in view.Inventory.Relics)
        {
            var icon = RouteObjectIcon(relic.Key, GameContentKind.Relic);
            if (relic.Melted) icon.Modulate = new Color(1, 1, 1, .4f);
            relics.AddChild(icon);
        }
    }
    private void RenderRouteHistory(PredictorRuntimeView view)
    {
        _wbCenter.AddChild(WLabel(RText("history"), true));
        var scroll = new ScrollContainer { CustomMinimumSize = new(0, 80), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _wbCenter.AddChild(scroll); var row = new HBoxContainer(); scroll.AddChild(row);
        for (int i = 0; i < view.History.Length; i++)
        {
            int entry = i;
            var checkpoint = _routeRuntime!.Checkpoints[i + 1];
            var visit = checkpoint.State.Visits.LastOrDefault();
            string label = visit is null ? (i + 1).ToString() : visit.TotalFloor.ToString();
            var button = RButton(row, label, () => RouteAction(() => { _routeRuntime!.Rewind(entry); _routeEdits.Clear(); }));
            button.CustomMinimumSize = new(60, 48);
            button.ExpandIcon = true; button.AddThemeConstantOverride("icon_max_width", 28);
            button.TooltipText = RText("rewind");
            if (visit != null)
            {
                if (visit.Room is PredictorRoomKind.Opening or PredictorRoomKind.Ancient)
                    button.Icon = _icons.Resolve(visit.Model ?? _routeRuntime.Working.Acts[visit.Act - 1].Ancient,
                        GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon).Texture;
                else button.Icon = ResourceLoader.Load<Texture2D>(MegaCrit.Sts2.Core.Helpers.ImageHelper.GetRoomIconPath(
                    MegaCrit.Sts2.Core.Map.MapPointType.Unknown,
                    Enum.Parse<MegaCrit.Sts2.Core.Rooms.RoomType>(visit.Room.ToString()), null));
            }
        }
        foreach (var result in view.CompletedChildRewards.Concat(view.Rewards.Select(r => r.Reward)).Where(r => r.Status == PredictorRewardStatus.Taken))
            _wbCenter.AddChild(WLabel(RText("taken") + " · " + (result.Kind == PredictorRewardKind.Gold ? RGold(result) :
                result.Key.IsValid ? RName(result.Key) : RText("reward." + result.Kind))));
    }
    private void RenderRouteMap(int act, PredictorRuntimeView view)
    {
        ShowMapRail(true); _wbRight.CustomMinimumSize = new(264, 0);
        var scroll = WScroll(); _wbRight.AddChild(scroll);
        var rail = WColumn(); scroll.AddChild(rail);
        var map = _routeRuntime!.RouteMap(act);
        rail.AddChild(WLabel(RText("draw_route")));
        var path = _routeEdits.GetValueOrDefault(act);
        if (path.IsDefault) path = view.Routes.FirstOrDefault(r => r.Act == act)?.Points ?? [];
        var state = _routeRuntime.Working.Acts[act - 1];
        var actModel = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.ActModel>(
            new MegaCrit.Sts2.Core.Models.ModelId(state.Key.Category, state.Key.Entry));
        var panel = new PanelContainer { CustomMinimumSize = new(0, 420) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = actModel.MapBgColor,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        rail.AddChild(panel);
        var display = new VerticalMapView { Name = "RuntimeRouteMap" };
        display.SetBackgroundColor(actModel.MapBgColor); panel.AddChild(display);
        display.Bind(map, state.Bosses.Select(k => _icons.Resolve(k, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon).Texture).ToArray(),
            _icons.Resolve(state.Ancient, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon).Texture, _uiText!);
        display.SetRoute(path, !_routeEdits.ContainsKey(act));
        display.SetCurrentPosition(view.Visits.LastOrDefault(v => v.Act == act)?.Point);
        var rooms = _routeRuntime.PreviewRouteRoomsUnderCurrentInventory(act, path);
        display.SetRouteRooms(rooms.Select(r => (r.Point,
            Enum.Parse<MegaCrit.Sts2.Core.Rooms.RoomType>(r.Room.ToString()), r.Preview)));
        if (rooms.Any(r => r.Preview)) rail.AddChild(WLabel(RText("unknown_preview")));
        display.StrokeFinished += stroke => { _routeEdits[act] = new MapRouteSet(map).Match(stroke).ToImmutableArray(); RenderWorkbench(); };
        if (!path.IsEmpty)
        {
            var chosen = path;
            RButton(rail, RText("commit_route"), () => RouteAction(() => { _routeRuntime.SetRoute(act, _routeEdits.TryGetValue(act, out var edited) ? edited : chosen); _routeEdits.Remove(act); }));
            var details = _palette.Button("▸ " + RText("refine_route"));
            details.ToggleMode = true; details.Flat = true;
            details.Alignment = HorizontalAlignment.Left;
            rail.AddChild(details);
            var edit = WColumn(); edit.Visible = false; rail.AddChild(edit);
            details.Toggled += value =>
            {
                edit.Visible = value;
                details.Text = (value ? "▾ " : "▸ ") + RText("refine_route");
            };
            foreach (var point in path)
            {
                int index = path.IndexOf(point); var options = map.Points.Where(p => p.Position.Row == point.Row).ToArray();
                var picker = new OptionButton { FitToLongestItem = false, ClipText = true }; edit.AddChild(picker);
                for (int j = 0; j < options.Length; j++) picker.AddItem($"{point.Row} · {options[j].Position.Column + 1} · " + Text("ui1.map.type." + options[j].Type));
                picker.Select(Array.FindIndex(options, p => p.Position == point));
                picker.ItemSelected += j => { _routeEdits[act] = (_routeEdits.GetValueOrDefault(act).IsDefault ? chosen : _routeEdits[act]).SetItem(index, options[j].Position); };
            }
        }
        if (view.RouteActsNeedingReplan.Contains(act)) rail.AddChild(WLabel(RText("route_needed")));
    }
}
