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
    private static ModelKey Key(AbstractModel model) => new(model.Id.Category, model.Id.Entry);
}
