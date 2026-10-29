using Godot;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private string[]? _manualFamilyOrder;
    private IReadOnlyList<string> _automaticFamilyOrder = [];
    private IReadOnlyList<string> _plannedFamilyOrder = [];
    private IReadOnlySet<string> _denseFirstOrderUnits = new HashSet<string>();
    private bool _executionOrderPending = true;
    private string _executionOrderIssue = "";
    private OptionButton? _executionOrderMode;
    private Label? _executionOrderSummary, _executionOrderError;
    private ScrollContainer? _executionOrderScroll;
    private VBoxContainer? _executionOrderRows;

    private string OrderText(string key) => _text.Get("workflow.execution_order." + key);

    private void BuildExecutionOrder(VBoxContainer sidebar)
    {
        var section = new VBoxContainer { Name = "SearchExecutionOrder", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        section.AddThemeConstantOverride("separation", 5); sidebar.AddChild(section);
        var heading = new HBoxContainer(); heading.AddThemeConstantOverride("separation", 8); section.AddChild(heading);
        var title = _p.Label(_text.Get("workflow.execution_order"), 16, heading: true);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(title);
        _executionOrderMode = ContextOptions(0, 0, 112); _executionOrderMode.Reparent(heading, false);
        _executionOrderMode.Name = "ExecutionOrderMode";
        _executionOrderMode.CustomMinimumSize = new(112, 32);
        _executionOrderMode.AddItem(OrderText("auto")); _executionOrderMode.AddItem(OrderText("manual"));
        _executionOrderMode.ItemSelected += index =>
        {
            if (_persistence.Runtime.Receiving) return;
            _manualFamilyOrder = index == 0 ? null : (_plannedFamilyOrder.Count > 0 ? _plannedFamilyOrder : _automaticFamilyOrder).ToArray();
            _executionOrderIssue = "";
            InvalidateProbabilityPreview(); RenderExecutionOrder();
        };
        _executionOrderSummary = _p.Label("", 15, true);
        _executionOrderSummary.Name = "AutomaticExecutionOrder";
        _executionOrderSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart; section.AddChild(_executionOrderSummary);
        _executionOrderScroll = new ScrollContainer { Name = "ManualExecutionOrder", FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        section.AddChild(_executionOrderScroll);
        _executionOrderRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _executionOrderRows.AddThemeConstantOverride("separation", 3); _executionOrderScroll.AddChild(_executionOrderRows);
        _executionOrderError = _p.Label("", 14, true);
        _executionOrderError.Name = "ExecutionOrderIssue";
        _executionOrderError.AutowrapMode = TextServer.AutowrapMode.WordSmart; section.AddChild(_executionOrderError);
        RenderExecutionOrder();
    }

    private void ApplyExecutionOrderPreview(WorkbenchProbabilityPreview preview)
    {
        _executionOrderPending = false;
        _automaticFamilyOrder = preview.AutomaticFamilyOrder;
        _plannedFamilyOrder = preview.FamilyOrder;
        _denseFirstOrderUnits = preview.DenseFirstOrderUnits;
        _executionOrderIssue = preview.OrderIssue;
        // The core reconciles removed/new conditions without running automatic
        // ordering over the player's surviving units or combining them anew.
        if (_manualFamilyOrder is not null && preview.OrderIssue.Length == 0)
            _manualFamilyOrder = preview.FamilyOrder.ToArray();
        RenderExecutionOrder();
    }

    private void PresentExecutionOrderFailure(string issue)
    {
        _executionOrderPending = false;
        _executionOrderIssue = issue;
        _automaticFamilyOrder = []; _plannedFamilyOrder = [];
        RenderExecutionOrder();
    }

    private string OrderUnitTitle(string key) => string.Join(" + ", key.Split('+').Select(id =>
    {
        string shortId = id.Split('.')[0];
        string? category = shortId switch { "N" => "neow", "A" => "ancients", "C" => "combat_rewards",
            "E" => "events", "R" => "relics", "S" => "shop", "T" => "transform", _ => null };
        return shortId.Length == 1 && "NACERTMVWS".Contains(shortId, StringComparison.Ordinal)
            ? shortId + " · " + (category is null ? FamilyTitle(shortId) : _text.Get("query.domain." + category)) : id;
    }));

    private bool OrderControlsDisabled => _persistence.Runtime.Receiving;

    private void UpdateExecutionOrderAvailability()
    {
        if (_executionOrderMode is { } mode && GodotObject.IsInstanceValid(mode) && mode.Disabled != OrderControlsDisabled)
            RenderExecutionOrder();
    }

    private void RenderExecutionOrder()
    {
        if (_executionOrderMode is null || !GodotObject.IsInstanceValid(_executionOrderMode) || _executionOrderRows is null) return;
        bool manual = _manualFamilyOrder is not null, running = _persistence.Runtime.Receiving;
        _executionOrderMode.Select(manual ? 1 : 0); _executionOrderMode.Disabled = OrderControlsDisabled;
        var order = manual ? _manualFamilyOrder! : _automaticFamilyOrder;
        _executionOrderSummary!.Visible = !manual || order.Count == 0;
        _executionOrderSummary.Text = _executionOrderPending ? OrderText("pending") :
            order.Count == 0 ? OrderText("empty") : string.Join(" → ", order.Select(OrderUnitTitle));
        _executionOrderScroll!.Visible = manual && order.Count > 0;
        _executionOrderScroll.CustomMinimumSize = new(0, Math.Min(132, order.Count * 33));
        foreach (var child in _executionOrderRows.GetChildren()) { _executionOrderRows.RemoveChild(child); child.QueueFree(); }
        for (int index = 0; index < order.Count && manual; index++)
        {
            int position = index;
            var row = new HBoxContainer { Name = "ExecutionOrderUnit" + index, SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 4);
            _executionOrderRows.AddChild(row);
            bool firstOnly = _denseFirstOrderUnits.Contains(order[index]);
            var title = _p.Label((index + 1) + ". " + OrderUnitTitle(order[index]) + (firstOnly ? " · " + OrderText("first") : ""), 15);
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill; title.CustomMinimumSize = new(0, 30);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.ClipText = true; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; row.AddChild(title);
            var up = _p.CompactButton(OrderText("up"), 30, 14); up.Name = "MoveOrderUp" + index; up.CustomMinimumSize = new(56, 30);
            up.TooltipText = ""; up.ClipText = false;
            up.Disabled = running || index == 0 || _denseFirstOrderUnits.Contains(order[index - 1]);
            up.Pressed += () => MoveExecutionOrder(position, -1); row.AddChild(up);
            var down = _p.CompactButton(OrderText("down"), 30, 14); down.Name = "MoveOrderDown" + index; down.CustomMinimumSize = new(56, 30);
            down.TooltipText = ""; down.ClipText = false;
            down.Disabled = running || firstOnly || index == order.Count - 1;
            down.Pressed += () => MoveExecutionOrder(position, 1); row.AddChild(down);
        }
        _executionOrderError!.Visible = _executionOrderIssue.Length > 0;
        _executionOrderError.Text = _executionOrderIssue.Contains("DenseOnlyUnitMustBeFirst", StringComparison.Ordinal)
            ? OrderText("dense_first") : _executionOrderIssue;
    }

    private void MoveExecutionOrder(int index, int offset)
    {
        if (_persistence.Runtime.Receiving || _manualFamilyOrder is not { } current ||
            index < 0 || index >= current.Length || index + offset < 0 || index + offset >= current.Length) return;
        var reordered = current.ToArray();
        (reordered[index], reordered[index + offset]) = (reordered[index + offset], reordered[index]);
        _manualFamilyOrder = reordered; _plannedFamilyOrder = reordered;
        _executionOrderIssue = "";
        InvalidateProbabilityPreview(); RenderExecutionOrder();
    }
}
