using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;

internal static partial class UiSurfaceChecks
{
    private static void RunContentNamesChecks(Node root, string output)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        int checks = 0;
        void Check(bool ok, string reason)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("ContentNamesUi: " + reason);
        }
        var create = typeof(IGameContentNameResolver).Assembly
            .GetType("RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver", true)!
            .GetMethod("Create", BindingFlags.Static | BindingFlags.Public)!;
        IGameContentNameResolver Names(string language) =>
            (IGameContentNameResolver)create.Invoke(null, [language])!;
        static ModelKey Key(AbstractModel model) => new(model.Id.Category, model.Id.Entry);

        // These are the actual loaded game's registry and localization manager.
        // Inject uses the game's supported test/mod registration entry point. Only
        // deliberate mismatched-identity cases need direct registry access.
        var registry = (Dictionary<ModelId, AbstractModel>)typeof(ModelDb)
            .GetField("_contentById", statics)!.GetValue(null)!;
        var originalRegistry = registry.ToArray();
        var dbFields = typeof(ModelDb).GetFields(statics).Where(field => !field.IsInitOnly && !field.IsLiteral)
            .Select(field => (Field: field, Value: field.GetValue(null))).ToArray();
        var loc = LocManager.Instance;
        var locFields = typeof(LocManager).GetFields(instance).Where(field => !field.IsInitOnly)
            .Select(field => (Field: field, Value: field.GetValue(loc))).ToArray();
        string originalLocale = TranslationServer.GetLocale();
        string? originalSaveLanguage = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language;
        var runState = RunManager.Instance.DebugOnlyGetState();
        var surface = new VBoxContainer();
        root.AddChild(surface);
        var evidence = new List<object>();
        try
        {
            Check(runState is null, "native name fixture starts without an active game RunState");
            Check(typeof(ContentNameHostAncient).Assembly != typeof(AncientEventModel).Assembly,
                "Mod fixture is outside sts2, so game-assembly scanning cannot find it");
            Check(!ModelDb.Contains(typeof(ContentNameHostAncient)), "fixture starts unregistered");
            ModelDb.Inject(typeof(ContentNameHostAncient));
            var ancient = ModelDb.GetById<AncientEventModel>(ModelDb.GetId<ContentNameHostAncient>());
            var fixture = (ContentNameHostAncient)ancient;
            ModelKey fixtureKey = Key(fixture);
            Check(ReferenceEquals(fixture, registry[fixture.Id]) && fixture.IsCanonical,
                "external canonical Ancient is registered in real ModelDb");
            Check(typeof(ContentNameHostAncient).GetProperty("Title")!.DeclaringType == typeof(EventModel),
                "Mod fixture uses the genuine inherited EventModel.Title LocString");

            foreach (string gameLanguage in new[] { "eng", "zhs" })
            {
                loc.SetLanguage(gameLanguage);
                var tables = (Dictionary<string, LocTable>)typeof(LocManager)
                    .GetField("_tables", instance)!.GetValue(loc)!;
                Check(!tables.ContainsKey(ContentNameHostAncient.Table), "fixture table has no production collision");
                string title = gameLanguage == "eng" ? "Host Ancient" : "宿主先古";
                var translations = new Dictionary<string, string> { [fixture.Id.Entry + ".title"] = title };
                tables.Add(ContentNameHostAncient.Table, new LocTable(ContentNameHostAncient.Table, translations));
                try
                {
                    Check(fixture.Title.GetFormattedText() == title, "genuine LocString resolves the fixture's game table");
                    foreach (string uiLanguage in new[] { "zh", "en" })
                    {
                        var names = Names(uiLanguage);
                        var text = JsonUiTextProvider.CreateUi13(uiLanguage);
                        Check(names.LanguageCode == uiLanguage && text.LanguageCode == uiLanguage,
                            "name and UI resolver language identifiers survive static-table retirement");
                        Check(names.Resolve(fixtureKey, GameContentKind.Ancient) == title,
                            "registered external Ancient gets its actual game-localized Title");
                        Check(loc.Language == gameLanguage, "RT2 language selection does not change the game language");
                        string ownCaption = text.Get("settings.language");
                        Check(ownCaption == (uiLanguage == "zh" ? "UI 语言" : "UI language"),
                            "RT2-owned caption still follows its selected UI language");
                        var label = new Label { Text = ownCaption + ": " + names.Resolve(fixtureKey, GameContentKind.Ancient) };
                        surface.AddChild(label);
                        Check(label.Text == ownCaption + ": " + title,
                            "mixed-language label keeps game-owned name and RT2-owned caption");

                        var vanilla = new (AbstractModel Model, GameContentKind Kind, string Title)[]
                        {
                            (ModelDb.AncientEvent<Neow>(), GameContentKind.Ancient, ModelDb.AncientEvent<Neow>().Title.GetFormattedText()),
                            (ModelDb.Event<AbyssalBaths>(), GameContentKind.Event, ModelDb.Event<AbyssalBaths>().Title.GetFormattedText()),
                            (ModelDb.Card<Anger>(), GameContentKind.Card, ModelDb.Card<Anger>().Title),
                            (ModelDb.Relic<GoldenPearl>(), GameContentKind.Relic, ModelDb.Relic<GoldenPearl>().Title.GetFormattedText())
                        };
                        foreach (var sample in vanilla)
                        {
                            Check(!string.IsNullOrWhiteSpace(sample.Title) && sample.Title != Key(sample.Model).Serialized,
                                "vanilla control has a real localized name");
                            Check(names.Resolve(Key(sample.Model), sample.Kind) == sample.Title,
                                "existing vanilla " + sample.Kind + " keeps its actual Title");
                        }
                        evidence.Add(new { gameLanguage, uiLanguage, fixture = fixtureKey.Serialized,
                            fixtureAssembly = fixture.GetType().Assembly.GetName().Name,
                            titleDeclaringType = typeof(EventModel).FullName, title, ownCaption,
                            vanilla = vanilla.Select(sample => new { key = Key(sample.Model).Serialized, kind = sample.Kind.ToString(), sample.Title }) });
                    }

                    void Raw(ModelKey key, GameContentKind kind, string reason)
                    {
                        foreach (string language in new[] { "zh", "en" })
                        {
                            var resolver = Names(language);
                            Check(resolver.Resolve(key, kind) == key.Serialized, reason + " gives stable full raw ID");
                            Check(resolver.Resolve(key, kind) == key.Serialized, reason + " stays stable on repeat");
                            var neow = ModelDb.AncientEvent<Neow>();
                            Check(resolver.Resolve(Key(neow), GameContentKind.Ancient) == neow.Title.GetFormattedText(),
                                reason + " does not affect another object's valid Title");
                        }
                    }
                    Raw(new ModelKey("EVENT", "RT2_CONTENT_NAME_MISSING_MODEL"), GameContentKind.Ancient, "missing model");
                    Raw(default, GameContentKind.Ancient, "invalid identity");
                    translations[fixture.Id.Entry + ".title"] = "  ";
                    Raw(fixtureKey, GameContentKind.Ancient, "empty Title");
                    translations[fixture.Id.Entry + ".title"] = fixtureKey.Entry;
                    Raw(fixtureKey, GameContentKind.Ancient, "raw entry Title");
                    translations[fixture.Id.Entry + ".title"] = fixtureKey.Serialized;
                    Raw(fixtureKey, GameContentKind.Ancient, "raw full-ID Title");
                    translations.Remove(fixture.Id.Entry + ".title");
                    Raw(fixtureKey, GameContentKind.Ancient, "missing localization entry");
                    translations[fixture.Id.Entry + ".title"] = title;
                    fixture.ThrowOnLocTable = true;
                    try { Raw(fixtureKey, GameContentKind.Ancient, "throwing Title lookup"); }
                    finally { fixture.ThrowOnLocTable = false; }
                    Raw(fixtureKey, GameContentKind.Card, "wrong requested model type");
                    Raw(new ModelKey("RELIC", "NEOW"), GameContentKind.Ancient, "same vanilla entry in wrong category");
                    Raw(new ModelKey(fixtureKey.Category, fixtureKey.Entry.ToLowerInvariant()), GameContentKind.Ancient,
                        "entry case mismatch");
                    var alias = new ModelId(fixture.Id.Category, "RT2_CONTENT_NAME_ALIAS");
                    var categoryAlias = new ModelId("RELIC", fixture.Id.Entry);
                    registry.Add(alias, fixture);
                    registry.Add(categoryAlias, fixture);
                    try
                    {
                        Raw(new ModelKey(alias.Category, alias.Entry), GameContentKind.Ancient, "registered object entry mismatch");
                        Raw(new ModelKey(categoryAlias.Category, categoryAlias.Entry), GameContentKind.Ancient,
                            "registered object category mismatch");
                    }
                    finally { registry.Remove(alias); registry.Remove(categoryAlias); }
                    Check(Names("en").Resolve(fixtureKey, GameContentKind.Ancient) == title,
                        "one object's unavailable name does not affect other resolver requests");
                    Check(fixture.IsCanonical && fixture.Owner is null && fixture.Node is null &&
                        fixture.Description is null && !fixture.IsFinished,
                        "name reads leave external Ancient canonical, unstarted and unowned");
                    Check(new[] { "_dialogueSet", "_generatedOptions" }.All(name =>
                        typeof(AncientEventModel).GetField(name, instance)!.GetValue(fixture) is null) &&
                        new[] { "_currentOptions", "_dynamicVars" }.All(name =>
                        typeof(EventModel).GetField(name, instance)!.GetValue(fixture) is null),
                        "name reads never generate dialogue, options or event dynamic state");
                }
                finally { tables.Remove(ContentNameHostAncient.Table); }
            }
            Check(registry.Count == originalRegistry.Length + 1 && originalRegistry.All(pair =>
                registry.TryGetValue(pair.Key, out var value) && ReferenceEquals(value, pair.Value)),
                "name resolution preserves every original registered model");
        }
        finally
        {
            registry.Clear();
            foreach (var pair in originalRegistry) registry.Add(pair.Key, pair.Value);
            foreach (var state in dbFields) state.Field.SetValue(null, state.Value);
            foreach (var state in locFields) state.Field.SetValue(loc, state.Value);
            TranslationServer.SetLocale(originalLocale);
            surface.GetParent()?.RemoveChild(surface);
            surface.QueueFree();
        }
        Check(registry.Count == originalRegistry.Length && originalRegistry.All(pair =>
            registry.TryGetValue(pair.Key, out var value) && ReferenceEquals(value, pair.Value)),
            "entire model registry restored with original object identities");
        Check(locFields.All(state => state.Field.FieldType.IsValueType
                ? Equals(state.Field.GetValue(loc), state.Value)
                : ReferenceEquals(state.Field.GetValue(loc), state.Value)),
            "localization state and original table objects restored");
        Check(TranslationServer.GetLocale() == originalLocale &&
            MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language == originalSaveLanguage,
            "Godot locale restored and saved game language unchanged");
        Check(ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), runState), "real RunState untouched");
        File.WriteAllText(Path.Combine(output, "content-names-ui-checks.json"),
            JsonSerializer.Serialize(new { checks, restored = true, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"CONTENT_NAMES_UI_PASS checks={checks};external-Mod-Ancient;vanilla=Ancient,Event,Card,Relic;game=eng,zhs;ui=zh,en;raw-ID;registry+localization-restored");
    }
}

// Deliberately not in the sts2 assembly. The inherited Title uses the game's
// LocString machinery; execution-only members fail immediately if name lookup
// accidentally expands into event execution.
public sealed class ContentNameHostAncient : AncientEventModel
{
    internal const string Table = "rt2_content_name_host_fixture";
    internal bool ThrowOnLocTable { get; set; }
    public override string LocTable => ThrowOnLocTable
        ? throw new InvalidOperationException("Content-name fixture Title lookup failed") : Table;
    public override IEnumerable<EventOption> AllPossibleOptions =>
        throw new InvalidOperationException("Name lookup must not enumerate event options");
    protected override AncientDialogueSet DefineDialogues() =>
        throw new InvalidOperationException("Name lookup must not generate dialogue");
    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
        throw new InvalidOperationException("Name lookup must not execute an event");
}
