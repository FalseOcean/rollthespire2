using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Pages.Search.Editors;

internal static class EventEligibilityEditorChecks
{
    internal static int Run()
    {
        var baseline = NativeWorkbenchCheckEnvironment.Initialize("EventEligibilityEditor");
        var runtime = baseline with { Detection = baseline.Detection with
            { RawVersion = "0.113.1", NormalizedVersion = "0.113.1" } };
        var editor = new EventSearchEditor(runtime) { Size = new(1000, 820) };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(editor);
        int checks = 0;

        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidDataException("EventEligibilityEditor." + name);
        }

        T Get<T>(Node parent, string name) where T : Node => parent.FindChild(name, true, false) is T node
            ? node : throw new InvalidDataException("EventEligibilityEditor.MissingNode:" + name);

        void OpenPicker() => Get<Button>(editor, "EventQueueChoose").EmitSignal(BaseButton.SignalName.Pressed);

        void SelectAct(int act)
        {
            var pickerAct = Get<OptionButton>(editor, "EventPickerAct");
            pickerAct.Select(act - 1);
            pickerAct.EmitSignal(OptionButton.SignalName.ItemSelected, (long)act - 1);
            Check(Get<OptionButton>(editor, "EventQueueAct").Selected == act - 1, "BuilderTracksPickerAct:" + act);
        }

        bool HasChoice(string eventId) => editor.FindChild("EventChoice_" + eventId, true, false) is Button;
        Button Choice(string eventId) => Get<Button>(editor, "EventChoice_" + eventId);

        try
        {
            const string museum = "MUSEUM_OF_TORTURE";
            const string choir = "LUMINOUS_CHOIR";
            const string bath = "THERMAE_NONUPEIPE";
            const string crystal = "CRYSTAL_SPHERE";
            const string repy = "WAR_HISTORIAN_REPY";
            var imported = new EventSequenceSearchCondition(1, null, SearchSequenceRangeMode.FirstN, 3,
                new([Event(museum)], [], []));
            ModelKey[] nativeEvents = ModelDb.AllEvents.Select(nativeEvent => Event(nativeEvent.Id.Entry)).ToArray();
            Check(nativeEvents.Length == 62, "NativeRegistrationHas62Events");
            editor.Refresh("en", JsonUiTextProvider.CreateUi13("en"), BaseGameModelKeys.Characters.Ironclad,
                10, 1, 0, render: false);
            editor.ImportQuery(SearchQuery.Empty with { EventSequenceConstraints = [imported] });
            foreach (string language in new[] { "en", "zh" })
            {
                var text = JsonUiTextProvider.CreateUi13(language);
                foreach (ModelKey nativeEvent in nativeEvents)
                {
                    var description = EventEligibilityPresentation.Describe(text, RuntimeProfileId.Beta111,
                        RngSemanticProfileId.Modern113, nativeEvent, 2);
                    bool expectedCondition = text.TryGet("event.eligibility.modern113." + Beta111EventPresentationKnowledge.Normalize(nativeEvent.Entry), out _);
                    bool expectedScope = EventStaticEligibilityCatalog.KnownModern113AllowedActs(nativeEvent).Count is 1 or 2;
                    Check(expectedCondition || expectedScope ? description is not null && description.HasAdditionalCondition == expectedCondition &&
                        !string.IsNullOrWhiteSpace(description.Text) && (language != "zh" || description.Text.Any(c => c is >= '\u4e00' and <= '\u9fff'))
                        : description is null,
                        "LocalizedNativeOccurrence:" + language + ":" + nativeEvent.Entry);
                }
                editor.Refresh(language, text, BaseGameModelKeys.Characters.Ironclad, 10, 1, 0);
                var historical = Get<Control>(editor, "EventCondition0");
                Check(Get<Label>(historical, "EventConditionUnavailable").Text ==
                    text.Format("query.event.inline.scope_affected", text.Format("query.event.act", 1)),
                    "UnavailableImportExplainsOwnAct:" + language);
                Check(historical.FindChild("EventOccurrenceNote", true, false) is null,
                    "CollapsedImportKeepsUnavailableStatusWithoutOccurrenceNote:" + language);

                OpenPicker();
                SelectAct(1);
                Check(!HasChoice(museum) && !HasChoice(crystal) && !HasChoice(bath) && !HasChoice(repy),
                    "ActOneHidesImpossibleChoices:" + language);
                Choice(choir).EmitSignal(Control.SignalName.FocusEntered);
                Check(Get<Label>(editor, "EventPickerOccurrence").Text.Contains("149", StringComparison.Ordinal),
                    "FocusShowsAuditedGoldRequirement:" + language);

                SelectAct(2);
                Check(HasChoice(museum) && HasChoice(crystal) && !HasChoice(choir) && !HasChoice(bath) && !HasChoice(repy),
                    "ActTwoReplacesChoicesImmediately:" + language);
                Choice(crystal).EmitSignal(Control.SignalName.MouseEntered);
                Check(Get<Label>(editor, "EventPickerOccurrence").Text.Contains("100", StringComparison.Ordinal),
                    "HoverShowsDynamicRequirementWithoutHidingCandidate:" + language);

                SelectAct(3);
                Check(!HasChoice(museum) && HasChoice(crystal) && HasChoice(bath) && !HasChoice(repy),
                    "ActThreeReplacesChoicesImmediately:" + language);
                Choice(bath).EmitSignal(Control.SignalName.FocusEntered);
                string bathNote = Get<Label>(editor, "EventPickerOccurrence").Text;
                Check(bathNote.Contains("175", StringComparison.Ordinal) && bathNote.Contains("75%", StringComparison.Ordinal) &&
                    bathNote.Contains(language == "zh" ? "诺奴佩普" : "Nonupeipe", StringComparison.Ordinal),
                    "BathDescriptionCoversEveryRequirement:" + language);
                editor.CloseQueuePicker();
                var constraints = editor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints;
                Check(constraints.Count == 1 && constraints[0].Act == 1 && constraints[0].Keys.Any.Single() == Event(museum),
                    "SwitchingActPreservesImport:" + language);
            }

            OpenPicker();
            SelectAct(2);
            Choice(museum).EmitSignal(BaseButton.SignalName.Pressed);
            var rows = editor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints;
            Check(rows.Select(row => row.Act).SequenceEqual([1, 2]) && rows.All(row => row.Keys.Any.Single() == Event(museum)),
                "SelectedCandidateUsesCurrentPickerAct");
            Get<Button>(Get<Control>(editor, "EventCondition0"), "EventConditionRemove").EmitSignal(BaseButton.SignalName.Pressed);
            Check(editor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints.Single().Act == 2,
                "UserCanRemoveOnlyUnavailableRow");

            OpenPicker();
            SelectAct(2);
            Choice(crystal).EmitSignal(BaseButton.SignalName.Pressed);
            var crystalRow = Get<Control>(editor, "EventCondition1");
            var crystalEligibility = Get<Label>(crystalRow, "EventOccurrenceNote").Text;
            Check(crystalEligibility.Contains("100", StringComparison.Ordinal) && !crystalEligibility.Contains("第 2 幕", StringComparison.Ordinal),
                "ExpandedResultsShowActualEligibilityWithoutRepeatingActScope");
            Check(!crystalRow.FindChildren("*", "Label", true, false).OfType<Label>().Any(label =>
                    label.Text == JsonUiTextProvider.CreateUi13("zh").Get("query.event.opportunity.crystal_minimum_relic")),
                "DefaultThreeFlipsDoNotShowRelicWarningWithoutRelicTarget");
            crystalRow.GetChildren().OfType<Button>().Single(button => button.Text.StartsWith("▾", StringComparison.Ordinal))
                .EmitSignal(BaseButton.SignalName.Pressed);
            Check(Get<Control>(editor, "EventCondition1").FindChild("EventOccurrenceNote", true, false) is null,
                "ClosingResultsRemovesEligibilityNote");

            var zh = JsonUiTextProvider.CreateUi13("zh");
            var aroma = EventEligibilityPresentation.Describe(zh, RuntimeProfileId.Beta111, RngSemanticProfileId.Modern113,
                Event("AROMA_OF_CHAOS"), 1)!;
            Check(aroma.Text.Contains("1", StringComparison.Ordinal) && !aroma.Text.Contains("尚未", StringComparison.Ordinal),
                "OrdinaryEventShowsActWithoutGenericUnvisitedClaim");
            var solo = EventEligibilityPresentation.Describe(zh, RuntimeProfileId.Beta111, RngSemanticProfileId.Modern113,
                Event("DENSE_VEGETATION"), 1);
            var party = EventEligibilityPresentation.Describe(zh, RuntimeProfileId.Beta111, RngSemanticProfileId.Modern113,
                Event("DENSE_VEGETATION"), 2);
            Check(solo?.HasAdditionalCondition != true && party is { HasAdditionalCondition: true } &&
                party.Text.Contains("8", StringComparison.Ordinal), "MultiplayerOnlyHpLimit");
            Check(EventEligibilityPresentation.Describe(zh, RuntimeProfileId.Beta111, RngSemanticProfileId.Modern113,
                Event("MOD_EVENT"), 1) is null, "UnknownEventDoesNotClaimNoRequirements");
            Console.WriteLine($"EVENT_ELIGIBILITY_EDITOR=PASS;checks={checks}");
            return 0;
        }
        finally { editor.Free(); }
    }

    private static ModelKey Event(string entry) => new(BaseGameModelKeys.Categories.Event, entry);
}
