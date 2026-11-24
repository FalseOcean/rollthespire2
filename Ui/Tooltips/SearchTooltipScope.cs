using Godot;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Ui.Tooltips;

/// <summary>One active hover per Search host; no whole-tree scan or per-control processing.</summary>
internal sealed partial class SearchTooltipScope : Node
{
    private readonly WeakReference<Control> _owner;
    private WeakReference<Control> _bounds;
    private readonly AnchoredTooltipHost _host;
    private readonly Control _proxy = new() { Name = "SearchTextTooltipAnchor", MouseFilter = Control.MouseFilterEnum.Ignore };
    private SearchTooltip.Binding? _active;
    private bool _popup;
    private bool _itemsPrepared;
    private string _shown = string.Empty;
    private int _item = -1;
    private double _elapsed;
    private Rect2 _previousRect;
    private readonly double _delay = ProjectSettings.GetSetting("gui/timers/tooltip_delay_sec", 0.5).AsDouble();

    internal SearchTooltipScope(Control owner, AnchoredTooltipHost host)
    {
        _owner = new(owner);
        _bounds = new(owner);
        _host = host;
        Name = "SearchTooltipScope";
        host.AddChild(_proxy);
        SetProcess(false);
    }

    internal void SetBounds(Control bounds) => _bounds = new(bounds);

    internal void Begin(SearchTooltip.Binding binding, bool popup)
    {
        Dismiss();
        _active = binding;
        _popup = popup;
        _itemsPrepared = false;
        SetProcess(true);
    }

    internal void End(SearchTooltip.Binding binding)
    {
        if (ReferenceEquals(binding, _active)) Dismiss();
    }

    internal bool Refresh(SearchTooltip.Binding binding)
    {
        if (!ReferenceEquals(binding, _active)) return false;
        _shown = string.Empty;
        _itemsPrepared = false;
        if (!_popup && string.IsNullOrWhiteSpace(binding.Text)) Dismiss();
        return true;
    }

    internal void Dismiss()
    {
        _host.Dismiss(_proxy);
        _active = null;
        _shown = string.Empty;
        _item = -1;
        _elapsed = 0;
        SetProcess(false);
    }

    public override void _ExitTree() => Dismiss();

    public override void _Process(double delta)
    {
        if (_active is not { } binding || !_owner.TryGetTarget(out var owner) ||
            !GodotObject.IsInstanceValid(owner) || !owner.IsVisibleInTree() ||
            !binding.Anchor.TryGetTarget(out var anchor) || !GodotObject.IsInstanceValid(anchor) ||
            !anchor.IsInsideTree() || anchor.IsQueuedForDeletion() || !anchor.IsVisibleInTree())
        { Dismiss(); return; }

        Rect2 anchorRect;
        string text;
        Vector2 pointer = _host.GetViewport().GetMousePosition();
        if (_popup && anchor is OptionButton option)
        {
            var popup = option.GetPopup();
            if (!popup.Visible) { Dismiss(); return; }
            if (!popup.IsEmbedded() || popup.IsNativeMenu())
            {
                foreach (var (index, value) in binding.Items)
                    if (index < option.ItemCount) option.SetItemTooltip(index, value);
                if (!binding.ExternalPopupReported)
                {
                    binding.ExternalPopupReported = true;
                    RuntimeLog.Warn("SearchTooltip: external/native popup keeps its existing native item tooltips; custom text tooltips support embedded popups.");
                }
                Dismiss(); return;
            }
            if (!_itemsPrepared)
            {
                foreach (int index in binding.Items.Keys)
                    if (index < option.ItemCount) option.SetItemTooltip(index, string.Empty);
                _itemsPrepared = true;
            }

            // PopupMenu.GetFocusedItem is the engine's mouse_over index, including
            // scroll offsets, disabled rows and separators. Do not guess row sizes.
            int item = popup.GetFocusedItem();
            var popupToViewport = _host.GetViewport().GetScreenTransform().AffineInverse() * popup.GetScreenTransform();
            Rect2 popupRect = popupToViewport * new Rect2(Vector2.Zero, popup.GetVisibleRect().Size);
            if (!popupRect.HasPoint(pointer) || item < 0 || !binding.Items.TryGetValue(item, out text!))
            { ResetHover(); return; }
            if (item != _item) { ResetHover(); _item = item; }
            // Anchor to the whole real menu, whose item geometry is not public.
            // This proxy lives in the host viewport, rather than the popup Window.
            anchorRect = popupRect;
        }
        else
        {
            text = binding.Text;
            anchorRect = anchor.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, anchor.Size);
            if (!anchorRect.HasPoint(pointer) || !InsideClippedParents(anchor, pointer))
            { Dismiss(); return; }
        }

        if (string.IsNullOrWhiteSpace(text)) { ResetHover(); return; }
        if (_bounds.TryGetTarget(out var bounds) && GodotObject.IsInstanceValid(bounds) && bounds.IsInsideTree())
        {
            Rect2 rect = bounds.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, bounds.Size);
            var viewportToCanvas = _host.GetCanvasTransform().AffineInverse();
            Rect2 canvasRect = viewportToCanvas * rect;
            Vector2 globalScale = (viewportToCanvas * bounds.GetGlobalTransformWithCanvas()).Scale.Abs();
            Vector2 parentScale = _host.GetParent() is CanvasItem parent ? parent.GetGlobalTransform().Scale.Abs() : Vector2.One;
            _host.Scale = globalScale / parentScale;
            _host.GlobalPosition = canvasRect.Position;
            _host.Size = canvasRect.Size / globalScale;
        }
        Rect2 proxyRect = _host.GetCanvasTransform().AffineInverse() * anchorRect;
        _proxy.GlobalPosition = proxyRect.Position;
        _proxy.Size = proxyRect.Size / _proxy.GetGlobalTransform().Scale.Abs();
        if (anchorRect != _previousRect) { _shown = string.Empty; _previousRect = anchorRect; }
        _elapsed += delta;
        if (_elapsed < _delay || string.Equals(text, _shown, StringComparison.Ordinal)) return;
        _shown = text;
        _host.ShowText(_proxy, text);
    }

    private void ResetHover()
    {
        _host.Dismiss(_proxy);
        _shown = string.Empty;
        _item = -1;
        _elapsed = 0;
    }

    private static bool InsideClippedParents(Control anchor, Vector2 pointer)
    {
        for (Node? node = anchor.GetParent(); node is Control control; node = node.GetParent())
            if (control.ClipContents && !(control.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, control.Size)).HasPoint(pointer))
                return false;
        return true;
    }
}
