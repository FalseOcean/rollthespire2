using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Editors;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private int? _pandoraCountOverride;
    private readonly HashSet<(int Act, ModelKey Relic)> _expandedAncientTransforms = [];

    private Button AncientTransformToggle(int act, ModelKey relic, string name, VBoxContainer results, Label openingNote)
    {
        var panel = WColumn();
        results.AddChild(panel);
        var button = _palette.Button(name);
        button.Name = "AncientTransformToggle_" + relic.Entry;
        button.ToggleMode = true;
        button.Icon = _icons.Resolve(relic, GameContentKind.Relic, IconVariant.Small).Texture;
        button.AddThemeConstantOverride("icon_max_width", 32);
        button.AddThemeFontSizeOverride("font_size", 15);
        button.CustomMinimumSize = new Vector2(0, 42);
        BindWorkbenchTooltip(button, relic, GameContentKind.Relic, name);

        void ShowPanel(bool expanded)
        {
            button.Text = name + (expanded ? " ▾" : " ▸");
            _palette.SetActive(button, expanded, horizontalPadding: 6);
            panel.Visible = expanded;
            results.Visible = expanded || results.GetChildren().OfType<VBoxContainer>().Any(p => p.Visible);
            if (!expanded || panel.GetChildCount() > 0) return;
            // Use the displayed opening even when this act is the first page visited.
            if (_preferredOpeningChoiceSlotIndex is null && string.IsNullOrEmpty(_preferredOpeningRouteId))
                ReconcileOpeningSelection();
            RenderAncientTransformOption(panel, openingNote, relic, name);
        }
        button.ButtonPressed = _expandedAncientTransforms.Contains((act, relic));
        ShowPanel(button.ButtonPressed);
        button.Toggled += expanded =>
        {
            if (expanded) _expandedAncientTransforms.Add((act, relic));
            else _expandedAncientTransforms.Remove((act, relic));
            ShowPanel(expanded);
        };
        return button;
    }

    private void RenderAncientTransformOption(VBoxContainer results, Label openingNote, ModelKey relic, string name)
    {
        var block = WCard(results, _uiText!.Format("predictor.ancient.transform.title", name));
        if (!TryGetProjectionRoot(out ulong root)) { WEmpty(block); return; }
        var request = LastRequest!;
        var facts = LastFacts!;
        bool pandora = relic.Entry == "PANDORAS_BOX";
        SeedAncientTransformResult? Project()
        {
            try
            {
                return SeedAncientTransformPredictor.Project(root, request, facts, relic,
                    _preferredOpeningChoiceSlotIndex, _preferredOpeningRouteId, pandora ? _pandoraCountOverride : null);
            }
            catch (Exception ex)
            {
                RuntimeLog.WarnException($"seedAncientTransform={relic.Entry};seed={facts.CanonicalSeed};slot={request.PlayerSlotIndex}", ex);
                return null;
            }
        }
        var result = Project();
        if (result is null) { WEmpty(block); return; }
        bool openingKnown = result.Status is not (SeedAncientTransformStatus.UnsupportedVersion or
            SeedAncientTransformStatus.UnsupportedParty or SeedAncientTransformStatus.OpeningUnavailable or
            SeedAncientTransformStatus.ContextUnavailable);
        if (openingKnown && !openingNote.Visible &&
            request.Authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113)
        {
            var choice = _viewModel!.NeowChoices.FirstOrDefault(c => c.SlotIndex == _preferredOpeningChoiceSlotIndex);
            var route = choice?.OpeningRoutes.FirstOrDefault(r => r.RouteId == _preferredOpeningRouteId);
            string opening = route is { AcquisitionOrder.Count: > 0 } && choice?.BonesOutcome is not null
                ? string.Join(" → ", route.AcquisitionOrder.Select(key => _contentNames!.Resolve(key, GameContentKind.Relic)))
                : choice?.DisplayName ?? Text("predictor.ancient.transform.start");
            openingNote.Text = _uiText.Format("predictor.ancient.transform.premise", opening);
            openingNote.Show();
        }
        var body = WColumn();
        if (pandora && result.PreviewCount.HasValue)
        {
            var controls = new HFlowContainer();
            controls.AddThemeConstantOverride("h_separation", 10);
            block.AddChild(controls);
            controls.AddChild(WNote(Text("predictor.ancient.transform.preview_count")));
            var quantity = new MapQuantityInput(_palette, result.PreviewCount, true)
                { Name = "PandoraCount" };
            controls.AddChild(quantity);
            var restore = WAction("predictor.ancient.transform.reset_count");
            restore.Name = "PandoraAuto";
            restore.Disabled = !_pandoraCountOverride.HasValue;
            controls.AddChild(restore);
            quantity.ValueChanged += count =>
            {
                _pandoraCountOverride = count;
                restore.Disabled = false;
                ShowAncientTransformCards(body, Project());
            };
            restore.Pressed += () =>
            {
                _pandoraCountOverride = null;
                RenderWorkbench();
            };
        }
        block.AddChild(body);
        ShowAncientTransformCards(body, result);
    }

    private void ShowAncientTransformCards(VBoxContainer body, SeedAncientTransformResult? result)
    {
        WClear(body);
        if (result is null) { WEmpty(body); return; }
        if (result.Status != SeedAncientTransformStatus.Ready)
        {
            body.AddChild(WNote(Text("predictor.ancient.transform." + result.Status)));
            return;
        }
        if (result.Cards.Count == 0)
        {
            body.AddChild(WNote(Text("predictor.ancient.transform.empty")));
            return;
        }
        var grid = WResponsiveGrid(body, "AncientTransformCards", 205, 3);
        int shown = 0;
        void AddBatch()
        {
            int end = Math.Min(shown + 12, result.Cards.Count);
            while (shown < end)
            {
                var card = result.Cards[shown++];
                string label = _contentNames!.Resolve(card.CardKey, GameContentKind.Card) + (card.Upgraded ? "+" : "");
                grid.AddChild(WObject(card.CardKey, GameContentKind.Card, $"{shown}. {label}", 36, IconVariant.CardPickerLarge));
            }
        }
        AddBatch();
        if (shown >= result.Cards.Count) return;
        var more = WAction("predictor.map.more");
        body.AddChild(more);
        more.Pressed += () => { AddBatch(); more.Visible = shown < result.Cards.Count; };
    }
}
