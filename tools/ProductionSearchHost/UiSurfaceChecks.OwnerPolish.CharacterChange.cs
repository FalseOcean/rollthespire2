using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunOwnerCharacterChangeChecksAsync(Node root, WorkspaceShell shell, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        int checks = 0;
        void Check(bool valid, string why)
        { checks++; if (!valid) throw new InvalidOperationException("OwnerCharacterChange:" + why); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var neow = Field<NeowSearchEditor>(frame, "_neowEditor");
        var original = frame.CaptureDraft();
        var history = store.ResultBatch?.Results ?? store.Workspace.Results;
        var sentinel = new PersistedSearchResult { Seed = "000000001717", CharacterKey = BaseGameModelKeys.Characters.Ironclad.Serialized, Ascension = 10 };
        history.Add(sentinel);
        string historyBefore = JsonSerializer.Serialize(history);
        var screenshots = new List<string>();
        var blank = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        NeowSearchUiCatalog Catalog() => Field<NeowSearchUiCatalog>(Field<object>(neow, "_draft"), "Catalog");
        async Task Restore(WorkbenchSearchDraft draft, string locale)
        {
            frame.RestoreDraft(draft, render: false); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
            Call(frame, "SelectDomain", "neow"); await Frames();
        }
        async Task ChooseCharacter(ModelKey key)
        {
            var picker = (OptionButton)frame.FindChild("SearchCharacter", true, false)!;
            var characters = Field<ModelKey[]>(frame, "_characters");
            int index = Array.IndexOf(characters, key);
            Check(index >= 0, "native character option exists " + key);
            picker.Select(index); picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index); await Frames();
        }
        SearchQuery Output(ModelKey source, ModelKey target, bool kaleidoscope = false) => SearchQuery.Empty with
        {
            OpeningRoute = new(source),
            StructuredOpeningEffects = [new(source, kaleidoscope ? NeowStructuredConditionKind.IndependentOfferGroupTargets : NeowStructuredConditionKind.ExactSingle,
                kaleidoscope ? NeowStructuredEffectScope.SelectableOfferGroups : NeowStructuredEffectScope.ProductRelevantEffects,
                NeowStructuredOutputKind.Card, [target], AllowDuplicateOutputs: kaleidoscope)]
        };
        async Task Capture(string name)
        {
            await Frames(); RenderingServer.ForceDraw();
            string path = Path.Combine(output, "owner-character-" + name + ".png");
            root.GetViewport().GetTexture().GetImage().SavePng(path); screenshots.Add(path);
        }
        try
        {
            foreach (string locale in new[] { "zh", "en" })
            {
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                store.SetLanguageOverride(locale); Call(shell, "RefreshLanguage");
                await Restore(blank, locale);
                ModelKey[] ironRare = Catalog().RareCharacterCards.ToArray();
                await Restore(blank with { Character = BaseGameModelKeys.Characters.Silent }, locale);
                ModelKey target = ironRare.First(key => !Catalog().RareCharacterCards.Contains(key, ModelKeyComparer.Instance) &&
                    Catalog().OtherCharacterCards.Contains(key, ModelKeyComparer.Instance));
                Check(Catalog().CompleteCandidatePools.Contains(NeowCandidatePoolKind.RareCharacterCards), "actual new-character rare pool is complete");
                var incompatible = blank with { Query = Output(BaseGameModelKeys.Relics.ArcaneScroll, target) };
                await Restore(incompatible, locale);
                await ChooseCharacter(BaseGameModelKeys.Characters.Silent);
                var switched = frame.CaptureDraft();
                Check(switched.Character == BaseGameModelKeys.Characters.Silent && switched.Query.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.ArcaneScroll &&
                    switched.Query.StructuredOpeningEffects.Count == 0, "native character edit removes only the unavailable source-specific output");
                string name = RuntimeGameContentNameResolver.Create(locale).Resolve(target, GameContentKind.Card);
                Check(Field<Label>(frame, "_status").Text.Contains(name, StringComparison.Ordinal), "character receipt names the removed card");
                await Capture("removed-" + locale);

                await Restore(blank with { Query = Output(BaseGameModelKeys.Relics.Kaleidoscope, target, true) }, locale);
                await ChooseCharacter(BaseGameModelKeys.Characters.Silent);
                Check(frame.CaptureDraft().Query.StructuredOpeningEffects.Single().OutputKeys.Contains(target),
                    "Kaleidoscope retains the same cross-character card because its actual other-character pool contains it");
                await Capture("cross-pool-retained-" + locale);

                ModelKey unknown = new("CARD", "OWNER_UI_UNKNOWN_MOD_CARD");
                await Restore(blank with { Query = Output(BaseGameModelKeys.Relics.ArcaneScroll, unknown) }, locale);
                await ChooseCharacter(BaseGameModelKeys.Characters.Silent);
                Check(frame.CaptureDraft().Query.StructuredOpeningEffects.Single().OutputKeys.Contains(unknown),
                    "unknown Mod identity survives the native character edit");

                var stale = incompatible with { Character = BaseGameModelKeys.Characters.Silent };
                await Restore(stale, locale);
                frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale)); await Frames();
                Check(frame.CaptureDraft().Query.StructuredOpeningEffects.Single().OutputKeys.Contains(target),
                    "restoration and ordinary refresh preserve authored stale targets");

                // Deliberately degrade only the captured proof in the existing
                // native editor; this is a bounded Partial-authority fixture.
                object draft = Field<object>(neow, "_draft");
                NeowSearchUiCatalog complete = Catalog();
                draft.GetType().GetField("Catalog", flags)!.SetValue(draft, complete with { CompleteCandidatePools = new HashSet<NeowCandidatePoolKind>() });
                try
                {
                    Check(neow.RemoveUnavailableSelectionsAfterCharacterChange().Count == 0 &&
                        frame.CaptureDraft().Query.StructuredOpeningEffects.Single().OutputKeys.Contains(target),
                        "Partial pool metadata cannot establish a negative membership proof");
                }
                finally { draft.GetType().GetField("Catalog", flags)!.SetValue(draft, complete); }
                Check(JsonSerializer.Serialize(history) == historyBefore, "character edits do not mutate historical result entries");
            }
            File.WriteAllText(Path.Combine(output, "owner-character-change.json"), JsonSerializer.Serialize(new { checks, screenshots,
                nativeCharacterSignal = true, partialFixture = "complete captured catalog with only negative-proof set removed" }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"OWNER_CHARACTER_CHANGE_PASS checks={checks};screenshots={screenshots.Count}");
        }
        finally { history.Remove(sentinel); frame.RestoreDraft(original); await Frames(); }
    }
}
