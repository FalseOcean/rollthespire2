using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

internal static class EventOpportunityEditorChecks
{
    internal static int Run()
    {
        var baseline = NativeWorkbenchCheckEnvironment.Initialize("EventOpportunityEditor");
        var runtime = baseline with { Detection = baseline.Detection with
            { RawVersion = "0.113.1", NormalizedVersion = "0.113.1" } };
        var editor = new EventSearchEditor(runtime) { Size = new(1000, 820) };
        var reopened = new EventSearchEditor(runtime) { Size = new(1000, 820) };
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.Root.AddChild(editor);
        tree.Root.AddChild(reopened);
        var json = SearchPresetStore.CreateJsonOptions();
        int checks = 0;
        void Check(bool ok, string name)
        {
            checks++;
            if (!ok) throw new InvalidDataException("EventOpportunityEditor." + name);
        }

        try
        {
            void Refresh(EventSearchEditor target, string language) =>
                target.Refresh(language, JsonUiTextProvider.CreateUi13(language), BaseGameModelKeys.Characters.Ironclad,
                    10, 1, 0, render: false);
            void PressChoice(string name)
            {
                var button = editor.FindChild(name, true, false) as BaseButton;
                Check(button is not null, "ChoiceExists:" + name);
                button!.EmitSignal(BaseButton.SignalName.Pressed);
            }
            void PressLabel(string caption)
            {
                var button = editor.FindChildren("*", "Button", true, false).OfType<Button>()
                    .SingleOrDefault(choice => choice.Text == caption);
                Check(button is not null, "ChoiceLabelExists:" + caption);
                button!.EmitSignal(BaseButton.SignalName.Pressed);
            }
            var doll = Event(EventOpportunityCatalog.DollRoom);
            var museum = Event(EventOpportunityCatalog.MuseumOfTorture);
            var thermae = Event(EventOpportunityCatalog.ThermaeNonupeipe);
            var crystal = Event(EventOpportunityCatalog.CrystalSphere);
            var conditions = new List<EventResultSearchCondition>
            {
                new(EventResultConditionKind.DollRoomRandomRelic, EventOpportunityCatalog.Dolls[0]),
                new(EventResultConditionKind.DollRoomTakeSomeTimeRelic, EventOpportunityCatalog.Dolls[2]),
                new(EventResultConditionKind.MuseumOfTortureOfferedRelic, EventOpportunityCatalog.MuseumRelics[1]),
                new(EventResultConditionKind.ThermaeNonupeipeDrinkRelic, EventOpportunityCatalog.ThermaeRelics[^1]),
                new(EventResultConditionKind.CrystalSphereRewards, crystal)
                {
                    CrystalDivinations = 6,
                    CrystalCards = CrystalSphereSeedCards.Common | CrystalSphereSeedCards.Rare,
                    RelicRarity = EffectRelicRarity.Uncommon
                }
            };
            conditions.AddRange(EventOpportunityCatalog.RarityBranches.Select(opportunity => new EventResultSearchCondition(
                EventResultConditionKind.EventRelicRarityRoll, opportunity.EventKey)
            {
                RelicRarityBranch = opportunity.Branch,
                RelicRaritySelection = opportunity.RollCount > 1 ? EventRelicRaritySelection.Any : EventRelicRaritySelection.First,
                RelicRarity = EffectRelicRarity.Rare
            }));
            var trade = EventOpportunityCatalog.RarityBranches.Single(c => c.Branch == EventRelicRarityBranch.TradeOffers).EventKey;
            conditions.Add(new(EventResultConditionKind.EventRelicRarityRoll, trade)
            {
                RelicRarityBranch = EventRelicRarityBranch.TradeOffers,
                RelicRaritySelection = EventRelicRaritySelection.Third,
                RelicRarity = EffectRelicRarity.Common
            });
            var query = SearchQuery.Empty with { EventResultConditions = conditions.ToArray() };
            Check(conditions.All(condition => condition.IsValid), "FixtureConditionShapes");
            typeof(QueryWorkbenchFrame).GetMethod("ValidateRepresentable", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [query, false]);

            Refresh(editor, "en");
            Refresh(reopened, "en");
            editor.ImportQuery(query);
            Check(editor.ExportQuery(SearchQuery.Empty).EventResultConditions.SequenceEqual(conditions), "DirectPresetRoundTrip");
            Check(editor.AuthoredConditionCount() == conditions.Select(condition =>
            {
                EventOpportunitySemantics.TryGetEventKey(condition, out var source);
                return source;
            }).Distinct().Count(), "CountsDistinctEventSources");

            var state = editor.ExportEditorState() with { InactiveResultEvents = [crystal.Entry, doll.Entry] };
            var serialized = JsonSerializer.Serialize(state, json);
            var loaded = JsonSerializer.Deserialize<EventEditorStateSnapshot>(serialized, json)!;
            var dormantQuery = query with { EventResultConditions = conditions
                .Where(condition => EventOpportunitySemantics.TryGetEventKey(condition, out var source) &&
                    source != doll && source != crystal).ToArray() };
            reopened.ImportQuery(dormantQuery);
            reopened.ImportEditorState(loaded);
            Refresh(reopened, "zh");
            Check(reopened.ExportQuery(SearchQuery.Empty).EventResultConditions.SequenceEqual(dormantQuery.EventResultConditions),
                "DormantStatesDoNotFilter");
            Check(JsonSerializer.Serialize(reopened.ExportEditorState(), json) == serialized, "DormantStatesSurviveJSONAndRefresh");
            var undo = reopened.ClearConditions(false);
            Check(!reopened.HasConditions(false), "ClearIncludesNewConditions");
            undo();
            Check(JsonSerializer.Serialize(reopened.ExportEditorState(), json) == serialized, "UndoRestoresNewConditions");

            foreach (string language in new[] { "en", "zh" })
            {
                Refresh(editor, language);
                var text = JsonUiTextProvider.CreateUi13(language);
                var names = RuntimeGameContentNameResolver.Create(language);
                foreach (var condition in conditions)
                {
                    Check(EventOpportunityPresentation.TryDescribe(text, names, condition, out string summary) &&
                        !summary.Contains(text.Get("test.missing_event_opportunity_key"), StringComparison.Ordinal),
                        "LocalizedSummary:" + language + ":" + condition.Kind);
                }
                editor.FocusEvent(doll);
                Check(RelicTiles(editor).Count() == 3, "DollPool:" + language);
                editor.FocusEvent(museum);
                var museumTiles = RelicTiles(editor).ToArray();
                Check(museumTiles.Length == 3, "MuseumPool:" + language);
                foreach (var key in EventOpportunityCatalog.MuseumRelics)
                {
                    var tile = museumTiles.Single(candidate => candidate.Name.ToString() == "EventRelicChoice_" + key.Entry);
                    string path = "res://images/relics/" + key.Entry.ToLowerInvariant() + ".png";
                    var rendered = tile.GetChildren().OfType<TextureRect>().SingleOrDefault()?.Texture;
                    Check(rendered?.ResourcePath == path, "MuseumUsesNativeStandaloneImage:" + key.Entry);
                }
                editor.FocusEvent(thermae);
                Check(RelicTiles(editor).Count() == 9 && RelicTiles(editor).All(tile =>
                    tile.GetChildren().OfType<TextureRect>().Any(icon => icon.Texture is not null) &&
                    !string.IsNullOrWhiteSpace(tile.AccessibilityName)), "BathIdentitiesAndIcons:" + language);
                editor.FocusEvent(crystal);
                var expected = language == "en" ? "Payment Plan · 6 flips" : "分期付款 · 翻 6 次";
                Check(editor.FindChildren("*", "Button", true, false).OfType<Button>().Any(button => button.Text == expected),
                    "CrystalBranchLocalized:" + language);
            }

            editor.FocusEvent(museum);
            var replacement = EventOpportunityCatalog.MuseumRelics[2];
            PressChoice("EventRelicChoice_" + replacement.Entry);
            Check(editor.ExportQuery(SearchQuery.Empty).EventResultConditions.Single(condition =>
                condition.Kind == EventResultConditionKind.MuseumOfTortureOfferedRelic).TargetKey == replacement,
                "MuseumChoiceReplacesTarget");
            editor.FocusEvent(crystal);
            PressLabel("揭幕未来 · 翻 3 次");
            var threeFlips = editor.ExportQuery(SearchQuery.Empty).EventResultConditions.Single(condition =>
                condition.Kind == EventResultConditionKind.CrystalSphereRewards);
            Check(threeFlips is { CrystalDivinations: 3, RelicRarity: EffectRelicRarity.Uncommon } &&
                threeFlips.CrystalCards == (CrystalSphereSeedCards.Common | CrystalSphereSeedCards.Rare),
                "CrystalBranchChangePreservesSamePathTargets");

            editor.ImportQuery(SearchQuery.Empty with { EventResultConditions = [conditions[0]] });
            editor.FocusEvent(doll);
            PressLabel("二选一 · 扣 5 生命");
            Check(editor.ExportQuery(SearchQuery.Empty).EventResultConditions.Single() ==
                (conditions[0] with { Kind = EventResultConditionKind.DollRoomTakeSomeTimeRelic }),
                "DollModeChangeKeepsChosenIdentity");

            Console.WriteLine($"EVENT_OPPORTUNITY_EDITOR=PASS;checks={checks}");
            return 0;
        }
        finally { reopened.Free(); editor.Free(); }
    }

    private static ModelKey Event(string entry) => new(BaseGameModelKeys.Categories.Event, entry);

    private static IEnumerable<Button> RelicTiles(EventSearchEditor editor) =>
        editor.FindChildren("EventRelicChoice_*", "Button", true, false).OfType<Button>();
}
