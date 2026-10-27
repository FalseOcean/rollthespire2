using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using RolltheSpire2.Ui.InRunOperations;

namespace RolltheSpire2.Bootstrap;

// Hover observers preserve native selection/acquisition. The confirmation
// prefix changes only NTransformPreview's display argument, never its caller.
internal static class NeowOperationPredictionPatch
{
    internal static void Install(Harmony harmony)
    {
        foreach (var type in new[] { typeof(NEventOptionButton), typeof(NMerchantSlot), typeof(NRestSiteButton),
            typeof(NRewardButton), typeof(NCardRewardAlternativeButton), typeof(NPotionHolder), typeof(NCardHolder) })
        {
            Observe(type, "OnFocus", nameof(Focused));
            Observe(type, "OnUnfocus", nameof(Left));
        }
        foreach (var type in new[] { typeof(NEventOptionButton), typeof(NRestSiteButton), typeof(NRewardButton), typeof(NPotionHolder) })
            Observe(type, "OnRelease", nameof(Left));
        Observe(typeof(NCardRewardAlternativeButton), "OnPress", nameof(Left));
        Observe(typeof(NMerchantSlot), "OnMouseReleased", nameof(Left));
        Observe(typeof(NCardHolder), "OnMouseReleased", nameof(Left));
        // Observe the concrete native entry as well as the base observer. This
        // records admission without starting a second prediction or selection.
        Observe(typeof(NGridCardHolder), "OnFocus", nameof(GridCardFocused));
        foreach (var type in new[] { typeof(NEventOptionButton), typeof(NRestSiteButton), typeof(NMerchantSlot), typeof(NPotionHolder), typeof(NGridCardHolder) })
            Observe(type, "_ExitTree", nameof(Left));
        harmony.Patch(AccessTools.Method(typeof(NTransformPreview), nameof(NTransformPreview.Initialize))
            ?? throw new MissingMethodException(typeof(NTransformPreview).FullName, nameof(NTransformPreview.Initialize)),
            prefix: new HarmonyMethod(typeof(NeowOperationPredictionPatch), nameof(TransformConfirmation)),
            postfix: new HarmonyMethod(typeof(NeowOperationPredictionPatch), nameof(TransformConfirmationFinished)));
        void Observe(Type type, string native, string observer) => harmony.Patch(
            AccessTools.Method(type, native) ?? throw new MissingMethodException(type.FullName, native),
            postfix: new HarmonyMethod(typeof(NeowOperationPredictionPatch), observer));
    }

    private static void TransformConfirmation(NTransformPreview __instance, ref IEnumerable<CardTransformation> cardTransformations,
        out Exception? __state)
    {
        __state = null;
        try
        {
            if (TransformConfirmationPreview.PredictionEnabled && TransformConfirmationPreview.IsSupported(__instance))
                TransformConfirmationPreview.Prepare(__instance, ref cardTransformations);
        }
        catch (Exception error)
        {
            // Only a confirmed unsupported source keeps the native display.
            // Discovery failure must not enumerate its lazy demo delegate.
            __state = error;
            cardTransformations = [];
            RuntimeLog.Fault("operationUiFailed=true;stage=transformConfirmation;owner=NTransformPreview", error);
        }
    }

    private static void TransformConfirmationFinished(NTransformPreview __instance, Exception? __state)
    {
        if (__state == null) return;
        try { TransformConfirmationPreview.ShowUnavailable(__instance); }
        catch (Exception error) { RuntimeLog.Fault("operationUiFailed=true;stage=transformConfirmationNotice;owner=NTransformPreview", error); }
    }

    private static void Focused(Control __instance)
    {
        try { NeowOperationHoverController.Focus(__instance, ModRuntime.Snapshot.Detection.NormalizedVersion); }
        catch (Exception error) { RuntimeLog.Fault("operationUiFailed=true;stage=focus;owner=" + __instance.GetType().Name, error); }
    }
    private static void GridCardFocused(NGridCardHolder __instance)
    {
        try { NeowOperationHoverController.ObserveGridFocus(__instance); }
        catch (Exception error) { RuntimeLog.Fault("operationUiFailed=true;stage=gridFocusObservation", error); }
    }
    private static void Left(Control __instance)
    {
        try { NeowOperationHoverController.Leave(__instance); }
        catch (Exception error) { RuntimeLog.Fault("operationUiFailed=true;stage=leave;owner=" + __instance.GetType().Name, error); }
    }
}
