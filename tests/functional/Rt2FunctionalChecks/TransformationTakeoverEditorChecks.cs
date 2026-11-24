using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class TransformationTakeoverEditorChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Transformation editor: " + description);
            checks++;
        }

        var text = JsonUiTextProvider.CreateUi13("zh");
        var json = SearchPresetStore.CreateJsonOptions();
        ModelKey card = new("CARD", "CLAW"), grove = new("EVENT", "MORPHIC_GROVE");
        var pandora = new TransformationSearchSource(TransformationSearchSource.PandoraId, new("RELIC", "PANDORAS_BOX"), 0) { AncientAct = 2 };
        var lab = new TransformationSearchSource(TransformationSearchSource.AstrolabeId, new("RELIC", "ASTROLABE"), 3) { AncientAct = 2 };
        var leafy = new TransformationSearchSource("N.LeafyPoultice", BaseGameModelKeys.Relics.LeafyPoultice, 2);
        var eventSource = new TransformationSearchSource("E.MORPHIC_GROVE", grove, 2) { EventAct = 1, HasIndependentResult = true };
        TransformationSearchSource[] sources = [leafy, eventSource, pandora, lab];
        var query = SearchQuery.Empty with
        {
            OpeningRoute = new(leafy.Identity),
            EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 3, new([grove], [], []))],
            EventResultConditions = [new(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, card)]
        };

        Action<ModelKey>? chooseCard = null;
        var editor = new TransformationSearchEditor((_, choose) => chooseCard = choose) { Size = new(780, 620) };
        try
        {
            T Node<T>(string name) where T : Node => (T)(editor.FindChild(name, true, false)
                ?? throw new InvalidOperationException("Transformation editor missing control " + name));
            void Press(string name) => Node<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
            void AddCard()
            {
                Node<Control>("TransformationAddTarget").GetChild<Button>(0).EmitSignal(BaseButton.SignalName.Pressed);
                (chooseCard ?? throw new InvalidOperationException("Transformation card picker did not open"))(card);
            }
            void Refresh() => editor.Refresh("zh", text, 0, sources, [card], startingStrikeDefendCount: 9, openingRelics: [leafy.Identity]);

            Refresh();
            Check(editor.FindChild("TransformationAddGroup", true, false) is null &&
                editor.FindChild("TransformationGroupSelector", true, false) is null,
                "One combination does not ask the player to create or select result groups");
            Check(editor.SetSourceTakenOver(pandora.Id, true) && !editor.SetSourceTakenOver(lab.Id, true), "Ancient pickups are mutually exclusive");
            var initial = editor.ExportCondition(query)!;
            Check(initial is { AncientSource: TransformationAncientSource.PandorasBox, AncientAct: 2, PandoraAutomaticCount: 5, UsesNeow: false, UsesEvents: false },
                "Unmanaged opening and earlier event transform reduce the automatic count");
            sources[2] = pandora with { PandoraCountOverride = 13 };
            Refresh();
            Check(editor.ExportCondition(query) is { PandoraCountOverride: 13, OpportunityCount: 13 }, "Manual count can exceed the automatic count");
            sources[2] = pandora;
            Refresh();
            Check(editor.ExportCondition(query) is { PandoraCountOverride: null, OpportunityCount: 5 }, "Restore automatic clears the manual premise");

            Press("TransformationObjective1");
            AddCard();
            AddCard();
            Node<MapQuantityInput>("TransformationCardsRareCount").Value = 2;
            Check(editor.SetSourceTakenOver(leafy.Id, true) && !editor.SetSourceTakenOver(lab.Id, true), "Multiple selected sources share one takeover");
            var grouped = editor.ExportCondition(query)!;
            Check(grouped.ResultRequirements.Count == 1 && grouped.ResultRequirements[0] is
                    { Predicate: TransformationAggregatePredicate.ContainsMultisetAndRareCount, MinimumRareCount: 2 } and var named &&
                named.Sources.Order().SequenceEqual(new[] { pandora.Id, leafy.Id }.Order()) && named.TargetMultiset.SequenceEqual([card, card]),
                "One takeover checks repeated cards and rarity across all selected sources");

            string snapshot = JsonSerializer.Serialize(editor.ExportEditorState(), json);
            string authored = JsonSerializer.Serialize(query with { TransformationAggregate = grouped }, json);
            editor.ImportQuery(JsonSerializer.Deserialize<SearchQuery>(authored, json)!);
            editor.ImportEditorState(JsonSerializer.Deserialize<TransformationEditorStateSnapshot>(snapshot, json)!);
            Refresh();
            Check(JsonSerializer.Serialize(editor.ExportEditorState(), json) == snapshot, "Scoped editor state round-trips");
            Check(JsonSerializer.Serialize(query with { TransformationAggregate = editor.ExportCondition(query) }, json) == authored,
                "Scoped query round-trips");
            var oldSaved = new TransformationEditorStateSnapshot([pandora.Id], 1, 1, [card, card])
            {
                AdditionalGroups = [new([leafy.Id], 0, 1, [])], ExplicitRequirements = true
            };
            editor.ImportEditorState(oldSaved);
            Refresh();
            Check(editor.ExportCondition(query)!.ResultRequirements.Count == 2 &&
                editor.FindChild("TransformationAddGroup", true, false) is null &&
                editor.FindChild("TransformationGroupSelector", true, false) is null,
                "Older independent requirements stay active without restoring group management");
            Press("TransformationRemoveSavedRequirement0");
            Check(!editor.IsTakenOver(0, leafy.Id) && editor.IsTakenOver(0, pandora.Id) &&
                editor.ExportCondition(query)!.ResultRequirements.Count == 1, "An explicit removal releases only the saved extra requirement");

            var bones = BaseGameModelKeys.Relics.NeowsBones;
            ModelKey[] openingPair = [BaseGameModelKeys.Relics.PrecariousShears, leafy.Identity];
            var shearsQuery = SearchQuery.Empty with
            {
                OpeningRoute = new(bones),
                OpeningRouteRelicRequirement = new(bones, openingPair)
            };
            editor.ImportQuery(shearsQuery);
            editor.Refresh("zh", text, 0, [pandora, lab], [card], startingStrikeDefendCount: 9, openingRelics: openingPair);
            Check(editor.GetAutomaticPandoraCount(pandora) == 7 &&
                editor.ExportCondition(shearsQuery, [new(pandora, [card])]) is { AncientOpportunityCount: 7, PandoraCountOverride: null } &&
                editor.ExportCondition(shearsQuery, [new(lab, [card])]) is { AncientOpportunityCount: 3 },
                "The Shears and Leafy Poultice opening exports the displayed Pandora count or three Astrolabe rolls");
            var joint = new TransformationAggregateCondition(TransformationOpening.BonesLeafyNewLeaf, TransformationPickupOrder.Any,
                false, false, false, TransformationAggregatePredicate.ContainsMultiset, 0, [card]);
            var jointQuery = SearchQuery.Empty with
            {
                OpeningRoute = new(bones),
                OpeningRouteRelicRequirement = new(bones, [leafy.Identity, BaseGameModelKeys.Relics.NewLeaf]),
                TransformationAggregate = joint
            };
            editor.ImportQuery(jointQuery);
            editor.Refresh("zh", text, 0, [lab], [card], startingStrikeDefendCount: 9);
            Check(editor.SetSourceTakenOver(lab.Id, true), "Astrolabe takeover attaches to the authored Ancient option");
            var merged = editor.ExportCondition(jointQuery)!;
            Check(merged.ResultRequirements.Count == 2 && merged.ResultRequirements[1].Sources.SequenceEqual(["N.BonesLeafyNewLeaf"]) &&
                !editor.IsTakenOver(0, "N.BonesLeafyNewLeaf"), "An independent Neow joint target survives Ancient takeover");
        }
        finally { editor.Free(); }
        Console.WriteLine($"TRANSFORMATION_TAKEOVER_EDITOR_CHECKS_OK {checks}");
        return AncientTransformationLocalEditorChecks.Run();
    }
}
