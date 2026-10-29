using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Settings;
using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

/// <summary>Main-thread view of an already detached operation projection.</summary>
internal sealed partial class NeowOperationOverlay : Control
{
    private static IUiTextProvider UiText => JsonUiTextProvider.CreateUi13(UiLanguagePreferences.LanguageCode);
    private OperationPreview? _preview;
    private Control? _summary;
    private readonly List<(MarginContainer Shell, VBoxContainer Content)> _summaryPanels = [];
    private VBoxContainer? _summaryContent;
    private Control? _cardsArea;
    private Rect2 _summaryBounds, _cardsBounds;
    private Rect2[]? _potionGroupRegions;
    private float _summaryNaturalWidth;
    internal bool ExternallyFramed { get; init; }
    private readonly List<Control> _visibleCardRegions = [];

    public Vector2 SummaryDesiredSize
    {
        get
        {
            if (_summaryContent == null) return Vector2.Zero;
            float screenWidth = GetViewportRect().Size.X;
            if (screenWidth <= 0) screenWidth = 1920;
            // Native NHoverTipSet uses 360px. Grow only for content that needs
            // it; compact results should not inherit the old fixed 460/620px.
            float width = Math.Clamp(_summaryNaturalWidth + 42, Math.Min(360, screenWidth - 36),
                Math.Min(IsComplex ? screenWidth * .82f : 620, screenWidth - 36));
            return new(Math.Max(1, width), Mathf.Ceil(_summaryPanels.Sum(panel => panel.Content.GetCombinedMinimumSize().Y + 33) +
                Math.Max(0, _summaryPanels.Count - 1) * 12));
        }
    }
    public Vector2 CardsDesiredSize => CardsSizeForWidth(float.PositiveInfinity);
    public bool IsBones => _preview?.Source.Entry == "NEOWS_BONES";
    public bool IsComplex => _preview != null && OperationDisplayPolicy.Resolve(_preview).Text == OperationTextMode.Complex;

    public IReadOnlyList<Rect2> VisibleRegions
    {
        get
        {
            if (!IsVisibleInTree() || Modulate.A <= 0) return [];
            var regions = new List<Rect2>();
            regions.AddRange(_summaryPanels.Where(panel => panel.Shell.IsVisibleInTree()).Select(panel => panel.Shell.GetGlobalRect()));
            regions.AddRange(_visibleCardRegions.Where(c => c.IsVisibleInTree()).Select(c => c.GetGlobalRect()));
            return regions;
        }
    }

    public NeowOperationOverlay()
    {
        Name = "Rt2NeowOperationOverlay";
        MouseFilter = MouseFilterEnum.Ignore; FocusMode = FocusModeEnum.None; ZIndex = 100;
    }

    public void SetPreview(OperationPreview preview) { _preview = preview; Rebuild(); }

    internal static bool HasVisibleContent(OperationPreview preview)
    {
        if (OperationDisplayPolicy.HiddenSource(preview.Source)) return false;
        if (!string.IsNullOrWhiteSpace(preview.Heading) || DisplayNotices(preview).Any(n => !string.IsNullOrWhiteSpace(n)) || !preview.Faults.IsDefaultOrEmpty ||
            preview.Branches.Any(b => b.Faulted) || preview.PendingReasons.Any(IsUnexpandedRemainder) ||
            preview.Branches.Any(b => b.PendingReasons.Any(IsUnexpandedRemainder))) return true;
        bool text = OperationDisplayPolicy.Resolve(preview).Text != OperationTextMode.None;
        if (text && (!preview.ContainedRelics.IsDefaultOrEmpty || !preview.CurseCandidates.IsDefaultOrEmpty)) return true;
        return preview.Branches.Any(branch => branch.Sections.Any(section =>
        {
            bool sectionText = OperationDisplayPolicy.Resolve(preview, section).Text != OperationTextMode.None;
            return section.EmptyReason != null || section.Rows.Any(row => !row.Models.IsDefaultOrEmpty ||
                !row.Cards.IsDefaultOrEmpty && (sectionText || row.ShowCards)) ||
                section.CardGroups.Any(group => !group.Cards.IsDefaultOrEmpty);
        }));
    }

    /// <summary>Parent-local bounds; the detached view always stays at scale one.</summary>
    public void SetLayout(Rect2 summaryBounds, Rect2 cardsBounds, Rect2[]? potionGroupRegions = null)
    {
        _potionGroupRegions = potionGroupRegions;
        Position = Vector2.Zero; Scale = Vector2.One;
        _summaryBounds = summaryBounds; _cardsBounds = cardsBounds;
        LayoutSummary(); LayoutStacks();
    }

    private void LayoutSummary()
    {
        if (_summary == null) return;
        _summary.Visible = _summaryBounds.Size.X > 0 && _summaryBounds.Size.Y > 0;
        _summary.Position = _summaryBounds.Position;
        if (!_summary.Visible) return;
        LayoutMapSummaryNames(_summaryBounds.Size.X);
        _summary.Size = new(_summaryBounds.Size.X, Math.Min(_summaryBounds.Size.Y, SummaryDesiredSize.Y));
        float natural = SummaryDesiredSize.Y;
        float available = Math.Max(1, _summary.Size.Y - Math.Max(0, _summaryPanels.Count - 1) * 12);
        float naturalPanels = Math.Max(1, natural - Math.Max(0, _summaryPanels.Count - 1) * 12);
        float y = 0;
        foreach (var panel in _summaryPanels)
        {
            float height = (panel.Content.GetCombinedMinimumSize().Y + 33) * Math.Min(1, available / naturalPanels);
            panel.Shell.Position = new(0, y); panel.Shell.Size = new(_summary.Size.X, height);
            y += height + 12;
        }
    }

    private void LayoutSummaryAfterResize()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() ||
            _summary == null || !GodotObject.IsInstanceValid(_summary)) return;
        LayoutSummary();
    }

    private void Rebuild()
    {
        _potionGroupScale = 1f;
        _potionGroupRegions = null;
        ReleaseCardDisplays();
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _nativeTiles.Clear();
        _mapSummaryNames.Clear();
        _stacks.Clear(); _iconGroups.Clear(); _visibleCardRegions.Clear(); _summaryPanels.Clear();
        _summary = null; _summaryContent = null; _cardsArea = null; _summaryNaturalWidth = 0;
        NativeCardCreateMilliseconds = 0; NativeCardReadyMilliseconds = 0; NativeCardCount = 0; NativeCardReuseCount = 0;
        if (_preview is not { } preview || !HasVisibleContent(preview)) { _cardReuse?.Clear(); return; }
        var rootStyle = OperationDisplayPolicy.Resolve(preview);
        var details = Column(7);
        if (MapCardPresentation && !string.IsNullOrWhiteSpace(preview.Heading))
        {
            var heading = WrappedText(preview.Heading);
            heading.Name = "Rt2MapOptionHeading";
            heading.AddThemeFontSizeOverride("font_size", 18);
            details.AddChild(heading);
        }
        foreach (var progress in preview.Notices.Where(notice => notice.StartsWith("RewardSacrificeProgress:", StringComparison.Ordinal)))
            details.AddChild(WrappedText(NoticeText(progress)));
        var effectDetails = new List<(ModelKey Source, VBoxContainer Content)>();
        if (rootStyle.Text != OperationTextMode.None && !preview.CurseCandidates.IsDefaultOrEmpty)
        {
            var curse = WrappedText((preview.CursesPossible ? UiText.Get("operation.possiblePrefix") : UiText.Get("operation.obtainPrefix")) +
                string.Join("／", preview.CurseCandidates.Select(CardName)));
            curse.Name = "Rt2BonesSharedCurse";
            curse.AddThemeColorOverride("font_color", new Color("cfb1d3")); details.AddChild(curse);
        }
        // Two retained Bones branches have not been proven equivalent. Their
        // full textual comparison must not imply a shared set of external cards.
        bool suppressBranchCards = IsBones && preview.Branches.Length > 1;
        for (int branchIndex = 0; branchIndex < preview.Branches.Length; branchIndex++)
        {
            var branch = preview.Branches[branchIndex];
            var block = Column(5);
            block.Name = "Rt2OperationBranch_" + branchIndex;
            string branchName = BranchLabel(preview.Source, branch.Label, branchIndex);
            if (preview.Branches.Length > 1)
            {
                var targets = branch.Sections.SelectMany(s => s.Rows).Where(r => r.Label == "EventTransformTargets")
                    .SelectMany(r => r.Cards).Select(CardName).ToArray();
                if (targets.Length > 0) branchName = JoinCaption(branchName, string.Join("／", targets));
            }
            bool branchHasText = branch.Sections.Any(s => OperationDisplayPolicy.Resolve(preview, s).Text != OperationTextMode.None);
            if (branchHasText && !branch.Order.IsDefaultOrEmpty)
                block.AddChild(WrappedText((preview.Branches.Length > 1 ? branchName + " " : "") +
                    string.Join(" → ", branch.Order.Select(NameOf)), true));
            else if (branchHasText && preview.Branches.Length > 1) block.AddChild(WrappedText(branchName, true));
            foreach (var section in DisplaySections(preview, branch))
            {
                var style = OperationDisplayPolicy.Resolve(preview, section);
                bool separateEffect = preview.Branches.Length == 1 && section.Source != preview.Source &&
                    section.Source.Entry is "WHETSTONE" or "WAR_PAINT";
                bool mapNeedsCardNames = MapCardPresentation && (MapSummaryCardNames ||
                    section.Rows.Any(row => row.Action != OperationPreviewAction.AddCard && !row.Cards.IsEmpty) ||
                    section.Rows.Any(row => row.Cards.Any(card => !MapUsesCardFace(card))) ||
                    section.CardGroups.Any(group => group.Cards.Any(card => !MapUsesCardFace(card))));
                bool text = separateEffect || style.Text != OperationTextMode.None || mapNeedsCardNames;
                var sectionBlock = block;
                if (section.EmptyReason == null && !section.Rows.Any(r => !r.Models.IsDefaultOrEmpty || !r.Cards.IsDefaultOrEmpty) &&
                    !section.CardGroups.Any(g => !g.Cards.IsDefaultOrEmpty)) continue;
                if (separateEffect)
                {
                    sectionBlock = Column(7);
                    effectDetails.Add((section.Source, sectionBlock));
                }
                string sectionName = LocalizedLabel(section.Source, section.Label);
                if (text && !separateEffect && section.Source != preview.Source) sectionBlock.AddChild(Identity(section.Source, NameOf(section.Source), 24));
                if (text && sectionName.Length > 0) sectionBlock.AddChild(WrappedText(sectionName, true));
                if (text && section.EmptyReason != null) sectionBlock.AddChild(WrappedText(LocalizedLabel(section.Source, section.EmptyReason)));
                string artCaption = preview.Branches.Length > 1 ? branchName : "";
                if (section.Source != preview.Source && section.Source.Entry != "THE_HUNT") artCaption = JoinCaption(artCaption, NameOf(section.Source));
                artCaption = JoinCaption(artCaption, sectionName);
                foreach (var original in section.Rows)
                {
                    var row = original;
                    if (IsBones && section.Source == preview.Source && row.Action == OperationPreviewAction.AddCard)
                        row = row with { Cards = row.Cards.Where(c => !preview.CurseCandidates.Any(shared =>
                            (shared with { Id = 0 }) == (c with { Id = 0 }))).ToImmutableArray() };
                    if (row.Models.IsDefaultOrEmpty && row.Cards.IsDefaultOrEmpty) continue;
                    string rowName = LocalizedLabel(section.Source, row.Label);
                    string action = (row.Possible ? UiText.Get("operation.possiblePrefix") : "") + ActionName(row.Action);
                    if (text)
                    {
                        if (rowName.Length > 0) sectionBlock.AddChild(WrappedText(rowName));
                        if (!row.Models.IsDefaultOrEmpty) AddModelRow(sectionBlock, action, row.Models,
                            row.Action == OperationPreviewAction.GeneratePotion || style.Text == OperationTextMode.CardRows);
                        if (!row.Cards.IsDefaultOrEmpty)
                        {
                            if (MapCardPresentation && row.ShowCards && !MapSummaryCardNames)
                            {
                                var compactCards = row.Cards.Where(card => !MapUsesCardFace(card)).ToArray();
                                // Changed-card results also name their targets:
                                // the upper card face can be outside the view.
                                if (row.Action != OperationPreviewAction.AddCard)
                                    sectionBlock.AddChild(WrappedText(action + " · " + string.Join("／", row.Cards.Select(CardName))));
                                else if (compactCards.Length != 0)
                                    sectionBlock.AddChild(WrappedText(action + " · " + string.Join("／", compactCards.Select(CardName))));
                            }
                            else if (style.Text == OperationTextMode.CardRows)
                                foreach (var card in row.Cards) sectionBlock.AddChild(WrappedText(action + " · " + CardName(card)));
                            else sectionBlock.AddChild(WrappedText(action + " · " + string.Join("／", row.Cards.Select(CardName))));
                        }
                        if (row.Price is { } modelPrice && row.Cards.IsDefaultOrEmpty) sectionBlock.AddChild(PriceDisplay(modelPrice));
                    }
                    else if (!row.Models.IsDefaultOrEmpty) AddIconGroup(row.Models, JoinCaption(artCaption, rowName), row.Possible);
                    if (!suppressBranchCards && row.ShowCards && !row.Cards.IsDefaultOrEmpty)
                        AddCardGroup(row.Cards, JoinCaption(artCaption.Length == 0 && row.Price.HasValue
                            ? NameOf(section.Source) : artCaption, rowName), branchIndex,
                            style.Cards is OperationCardLayout.Stack or OperationCardLayout.Groups,
                            style.Cards == OperationCardLayout.Spread, row.Possible && row.Action != OperationPreviewAction.TransformCard,
                            forceSingleRow: section.Source.Entry == "DREAM_CATCHER" || preview.Source.Entry == "SLIPPERY_BRIDGE",
                            price: row.Price);
                }
                bool ordinalFocus = section.CardGroups.Any(group => group.Highlighted);
                for (int groupIndex = 0; groupIndex < section.CardGroups.Length; groupIndex++)
                {
                    var group = section.CardGroups[groupIndex];
                    if (group.Cards.IsDefaultOrEmpty) continue;
                    bool potionOrderCaption = !group.PotionUseOrder.IsDefaultOrEmpty &&
                        group.Label?.StartsWith("PotionUseStep:", StringComparison.Ordinal) == true;
                    string groupName = LocalizedLabel(section.Source, group.Label);
                    if (groupName.Length == 0) groupName = UiText.Format("operation.group", groupIndex + 1);
                    if (text)
                    {
                        string qualifier = group.Possible ? UiText.Get("operation.possiblePrefix") : "";
                        if (MapCardPresentation)
                        {
                            // Retain authored grouping/conditional context,
                            // without repeating the native card-name list.
                            if (!string.IsNullOrWhiteSpace(group.Label) || group.Possible)
                                sectionBlock.AddChild(WrappedText(qualifier + groupName, true));
                            var compactCards = group.Cards.Where(card => MapSummaryCardNames || !MapUsesCardFace(card)).ToArray();
                            if (compactCards.Length != 0)
                                sectionBlock.AddChild(WrappedText(string.Join("／", compactCards.Select(CardName))));
                        }
                        else if (style.Text == OperationTextMode.CardRows)
                        {
                            sectionBlock.AddChild(WrappedText(qualifier + groupName, true));
                            foreach (var card in group.Cards) sectionBlock.AddChild(WrappedText(CardName(card)));
                        }
                        else sectionBlock.AddChild(WrappedText(qualifier + groupName + UiText.Get("operation.labelSeparator") +
                            string.Join("／", group.Cards.Select(CardName))));
                    }
                    bool rerollGroup = group.Label?.StartsWith("RewardRerollStep:", StringComparison.Ordinal) == true;
                    if (!suppressBranchCards) AddCardGroup(group.Cards,
                        rerollGroup ? groupName : string.IsNullOrEmpty(group.Label) || potionOrderCaption ? artCaption : JoinCaption(artCaption, groupName), branchIndex,
                        style.Cards is OperationCardLayout.Stack or OperationCardLayout.Groups or OperationCardLayout.Branches,
                        style.Cards == OperationCardLayout.Spread, group.Possible && !group.SuppressPossiblePrefix,
                        group.Highlighted, group.EarliestFirst, group.ForceSingleRow || section.Source.Entry == "DREAM_CATCHER" ||
                        preview.Source.Entry == "SLIPPERY_BRIDGE" && style.Cards == OperationCardLayout.Parallel,
                        dimmed: ordinalFocus && !group.Highlighted, compact: ordinalFocus,
                        potionUseOrder: group.PotionUseOrder);
                }
            }
            if (block.GetChildCount() > 0) details.AddChild(block); else block.QueueFree();
        }
        if (rootStyle.Text != OperationTextMode.None)
            foreach (var key in preview.ContainedRelics.Where(key => !preview.Branches.Any(b => b.Order.Contains(key) || b.Sections.Any(s => s.Source == key))))
                details.AddChild(Identity(key, NameOf(key), 24));
        if (OperationDisplayPolicy.TransformOnly(preview) && _stacks.Count > 0 ||
            preview.Source.Category == "POTION" && (_stacks.Count > 0 || _iconGroups.Count > 0))
        {
            // Preserve incomplete/failure information beside the cards without
            // reintroducing a redundant left-hand transform summary panel.
            var notices = Column(); AddOutcomeNotices(notices, preview);
            string caption = string.Join(" · ", notices.GetChildren().OfType<Label>().Select(label => label.Text));
            if (_stacks.Count > 0) _stacks[0].Caption = JoinCaption(_stacks[0].Caption, caption);
            else _iconGroups[0].Caption = JoinCaption(_iconGroups[0].Caption, caption);
            notices.QueueFree();
        }
        else AddOutcomeNotices(details, preview);
        if (details.GetChildCount() > 0 || effectDetails.Count > 0)
        {
            _summary = new Control { Name = "Rt2NeowSummary", MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_summary);
            if (details.GetChildCount() > 0) AddSummaryPanel(details, preview.Source, true);
            else details.QueueFree();
            foreach (var effect in effectDetails) AddSummaryPanel(effect.Content, effect.Source, false);
            _summaryContent = _summaryPanels[0].Content;
        }
        else details.QueueFree();
        BuildStacks();
        // No unbounded history or identity-only reuse: unmatched old faces are
        // discarded after this display, including language/attribute changes.
        _cardReuse?.Clear();
        SetLayout(_summaryBounds.Size.X > 0 ? _summaryBounds : new(Vector2.Zero, SummaryDesiredSize), _cardsBounds);
    }

    // Presentation segmentation preserves the full authored result sequence.
    internal static ImmutableArray<OperationPreviewSection> DisplaySections(OperationPreview preview, OperationPreviewBranch branch)
    {
        if (preview.Source.Entry == "SLIPPERY_BRIDGE")
        {
            var cards = branch.Sections.SelectMany(section => section.Rows)
                .Where(row => row.Action == OperationPreviewAction.RemoveCard).SelectMany(row => row.Cards).ToImmutableArray();
            if (cards.IsEmpty) return branch.Sections;
            bool stacked = cards.Length > 8;
            var groups = cards.Chunk(stacked ? 5 : 8).Select((chunk, index) =>
                new OperationPreviewCardGroup(index, chunk.ToImmutableArray(), 0, 0, false, false)
                {
                    ForceSingleRow = !stacked, EarliestFirst = true,
                    Label = stacked ? UiText.Format("operation.stepRange", index * 5 + 1, index * 5 + chunk.Length) : null
                }).ToImmutableArray();
            return [new(preview.Source, [], groups)
                { Style = new(OperationTextMode.None, stacked ? OperationCardLayout.Groups : OperationCardLayout.Parallel) }];
        }
        return branch.Sections.Select(section => section.Source.Entry != "SEA_GLASS" ? section : section with
        {
            CardGroups = section.CardGroups.SelectMany(group => group.Cards.GroupBy(card =>
                ModelDb.GetById<CardModel>(new ModelId(card.Key.Category, card.Key.Entry)).Rarity.ToString()).Select((rarity, index) =>
                    group with { Cards = rarity.ToImmutableArray(), ForceSingleRow = true,
                        Label = rarity.Key switch { "Common" => UiText.Get("operation.rarity.Common"), "Uncommon" => UiText.Get("operation.rarity.Uncommon"), "Rare" => UiText.Get("operation.rarity.Rare"), _ => rarity.Key } }))
                .ToImmutableArray(),
            Style = new(OperationDisplayPolicy.Resolve(preview, section).Text, OperationCardLayout.Groups)
        }).ToImmutableArray();
    }

    private void AddSummaryPanel(VBoxContainer details, ModelKey source, bool primary)
    {
        if (!MapCardPresentation || !primary)
        {
            Control title = source.IsValid ? Identity(source, NameOf(source), 32, true)
                : Text(UiText.Get("operation.operation.prediction"), 22, true);
            details.AddChild(title); details.MoveChild(title, 0);
        }
        _summaryNaturalWidth = Math.Max(_summaryNaturalWidth, NaturalWidth(details));
        details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { Name = primary ? "Rt2NeowSummaryScroll" : "Rt2OperationEffectScroll_" + source.Entry,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = MapCardPresentation ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.Auto,
            MouseFilter = MouseFilterEnum.Ignore };
        scroll.GetVScrollBar().MouseFilter = MouseFilterEnum.Ignore;
        scroll.GetHScrollBar().MouseFilter = MouseFilterEnum.Ignore;
        scroll.AddChild(details);
        details.MinimumSizeChanged += () => Callable.From(LayoutSummaryAfterResize).CallDeferred();
        var shell = Surface(scroll); shell.Name = primary ? "Rt2OperationPrimaryPanel" : "Rt2OperationEffectPanel_" + source.Entry;
        _summary!.AddChild(shell); _summaryPanels.Add((shell, details));
    }

    private void AddModelRow(VBoxContainer block, string action, ImmutableArray<ModelKey> models, bool onePerRow)
    {
        if (onePerRow)
        {
            foreach (var key in models)
            {
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", 7);
                row.AddChild(Text(action));
                var identity = Identity(key, NameOf(key), 24);
                foreach (var label in identity.GetChildren().OfType<Label>())
                    label.CustomMinimumSize = new(Math.Min(260, label.CustomMinimumSize.X), 0);
                row.AddChild(identity);
                block.AddChild(row);
            }
            return;
        }
        var line = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("h_separation", 7); line.AddThemeConstantOverride("v_separation", 2);
        line.AddChild(Text(action));
        foreach (var key in models) line.AddChild(Identity(key, NameOf(key), 24));
        block.AddChild(line);
    }

    private static float NaturalWidth(Control control)
    {
        if (control is Label label)
        {
            float width = label.Text.Split('\n').Select(line => label.GetThemeFont("font")
                .GetStringSize(line, fontSize: label.GetThemeFontSize("font_size")).X).DefaultIfEmpty().Max();
            // Explanations and card-name lists already wrap. They should grow
            // vertically, not force every otherwise compact panel to 620px.
            return label.AutowrapMode != TextServer.AutowrapMode.Off && label.CustomMinimumSize.X == 0
                ? Math.Min(318, width) : width;
        }
        var children = control.GetChildren().OfType<Control>().ToArray();
        if (control is HBoxContainer or HFlowContainer) return children.Sum(NaturalWidth) + Math.Max(0, children.Length - 1) * 9;
        return Math.Max(control.CustomMinimumSize.X, children.Select(NaturalWidth).DefaultIfEmpty().Max());
    }

    private static IEnumerable<string> DisplayNotices(OperationPreview preview) => preview.Notices.Where(notice =>
        !notice.StartsWith("RewardSacrificeProgress:", StringComparison.Ordinal) &&
        notice != "BridgeAlternativeRemovalProposals" && (notice != "EventTransformSlotAlternativesRelated" ||
            preview.Branches.SelectMany(branch => branch.Sections).Any(section =>
                OperationDisplayPolicy.Resolve(preview, section).Text != OperationTextMode.None)));

    private static void AddOutcomeNotices(VBoxContainer target, OperationPreview preview)
    {
        foreach (string notice in DisplayNotices(preview).Select(notice => preview.NoticesAreLocalized ? notice : NoticeText(notice))
            .Where(s => s.Length > 0).Distinct()) target.AddChild(WrappedText(notice));
        if (!preview.Faults.IsDefaultOrEmpty || preview.Branches.Any(b => b.Faulted))
        {
            bool hasResults = !preview.CurseCandidates.IsEmpty || !preview.ContainedRelics.IsEmpty ||
                preview.Branches.Any(b => b.Sections.Any(s => s.Rows.Any(r => !r.Cards.IsEmpty || !r.Models.IsEmpty) ||
                    s.CardGroups.Any(g => !g.Cards.IsEmpty)));
            target.AddChild(WrappedText(hasResults ? UiText.Get("operation.partialPredictionFailed") :
                UiText.Get("operation.predictionFailed")));
        }
        foreach (string notice in preview.PendingReasons.Concat(preview.Branches.SelectMany(b => b.PendingReasons))
            .Where(IsUnexpandedRemainder).Select(PendingText).Distinct()) target.AddChild(WrappedText(notice));
    }

    private static bool IsUnexpandedRemainder(string reason) => !string.IsNullOrWhiteSpace(reason) &&
        !reason.StartsWith("AwaitingPlayerChoice:", StringComparison.Ordinal) && !reason.StartsWith("EventDesignDeferred:", StringComparison.Ordinal);

    private static string PendingText(string reason)
    {
        if (reason == "EventCombatVictoryHealthRequired") return UiText.Get("operation.pending.victoryHealth");
        if (reason == "EventCombatSealBudgetRequired") return UiText.Get("operation.pending.sealSettlement");
        if (reason.StartsWith("EventCombatRewardPending:", StringComparison.Ordinal)) return UiText.Get("operation.pending.combatRewardFacts");
        if (reason.Contains("CombatFacts", StringComparison.Ordinal)) return UiText.Get("operation.pending.combatSettlement");
        if (reason.Contains("CourierRestockAwaiting", StringComparison.Ordinal)) return UiText.Get("operation.pending.courierObtain");
        if (reason.Contains("Limit", StringComparison.Ordinal)) return UiText.Get("operation.pending.unexpandedOutcomes");
        if (reason.Contains("Reroll", StringComparison.Ordinal) || reason.Contains("Sacrifice", StringComparison.Ordinal))
            return UiText.Get("operation.pending.alternativeOutcomes");
        if (reason.Contains("Input:", StringComparison.Ordinal) || reason.StartsWith("OfferSelection:", StringComparison.Ordinal))
            return UiText.Get("operation.pending.currentState");
        return UiText.Get("operation.pending.undeterminedInputs");
    }

    private static string NoticeText(string notice)
    {
        if (notice == "Purchase:InsufficientGold") return UiText.Get("operation.insufficientGold");
        if (notice == "Purchase:TooFull") return UiText.Get("operation.potionSlotsFull");
        if (notice.StartsWith("RewardSacrificeProgress:", StringComparison.Ordinal) && int.TryParse(notice[24..], out int progress))
            return UiText.Format("operation.sacrificeProgress", progress, (progress + 1) % 2);
        if (notice == "EventTransformSlotAlternativesRelated") return UiText.Get("operation.notice.transformSlotAlternatives");
        if (notice == "EventCombatStableContextNoExtraActions") return UiText.Get("operation.notice.stableCombatContext");
        if (notice == "EventCombatRoyaltiesPlayed") return UiText.Get("operation.notice.royaltiesPlayed");
        if (notice == "EventCombatRoyaltiesNotPlayed") return UiText.Get("operation.notice.royaltiesNotPlayed");
        if (notice == "PotionAfterAcquisition") return UiText.Get("operation.potion.afterAcquisition");
        if (notice == "PotionAcquisitionRequiresSlot") return UiText.Get("operation.potion.replaceSlot");
        if (notice == "PotionAcquisitionBlocked") return UiText.Get("operation.potion.acquisitionBlocked");
        if (notice == "EventCombatNoInterveningRewardClaims") return UiText.Get("operation.notice.noInterveningClaims");
        if (notice.StartsWith("战斗使用身份预测", StringComparison.Ordinal)) return UiText.Get("operation.notice.combatUseIdentities");
        if (notice.StartsWith("假设除本瓶", StringComparison.Ordinal))
        {
            var count = System.Text.RegularExpressions.Regex.Match(notice, @"生成 (\d+)，入槽 (\d+)");
            return UiText.Get("operation.notice.otherPotionSlotsEmpty") +
                (count.Success ? UiText.Format("operation.generatedObtainedSuffix", count.Groups[1].Value, count.Groups[2].Value) : "");
        }
        if (notice == "BridgeAlternativeRemovalProposals") return UiText.Get("operation.notice.bridgeRemovalProposals");
        if (notice.Contains(':') && notice.All(c => c < 128)) return PendingText(notice);
        return LocalizedBilingual(notice);
    }

    private static string LocalizedBilingual(string value)
    {
        int divider = value.IndexOf(" / ", StringComparison.Ordinal);
        return divider < 0 ? value : UiText.LanguageCode == "zh" ? value[..divider] : value[(divider + 3)..];
    }

    private static string LocalizedLabel(ModelKey source, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value == "PotionCombatUse") return UiText.Get("operation.combat.use");
        if (value == "PotionUseOrdinal") return "";
        if (value.StartsWith("PotionUseStep:", StringComparison.Ordinal) && int.TryParse(value[14..], out int potionStep))
            return UiText.Format("operation.potion.useStep", potionStep);
        if (value.StartsWith("EntropicCurrentSlots:", StringComparison.Ordinal) || value.StartsWith("EntropicUseStep:", StringComparison.Ordinal))
        {
            var parts = value.Split(':');
            bool sequence = parts[0] == "EntropicUseStep";
            int offset = sequence ? 2 : 1;
            if (parts.Length == offset + 2 && int.TryParse(parts[offset], out int generated) &&
                int.TryParse(parts[offset + 1], out int obtained))
            {
                string label = sequence && int.TryParse(parts[1], out int ordinal)
                    ? UiText.Format("operation.potion.entropicStep", ordinal) : "";
                return generated == obtained ? label : JoinCaption(label, UiText.Format("operation.generatedObtained", generated, obtained));
            }
        }
        if (value == "CardTargetConditional") return UiText.Get("operation.current.selection.order");
        if (value == "RewardRerollSameRecipe") return UiText.Get("operation.label.sameRewardRerolls");
        if (value == "RewardRerollOrdinal") return UiText.Get("operation.place.this.group");
        if (value.StartsWith("RewardRerollStep:", StringComparison.Ordinal) && int.TryParse(value[17..], out int rerollStep))
            return UiText.Format("operation.rerollStep", rerollStep);
        if (value.StartsWith("RewardSacrificeStep:", StringComparison.Ordinal) && int.TryParse(value[20..], out int sacrificeStep))
            return UiText.Format("operation.sacrificeStep", sacrificeStep);
        if (value == "OrreryFinalRerollRestock") return UiText.Get("operation.label.finalRerollRestock");
        if (value == "OrreryPurchaseRestockNoReroll") return UiText.Get("operation.without.rerolling");
        if (value == "OrreryPurchaseRestockNoSacrifice") return UiText.Get("operation.without.sacrificing");
        if (value == "OrreryPurchaseRestockNoAlternatives") return UiText.Get("operation.without.rerolling.or.sacrificing");
        if (value == "OrreryFinalRerollRestockNoSacrifice") return UiText.Get("operation.label.noMoreRerollsOrSacrificesRestock");
        if (value == "OrreryFinalSacrificeRestock") return UiText.Get("operation.label.finalSacrificeRestock");
        if (value == "OrreryFinalSacrificeRestockNoReroll") return UiText.Get("operation.label.noMoreSacrificesOrRerollsRestock");
        if (value.StartsWith("GeneratedObtained:", StringComparison.Ordinal))
        {
            var counts = value.Split(':');
            if (counts.Length == 3 && int.TryParse(counts[1], out int generated) && int.TryParse(counts[2], out int obtained))
                return UiText.Format("operation.generatedObtained", generated, obtained);
        }
        if (value == "EntropicOtherSlotsEmpty") return UiText.Get("operation.other.slots.empty");
        if (value.StartsWith("EntropicOtherSlotsEmpty:", StringComparison.Ordinal))
        {
            var counts = value.Split(':');
            return UiText.Get("operation.other.slots.empty") +
                (counts.Length == 3 && int.TryParse(counts[1], out int generated) && int.TryParse(counts[2], out int obtained)
                    && generated != obtained ? UiText.Format("operation.entropicCountsSuffix", generated, obtained) : "");
        }
        if (value == "EventRestRewards") return UiText.Get("operation.rest.rewards");
        if (value == "EventCombatRewards") return UiText.Get("operation.combat.rewards");
        if (value == "EventCombatNoHunt") return UiText.Get("operation.label.noHunt");
        if (value.StartsWith("EventHuntTriggers:", StringComparison.Ordinal) && int.TryParse(value[18..], out int huntCount))
            return UiText.Format("operation.label.huntTriggers", huntCount);
        if (value.StartsWith("EventHuntConditional:", StringComparison.Ordinal) && int.TryParse(value[21..], out int huntIndex))
            return UiText.Format("operation.label.huntGroup", huntIndex);
        if (value == "WhetstoneAfterSkippingCardRewards") return UiText.Get("operation.label.whetstoneSkipCards");
        if (value == "WhetstoneAfterMirrorChoice") return UiText.Get("operation.label.whetstoneMirrorChoice");
        if (value == "WhetstoneNoUpgradeTargets") return UiText.Get("operation.noUpgradeTargets");
        if (value == "WhetstonePossibleNoUpgradeTargets") return UiText.Get("operation.possibleNoUpgradeTargets");
        if (value == "EventCombatNoDamage") return UiText.Get("operation.no.damage.taken");
        if (value == "EventCombatDamageTaken") return UiText.Get("operation.damage.taken");
        if (int.TryParse(value, out int step)) return UiText.Format("operation.step", step);
        if (value == "EventTransformTargets") return UiText.Get("operation.selected.original.cards");
        if (value.Contains(" / ", StringComparison.Ordinal)) return LocalizedBilingual(value);
        if (value.All(c => char.IsUpper(c) || c is '_' or '/' || char.IsDigit(c)))
        {
            var table = LocManager.Instance?.GetTable("events");
            if (table == null) return "";
            var pieces = value.Split('/').Select(part =>
            {
                string? key = table.Keys.FirstOrDefault(k => k.StartsWith(source.Entry + ".", StringComparison.Ordinal) &&
                    k.EndsWith(".options." + part + ".title", StringComparison.Ordinal));
                return key == null ? "" : new LocString("events", key).GetFormattedText();
            }).Where(p => p.Length > 0);
            return string.Join(" → ", pieces);
        }
        return value;
    }

    private static string BranchLabel(ModelKey source, string? label, int index)
    {
        if (int.TryParse(label, out int ordinal)) return BranchNumber(Math.Max(0, ordinal - 1));
        int divider = label?.IndexOf('/') ?? -1;
        if (divider > 0 && int.TryParse(label![..divider], out ordinal))
            return JoinCaption(BranchNumber(Math.Max(0, ordinal - 1)), LocalizedLabel(source, label[(divider + 1)..]));
        return LocalizedLabel(source, label) is { Length: > 0 } localized ? localized : BranchNumber(index);
    }
    private static string JoinCaption(string left, string right) => left.Length == 0 ? right : right.Length == 0 ? left : left + " · " + right;
    private static string BranchNumber(int index) => index == 0 ? "①" : index == 1 ? "②" : (index + 1).ToString();
    private static string ActionName(OperationPreviewAction action) => action switch
    {
        OperationPreviewAction.Restock => UiText.Get("operation.restock"),
        OperationPreviewAction.ObtainRelic or OperationPreviewAction.AddCard => UiText.Get("operation.obtain"),
        OperationPreviewAction.UpgradeCard => UiText.Get("operation.upgrade"), OperationPreviewAction.TransformCard => UiText.Get("operation.transform"),
        OperationPreviewAction.EnchantCard => UiText.Get("operation.enchant"), OperationPreviewAction.RemoveCard => UiText.Get("operation.remove"),
        OperationPreviewAction.DowngradeCard => UiText.Get("operation.downgrade"),
        OperationPreviewAction.GeneratePotion => UiText.Get("operation.obtain"),
        OperationPreviewAction.Choose => UiText.Get("operation.choose"), _ => UiText.Get("operation.result")
    };
    private static string NameOf(ModelKey key)
    {
        if (string.IsNullOrEmpty(key.Entry)) return UiText.Get("operation.operation.result");
        string table = key.Category switch { "CARD" => "cards", "POTION" => "potions", "EVENT" or "ANCIENT" => "events", "ENCHANTMENT" => "enchantments", _ => "relics" };
        return new LocString(table, key.Entry + ".title").GetFormattedText();
    }
    private static string CardName(PredictorCard card) => NameOf(card.Key) +
        (card.UpgradeLevel > 0 ? "+" + (card.UpgradeLevel > 1 ? card.UpgradeLevel.ToString() : "") : "") +
        (card.Enchantment is { } enchantment ? " · " + NameOf(enchantment) + " " + card.EnchantmentAmount : "");
}
