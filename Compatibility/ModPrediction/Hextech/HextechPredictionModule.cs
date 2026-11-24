using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

internal static class HextechPredictionModule
{
    internal static ModPredictionModule Create() => new("HextechRunes", [new ShopForge(), new RuneReroll()])
    { BindSurface = BindSurface };

    private static void BindSurface(Control owner, string version)
    {
        if (owner is Button && HextechRuneRerollCapture.IsReroll(owner))
            ModOperationSurfaceObservers.Observe(owner, version);
    }

    private sealed class RuneReroll : IModOperationPrediction
    {
        public OperationPredictionEntry Setting { get; } = new(HextechRuneRerollOperationPreview.FeatureId,
            OperationPredictionCategory.RelicEffect, NameKey: "settings.operation.entry.hextech:selection-reroll");

        public bool TryMatch(Control owner, out RunState? run, out ModelKey source, out long identity) =>
            HextechRuneRerollCapture.TryMatch(owner, out run, out source, out identity);

        public ModOperationSnapshot Capture(Control owner, RunState run) =>
            new RerollSnapshot(HextechRuneRerollCapture.Capture(owner, run));

        public bool WantsPrediction(OperationPredictionMode mode, RandomForeseerSnapshot other) =>
            mode != OperationPredictionMode.Off;
    }

    private sealed record RerollSnapshot(HextechRuneRerollSnapshot Data) : ModOperationSnapshot(Data.Fingerprint)
    {
        internal override OperationPreview Predict(CancellationToken token) =>
            HextechRuneRerollOperationPreview.Build(Data, token);
    }

    private sealed class ShopForge : IModOperationPrediction
    {
        public OperationPredictionEntry Setting { get; } = new(HextechShopForgeOperationPreview.FeatureId,
            OperationPredictionCategory.RelicEffect, NameKey: "settings.operation.entry.shop:hextech-forge");

        public bool TryMatch(Control owner, out RunState? run, out ModelKey source, out long identity)
        {
            run = null; source = default; identity = 0;
            return owner is NMerchantRelic { Entry: MerchantRelicEntry { Model: { } relic } } slot &&
                HextechShopForgeCapture.IsForge(relic) &&
                OperationSurfaceHoverCapture.TryShop(slot, out run, out identity, out source);
        }

        public ModOperationSnapshot Capture(Control owner, RunState run)
        {
            if (!TryMatch(owner, out var current, out _, out _) || !ReferenceEquals(current, run))
                throw new InvalidOperationException("HextechShopForgeMerchantChanged");
            var relic = ((MerchantRelicEntry)((NMerchantRelic)owner).Entry).Model;
            return new Snapshot(HextechShopForgeCapture.Capture(run, relic!));
        }

        // RF has no equivalent for this particular generated offer.
        public bool WantsPrediction(OperationPredictionMode mode, RandomForeseerSnapshot other) =>
            mode != OperationPredictionMode.Off;
    }

    private sealed record Snapshot(HextechShopForgeSnapshot Data) : ModOperationSnapshot(Data.Fingerprint)
    {
        internal override OperationPreview Predict(CancellationToken token) =>
            HextechShopForgeOperationPreview.Build(Data, token);
    }
}
