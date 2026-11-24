using System.Runtime.CompilerServices;
using Godot;

namespace RolltheSpire2.Ui.Tooltips;

/// <summary>Explicit text bindings for Search. Other workspace pages keep their own tooltips.</summary>
internal static class SearchTooltip
{
    private sealed record ScopeReference(WeakReference<SearchTooltipScope>? Scope, WeakReference<Control>? Owner);
    private static readonly ConditionalWeakTable<Control, ScopeReference> Scopes = new();
    private static readonly ConditionalWeakTable<AnchoredTooltipHost, SearchTooltipScope> Hosts = new();
    private static readonly ConditionalWeakTable<Control, Binding> Bindings = new();

    internal static void AttachScope(Control root, AnchoredTooltipHost host, Control? bounds = null)
    {
        var scope = Hosts.GetValue(host, value =>
        {
            var created = new SearchTooltipScope(root, value);
            value.AddChild(created);
            return created;
        });
        scope.SetBounds(bounds ?? root);
        Scopes.Remove(root);
        Scopes.Add(root, new(new(scope), null));
    }

    // Overlay registration survives Reparent without registering the whole workspace shell.
    internal static void AttachScope(Control root, Control owner)
    {
        Scopes.Remove(root);
        Scopes.Add(root, new(null, new(owner)));
    }

    internal static void DetachScope(Control root)
    {
        Resolve(root)?.Dismiss();
        Scopes.Remove(root);
    }

    internal static bool IsScoped(Control anchor) => Resolve(anchor) is not null;

    internal static void Set(Control anchor, string text)
    {
        var binding = Bindings.GetValue(anchor, value => new Binding(value));
        binding.Text = text ?? string.Empty;
        // Controls are commonly authored before AddChild. Preserve their text until
        // entering an explicit Search scope; MouseEntered clears the native copy.
        anchor.TooltipText = Resolve(anchor) is null ? binding.Text : string.Empty;
        binding.Changed();
    }

    internal static bool SetIfScoped(Control anchor, string text)
    {
        // Shared tiles bind while still parentless. Register the deferred binding
        // in both cases; entering a non-Search parent retains the native text.
        bool scoped = IsScoped(anchor);
        Set(anchor, text);
        return scoped;
    }

    internal static void SetItem(OptionButton option, int index, string text)
    {
        var binding = Bindings.GetValue(option, value => new Binding(value));
        binding.Items[index] = text ?? string.Empty;
        // The popup's Window type is known when it opens. Until then keep the
        // original string, including for a native/external-window presentation.
        option.SetItemTooltip(index, text);
        binding.Changed();
    }

    private static SearchTooltipScope? Resolve(Control anchor, int redirects = 0)
    {
        if (redirects > 16 || !GodotObject.IsInstanceValid(anchor)) return null;
        for (Node? node = anchor; node is not null; node = node.GetParent())
        {
            if (node is not Control control || !Scopes.TryGetValue(control, out var reference)) continue;
            if (reference.Scope is { } scope && scope.TryGetTarget(out var found) && GodotObject.IsInstanceValid(found))
                return found;
            if (reference.Owner is { } owner && owner.TryGetTarget(out var parent) && GodotObject.IsInstanceValid(parent))
                return Resolve(parent, redirects + 1);
        }
        return null;
    }

    internal sealed class Binding
    {
        internal readonly WeakReference<Control> Anchor;
        internal string Text = string.Empty;
        internal readonly Dictionary<int, string> Items = [];
        private WeakReference<SearchTooltipScope>? _activeScope;
        private bool _popupOpen;
        private bool _hovered;
        internal bool ExternalPopupReported;

        internal Binding(Control anchor)
        {
            Anchor = new(anchor);
            anchor.MouseEntered += Enter;
            anchor.MouseExited += () => { _hovered = false; if (!_popupOpen) Leave(); };
            anchor.TreeExiting += () => { _hovered = false; Leave(); };
            anchor.VisibilityChanged += () => { if (!anchor.IsVisibleInTree()) { _hovered = false; Leave(); } };
            if (anchor is OptionButton option)
            {
                option.GetPopup().AboutToPopup += () =>
                {
                    if (Resolve(option) is not { } scope) return;
                    _popupOpen = true;
                    _activeScope = new(scope);
                    scope.Begin(this, popup: true);
                };
                option.GetPopup().PopupHide += () => { _popupOpen = false; Leave(); };
            }
        }

        private void Enter()
        {
            _hovered = true;
            if (!Anchor.TryGetTarget(out var anchor) || !GodotObject.IsInstanceValid(anchor)) return;
            if (Resolve(anchor) is not { } scope) { anchor.TooltipText = Text; return; }
            anchor.TooltipText = string.Empty;
            _activeScope = new(scope);
            scope.Begin(this, popup: false);
        }

        internal void Changed()
        {
            if (_activeScope is { } reference && reference.TryGetTarget(out var scope) && GodotObject.IsInstanceValid(scope))
            {
                if (scope.Refresh(this)) return;
            }
            if (_hovered && !string.IsNullOrWhiteSpace(Text)) Enter();
        }

        private void Leave()
        {
            if (_activeScope is { } reference && reference.TryGetTarget(out var scope) && GodotObject.IsInstanceValid(scope))
                scope.End(this);
            _activeScope = null;
        }
    }
}
