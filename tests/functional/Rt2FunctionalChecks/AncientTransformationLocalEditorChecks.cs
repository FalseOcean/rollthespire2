using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class AncientTransformationLocalEditorChecks
{
    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("AncientTransformationLocalEditor");
        runtime = runtime with { Detection = new("0.113.1", "0.113.1", "beta", "AncientTransformationLocalEditor", true, "") };
        var ancient = new AncientSearchEditor(runtime) { Size = new(1000, 780) };
        var transform = new TransformationSearchEditor((_, _) => { }) { Size = new(780, 620) };
        var text = JsonUiTextProvider.CreateUi13("zh");
        var json = SearchPresetStore.CreateJsonOptions();
        var card = new ModelKey("CARD", "OFFERING");
        var box = new ModelKey("RELIC", "PANDORAS_BOX");
        var query = SearchQuery.Empty with { AncientBranches = [new(2, new("EVENT", "DARV"), [box], [])] };
        var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(runtime.Profile, "000000000000",
            CharacterIdentity.FromKey(BaseGameModelKeys.Characters.Ironclad), 10, "0.113.1",
            predictionGameMode: WorldGameMode.Singleplayer, predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest,
            explicitUnlockState: UnlockState.all,
            explicitDiscoveredActs: [new("ACT", "OVERGROWTH"), new("ACT", "UNDERDOCKS"), new("ACT", "HIVE"), new("ACT", "GLORY")]);
        var shared = new EditorRefreshContext("AncientLocal113FullUnlock", UnlockState.all.ToSerializable(), authority);
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Ancient local transformations: " + message);
            checks++;
        }
        T Control<T>(string name) where T : Node => (T)(ancient.FindChild(name, true, false)
            ?? throw new InvalidOperationException("Ancient local transformation control missing: " + name));
        void Refresh()
        {
            ancient.Refresh("zh", text, BaseGameModelKeys.Characters.Ironclad, 10, 1, 0, render: false, sharedContext: shared);
            transform.Refresh("zh", text, 0, ancient.TransformationSources().ToArray(), [card],
                startingStrikeDefendCount: 8);
            ancient.FocusTransformationSource(2);
        }
        TransformationAggregateCondition? Export() => transform.ExportCondition(query, ancient.ExportTransformationResults());
        try
        {
            Action<ModelKey>? choose = null;
            ancient.PickTransformationTarget = callback => choose = callback;
            ancient.TransformTakenOver = (seat, source) => transform.IsTakenOver(seat, source);
            ancient.AutomaticPandoraCount = source => transform.GetAutomaticPandoraCount(source);
            ancient.Refresh("zh", text, BaseGameModelKeys.Characters.Ironclad, 10, 1, 0, render: false, sharedContext: shared);
            ancient.ImportQuery(query, AncientOptionConditionProfile.BroadDefault);
            Refresh();
            Check(ancient.FindChild("TransformationObjective0", true, false) is null &&
                ancient.FindChild("TransformationObjective2", true, false) is null,
                "Original Ancient location offers named cards, not rarity modes");
            Check(ancient.FindChild("AncientTransformationPremise", true, false) is null,
                "An offered relic alone does not show result premises");
            for (int index = 0; index < 3; index++)
            {
                Control<Control>("AncientTransformationAddTarget_PANDORAS_BOX").GetChild<Button>(0)
                    .EmitSignal(BaseButton.SignalName.Pressed);
                (choose ?? throw new InvalidOperationException("Original-location picker did not open"))(card);
            }
            Check(Export() is { AncientSource: TransformationAncientSource.PandorasBox,
                    Predicate: TransformationAggregatePredicate.ContainsMultiset, MinimumRareCount: 0 } local &&
                local.TargetMultiset.SequenceEqual([card, card, card]) && !transform.IsTakenOver(0, TransformationSearchSource.PandoraId),
                "Three same-name targets participate directly without takeover");
            Check(Control<Label>("AncientTransformationPremise").Text == text.Get("query.transform.ancient.premise.pandora"),
                "Only the selected relic’s premise appears after a local target is set");
            Check(Control<MapQuantityInput>("TransformationPandoraCount").Value == 8,
                "Automatic material quantity is displayed at Pandora's original location");
            Control<MapQuantityInput>("TransformationPandoraCount").Value = 13;
            Refresh();
            Check(Export() is { PandoraCountOverride: 13, OpportunityCount: 13 }, "Original quantity supplies the query premise");
            Control<Button>("TransformationPandoraRestoreAutomatic").EmitSignal(BaseButton.SignalName.Pressed);
            Refresh();
            Check(Export() is { PandoraCountOverride: null, OpportunityCount: 8 }, "Restore-auto is shared with combination capacity");

            string saved = JsonSerializer.Serialize(ancient.ExportEditorState(), json);
            Check(transform.SetSourceTakenOver(TransformationSearchSource.PandoraId, true), "Combination can take over an authored local source");
            Refresh();
            Check(Export() is { Predicate: TransformationAggregatePredicate.RareCountAtLeast, MinimumRareCount: 1 } &&
                ancient.FindChild("AncientTransformationAddTarget_PANDORAS_BOX", true, false) is null,
                "Takeover replaces local participation without exposing two active result editors");
            Check(ancient.FindChild("AncientTransformationPremise", true, false) is null &&
                transform.FindChild("TransformationAncientPremise", true, false) is Label takenOverPremise &&
                takenOverPremise.Text == text.Get("query.transform.ancient.premise.pandora"),
                "The combination alone shows the premise after takeover");
            Check(transform.SetSourceTakenOver(TransformationSearchSource.PandoraId, false), "Takeover can be released");
            Refresh();
            Check(Export()!.TargetMultiset.SequenceEqual([card, card, card]) &&
                JsonSerializer.Serialize(ancient.ExportEditorState(), json) == saved,
                "Releasing takeover restores all original repeated targets unchanged");

            var roundtrip = JsonSerializer.Deserialize<AncientEditorStateSnapshot>(saved, json)!;
            ancient.ImportQuery(query with { TransformationAggregate = Export() }, AncientOptionConditionProfile.BroadDefault, roundtrip);
            Refresh();
            Check(JsonSerializer.Serialize(ancient.ExportEditorState(), json) == saved &&
                Export()!.TargetMultiset.SequenceEqual([card, card, card]), "Local requirements survive save/reopen");
            var undo = ancient.ClearConditions(false);
            Check(!ancient.HasConditions(false), "Clear includes original-location requirements");
            undo();
            Refresh();
            Check(JsonSerializer.Serialize(ancient.ExportEditorState(), json) == saved,
                "Undo restores original-location requirements and quantity");

            ancient.ImportQuery(query, AncientOptionConditionProfile.BroadDefault);
            transform.ImportEditorState(new([TransformationSearchSource.PandoraId], 1, 1, [card, card, card])
                { PandoraCountOverride = 13, ExplicitRequirements = true });
            Refresh();
            transform.MigrateLegacyRequirements(2, ancient.TryRestorePandoraCount,
                (source, targets) => ancient.TryRestoreTransformationTargets(source.Id, source.AncientAct, targets));
            Refresh();
            Check(!transform.IsTakenOver(0, TransformationSearchSource.PandoraId) &&
                transform.ExportEditorState().PandoraCountOverride is null &&
                Export() is { PandoraCountOverride: 13 } migrated && migrated.TargetMultiset.SequenceEqual([card, card, card]),
                "A single old named group and its count migrate without changing the requested outputs");
            Control<Button>("TransformationPandoraRestoreAutomatic").EmitSignal(BaseButton.SignalName.Pressed);
            Refresh();
            Check(Export() is { PandoraCountOverride: null, OpportunityCount: 8 },
                "Restore-auto after migration cannot resurrect the old combination's override");

            var anyActThree = query with { AncientBranches = [.. query.AncientBranches,
                new(3, new("EVENT", "DARV"), [box], []), new(3, new("EVENT", "NONUPEIPE"), [], [])] };
            ancient.ImportQuery(anyActThree, AncientOptionConditionProfile.BroadDefault);
            Check(ancient.TryRestoreTransformationTargets(TransformationSearchSource.PandoraId, 2, [card]),
                "The local target belongs to its authored Ancient act");
            Refresh();
            Check(ancient.FindChild("AncientTransformationAddTarget_PANDORAS_BOX", true, false) is not null,
                "The local editor stays available when the same option occurs among another act's alternatives");
            Check(transform.ExportCondition(ancient.ExportQuery(SearchQuery.Empty), ancient.ExportTransformationResults()) is
                { AncientAct: 2, AncientSource: TransformationAncientSource.PandorasBox } bound && bound.TargetMultiset.SequenceEqual([card]),
                "An Act 3 Darv alternative does not remove Act 2 local Ancient results from the search");
        }
        finally { ancient.Free(); transform.Free(); }
        Console.WriteLine($"ANCIENT_TRANSFORMATION_LOCAL_EDITOR_CHECKS_OK {checks}");
        return 0;
    }
}
