using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

internal static class CrystalNativePoolChecks
{
    internal static int Run()
    {
        var baseline = NativeWorkbenchCheckEnvironment.Initialize("CrystalNativePools");
        var runtime = baseline with { Detection = baseline.Detection with { RawVersion = "0.113.1", NormalizedVersion = "0.113.1" } };
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("CrystalNativePools." + name); }
        foreach (var unlocks in new[] { UnlockState.none, UnlockState.all })
        foreach (var character in unlocks.Characters)
        foreach (int players in new[] { 1, 2 })
        {
            var characterKey = Key(character);
            var capture = ReflectionNeowEffectSnapshotAdapter.Capture(runtime.Profile,
                ProfileSeedGenerator.CreateProbeSeed(runtime.Profile), CharacterIdentity.FromKey(characterKey),
                10, players, 0, runtime.Profile.ProfileId, "0.113.1",
                RuntimeProfilePolicies.EffectCatalogRules(runtime.Profile.ProfileId), unlocks);
            var effects = capture.EffectAuthority!;
            Check(effects.NativeCharacterPoolOrder?.SequenceEqual(unlocks.Characters.Select(Key)) == true, "NativeCharacterOrder");
            foreach (int mask in Enumerable.Range(0, 8))
            {
                var flags = (CrystalSphereSearchRelics)mask;
                IEnumerable<CardPoolModel> pools = [character.CardPool];
                if (flags.HasFlag(CrystalSphereSearchRelics.PrismaticGem)) pools = unlocks.CharacterCardPools.Union(pools);
                if (flags.HasFlag(CrystalSphereSearchRelics.DingyRug)) pools = pools.Union([ModelDb.CardPool<ColorlessCardPool>()]);
                var native = pools.SelectMany(p => p.GetUnlockedCards(unlocks, players == 1
                    ? CardMultiplayerConstraint.SingleplayerOnly : CardMultiplayerConstraint.MultiplayerOnly))
                    .Where(card => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
                    .Distinct().Select(Key).ToArray();
                var projected = CrystalSphereSearchSemantics.Pool(effects, players, flags, characterKey);
                if (projected is null || !projected.Select(card => card.CardKey).SequenceEqual(native))
                    Console.WriteLine($"pool mismatch captured={effects.CaptureDiagnosticCode};identity={effects.HasExactCharacterRewardIdentityPool};native={string.Join(',', native)};projected={string.Join(',', projected?.Select(card => card.CardKey) ?? [])}");
                Check(projected is not null && projected.Select(card => card.CardKey).SequenceEqual(native),
                    "PoolOrder:" + characterKey + ":" + players + ":" + flags);
            }
        }
        CheckLimitedStartingDeck(runtime, Check);
        var editor = new EventSearchEditor(runtime) { Size = new(1000, 820) };
        var reopened = new EventSearchEditor(runtime) { Size = new(1000, 820) };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(editor);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(reopened);
        try
        {
            foreach (var item in new[] { editor, reopened }) item.Refresh("en", JsonUiTextProvider.CreateUi13("en"),
                BaseGameModelKeys.Characters.Ironclad, 10, 1, 0, render: false);
            var condition = new EventResultSearchCondition(EventResultConditionKind.CrystalSphereRewards, new("EVENT", "CRYSTAL_SPHERE"))
            { CrystalDivinations = 6, CrystalRelics = CrystalSphereSearchRelics.Driftwood | CrystalSphereSearchRelics.DingyRug,
                CrystalTargetCards = [new("CARD", "APOTHEOSIS"), new("CARD", "APOTHEOSIS")] };
            var query = SearchQuery.Empty with { EventResultConditions = [condition] };
            editor.ImportQuery(query);
            var json = SearchPresetStore.CreateJsonOptions();
            var state = JsonSerializer.Deserialize<EventEditorStateSnapshot>(JsonSerializer.Serialize(editor.ExportEditorState(), json), json)!;
            reopened.ImportQuery(query); reopened.ImportEditorState(state);
            var restored = reopened.ExportQuery(SearchQuery.Empty).EventResultConditions;
            Check(restored.Count == 1 && restored[0].CrystalTargetCards!.SequenceEqual(condition.CrystalTargetCards), "QueryAndEditorDoNotDuplicateRows");
            Check(restored[0].CrystalRelics == condition.CrystalRelics, "PremisesPreserved");
            typeof(EventSearchEditor).GetMethod("RenderCrystalResults", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(reopened, [reopened, condition.TargetKey, 0f, 0f, 900f]);
            Check(reopened.FindChild("CrystalTarget_Uncommon", true, false) is not null &&
                reopened.FindChild("CrystalTarget_Rare", true, false) is not null &&
                reopened.FindChildren("CrystalTarget_*", "", true, false).Count == 2, "TwoRaritySlotsOnly");
            Check(reopened.FindChild("CrystalRetainedTarget_1", true, false) is not null, "OldExtraTargetRemainsRemovable");
            Check(reopened.FindChildren("*", "Button", true, false).OfType<Button>().Count(button =>
                button.Text == "Common") == 1, "OnlyRelicRarityButtonsRemain");
            var prism = reopened.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(button => button.Text == "Prismatic Gem");
            prism.EmitSignal(BaseButton.SignalName.Pressed);
            Check(reopened.ExportQuery(SearchQuery.Empty).EventResultConditions.Single().CrystalRelics ==
                (CrystalSphereSearchRelics.PrismaticGem | CrystalSphereSearchRelics.DingyRug), "PrismaticReplacesDriftwoodKeepsDingy");
        }
        finally { editor.Free(); reopened.Free(); }
        Console.WriteLine("CRYSTAL_NATIVE_POOLS_PASS checks=" + checks);
        return 0;
    }
    private static int _basicCount;

    private static void LimitStartingDeck(ref IEnumerable<CardModel> __result) =>
        __result = __result.Where(c => c.Rarity == CardRarity.Basic && c.Tags.Contains(CardTag.Strike)).Take(_basicCount).ToArray();

    private static void CheckLimitedStartingDeck(RolltheSpire2.Bootstrap.ModRuntimeSnapshot runtime, Action<bool, string> check)
    {
        var harmony = new HarmonyLib.Harmony("rt2.check.crystal-native-pools.limited-deck");
        var getter = typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad).GetProperty(nameof(CharacterModel.StartingDeck))!.GetMethod!;
        var postfix = typeof(CrystalNativePoolChecks).GetMethod(nameof(LimitStartingDeck), BindingFlags.Static | BindingFlags.NonPublic)!;
        harmony.Patch(getter, postfix: new HarmonyLib.HarmonyMethod(postfix));
        try
        {
            foreach (int count in new[] { 0, 1, 2 })
            {
                _basicCount = count;
                var editor = new EventSearchEditor(runtime) { Size = new(1100, 820) };
                ((SceneTree)Engine.GetMainLoop()).Root.AddChild(editor);
                editor.Picker = new RolltheSpire2.Ui.Controls.Pickers.ObjectPickerPanel();
                editor.AddChild(editor.Picker);
                try
                {
                    editor.Refresh("en", JsonUiTextProvider.CreateUi13("en"), BaseGameModelKeys.Characters.Ironclad, 10, 1, 0, render: true);
                    var draft = typeof(EventSearchEditor).GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
                    var catalog = (RolltheSpire2.Ui.Pages.Search.Event.EventSequenceSearchUiCatalog)draft.GetType().GetField("Catalog")!.GetValue(draft)!;
                    var ordinary = catalog.CandidatesForAct(1).First(candidate => candidate.EventKey.Entry != "CRYSTAL_SPHERE");
                    void ChooseEvent(int act, string entry)
                    {
                        ((Button)editor.FindChild("EventQueueChoose", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
                        var acts = (OptionButton)editor.FindChild("EventPickerAct", true, false)!;
                        acts.EmitSignal(OptionButton.SignalName.ItemSelected, act - 1);
                        ((Button)editor.FindChild("EventChoice_" + entry, true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
                    }
                    ChooseEvent(1, ordinary.EventKey.Entry);
                    int crystalAct = Enumerable.Range(1, 3).First(act => catalog.CandidatesForAct(act).Any(candidate => candidate.EventKey.Entry == "CRYSTAL_SPHERE"));
                    ChooseEvent(crystalAct, "CRYSTAL_SPHERE");
                    var slot = editor.FindChild("CrystalTarget_Rare", true, false)!;
                    slot.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "+").EmitSignal(BaseButton.SignalName.Pressed);
                    check(editor.Picker.Visible, "Crystal rare picker opened with " + count + " basics");
                    var card = editor.Picker.FindChildren("*", "Button", true, false).OfType<Button>().First(button => button.GetParent() is GridContainer);
                    card.EmitSignal(BaseButton.SignalName.Pressed);
                    var query = editor.ExportQuery(SearchQuery.Empty);
                    check(query.EventSequenceConstraints.Any(condition => condition.Keys.Any.Contains(ordinary.EventKey)), "Ordinary event selected with " + count + " basics");
                    check(query.EventResultConditions.Single().CrystalTargetCards?.Count == 1, "Crystal card selected with " + count + " basics");
                    if (count < 2) CheckRetainedTransform(runtime, count, check);
                    var resultPool = typeof(EventSearchEditor).GetMethod("ResultPool", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    bool HasChoices(EventResultConditionKind kind) => ((IReadOnlyList<ModelKey>)resultPool.Invoke(editor, [kind])!).Count > 0;
                    check(HasChoices(EventResultConditionKind.WhisperingHollowInitialBasicTransform) == (count > 0), "Single transform with " + count + " basics");
                    check(HasChoices(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains) == (count > 1), "Double transform with " + count + " basics");
                }
                finally { editor.Free(); }
            }
        }
        finally { harmony.Unpatch(getter, postfix); }
    }

    private static void CheckRetainedTransform(RolltheSpire2.Bootstrap.ModRuntimeSnapshot runtime, int count, Action<bool, string> check)
    {
        var pair = new EventResultSearchCondition(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, new("CARD", "BASH"));
        var trial = new EventResultSearchCondition(EventResultConditionKind.TrialCase, new("EVENT", "TRIAL")) { TrialCase = TrialCaseTarget.Merchant };
        var options = new SearchRunOptions("000000000000", 2, 2, 1);
        var compiled = WorkbenchQueryCompiler.CompileAuthoredQuery(SearchQuery.Empty with { EventResultConditions = [pair, trial] },
            AncientOptionConditionProfile.BroadDefault, runtime, BaseGameModelKeys.Characters.Ironclad, 10, out var authority);
        var model = SearchPreparationCompiler.Compile(compiled, options).Plan!.Model;
        string issue = "EventTransform.LegalInitialBasicsRequired:2";
        CheckRoot(false);
        CheckRoot(true);
        void CheckRoot(bool trialMatches)
        {
            ulong root = 0;
            while ((Beta111TrialTinkerProjector.TrialCase(root, 0) == (int)TrialCaseTarget.Merchant) != trialMatches) root++;
            var verdict = ProductionExactSearchEvaluator.Evaluate(model, TrustedRootHashInput.RootOnly(root)).Evaluation;
            check(trialMatches ? verdict.Disposition == SearchDisposition.Unknown && verdict.FailureCode == issue
                : verdict.Disposition == SearchDisposition.NoMatch, "Independent Trial and retained pair with " + count + " basics: " + verdict);
        }

        var aggregate = new TransformationAggregateCondition(TransformationOpening.None, TransformationPickupOrder.Any,
            true, false, true, TransformationAggregatePredicate.ContainsMultiset, 0, [new("CARD", "BASH")])
        {
            ResultRequirements = [new(["E.WHISPERING_HOLLOW"], TransformationAggregatePredicate.ContainsMultiset, 0, [new("CARD", "BASH")])]
        };
        compiled = WorkbenchQueryCompiler.CompileAuthoredQuery(SearchQuery.Empty with { TransformationAggregate = aggregate },
            AncientOptionConditionProfile.BroadDefault, runtime, BaseGameModelKeys.Characters.Ironclad, 10, out _, authority);
        model = SearchPreparationCompiler.Compile(compiled, options).Plan!.Model;
        var combined = TransformationAggregateValidator.Evaluate(model, 0, authority);
        check(count == 0 ? combined.Disposition == SearchDisposition.Unknown && combined.FailureCode == issue
            : combined.Disposition == SearchDisposition.NoMatch, "Single-event evidence inside partial aggregate: " + combined);
    }

    private static ModelKey Key(AbstractModel model) => new(model.Id.Category, model.Id.Entry);
}
