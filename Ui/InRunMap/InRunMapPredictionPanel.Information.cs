using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunMap;

[Flags]
internal enum InRunMapOverviewKind { Encounters = 1, Relics = 2, Cards = 4, Potions = 8, Events = 16 }

internal sealed partial class InRunMapPredictionPanel
{
    // Presentation whitelist only; neither forecast computation nor full node
    // details use this mask. Card membership is a separate display preference.
    internal InRunMapOverviewKind OverviewKinds { get; set; } = InRunMapOverviewKind.Encounters |
        InRunMapOverviewKind.Relics | InRunMapOverviewKind.Cards | InRunMapOverviewKind.Potions | InRunMapOverviewKind.Events;
    private ModRuntimeSnapshot _runtime = null!;
    private string _language = "zh";
    private InRunMapPredictionSession _prediction = new();
    private InRunMapNodePresenter _presenter = null!;
    private InRunMapNodeDetails _details = null!;
    private IGameContentNameResolver _names = null!;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("in-run-map-information");
    private Control _overview = null!;
    private Label _forecastStatus = null!;
    private readonly Dictionary<MapPosition, Control> _annotations = [];
    private readonly Dictionary<MapPosition, InRunMapNodeDetailData> _nodeDetails = [];
    private readonly HashSet<MapPosition> _nodeInformationFailures = [];
    private bool _predictionQueued, _lastDepartureBoundary;
    private string? _predictionFault;
    private bool _diagnosticsOnly;
    private string? _hoverCandidate;
    private ulong _hoverCandidateSince;

    private void RefreshLocalization() => ApplyLanguage(UiLanguagePreferences.LanguageCode);

    private void ApplyLanguage(string language)
    {
        if (language == _language) return;
        _language = language;
        _text = JsonUiTextProvider.CreatePredictorUi13(language);
        _names = RuntimeGameContentNameResolver.Create(language);
        UpdateCollectionPicker();
        if (_presenter == null) return;
        _presenter.RebindLocalization(_names, _text);
        _details.RebindLocalization(_names);
        UpdateStatusText();
        // Reproject existing observations only. Translation must not capture
        // the live run, recompute the forecast or clear local route/edit state.
        if (_predictionFault != null)
            _forecastStatus.Text = _text.Get(_predictionFault);
        else if (_featureEnabled && PredictionEnabled && _screen.IsOpen) RenderInformation();
    }

    private void InitializeInformation()
    {
        _names = RuntimeGameContentNameResolver.Create(_language);
        _presenter = new(_prediction, _names, _text);
        _overview = new Control { Name = "RouteObservations", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _screen.AddChild(_overview); _overview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _details = new(_icons, _names); _screen.AddChild(_details);
        _details.ChoiceRequested += (node, choice) => DeferInformationEdit(() => _presenter.ApplyChoice(node, choice));
        _details.FieldChanged += (node, field, value) => DeferInformationEdit(() => _presenter.ApplyField(node, field, value));
        _details.EditorRequested += (node, action) =>
        {
            if (!_featureEnabled || !PredictionEnabled || !_screen.IsOpen) return;
            _diagnosticsOnly = action == "diagnostics";
            if (FindObservation(node) is { } location) ShowInformationEditor(location, _diagnosticsOnly);
        };
        _forecastStatus = new Label { Name = "ForecastStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _forecastStatus.AddThemeFontSizeOverride("font_size", 15); AddChild(_forecastStatus);
    }

    private (int ActIndex, MapPosition Point)? FindObservation(string nodeId)
    {
        foreach (var pair in _prediction.Nodes) if (pair.Value.Position.NodeId == nodeId) return pair.Key;
        foreach (var pair in _prediction.TerminalBoundaries) if (pair.Value.TargetPosition.NodeId == nodeId) return pair.Key;
        foreach (var pair in _nodeDetails) if (pair.Value.NodeId == nodeId) return (_actIndex, pair.Key);
        return null;
    }
    private void ResetInformation()
    {
        if (_details == null) return;
        _details.CloseHover(); _details.CloseEditor(); _prediction = new();
        _presenter = new(_prediction, _names, _text); _predictionFault = null;
        ClearAnnotations();
    }
    private void QueueInformationRefresh()
    {
        if (!_featureEnabled || _details == null || _predictionQueued || !IsInsideTree()) return;
        _predictionQueued = true;
        int featureRevision = _featureRevision;
        Callable.From(() =>
        {
            if (featureRevision != _featureRevision) return;
            _predictionQueued = false;
            if (!_featureEnabled || !IsInsideTree()) return;
            if (!_screen.IsOpen)
            {
                _overview.Hide(); _details.CloseHover(); _details.CloseEditor(); _forecastStatus.Hide(); return;
            }
            try
            {
                    _prediction.Update(_run, _runtime.Detection.NormalizedVersion,
                        _screen.IsTravelEnabled && !_screen.IsTraveling, _drafts);
                    _prediction.ForgetRedrawUndo();
                    _predictionFault = null;
                    if (PredictionEnabled) RenderInformation();
                    else { _overview.Hide(); _details.CloseHover(); _details.CloseEditor(); _forecastStatus.Hide(); }
            }
            catch (Exception ex) { InformationFailure(ex); }
        }).CallDeferred();
    }
    private void DeferInformationEdit(Func<bool> edit)
    {
        int featureRevision = _featureRevision;
        Callable.From(() =>
        {
            if (featureRevision != _featureRevision || !_featureEnabled || !IsInsideTree() || !PredictionEnabled || !_screen.IsOpen) return;
            try { if (edit()) { _predictionFault = null; RenderInformation(); } }
            catch (Exception ex) { InformationFailure(ex); }
        }).CallDeferred();
    }
    private void InformationFailure(Exception ex)
    {
        RuntimeLog.WarnException("inRunRouteInformationFailed=true", ex);
        _predictionFault = "predictor.map.information_failed";
        ClearAnnotations(); _details.CloseHover(); _details.CloseEditor();
        _forecastStatus.Text = _text.Get(_predictionFault);
        _forecastStatus.Show();
        AlignStatus();
    }
    private void ClearAnnotations()
    {
        foreach (var node in _annotations.Values) { _overview.RemoveChild(node); node.QueueFree(); }
        _annotations.Clear(); _nodeDetails.Clear(); _nodeInformationFailures.Clear();
    }
    private void RenderInformation()
    {
        if (!_featureEnabled) return;
        string? hover = _details.HoverNodeId, editor = _details.EditorNodeId;
        ClearAnnotations();
        foreach (var pair in _prediction.Nodes.Where(p => p.Key.ActIndex == _actIndex))
        {
            PanelContainer? surface = null;
            try
            {
                if (_presenter.Detail(pair.Key) is { } detail) _nodeDetails[pair.Key.Point] = detail;
                var contents = Overview(pair.Value).Distinct().ToArray();
                string? prerequisite = _nodeDetails.GetValueOrDefault(pair.Key.Point)?.PrerequisiteSummary;
                if (contents.Length == 0 && prerequisite == null) continue;
                surface = new PanelContainer { Name = $"Observation_{pair.Key.Point.Column}_{pair.Key.Point.Row}",
                    MouseFilter = MouseFilterEnum.Ignore };
                surface.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new(.035f, .05f, .07f, .62f),
                    ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 3, ContentMarginBottom = 3,
                    BorderColor = new(.60f, .71f, .80f, .3f), BorderWidthLeft = 1,
                    CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
                var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; rows.AddThemeConstantOverride("separation", 2);
                surface.AddChild(rows);
                if (prerequisite != null)
                {
                    var need = new Label { Text = prerequisite, MouseFilter = MouseFilterEnum.Ignore,
                        AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(245, 0) };
                    MainPanelTheme.ApplyFont(need); need.AddThemeFontSizeOverride("font_size", 14);
                    need.AddThemeColorOverride("font_color", new Color("dbcaa5")); rows.AddChild(need);
                }
                foreach (var item in contents)
                {
                    var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                    row.AddThemeConstantOverride("separation", 5); rows.AddChild(row);
                    if (item.Key.IsValid && _icons.Resolve(item.Key, item.Kind, IconVariant.Small).Texture is { } texture)
                        row.AddChild(new TextureRect { Texture = texture, CustomMinimumSize = new(21, 21),
                            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
                    string name = item.DisplayName ?? _names.Resolve(item.Key, item.Kind) +
                        (item.UpgradeLevel > 0 ? "+" + (item.UpgradeLevel > 1 ? item.UpgradeLevel.ToString() : "") : "");
                    var label = new Label { Text = name, MouseFilter = MouseFilterEnum.Ignore,
                        ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                        CustomMinimumSize = new(Math.Min(245, MainPanelTheme.Regular.GetStringSize(name, fontSize: 16).X), 21),
                        SizeFlagsHorizontal = SizeFlags.ExpandFill };
                    MainPanelTheme.ApplyFont(label); label.AddThemeFontSizeOverride("font_size", 16);
                    label.AddThemeColorOverride("font_color", new Color("e0e4e9")); row.AddChild(label);
                }
                _overview.AddChild(surface); _annotations[pair.Key.Point] = surface;
            }
            catch (Exception ex)
            {
                if (surface != null && !_annotations.ContainsKey(pair.Key.Point)) surface.QueueFree();
                NodeInformationFailure(pair.Key, pair.Value.Position.NodeId, "node-observation", ex);
            }
        }
        foreach (var pair in _prediction.TerminalBoundaries.Where(p => p.Key.ActIndex == _actIndex))
        {
            if (_nodeInformationFailures.Contains(pair.Key.Point)) continue;
            PanelContainer? surface = null;
            try
            {
                if (!_nodeDetails.ContainsKey(pair.Key.Point) && _presenter.Detail(pair.Key) is { } detail)
                    _nodeDetails[pair.Key.Point] = detail;
                if (_annotations.ContainsKey(pair.Key.Point)) continue;
                surface = new PanelContainer { Name = $"BlockedEntry_{pair.Key.Point.Column}_{pair.Key.Point.Row}",
                    MouseFilter = MouseFilterEnum.Ignore };
                var label = new Label { Text = _text.Get("predictor.map.diagnostics.entry_blocked"),
                    MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new(180, 22),
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
                label.AddThemeFontSizeOverride("font_size", 16); surface.AddChild(label);
                _overview.AddChild(surface); _annotations[pair.Key.Point] = surface;
            }
            catch (Exception ex)
            {
                if (surface != null && !_annotations.ContainsKey(pair.Key.Point)) surface.QueueFree();
                NodeInformationFailure(pair.Key, pair.Value.TargetPosition.NodeId, "terminal-observation", ex);
            }
        }
        if (_drafts.TryGetValue(_actIndex, out var drawn))
            foreach (var pair in _presenter.DeferredDetails(_actIndex, drawn.ReadyPrefix))
            {
                if (_nodeInformationFailures.Contains(pair.Key.Point)) continue;
                _nodeDetails[pair.Key.Point] = pair.Value;
                var surface = new PanelContainer { Name = $"DeferredObservation_{pair.Key.Point.Column}_{pair.Key.Point.Row}",
                    MouseFilter = MouseFilterEnum.Ignore };
                var label = new Label { Text = pair.Value.PrerequisiteSummary,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(230, 0),
                    MouseFilter = MouseFilterEnum.Ignore };
                MainPanelTheme.ApplyFont(label); label.AddThemeFontSizeOverride("font_size", 14);
                label.AddThemeColorOverride("font_color", new Color("dbcaa5")); surface.AddChild(label);
                _overview.AddChild(surface); _annotations[pair.Key.Point] = surface;
            }
        UpdateStatusText();
        _overview.Visible = PredictionEnabled && _screen.IsOpen;
        if (hover != null && FindObservation(hover) is { } hovered && hovered.ActIndex == _actIndex &&
            _nodeDetails.TryGetValue(hovered.Point, out var hoverData) && _visuals.TryGetValue(hovered.Point, out var visual))
            ShowInformationNode(hovered, hoverData, visual);
        else _details.CloseHover();
        if (editor != null)
        {
            if (FindObservation(editor) is { } edited) ShowInformationEditor(edited, _diagnosticsOnly);
            else _details.CloseEditor();
        }
        UpdateInformationPositions();
    }
    private static void IgnoreInput(Node node)
    {
        if (node is Control control) { control.MouseFilter = MouseFilterEnum.Ignore; control.TooltipText = ""; }
        foreach (var child in node.GetChildren()) IgnoreInput(child);
    }
    private IEnumerable<InRunMapDetailObject> Overview(PredictorRouteNodeKnowledge node)
    {
        bool Allows(InRunMapOverviewKind kind) => (OverviewKinds & kind) != 0;
        var cards = Allows(InRunMapOverviewKind.Cards)
            ? ResolveOverviewCardCollection()?.Cards.ToHashSet(ModelKeyComparer.Instance) : null;
        bool Tracks(PredictorCard card) => cards?.Contains(card.Key) == true;
        if (Allows(InRunMapOverviewKind.Encounters) && node.Room is PredictorRoomKind.Monster or PredictorRoomKind.Elite or PredictorRoomKind.Boss
            && node.Model is { } encounter) yield return new(default, GameContentKind.Encounter, DisplayName: _names.Resolve(encounter, GameContentKind.Encounter));
        foreach (var observation in node.Rewards)
        {
            // CardOptions already proves identity/level. Unresolved local
            // enchantments do not make that card's collection membership unknown.
            if (!observation.IsKnown && Allows(InRunMapOverviewKind.Cards))
                foreach (var card in observation.CardOptions.Select(option => option.Value).Where(Tracks))
                    yield return new(card.Key, GameContentKind.Card, card.UpgradeLevel);
            if (!observation.IsKnown || observation.Value is not { } reward) continue;
            if (Allows(InRunMapOverviewKind.Relics) && reward.Kind == PredictorRewardKind.Relic)
                yield return new(reward.Key, GameContentKind.Relic);
            if (Allows(InRunMapOverviewKind.Potions) && reward.Kind == PredictorRewardKind.Potion)
                yield return new(reward.Key, GameContentKind.Potion);
            if (Allows(InRunMapOverviewKind.Cards) && reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
                foreach (var card in reward.Cards.Where(Tracks)) yield return new(card.Key, GameContentKind.Card, card.UpgradeLevel);
        }
        if (Allows(InRunMapOverviewKind.Relics) && node.Treasure is { OfferedRelic: { } relic } treasure && treasure.RelicPendingCauses.IsEmpty)
            yield return new(relic, GameContentKind.Relic);
        if (node.Shop is { } shop)
            foreach (var item in shop.Offers)
            {
                if (!item.IsKnown || item.Value is not { } offer) continue;
                if (Allows(InRunMapOverviewKind.Relics) && offer.Kind == PredictorShopEntryKind.Relic && offer.Key is { } key)
                    yield return new(key, GameContentKind.Relic);
                if (Allows(InRunMapOverviewKind.Cards) && offer.Card is { } card && Tracks(card))
                    yield return new(card.Key, GameContentKind.Card, card.UpgradeLevel);
            }
        if (Allows(InRunMapOverviewKind.Events) && node.Event is { } eventNode)
        {
            if (eventNode.Event is { } known) yield return new(known, GameContentKind.Event);
            else if (eventNode.Occurrence is { } occurrence)
                foreach (var candidate in occurrence.Candidates.Take(3)) yield return new(candidate.Event, GameContentKind.Event);
        }
        // The applied prefix owns acquisitions from every accepted event stage,
        // including current-node observations without a hypothetical future plan.
        // Overview is a set of icons: its caller deduplicates reward overlap.
        if (Allows(InRunMapOverviewKind.Relics) &&
            node.Event is { AuthoredActionsNotApplied: false, AppliedResult: { } applied })
            foreach (var obtained in applied.ObtainedRelics) yield return new(obtained.Key, GameContentKind.Relic);
        if (Allows(InRunMapOverviewKind.Cards))
        {
            if (node.Event is { AuthoredActionsNotApplied: false, AppliedResult: { } adopted })
                foreach (var card in adopted.GeneratedCards.Where(card => card.IsKnown).Select(card => card.Value).Where(Tracks))
                    yield return new(card.Key, GameContentKind.Card, card.UpgradeLevel);
            if (node.Ancient is { } ancient)
            {
                // Native optional cards/bundles are offers. Existing deck
                // targets (such as Claws) are not new-card opportunities.
                var offered = (ancient.OptionalCards?.Choice.Cards ?? [])
                    .Concat(ancient.BundleChoice?.Choice.Bundles.SelectMany(bundle => bundle) ?? []);
                foreach (var card in (ancient.Outcome?.AddedCards ?? []).Concat(offered).Where(Tracks))
                    yield return new(card.Key, GameContentKind.Card, card.UpgradeLevel);
            }
        }
    }
    // Native focus/pulse animation must not make an open information panel bob.
    private Rect2 GlobalRegion(PointVisual visual) => _map.GetGlobalTransform() * Region(visual);
    private void UpdateInformationPositions()
    {
        if (_details == null) return;
        bool active = _featureEnabled && PredictionEnabled && _screen.IsOpen && IsVisibleInTree();
        _overview.Visible = active;
        if (!active) { _details.CloseHover(); _details.CloseEditor(); return; }
        foreach (var (point, annotation) in _annotations)
        {
            if (!_visuals.TryGetValue(point, out var visual)) { annotation.Hide(); continue; }
            var region = _screen.GetGlobalTransform().AffineInverse() * GlobalRegion(visual);
            var center = region.GetCenter();
            annotation.Visible = center.Y > 90 && center.Y < _screen.Size.Y - 35 &&
                !(_details.HoverNodeId is { } shown && _nodeDetails.TryGetValue(point, out var detail) && detail.NodeId == shown);
            float x = center.X < _screen.Size.X * .52f ? region.End.X + 18 : region.Position.X - annotation.Size.X - 18;
            annotation.Position = new(Math.Clamp(x, 8, Math.Max(8, _screen.Size.X - annotation.Size.X - 8)),
                Math.Clamp(center.Y - annotation.Size.Y * .5f, 102, Math.Max(102, _screen.Size.Y - annotation.Size.Y - 8)));
        }
        bool drawing = _nativeDrawing != null || _map.GetNode<NMapDrawings>("Drawings").GetLocalDrawingMode(useOverride: false) != DrawingMode.None;
        _details.SetDrawingSuppressed(drawing);
        if (_details.HoverNodeId is { } nodeId && FindObservation(nodeId) is { } location && _visuals.TryGetValue(location.Point, out var hovered))
            _details.UpdateAnchor(GlobalRegion(hovered), _screen.GetGlobalRect());
        if (drawing || _details.EditorOpen || _predictionFault != null) return;
        var mouse = GetGlobalMousePosition();
        if (_details.ContainsPointer(mouse) || !IsMapArea(mouse)) { _hoverCandidate = null; return; }
        foreach (var (point, visual) in _visuals)
            if (_nodeDetails.TryGetValue(point, out var data) && GlobalRegion(visual).HasPoint(mouse) && _details.CanOpenNode(data.NodeId))
            {
                if (_details.HoverOpen)
                {
                    if (_hoverCandidate != data.NodeId) { _hoverCandidate = data.NodeId; _hoverCandidateSince = Time.GetTicksMsec(); }
                    if (Time.GetTicksMsec() - _hoverCandidateSince < 450) return;
                }
                ShowInformationNode((_actIndex, point), data, visual);
                _hoverCandidate = null; return;
            }
        _hoverCandidate = null;
    }
    private void CloseInformation()
    {
        _collectionPicker?.GetPopup().Hide();
        if (_details == null) return;
        _details.CloseHover(); _details.CloseEditor(); _overview.Hide(); _forecastStatus.Hide();
    }
    private void DisposeInformation()
    {
        if (_details != null && IsInstanceValid(_details)) _details.QueueFree();
        if (_overview != null && IsInstanceValid(_overview)) _overview.QueueFree();
    }
}
