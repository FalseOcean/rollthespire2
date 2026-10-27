using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodeDetails
{
    private const float RewardDockCardScale = .42f;
    private const int RewardDockGroupGap = 18;
    private ScrollContainer? _rewardCardDock;
    private HBoxContainer? _rewardCardDockGroups;
    private readonly List<Control> _rewardCardGroupHosts = [];
    private Vector2 _rewardCardDockNaturalSize;

    internal bool HasRewardCardDock => _rewardCardGroupHosts.Count != 0;
    internal Vector2 RewardCardDockDesiredSize => _rewardCardDockNaturalSize;
    internal Rect2 RewardCardDockGlobalRegion => _rewardCardDock?.IsVisibleInTree() == true
        ? _rewardCardDock.GetGlobalRect() : default;
    internal bool RewardCardDockContains(Vector2 global) => _rewardCardDock?.IsVisibleInTree() == true &&
        _rewardCardDock.GetGlobalRect().HasPoint(global);

    private void ClearRewardCardDock()
    {
        _rewardCardGroupHosts.Clear();
        _rewardCardDockNaturalSize = Vector2.Zero;
        if (_rewardCardDock is not null)
        {
            RemoveChild(_rewardCardDock);
            _rewardCardDock.QueueFree();
        }
        _rewardCardDock = null;
        _rewardCardDockGroups = null;
    }

    private void EnsureRewardCardDock()
    {
        if (_rewardCardDock is not null) return;
        _rewardCardDock = new ScrollContainer
        {
            Name = "MapRewardCardDock", Visible = false, MouseFilter = MouseFilterEnum.Stop, ClipContents = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            FollowFocus = true, ZIndex = 110
        };
        _rewardCardDock.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _rewardCardDockGroups = new HBoxContainer
        {
            Name = "MapRewardCardGroups", MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _rewardCardDockGroups.AddThemeConstantOverride("separation", RewardDockGroupGap);
        _rewardCardDock.AddChild(_rewardCardDockGroups);
        AddChild(_rewardCardDock);
    }

    private Control? RegisterRewardCardGroup(InRunMapDetailSection section, IReadOnlyList<InRunMapDetailObject> items)
    {
        items = items.Where(item => item.CardSnapshot == null || NeowOperationOverlay.MapUsesCardFace(item.CardSnapshot)).ToArray();
        if (items.Count == 0) return null;
        EnsureRewardCardDock();
        int ordinal = _rewardCardGroupHosts.Count;
        var host = new Control
        {
            Name = "MapRewardCardGroup_" + ordinal, MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        // Real reward sections carry their stable opportunity identity. Other
        // authored card-selection sections only need a local display address.
        string displayId = section.StableId ?? (_data?.NodeId + "|card-section|" + ordinal);
        host.SetMeta("reward_group", displayId);
        host.SetMeta("reward_group_has_stable_identity", section.StableId is not null);
        host.SetMeta("map_card_count", items.Count);
        _rewardCardDockGroups!.AddChild(host);
        var cards = items.Select(item => item.CardSnapshot).OfType<PredictorCard>().ToImmutableArray();
        if (cards.Length != items.Count)
        {
            // Do not replace a missing snapshot with a canonical model or show
            // a partial opportunity as if it were the complete offer.
            RuntimeLog.Warn("mapRewardCardDockUnavailable=true;group=" + displayId +
                ";reason=MissingCardSnapshot;missing=" + (items.Count - cards.Length));
            var unavailable = Text(JsonUiTextProvider.CreateUi13(UiLanguagePreferences.LanguageCode)
                .Get("operation.card.artwork.unavailable"), 16, true);
            unavailable.MouseFilter = MouseFilterEnum.Ignore;
            unavailable.Size = new(180, 90);
            host.AddChild(unavailable);
            host.CustomMinimumSize = new(180, 90);
        }
        else
        {
            var source = _data?.HeaderObject?.Key ?? default;
            var preview = new OperationPreview(source, [], [], false,
                [new([], [new(source, [], [new(ordinal, cards, 0, 1, true, false)])], [], 1, true)], [])
            { Style = new(OperationTextMode.None, OperationCardLayout.Stack), OptionKey = "group:" + displayId };
            var stack = new NeowOperationOverlay
            {
                Name = "MapRewardCardStack_" + ordinal, ExternallyFramed = true,
                MapCardPresentation = true, CardScaleOverride = RewardDockCardScale
            };
            host.AddChild(stack);
            stack.SetPreview(preview);
            Vector2 size = stack.CardsDesiredSize;
            host.CustomMinimumSize = size;
            host.Size = size;
            stack.SetLayout(default, new Rect2(Vector2.Zero, size));
        }
        AddCardDockHost(host, host.CustomMinimumSize);
        return host;
    }

    private void RegisterOutcomeCardDock(InRunMapDetailSection section, OperationPreview outcome)
    {
        EnsureRewardCardDock();
        int ordinal = _rewardCardGroupHosts.Count;
        var host = new Control
        {
            Name = "MapOutcomeCardGroup_" + ordinal, MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        // Settled generated/changed cards belong to the outcome receipt, not
        // to a new reward offer. Keep that scope distinct in presentation too.
        host.SetMeta("outcome_scope", section.StableId ?? (_data?.NodeId + "|outcome-section|" + ordinal));
        var preview = new NeowOperationOverlay
        {
            Name = "MapOutcomeCardStack_" + ordinal, ExternallyFramed = true,
            MapCardPresentation = true, CardScaleOverride = RewardDockCardScale
        };
        host.AddChild(preview);
        _rewardCardDockGroups!.AddChild(host);
        preview.SetPreview(outcome);
        host.SetMeta("map_card_count", preview.MapCardCount);
        Vector2 size = preview.CardsDesiredSize;
        if (size.X <= 0 || size.Y <= 0)
        {
            _rewardCardDockGroups.RemoveChild(host);
            host.QueueFree();
            return;
        }
        // This is the same immutable outcome used by the main summary. Its
        // action verbs, links and unknown/failure notices remain there; the
        // upper view displays only its native art and authored art captions.
        preview.SetLayout(default, new Rect2(Vector2.Zero, size));
        AddCardDockHost(host, size);
    }

    private void AddCardDockHost(Control host, Vector2 size)
    {
        host.CustomMinimumSize = host.Size = size;
        _rewardCardGroupHosts.Add(host);
        int totalCards = _rewardCardGroupHosts.Sum(group => group.GetMeta("map_card_count").AsInt32());
        foreach (var group in _rewardCardGroupHosts)
        {
            if (group.GetChildren().OfType<NeowOperationOverlay>().FirstOrDefault() is not { } preview) continue;
            preview.SetMapCardCount(totalCards);
            Vector2 desired = preview.CardsDesiredSize;
            group.CustomMinimumSize = group.Size = desired;
            preview.SetLayout(default, new Rect2(Vector2.Zero, desired));
        }
        _rewardCardDockNaturalSize = new(
            _rewardCardGroupHosts.Sum(group => group.CustomMinimumSize.X) +
                Math.Max(0, _rewardCardGroupHosts.Count - 1) * RewardDockGroupGap,
            _rewardCardGroupHosts.Max(group => group.CustomMinimumSize.Y) + RewardDockScrollHeight());
    }

    private float RewardDockScrollHeight() => _rewardCardDock is null ? 0 :
        Math.Max(16, _rewardCardDock.GetHScrollBar().GetCombinedMinimumSize().Y);

    private void FocusRewardCardGroup(Control? host)
    {
        if (host is null || !IsInstanceValid(host) || host.IsQueuedForDeletion() ||
            _rewardCardDock?.IsVisibleInTree() != true) return;
        _rewardCardDock.EnsureControlVisible(host);
        host.Modulate = new Color(1.07f, 1.07f, 1.07f);
        host.SetMeta("reward_group_focus", true);
    }

    private static void UnfocusRewardCardGroup(Control? host)
    {
        if (host is null || !IsInstanceValid(host) || host.IsQueuedForDeletion()) return;
        host.Modulate = Colors.White;
        host.SetMeta("reward_group_focus", false);
    }

    private void HideRewardCardDock() => _rewardCardDock?.Hide();

    private void ShowRewardCardDock()
    {
        if (HasRewardCardDock && HoverOpen && !_optionPreview.Visible) _rewardCardDock?.Show();
    }

    private void LayoutRewardCardDock(Rect2 area)
    {
        if (_rewardCardDock is null || !HasRewardCardDock || !HoverOpen || _optionPreview.Visible)
        { HideRewardCardDock(); return; }
        // The shared allocator owns placement and same-side spacing. Keep
        // native card scale and both scrollbars within the assigned region.
        var content = _rewardCardDockGroups!.GetCombinedMinimumSize();
        float verticalBar = _rewardCardDock.GetVScrollBar().GetCombinedMinimumSize().X;
        float horizontalBar = _rewardCardDock.GetHScrollBar().GetCombinedMinimumSize().Y;
        // A shortened card area needs room for its vertical scrollbar too.
        // Otherwise that bar can create horizontal overflow after Auto has
        // already decided to hide the horizontal scrollbar.
        bool horizontal = content.X > area.Size.X;
        bool vertical = content.Y > area.Size.Y - (horizontal ? horizontalBar : 0);
        horizontal = content.X > area.Size.X - (vertical ? verticalBar : 0);
        vertical = content.Y > area.Size.Y - (horizontal ? horizontalBar : 0);
        _rewardCardDock.HorizontalScrollMode = horizontal ? ScrollContainer.ScrollMode.ShowAlways : ScrollContainer.ScrollMode.Auto;
        _rewardCardDock.VerticalScrollMode = vertical ? ScrollContainer.ScrollMode.ShowAlways : ScrollContainer.ScrollMode.Auto;
        _rewardCardDock.Position = area.Position;
        _rewardCardDock.Size = area.Size;
        ShowRewardCardDock();
    }
}
