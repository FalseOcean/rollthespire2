using Godot;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// One-line Neow choice/route selector. Primary buttons are real Neow relic choices.
/// Bones primary selection is allowed only when the existing canonical outcome grouping
/// proves all internal acquisition routes equivalent. Split Bones routes are selected by
/// their first-pick child relic buttons; presentation never recomputes route equivalence.
/// </summary>
internal sealed partial class NeowChoiceStrip : VBoxContainer
{
    public const float PrimaryMinimumHeight = 44f;
    public const float ChildMinimumHeight = 38f;
    public const float PrimaryStretchRatio = 1f;
    public const float ChildStretchRatio = 1f;
    public const int PrimaryIconMaxWidth = 38;
    public const int ChildIconMaxWidth = 32;

    private readonly IGameIconResolver _icons;
    public bool HorizontalChoices { get; set; }

    public NeowChoiceStrip(IGameIconResolver icons)
    {
        _icons = icons;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        AddThemeConstantOverride("separation", 6);
    }

    public event Action<ModelKey, string>? SelectionRequested;

    public void Bind(
        IReadOnlyList<NeowChoiceViewModel> choices,
        ModelKey selectedRootRelicKey,
        string selectedOpeningRouteId,
        string missingIconTooltip,
        string firstPickupLabel)
    {
        Clear();
        HBoxContainer? primaryRow = null;
        var routeRows = new List<Control>();
        if (HorizontalChoices)
        {
            primaryRow = new HBoxContainer(); primaryRow.AddThemeConstantOverride("separation", 6); AddChild(primaryRow);
        }
        foreach (NeowChoiceViewModel choice in choices.OrderBy(choice => choice.SlotIndex))
        {
            bool splitBonesRoutes = HasAmbiguousBonesRoutes(choice);
            bool selectedRoot = choice.RelicKey == selectedRootRelicKey;
            bool selectedPrimary = selectedRoot && !splitBonesRoutes &&
                                   (choice.OpeningRoutes.Count == 0 || choice.OpeningRoutes.Any(route =>
                                       string.Equals(route.RouteId, selectedOpeningRouteId, StringComparison.Ordinal)));
            Button primary = BuildRelicButton(
                choice.RelicDisplay,
                selectedPrimary,
                compact: false,
                enabled: !splitBonesRoutes && IsBonesPrimarySelectable(choice),
                missingIconTooltip);
            primary.TooltipText = choice.Tooltip;
            if (!splitBonesRoutes)
            {
                primary.Pressed += () =>
                {
                    string routeId = ResolvePrimaryRoute(choice, selectedOpeningRouteId);
                    SelectionRequested?.Invoke(choice.RelicKey, routeId);
                };
            }
            if (HorizontalChoices) primaryRow!.AddChild(primary); else AddChild(primary);

            if (choice.BonesOutcome is not { } bones || !splitBonesRoutes)
            {
                continue;
            }

            BonesOutcomeGroupViewModel[] selectableGroups = bones.OutcomeGroups
                .Where(group =>
                    group.RelicResults.Count > 0 &&
                    !string.IsNullOrWhiteSpace(group.RepresentativeRouteId))
                .ToArray();
            if (selectableGroups.Length == 0)
            {
                continue;
            }

            BoxContainer childColumn = HorizontalChoices ? new HBoxContainer() : new VBoxContainer();
            childColumn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            childColumn.SizeFlagsStretchRatio = HorizontalChoices ? 1.8f : 1f;
            childColumn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            childColumn.AddThemeConstantOverride("separation", HorizontalChoices ? 6 : 4);
            Label firstPickupHeader = Ui1Theme.Label(firstPickupLabel, Ui1TextRole.Muted, wrap: false);
            firstPickupHeader.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            firstPickupHeader.MouseFilter = Control.MouseFilterEnum.Ignore;
            childColumn.AddChild(firstPickupHeader);
            foreach (BonesOutcomeGroupViewModel group in selectableGroups)
            {
                BonesRelicScopedResultViewModel? firstRelic = group.RelicResults.FirstOrDefault();
                ArgumentNullException.ThrowIfNull(firstRelic);
                bool routeSelected = selectedRoot &&
                                     group.EquivalentRouteIds.Contains(selectedOpeningRouteId, StringComparer.Ordinal);
                Button route = BuildRelicButton(
                    firstRelic.RelicDisplay,
                    routeSelected,
                    compact: true,
                    enabled: true,
                    missingIconTooltip);
                route.Text = firstRelic.DisplayName;
                route.TooltipText = firstRelic.RelicDisplay.Tooltip;
                string routeId = group.RepresentativeRouteId;
                route.Pressed += () => SelectionRequested?.Invoke(choice.RelicKey, routeId);
                var indent = new MarginContainer
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
                };
                indent.AddThemeConstantOverride("margin_left", HorizontalChoices ? 0 : 18);
                indent.AddChild(route);
                childColumn.AddChild(indent);
            }
            if (HorizontalChoices) routeRows.Add(childColumn); else AddChild(childColumn);
        }
        foreach (var routes in routeRows) primaryRow!.AddChild(routes);
    }

    public static bool HasAmbiguousBonesRoutes(NeowChoiceViewModel choice) =>
        choice.BonesOutcome is { } bones &&
        bones.OutcomeGroups.Count(group =>
            group.RelicResults.Count > 0 &&
            !string.IsNullOrWhiteSpace(group.RepresentativeRouteId)) > 1;

    public static bool BonesOrderAffectsResults(NeowChoiceViewModel choice) =>
        choice.BonesOutcome is { } bones &&
        (bones.OverallImpact == AcquisitionOrderImpact.ProvenSensitive ||
         bones.OrderComparisonStatus == OrderComparisonStatus.ProvenSensitive);

    public static bool IsBonesPrimarySelectable(NeowChoiceViewModel choice) =>
            choice.BonesOutcome is not { } bones ||
            !HasAmbiguousBonesRoutes(choice) &&
            (bones.OutcomeGroups.Count <= 1 || choice.OpeningRoutes.Count > 0);

    private Button BuildRelicButton(
        GameContentDisplayViewModel relic,
        bool selected,
        bool compact,
        bool enabled,
        string missingIconTooltip)
    {
        IconDescriptor descriptor = _icons.Resolve(relic.ModelKey, relic.ContentKind, IconVariant.Small);
        var button = new Button
        {
            Text = relic.DisplayName,
            Icon = descriptor.Texture,
            ExpandIcon = true,
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0f, compact ? ChildMinimumHeight : PrimaryMinimumHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = compact ? ChildStretchRatio : PrimaryStretchRatio,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            Disabled = !enabled,
            TooltipText = descriptor.IsMissing
                ? missingIconTooltip
                : string.Empty
        };
        button.AddThemeConstantOverride("icon_max_width", compact ? ChildIconMaxWidth : PrimaryIconMaxWidth);
        Ui1Theme.ApplyButton(button, selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Secondary);
        return button;
    }

    private static string ResolvePrimaryRoute(NeowChoiceViewModel choice, string selectedOpeningRouteId)
    {
        if (!IsBonesPrimarySelectable(choice))
        {
            return string.Empty;
        }
        if (choice.OpeningRoutes.Any(route => string.Equals(route.RouteId, selectedOpeningRouteId, StringComparison.Ordinal)))
        {
            return selectedOpeningRouteId;
        }
        if (choice.BonesOutcome is { OutcomeGroups.Count: 1 } bones)
        {
            return bones.OutcomeGroups[0].RepresentativeRouteId;
        }
        return choice.OpeningRoutes.OrderBy(route => route.RouteOrder).FirstOrDefault()?.RouteId ?? string.Empty;
    }

    private void Clear()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }
}
