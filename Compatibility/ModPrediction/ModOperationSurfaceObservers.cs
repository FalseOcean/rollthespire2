using System.Runtime.CompilerServices;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

namespace RolltheSpire2.Compatibility.ModPrediction;

// Optional modules discover their own controls; prediction lifecycle remains
// with the common hover controller. Native controls keep their existing hooks.
internal static class ModOperationSurfaceObservers
{
    private static readonly ConditionalWeakTable<Control, SurfaceBinding> Bindings = new();
    private static SceneTree? _tree;
    private static string _version = "";

    internal static void Attach(SceneTree tree, string version)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _version = version;
        if (ReferenceEquals(_tree, tree)) return;
        if (_tree != null && GodotObject.IsInstanceValid(_tree)) _tree.NodeAdded -= NodeAdded;
        _tree = tree;
        tree.NodeAdded += NodeAdded;
        Discover(tree.Root);
    }

    private static void Discover(Node node)
    {
        NodeAdded(node);
        foreach (Node child in node.GetChildren()) Discover(child);
    }

    private static void NodeAdded(Node node)
    {
        if (node is not Control control) return;
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree() || control.IsQueuedForDeletion()) return;
            foreach (var module in ModOperationPredictions.Modules)
            {
                try { module.BindSurface?.Invoke(control, _version); }
                catch (Exception error)
                {
                    RuntimeLog.Fault("operationUiFailed=true;stage=mod-surface-binding;module=" + module.Id +
                        ";control=" + control.GetType().FullName, error);
                }
            }
        }).CallDeferred();
    }

    internal static void Observe(Control control, string version)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (Bindings.TryGetValue(control, out _)) return;
        var binding = new SurfaceBinding(control, version);
        Bindings.Add(control, binding);
        binding.Refresh();
    }

    private sealed class SurfaceBinding
    {
        private readonly Control _owner;
        private readonly string _version;
        private bool _hovered, _focused, _pressed;

        internal SurfaceBinding(Control owner, string version)
        {
            _owner = owner; _version = version;
            _focused = owner.HasFocus();
            _hovered = owner.IsVisibleInTree() && owner.GetGlobalRect().HasPoint(owner.GetGlobalMousePosition());
            owner.MouseEntered += () => { _hovered = true; Refresh(); };
            owner.MouseExited += () => { _hovered = false; Refresh(); };
            owner.FocusEntered += () => { _focused = true; Refresh(); };
            owner.FocusExited += () => { _focused = false; Refresh(); };
            owner.TreeExiting += () => { _hovered = false; _focused = false; Refresh(); };
            if (owner is BaseButton button)
            {
                button.ButtonDown += () => { _pressed = true; Refresh(); };
                button.ButtonUp += () => { _pressed = false; Refresh(); };
            }
        }

        internal void Refresh()
        {
            try
            {
                if (!_pressed && (_hovered || _focused)) NeowOperationHoverController.Focus(_owner, _version);
                else NeowOperationHoverController.Leave(_owner);
            }
            catch (Exception error)
            {
                RuntimeLog.Fault("operationUiFailed=true;stage=mod-surface-focus;control=" + _owner.GetType().FullName, error);
            }
        }
    }
}
