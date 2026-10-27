using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.Shell;

internal static class CrystalSphereMaskAppearance
{
    internal const float Opacity = .35f;
    private static bool? _enabled;
    // First native mask Ready restores the saved appearance even before opening
    // the assistant. Preference capture and mask changes stay on the main thread.
    internal static bool Enabled
    {
        get
        {
            _enabled ??= InRunFeaturePreferences.CrystalSphereTransparentMask;
            return _enabled.Value;
        }
    }
    private sealed class TrackedMask(NCrystalSphereMask mask)
    {
        internal readonly WeakReference<NCrystalSphereMask> Node = new(mask);
        internal float? OriginalAlpha;
    }
    private static readonly List<TrackedMask> Masks = [];

    // Called only on the main thread. The panel separately saves the user's choice.
    internal static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        _enabled = enabled;
        for (int i = Masks.Count - 1; i >= 0; i--)
        {
            if (!Masks[i].Node.TryGetTarget(out var mask) || !GodotObject.IsInstanceValid(mask))
                Masks.RemoveAt(i);
            else Apply(Masks[i], mask);
        }
    }

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(NCrystalSphereMask), nameof(NCrystalSphereMask._Ready)),
        postfix: new HarmonyMethod(typeof(CrystalSphereMaskAppearance), nameof(ReadyPostfix)) { priority = Priority.Last });

    private static void ReadyPostfix(NCrystalSphereMask __instance)
    {
        Masks.RemoveAll(entry => !entry.Node.TryGetTarget(out var node) || !GodotObject.IsInstanceValid(node));
        var tracked = Masks.FirstOrDefault(entry => entry.Node.TryGetTarget(out var node) && ReferenceEquals(node, __instance));
        if (tracked == null) { tracked = new(__instance); Masks.Add(tracked); }
        Apply(tracked, __instance);
    }

    private static void Apply(TrackedMask tracked, NCrystalSphereMask mask)
    {
        // Default-off is a strict no-write. On disable, restore only our alpha,
        // never RGB or an alpha subsequently changed by another mod/animation.
        var color = mask.SelfModulate;
        if (Enabled)
        {
            if (tracked.OriginalAlpha == null && color.A > Opacity)
            {
                tracked.OriginalAlpha = color.A;
                color.A = Opacity;
                mask.SelfModulate = color;
            }
        }
        else if (tracked.OriginalAlpha is { } original)
        {
            if (color.A == Opacity) { color.A = original; mask.SelfModulate = color; }
            tracked.OriginalAlpha = null;
        }
    }
}
