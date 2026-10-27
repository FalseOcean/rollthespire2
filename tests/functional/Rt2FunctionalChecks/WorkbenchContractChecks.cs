using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Controls.Pickers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Pages.Search.Neow;

namespace RolltheSpire2.Tests.FastSearch;

internal static class WorkbenchContractChecks
{
    private static void CheckUiTextCatalogReuse(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var providerType = typeof(RolltheSpire2.Presentation.Localization.JsonUiTextProvider);
        var assembly = providerType.Assembly;
        var catalogType = assembly.GetType("RolltheSpire2.Presentation.Localization.JsonLocalizationCatalog")!;
        var load = catalogType.GetMethod("Load", flags, [typeof(string), typeof(string)])!;
        IReadOnlyDictionary<string, string> Catalog(string group, string language) =>
            (IReadOnlyDictionary<string, string>)load.Invoke(null, [group, language])!;
        Dictionary<string, string> Resource(string language, string name)
        {
            using var stream = assembly.GetManifestResourceStream($"RolltheSpire2.Texts.{language}.UI.{name}.json")!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }

        var factories = new Func<string, RolltheSpire2.Presentation.Localization.IUiTextProvider>[] {
            RolltheSpire2.Presentation.Localization.JsonUiTextProvider.Create,
            RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13,
            RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreatePredictorUi13 };
        var providers = new List<RolltheSpire2.Presentation.Localization.IUiTextProvider>();
        foreach (var language in new[] { "en", "zh" })
        {
            foreach (var group in new[] { "rt2_ui_legacy", "rt2_ui_v13" })
            {
                var catalog = Catalog(group, language);
                check(ReferenceEquals(catalog, Catalog(group, language)), "Embedded text catalog is reused");
                check(catalog is not IDictionary<string, string> mutable || mutable.IsReadOnly, "Shared text catalog is immutable");
            }
            for (int i = 0; i < factories.Length; i++)
            {
                var provider = factories[i](language);
                providers.Add(provider);
                check(ReferenceEquals(provider, factories[i](language)), "Text factory reuses provider for unchanged catalogs");
                var expected = Resource(language, i == 1 ? "workbench" : "legacy");
                var english = Resource("en", i == 1 ? "workbench" : "legacy");
                if (i == 2)
                {
                    foreach (var entry in Resource(language, "workbench")) expected[entry.Key] = entry.Value;
                    foreach (var entry in Resource("en", "workbench")) english[entry.Key] = entry.Value;
                }
                foreach (var entry in english)
                {
                    string value = expected.TryGetValue(entry.Key, out var localized) && !string.IsNullOrWhiteSpace(localized)
                        ? localized : entry.Value;
                    if (!string.IsNullOrWhiteSpace(value)) check(provider.Get(entry.Key) == value, "Localized values and merge precedence match resources");
                }
                var missing = RolltheSpire2.Presentation.Localization.UiTextKey.MissingTranslation;
                check(provider.Get("__localization_reuse_missing__") == (english.TryGetValue(missing, out var missingText) ? missingText : "Missing translation"),
                    "Missing-key English fallback is preserved");
            }
        }
        check(!ReferenceEquals(providers[0], providers[3]) && ReferenceEquals(providers[0], factories[0]("other")) &&
            ReferenceEquals(providers[3], factories[0]("ZH")) && ReferenceEquals(providers[2], factories[2]("ZH")),
            "Locale switches and each factory's existing normalization are preserved");
        var loadEntries = catalogType.GetMethod("LoadEntries", flags)!;
        var firstEntries = (IReadOnlyDictionary<string, Dictionary<string, string>>)loadEntries.Invoke(null, ["seed_presets", "en"])!;
        var secondEntries = (IReadOnlyDictionary<string, Dictionary<string, string>>)loadEntries.Invoke(null, ["seed_presets", "en"])!;
        check(!ReferenceEquals(firstEntries.First().Value, secondEntries[firstEntries.First().Key]), "Mutable nested entries remain caller-owned");

        // This group contains nested entries, so Load<string> cannot parse its
        // embedded resource. It exercises the real loose fallback without replacing UI assets.
        string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        string loosePath = Path.Combine(assemblyDirectory, "Texts", "en", "SeedLibrary", "presets.json");
        byte[]? original = File.Exists(loosePath) ? File.ReadAllBytes(loosePath) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(loosePath)!);
        try
        {
            File.WriteAllText(loosePath, "{");
            check(Catalog("seed_presets", "en").Count == 0, "Invalid loose text remains an empty result");
            File.WriteAllText(loosePath, "{\"development\":\"first\"}");
            check(Catalog("seed_presets", "en")["development"] == "first", "Failed catalog load can recover");
            File.WriteAllText(loosePath, "{\"development\":\"updated\"}");
            check(Catalog("seed_presets", "en")["development"] == "updated", "Loose development text changes remain visible");
        }
        finally
        {
            if (original is null) File.Delete(loosePath);
            else File.WriteAllBytes(loosePath, original);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 1000;
        for (int n = 0; n < iterations; n++)
            foreach (var factory in factories) { _ = factory("en"); _ = factory("zh"); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"UI_TEXT_REUSE warmFactoryCalls={iterations * factories.Length * 2};allocatedBytes={allocated};embeddedCatalogs=4;providers=6;looseReload=PASS");
    }

    private static void CheckSearchTooltipBindings(Action<bool, string> check)
    {
        var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
        var sandbox = new Godot.Control { Size = new(800, 600) };
        tree.CurrentScene.AddChild(sandbox);
        var scopeRoot = new Godot.Control { Size = new(600, 400) };
        sandbox.AddChild(scopeRoot);
        var host = new RolltheSpire2.Ui.Tooltips.AnchoredTooltipHost(new RolltheSpire2.Infrastructure.ContentNames.RuntimeRelicTooltipResolver(),
            new RolltheSpire2.Infrastructure.ContentNames.RuntimePotionTooltipResolver(), new RolltheSpire2.Infrastructure.ContentNames.RuntimeCardTooltipResolver());
        scopeRoot.AddChild(host);
        RolltheSpire2.Ui.Tooltips.SearchTooltip.AttachScope(scopeRoot, host);
        var anchor = new Godot.Button { Size = new(80, 32) };
        // Shared tiles bind while still detached from the Search tree.
        RolltheSpire2.Ui.Tooltips.SearchTooltip.SetIfScoped(anchor, "detached text");
        scopeRoot.AddChild(anchor);
        var scope = host.GetChildren().OfType<RolltheSpire2.Ui.Tooltips.SearchTooltipScope>().Single();
        bool embedded = tree.Root.GuiEmbedSubwindows;
        try
        {
            anchor.EmitSignal(Godot.Control.SignalName.MouseEntered);
            check(anchor.TooltipText.Length == 0 && scope.IsProcessing(), "Detached tile binding adopts Search custom tooltip on hover");
            RolltheSpire2.Ui.Tooltips.SearchTooltip.Set(anchor, "updated text");
            check(host.GetChildren().OfType<RolltheSpire2.Ui.Tooltips.SearchTooltipScope>().Count() == 1 && scope.IsProcessing(),
                "Updating a hint reuses its existing tooltip coordinator");
            RolltheSpire2.Ui.Tooltips.SearchTooltip.Set(anchor, "");
            check(!scope.IsProcessing(), "Clearing the active hint dismisses it");
            RolltheSpire2.Ui.Tooltips.SearchTooltip.Set(anchor, "visible text");
            anchor.EmitSignal(Godot.Control.SignalName.MouseEntered);
            anchor.Hide();
            check(!scope.IsProcessing(), "Hiding an anchor dismisses its hint");
            anchor.Show();
            anchor.EmitSignal(Godot.Control.SignalName.MouseEntered);
            scopeRoot.RemoveChild(anchor);
            check(!scope.IsProcessing(), "Removing an anchor dismisses its hint");
            anchor.Free();

            var overlay = new Godot.Control();
            RolltheSpire2.Ui.Tooltips.SearchTooltip.AttachScope(overlay, scopeRoot);
            sandbox.AddChild(overlay);
            var overlayButton = new Godot.Button(); overlay.AddChild(overlayButton);
            var neighbor = new Godot.Button(); sandbox.AddChild(neighbor);
            RolltheSpire2.Ui.Tooltips.SearchTooltip.SetIfScoped(neighbor, "other page");
            check(RolltheSpire2.Ui.Tooltips.SearchTooltip.IsScoped(overlayButton) &&
                !RolltheSpire2.Ui.Tooltips.SearchTooltip.IsScoped(neighbor) && neighbor.TooltipText == "other page",
                "Detached overlay forwards the Search scope without changing neighboring page tooltips");

            tree.Root.GuiEmbedSubwindows = true;
            var option = new Godot.OptionButton { Size = new(120, 32) }; scopeRoot.AddChild(option);
            option.AddItem("One"); option.AddItem("Two");
            RolltheSpire2.Ui.Tooltips.SearchTooltip.SetItem(option, 0, "First item");
            RolltheSpire2.Ui.Tooltips.SearchTooltip.SetItem(option, 1, "Second item");
            option.GetPopup().Popup();
            scope._Process(0);
            check(option.GetItemTooltip(0).Length == 0 && option.GetItemTooltip(1).Length == 0,
                "Embedded dropdown item hints suppress the native tooltip copy");
            option.GetPopup().Hide();
            check(!scope.IsProcessing(), "Closing the dropdown stops its tooltip processing");
        }
        finally
        {
            RolltheSpire2.Ui.Tooltips.SearchTooltip.DetachScope(scopeRoot);
            sandbox.Free();
            tree.Root.GuiEmbedSubwindows = embedded;
        }
    }

    private static void CheckShopCombatAuthoringRanges(ModRuntimeSnapshot runtime, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Draft(object editor) => editor.GetType().GetField("_draft", flags)!.GetValue(editor)!;
        object Field(object owner, string name)
        {
            const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = owner.GetType();
            if (type.GetField(name, members) is { } field) return field.GetValue(owner)!;
            if (type.GetProperty(name, members) is { } property) return property.GetValue(owner)!;
            throw new MissingMemberException(type.FullName, name);
        }
        void Call(object editor, string name, params object[] args) => editor.GetType().GetMethod(name, flags)!.Invoke(editor, args);
        string Snapshot(object value) => JsonSerializer.Serialize(value);
        var picker = new ObjectPickerPanel();
        var opening = new NeowSearchEditor(runtime, picker);
        var shop = new ShopSearchEditor(runtime, picker) { Size = new(936, 600) };
        var combat = new CombatRewardSearchEditor(runtime, picker,
            (players, seat) => players > 1 ? opening.ExportPartyConditions(seat) : opening.ExportQuery()) { Size = new(936, 600) };
        // These checks concern authoring storage and range transitions, not whether
        // a fixture identity belongs to the current seat's shop or reward catalog.
        var card = BaseGameModelKeys.Cards.Claw;
        var otherCard = BaseGameModelKeys.Cards.Greed;
        var relic = BaseGameModelKeys.Relics.GoldenPearl;
        var potion = BaseGameModelKeys.Potions.Ambergris;
        var neutral = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null);
        try
        {
            object shopDraft = Draft(shop);
            object[] rows = new[] { "Relic", "Uncommon", "Rare" }.Select(name => Field(shopDraft, name)).ToArray();
            check(rows.All(row => (int)Field(row, "ShopRange") == 3), "Each shop block defaults independently to three visits");
            shop.ImportQuery(SearchQuery.Empty with {
                MerchantColorlessSequenceConditions = [
                    new(2, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon, [null, card]),
                    new(5, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Rare, [otherCard, null, null, null, card])],
                RelicShopSequenceConditions = [new(4, CombatRewardSequenceOrderMode.Ordered, [null, null, null, relic])]
            });
            var independent = shop.ExportQuery(SearchQuery.Empty);
            check(independent.MerchantColorlessSequenceConditions.Single(c => c.Slot == MerchantColorlessSlot.Uncommon).Count == 2 &&
                independent.MerchantColorlessSequenceConditions.Single(c => c.Slot == MerchantColorlessSlot.Rare).Count == 5 &&
                independent.RelicShopSequenceConditions.Single().Count == 4,
                "Shop import/export preserves three different block ranges");
            for (int index = 0; index < rows.Length; index++)
            {
                object row = rows[index];
                int beforeRange = (int)Field(row, "ShopRange");
                string beforeSlots = Snapshot(Field(row, "Slots"));
                var beforeMode = Field(row, "OrderMode");
                Call(shop, "SetShopRange", row, beforeRange - 1);
                check((int)Field(row, "ShopRange") == beforeRange && Snapshot(Field(row, "Slots")) == beforeSlots &&
                    ((string)Field(row, "RangeProblem")).Length > 0,
                    "Shop block " + index + " refuses to exclude its last-position target and explains why");
                Call(shop, "ToggleMode", row);
                check(Snapshot(Field(row, "Slots")) == beforeSlots, "Shop mode change keeps exact authored slot positions for block " + index);
                Call(shop, "ToggleMode", row);
                check(Field(row, "OrderMode").Equals(beforeMode) && Snapshot(Field(row, "Slots")) == beforeSlots,
                    "Shop mode round trip restores the same slots and mode for block " + index);
            }
            check(Snapshot(shop.ExportQuery(SearchQuery.Empty).MerchantColorlessSequenceConditions) ==
                Snapshot(independent.MerchantColorlessSequenceConditions) &&
                Snapshot(shop.ExportQuery(SearchQuery.Empty).RelicShopSequenceConditions) == Snapshot(independent.RelicShopSequenceConditions),
                "Rejected shop reductions and mode round trips preserve exported requirements");
            shop.ImportQuery(SearchQuery.Empty);
            check(rows.All(row => (int)Field(row, "ShopRange") == 3 &&
                (CombatRewardSequenceOrderMode)Field(row, "OrderMode") == CombatRewardSequenceOrderMode.Unordered),
                "Empty shop imports reset each independent block to its own defaults");
            foreach (object row in rows) Call(shop, "SetShopRange", row, 1);
            check(rows.All(row => (int)Field(row, "ShopRange") == 1) &&
                shop.ExportQuery(SearchQuery.Empty).MerchantColorlessSequenceConditions.Count == 0 &&
                shop.ExportQuery(SearchQuery.Empty).RelicShopSequenceConditions.Count == 0,
                "Empty shop blocks can independently contract to one visit without adding requirements");
            shop.ImportQuery(SearchQuery.Empty with {
                MerchantColorlessSequenceConditions = [
                    new(3, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Uncommon, [card, null, null]),
                    new(3, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Rare, [otherCard, null, null])],
                RelicShopSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Ordered, [relic, null, null])]
            });
            foreach (object row in rows) Call(shop, "SetShopRange", row, 1);
            var smallerShops = shop.ExportQuery(SearchQuery.Empty);
            check(smallerShops.MerchantColorlessSequenceConditions.All(condition => condition.Count == 1) &&
                smallerShops.MerchantColorlessSequenceConditions.Single(condition => condition.Slot == MerchantColorlessSlot.Uncommon).Slots.Single() == card &&
                smallerShops.MerchantColorlessSequenceConditions.Single(condition => condition.Slot == MerchantColorlessSlot.Rare).Slots.Single() == otherCard &&
                smallerShops.RelicShopSequenceConditions.Single() is { Count: 1 } oneShop && oneShop.Slots.Single() == relic,
                "Shop blocks allow valid reductions to one while preserving their first targets");

            combat.ImportQuery(SearchQuery.Empty with {
                CombatCardRewards = new(4, CombatRewardSequenceOrderMode.Ordered, [null, null, null, card]),
                CombatPotionRewards = new(5, CombatRewardSequenceOrderMode.Ordered,
                    [neutral, neutral, neutral, neutral, new(CombatPotionSlotRequirement.DropSpecific, potion)])
            });
            object combatDraft = Draft(combat);
            string cardSlots = Snapshot(Field(combatDraft, "Slots"));
            string potionSlots = Snapshot(Field(combatDraft, "Potions"));
            Call(combat, "SetBattleRange", 3);
            Call(combat, "SetPotionRange", 4);
            check(combat.ExportCondition() is { Count: 4 } && combat.ExportPotionCondition() is { Count: 5 } &&
                Snapshot(Field(combatDraft, "Slots")) == cardSlots && Snapshot(Field(combatDraft, "Potions")) == potionSlots &&
                ((string)Field(combatDraft, "CardRangeProblem")).Length > 0 &&
                ((string)Field(combatDraft, "PotionRangeProblem")).Length > 0,
                "Card and potion range reductions preserve independent last-position targets and explain the affected block");
            Call(combat, "SetMode", true);
            check(combat.ExportCondition()!.OrderMode == CombatRewardSequenceOrderMode.Unordered &&
                combat.ExportPotionCondition()!.OrderMode == CombatRewardSequenceOrderMode.Ordered &&
                Snapshot(Field(combatDraft, "Slots")) == cardSlots,
                "Card mode changes retain authored slots and leave the potion mode independent");
            Call(combat, "SetMode", false);
            void TogglePotionMode() => ((Godot.Button)combat.FindChild("CombatPotionSpecificMode", true, false))
                .EmitSignal(Godot.BaseButton.SignalName.Pressed);
            TogglePotionMode();
            check(combat.ExportPotionCondition()!.OrderMode == CombatRewardSequenceOrderMode.Unordered &&
                combat.ExportCondition()!.OrderMode == CombatRewardSequenceOrderMode.Ordered &&
                Snapshot(Field(combatDraft, "Potions")) == potionSlots,
                "Potion mode changes retain authored slots and leave the card mode independent");
            TogglePotionMode();
            check(Snapshot(Field(combatDraft, "Slots")) == cardSlots && Snapshot(Field(combatDraft, "Potions")) == potionSlots,
                "Combat mode round trips keep all authored target positions");
            combat.ImportQuery(SearchQuery.Empty);
            Call(combat, "SetBattleRange", 1);
            Call(combat, "SetPotionRange", 1);
            check((int)Field(combatDraft, "BattleRange") == 1 && (int)Field(combatDraft, "PotionRange") == 1 &&
                combat.ExportCondition() is null && combat.ExportPotionCondition() is null,
                "Empty card and potion ranges can contract to one combat without adding requirements");
            combat.ImportQuery(SearchQuery.Empty with {
                CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Ordered, [card, null, null]),
                CombatPotionRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
                    [new(CombatPotionSlotRequirement.NoDrop, null), neutral, neutral])
            });
            Call(combat, "SetBattleRange", 1);
            Call(combat, "SetPotionRange", 1);
            check(combat.ExportCondition() is { Count: 1 } cards && cards.Slots.Single() == card &&
                combat.ExportPotionCondition() is { Count: 1 } potions && potions.Slots.Single().Requirement == CombatPotionSlotRequirement.NoDrop,
                "Card and potion ranges allow valid reductions to one while retaining their first targets");
        }
        finally { combat.Free(); shop.Free(); opening.Free(); picker.Free(); }
    }

    private static void CheckEditorStateRoundTrip(ModRuntimeSnapshot runtime, QueryWorkbenchFrame frame,
        SearchWorkspace store, string directory, Action<bool, string> check)
    {
        var original = frame.CaptureDraft();
        var json = SearchPresetStore.CreateJsonOptions();
        string SavedState(WorkbenchSearchDraft draft) => JsonSerializer.Serialize(new {
            draft.NeowEditor, draft.EventEditor, draft.TransformationEditor
        }, json);
        try
        {
            // Literal payloads from the existing nested-state format: this must
            // remain a type move, with the same names, numeric values and nulls.
            var strict = new JsonSerializerOptions(json) { RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            void CheckSavedShape<T>(string saved)
            {
                var snapshot = JsonSerializer.Deserialize<T>(saved, strict)!;
                check(JsonNode.DeepEquals(JsonNode.Parse(saved), JsonSerializer.SerializeToNode(snapshot, json)),
                    typeof(T).Name + " retains the existing persisted JSON shape");
            }
            CheckSavedShape<NeowEditorStateSnapshot>("""
                {"Source":null,"Bones":[],"Ordered":true,"KaleidoscopeOrder":1,"ScrollOffer":1,
                 "JointTransforms":false,"JointCapsules":true,"TransformTargets":[null,"CARD:CLAW",null],
                 "CapsuleTargets":[null,null,"RELIC:GOLDEN_PEARL"],"Curse":null,
                 "Slots":{"RELIC:KALEIDOSCOPE/0":null,"RELIC:KALEIDOSCOPE/1":"CARD:CLAW"},
                 "TransformSeparateInitialized":true,"TransformJointInitialized":false,
                 "CapsuleSeparateInitialized":false,"CapsuleJointInitialized":true}
                """);
            CheckSavedShape<EventEditorStateSnapshot>("""
                {"Results":{"MorphicGroveGroupInitialBasicsContains":["CARD:CLAW",null]},
                 "CharacterColor":4,"TrialCase":2,"TinkerType":1,"TinkerEffect":null,
                 "InactiveResultEvents":["MORPHIC_GROVE"]}
                """);
            CheckSavedShape<TransformationEditorStateSnapshot>("""
                {"TakenOver":[],"Objective":2,"RareCount":1,"Cards":["CARD:CLAW","CARD:CLAW"],
                 "KnownSources":[{"Id":"N.KALEIDOSCOPE","Identity":"RELIC:KALEIDOSCOPE","ResultCount":2,"OtherRelic":null}]}
                """);
            var olderTransform = JsonSerializer.Deserialize<TransformationEditorStateSnapshot>(
                """{"TakenOver":[],"Objective":0,"RareCount":0,"Cards":[]}""", strict)!;
            check(olderTransform.KnownSources is null &&
                JsonSerializer.Deserialize<TransformationEditorStateSnapshot>(
                    """{"TakenOver":[],"Objective":0,"RareCount":0,"Cards":[],"KnownSources":null}""", strict)!.KnownSources is null,
                "Older omitted and explicit-null KnownSources retain their optional meaning");

            var card = BaseGameModelKeys.Cards.Claw;
            frame.RestoreDraft(new WorkbenchSearchDraft(original.Character, original.Ascension, SearchQuery.Empty with {
                EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard, card)]
            }, AncientOptionConditionProfile.BroadDefault), render: false);
            var initial = frame.CaptureDraft();
            var eventResults = new Dictionary<EventResultConditionKind, ModelKey?[]>(initial.EventEditor!.Results)
            { [EventResultConditionKind.MorphicGroveGroupInitialBasicsContains] = [card, null] };
            var authored = initial with {
                NeowEditor = initial.NeowEditor! with {
                    TransformTargets = [card, null, null], CapsuleTargets = [BaseGameModelKeys.Relics.GoldenPearl, null, null],
                    TransformJointInitialized = true, CapsuleJointInitialized = true
                },
                TransformationEditor = initial.TransformationEditor! with { Cards = [card, card],
                    KnownSources = [new("N.KALEIDOSCOPE", BaseGameModelKeys.Relics.Kaleidoscope, 2, BaseGameModelKeys.Relics.GoldenPearl)] },
                EventEditor = initial.EventEditor with { Results = eventResults, InactiveResultEvents = ["MORPHIC_GROVE"] }
            };
            frame.RestoreDraft(authored, render: false);
            var captured = frame.CaptureDraft();
            check(SavedState(captured) == SavedState(authored) &&
                JsonSerializer.Serialize(captured.Query, json) == JsonSerializer.Serialize(initial.Query, json),
                "Inactive Neow allocation, event source and transformation targets survive restore without entering the active query");
            var transported = JsonSerializer.Deserialize<WorkbenchSearchDraft>(JsonSerializer.Serialize(captured, json), json)!;
            frame.RestoreDraft(transported, render: false);
            check(SavedState(frame.CaptureDraft()) == SavedState(authored),
                "Neow, event and transformation editor sidecars survive a JSON round trip");
            store.SaveWorkbench(captured);
            var reloaded = new SearchWorkspace(directory, false).LoadWorkbench()!;
            frame.RestoreDraft(reloaded, render: false);
            check(SavedState(frame.CaptureDraft()) == SavedState(authored),
                "Autosave restores inactive allocation targets, event targets and transformation duplicates");
            var preset = store.Presets.SaveUserPreset("Inactive editor state", "", [], frame.CapturePreset(captured));
            frame.RestoreDraft(original, render: false);
            frame.ApplyPreset(preset, render: false);
            check(SavedState(frame.CaptureDraft()) == SavedState(authored),
                "Preset representability transaction and restore preserve every optional editor sidecar");
            string historyDirectory = Path.Combine(directory, "editor-state-history", Guid.NewGuid().ToString("N"));
            var historyStore = new SearchWorkspace(historyDirectory, false);
            var history = historyStore.EnsureQuery(captured, null);
            history.RunCount = 1; historyStore.FlushAll();
            var historyDraft = new SearchWorkspace(historyDirectory, false).RecentQueries.Single().Draft;
            frame.RestoreDraft(historyDraft.BindPresetIntent(runtime, _ => throw new InvalidOperationException("UnexpectedCurrentContextFixture")), render: false);
            check(SavedState(frame.CaptureDraft()) == SavedState(authored),
                "Frozen history and disk reload preserve dormant solo editor declarations");
            var legacyShape = captured with { NeowEditor = null, EventEditor = null, TransformationEditor = null };
            frame.RestoreDraft(legacyShape, render: false);
            check(JsonSerializer.Serialize(frame.CaptureDraft().Query, json) == JsonSerializer.Serialize(captured.Query, json),
                "Older drafts without editor sidecars restore their original active query");

            var party = captured with {
                Version = 4, Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer,
                GameVersion = runtime.Detection.NormalizedVersion, Profile = runtime.Profile.ProfileId,
                NeowEditor = null, EventEditor = null, TransformationEditor = null,
                Players = Enumerable.Range(0, 2).Select(slot => new WorkbenchPlayerDraft(slot, captured.Character,
                    MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable(), "AssumedFullyUnlocked") {
                    NeowEditor = authored.NeowEditor! with { TransformTargets = slot == 0 ? [card, null, null] : [null, card, null] },
                    EventEditor = authored.EventEditor! with { InactiveResultEvents = slot == 0 ? ["MORPHIC_GROVE"] : ["MORPHIC_GROVE", "TRIAL"] }
                }).ToArray(),
                Query = SearchQuery.Empty with { Players = Enumerable.Range(0, 2)
                    .Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) { Conditions = captured.Query }).ToArray() }
            };
            string PartyState(WorkbenchSearchDraft draft) => JsonSerializer.Serialize(
                draft.Players.Select(p => new { p.Slot, p.NeowEditor, p.EventEditor }), json);
            string partyDirectory = Path.Combine(directory, "editor-state-party", Guid.NewGuid().ToString("N"));
            var partyStore = new SearchWorkspace(partyDirectory, false);
            var partyFrame = new QueryWorkbenchFrame(runtime, partyStore);
            try
            {
                partyFrame.RestoreDraft(JsonSerializer.Deserialize<WorkbenchSearchDraft>(JsonSerializer.Serialize(party, json), strict)!, false);
                var partyCaptured = partyFrame.CaptureDraft();
                check(PartyState(partyCaptured) == PartyState(party) &&
                    QueryHistoryIdentity.Key(partyCaptured) == QueryHistoryIdentity.Key(party),
                    "Party JSON restore retains each original slot's active and dormant declarations without changing query intent");
                partyStore.SaveWorkbench(partyCaptured);
                partyFrame.RestoreDraft(new SearchWorkspace(partyDirectory, false).LoadWorkbench()!, false);
                check(PartyState(partyFrame.CaptureDraft()) == PartyState(party), "Party autosave retains independent per-slot editor snapshots");
                var partyPreset = partyStore.Presets.SaveUserPreset("Party inactive editor state", "", [], partyFrame.CapturePreset(partyCaptured));
                partyFrame.RestoreDraft(original, false); partyFrame.ApplyPreset(partyPreset, false);
                check(PartyState(partyFrame.CaptureDraft()) == PartyState(party), "Strict party preset import retains each slot's dormant choices");
                var partyHistory = historyStore.EnsureQuery(partyCaptured, null);
                partyHistory.RunCount = 1; historyStore.FlushAll();
                var partyHistoryDraft = new SearchWorkspace(historyDirectory, false)
                    .RecentQueries.Single(record => record.Draft.Players.Count == 2).Draft;
                partyFrame.RestoreDraft(partyHistoryDraft.BindPresetIntent(runtime, _ => throw new InvalidOperationException("UnexpectedCurrentContextFixture")), false);
                check(PartyState(partyFrame.CaptureDraft()) == PartyState(party), "Frozen party history retains per-slot editor declarations");
            }
            finally { partyFrame.Free(); }
        }
        finally { frame.RestoreDraft(original, render: false); store.SaveWorkbench(original); }
    }

    private static void CheckProbabilityPreviewQueue(CompiledSearch compiled, Action<bool, string> check)
    {
        // Exercise real pure numerical work off the bound capture/UI thread.
        var preview = Task.Run(() => WorkbenchProbabilityPreview.Compute(compiled, 30, 1, true)).GetAwaiter().GetResult();
        check(preview.Joint.Probability == 1, "Background preview consumes captured authority without native access");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var queue = new WorkbenchProbabilityPreviewQueue(r =>
        {
            calls.Enqueue(r.Key);
            if (r.Key is "first" or "cancelled")
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Preview test release");
            }
            if (r.Key == "fault") throw new InvalidOperationException("Preview test fault");
            return preview;
        });
        WorkbenchProbabilityPreviewQueue.Request Request(string key) => new(key, compiled, 30, 1, true);
        queue.Submit(Request("first"));
        check(entered.Wait(TimeSpan.FromSeconds(5)), "First preview started");
        queue.Submit(Request("obsolete")); queue.Submit(Request("latest"));
        check(calls.Count == 1 && !queue.TryTake(out _), "Edits return while one preview runs, without concurrent estimates");
        release.Set();
        WorkbenchProbabilityPreviewQueue.Completion? result = null;
        check(SpinWait.SpinUntil(() => queue.TryTake(out result), 5000), "Latest preview delivered");
        check(result?.Request.Key == "latest" && calls.SequenceEqual(new[] { "first", "latest" }),
            "Discard stale completion and skip superseded pending query");
        entered.Reset(); release.Reset();
        queue.Submit(Request("cancelled"));
        check(entered.Wait(TimeSpan.FromSeconds(5)), "Cancellation preview started");
        queue.Invalidate(); release.Set();
        queue.Submit(Request("fault"));
        check(SpinWait.SpinUntil(() => queue.TryTake(out result), 5000) && result?.Request.Key == "fault" &&
            result.Error?.Message == "Preview test fault", "Invalidated query never publishes; worker failure is observed");
    }

    private static void CheckConfiguredCpuPreview(SearchContext context, Action<bool, string> check)
    {
        var shop = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/cpu-main-v1/S-three-slots.json").Query;
        var color = new EventResultSearchCondition(EventResultConditionKind.ColorfulPhilosophersOfferedColor,
            BaseGameModelKeys.Characters.Silent);
        var queries = new[] {
            (Family: "S.MerchantShopColorless", Query: shop),
            (Family: "E.EventResult", Query: SearchQuery.Empty with { EventResultConditions = [color] })
        };
        var geometry = new FamilyPhysicalQuoteRequest(false, false, 1 << 20);
        foreach (var (familyId, query) in queries)
        {
            var compiled = SearchCompiler.Compile(query, context);
            double? probability = null;
            foreach (int workers in new[] { 1, 3, 4, 7, 8, 16 })
            {
                var request = ExactSearchExecutionRequestFactory.Compile(compiled,
                    new("000000000000", long.MaxValue, 30, workers)).Plan!;
                var family = FamilyExecutionCoordinator.CreateRegisteredFamilies(request).Single(f => f.FamilyId == familyId);
                var configured = FamilyCpuExecution.ConfiguredRealizations(family).ToArray();
                check(configured.Length > 0 && configured.All(f => f is FamilyCpuExecution cpu && cpu.Workers == workers),
                    $"Preview fixture follows actual CPU budget: {familyId}/P{workers}");
                var expected = configured.Select(f => f.QuotePhysicalWork(geometry))
                    .Where(q => q is { NanosecondsPerInput: > 0 }).Min(q => (double?)q!.NanosecondsPerInput);
                // The P1 anchor deliberately exists. Missing P3/P7 therefore
                // tests that preview cannot borrow it or extrapolate workers.
                if (workers == 1 || familyId == "S.MerchantShopColorless" && workers == 16)
                    check(expected is > 0, $"Known configured CPU reference remains available: {familyId}/P{workers}");
                if (workers is 3 or 7)
                    check(expected is null, $"Unmeasured configured CPU budget remains Unknown: {familyId}/P{workers}");

                var preview = WorkbenchProbabilityPreview.Compute(compiled, 30, workers, true);
                check(preview.Issues.Count == 0 && preview.Costs.TryGetValue(familyId, out var cost) && cost.Cpu == expected,
                    $"CPU preview consumes only its actual configured reference: {familyId}/P{workers}");
                check(preview.Costs[familyId].Gpu is null, "CPU-only preview does not display a GPU quote");
                if (workers == 1) probability = preview.Joint.Probability;
                else check(preview.Joint.Probability == probability, "CPU budget does not change query probability");
                if (familyId == "E.EventResult")
                    check(preview.Events[color].Cpu == expected && preview.Events[color].Gpu is null,
                        $"Event row uses the same configured CPU scope as family preview: P{workers}");
                if (workers == 7)
                {
                    var mixed = WorkbenchProbabilityPreview.Compute(compiled, 30, workers, false);
                    check(mixed.Issues.Count == 0 && mixed.Costs[familyId].Cpu is null &&
                        (familyId != "E.EventResult" || mixed.Events[color].Cpu is null),
                        "Enabling GPU alternatives cannot lend P1 prices to a P7 CPU preview");
                }
            }
        }
    }

    internal static int RunResultNavigation()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("retained-results");
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "retained-results-" + Guid.NewGuid().ToString("N"));
        var store = new SearchWorkspace(directory, false);
        store.EnsureEnvironment(SearchEnvironmentSignatureBuilder.Capture(runtime));
        var draft = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10, SearchQuery.Empty,
            AncientOptionConditionProfile.BroadDefault);
        store.SaveWorkbench(draft);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var frame = new QueryWorkbenchFrame(runtime, store);
        var references = frame;
        var shell = new WorkspaceShell();
        foreach (string field in new[] { "_settingsPage", "_feedbackPage" })
        {
            var placeholder = new Godot.Control(); shell.AddChild(placeholder);
            typeof(WorkspaceShell).GetField(field, flags)!.SetValue(shell, placeholder);
        }
        typeof(WorkspaceShell).GetField("_references", flags)!.SetValue(shell, references);
        typeof(WorkspaceShell).GetMethod("InitializeResultsEntry", flags)!.Invoke(shell, null);
        Godot.Button ResultsButton() => (Godot.Button)frame.FindChild("ViewLastSearchResults", true, false);
        object Field(string name) => typeof(QueryWorkbenchFrame).GetField(name, flags)!.GetValue(frame)!;
        void Call(string name, params object[] args) => typeof(QueryWorkbenchFrame).GetMethod(name, flags)!.Invoke(frame, args);
        int checks = 0;
        void Check(bool ok, string reason) { checks++; if (!ok) throw new InvalidOperationException(reason); }
        try
        {
            CheckUiTextCatalogReuse(Check);
            CheckSearchTooltipBindings(Check);
            CheckShopCombatAuthoringRanges(runtime, Check);
            CheckEditorStateRoundTrip(runtime, frame, store, directory, Check);
            frame.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
            // Disclosure preserves selected builder parameters and committed predicates.
            {
                var queueEditor = (EventSearchEditor)Field("_eventEditor");
                object queueDraft = typeof(EventSearchEditor).GetField("_draft", flags)!.GetValue(queueEditor)!;
                void CallQueue(string name, params object[] values) => typeof(EventSearchEditor).GetMethod(name, flags)!.Invoke(queueEditor, values);
                Godot.Button QueueButton(string name) => (Godot.Button)queueEditor.FindChild(name, true, false);
                void QueueOption(string name, int index)
                {
                    var option = (Godot.OptionButton)queueEditor.FindChild(name, true, false);
                    option.Select(index);
                    option.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, index);
                }
                Check((int)queueDraft.GetType().GetField("QueueAct")!.GetValue(queueDraft)! == 1 && !queueEditor.AdvancedOpen &&
                    queueEditor.FindChild("EventQueuePositionValue", true, false) is Godot.Label { Text: "3" } &&
                    queueEditor.FindChild("EventQueueRangeMode", true, false) is Godot.Label &&
                    queueEditor.FindChild("EventQueuePolarity", true, false) is Godot.Label,
                    "New event builders default to Act 1 and show only First N inclusion controls");
                var fake = new ModelKey("EVENT", "FAKE_MERCHANT");
                var queueKey = ((RolltheSpire2.Ui.Pages.Search.Event.EventSequenceSearchUiCatalog)queueDraft.GetType()
                    .GetField("Catalog")!.GetValue(queueDraft)!).CandidatesForAct(1).First().EventKey;
                var imported = new EventSequenceSearchCondition(2, null, SearchSequenceRangeMode.ExactSlot, 5,
                    new([], [], [fake]));
                queueEditor.ImportQuery(SearchQuery.Empty with { EventSequenceConstraints = [imported] });
                Check((int)queueDraft.GetType().GetField("QueueAct")!.GetValue(queueDraft)! == 1,
                    "Importing Act 2 rows preserves their act without changing the new-row default");
                QueueButton("EventQueueAdvanced").EmitSignal(Godot.BaseButton.SignalName.Pressed);
                Check(queueEditor.AdvancedOpen && queueEditor.FindChild("EventQueueRangeMode", true, false) is Godot.OptionButton &&
                    queueEditor.FindChild("EventQueuePolarity", true, false) is Godot.OptionButton,
                    "Advanced event builder exposes position and polarity choices");
                QueueOption("EventQueueRangeMode", 1);
                QueueOption("EventQueuePolarity", 1);
                QueueButton("EventQueueAdvanced").EmitSignal(Godot.BaseButton.SignalName.Pressed);
                Check(!queueEditor.AdvancedOpen && (bool)queueDraft.GetType().GetField("QueueExact")!.GetValue(queueDraft)! &&
                    (bool)queueDraft.GetType().GetField("QueueExcluded")!.GetValue(queueDraft)! &&
                    queueEditor.FindChild("EventQueueRangeMode", true, false) is Godot.Label &&
                    queueEditor.FindChild("EventQueuePolarity", true, false) is Godot.Label &&
                    queueEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints.Single() is
                        { Act: 2, RangeValue: 5, RangeMode: SearchSequenceRangeMode.ExactSlot } &&
                    queueEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints.Single().Keys.Ban.Single() == fake,
                    "Closing advanced disclosure keeps selected builder parameters and imported exact exclusions");
                CallQueue("AddQueueCondition", queueKey);
                var simpleAdded = queueEditor.ExportQuery(SearchQuery.Empty);
                Check(simpleAdded.EventSequenceConstraints.Count == 2 && simpleAdded.EventSequenceConstraints[1] is
                    { Act: 1, RangeValue: 3, RangeMode: SearchSequenceRangeMode.ExactSlot } && simpleAdded.EventSequenceConstraints[1].Keys.Ban.Single() == queueKey,
                    "Collapsed event builder adds the retained exact exclusion without changing its meaning");
                QueueButton("EventQueueAdvanced").EmitSignal(Godot.BaseButton.SignalName.Pressed);
                QueueOption("EventQueueRangeMode", 0);
                QueueOption("EventQueuePolarity", 0);
                CallQueue("AddQueueCondition", queueKey);
                Check(queueEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints[2] is
                    { Act: 1, RangeValue: 3, RangeMode: SearchSequenceRangeMode.FirstN } &&
                    queueEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints[2].Keys.Any.Single() == queueKey &&
                    queueEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints[1].Keys.Ban.Single() == queueKey,
                    "Explicit builder edits add a new inclusion while preserving the previous exact exclusion");
                CallQueue("ToggleQueueAdvanced");
                var savedQueueDraft = frame.CaptureDraft();
                frame.RestoreDraft(savedQueueDraft, false);
                Check(JsonSerializer.Serialize(queueEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints) ==
                    JsonSerializer.Serialize(savedQueueDraft.Query.EventSequenceConstraints),
                    "Simple-mode save and restore retain every advanced event row");
                var picker = (Godot.Control)typeof(EventSearchEditor).GetField("_queuePickerOverlay", flags)!.GetValue(queueEditor)!;
                var pickerParent = picker.GetParent();
                // The picker takes keyboard focus, so exercise its lifecycle in the real SceneTree.
                picker.Reparent(((Godot.SceneTree)Godot.Engine.GetMainLoop()).CurrentScene);
                try
                {
                    CallQueue("OpenQueuePicker");
                    Check(picker.Visible, "Event picker opens for the current builder");
                    queueEditor.AdvancedOpen = true;
                    Check(!picker.Visible, "Changing the builder mode closes the old event picker");
                    CallQueue("OpenQueuePicker");
                    queueEditor.ImportQuery(savedQueueDraft.Query);
                    Check(!picker.Visible && queueEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints.Count == 3,
                        "Import closes the old picker while preserving imported advanced event rows");
                }
                finally { queueEditor.CloseQueuePicker(); picker.Reparent(pickerParent); }
                frame.RestoreDraft(draft, false);
                frame.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
            }
            Check(ResultsButton().Visible && !ResultsButton().Disabled && ResultsButton().CustomMinimumSize.Y >= 44 &&
                ResultsButton().GetParent().Name == "SearchInformationRail", "Saved results have a full-size action in the Search sidebar even when empty");
            ResultsButton().EmitSignal(Godot.BaseButton.SignalName.Pressed);
            Check(((Godot.Control)Field("_runtimeSurface")).Visible, "Empty saved-results view opens");
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(draft.Character, 10, 0, 1);
            Check(SeedPredictionRequest.TryCreate("000000000001", authority.Character, 10, 1, 0, authority, false,
                out var request, out _), "Retained candidate request");
            var candidate = new SearchCandidate("000000000001", RuntimeProfileId.Beta111, draft.Character, 10,
                "retained-result-fixture", request!, null, authority, [], [], []);
            var retainedPlan = ExactSearchExecutionRequestFactory.Compile(draft.Compile(runtime, out _), new("000000000001", 1, 1, 1)).Plan!;
            var receiving = store.PrepareSearchStart(draft, retainedPlan, SeedLibraryContextCapture.ForSearch(draft, runtime));
            store.BindPreparedResultWorkspace(receiving);
            Call("SyncSavedResults");
            store.AppendResult(candidate, retainedPlan.CompiledSearch.SemanticFingerprint);
            var context = store.ResultBatch?.Context;
            var frozenDraft = store.ResultBatch?.Draft;
            Check(context is not null, "Original result context captured");
            foreach (var disposition in new[] { SearchDisposition.NoMatch, SearchDisposition.Unknown,
                         SearchDisposition.Unsupported, SearchDisposition.Cancelled, SearchDisposition.NotEvaluatedByPolicy })
            {
                // Exercise the actual result-action/favorite boundary with an observed non-Match.
                var checkedCandidate = candidate with { IsUnverified = true, IsUnfiltered = false,
                    ValidationDisposition = disposition, ValidationIssue = "actual-cause:" + disposition,
                    Document = null, MatchEvidence = [], MatchedRouteIds = [], Witnesses = [] };
                SeedLibraryContext? transportedContext = null;
                SeedLibraryResultProvenance? transported = null;
                void Transport(RolltheSpire2.Ui.Workspace.SearchResultAction action)
                    { transportedContext = action.Context; transported = action.Provenance; }
                frame.FavoriteSeedRequested += Transport;
                var liveRow = RolltheSpire2.Ui.Workspace.SearchResultProjection.FromCandidate(checkedCandidate, retainedPlan.CompiledSearch.SemanticFingerprint);
                liveRow.Context = context;
                var button = (Godot.Button)typeof(QueryWorkbenchFrame).GetMethod("FavoriteResultButton", flags)!.Invoke(frame, [store.ResultAction(liveRow)])!;
                try
                {
                    Check(!button.Disabled, "Non-Match result with a restorable context can be bookmarked");
                    button.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    Check(transported?.ValidationDisposition == disposition && transported.ValidationIssue == checkedCandidate.ValidationIssue &&
                        transportedContext?.Players[0].Selection is null,
                        "Live favorite carries actual outcome/cause and does not borrow a prior match selection");
                }
                finally { frame.FavoriteSeedRequested -= Transport; button.Free(); }
                var savedResult = new PersistedSearchResult { Seed = candidate.Seed, Context = context,
                    ValidationDisposition = disposition, ValidationIssue = checkedCandidate.ValidationIssue };
                transported = null;
                frame.FavoriteSeedRequested += Transport;
                button = (Godot.Button)typeof(QueryWorkbenchFrame).GetMethod("FavoriteResultButton", flags)!.Invoke(frame, [store.ResultAction(savedResult)])!;
                try
                {
                    Check(!button.Disabled, "Saved non-Match result can be bookmarked after page/worker lifetime ends");
                    button.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    Check(transported?.ValidationDisposition == disposition && transported.ValidationIssue == checkedCandidate.ValidationIssue,
                        "Saved favorite retains exact known mismatch/unknown/unsupported/cancelled outcome");
                }
                finally { frame.FavoriteSeedRequested -= Transport; button.Free(); }
            }
            foreach (string language in new[] { "zh", "en" })
            {
                var text = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(language);
                typeof(WorkspaceShell).GetField("_languageCode", flags)!.SetValue(shell, language);
                frame.Refresh(language, text);
                references.Visible = false;
                Check(references.IsAncestorOf(ResultsButton()) && ResultsButton().Text.Contains('1'), "Result action belongs to Search and retains its count");
                references.Visible = true;
                ResultsButton().EmitSignal(Godot.BaseButton.SignalName.Pressed);
                Check(((Godot.Control)Field("_runtimeSurface")).Visible && !((Godot.Control)Field("_neowEditor")).Visible,
                    "Last-results action reopens the retained list");
                var rows = (Godot.VBoxContainer)Field("_resultRows");
                var back = rows.GetChild<Godot.HBoxContainer>(0).GetChildren().OfType<Godot.Button>().Single();
                back.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                Check(!((Godot.Control)Field("_runtimeSurface")).Visible && ((Godot.Control)Field("_neowEditor")).Visible,
                    "Actual Back action returns to the editor");
                frame.RestoreDraft(draft with { Ascension = 9 }, render: false);
                frame.Refresh(language, text);
                ResultsButton().EmitSignal(Godot.BaseButton.SignalName.Pressed);
                Check(frame.ResultCount == 0 && store.RecentQueries.Single().Results.Count == 1,
                    "Different query opens an empty result view while the original record is retained");
                Check(frame.CaptureDraft().Ascension == 9 && candidate.Ascension == 10, "Old results do not inherit new editor conditions");
                frame.RestoreDraft(draft, render: false);
                frame.Refresh(language, text);
                ResultsButton().EmitSignal(Godot.BaseButton.SignalName.Pressed);
                Check(frame.ResultCount == 1 && store.ResultBatch?.Draft?.Ascension == 10 && store.ResultBatch?.Context?.Ascension == 10,
                    "Returning to original query restores its result and original context");
                rows = (Godot.VBoxContainer)Field("_resultRows");
                back = rows.GetChild<Godot.HBoxContainer>(0).GetChildren().OfType<Godot.Button>().Single();
                back.EmitSignal(Godot.BaseButton.SignalName.Pressed);
            }
            frame.CloseSearch();
            var reopenedStore = new SearchWorkspace(directory, false);
            var reopened = new QueryWorkbenchFrame(runtime, reopenedStore) { Size = new(1536, 772) };
            try
            {
                reopened.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
                reopened.ShowLastResults();
                Check(reopened.ResultCount == 1 && reopenedStore.Workspace.ResultDraft?.Ascension == 10 && reopened.CaptureDraft().Ascension == 10,
                    "A fresh store and frame recover matching query results");
                var rows = (Godot.VBoxContainer)typeof(QueryWorkbenchFrame).GetField("_resultRows", flags)!.GetValue(reopened)!;
                var resultRow = rows.GetChildren().OfType<Godot.PanelContainer>().Single().GetChild<Godot.VBoxContainer>(0).GetChild<Godot.HBoxContainer>(0);
                Check(resultRow.GetChild<Godot.Label>(0).Text == candidate.Seed &&
                    resultRow.GetChildren().OfType<Godot.Button>().All(b => !b.Disabled), "Reloaded row retains copy, favorite and analysis actions");
                Check(!reopenedStore.Runtime.Receiving &&
                    reopenedStore.ResultBatch?.Start is null, "Reload never restarts a search or reconstructs authority");
            }
            finally { reopened.CloseSearch(); reopened.Free(); }
            // Editor-only multiplayer checks: shared W authoring, private E, and
            // scoped clearing. No search dispatch or GPU work is needed.
            var eventA = new ModelKey("EVENT", "AROMA_OF_CHAOS");
            var eventB = new ModelKey("EVENT", "WHISPERING_HOLLOW");
            var eventCondition = new EventSequenceSearchCondition(1, null, SearchSequenceRangeMode.FirstN, 3, new([eventA], [], []));
            var unlocks = MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable();
            var personal0 = SearchQuery.Empty with { EventSequenceConstraints = [eventCondition],
                EventResultConditions = [new(EventResultConditionKind.TrashHeapDiveRelic, Beta111EventResultCatalog.TrashHeapDiveRelics[0])] };
            var personal1 = SearchQuery.Empty with { EventSequenceConstraints = [eventCondition,
                eventCondition with { RangeMode = SearchSequenceRangeMode.ExactSlot, Keys = new([], [], [eventB]) }],
                EventResultConditions = [new(EventResultConditionKind.TrashHeapDiveRelic, Beta111EventResultCatalog.TrashHeapDiveRelics[1])] };
            var partyDraft = draft with { Version = 4, Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer,
                GameVersion = runtime.Detection.NormalizedVersion, Profile = runtime.Profile.ProfileId,
                Players = [new(0, draft.Character, unlocks, "AssumedFullyUnlocked"), new(1, draft.Character, unlocks, "AssumedFullyUnlocked")],
                Query = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = personal0 }, new(1, ModelKeySetFilter.Empty) { Conditions = personal1 }] } };
            frame.RestoreDraft(partyDraft, false); frame.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
            var roundtrip = frame.CaptureDraft();
            Check(roundtrip.Query.EventSequenceConstraints.Count == 2 && roundtrip.Query.Players.All(p => p.Conditions.EventSequenceConstraints.Count == 0),
                "Legacy per-player queues migrate and deduplicate into one shared W queue");
            Check(roundtrip.Query.Players.Select(p => p.Conditions.EventResultConditions.Single().TargetKey)
                .SequenceEqual(new[] { Beta111EventResultCatalog.TrashHeapDiveRelics[0], Beta111EventResultCatalog.TrashHeapDiveRelics[1] }),
                "Personal E predicates are not copied between players");
            var eventEditor = (EventSearchEditor)Field("_eventEditor");
            eventEditor.AdvancedOpen = true; eventEditor.AdvancedOpen = false;
            Check(eventEditor.ExportSharedPartyQuery(SearchQuery.Empty).EventSequenceConstraints.Any(c =>
                c.RangeMode == SearchSequenceRangeMode.ExactSlot && c.Keys.Ban.Contains(eventB)),
                "Simple builder mode retains migrated multiplayer exact exclusions in the shared queue");
            var partyAuthority = roundtrip.Compile(runtime, out _).Context.Party!;
            foreach (int seat in new[] { 0, 1 })
            {
                eventEditor.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"), draft.Character, 10, 2, seat, unlocks,
                    render: false, partyAuthority: partyAuthority.Players[seat]);
                Check(eventEditor.ExportQuery(SearchQuery.Empty).EventSequenceConstraints.Count == 2, "Both player pages see the same event rows");
                Check(eventEditor.AuthoredConditionCount() == 3, "Sidebar count includes shared appearances and only this player's E target");
            }
            Call("SelectDomain", "events");
            frame.GetNode<Godot.Button>("ClearCategoryConditions").EmitSignal(Godot.BaseButton.SignalName.Pressed);
            var cleared = frame.CaptureDraft();
            Check(cleared.Query.EventSequenceConstraints.Count == 0 && cleared.Query.Players.All(p => p.Conditions.EventResultConditions.Count == 0),
                "Clearing shared event page also clears linked per-player outcomes");
            frame.UndoClearConditions();
            Check(frame.CaptureDraft().Query.EventSequenceConstraints.Count == 2 && frame.CaptureDraft().Query.Players.All(p => p.Conditions.EventResultConditions.Count == 1),
                "Shared event clear can be undone without losing either player's result");
            // Keep E while clearing a different page, then undo while other conditions remain.
            // Replace N's semantic fixture and let ImportQuery derive its editor
            // state; the captured empty-N sidecar describes the previous query.
            var neowParty = roundtrip with {
                Players = roundtrip.Players.Select(player => player with { NeowEditor = null }).ToArray(),
                Query = roundtrip.Query with { Players = roundtrip.Query.Players.Select(p => p with
                    { Conditions = p.Conditions with { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl) } }).ToArray() }
            };
            frame.RestoreDraft(neowParty, false); frame.Refresh("en", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en"));
            Check(frame.CaptureDraft().Query.Players.All(player =>
                player.Conditions.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.GoldenPearl),
                "Both clear-test players begin with the authored Golden Pearl opening");
            Call("SelectDomain", "neow"); frame.ClearCategoryConditions();
            var afterClear = frame.CaptureDraft();
            bool HasN(PlayerOfferQuery p) => !p.Offers.IsEmpty || !p.Conditions.LegacyNeow.NeowRelics.IsEmpty || p.Conditions.OpeningRoute is not null;
            Check(!HasN(afterClear.Query.Players[0]) && HasN(afterClear.Query.Players[1]) && afterClear.Query.EventSequenceConstraints.Count == 2,
                "Clear this page only removes the active player's private N conditions");
            frame.UndoClearConditions();
            Check(frame.CaptureDraft().Query.Players.All(HasN), "Partial clear undo remains available while other categories have conditions");

            var libraryStore = new SeedLibraryStore(Path.Combine(directory, "library"), []);
            var canvas = new SeedLibraryCanvas(libraryStore, store);
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root.AddChild(canvas);
            void ApplyLibraryPresentation()
            {
                // Existing synchronous structural fixture; actual frame/layout
                // readiness is covered by the native Library lifecycle suite.
                for (int i = 0; i < 10000; i++)
                {
                    canvas._Process(0);
                    if (typeof(SeedLibraryCanvas).GetField("_presentation", flags)!.GetValue(canvas) is null)
                    {
                        Check(!canvas.HasPresentationFailure, "Library structural presentation did not fail");
                        return;
                    }
                }
                throw new InvalidOperationException("Library structural presentation did not settle");
            }
            try
            {
                var savedPreset = store.Presets.SaveUserPreset("Native history", "", [], frame.CapturePreset(draft));
                store.PruneAndFlushQueryHistory();
                var history = store.QueriesForPreset(savedPreset).Single();
                frame.LoadQueryHistory(history, "Native history");
                Check(QueryHistoryIdentity.Key(frame.CaptureDraft()) == history.QueryKey && store.QueriesForPreset(savedPreset).Single().Results.Count == 1,
                    "Loading a history query uses the preset round-trip transaction without replacing saved results");
                QueryHistoryEntry? reloadedHistory = null;
                canvas.QueryLoadRequested += (entry, _) => reloadedHistory = entry;
                foreach (string language in new[] { "zh", "en" })
                {
                    canvas.Refresh(language);
                    ApplyLibraryPresentation();
                    ((Godot.Button)canvas.FindChild("SeedLibraryHistory", true, false)).EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    ApplyLibraryPresentation();
                    Check(canvas.FindChildren("*", "PanelContainer", true, false).Count > 0, "History tab renders saved query rows");
                    canvas.ShowPresetResults(savedPreset);
                    ApplyLibraryPresentation();
                    Godot.Button[] ListRows() => canvas.FindChildren("*", "Button", true, false).OfType<Godot.Button>()
                        .Where(b => b.HasMeta("entry_id")).ToArray();
                    var listRows = ListRows();
                    Check(listRows.Length > 0 && (long)typeof(SeedLibraryCanvas).GetField("_historyRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)! == store.ResultsRevision,
                        "History entry rendering synchronizes its observed revision immediately");
                    var conditionButton = canvas.FindChildren("*", "Button", true, false).OfType<Godot.Button>()
                        .Single(b => b.Text == (language == "zh" ? "查看条件" : "Conditions"));
                    conditionButton.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    ApplyLibraryPresentation();
                    Check(canvas.FindChild("LibraryConditionSummary", true, false) is not null,
                        "History conditions render inline in the common detail pane");
                    Check(ListRows().Select(b => b.GetInstanceId()).SequenceEqual(listRows.Select(b => b.GetInstanceId())),
                        "Changing history detail tabs retains every list node");
                    var summary = canvas.FindChild("LibraryConditionSummary", true, false);
                    ListRows().Single(b => b.GetMeta("selected").AsBool()).EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    ((Godot.Button)canvas.FindChild("HistoryConditions", true, false)).EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    ApplyLibraryPresentation();
                    Check(ReferenceEquals(summary, canvas.FindChild("LibraryConditionSummary", true, false)) &&
                        ListRows().Select(b => b.GetInstanceId()).SequenceEqual(listRows.Select(b => b.GetInstanceId())),
                        "Repeated selection of the same history row or detail tab does not rebuild details or list");
                    ((Godot.Button)canvas.FindChild("ReloadHistory", true, false)).EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    Check(reloadedHistory?.Id == history.Id, "History reload keeps the original query identity");
                    var marked = store.Presets.UpdateUserEnvironment(savedPreset.Id, store.CurrentEnvironmentFingerprint);
                    canvas.ShowSection(SeedLibraryCanvas.LibrarySection.Presets);
                    ApplyLibraryPresentation();
                    var presetRow = ListRows().Single(b => b.GetMeta("entry_id").AsString() == marked.Id);
                    presetRow.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    ApplyLibraryPresentation();
                    var notice = (Godot.Label)canvas.FindChild("LibraryEnvironmentNotice", true, false);
                    Check(notice.Text == RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(language)
                        .Get("ui1.library.environment.preset_marker_same"),
                        "Personal presets explain their own matching marker separately from historical evidence in both languages");
                    var presetRows = ListRows().Select(b => b.GetInstanceId()).ToArray();
                    presetRow.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                    canvas.ShowSection(SeedLibraryCanvas.LibrarySection.Presets);
                    ApplyLibraryPresentation();
                    Check(ReferenceEquals(notice, canvas.FindChild("LibraryEnvironmentNotice", true, false)) &&
                        ListRows().Select(b => b.GetInstanceId()).SequenceEqual(presetRows),
                        "Repeated preset/section selection retains the rendered list and detail");
                }
            }
            finally { canvas.GetParent()?.RemoveChild(canvas); canvas.Free(); }
            Console.WriteLine($"RetainedResults checks={checks};PASS;searchSidebar;restart;queryContextPreserved;partyEvents;categoryClear;historyUI;zh+en");
            return 0;
        }
        finally { frame.CloseSearch(); references.Free(); shell.Free(); }
    }

    private static void CheckSettingsSearchStartOrigin(ModRuntimeSnapshot runtime, string directory, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(object owner, string name) => owner.GetType().GetField(name, flags)!.GetValue(owner);
        void Set(object owner, string name, object value) => owner.GetType().GetField(name, flags)!.SetValue(owner, value);
        void Call(object owner, string name, params object[] values) => owner.GetType().GetMethod(name, flags)!.Invoke(owner, values);
        var text = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en");
        foreach (bool random in new[] { false, true })
        foreach (bool reopen in new[] { false, true })
        {
            string label = (random ? "Random" : "Apply") + (reopen ? "/reopened" : "/live");
            string path = Path.Combine(directory, "settings-start", label.Replace('/', '-'));
            var store = new SearchWorkspace(path, false);
            store.SetWorkbenchSearchMode("CPU"); store.Preferences.SearchWorkerBudget = 1;
            store.SetSkipExactValidation(true);
            store.EnsureEnvironment(SearchEnvironmentSignatureBuilder.Capture(runtime));
            store.InitializeWorkbenchCursor();
            var frame = new QueryWorkbenchFrame(runtime, store) { Size = new(1536, 772) };
            var shell = new WorkspaceShell();
            shell.InitializeWithPersistence(runtime, path);
            ((QueryWorkbenchFrame)Field(shell, "_references")!).Free();
            Set(shell, "_persistence", store); Set(shell, "_references", frame);
            ((Godot.Control)Field(shell, "_content")!).AddChild(frame);
            try
            {
                frame.RestoreDraft(new(BaseGameModelKeys.Characters.Ironclad, 10, SearchQuery.Empty,
                    AncientOptionConditionProfile.BroadDefault), render: false);
                frame.Refresh("en", text);
                var draft = frame.CaptureDraft();
                var compiled = draft.Compile(runtime, out _);
                var priorPlan = ExactSearchExecutionRequestFactory.Compile(compiled,
                    new("000000000005", 1, 1, 1, SkipExactValidation: true)).Plan!;
                const ulong oldFrontier = 17;
                var priorContinuation = new SearchContinuationSnapshot(priorPlan.CompiledSearch.SemanticFingerprint,
                    priorPlan.SnapshotFingerprint, oldFrontier, [], []) {
                    ContinuationFingerprint = SearchContinuationIdentity.For(priorPlan), UnreceivedOrdinals = [] };
                store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(priorPlan.CompiledSearch.SemanticFingerprint, draft, skipExactValidation: true));
                store.RecentQueries.Single().Continuation = priorContinuation;
                store.Workspace.ResultContinuation = priorContinuation;
                store.ObserveSafeNextCursor(oldFrontier); store.FlushAll();
                check(store.ContinuationFor(draft, priorPlan)?.NextOrdinal == oldFrontier,
                    label + ": same-query history has a compatible continuation before Settings changes the origin");

                // Build the production Settings controls with the same persistence
                // owner as the real Workbench. Emit their signals, not the helper.
                Set(shell, "_languageCode", "en");
                Call(shell, "EnsurePageViewSynchronously", 2); Call(shell, "LocalizeSettings", text);
                var input = (Godot.LineEdit)Field(shell, "_originInput")!;
                input.Text = "0000000000AB";
                // Assigning LineEdit.Text does not represent native text entry's
                // TextChanged signal; deliver that same UI event before clicking.
                input.EmitSignal(Godot.LineEdit.SignalName.TextChanged, input.Text);
                var originButton = (Godot.Button)Field(shell, random ? "_originRandom" : "_originApply")!;
                check(!originButton.Disabled, label + ": actual Settings origin button is available; busy=" +
                    store.Runtime.HasSearchInFlight + "; input=" + input.Text + "; feedback=" + ((Godot.Label)Field(shell, "_originFeedback")!).Text);
                originButton.EmitSignal(Godot.Button.SignalName.Pressed);
                if (random && store.CurrentPersistentOriginOrdinal == oldFrontier)
                    originButton.EmitSignal(Godot.Button.SignalName.Pressed);
                string selected = input.Text;
                check(selected == store.CurrentPersistentOriginSeed && store.CurrentPersistentOriginOrdinal != oldFrontier &&
                    store.Cursor.PendingOriginChangeId.Length > 0 && (random || selected == "0000000000AB"),
                    label + ": Settings button records the selected start independently from the old continuation");
                if (reopen)
                {
                    frame.CloseSearch(); frame.Free(); store.FlushAll();
                    store = new SearchWorkspace(path, false);
                    frame = new QueryWorkbenchFrame(runtime, store) { Size = new(1536, 772) };
                    frame.RestoreDraft(store.LoadWorkbench()!, render: false); frame.Refresh("en", text);
                    check(store.CurrentPersistentOriginSeed == selected && store.Cursor.PendingOriginChangeId.Length > 0 &&
                        store.ContinuationFor(frame.CaptureDraft(), priorPlan)?.NextOrdinal == oldFrontier,
                        label + ": persisted Settings intent survives reopening beside the older compatible continuation");
                }
                var target = (Godot.SpinBox)Field(frame, "_targetCount")!;
                target.Value = 1;
                target.EmitSignal(Godot.Range.SignalName.ValueChanged, target.Value);
                check((int)Field(frame, "_target")! == 1 && store.Preferences.SearchMode == "CPU" &&
                    store.Preferences.SkipExactValidation,
                    label + ": native target input event binds target one while CPU and candidate settings remain selected");
                var start = (Godot.Button)Field(frame, "_start")!;
                check(!start.Disabled, label + ": actual Workbench Start button is available");
                store.RecordPresentationFailure(new InvalidOperationException("integration.transform.capacity",
                    new Exception("legacy-transform-admission")), "QueryAdmission");
                store.RecordPresentationFailure(new InvalidOperationException("integration.transform.capacity",
                    new Exception("legacy-transform-draft-save")), "DraftSave");
                store.FlushAll();
                start.EmitSignal(Godot.Button.SignalName.Pressed);
                var resultPlan = store.ResultBatch?.Start?.Plan;
                check(!store.Workspace.AuxiliaryIssues.Any(detail => detail.StartsWith("QueryAdmission: ", StringComparison.Ordinal) ||
                    detail.StartsWith("DraftSave: ", StringComparison.Ordinal)) &&
                    !((Godot.Label)Field(frame, "_status")!).Text.Contains("integration.transform.capacity", StringComparison.Ordinal) &&
                    !frame.FindChildren("*", "Label", true, false).OfType<Godot.Label>()
                        .Any(node => node.Text.Contains("legacy-transform-", StringComparison.Ordinal)),
                    label + ": a legal native Start and successful save recover old admission/save notices before presenting results");
                store.FlushAll();
                check(!new SearchWorkspace(path, false).Workspace.AuxiliaryIssues.Any(detail =>
                    detail.StartsWith("QueryAdmission: ", StringComparison.Ordinal) || detail.StartsWith("DraftSave: ", StringComparison.Ordinal)),
                    label + ": reopening the committed workspace does not revive recovered admission/save errors");
                check(resultPlan?.CanonicalStartSeed == selected && resultPlan.RunOptions.SkipExactValidation &&
                    resultPlan.RunOptions.TargetMatchCount == 1 && store.Cursor.PendingOriginChangeId.Length == 0,
                    label + ": real StartSearch uses the Settings origin and consumes its token after successful binding; selected=" +
                    selected + "; planStart=" + (resultPlan?.CanonicalStartSeed ?? "<null>") + "; skip=" + resultPlan?.RunOptions.SkipExactValidation +
                    "; target=" + resultPlan?.RunOptions.TargetMatchCount + "; pending=" + store.Cursor.PendingOriginChangeId +
                    "; session=" + store.Runtime.Receiving + "; status=" + ((Godot.Label?)Field(frame, "_status"))?.Text);
                check(SpinWait.SpinUntil(() => { store.Runtime.Tick(.01); Call(frame, "PollProduction", .01); return !frame.HasActiveSearch; }, 15000),
                    label + ": CPU target-one search completes through the production UI delivery path");
            }
            finally
            {
                frame.CloseSearch();
                if (frame.HasActiveSearch)
                    SpinWait.SpinUntil(() => { store.Runtime.Tick(.01); Call(frame, "PollProduction", .01); return !frame.HasActiveSearch; }, 15000);
                frame.Free(); shell.Free();
            }
        }
    }

    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("NativeWorkbenchContract");
        int checks=0;
        void Check(bool ok,string name) { checks++; if(!ok) throw new InvalidOperationException(name); }
        CheckUiTextCatalogReuse(Check);
        string directory=Path.Combine(Environment.CurrentDirectory,".build-evidence","workbench-contract",Guid.NewGuid().ToString("N"));
        var store=new SearchWorkspace(directory,false);
        var frame=new QueryWorkbenchFrame(runtime,store) { Size=new(1536,772) };
        var character=BaseGameModelKeys.Characters.Ironclad;
        var empty=new WorkbenchSearchDraft(character,10,SearchQuery.Empty,AncientOptionConditionProfile.BroadDefault);
        frame.RestoreDraft(empty, render: false);
        var capture=empty.Compile(runtime,out var authority);
        CheckProbabilityPreviewQueue(capture, Check);
        CheckShopCombatAuthoringRanges(runtime, Check);
        CheckEditorStateRoundTrip(runtime, frame, store, directory, Check);
        CheckSettingsSearchStartOrigin(runtime, directory, Check);
        Console.WriteLine($"NATIVE_AUTHORITY effects={authority.EffectAuthority is not null};eventStatic={authority.EffectAuthority?.EventResultStaticCatalogExact};neow={authority.VanillaNeowCatalogExact}");
        var catalog=NeowSearchUiCatalog.CaptureForPicker(runtime,character,10,1,0,MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress());
        var eventCard=Beta111MorphicGroveAuthorityCapture.CaptureAuthoredInitialBasics(authority).Targets[0].OrderedSourceCandidates![0].CardKey;
        var card=catalog.TransformCards[0]; var rare=catalog.RareCharacterCards[0]; var relic=catalog.OrdinaryRelics[0];
        SearchQuery Source(ModelKey source) => SearchQuery.Empty with { OpeningRoute=new(source) };
        var qn=Source(BaseGameModelKeys.Relics.LeafyPoultice) with { StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.LeafyPoultice,
            NeowStructuredConditionKind.ExactUnorderedPair,NeowStructuredEffectScope.TransformResults,NeowStructuredOutputKind.Card,[card,card],AllowDuplicateOutputs:true)] };
        var cases=new Dictionary<string,SearchQuery>
        {
            ["N.identity"]=Source(BaseGameModelKeys.Relics.GoldenPearl),
            ["N.Leafy.duplicate"]=qn,
            ["N.Arcane"]=Source(BaseGameModelKeys.Relics.ArcaneScroll) with { StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.ArcaneScroll,
                NeowStructuredConditionKind.ExactSingle,NeowStructuredEffectScope.ProductRelevantEffects,NeowStructuredOutputKind.Card,[rare])] },
            ["R.prefix"]=SearchQuery.Empty with { RelicSequenceConstraints=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.FirstN,5,new([relic],[],[]))] },
            ["E.fake"]=SearchQuery.Empty with { EventResultConditions=[new(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[0])] },
            ["E.trash"]=SearchQuery.Empty with { EventResultConditions=[new(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0]),new(EventResultConditionKind.TrashHeapDiveRelic,Beta111EventResultCatalog.TrashHeapDiveRelics[0])] },
            ["E.color"]=SearchQuery.Empty with { EventResultConditions=[new(EventResultConditionKind.ColorfulPhilosophersOfferedColor,BaseGameModelKeys.Characters.Silent)] },
            ["E.morphic"]=SearchQuery.Empty with { EventResultConditions=[new(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains,eventCard) {MorphicGroveSecondCard=eventCard}] },
            ["T.Leafy"]=Source(BaseGameModelKeys.Relics.LeafyPoultice) with {TransformationAggregate=new(TransformationOpening.LeafyPoultice,
                TransformationPickupOrder.Any,false,false,false,TransformationAggregatePredicate.RareCountAtLeast,1,[])},
            ["T.Bones.order"]=Source(BaseGameModelKeys.Relics.NeowsBones) with {
                OpeningRouteRelicRequirement=new(BaseGameModelKeys.Relics.NeowsBones,[BaseGameModelKeys.Relics.NewLeaf,BaseGameModelKeys.Relics.LeafyPoultice],BonesRouteOrderMode.ExactOrder),
                TransformationAggregate=new(TransformationOpening.BonesLeafyNewLeaf,TransformationPickupOrder.NewLeafThenLeafy,false,false,false,TransformationAggregatePredicate.ContainsMultiset,0,[card,card])}
        };
        cases["T.Bones.other"]=Source(BaseGameModelKeys.Relics.NeowsBones) with {
            OpeningRouteRelicRequirement=new(BaseGameModelKeys.Relics.NeowsBones,[BaseGameModelKeys.Relics.ArcaneScroll,BaseGameModelKeys.Relics.LeafyPoultice],BonesRouteOrderMode.ExactOrder),
            TransformationAggregate=new(TransformationOpening.BonesLeafyOther,TransformationPickupOrder.CompanionThenLeafy,false,false,false,TransformationAggregatePredicate.RareCountAtLeast,1,[])
                { CompanionRelic=BaseGameModelKeys.Relics.ArcaneScroll } };
        var mixedEventSeat=Private(Private(frame,"_eventEditor"),"_draft");
        var mixedEventCatalog=(RolltheSpire2.Ui.Pages.Search.Event.EventSequenceSearchUiCatalog)mixedEventSeat.GetType().GetField("Catalog")!.GetValue(mixedEventSeat)!;
        int EventAct(string entry)=>Enumerable.Range(1,3).First(act=>mixedEventCatalog.CandidatesForAct(act).Any(e=>e.EventKey.Entry==entry));
        var mixedEvents=SearchQuery.Empty with {
            EventSequenceConstraints=[new(EventAct("SYMBIOTE"),null,SearchSequenceRangeMode.FirstN,5,new([new("EVENT","SYMBIOTE")],[],[])),
                new(EventAct("TRIAL"),null,SearchSequenceRangeMode.FirstN,5,new([new("EVENT","TRIAL")],[],[]))],
            TransformationAggregate=new(TransformationOpening.None,TransformationPickupOrder.Any,false,false,false,
                TransformationAggregatePredicate.ContainsMultisetAndRemainingRare,0,[eventCard,eventCard]) {Symbiote=true,TrialNondescript=true} };
        frame.RestoreDraft(empty with {Query=mixedEvents},render:false);
        var mixedEventsDraft=frame.CaptureDraft();
        Check(mixedEventsDraft.Query.TransformationAggregate is {Symbiote:true,TrialNondescript:true,
            Predicate:TransformationAggregatePredicate.ContainsMultisetAndRemainingRare,TargetMultiset.Count:2},
            "Both new event sources retain their shared mixed objective");
        store.SaveWorkbench(mixedEventsDraft);
        frame.RestoreDraft(new SearchWorkspace(directory,false).LoadWorkbench()!,render:false);
        Check(frame.CaptureDraft().Query.TransformationAggregate!.TargetMultiset.SequenceEqual([eventCard,eventCard]),
            "Mixed event targets survive persisted workbench restore");
        frame.RestoreDraft(empty,render:false);
        cases["T.Bones.mixed"]=cases["T.Bones.order"] with { TransformationAggregate=cases["T.Bones.order"].TransformationAggregate! with {
            Predicate=TransformationAggregatePredicate.ContainsMultisetAndRemainingRare,TargetMultiset=[card] },
            CombatCardRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[catalog.CommonCharacterCards.First()]) };
        // Actual world catalog supplies identities, never invented Ancient/Variant keys.
        var world=RolltheSpire2.Ui.Pages.Search.BossMap.BossMapSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId,10,authority.WorldAuthority);
        var variant=world.Sections.First().Variants.First();
        cases["W.boss"]=SearchQuery.Empty with {VariantBossBranches=[new(1,variant.ActKey,new([variant.Bosses.First()],[],[]),ModelKeySetFilter.Empty,false)]};
        var finalVariant = world.Sections.Single(section => section.Act == 3).Variants.First(row => row.Bosses.Count >= 2);
        var firstFinalBoss = finalVariant.Bosses[0]; var secondFinalBoss = finalVariant.Bosses[1];
        cases["W.doubleBoss.ordered"] = SearchQuery.Empty with { VariantBossBranches =
            [new(3, finalVariant.ActKey, new([firstFinalBoss], [], []), new([secondFinalBoss], [], []), true)] };
        cases["W.doubleBoss.reversed"] = SearchQuery.Empty with { VariantBossBranches =
            [new(3, finalVariant.ActKey, new([secondFinalBoss], [], []), new([firstFinalBoss], [], []), true)] };
        cases["W.doubleBoss.presence"] = SearchQuery.Empty with { VariantBossBranches =
            [new(3, finalVariant.ActKey, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, true)] };
        cases["W.doubleBoss.unscoped"] = SearchQuery.Empty with { LegacyWorld = LegacyWorldSemanticConstraints.Empty with
            { BossOrdinalFilters = [new(3, 1, new([firstFinalBoss], [], [])), new(3, 2, new([secondFinalBoss], [], []))] } };
        Check(world.Sections.All(section => section.ShowSecondBoss == (section.Act == 3)) &&
            RolltheSpire2.Ui.Pages.Search.BossMap.BossMapSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId, 9, authority.WorldAuthority)
                .Sections.All(section => !section.ShowSecondBoss), "Second Boss candidates are offered only in Act 3 at A10");
        var events=RolltheSpire2.Ui.Pages.Search.Event.EventSequenceSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId,authority.WorldAuthority);
        var eventKey=events.CandidatesForAct(1).First().EventKey;
        cases["W.queue"]=SearchQuery.Empty with {EventSequenceConstraints=[new(1,null,SearchSequenceRangeMode.FirstN,3,new([eventKey],[],[]))]};
        var ac=RolltheSpire2.Ui.Pages.Search.Ancient.AncientSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId,character,authority.WorldAuthority);
        var ancient=ac.Sections.First().Rows.First(r=>r.IdentityAvailable&&r.Options.Count>0);
        var option=ancient.Options.First().OptionKey;
        cases["A.any"]=SearchQuery.Empty with {AncientBranches=[new(2,ancient.AncientKey,[option],[])]};
        cases["A.all"]=SearchQuery.Empty with {AncientBranches=[new(2,ancient.AncientKey,[],[])],
            LegacyWorld=LegacyWorldSemanticConstraints.Empty with {AncientOptionFilters=[new(2,new([], [option], []))]} };
        cases["C.three"]=SearchQuery.Empty with {CombatCardRewards=new(3,CombatRewardSequenceOrderMode.Unordered,[catalog.CommonCharacterCards.First(),null,null])};
        cases["C.potions.six"] = SearchQuery.Empty with { CombatPotionRewards = new(6, CombatRewardSequenceOrderMode.Ordered,
            [new(CombatPotionSlotRequirement.DropAny,null), new(CombatPotionSlotRequirement.NoDrop,null),
             new(CombatPotionSlotRequirement.Neutral,null), new(CombatPotionSlotRequirement.NoDrop,null),
             new(CombatPotionSlotRequirement.DropAny,null), new(CombatPotionSlotRequirement.NoDrop,null)]) };
        cases["C.cards-and-potions"] = cases["C.three"] with { CombatPotionRewards = new(2, CombatRewardSequenceOrderMode.Ordered,
            [new(CombatPotionSlotRequirement.NoDrop,null), new(CombatPotionSlotRequirement.DropAny,null)]) };
        foreach (int horizon in new[] {4,5,6})
        foreach (var order in Enum.GetValues<CombatRewardSequenceOrderMode>())
            cases[$"C.h{horizon}.{order}"]=SearchQuery.Empty with { CombatCardRewards=new(horizon,order,
                Enumerable.Range(0,horizon).Select(i => i == horizon-1 ? (ModelKey?)catalog.RareCharacterCards.First() : null).ToArray()) };
        cases["M.act"]=SearchQuery.Empty with {StandardMaps=[new(1,StandardMapMetric.ReachableMaxElite,StandardMapComparison.AtLeast,3,true)]};
        cases["M.nohits"]=SearchQuery.Empty with {StandardMaps=[new(1,StandardMapMetric.ReachableMaxElite,StandardMapComparison.AtLeast,99,true)]};
        cases["M.whole"]=SearchQuery.Empty with {StandardMaps=[new(0,StandardMapMetric.ReachableMaxRest,StandardMapComparison.AtLeast,9,true)]};
        cases["M.properties"]=SearchQuery.Empty with {StandardMaps=[new(2,StandardMapMetric.GuaranteedUnknown,StandardMapComparison.AtLeast,4),new(2,StandardMapMetric.ReachableMaxMonster,StandardMapComparison.AtMost,8)]};
        var archived=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        if(Environment.GetEnvironmentVariable("RT2_BONES_C_CHECK")=="1") { VerifyBonesCombat(archived);return 0; }
        if(Environment.GetEnvironmentVariable("RT2_C6_BENCH")=="1") return BenchmarkC6(archived);
        CheckConfiguredCpuPreview(archived, Check);
        foreach(var kind in new[] { EventResultConditionKind.SymbioteInitialBasicTransform,EventResultConditionKind.AromaOfChaosInitialBasicTransform,
            EventResultConditionKind.WhisperingHollowInitialBasicTransform,EventResultConditionKind.TrialNondescriptInitialBasicsContains })
            cases["E."+kind]=SearchQuery.Empty with {EventResultConditions=[new(kind,eventCard)]};
        cases["E.trial-pair"]=SearchQuery.Empty with {EventResultConditions=[new(EventResultConditionKind.TrialNondescriptInitialBasicsContains,eventCard) {MorphicGroveSecondCard=eventCard}]};
        foreach(var trial in Enum.GetValues<TrialCaseTarget>()) cases["E.trial-"+trial]=SearchQuery.Empty with {
            EventResultConditions=[new(EventResultConditionKind.TrialCase,new("EVENT","TRIAL")) {TrialCase=trial}]};
        foreach(var type in Enum.GetValues<TinkerCardTypeTarget>()) {
            cases["E.tinker-"+type]=SearchQuery.Empty with {EventResultConditions=[new(EventResultConditionKind.TinkerTimeTypeAndRider,new("CARD","MAD_SCIENCE")) {TinkerCardType=type}]};
            cases["E.tinker-rider-"+type]=SearchQuery.Empty with {EventResultConditions=[new(EventResultConditionKind.TinkerTimeTypeAndRider,new("CARD","MAD_SCIENCE")) {TinkerCardType=type,TinkerRider=(TinkerRiderTarget)((int)type*3)}]};
        }
        var rCatalog=RolltheSpire2.Ui.Pages.Search.Relic.RelicSequenceSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId,archived.Authority.WorldAuthority);
        var commonRelic=rCatalog.LaneCandidates[RelicSequenceKind.Common].First();
        cases["R.prefix"]=SearchQuery.Empty with {RelicSequenceConstraints=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.FirstN,5,new([commonRelic],[],[]))]};
        cases["R.exclude"]=SearchQuery.Empty with {RelicSequenceConstraints=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.ExactSlot,1,new([],[],[commonRelic]))]};
        cases["N.scroll.bundle"]=Source(BaseGameModelKeys.Relics.ScrollBoxes) with {StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.ScrollBoxes,
            NeowStructuredConditionKind.StructuredCardComposition,NeowStructuredEffectScope.SelectableOfferGroups,NeowStructuredOutputKind.Card,
            [catalog.CommonCharacterCards[0],catalog.CommonCharacterCards[1],catalog.UncommonCharacterCards[0]])]};
        if(catalog.RouteRelics.Contains(BaseGameModelKeys.Relics.Kaleidoscope))
            cases["N.kaleidoscope.position"]=Source(BaseGameModelKeys.Relics.Kaleidoscope) with {StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.Kaleidoscope,
                NeowStructuredConditionKind.IndependentOfferGroupTargets,NeowStructuredEffectScope.SelectableOfferGroups,NeowStructuredOutputKind.Card,
                [catalog.OtherCharacterCards[0]],AllowDuplicateOutputs:true,KaleidoscopeGroupOrder:KaleidoscopeGroupOrderMode.ExactOrder)
                {KaleidoscopePositionalSlots=[null,catalog.OtherCharacterCards[0]]}]};
        cases["N.capsules.joint"]=Source(BaseGameModelKeys.Relics.NeowsBones) with {
            OpeningRouteRelicRequirement=new(BaseGameModelKeys.Relics.NeowsBones,[BaseGameModelKeys.Relics.LargeCapsule,BaseGameModelKeys.Relics.SmallCapsule]),
            StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.NeowsBones,NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                NeowStructuredEffectScope.NestedRelics,NeowStructuredOutputKind.Relic,catalog.OrdinaryRelics.Take(3).ToArray())]};
        var secondOption=ancient.Options.Select(o=>o.OptionKey).First(o=>o!=option&&ancient.CanOfferTogether([option,o]));
        cases["A.all"]=cases["A.all"] with {LegacyWorld=LegacyWorldSemanticConstraints.Empty with {AncientOptionFilters=[new(2,new([],[option,secondOption],[]))]}};
        var ancientModes = new AncientEditorStateSnapshot(0, 2,
        [
            new([new(2, ancient.AncientKey, [option], null)], false, [], 1),
            new([new(2, ancient.AncientKey, [secondOption], null)], false, [], 2),
            new([new(2, ancient.AncientKey, [option, secondOption], null)], false, [], 1)
        ]);
        frame.RestoreDraft(empty with { Query = cases["A.any"], AncientEditor = ancientModes }, render: false);
        var ancientEditor = (AncientSearchEditor)Private(frame, "_ancientEditor");
        void SelectAncientMode(int mode) => typeof(AncientSearchEditor)
            .GetMethod("SelectMode", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ancientEditor, [mode]);
        Check(frame.CaptureDraft().Query.AncientBranches.Single().OptionAny.SequenceEqual([option]),
            "Default Ancient mode keeps its own option");
        SelectAncientMode(1);
        Check(frame.CaptureDraft().Query.AncientBranches.Single().OptionAny.SequenceEqual([secondOption]),
            "Any-offered Ancient mode keeps its own option");
        SelectAncientMode(2);
        Check(frame.CaptureDraft().Query.LegacyWorld.AncientOptionFilters.Single().Keys.All.SequenceEqual([option, secondOption]),
            "Offered-group Ancient mode keeps its own option group");
        store.SaveWorkbench(frame.CaptureDraft());
        var savedAncientModes = new SearchWorkspace(directory, false).LoadWorkbench()!;
        frame.RestoreDraft(savedAncientModes, render: false);
        SelectAncientMode(0);
        Check(frame.CaptureDraft().Query.AncientBranches.Single().OptionAny.SequenceEqual([option]) &&
              frame.CaptureDraft().AncientEditor!.Modes[1].Rows.Single().Options.SequenceEqual([secondOption]),
            "All three Ancient mode drafts survive save and restore");
        cases["T.event"]=SearchQuery.Empty with {
            EventSequenceConstraints=[new(1,null,SearchSequenceRangeMode.FirstN,3,new([new ModelKey("EVENT","MORPHIC_GROVE")],[],[]))],
            TransformationAggregate=new(TransformationOpening.None,TransformationPickupOrder.Any,true,false,false,TransformationAggregatePredicate.RareCountAtLeast,1,[])};
        var colorless=catalog.ColorlessCards.First(k=>catalog.CardPickerMetadata[k].Rarity==RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon);
        cases["S.colorless"]=SearchQuery.Empty with {MerchantColorlessSequenceConditions=[new(3,CombatRewardSequenceOrderMode.Unordered,MerchantColorlessSlot.Uncommon,[colorless,null,null])]};
        cases["R.shop"]=SearchQuery.Empty with {RelicShopSequenceConditions=[new(3,CombatRewardSequenceOrderMode.Ordered,[rCatalog.LaneCandidates[RelicSequenceKind.Shop].First(),null,null])]};
        frame.RestoreDraft(empty with { Query = cases["N.capsules.joint"] }, render: false);
        Check(((NeowSearchEditor)Private(frame,"_neowEditor")).AuthoredConditionCount()==3,
            "Navigation N counts identity, nested pair, and joint result groups");
        frame.RestoreDraft(empty with { Query = cases["A.all"] }, render: false);
        Check(((AncientSearchEditor)Private(frame,"_ancientEditor")).AuthoredConditionCount()==2,
            "Navigation A counts an identity and its two-option group");
        frame.RestoreDraft(empty with { Query = cases["S.colorless"] }, render: false);
        Check(((ShopSearchEditor)Private(frame,"_shopEditor")).AuthoredConditionCount()==1,
            "Navigation S counts an unordered shop set once");
        var otherColorless = catalog.ColorlessCards.First(key => key != colorless &&
            catalog.CardPickerMetadata[key].Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon);
        frame.RestoreDraft(empty with { Query = SearchQuery.Empty with { MerchantColorlessSequenceConditions =
            [new(3, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon,
                [colorless, null, otherColorless])] } }, render: false);
        Check(((ShopSearchEditor)Private(frame,"_shopEditor")).AuthoredConditionCount()==2,
            "Navigation S counts independently specified shop positions");
        frame.RestoreDraft(empty with { Query = cases["C.three"] }, render: false);
        Check(((CombatRewardSearchEditor)Private(frame,"_combatEditor")).AuthoredConditionCount()==1,
            "Navigation C counts a battle sequence once");
        frame.RestoreDraft(empty with { Query = cases["E.fake"] with
            { EventSequenceConstraints = cases["W.queue"].EventSequenceConstraints } }, render: false);
        Check(((EventSearchEditor)Private(frame,"_eventEditor")).AuthoredConditionCount()==2,
            "Navigation E retains queue and result requirements together");
        frame.RestoreDraft(empty with { Query = SearchQuery.Empty with { EventResultConditions =
            [new(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards.First()),
             new(EventResultConditionKind.TrashHeapDiveRelic, Beta111EventResultCatalog.TrashHeapDiveRelics.First())] } }, render: false);
        Check(((EventSearchEditor)Private(frame,"_eventEditor")).AuthoredConditionCount()==1,
            "Navigation E counts two targets from one event as one result group");
        frame.RestoreDraft(empty with { Query = cases["W.boss"] with
            { StandardMaps = cases["M.properties"].StandardMaps } }, render: false);
        var actEditor = (ActInformationSearchEditor)Private(frame,"_actInformationEditor");
        Check(actEditor.BossVariantConditionCount()==2 && actEditor.MapConditionCount()==2 &&
            frame.CaptureDraft().Query.VariantBossBranches.Single().VariantKey == variant.ActKey,
            "Navigation retains the authored variant and Boss separately from map properties");
        // U17 is an export/feasibility model check, not a claim that this machine's
        // current act catalog happens to contain two disjoint boss variants.
        // Reuse the existing synthetic authority shape and explicitly define them.
        var syntheticWorld = Beta109WorldFixtureFactory.Create();
        var syntheticGeneration = syntheticWorld.Beta109Generation!;
        var template = syntheticGeneration.OrderedActCatalog.Single(a => a.Act == 1);
        var variantA = new ModelKey(template.ActKey.Category, "U17_VARIANT_A");
        var variantB = new ModelKey(template.ActKey.Category, "U17_VARIANT_B");
        var bossA = new ModelKey(BaseGameModelKeys.Categories.Encounter, "U17_BOSS_A");
        var bossB = new ModelKey(BaseGameModelKeys.Categories.Encounter, "U17_BOSS_B");
        RolltheSpire2.Core.World.Snapshots.Beta109ActGenerationSnapshot SyntheticVariant(ModelKey key, ModelKey boss) =>
            template with { ActKey = key, OrderedGenerateAllEncounters = template.OrderedGenerateAllEncounters
                .Select(e => e.RoomType == RolltheSpire2.Core.World.Snapshots.Beta109RoomType.Boss ? e with { EncounterKey = boss } : e).ToArray() };
        var actA = SyntheticVariant(variantA, bossA); var actB = SyntheticVariant(variantB, bossB);
        Check(actA.HasExactGenerationInputs && actB.HasExactGenerationInputs,
            "U17 synthetic variants declare complete feasibility facts");
        syntheticWorld = syntheticWorld with { Beta109Generation = syntheticGeneration with {
            Profile = runtime.Profile.ProfileId,
            OrderedActCatalog = new[] { actA, actB }.Concat(syntheticGeneration.OrderedActCatalog.Where(a => a.Act != 1)).ToArray(),
            ActSelectionGroups = syntheticGeneration.ActSelectionGroups.Select(g => g.Act == 1
                ? g with { EligibleActsInSourceOrder = new[] { variantA, variantB } } : g).ToArray(),
            SelectedActs = new[] { variantA }.Concat(syntheticGeneration.SelectedActs.Skip(1)).ToArray(),
            CatalogFingerprint = "U17:SyntheticExportFeasibilityOnly" } };
        var modelContext = (empty with { Query = cases["W.boss"] }).Compile(runtime, out _).Context;
        modelContext = modelContext with { Authority = modelContext.Authority.WithWorldAuthority(syntheticWorld) };
        var worldSeat = Private(actEditor, "_draft");
        worldSeat.GetType().GetField("Catalog")!.SetValue(worldSeat,
            RolltheSpire2.Ui.Pages.Search.BossMap.BossMapSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId, 10, syntheticWorld));
        var impossibleWorld = SearchQuery.Empty with { VariantBossBranches =
            [new(1, variantA, new([bossB], [], []), ModelKeySetFilter.Empty, false)] };
        actEditor.ImportQuery(impossibleWorld);
        var exportedWorld = actEditor.ExportQuery(SearchQuery.Empty);
        Check(exportedWorld.VariantBossBranches.Count == 1 &&
            exportedWorld.VariantBossBranches.Single().VariantKey == variantA &&
            exportedWorld.VariantBossBranches.Single().FirstBoss.Any.SequenceEqual([bossB]),
            "Disjoint selected Variant and Boss export the actual declaration instead of an unconstrained Act");
        var impossibleCompiled = SearchCompiler.Compile(exportedWorld, modelContext);
        var impossibility = SearchFeasibilityAnalyzer.Analyze(impossibleCompiled);
        Check(impossibleCompiled.NormalizedQuery.VariantBossBranches.Count == 1 && impossibility.IsImpossible &&
            impossibility.Proof!.ReasonCode == SearchImpossibilityReasonCode.ActVariantBossCombinationImpossible,
            "Exported disjoint Variant/Boss reaches the existing exact-authority impossibility proof");
        var missingCatalogSeat = Private(actEditor, "_draft");
        missingCatalogSeat.GetType().GetField("Catalog")!.SetValue(missingCatalogSeat,
            RolltheSpire2.Ui.Pages.Search.BossMap.BossMapSearchUiCatalog.Empty(runtime.Profile.ProfileId, "fixture-catalog-missing"));
        var retainedWithoutCatalog = actEditor.ExportQuery(SearchQuery.Empty);
        var unknownContext = impossibleCompiled.Context with { Authority = impossibleCompiled.Context.Authority.WithWorldAuthority(null) };
        var unknownCompiled = SearchCompiler.Compile(retainedWithoutCatalog, unknownContext);
        Check(retainedWithoutCatalog.VariantBossBranches.Single().VariantKey == variantA &&
            retainedWithoutCatalog.VariantBossBranches.Single().FirstBoss.Any.SequenceEqual([bossB]) &&
            !SearchFeasibilityAnalyzer.Analyze(unknownCompiled).IsImpossible,
            "Absent catalog preserves authored Variant/Boss but cannot prove their intersection impossible");
        var bossOnly = SearchQuery.Empty with { LegacyWorld = LegacyWorldSemanticConstraints.Empty with
            { BossOrdinalFilters = [new(1, 1, new([bossB], [], []))] } };
        actEditor.ImportQuery(bossOnly);
        var bossOnlyWithoutCatalog = actEditor.ExportQuery(SearchQuery.Empty);
        var bossOnlyCompiled = SearchCompiler.Compile(bossOnlyWithoutCatalog, unknownContext);
        Check(bossOnlyWithoutCatalog.VariantBossBranches.Count == 0 &&
            bossOnlyWithoutCatalog.LegacyWorld.BossOrdinalFilters.Single() is { Act: 1, Ordinal: 1 } bossOnlyFilter &&
            bossOnlyFilter.Keys.Any.SequenceEqual([bossB]) &&
            bossOnlyCompiled.NormalizedQuery.LegacyWorld.BossOrdinalFilters.Single().Keys.Any.SequenceEqual([bossB]) &&
            !SearchFeasibilityAnalyzer.Analyze(bossOnlyCompiled).IsImpossible,
            "Boss-only declaration survives an empty catalog and compilation without inventing variants or an impossibility proof");
        missingCatalogSeat.GetType().GetField("Catalog")!.SetValue(missingCatalogSeat,
            RolltheSpire2.Ui.Pages.Search.BossMap.BossMapSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId, 10, syntheticWorld));
        Check(JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty)) == JsonSerializer.Serialize(bossOnlyWithoutCatalog),
            "Catalog recovery resolves Boss-only intent without changing its saved declaration");
        frame.RestoreDraft(empty with { Query = cases["W.doubleBoss.ordered"] }, render: false);
        Check(actEditor.BossVariantConditionCount() == 3 && actEditor.HasConditions(false),
            "Double Boss navigation counts the parent and both distinct positions");
        var orderedDoubleBoss = JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty));
        var undoBossClear = actEditor.ClearPageConditions(false, false);
        Check(actEditor.BossVariantConditionCount() == 0 && !actEditor.HasConditions(false),
            "Clearing Boss conditions clears the second position and its existence declaration");
        undoBossClear();
        Check(JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty)) == orderedDoubleBoss,
            "Undo restores both Boss positions and their parent association");
        frame.RestoreDraft(empty with { Ascension = 9, Query = cases["W.doubleBoss.ordered"] }, render: false);
        Check(JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty)) == orderedDoubleBoss,
            "Changing to A9 retains the authored double Boss condition for actual feasibility judgment");
        frame.RestoreDraft(empty with { Query = cases["W.doubleBoss.unscoped"] }, render: false);
        var unscopedDouble = JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty));
        var secondMissingSeat = Private(actEditor, "_draft");
        secondMissingSeat.GetType().GetField("Catalog")!.SetValue(secondMissingSeat,
            RolltheSpire2.Ui.Pages.Search.BossMap.BossMapSearchUiCatalog.Empty(runtime.Profile.ProfileId, "double-boss-catalog-missing"));
        Check(JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty)) == unscopedDouble,
            "Unscoped first and second Boss targets survive a missing catalog without invented variants");
        Check(!ActInformationSearchEditor.CanImportVariantBossBranches(cases["W.doubleBoss.ordered"] with
            { VariantBossBranches = [.. cases["W.doubleBoss.ordered"].VariantBossBranches, .. cases["W.doubleBoss.reversed"].VariantBossBranches] }),
            "Distinct branch-local Boss pairs cannot be flattened into cross-row combinations");
        frame.RestoreDraft(empty, render: false);
        actEditor.Size = new(900, 700);
        actEditor.SelectPage(false);
        var renderActIdentity = typeof(ActInformationSearchEditor).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;
        renderActIdentity.Invoke(actEditor, null);
        Check(actEditor.FindChildren("SecondBoss1_*", "Button", true, false).Count == 0 &&
            actEditor.FindChildren("SecondBoss2_*", "Button", true, false).Count == 0 &&
            actEditor.FindChildren("SecondBoss3_*", "Button", true, false).Count >= 2,
            "A10 authoring offers second-position targets only for the final Act");
        ((Godot.Button)actEditor.FindChildren("FirstBoss3_0", "Button", true, false).Single()).EmitSignal(Godot.BaseButton.SignalName.Pressed);
        ((Godot.Button)actEditor.FindChildren("SecondBoss3_1", "Button", true, false).Single()).EmitSignal(Godot.BaseButton.SignalName.Pressed);
        var clickedBosses = actEditor.ExportQuery(SearchQuery.Empty).LegacyWorld.BossOrdinalFilters;
        Check(clickedBosses.Count == 2 && clickedBosses.Single(b => b.Ordinal == 1).Keys.Any.Single() == firstFinalBoss &&
            clickedBosses.Single(b => b.Ordinal == 2).Keys.Any.Single() == secondFinalBoss,
            "Independent first and second Boss clicks reach distinct production ordinal predicates");
        frame.RestoreDraft(empty with { Ascension = 9 }, render: false);
        renderActIdentity.Invoke(actEditor, null);
        Check(actEditor.FindChildren("SecondBoss*", "Button", true, false).Count == 0,
            "An empty A9 draft does not invent a second Boss slot");
        frame.RestoreDraft(empty with { Query = cases["T.event"] }, render: false);
        Check(((TransformationSearchEditor)Private(frame,"_transformationEditor")).AuthoredConditionCount()==1,
            "Navigation T counts one aggregate condition");
        frame.RestoreDraft(empty with { Query = cases["T.event"] with { EventSequenceConstraints =
            [.. cases["T.event"].EventSequenceConstraints,
             new(1, null, SearchSequenceRangeMode.FirstN, 3,
                 new([new ModelKey("EVENT", "AROMA_OF_CHAOS")], [], []))],
            TransformationAggregate = cases["T.event"].TransformationAggregate! with { AromaOfChaos = true } } }, render: false);
        var aggregateNavigation = (TransformationSearchEditor)Private(frame, "_transformationEditor");
        Check(aggregateNavigation.AuthoredConditionCount() == 1 &&
            aggregateNavigation.ExportEditorState().TakenOver.ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "E.MORPHIC_GROVE", "E.AROMA_OF_CHAOS" }),
            "Navigation T counts one aggregate condition while retaining both managed sources");
        // Inline E authoring preserves legacy E-only intent, shares results across
        // repeated inclusions, and keeps inactive targets when the final inclusion is removed.
        frame.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
        var inlineEvents = (EventSearchEditor)Private(frame, "_eventEditor");
        var fakeKey = new ModelKey("EVENT", "FAKE_MERCHANT");
        inlineEvents.ImportQuery(cases["E.fake"]);
        Check(inlineEvents.ExportQuery(SearchQuery.Empty).EventSequenceConstraints.Count == 0 &&
            inlineEvents.ExportQuery(SearchQuery.Empty).EventResultConditions.Count == 1,
            "Legacy E-only predicates remain unchanged until explicit binding");
        var fakeInclusion = new EventSequenceSearchCondition(1, null, SearchSequenceRangeMode.FirstN, 3,
            new([fakeKey], [], []));
        inlineEvents.ImportQuery(cases["E.fake"] with { EventSequenceConstraints = [fakeInclusion, fakeInclusion with { RangeValue = 5 }] });
        var removeQueue = typeof(EventSearchEditor).GetMethod("RemoveQueueCondition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        removeQueue.Invoke(inlineEvents, [0]);
        Check(inlineEvents.ExportQuery(SearchQuery.Empty).EventResultConditions.Count == 1,
            "Another positive condition retains the event result");
        var ownedEventTargets = inlineEvents.ExportEditorState().Results;
        removeQueue.Invoke(inlineEvents, [0]);
        Check(inlineEvents.ExportQuery(SearchQuery.Empty).EventResultConditions.Count == 0 &&
            JsonSerializer.Serialize(inlineEvents.ExportEditorState().Results) == JsonSerializer.Serialize(ownedEventTargets) &&
            inlineEvents.ExportEditorState().InactiveResultEvents.Contains("FAKE_MERCHANT"),
            "Removing the final inclusion retains its targets and marks their source inactive");
        inlineEvents.ImportQuery(cases["E.fake"] with { EventSequenceConstraints =
            [fakeInclusion with { Keys = new([], [], [fakeKey]) }] });
        removeQueue.Invoke(inlineEvents, [0]);
        Check(inlineEvents.ExportQuery(SearchQuery.Empty).EventResultConditions.Count == 1,
            "Removing an exclusion does not erase an independent legacy result");
        var transformEditor = (TransformationSearchEditor)Private(frame, "_transformationEditor");
        void CheckUnavailableTransform(string sourceId, TransformationEditorStateSnapshot before, string reason)
        {
            string? issue = null;
            try { frame.CaptureDraft(); }
            catch (InvalidOperationException exception) { issue = exception.Message; }
            Check(issue is not null && issue.StartsWith("TransformationDeclaredSourcesUnavailable:", StringComparison.Ordinal) &&
                issue.Contains(sourceId, StringComparison.Ordinal) &&
                JsonSerializer.Serialize(transformEditor.ExportEditorState()) == JsonSerializer.Serialize(before), reason);
        }
        frame.RestoreDraft(empty with { Query = cases["T.event"] }, render: false);
        var eventTakeover = transformEditor.ExportEditorState();
        removeQueue.Invoke(inlineEvents, [0]);
        CheckUnavailableTransform("E.MORPHIC_GROVE", eventTakeover,
            "Removing a queued T source retains its aggregate requirement and identifies the unavailable event source");
        var neowEditor = (NeowSearchEditor)Private(frame, "_neowEditor");
        var sourcePressed = typeof(NeowSearchEditor).GetMethod("OnSourceRelicPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        frame.RestoreDraft(empty with { Query = cases["T.Leafy"] }, render: false);
        var openingTakeover = transformEditor.ExportEditorState();
        sourcePressed.Invoke(neowEditor, [BaseGameModelKeys.Relics.LeafyPoultice]);
        Check(neowEditor.ExportQuery().OpeningRoute is null, "Explicitly removing N's parent source updates that selected opening");
        CheckUnavailableTransform("N.LeafyPoultice", openingTakeover,
            "Removing N's parent source keeps T's authored requirement and identifies the unavailable opening source");
        frame.RestoreDraft(empty with { Query = cases["T.Leafy"] }, render: false);
        openingTakeover = transformEditor.ExportEditorState();
        typeof(NeowSearchEditor).GetMethod("ToggleBonesMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(neowEditor, []);
        CheckUnavailableTransform("N.LeafyPoultice", openingTakeover,
            "Switching N to Bones keeps the old T requirement and identifies its unavailable opening source");
        var clearTakeoverQuery = cases["T.event"] with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
            TransformationAggregate = cases["T.event"].TransformationAggregate! with
            {
                Opening = TransformationOpening.LeafyPoultice,
                Predicate = TransformationAggregatePredicate.ContainsMultiset,
                MinimumRareCount = 0, TargetMultiset = [card, eventCard]
            }
        };
        frame.RestoreDraft(empty with { Query = clearTakeoverQuery }, render: false);
        frame.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
        var beforeClearTakeover = transformEditor.ExportEditorState();
        typeof(QueryWorkbenchFrame).GetField("_selectedDomain", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(frame, "neow");
        frame.ClearCategoryConditions();
        var incompleteAfterClear = transformEditor.ExportEditorState();
        CheckUnavailableTransform("N.LeafyPoultice", incompleteAfterClear,
            "Clearing N retains both N/E takeovers and targets while missing-source execution remains guarded");
        Check(Private(frame, "_restoreClearedConditions") is Action, "Incomplete T state retains ordinary clear undo");
        frame.UndoClearConditions();
        Check(frame.CaptureDraft().Query.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.LeafyPoultice &&
            transformEditor.ExportEditorState().TakenOver.SequenceEqual(beforeClearTakeover.TakenOver) &&
            transformEditor.ExportEditorState().Cards.SequenceEqual(beforeClearTakeover.Cards),
            "Ordinary undo restores the cleared N source and its preserved joint T declaration");
        frame.ClearCategoryConditions();
        incompleteAfterClear = transformEditor.ExportEditorState();
        var editedIncomplete = incompleteAfterClear with { Objective = 2, Cards = [eventCard] };
        transformEditor.ImportEditorState(editedIncomplete);
        CheckUnavailableTransform("N.LeafyPoultice", editedIncomplete,
            "T target/mode edits remain authored while the same cleared source is unavailable");
        frame.UndoClearConditions();
        Check(neowEditor.ExportQuery().OpeningRoute is null &&
            transformEditor.ExportEditorState().Objective == 2 &&
            transformEditor.ExportEditorState().Cards.SequenceEqual([eventCard]) &&
            typeof(QueryWorkbenchFrame).GetField("_restoreClearedConditions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(frame) is null,
            "Editing T between two incomplete captures invalidates undo instead of overwriting the new mode/targets");
        frame.RestoreDraft(empty with { Query = cases["N.identity"] }, render: false);
        var beforeEligibilityClear = frame.CaptureDraft();
        var undoAncient = (AncientSearchEditor)Private(frame, "_ancientEditor");
        frame.ClearAllConditions();
        Check(Private(frame, "_restoreClearedConditions") is Action,
            "Clearing solo conditions retains undo when Ancient eligibility remains unchanged");
        frame.UndoClearConditions();
        Check(beforeEligibilityClear.Query.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.GoldenPearl &&
            frame.CaptureDraft().Query.OpeningRoute?.RouteRelicKey == beforeEligibilityClear.Query.OpeningRoute?.RouteRelicKey &&
            frame.CaptureDraft().AncientPremises == beforeEligibilityClear.AncientPremises,
            "Ordinary solo clear undo restores the opening and its unchanged Ancient eligibility premises");
        frame.ClearAllConditions();
        var beforeEligibilityEdit = frame.CaptureDraft();
        var eligibilityOption = AncientOptionConditionProfile.AuthoredOptionKeys[0];
        Check(undoAncient.OptionConditions.TryGetOptionEligibility(eligibilityOption, out bool eligibilityBefore),
            "Eligibility undo fixture targets an actual authored option switch");
        typeof(AncientSearchEditor).GetMethod("ToggleEligibility", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(undoAncient, [eligibilityOption, eligibilityBefore]);
        var afterEligibilityEdit = frame.CaptureDraft();
        Check(JsonSerializer.Serialize(afterEligibilityEdit.Query) == JsonSerializer.Serialize(beforeEligibilityEdit.Query) &&
            JsonSerializer.Serialize(afterEligibilityEdit.AncientEditor) == JsonSerializer.Serialize(beforeEligibilityEdit.AncientEditor) &&
            afterEligibilityEdit.AncientPremises == beforeEligibilityEdit.AncientPremises.WithOptionEligibility(eligibilityOption, !eligibilityBefore) &&
            afterEligibilityEdit.AncientPremises != beforeEligibilityEdit.AncientPremises,
            "After a solo clear the actual eligibility switch changes only Ancient premises, independently of the query and mode sidecar");
        frame.UndoClearConditions();
        Check(frame.CaptureDraft().Query.OpeningRoute is null &&
            frame.CaptureDraft().AncientPremises == afterEligibilityEdit.AncientPremises &&
            typeof(QueryWorkbenchFrame).GetField("_restoreClearedConditions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(frame) is null,
            "A new Ancient eligibility edit invalidates clear undo instead of restoring old premises over the player's change");
        frame.RestoreDraft(empty with { Query = cases["M.properties"] }, render: false);
        string savedMap = JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty).StandardMaps);
        var changeScope = typeof(ActInformationSearchEditor).GetMethod("SetMapScope", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (int scope in new[] { 1, 0, 3, 2 }) changeScope.Invoke(actEditor, [scope]);
        Check(JsonSerializer.Serialize(actEditor.ExportQuery(SearchQuery.Empty).StandardMaps) == savedMap,
            "Map scope navigation never edits saved conditions");
        frame.RestoreDraft(empty, render: false);

        int parityRoots=0,returned=0;
        var coverage=new List<object>();
        foreach(var (name,query) in cases)
        {
            frame.RestoreDraft(empty with {Query=query}, render: false);
            var authored=frame.CaptureDraft();
            var before=authored.Compile(runtime,out _);
            var expected=(empty with {Query=query}).Compile(runtime,out _);
            if (JsonSerializer.Serialize(authored.WithoutCapturedAuthority().Query,ProductionSearchReplay.JsonOptions)!=JsonSerializer.Serialize(query,ProductionSearchReplay.JsonOptions))
            {
                File.WriteAllText(Path.Combine(directory,"mismatch.json"),JsonSerializer.Serialize(new {name,authored=authored.Query,expected=query,actualFingerprint=before.SemanticFingerprint,expectedFingerprint=expected.SemanticFingerprint},ProductionSearchReplay.JsonOptions));
                Console.WriteLine("MISMATCH "+Path.Combine(directory,"mismatch.json"));
            }
            Check(JsonSerializer.Serialize(authored.WithoutCapturedAuthority().Query,ProductionSearchReplay.JsonOptions)==JsonSerializer.Serialize(query,ProductionSearchReplay.JsonOptions),"UI export semantics: "+name);
            store.SaveWorkbench(authored);
            var restoredStore=new SearchWorkspace(directory,false);
            var saved=restoredStore.LoadWorkbench()!;
            Check(!JsonSerializer.Serialize(saved).Contains("OrderedSourceCandidates"),"No captured pools: "+name);
            frame.RestoreDraft(saved, render: false);
            var after=frame.CaptureDraft().Compile(runtime,out _);
            Check(JsonSerializer.Serialize(frame.CaptureDraft().WithoutCapturedAuthority().Query,ProductionSearchReplay.JsonOptions)==JsonSerializer.Serialize(authored.WithoutCapturedAuthority().Query,ProductionSearchReplay.JsonOptions),"save/reload/compile: "+name);
            // The headless host lacks full game-startup authority. Execute the identical
            // UI-authored intent against the existing GameRuntimeCapture corpus, not invented flags.
            var executionQuery=frame.CaptureDraft().Query;
            executionQuery=executionQuery with { EventResultConditions=executionQuery.EventResultConditions.Select(c=>EventResultTransformSemantics.IsTransform(c.Kind)
                ? c with {MorphicGroveScenario=Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(archived.Authority,c.Kind)} : c).ToArray() };
            if(executionQuery.TransformationAggregate is {UsesEvents:true} t)
                executionQuery=executionQuery with {TransformationAggregate=t with {EventScenario=Beta111MorphicGroveAuthorityCapture.CaptureAuthoredInitialBasics(archived.Authority)}};
            bool archivedWorld=Environment.GetEnvironmentVariable("RT2_WORKBENCH_AUTHORITY")=="corpus"||name.StartsWith("W.",StringComparison.Ordinal)||name.StartsWith("A.",StringComparison.Ordinal)||name=="T.event";
            var executionCompiled=archivedWorld?SearchCompiler.Compile(executionQuery,archived with {EvaluationAssumptions=new(saved.AncientPremises)}):after;
            var request=ExactSearchExecutionRequestFactory.Compile(executionCompiled,new("000000000000",64,int.MaxValue,2));
            Check(request.Success,"Execution compile: "+name+" "+request.Issue);
            var plan=request.Plan!;
            var probability=RolltheSpire2.Search.Selectivity.JointSelectivityEstimator.EstimateQuery(RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(executionCompiled));
            var backgroundPreview = Task.Run(() => WorkbenchProbabilityPreview.Compute(executionCompiled, 30, 1, true)).GetAwaiter().GetResult();
            Check(backgroundPreview.Joint.Probability == probability.Probability, "Background/main numerical preview agrees: " + name);
            string probabilityPanel=frame.DescribeProbability(executionCompiled, backgroundPreview);
            Check(!probabilityPanel.Contains("coverage.",StringComparison.Ordinal) && !probabilityPanel.Contains("workflow.",StringComparison.Ordinal),"Expectation panel localized: "+name);
            Check(!probabilityPanel.Contains("ns /") && !probabilityPanel.Contains("默认独立") && !probabilityPanel.Contains("Assumption:"),"No model prose in workflow panel");
            if(name.StartsWith("C.h",StringComparison.Ordinal))
            {
                Check(probability.JointlyPriced,"C4-6 chronological probability is connected");
                var combatFamily = (CombatRewardFamily)FamilyExecutionCoordinator.CreateRegisteredFamilies(plan).Single(f=>f.FamilyId=="C.CombatReward");
                var pool = combatFamily.Replay.Catalog.CombatRewardCardPool;
                // Native fresh profiles may have partial unlocks. H4–6 reference
                // pricing covers the measured full pool, while Unknown still admits Exact.
                bool measuredPool = pool.Common.Length == 20 && pool.Uncommon.Length == 35 && pool.Rare.Length == 25;
                Check(((IFamilyInvocation)combatFamily).CpuRealizations.Any(c=>c.QuotePhysicalWork(new(false,false,1<<20)) is not null) == measuredPool,
                    $"C4-6 pricing respects measured domain;pool={pool.Common.Length}/{pool.Uncommon.Length}/{pool.Rare.Length}");
            }
            if(name.StartsWith("M.",StringComparison.Ordinal))
            {
                if(name=="M.nohits") Check(!probability.JointlyPriced && probability.Probability is null && !probability.ExactlyImpossible,
                    "Map no observed hits stays Unknown, not impossible");
                else Check(probability.JointlyPriced || probability.UnknownComponents.Any(c=>c.StartsWith("M.StandardMap:")),
                    "Map is empirically priced or explicitly unknown, never omitted");
            }
            if(name=="A.all") Check(probability.JointlyPriced,"Ancient All probability connected");
            var quick=RolltheSpire2.Presentation.Ui1.SearchProbabilityPresentationBuilder.Build(executionCompiled,probability);
            if(name is "A.any" or "A.all")
            {
                var attributionInput=RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(executionCompiled);
                double identityP=RolltheSpire2.Search.Selectivity.AncientIdentityProbabilityEstimator.Estimate(attributionInput).Probability!.Value;
                var aRow=quick.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.Ancient);
                var wRow=quick.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.WorldEvent);
                Check(wRow.Probability.HasValue && Math.Abs(wRow.Probability.Value-identityP)<1e-12,"Ancient identity is W-owned");
                Check(aRow.Probability.HasValue && Math.Abs(aRow.Probability.Value*wRow.Probability!.Value-probability.Probability!.Value)<1e-12,"A conditional × W identity preserves full query probability");
                var identityQuery=executionQuery with {AncientBranches=executionQuery.AncientBranches.Select(b=>b with {OptionAny=[],SeaGlassTargetAny=[]}).ToArray(),LegacyWorld=LegacyWorldSemanticConstraints.Empty};
                var identityCompiled=SearchCompiler.Compile(identityQuery,executionCompiled.Context);
                var identityView=RolltheSpire2.Presentation.Ui1.SearchProbabilityPresentationBuilder.Build(identityCompiled);
                Check(!identityView.Rows.Any(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.Ancient),"Identity-only has no A probability row");
                Check(identityView.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.WorldEvent).Probability==identityView.TotalProbability,"Identity-only probability belongs wholly to W");
                var mixed=SearchCompiler.Compile(executionQuery with {EventSequenceConstraints=cases["W.queue"].EventSequenceConstraints},executionCompiled.Context);
                var mixedView=RolltheSpire2.Presentation.Ui1.SearchProbabilityPresentationBuilder.Build(mixed);
                double? mixedA=mixedView.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.Ancient).Probability;
                double? mixedW=mixedView.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.WorldEvent).Probability;
                Check(mixedA==aRow.Probability,"Event queue does not change A attribution");
                Check(mixedView.TotalProbability.HasValue && mixedW.HasValue && Math.Abs(mixedA!.Value*mixedW.Value-mixedView.TotalProbability.Value)<1e-12,"W aggregates queue and identity once");
            }
            if(executionCompiled.NormalizedQuery.TransformationAggregate is not null)
                Check(quick.Rows.Any(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.TransformationAggregate),"T probability row visible");
            if(Environment.GetEnvironmentVariable("RT2_GPU_COST_REQUIRED")=="1" && name is "N.identity" or "T.Bones.order" or "C.three")
            {
                Check(SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().RenderingBackend=="d3d12","New shell captures real D3D12 backend before pricing");
                var id=name.StartsWith("N.")?"N.Neow":name.StartsWith("T.")?"T.TransformationAggregate":"C.CombatReward";
                var family=FamilyExecutionCoordinator.CreateRegisteredFamilies(plan).Single(f=>f.FamilyId==id);
                var geometry=new FamilyPhysicalQuoteRequest(false,false,1<<20);
                var gpu=family.QuotePhysicalWork(geometry);
                var cpu=family.CpuRealizations.Select(f=>f.QuotePhysicalWork(geometry)).FirstOrDefault(q=>q is not null);
                Check(gpu is {NanosecondsPerInput:>0},"Restored GPU reference quote: "+name);
                var local=GpuCostCalibration.Capture().Local(family,gpu!,geometry);
                Check(QueryWorkbenchFrame.PreferGpuQuote(cpu?.NanosecondsPerInput,local.NanosecondsPerInput),"GPU wins representative display quote: "+name);
                Console.WriteLine($"WORKBENCH_GPU_QUOTE name={name};cpuNs={cpu?.NanosecondsPerInput};gpuNs={local.NanosecondsPerInput};backend=d3d12");
            }
            coverage.Add(new {name,probability.Probability,probability.EvidenceCode,probability.UnknownComponents,
                rows=quick.Rows, costs=FamilyExecutionCoordinator.CreateRegisteredFamilies(plan).Select(f=>new {
                    f.FamilyId, survival=f.Survival, cpu=f.CpuRealizations.Select(c=>c.QuotePhysicalWork(new(false,false,1<<20))).ToArray(),
                    gpu=f.ConditionPerformance.UsesGpu?f.QuotePhysicalWork(new(false,false,1<<20)):null}).ToArray()});
            var exactSeeds=new List<string>();
            var exactBySeed=new Dictionary<string,ProductionExactSearchResult>(StringComparer.Ordinal);
            for(ulong i=0;i<64;i++)
            {
                string seed=Beta110SeedCodec.FormatOrdinal(i);
                Check(TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance,Beta111Profile.Instance.ComputeRootSeed(seed),seed,out var input,out _),"root bind");
                var exact=ProductionExactSearchEvaluator.Evaluate(plan,input);
                exactBySeed.Add(seed,exact);
                if(exact.IsMatch) exactSeeds.Add(seed);
                parityRoots++;
            }
            var session=FamilyExecutionCoordinator.Start(plan,gpuAvailable:false);
            var actual=new List<string>();
            int unverified=0;
            void Observe(SearchCandidate hit)
            {
                var reference=exactBySeed[hit.Seed];
                if(!hit.IsUnverified)
                {
                    Check(reference.IsMatch && hit.ValidationDisposition==SearchDisposition.Match && hit.Document is not null && hit.Witnesses.Count>0,
                        "Confirmed delivery agrees with model Match and retains proof: "+name+":"+hit.Seed);
                    actual.Add(hit.Seed);
                    return;
                }
                Check(reference.Evaluation.Disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported &&
                    hit.ValidationDisposition==reference.Evaluation.Disposition,
                    "Unconfirmed delivery agrees with Exact Unknown/Unsupported, never NoMatch: "+name+":"+hit.Seed);
                string reason=string.IsNullOrWhiteSpace(reference.Evaluation.FailureCode)?reference.FailureCode:reference.Evaluation.FailureCode;
                if(string.IsNullOrWhiteSpace(reason)) reason="Validation."+reference.Evaluation.Disposition;
                if(reference.Evaluation.RouteRejections.Count>0)
                    reason+="\n"+string.Join("\n",reference.Evaluation.RouteRejections.Select(r=>
                        $"route={r.RouteId};disposition={r.Disposition};reason={r.Reason}"));
                Check(hit.ValidationIssue==reason && hit.Document is null && hit.MatchEvidence.Count==0 &&
                    hit.MatchedRouteIds.Count==0 && hit.Witnesses.Count==0 && hit.PredictionRequest.OriginalSeed==hit.Seed &&
                    hit.PredictionRequest.TrustedRootHashInput is { } bound && bound.CanonicalSeed==hit.Seed,
                    "Unconfirmed delivery preserves reason and seed identity without a proof payload: "+name+":"+hit.Seed+
                    ";actualReason="+hit.ValidationIssue+";expectedReason="+reason+";document="+(hit.Document is not null)+
                    ";evidence/routes/witness="+hit.MatchEvidence.Count+"/"+hit.MatchedRouteIds.Count+"/"+hit.Witnesses.Count);
                unverified++;
            }
            var timeout=System.Diagnostics.Stopwatch.StartNew();
            while(!session.Completion.IsCompleted)
            {
                while(session.TryReadCandidate(out var hit)) Observe(hit!);
                if(timeout.Elapsed.TotalSeconds>60) {session.Cancel();throw new TimeoutException(name);}
                Thread.Sleep(5);
            }
            while(session.TryReadCandidate(out var hit)) Observe(hit!);
            Check(session.GetProgress().State==SearchRunState.Completed,"Production completion: "+name+" "+session.GetProgress().FailureCode);
            Check(actual.OrderBy(s=>s,StringComparer.Ordinal).SequenceEqual(exactSeeds.OrderBy(s=>s,StringComparer.Ordinal)),"Search / Exact same accepted roots: "+name+" actual="+string.Join(",",actual)+" expected="+string.Join(",",exactSeeds));
            Check(session.GetProgress().MatchCount==actual.Count && session.GetProgress().UnverifiedCandidateCount==unverified &&
                session.GetProgress().DeliveredCandidateCount==actual.Count+unverified,
                "Confirmed/unconfirmed/delivered counts agree: "+name);
            if(name.StartsWith("M.",StringComparison.Ordinal))
            {
                var mapInvocation=new StandardMapFamily(plan);var batch=new SearchBatch(0,64);
                var sparse=FamilyCandidateSet.FromSortedAbi1(batch,Enumerable.Range(0,64).Where(i=>i%3==0).Select(i=>(ulong)i).ToArray());
                var window=new FamilyObservationWindow(batch,plan);
                var filtered=mapInvocation.InvokeAsync(null!,window,sparse,default).GetAwaiter().GetResult();
                Check(filtered.EnumerateLogicalOrdinals().Select(i=>Beta110SeedCodec.FormatOrdinal(i)).SequenceEqual(
                    sparse.EnumerateLogicalOrdinals().Select(i=>Beta110SeedCodec.FormatOrdinal(i)).Where(exactSeeds.Contains)),"Map sparse ABI1 Exact parity");
                using var mapCancellation=new CancellationTokenSource();mapCancellation.Cancel();
                bool stopped=false;try { mapInvocation.InvokeAsync(null!,window,sparse,mapCancellation.Token).GetAwaiter().GetResult(); }
                catch(OperationCanceledException){stopped=true;}
                Check(stopped,"Map cancellation propagates");mapInvocation.DisposeAsync(null!).GetAwaiter().GetResult();
            }
            session.DisposeAsync().AsTask().GetAwaiter().GetResult(); returned+=actual.Count;
            Console.WriteLine($"WORKBENCH_CASE {name};roots=64;matches={actual.Count};unconfirmed={unverified};roundtrip=true;ExactParity=true;authority={(archivedWorld?"GameRuntimeCaptureCorpus":"NativeRuntimeCapture")}");
        }
        Check(returned>0,"Non-vacuous returned Exact results");
        frame.RestoreDraft(empty, render: false);
        var ce=typeof(QueryWorkbenchFrame).GetField("_combatEditor",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(frame)!;
        var cd=ce.GetType().GetField("_draft",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ce)!;
        cd.GetType().GetField("BattleRange")!.SetValue(cd,7);
        ((ModelKey?[])cd.GetType().GetProperty("Slots")!.GetValue(cd)!)[0]=card;
        bool rejected=false; try {frame.CaptureDraft();} catch(ArgumentOutOfRangeException) {rejected=true;} catch(InvalidOperationException e) {rejected=e.Message=="integration.prototype.c";}
        Check(rejected,"C7 is not truncated");
        cd.GetType().GetField("BattleRange")!.SetValue(cd,3);
        frame.RestoreDraft(empty,render:false);
        typeof(QueryWorkbenchFrame).GetField("_multiplayer",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(frame,true);
        Check(frame.CaptureDraft().Players.All(p => p.UnlockSource == "AssumedFullyUnlocked"), "Multiplayer defaults to explicit full unlocks outside lobby");
        var partyBase=frame.CaptureDraft();
        var otherAncients=ac.Sections.First().Rows.Where(r=>r.IdentityAvailable&&r.AncientKey!=ancient.AncientKey)
            .Select(r=>r.AncientKey).Distinct().Take(2).ToArray();
        Check(otherAncients.Length>0,"Multiplayer Ancient fixture has an alternative identity");
        ModelKey secondAlternative=otherAncients.Length>1?otherAncients[1]:otherAncients[0];
        AncientEditorStateSnapshot PartyAncientState(int mode, AncientEditorRowSnapshot[] rows) => new(mode,mode,
        [
            new([],false,[],1),
            new(mode==1?rows:[],false,[],2),
            new(mode==2?rows:[],false,[],2)
        ]);
        var partyAncientPlayers=new[]
        {
            partyBase.Players[0] with {AncientEditor=PartyAncientState(1,[new(2,ancient.AncientKey,[],null),new(2,otherAncients[0],[],null)])},
            partyBase.Players[1] with {AncientEditor=PartyAncientState(1,[new(2,ancient.AncientKey,[],null),new(2,secondAlternative,[],null)])},
            partyBase.Players[0] with {Slot=2,AncientEditor=PartyAncientState(2,[new(2,ancient.AncientKey,[option,secondOption],null)])}
        };
        var partyAncientQueries=new[]
        {
            new PlayerOfferQuery(0,ModelKeySetFilter.Empty) {Conditions=SearchQuery.Empty with {AncientBranches=[new(2,ancient.AncientKey,[],[]),new(2,otherAncients[0],[],[])]}},
            new PlayerOfferQuery(1,ModelKeySetFilter.Empty) {Conditions=SearchQuery.Empty with {AncientBranches=[new(2,ancient.AncientKey,[],[]),new(2,secondAlternative,[],[])]}},
            new PlayerOfferQuery(2,ModelKeySetFilter.Empty) {Conditions=SearchQuery.Empty with {AncientBranches=[new(2,ancient.AncientKey,[],[])],
                LegacyWorld=LegacyWorldSemanticConstraints.Empty with {AncientOptionFilters=[new(2,new([],[option,secondOption],[]))]}}}
        };
        var partyAncientDraft=partyBase with {Players=partyAncientPlayers,
            PartyAncientEditor=new PartyAncientEditorStateSnapshot(true,[],[new(2,[ancient.AncientKey])]),
            Query=SearchQuery.Empty with
        {Players=partyAncientQueries,LegacyWorld=LegacyWorldSemanticConstraints.Empty with
        {AncientIdentityFilters=[new(2,new([ancient.AncientKey],[],[]))]}}};
        var boundAncientParty=PartyInitialQuery.BindSharedConditions(partyAncientDraft.Query);
        var sharedAncientFilters=boundAncientParty.LegacyWorld.AncientIdentityFilters.Where(f=>f.Act==2).ToArray();
        Check(sharedAncientFilters.All(f=>PartyInitialQuery.Matches(f.Keys,[ancient.AncientKey])) &&
            !sharedAncientFilters.All(f=>PartyInitialQuery.Matches(f.Keys,[otherAncients[0]])) &&
            boundAncientParty.Players[0].Conditions.AncientBranches.Count==0 &&
            boundAncientParty.Players[1].Conditions.AncientBranches.Count==0 &&
            boundAncientParty.Players[2].Conditions.LegacyWorld.AncientOptionFilters.Single().Keys.All.Count==2,
            "Shared Ancient conjunction resolves the common identity while retaining P3's local option group");
        store.SaveWorkbench(partyAncientDraft);
        var persistedAncientParty=new SearchWorkspace(directory,false).LoadWorkbench()!;
        Check(JsonSerializer.Serialize(persistedAncientParty.Query)==JsonSerializer.Serialize(partyAncientDraft.Query) &&
            persistedAncientParty.PartyAncientEditor is {Advanced:true} &&
            persistedAncientParty.PartyAncientEditor.AdvancedIdentities.Single().Ancients.Single()==ancient.AncientKey &&
            JsonSerializer.Serialize(persistedAncientParty.Players.Select(p=>p.AncientEditor))==
                JsonSerializer.Serialize(partyAncientDraft.Players.Select(p=>p.AncientEditor)),
            "Shared Ancient and individual preferences survive persistence");
        var sharedAncientEditor=(AncientSearchEditor)typeof(QueryWorkbenchFrame)
            .GetField("_ancientEditor",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(frame)!;
        var beforeSharedChange=JsonSerializer.Serialize(sharedAncientEditor.ExportEditorState());
        sharedAncientEditor.ImportSharedPartyQuery(partyAncientDraft.Query,partyAncientDraft.PartyAncientEditor);
        Check(sharedAncientEditor.ExportSharedPartyQuery(SearchQuery.Empty).LegacyWorld.AncientIdentityFilters
            .Single().Keys.Any.Single()==ancient.AncientKey,"Shared Ancient identity imports into its own editor state");
        sharedAncientEditor.ImportSharedPartyQuery(SearchQuery.Empty with {LegacyWorld=LegacyWorldSemanticConstraints.Empty with
            {AncientIdentityFilters=[new(2,new([otherAncients[0]],[],[]))]}});
        Check(sharedAncientEditor.ExportSharedPartyQuery(SearchQuery.Empty).LegacyWorld.AncientIdentityFilters
            .Single().Keys.Any.Single()==otherAncients[0] &&
            JsonSerializer.Serialize(sharedAncientEditor.ExportEditorState())==beforeSharedChange,
            "Editing common Ancient leaves the current player's mode drafts unchanged");
        var uiText=RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en");
        var partyUnlocks=MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable();
        AncientEditorStateSnapshot PersonalAncient(ModelKey personalOption) => new(0,2,
        [
            new([new(2,ancient.AncientKey,[personalOption],null)],false,[],1),
            new([],false,[],1),new([],false,[],1)
        ]);
        // Production restores shared identity before importing personal drafts.
        // The preceding check deliberately selected another shared Ancient.
        var sharedState=new PartyAncientEditorStateSnapshot(false,
            [new(2,[ancient.AncientKey])],[new(2,[ancient.AncientKey])]);
        sharedAncientEditor.ImportSharedPartyQuery(SearchQuery.Empty with {LegacyWorld=LegacyWorldSemanticConstraints.Empty with
            {AncientIdentityFilters=[new(2,new([ancient.AncientKey],[],[]))]}},sharedState);
        sharedAncientEditor.Refresh("en",uiText,character,10,2,0,partyUnlocks,false,authority);
        sharedAncientEditor.ImportQuery(cases["A.any"],AncientOptionConditionProfile.BroadDefault,PersonalAncient(option));
        sharedAncientEditor.Refresh("en",uiText,character,10,2,1,partyUnlocks,false,authority);
        sharedAncientEditor.ImportQuery(cases["A.any"] with {AncientBranches=[new(2,ancient.AncientKey,[secondOption],[])]},
            AncientOptionConditionProfile.BroadDefault,PersonalAncient(secondOption));
        var togglePartyAncient=typeof(AncientSearchEditor).GetMethod("ToggleAncient",BindingFlags.Instance|BindingFlags.NonPublic)!;
        togglePartyAncient.Invoke(sharedAncientEditor,[2,otherAncients[0],false]);
        Check(sharedAncientEditor.ExportSharedPartyQuery(SearchQuery.Empty).LegacyWorld.AncientIdentityFilters
            .Single().Keys.Any.Single()==otherAncients[0] &&
            sharedAncientEditor.ExportPartyQuery(0,SearchQuery.Empty).AncientBranches.Single().AncientKey==otherAncients[0] &&
            sharedAncientEditor.ExportPartyQuery(1,SearchQuery.Empty).AncientBranches.Single().AncientKey==otherAncients[0],
            "Changing Ancient identity on P2 synchronizes P1 and P2");
        togglePartyAncient.Invoke(sharedAncientEditor,[2,ancient.AncientKey,false]);
        Check(sharedAncientEditor.ExportPartyQuery(0,SearchQuery.Empty).AncientBranches.Single().OptionAny.Single()==option &&
            sharedAncientEditor.ExportPartyQuery(1,SearchQuery.Empty).AncientBranches.Single().OptionAny.Single()==secondOption,
            "Returning to the shared Ancient restores each player's own option draft");
        var unlockField = typeof(QueryWorkbenchFrame).GetField("_lobbyUnlocks",BindingFlags.Instance|BindingFlags.NonPublic)!;
        ((Array)unlockField.GetValue(frame)!).SetValue(null, 1);
        rejected=false;try{frame.CaptureDraft();}catch(InvalidOperationException e){rejected=e.Message.StartsWith("Party.UnlocksUnread:",StringComparison.Ordinal);}
        Check(rejected,"Multiplayer missing member authority never submitted as P1");
        frame.RestoreDraft(empty,render:false);
        object Private(object instance,string field) => instance.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
        void Refused(string issue)
        { bool refused=false;try{frame.CaptureDraft();}catch(InvalidOperationException e){refused=e.Message==issue;}Check(refused,issue); }
        var map=Private(frame,"_actInformationEditor");
        var mapDraft=Private(map,"_draft");
        var routes=(System.Collections.IDictionary)mapDraft.GetType().GetProperty("Routes")!.GetValue(mapDraft)!;
        var routeType=map.GetType().GetNestedType("RouteCondition",BindingFlags.NonPublic)!;
        routes[1]=Activator.CreateInstance(routeType,MapMetricKind.ReachableMaxElite,3)!;
        Check(frame.CaptureDraft().Query.StandardMaps.Single() is { Scope:1, Value:3, RouteObjective:true },"Map exports canonical predicate");
        frame.RestoreDraft(empty,render:false);
        var eventDraft=Private(Private(frame,"_eventEditor"),"_draft");
        ((HashSet<string>)eventDraft.GetType().GetProperty("PrototypeTargets")!.GetValue(eventDraft)!).Add("future-event");
        Refused("integration.prototype.e");
        frame.RestoreDraft(empty with { Query=cases["T.Leafy"] },render:false);
        var tDraft=Private(Private(frame,"_transformationEditor"),"_draft");
        tDraft.GetType().GetField("Objective")!.SetValue(tDraft,2);
        Refused("integration.transform.capacity");
        store.SaveWorkbench(empty);
        string savedDraftPath = Path.Combine(directory, "RolltheSpire2", "state", "query_workbench.json");
        string savedDraftBeforeIncomplete = File.ReadAllText(savedDraftPath);
        int auxiliaryBeforeIncomplete = store.Workspace.AuxiliaryIssues.Count;
        var explain = typeof(QueryWorkbenchFrame).GetMethod("Explain", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string conditionMessage = (string)explain.Invoke(frame, [new InvalidOperationException("integration.transform.capacity")])!;
        typeof(QueryWorkbenchFrame).GetMethod("SaveDraft", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(frame, null);
        Check(conditionMessage == RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh").Get("integration.transform.capacity") &&
            store.Workspace.AuxiliaryIssues.Count == auxiliaryBeforeIncomplete && File.ReadAllText(savedDraftPath) == savedDraftBeforeIncomplete,
            "An unfinished T condition keeps its localized capacity rejection and saved intent without inventing admission/save system failures");
        ((List<ModelKey>)tDraft.GetType().GetProperty("Cards")!.GetValue(tDraft)!).Add(card);
        var mixedDraft=frame.CaptureDraft();
        Check(mixedDraft.Query.TransformationAggregate is {Predicate:TransformationAggregatePredicate.ContainsMultisetAndRemainingRare,TargetMultiset.Count:1},
            "Mixed target exports to production predicate");
        var targetCards = (List<ModelKey>)tDraft.GetType().GetProperty("Cards")!.GetValue(tDraft)!;
        int opportunities = mixedDraft.Query.TransformationAggregate!.OpportunityCount;
        while (targetCards.Count <= opportunities) targetCards.Add(card);
        Refused("integration.transform.capacity");
        targetCards.RemoveRange(1, targetCards.Count - 1);
        frame.RestoreDraft(mixedDraft,render:false);
        var mixedRestored=Private(Private(frame,"_transformationEditor"),"_draft");
        Check((int)mixedRestored.GetType().GetField("Objective")!.GetValue(mixedRestored)! == 2 &&
            frame.CaptureDraft().Query.TransformationAggregate!.TargetMultiset.SequenceEqual(mixedDraft.Query.TransformationAggregate!.TargetMultiset),
            "Mixed editor mode and target round-trip");
        frame.RestoreDraft(empty with {Query=cases["A.all"]},render:false);
        var modeSwitchAncient = (AncientSearchEditor)Private(frame, "_ancientEditor");
        typeof(AncientSearchEditor).GetMethod("SelectMode", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(modeSwitchAncient, new object[] { 0 });
        var defaultAncientDraft = frame.CaptureDraft();
        Check(defaultAncientDraft.AncientEditor!.ActiveMode == 0 &&
            defaultAncientDraft.Query.LegacyWorld.AncientOptionFilters.Count==0 &&
            defaultAncientDraft.AncientEditor.Modes[2].Rows.Single().Options.Count==2,
            "Switching to default keeps the offered group in its separate draft");
        frame.RestoreDraft(empty,render:false);
        File.WriteAllText(".build-evidence/capability-coverage.json",JsonSerializer.Serialize(coverage,ProductionSearchReplay.JsonOptions));
        var cancelledRequest=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(SearchQuery.Empty,archived),new("000000000000",1_000_000,int.MaxValue,1)).Plan!;
        var cancelled=FamilyExecutionCoordinator.Start(cancelledRequest,gpuAvailable:false);
        cancelled.Cancel();
        Check(cancelled.Completion.Wait(TimeSpan.FromSeconds(30)),"Cancellation completes");
        Check(cancelled.GetProgress().State==SearchRunState.Cancelled,"Cancellation uses existing session authority");
        cancelled.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Check(QueryWorkbenchFrame.CompactNumber(1250)=="1.25K" && QueryWorkbenchFrame.CompactNumber(2_000_000)=="2M" &&
            QueryWorkbenchFrame.CompactNumber(3_000_000_000)=="3B" && QueryWorkbenchFrame.CompactNumber(4_000_000_000_000)=="4T","K/M/B/T formatting");
        Check(QueryWorkbenchFrame.Rarity(null)=="—" && QueryWorkbenchFrame.Rarity(.0008)=="1 / 1.25K","Rarity and unknown formatting");
        store.SetWorkbenchSearchMode("CPU");
        Check(new SearchWorkspace(directory,false).Preferences.SearchMode=="CPU","Search plan preference roundtrip");
        var updatePanel=typeof(QueryWorkbenchFrame).GetMethod("UpdateProbabilityPanel",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var jointDraft=empty with {Query=cases["T.Bones.order"]};
        // Production estimates run only for a visible page. The broad authoring
        // fixture above is detached; scope a real native host to this lifecycle test.
        var previewHost = new Godot.Control { Size = new(1536, 772) };
        ((Godot.SceneTree)Godot.Engine.GetMainLoop()).CurrentScene.AddChild(previewHost);
        previewHost.AddChild(frame);
        try
        {
            Check(frame.IsVisibleInTree(), "Probability lifecycle fixture is visible in the native SceneTree");
            frame.RestoreDraft(jointDraft);
            frame.Refresh("zh",RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
            bool UpdatePanel(WorkbenchSearchDraft draft)=> (bool)updatePanel.Invoke(frame,new object[]{draft,JsonSerializer.Serialize(draft)})!;
            void AwaitPreview()
            {
                var poll = typeof(QueryWorkbenchFrame).GetMethod("PollProbabilityPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Check(SpinWait.SpinUntil(() =>
                {
                    poll.Invoke(frame, null);
                    return !(bool)Private(frame, "_probabilityPending");
                }, 30000), "Current Dashboard preview completes without synchronous UI calculation");
            }
            Check(UpdatePanel(jointDraft),"Bones joint alone compiles for Dashboard");
            Check((bool)Private(frame,"_probabilityPending") &&
                ((System.Collections.ICollection)Private(frame,"_familyEntries")).Count == 0,
                "Pending query clears stale family probabilities");
            AwaitPreview();
            Check(((System.Collections.ICollection)Private(frame,"_familyEntries")).Count>0,"Joint Family summaries visible");
            string completedKey = (string)Private(frame, "_analysisKey");
            int completedFamilies = ((System.Collections.ICollection)Private(frame, "_familyEntries")).Count;
            frame.Hide(); frame.Show();
            Check(UpdatePanel(jointDraft) && !(bool)Private(frame, "_probabilityPending") &&
                (string)Private(frame, "_analysisKey") == completedKey &&
                ((System.Collections.ICollection)Private(frame, "_familyEntries")).Count == completedFamilies,
                "Unchanged hide/show retains completed family probabilities without another preview");
            var conflictDraft=jointDraft with {Query=jointDraft.Query with {CombatCardRewards=cases["C.three"].CombatCardRewards}};
            Check(UpdatePanel(conflictDraft),"Fixed Bones joint plus C is admitted");
            Check((bool)Private(frame, "_probabilityPending") &&
                ((System.Collections.ICollection)Private(frame, "_familyEntries")).Count == 0,
                "Changed admitted query clears completed family probabilities before estimating");
            frame.Hide(); frame.Show();
            Check(!(bool)Private(frame, "_probabilityPending") && (string)Private(frame, "_analysisKey") == "",
                "Hiding pending preview cancels its publication identity");
            Check(UpdatePanel(conflictDraft), "Returning to the pending query submits a current preview");
            AwaitPreview();
            Check(((string)Private(frame,"_draftCompileIssue")).Length==0 && ((System.Collections.ICollection)Private(frame,"_familyEntries")).Count>=3,"Admitted fixed joint retains N/T/C summaries");
            Check(UpdatePanel(conflictDraft),"Cached supported draft remains admitted");
            Check(UpdatePanel(jointDraft) && ((string)Private(frame,"_draftCompileIssue")).Length==0,"Removing conflict restores summary and clears stale issue");
            AwaitPreview();
        }
        finally
        {
            previewHost.RemoveChild(frame);
            previewHost.Free();
        }
        store.SaveWorkbench(empty);
        foreach(string language in new[]{"zh","en"})
        {
            frame.Refresh(language,RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(language));
            var targetControl=(Godot.SpinBox)Private(frame,"_targetCount");var planControl=(Godot.OptionButton)Private(frame,"_planSelector");
            var editor=(Godot.Control)Private(frame,"_neowEditor");var startControl=(Godot.Button)Private(frame,"_start");
            // The right rail now owns container-local coordinates. Verify its placement
            // and pinned controls without requiring a SceneTree layout frame in this fixture.
            var rail=frame.GetNode<Godot.VBoxContainer>("SearchInformationRail");
            var information=rail.GetNode<Godot.ScrollContainer>("SearchInformationScroll");
            Check(rail.Position.X>=editor.Position.X+editor.Size.X &&
                rail.IsAncestorOf(targetControl) && rail.IsAncestorOf(planControl),
                "Target/plan are in fixed right Dashboard");
            Check(startControl.GetParent()==rail && startControl.GetIndex()>0 &&
                startControl.GetIndex()<information.GetIndex() && !information.IsAncestorOf(targetControl) &&
                !information.IsAncestorOf(planControl), "Search controls remain pinned above scrolling information");
            Check(frame.GetNodeOrNull<Godot.Button>("SavePreset") is not null,"Save preset entry remains available");
            typeof(QueryWorkbenchFrame).GetField("_showResults",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(frame,true);
            typeof(QueryWorkbenchFrame).GetField("_lastProgress",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(frame,cancelled.GetProgress());
            typeof(QueryWorkbenchFrame).GetMethod("RenderResults",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(frame,null);
            typeof(QueryWorkbenchFrame).GetMethod("LockForRun",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(frame,new object[]{true});
            Check(((Godot.Control)Private(frame,"_runtimeSurface")).Visible && !editor.Visible,"Run surface replaces editor");
            Check(planControl.Disabled && !targetControl.Editable && !startControl.Disabled,"Run locks authoring but preserves Stop");
            typeof(QueryWorkbenchFrame).GetMethod("LockForRun",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(frame,new object[]{false});
            Check(!planControl.Disabled && targetControl.Editable,"Stop/completion restores controls");
            typeof(QueryWorkbenchFrame).GetField("_showResults",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(frame,false);
            frame.Refresh(language,RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(language));
            Check(!((Godot.Control)Private(frame,"_runtimeSurface")).Visible && editor.Visible,"Return to editing");
        }
        store.SaveWorkbenchPreferences(new Dictionary<string,bool>{{"n.guide",false},{"a.advanced",true}},"events");
        var prefStore=new SearchWorkspace(directory,false);
        Check(!prefStore.Preferences.WorkbenchFlags["n.guide"]&&prefStore.Preferences.WorkbenchPage=="events","Preference roundtrip independent of Query");
        string queryFile=Path.Combine(directory,"RolltheSpire2","state","query_workbench.json");
        var unknownJson=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(queryFile))!;
        unknownJson["FutureUnknownCondition"]=true;
        File.WriteAllText(queryFile,unknownJson.ToJsonString());
        bool unknownRejected=false;
        try { prefStore.LoadWorkbench(); } catch(InvalidDataException) { unknownRejected=true; }
        Check(unknownRejected,"Unknown persisted fields must not be silently discarded");
        checks+=VerifyBonesCombat(archived);
        frame.Free();
        Console.WriteLine($"WORKBENCH_CONTRACT_PASS checks={checks};parityRoots={parityRoots};returned={returned};nativeUICaptureAndCompile=true;executionAuthority=NativeRuntimeCapture+ExplicitWorldCorpus;ProductionCPU=true");
        return 0;
    }
    private static int VerifyBonesCombat(SearchContext context)
    {
        int checks=0;
        void Check(bool ok,string label){checks++;if(!ok)throw new InvalidOperationException(label);}
        const string seed="0000000001FA";
        Beta110SeedCodec.TryParseOrdinal(seed,out ulong startOrdinal,out _,out _);
        var root=TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,seed);
        var bones=BaseGameModelKeys.Relics.NeowsBones;var leafy=BaseGameModelKeys.Relics.LeafyPoultice;var leaf=BaseGameModelKeys.Relics.NewLeaf;
        var projection=new RolltheSpire2.Core.Effects.NeowEffectProjectionEngine(Beta111Profile.Instance,"BonesC.Check",root.RootHash,context.Authority).Project(bones);
        var routes=projection.BonesOutcome!.OriginalRoutes;
        ModelKey[] Cards(IEnumerable<RolltheSpire2.Core.Effects.PredictedEffectGroup> groups)=>groups.SelectMany(g=>g.OrderedItems)
            .Where(i=>i.Kind==RolltheSpire2.Core.Effects.PredictedEffectKind.TransformCard).Select(i=>i.TargetKey!.Value).ToArray();
        var cards=Cards(routes[0].RelicScopedResults.SelectMany(r=>r.EffectGroups));
        ExactSearchExecutionRequest Request(SearchQuery q)=>ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q,context),new(seed,64,30,1)).Plan!;
        var baseQuery=SearchQuery.Empty with {OpeningRoute=new(bones),OpeningRouteRelicRequirement=new(bones,[leafy,leaf]),
            TransformationAggregate=new(TransformationOpening.BonesLeafyNewLeaf,TransformationPickupOrder.Any,false,false,false,TransformationAggregatePredicate.ContainsMultiset,0,[cards[0]])};
        var probe=baseQuery with {CombatCardRewards=new(6,CombatRewardSequenceOrderMode.Ordered,[context.Authority.EffectAuthority!.CharacterRewardPool![0].CardKey,null,null,null,null,null])};
        var probeResult=ProductionExactSearchEvaluator.Evaluate(Request(probe),root);
        var reward=probeResult.Document?.Sections.FirstOrDefault(s=>s.NormalCombatRewardSequencePrediction is not null)?.NormalCombatRewardSequencePrediction;
        Check(reward is not null,"Fixed Bones+C reaches reward Predictor: "+probeResult.FailureCode);
        var battles=reward!.Routes[0].Battles;
        var continuations=routes.Select(r=>r.OpeningRewardContinuation!).ToArray();
        Check(continuations.Length==2 && continuations[0].RewardsRngState!.Fingerprint==continuations[1].RewardsRngState!.Fingerprint,"Both pickup orders preserve identical Rewards RNG");
        foreach(var order in new[] { TransformationPickupOrder.Any, TransformationPickupOrder.LeafyThenNewLeaf, TransformationPickupOrder.NewLeafThenLeafy })
        foreach(int count in new[]{1,2,3})
        foreach(var combatOrder in Enum.GetValues<CombatRewardSequenceOrderMode>())
        {
            var pair=order==TransformationPickupOrder.NewLeafThenLeafy?new[]{leaf,leafy}:new[]{leafy,leaf};
            ModelKey?[] combatTargets=battles.Select(b=>(ModelKey?)b.Cards[0].CardKey).ToArray();
            if(combatOrder==CombatRewardSequenceOrderMode.Unordered)Array.Reverse(combatTargets);
            var q=baseQuery with {OpeningRouteRelicRequirement=new(bones,pair,order==TransformationPickupOrder.Any?BonesRouteOrderMode.AnyOrder:BonesRouteOrderMode.ExactOrder),
                TransformationAggregate=baseQuery.TransformationAggregate! with {PickupOrder=order,TargetMultiset=cards.Take(count).Reverse().ToArray()},
                CombatCardRewards=new(6,combatOrder,combatTargets)};
            var request=Request(q);var exact=ProductionExactSearchEvaluator.Evaluate(request,root);
            Check(exact.IsMatch,"Partial/full joint with C6 "+order+"/"+count+"/"+combatOrder+": "+exact.FailureCode);
            var c=new CombatRewardReplay(request);var t=new TransformationAggregateNumericalPlan(request);
            Check(c.Plan.OpeningConsumption.ReplayBonesOffer && c.Matches(root.RootHash) && t.Matches(root.RootHash),"C and aggregate numerical retain positive witness");
            var checkpoint=c.Opening(root.RootHash,0).Rewards.CaptureCheckpoint();var state=continuations[0].RewardsRngState!;
            Check(checkpoint.S0==state.S0 && checkpoint.S1==state.S1 && checkpoint.S2==state.S2 && checkpoint.S3==state.S3,"C Fast arrives at vanilla Rewards checkpoint");
            Check(exact.Evaluation.Witnesses.All(w=>exact.Evaluation.Evidence.Any(e=>e.Code=="TransformationAggregateMatched" && e.AcquisitionOrder!.SequenceEqual(w.AcquisitionOrder))),"Reward and aggregate evidence share pickup route");
            if(order!=TransformationPickupOrder.Any)Check(exact.Evaluation.Witnesses.All(w=>w.AcquisitionOrder.SequenceEqual(pair)),"Exact retains fixed pickup order");
            var restored=JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(q,ProductionSearchReplay.JsonOptions),ProductionSearchReplay.JsonOptions)!;
            Check(Request(restored).SnapshotFingerprint==request.SnapshotFingerprint,"Joint+C persistence roundtrip");
        }
        // Match an actual C1 output for bounded real CPU/GPU integration and session checks.
        var query=baseQuery with {CombatCardRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[battles[0].Cards[0].CardKey])};
        var plan=Request(query);var replay=new CombatRewardReplay(plan);var aggregate=new TransformationAggregateNumericalPlan(plan);
        var batch=FamilyCandidateSet.Dense(new SearchBatch(startOrdinal,64));
        var expected=new List<ulong>();
        for(ulong i=0;i<64;i++){
            string candidate=Beta110SeedCodec.FormatOrdinal(startOrdinal+i);var input=TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,candidate);
            bool fast=aggregate.Matches(input.RootHash)&&replay.Matches(input.RootHash);var exact=ProductionExactSearchEvaluator.Evaluate(plan,input);
            Check(fast==exact.IsMatch,"Joint+C bounded Fast/Exact parity at "+candidate);if(fast)expected.Add(i);
        }
        Check(expected.Count>0,"Joint+C nonvacuous interval");
        var session=FamilyExecutionCoordinator.Start(plan,gpuAvailable:false);session.Completion.GetAwaiter().GetResult();var actual=new List<string>();
        while(session.TryReadCandidate(out var result))actual.Add(result!.Seed);session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Check(actual.SequenceEqual(expected.Select(i=>Beta110SeedCodec.FormatOrdinal(startOrdinal+i))),"Production Search Joint+C results");
        var sourceBound=query with {TransformationAggregate=null,StructuredOpeningEffects=routes[0].RelicScopedResults.Select(r=>
            new NeowStructuredEffectSearchCondition(r.SourceRelicKey,r.SourceRelicKey==leafy?NeowStructuredConditionKind.ExactUnorderedPair:NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.TransformResults,NeowStructuredOutputKind.Card,Cards(r.EffectGroups),AllowDuplicateOutputs:r.SourceRelicKey==leafy)).ToArray()};
        Check(ProductionExactSearchEvaluator.Evaluate(Request(sourceBound),root).IsMatch,"Separately assigned N+C donor remains valid");
        var repeated=query with {TransformationAggregate=query.TransformationAggregate! with {TargetMultiset=[cards[0],cards[0],cards[0]]}};
        Check(ProductionExactSearchEvaluator.Evaluate(Request(repeated),root).IsMatch==cards.All(k=>k==cards[0]),"Joint+C preserves duplicate multiplicity");
        var lateOnly=battles[5].Cards.Select(c=>c.CardKey).First(k=>!battles.Take(3).SelectMany(b=>b.Cards).Any(c=>c.CardKey==k));
        var ordinarySix=sourceBound with {CombatCardRewards=new(6,CombatRewardSequenceOrderMode.Unordered,[lateOnly,null,null,null,null,null])};
        Check(ProductionExactSearchEvaluator.Evaluate(Request(ordinarySix),root).IsMatch,"Ordinary typed C6 does not inherit legacy three-battle rejection");
        var legacyThree=ordinarySix with {LegacyCombatRewardConstraints=[new(0,new([lateOnly],[],[]),NormalCombatPotionRequirement.Any,ModelKeySetFilter.Empty,null,null)]};
        Check(!ProductionExactSearchEvaluator.Evaluate(Request(legacyThree),root).IsMatch,"Actual legacy any-battle condition retains three-battle scope");
        var cancelRequest=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query,context),new(seed,1_000_000,int.MaxValue,1)).Plan!;
        var cancelled=FamilyExecutionCoordinator.Start(cancelRequest,gpuAvailable:false);cancelled.Cancel();
        Check(cancelled.Completion.Wait(TimeSpan.FromSeconds(30)) && cancelled.GetProgress().State==SearchRunState.Cancelled,"Joint+C Stop completes via existing session");
        cancelled.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var estimateInput=RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(plan);
        var estimate=RolltheSpire2.Search.Selectivity.JointSelectivityEstimator.EstimateQuery(estimateInput);
        var cProbability=RolltheSpire2.Search.Selectivity.CombatRewardProbabilityEstimator.Estimate(estimateInput);
        var tProbability=TransformationAggregateProbability.Build(aggregate).QueryHitProbability;
        Check(estimate.Probability.HasValue && cProbability.Probability.HasValue && tProbability.HasValue &&
            Math.Abs(estimate.Probability.Value-cProbability.Probability.Value*tProbability.Value)<1e-12,"Joint probability charges opening once and C distribution once");
        var attribution=RolltheSpire2.Presentation.Ui1.SearchProbabilityPresentationBuilder.Build(plan.CompiledSearch);
        double nContribution=attribution.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.Neow).Probability!.Value;
        double tContribution=attribution.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.TransformationAggregate).Probability!.Value;
        Check(Math.Abs(nContribution-TransformationAggregateProbability.Build(aggregate).IdentityProbability)<1e-12 &&
            Math.Abs(nContribution*tContribution-tProbability!.Value)<1e-12,"N identity and conditional T contribution are visible and charged once");
        var merge=typeof(ProductionExactSearchEvaluator).GetMethod("MergeCanonicalGlobalEvidence",BindingFlags.Static|BindingFlags.NonPublic)!;
        var positive=ProductionExactSearchEvaluator.Evaluate(plan,root).Evaluation;
        var proof=positive.Evidence.First(e=>e.Code=="TransformationAggregateMatched");
        var oppositeWitness=positive.Witnesses[0] with {AcquisitionOrder=proof.AcquisitionOrder!.Reverse().ToArray()};
        var incompatible=SearchQueryEvaluation.Match([], [oppositeWitness.OpeningRouteId], [oppositeWitness]);
        var intersection=(SearchQueryEvaluation)merge.Invoke(null,new object[]{incompatible,SearchQueryEvaluation.Match([proof])})!;
        Check(intersection.Disposition==SearchDisposition.NoMatch,"Exact cannot combine opposite pickup route witnesses");
        if(Environment.GetEnvironmentVariable("RT2_BONES_C_GPU")=="1")
        {
            using var rd=Godot.RenderingServer.CreateLocalRenderingDevice();
            Check(rd is not null,"Vulkan device available for joint+C GPU parity");
            using var tgpu=new TransformationAggregateGpuExecutor(rd!,new TransformationAggregateGpuPlan(aggregate));
            var survivors=tgpu.Execute(batch,default,out _);
            var cpuT=Enumerable.Range(0,64).Select(i=>(ulong)i).Where(i=>aggregate.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(startOrdinal+i))));
            Check(survivors.EnumerateLogicalOrdinals().SequenceEqual(cpuT),"Aggregate GPU retains CPU candidates");
            using var cgpu=new CombatRewardGpuExecutor(rd!,CombatRewardGpuPlan.TryCreate(plan,replay,out _) !);
            var gpuResult=cgpu.Execute(survivors,default,out _);
            Check(gpuResult.EnumerateLogicalOrdinals().SequenceEqual(expected),"T GPU to C GPU compact handoff equals Exact");
            Console.WriteLine("BONES_C_GPU_PASS roots=64;compactHandoff=true;falseNegative=0");
        }
        Console.WriteLine($"BONES_C_PASS checks={checks};orders=3;partialTargets=1..3;C6OrderedUnordered=true;roots=64;matches={expected.Count}");
        return checks;
    }

    private static int BenchmarkC6(SearchContext context)
    {
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        var pool=context.Authority.EffectAuthority!.CharacterRewardPool!;
        var rewardCatalog=Beta110FastEffectCatalogCompiler.Compile(context.Authority.EffectAuthority,[]);
        var rare=rewardCatalog.KeyOf(rewardCatalog.CombatRewardCardPool.Rare[0]);
        var common=rewardCatalog.KeyOf(rewardCatalog.CombatRewardCardPool.Common[0]);
        var rd=Godot.RenderingServer.CreateLocalRenderingDevice();
        var results=new List<object>();
        foreach(int horizon in new[]{4,5,6})
        {
            ModelKey?[] slots=new ModelKey?[horizon]; slots[0]=common; slots[^1]=rare;
            var compiled=SearchCompiler.Compile(SearchQuery.Empty with {CombatCardRewards=new(horizon,CombatRewardSequenceOrderMode.Ordered,slots)},context);
            var request=ExactSearchExecutionRequestFactory.Compile(compiled,new("000000000000",1<<18,int.MaxValue,1)).Plan!;
            var replay=new CombatRewardReplay(request);
            var batch=FamilyCandidateSet.Dense(new SearchBatch(0,1<<18));
            var cpu=new FamilyCpuExecution(request,"C.CombatReward","C6.Evidence",windowCapacity:65536,numericalRoots:true);
            var observation=new FamilyObservationWindow(batch.Batch,request);
            var times=new List<double>(); FamilyCandidateSet? expected=null;
            for(int round=0;round<4;round++)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();expected=cpu.Execute(observation,batch,default,replay.Matches);watch.Stop();
                if(round>0)times.Add(watch.Elapsed.TotalMilliseconds);
            }
            double? deviceMs=null, canonicalMs=null;
            if(rd is not null)
            {
                var gpuPlan=CombatRewardGpuPlan.TryCreate(request,replay,out _) !;
                using var gpu=new CombatRewardGpuExecutor(rd,gpuPlan);
                var device=new List<double>();var canonical=new List<double>();
                for(int round=0;round<4;round++)
                {
                    var actual=gpu.Execute(batch,default,out var metrics);
                    Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected!.EnumerateLogicalOrdinals()),"C6 real-pool GPU ABI1 parity");
                    if(round>0){device.Add(metrics.DispatchSyncMs);canonical.Add(metrics.CanonicalMs);}
                }
                deviceMs=device.Average();canonicalMs=canonical.Average();
            }
            var exactWatch=System.Diagnostics.Stopwatch.StartNew();int accepted=0;
            foreach(ulong ordinal in expected!.EnumerateLogicalOrdinals())
            {
                string seed=Beta110SeedCodec.FormatOrdinal(ordinal);
                TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance,Beta111Profile.Instance.ComputeRootSeed(seed),seed,out var input,out _);
                var proof=ProductionExactSearchEvaluator.Evaluate(request,input);
                if(proof.IsMatch)accepted++; else Check(proof.FailureCode=="NoEligibleUnpinnedRewardRoute","Unexpected C6 Exact rejection: "+proof.FailureCode);
            }
            exactWatch.Stop();
            Check(accepted>0 && accepted<=expected.Count,$"C6 low-survivor workload is non-vacuous and Exact agrees: {expected.Count}/{accepted}; target={rare}; common={common}");
            Console.WriteLine($"C6_REAL_BENCH H={horizon};target={rare};roots={batch.Count};survivors={expected.Count};accepted={accepted};cpuCanonicalMs={times.Average()};gpuDeviceMs={deviceMs};gpuCanonicalMs={canonicalMs};exactTailMs={exactWatch.Elapsed.TotalMilliseconds}");
            results.Add(new {horizon,target=rare.Serialized,roots=batch.Count,survivors=expected.Count,accepted,cpuCanonicalMs=times.Average(),deviceMs,canonicalMs,exactTailMs=exactWatch.Elapsed.TotalMilliseconds});
        }
        rd?.Free();
        File.WriteAllText(".build-evidence/c6-real-benchmark.json",JsonSerializer.Serialize(results));
        Console.WriteLine("C6_REAL_BENCH_PASS");return 0;
    }

}
