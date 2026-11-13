using RolltheSpire2.Compatibility.ModPrediction.Hextech;
using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility.ModPrediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static Assembly? _hextechForgeCheckAssembly;
    private static List<CardModel>? _hextechUpgradeSelections;
    private static void HextechObserveUpgrade(CardModel card) => _hextechUpgradeSelections?.Add(card);
    private const BindingFlags HextechMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Type HextechCheckType(string name) => _hextechForgeCheckAssembly!.GetType("HextechRunes." + name, true)!;
    private static MethodInfo HextechCheckMethod(string type, string name, params Type[] parameters) =>
        HextechCheckType(type).GetMethod(name, HextechMembers, null, parameters, null)
        ?? throw new MissingMethodException(type, name);

    // Explicit isolated fixture, not a full mod-coexistence claim. The actual
    // installed implementation supplies models and the independent oracle;
    // its initializer, telemetry, Workshop tracking and file-backed config do
    // not run. Default config comes from its own in-memory factory.
    private static void LoadHextechForgeCheckModels(string output)
    {
        var args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--hextech-package");
        string package = Path.GetFullPath(at >= 0 && at + 1 < args.Length ? args[at + 1]
            : "D:/Game/Steam/steamapps/workshop/content/2868840/3747501308");
        string installed = Path.Combine(package, "lib", "0.111.0", "HextechRunes.dll");
        string copied = Path.Combine(output, "hextech-oracle", "HextechRunes.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
        File.Copy(installed, copied, overwrite: false);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installed)));
        if (hash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copied))))
            throw new IOException("Hextech forge oracle copy does not match installed implementation");
        _hextechForgeCheckAssembly = AssemblyLoadContext.GetLoadContext(typeof(RelicModel).Assembly)!
            .LoadFromAssemblyPath(copied);
        if (!ProjectSettings.LoadResourcePack(Path.Combine(package, "HextechRunes.pck"), false))
            throw new IOException("Hextech forge check could not load installed resources");
        var metadata = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(Path.Combine(package, "HextechRunes.json")),
            new JsonSerializerOptions { IncludeFields = true })
            ?? throw new InvalidDataException("Hextech forge check manifest missing");
        // Type ownership metadata only. Do not publish a fabricated Loaded mod
        // in ModManager or invoke any native package initializer.
        AssemblyInfo.ModMap![_hextechForgeCheckAssembly] = new Mod { path = package, manifest = metadata };
        var forgeBase = HextechCheckType("HextechForgeBase");
        var modelTypes = _hextechForgeCheckAssembly.GetTypes().Where(type => !type.IsAbstract &&
            (forgeBase.IsAssignableFrom(type) || type.Name is "RandomForgeShopRelic" or "DiceManiacRune" or "HextechMayhemModifier"
                or "DoubleVisionRune" or "StrikeUpgradeRune" or "DefendUpgradeRune")).ToArray();
        foreach (var type in modelTypes)
        {
            ModelDb.Inject(type);
            if (typeof(RelicModel).IsAssignableFrom(type)) ModHelper.AddModelToPool(typeof(SharedRelicPool), type);
        }
        var config = HextechCheckType("HextechRuneConfiguration");
        object defaults = HextechCheckMethod("HextechRuneConfiguration", "CreateDefaultConfig").Invoke(null, null)!;
        config.GetField("_config", HextechMembers)!.SetValue(null, defaults);
        config.GetField("_loaded", HextechMembers)!.SetValue(null, true);
        File.WriteAllText(Path.Combine(output, "hextech-forge-oracle-load.json"), JsonSerializer.Serialize(new
        {
            installed, copied, sha256 = hash, metadata.version,
            implementation = _hextechForgeCheckAssembly.FullName,
            models = modelTypes.Select(type => type.FullName).ToArray(),
            fullModInitializer = false, config = "actual default factory, memory only", ownerRuntimeAcceptance = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task RunHextechForgeChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        Action<object, string, object?> set)
    {
        int checks = 0;
        void Check(bool value, string reason)
        { checks++; if (!value) throw new InvalidOperationException("HextechForge: " + reason); }
        string receipt = Path.Combine(output, "hextech-forge-result.json");
        var evidence = new List<object>();
        string? failure = null;
        bool passed = false;
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        var preferences = new SearchWorkspace(OS.GetUserDataDir(), false);
        var originalMode = InRunFeaturePreferences.GetOperationMode(HextechShopForgeOperationPreview.FeatureId);
        var originalRelics = player.Relics.ToArray();
        var originalDeck = player.Deck.Cards.ToArray();
        var addedCards = new List<CardModel>();
        var relicList = PredictorOperationCapture.Read<List<RelicModel>>(player, "_relics");
        var originalModifiers = run.Modifiers;
        var originalRunRng = run.Rng.ToSerializable();
        var originalPlayerRng = player.PlayerRng.ToSerializable();
        int originalGold = player.Gold;
        var fixture = new Harmony("rt2.hextech.forge.native.visuals");
        MerchantRoom? merchant = null;
        NMerchantInventory? shop = null;
        NeowOperationHoverController? Controller() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => Controller()?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        OperationPreview? Preview() => View() is { } view ? PredictorOperationCapture.ReadNullable(view, "_preview") as OperationPreview : null;
        async Task Frames(int count = 3)
        { for (int frame = 0; frame < count; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var watch = Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(8)) await Frames(1);
            Check(ready(), reason);
        }
        RelicModel Relic(string name) => ModelDb.GetById<RelicModel>(ModelDb.GetId(HextechCheckType(name))).ToMutable();
        object DefaultConfig() => HextechCheckMethod("HextechRuneConfiguration", "GetDefaultSnapshot").Invoke(null, null)!;
        object config = DefaultConfig();
        var modifier = ModelDb.GetById<ModifierModel>(ModelDb.GetId(HextechCheckType("HextechMayhemModifier"))).ToMutable();
        void ApplyConfig(object value)
        {
            HextechCheckMethod("HextechMayhemModifier", "SetRunConfigurationSnapshot", value.GetType(), typeof(string))
                .Invoke(modifier, [value, "isolated forge check"]);
        }
        void Config(string name, object value) => config.GetType().GetProperty(name, HextechMembers)!.SetValue(config, value);
        RelicModel placeholder = Relic("RandomForgeShopRelic");
        void Ordinal(int value) => placeholder.GetType().GetProperty("SavedPurchaseCount")!.SetValue(placeholder, value);
        List<RelicModel> NativeOptions(Player owner, int ordinal)
        {
            object?[] parameters = [owner, ordinal, null];
            bool available = (bool)HextechCheckMethod("HextechForgeGrantHelper", "TryCreateStableShopForgeChoice",
                typeof(Player), typeof(int), typeof(List<RelicModel>).MakeByRefType()).Invoke(null, parameters)!;
            var options = (List<RelicModel>)parameters[2]!;
            Check(available == (options.Count > 0), "actual helper availability agrees with returned options");
            return options;
        }
        ModelKey Key(RelicModel relic) => new(relic.Id.Category, relic.Id.Entry);
        string NativeState(RunState? state = null, Player? owner = null, RelicModel? shopModel = null) =>
            JsonSerializer.Serialize(new { run = (state ?? run).Rng.ToSerializable(), personal = (owner ?? player).PlayerRng.ToSerializable(),
            relics = (owner ?? player).Relics.Select(relic => relic.ToSerializable()), gold = (owner ?? player).Gold,
            deck = (owner ?? player).Deck.Cards.Select(card => new { id = PredictorLiveRunCapture.Id(card), value = card.ToSerializable() }),
            ordinal = (shopModel ?? placeholder).GetType().GetProperty("PurchaseCount")!.GetValue(shopModel ?? placeholder),
            config = modifier.GetType().GetProperty("SavedHextechRunConfigurationSnapshotJson")!.GetValue(modifier) });
        HextechShopForgeResult Verify(string name, RunState currentRun, Player currentPlayer, RelicModel currentPlaceholder)
        {
            string before = NativeState(currentRun, currentPlayer, currentPlaceholder);
            var snapshot = HextechShopForgeCapture.Capture(currentRun, currentPlaceholder);
            var expected = NativeOptions(currentPlayer, snapshot.PurchaseOrdinal);
            var result = HextechShopForgePrediction.Predict(snapshot);
            Check(result.Options.SequenceEqual(expected.Select(Key)), name + " option identities and order match installed helper");
            if (snapshot.DirectGrant && expected.Count > 0)
            {
                var direct = (RelicModel)HextechCheckMethod("HextechForgeSelectionCoordinator", "PickStableRandomForge",
                    typeof(Player), typeof(IReadOnlyList<RelicModel>), typeof(string)).Invoke(null, [currentPlayer, expected, "shop"])!;
                Check(result.DirectGrant == Key(direct), name + " direct grant matches installed selection coordinator");
            }
            else Check(result.DirectGrant == null, name + " ordinary choice has no invented selected outcome");
            Check(before == NativeState(currentRun, currentPlayer, currentPlaceholder),
                name + " capture, pure prediction and native generation leave live state unchanged");
            evidence.Add(new { name, snapshot.PlayerKey, snapshot.PurchaseOrdinal, snapshot.RelicCount, snapshot.Weights,
                snapshot.DirectGrant, expected = expected.Select(relic => relic.Id.Entry).ToArray(),
                actual = result.Options.Select(key => key.Entry).ToArray(), granted = result.DirectGrant?.Entry });
            return result;
        }
        void FixtureDeck(params CardModel[] cards)
        {
            foreach (var card in player.Deck.Cards.ToArray()) player.Deck.RemoveInternal(card, silent: true);
            foreach (var card in addedCards) run.RemoveCard(card);
            addedCards.Clear();
            foreach (var card in cards)
            { run.AddCard(card, player); player.Deck.AddInternal(card, silent: true); addedCards.Add(card); }
        }
        void OnlyForge(string name)
        {
            var keep = ModelDb.GetId(HextechCheckType(name)).Entry;
            Config("DisabledForgeIds", ((IEnumerable)HextechCheckType("HextechContentRegistry")
                .GetProperty("AllForgeTypes", HextechMembers)!.GetValue(null)!).Cast<Type>()
                .Select(type => ModelDb.GetId(type).Entry).Where(id => id != keep).ToHashSet(StringComparer.Ordinal));
            Config("ForgeRarityWeights", Activator.CreateInstance(HextechCheckType("HextechRarityWeights"), 0, 0, 0)!);
            Config("RandomForgeDirectGrant", false); ApplyConfig(config);
        }
        async Task VerifyUpgrade(string name, string forgeName, CardModel[] cards, int copies = 0, bool owned = false, bool starterRune = false)
        {
            FixtureDeck(cards); OnlyForge(forgeName);
            var extras = new List<RelicModel>();
            RelicModel Add(string type)
            { var model = Relic(type); player.AddRelicInternal(model, silent: true); extras.Add(model); return model; }
            if (starterRune) Add("StrikeUpgradeRune");
            for (int copy = 0; copy < copies; copy++) Add("DoubleVisionRune");
            var forge = owned ? Add(forgeName) : Relic(forgeName);
            if (owned) forge.GetType().GetProperty("SavedStackCount")!.SetValue(forge, 3);
            try
            {
                string before = NativeState();
                var captured = HextechShopForgeCapture.Capture(run, placeholder);
                var result = HextechShopForgePrediction.Predict(captured);
                Check(before == NativeState(), name + " deck capture and worker prediction are read-only");
                var prediction = result.Upgrades.Single(upgrade => upgrade.Forge == Key(forge));
                Check(prediction.Issue == null, name + " supported upgrade has no unverified remainder");
                Check(prediction.GrantCount == copies + 1, name + " complete current purchase includes every active DoubleVision copy");
                Check(prediction.Targets.All(target => target.Before.HasEtherealKeyword ==
                    cards.Single(card => PredictorLiveRunCapture.Id(card) == target.Before.Id).Keywords.Contains(CardKeyword.Ethereal)),
                    name + " captured target preserves actual persistent Ethereal state");
                if (name == "persistent-ethereal")
                {
                    cards[0].RemoveKeyword(CardKeyword.Ethereal);
                    Check(captured.Fingerprint != HextechShopForgeCapture.Capture(run, placeholder).Fingerprint,
                        "persistent Ethereal change invalidates forge capture fingerprint");
                    cards[0].AddKeyword(CardKeyword.Ethereal);
                }
                if (copies > 0)
                {
                    var vision = extras.First(relic => relic.GetType() == HextechCheckType("DoubleVisionRune"));
                    int index = relicList.IndexOf(vision), count = player.Relics.Count;
                    var unrelated = ModelDb.Relic<Anchor>().ToMutable();
                    relicList.Remove(vision); player.AddRelicInternal(unrelated, silent: true);
                    try
                    {
                        var swapped = HextechShopForgeCapture.Capture(run, placeholder);
                        var swappedResult = HextechShopForgePrediction.Predict(swapped);
                        Check(player.Relics.Count == count && swapped.RelicCount == captured.RelicCount && swappedResult.Options.SequenceEqual(result.Options),
                            name + " replacing DoubleVision preserves relic count and current forge identities");
                        Check(swapped.Fingerprint != captured.Fingerprint &&
                            swappedResult.Upgrades.Single(upgrade => upgrade.Forge == Key(forge)).GrantCount == prediction.GrantCount - 1,
                            name + " DoubleVision amount alone invalidates the full-purchase upgrade forecast");
                    }
                    finally { relicList.Remove(unrelated); relicList.Insert(index, vision); }
                }
                _hextechUpgradeSelections = [];
                if (owned)
                    await (Task<RelicModel>)HextechCheckMethod("HextechForgeStackingHooks", "ObtainStackedForge",
                        HextechCheckType("HextechForgeBase")).Invoke(null, [forge])!;
                else
                {
                    player.AddRelicInternal(forge, silent: true); extras.Add(forge);
                    await forge.AfterObtained();
                }
                var firstGrant = _hextechUpgradeSelections.Select(card => new
                    { id = PredictorLiveRunCapture.Id(card), card.Id.Entry, card.CurrentUpgradeLevel }).ToArray();
                await (Task)HextechCheckMethod("DoubleVisionRune", "DuplicatePurchasedForge", typeof(Player), typeof(RelicModel))
                    .Invoke(null, [player, forge])!;
                var actual = _hextechUpgradeSelections.Distinct<CardModel>(ReferenceEqualityComparer.Instance).ToArray();
                Check(prediction.Targets.Select(target => target.Before.Id).SequenceEqual(actual.Select(PredictorLiveRunCapture.Id)),
                    name + " full purchase aggregates exact instances in first-affected order");
                Check(prediction.Targets.All(target => target.After is { } after &&
                    after.UpgradeLevel == actual.Single(card => PredictorLiveRunCapture.Id(card) == after.Id).CurrentUpgradeLevel),
                    name + " predicted upgraded levels match every native grant's final state");
                Check(prediction.Targets.All(target => target.After is { } after &&
                    after.HasEtherealKeyword == actual.Single(card => PredictorLiveRunCapture.Id(card) == after.Id).Keywords.Contains(CardKeyword.Ethereal) &&
                    OperationPreviewCardModel.Create(after).Keywords.Contains(CardKeyword.Ethereal) == after.HasEtherealKeyword),
                    name + " upgraded snapshot and detached native card face preserve Ethereal");
                Check(prediction.Targets.All(target =>
                {
                    var native = actual.Single(card => PredictorLiveRunCapture.Id(card) == target.Before.Id);
                    var face = OperationPreviewCardModel.Create(target.After!);
                    return face.CurrentUpgradeLevel == native.CurrentUpgradeLevel && face.DynamicVars.Damage.BaseValue == native.DynamicVars.Damage.BaseValue &&
                        face.EnergyCost.Canonical == native.EnergyCost.Canonical && face.Keywords.ToHashSet().SetEquals(native.Keywords) && face.RunState == null;
                }), name + " final detached faces match actual upgraded levels, damage, cost and keywords");
                Check(prediction.Targets.Length <= (forgeName == "UpgradeForge" ? 2 : 4) * prediction.GrantCount,
                    name + " full purchase contains at most the actual per-grant upgrade budget");
                evidence.Add(new { name, forgeName, copies, owned, starterRune, prediction.GrantCount, firstGrant,
                    selections = _hextechUpgradeSelections.Select(PredictorLiveRunCapture.Id).ToArray(),
                    final = actual.Select(card => new { id = PredictorLiveRunCapture.Id(card), card.Id.Entry, card.CurrentUpgradeLevel }).ToArray() });
            }
            finally { _hextechUpgradeSelections = null; foreach (var relic in extras) relicList.Remove(relic); }
        }
        async Task VerifyCommonPurchases(string name, string[] allowed, string? initiallyOwned = null,
            bool direct = false, bool retainedStateCase = false)
        {
            var baseline = player.Relics.ToArray();
            void Reset()
            {
                foreach (var relic in player.Relics.Where(relic => !baseline.Contains(relic)).ToArray()) relicList.Remove(relic);
                if (initiallyOwned != null) player.AddRelicInternal(Relic(initiallyOwned), silent: true);
            }
            try
            {
                Config("DisabledForgeIds", ((IEnumerable)HextechCheckType("HextechContentRegistry")
                    .GetProperty("AllForgeTypes", HextechMembers)!.GetValue(null)!).Cast<Type>()
                    .Where(type => !allowed.Contains(type.Name)).Select(type => ModelDb.GetId(type).Entry)
                    .ToHashSet(StringComparer.Ordinal));
                Config("ForgeRarityWeights", Activator.CreateInstance(HextechCheckType("HextechRarityWeights"), 1, 0, 0)!);
                Config("RandomForgeDirectGrant", direct); ApplyConfig(config); Reset();
                HextechShopForgeSnapshot captured = null!;
                HextechShopForgeResult predicted = null!;
                bool found = false;
                for (int ordinal = 0; ordinal < 128; ordinal++)
                {
                    Ordinal(ordinal); captured = HextechShopForgeCapture.Capture(run, placeholder);
                    predicted = HextechShopForgePrediction.Predict(captured);
                    if (!retainedStateCase || predicted.Purchases.Length == 2 &&
                        predicted.Options[0] != Key(player.Relics.Single(relic => relic.GetType() == HextechCheckType(initiallyOwned!))))
                    { found = true; break; }
                }
                Check(found, name + " located a native candidate vector for the branch case");
                string before = NativeState();
                var repeated = HextechShopForgePrediction.Predict(HextechShopForgeCapture.Capture(run, placeholder));
                Check(before == NativeState(), name + " common-prefix capture and prediction are read-only");
                Check(repeated.Purchases.Length == predicted.Purchases.Length && repeated.Purchases.Zip(predicted.Purchases)
                    .All(pair => pair.First.PurchaseOrdinal == pair.Second.PurchaseOrdinal &&
                        pair.First.DirectGrant == pair.Second.DirectGrant && pair.First.Options.SequenceEqual(pair.Second.Options)),
                    name + " prefix is deterministic");
                var expected = new List<HextechShopForgePurchase>();
                var paths = new List<ModelKey[]> { Array.Empty<ModelKey>() };
                for (int depth = 0; depth < 5; depth++)
                {
                    HextechShopForgePurchase? common = null;
                    var next = new List<ModelKey[]>();
                    bool diverged = false;
                    foreach (var path in paths)
                    {
                        Reset();
                        foreach (var chosen in path)
                        {
                            var owned = player.Relics.FirstOrDefault(relic => Key(relic) == chosen);
                            if (owned != null)
                                await (Task<RelicModel>)HextechCheckMethod("HextechForgeStackingHooks", "ObtainStackedForge",
                                    HextechCheckType("HextechForgeBase")).Invoke(null, [owned])!;
                            else
                            {
                                var acquired = ModelDb.GetById<RelicModel>(new ModelId(chosen.Category, chosen.Entry)).ToMutable();
                                player.AddRelicInternal(acquired, silent: true); await acquired.AfterObtained();
                            }
                        }
                        var options = NativeOptions(player, captured.PurchaseOrdinal + depth);
                        ModelKey? granted = direct && options.Count > 0 ? Key((RelicModel)HextechCheckMethod(
                            "HextechForgeSelectionCoordinator", "PickStableRandomForge", typeof(Player),
                            typeof(IReadOnlyList<RelicModel>), typeof(string)).Invoke(null, [player, options, "shop"])!) : null;
                        var current = new HextechShopForgePurchase(captured.PurchaseOrdinal + depth,
                            options.Select(Key).ToImmutableArray(), granted);
                        if (common != null && (common.DirectGrant != current.DirectGrant || !common.Options.SequenceEqual(current.Options)))
                        { diverged = true; break; }
                        common ??= current;
                        foreach (var choice in granted is { } selected ? new[] { selected } : current.Options.ToArray())
                            next.Add(path.Append(choice).ToArray());
                    }
                    if (diverged || common == null || common.Options.IsEmpty) break;
                    expected.Add(common); paths = next;
                }
                Check(predicted.Purchases.Length == expected.Count && predicted.Purchases.Zip(expected).All(pair =>
                    pair.First.Options.SequenceEqual(pair.Second.Options) && pair.First.DirectGrant == pair.Second.DirectGrant),
                    name + " exact common prefix matches actual helper after every possible native acquisition path");
                if (allowed.Length == 1 || direct) Check(predicted.Purchases.Length == 5, name + " deterministic path reaches five offers");
                if (retainedStateCase) Check(predicted.Purchases.Length == 2 && predicted.ContinuationIssue == "HextechForgeChoiceDivergence",
                    name + " equal next offers retain differing owned states and stop at later divergence");
                var unknown = HextechShopForgePrediction.Predict(captured with { ContinuationForges = [] });
                Check(unknown.Options.SequenceEqual(predicted.Options) && unknown.Purchases.Length == 1 &&
                    unknown.ContinuationIssue == "HextechForgeContinuationUnsupported", name + " unknown acquisition preserves current offer only");
                evidence.Add(new { name, initiallyOwned, direct, captured.PurchaseOrdinal,
                    prefix = predicted.Purchases.Select(purchase => purchase.Options.Select(key => key.Entry).ToArray()).ToArray(),
                    predicted.ContinuationIssue });
            }
            finally
            {
                foreach (var relic in player.Relics.Where(relic => !baseline.Contains(relic)).ToArray()) relicList.Remove(relic);
                config = DefaultConfig(); ApplyConfig(config); Ordinal(0);
            }
        }
        try
        {
            // Only audited native patches needed for these effects are loaded.
            // All target selection and actual upgrades still execute mod code.
            fixture.CreateClassProcessor(HextechCheckType("HextechStarterUpgradeHooks+MaxUpgradeLevelPatch")).Patch();
            fixture.CreateClassProcessor(HextechCheckType("HextechStarterUpgradeHooks+FromSerializablePatch")).Patch();
            fixture.CreateClassProcessor(HextechCheckType("HextechForgeStackingHooks+ObtainPatch")).Patch();
            fixture.Patch(AccessTools.Method(typeof(CardCmd), nameof(CardCmd.Upgrade),
                [typeof(CardModel), typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle)]),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(HextechObserveUpgrade)));
            // The native manager normally discovers these tables from Loaded
            // package metadata. This partial-load fixture reads the same actual
            // PCK tables directly without claiming the full mod was initialized.
            foreach (string language in new[] { "eng", LocManager.Instance.Language }.Distinct())
                foreach (string tableName in new[] { "relics", "relic_collection" })
                {
                    string resource = $"res://HextechRunes/localization/{language}/{tableName}.json";
                    if (!Godot.FileAccess.FileExists(resource)) continue;
                    var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(Godot.FileAccess.GetFileAsString(resource))!;
                    var native = PredictorOperationCapture.Read<Dictionary<string, string>>(LocManager.Instance.GetTable(tableName), "_translations");
                    foreach (var (key, value) in translations) native[key] = value;
                }
            preferences.SetOperationPredictionMode(HextechShopForgeOperationPreview.FeatureId, OperationPredictionMode.Auto);
            run.AddModifierDebug(modifier); set(modifier, "_runState", run); ApplyConfig(config);
            merchant = new MerchantRoom(); run.PushRoom(merchant); set(merchant, "_runState", run);
            var inventory = new MerchantInventory(player); merchant.Inventories.Add(inventory);
            var entry = new MerchantRelicEntry(placeholder, player); inventory.AddRelicEntry(entry);
            // Full package price hooks are intentionally absent. Mount its
            // default current quote on this controlled native offer.
            set(entry, "_cost", 250);
            player.Gold = 999;
            Check(HextechShopForgeCapture.IsForge(placeholder), "actual installed placeholder is recognized");
            foreach (int ordinal in new[] { 0, 1, 7 }) { Ordinal(ordinal); Verify("default-" + ordinal, run, player, placeholder); }
            var dice = Relic("DiceManiacRune"); player.AddRelicInternal(dice, silent: true);
            Verify("dice-maniac", run, player, placeholder); relicList.Remove(dice);
            Config("RandomForgeDirectGrant", true); ApplyConfig(config);
            Verify("direct-grant", run, player, placeholder);
            // Use captured actual pool IDs to disable the entire normally rolled
            // tier. The native helper alone determines its cross-tier fallback.
            var current = HextechShopForgeCapture.Capture(run, placeholder);
            Config("DisabledForgeIds", current.SilverPool.Select(key => key.Entry).ToHashSet(StringComparer.Ordinal));
            Config("ForgeRarityWeights", Activator.CreateInstance(HextechCheckType("HextechRarityWeights"), 1, 0, 0)!); ApplyConfig(config);
            Verify("empty-silver-falls-back", run, player, placeholder);
            current = HextechShopForgeCapture.Capture(run, placeholder);
            var allIds = current.AllPool.Select(key => key.Entry).Concat(current.SilverPool.Select(key => key.Entry))
                .Concat(((IEnumerable)HextechCheckType("HextechContentRegistry").GetProperty("AllForgeTypes", HextechMembers)!.GetValue(null)!)
                    .Cast<Type>().Select(type => ModelDb.GetId(type).Entry)).ToHashSet(StringComparer.Ordinal);
            Config("DisabledForgeIds", allIds); ApplyConfig(config);
            Check(Verify("all-disabled", run, player, placeholder).Options.IsEmpty, "all-disabled pool has no fabricated option");
            config = DefaultConfig(); ApplyConfig(config); Ordinal(0);
            foreach (var character in new CharacterModel[] { ModelDb.Character<Defect>(), ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>() })
            {
                var otherPlayer = Player.CreateForNewRun(character, UnlockState.all, player.NetId);
                var otherRun = RunState.CreateForTest([otherPlayer], modifiers: [modifier], seed: "HEXTECH-" + character.Id.Entry);
                Verify("character-" + character.Id.Entry, otherRun, otherPlayer, Relic("RandomForgeShopRelic"));
            }
            CardModel Strike() => ModelDb.Card<StrikeIronclad>().ToMutable();
            CardModel BashCard() => ModelDb.Card<Bash>().ToMutable();
            CardModel Upgraded(CardModel card) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); return card; }
            CardModel Ethereal(CardModel card) { card.AddKeyword(CardKeyword.Ethereal); return card; }
            await VerifyCommonPurchases("common-five-single-forge", ["StrengthForge"]);
            await VerifyCommonPurchases("common-new-choice-next-row", ["StrengthForge", "DexterityForge", "VigorForge"]);
            await VerifyCommonPurchases("common-retained-owned-states", ["StrengthForge", "DexterityForge"], "StrengthForge", retainedStateCase: true);
            await VerifyCommonPurchases("common-five-direct-grant", ["StrengthForge", "DexterityForge"], "StrengthForge", direct: true);
            await VerifyUpgrade("silver-duplicate-instances", "UpgradeForge", [Strike(), Strike(), Strike(), Strike(), BashCard(), ModelDb.Card<Anger>().ToMutable()]);
            await VerifyUpgrade("silver-equal-key-deck-order", "UpgradeForge", [Strike(), Strike(), Strike(), Strike(), Strike(), Strike()]);
            await VerifyUpgrade("silver-already-upgraded-excluded", "UpgradeForge", [Upgraded(BashCard()), Upgraded(Strike()), Strike(), BashCard()]);
            await VerifyUpgrade("silver-insufficient", "UpgradeForge", [Upgraded(BashCard()), Strike()]);
            await VerifyUpgrade("silver-none-eligible", "UpgradeForge", [Upgraded(BashCard()), Upgraded(Strike())]);
            await VerifyUpgrade("gold-four", "GoldUpgradeForge", [Strike(), Strike(), BashCard(), ModelDb.Card<Anger>().ToMutable(), Strike(), BashCard()]);
            await VerifyUpgrade("owned-stack-is-not-count-multiplier", "UpgradeForge", [Strike(), Strike(), Strike(), Strike(), BashCard()], owned: true);
            await VerifyUpgrade("double-vision-sequential-grants", "UpgradeForge", [Strike(), Strike(), Strike(), Strike(), BashCard(), BashCard()], copies: 1);
            await VerifyUpgrade("gold-double-vision-complete-purchase", "GoldUpgradeForge", [Strike(), Strike(), BashCard(), Strike(), BashCard(),
                ModelDb.Card<Anger>().ToMutable(), Strike(), Strike(), BashCard()], copies: 1);
            await VerifyUpgrade("double-vision-exhausted-upgrade-pool", "UpgradeForge", [Strike(), BashCard(), Strike()], copies: 2);
            await VerifyUpgrade("starter-rune-repeated-levels", "UpgradeForge", [Strike(), Strike(), Strike()], copies: 2, owned: true, starterRune: true);
            await VerifyUpgrade("starter-rune-final-levels-two-and-three", "UpgradeForge", [Upgraded(Strike()), Strike()], copies: 1, starterRune: true);
            await VerifyUpgrade("persistent-ethereal", "UpgradeForge", [Ethereal(Strike()), Ethereal(BashCard()), Ethereal(Strike())]);
            config = DefaultConfig(); ApplyConfig(config); Ordinal(0);
            FixtureDeck(Strike(), Strike(), BashCard(), ModelDb.Card<Anger>().ToMutable(), Strike());
            shop = new NMerchantInventory(); _neowStaticSceneNodes.Add(shop);
            // Match the existing isolated native shop fixture: the complete
            // inventory scene needs a game-only Spine hand and dialogue tree.
            // Its ownership and actual slot callbacks stay live here.
            foreach (string callback in new[] { "_Ready", "_EnterTree", "_ExitTree" })
                fixture.Patch(AccessTools.Method(typeof(NMerchantInventory), callback),
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowSkipStaticSceneStartup)));
            foreach (string callback in new[] { "OnMerchantHandHovered", "OnMerchantHandUnhovered", "TriggerMerchantHandToPointHere" })
                fixture.Patch(AccessTools.Method(typeof(NMerchantSlot), callback),
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            foreach (var oldLayout in game.GetChildren().OfType<NEventLayout>()) oldLayout.Hide();
            set(shop, nameof(NMerchantInventory.Inventory), inventory); set(shop, nameof(NMerchantInventory.IsOpen), true);
            game.AddChild(shop); game.MoveChild(shop, 0); shop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var slot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_relic.tscn").Instantiate<NMerchantRelic>();
            shop.AddChild(slot); slot.Position = new(1390, 690); slot.Initialize(shop); slot.FillSlot(entry);
            await Frames(4);
            var matched = ModOperationPredictions.Match(slot);
            Check(matched is { } && matched.Capability.Setting.Id == HextechShopForgeOperationPreview.FeatureId &&
                matched.Source == Key(placeholder) && ReferenceEquals(matched.Run, run), "shared mod registry retains this native shop operation's sole ownership");
            var unrelated = new Control();
            try { Check(ModOperationPredictions.Match(unrelated) == null, "unrelated native control has no fabricated mod operation match"); }
            finally { unrelated.Free(); }
            viewport.PushInput(new InputEventMouseMotion { Position = slot.Hitbox.GetGlobalRect().GetCenter(),
                GlobalPosition = slot.Hitbox.GetGlobalRect().GetCenter() }, true);
            async Task CheckDisplayed(string name)
            {
                var captured = HextechShopForgeCapture.Capture(run, placeholder);
                var expected = HextechShopForgePrediction.Predict(captured);
                var models = expected.DirectGrant is { } direct ? new[] { direct } : expected.Options.ToArray();
                var action = captured.DirectGrant ? OperationPreviewAction.ObtainRelic : OperationPreviewAction.Choose;
                try
                {
                    await Until(() => Controller() is { } controller &&
                        PredictorOperationCapture.ReadNullable(controller, "_modSnapshot") is ModOperationSnapshot latest &&
                        latest.Fingerprint == captured.Fingerprint && Preview() is { Faults.IsEmpty: true } preview &&
                        preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                            .Any(row => row.Action == action && row.Models.SequenceEqual(models)) &&
                        expected.Upgrades.All(upgrade => preview.Branches.SelectMany(branch => branch.Sections)
                            .Any(section => section.Source == upgrade.Forge && section.Rows
                                .Where(row => row.Action == OperationPreviewAction.UpgradeCard).SelectMany(row => row.Cards)
                                .SequenceEqual(upgrade.Cards))), name + " native hover publishes current ordered models and upgrade cards");
                }
                catch
                {
                    bool admitted = OperationSurfaceHoverCapture.TryShop(slot, out var admittedRun, out long admittedId, out var source);
                    File.WriteAllText(Path.Combine(output, "hextech-forge-ui-failure.json"), JsonSerializer.Serialize(new
                    {
                        name, admitted, sameRun = ReferenceEquals(run, admittedRun), admittedId, source,
                        ownerVisible = slot.IsVisibleInTree(), hitbox = slot.Hitbox.GetGlobalRect().ToString(),
                        mouse = viewport.GetMousePosition().ToString(), controller = Controller() != null,
                        request = Controller() == null ? null : PredictorOperationCapture.ReadNullable(Controller()!, "_request")?.ToString(),
                        latest = Controller() == null ? null : PredictorOperationCapture.ReadNullable(Controller()!, "_modSnapshot")?.ToString(),
                        expected = models.Select(key => key.Entry).ToArray(),
                        actual = Preview()?.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                            .Select(row => new { row.Action, models = row.Models.Select(key => key.Entry).ToArray() }).ToArray(),
                        notices = Preview()?.Notices.ToArray(), faults = Preview()?.Faults.Select(fault => fault.Exception.ToString()).ToArray()
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    throw;
                }
                Check(View()!.IsVisibleInTree() && Descendants(View()!).OfType<Label>().Any(label => label.Text.Length > 0),
                    name + " preview contains visible rendered content");
                Check(Preview()!.Branches.SelectMany(branch => branch.Sections).All(section =>
                    section.FeatureId == HextechShopForgeOperationPreview.FeatureId), name + " uses its own preference leaf");
                foreach (var upgrade in expected.Upgrades)
                {
                    var section = Preview()!.Branches.SelectMany(branch => branch.Sections).Single(section => section.Source == upgrade.Forge);
                    Check(section.Rows.Where(row => row.Action == OperationPreviewAction.UpgradeCard).SelectMany(row => row.Cards)
                        .SequenceEqual(upgrade.Cards), name + " upgrade cards remain associated with the correct candidate and exact instances");
                    Check(section.Label == (captured.DirectGrant ? null : "UiText:operation.hextechForge.ifChosen"), name + " upgrade choice condition matches grant mode");
                    Check(section.Rows.Where(row => row.Action == OperationPreviewAction.UpgradeCard && row.ShowCards && !row.Cards.IsEmpty)
                        .All(row => row.Label == (upgrade.GrantCount > 1 && upgrade.Issue == null ? "UiText:operation.hextechForge.withDoubleVision" : null)),
                        name + " complete-purchase final label appears only when DoubleVision contributes");
                }
            }
            string untouched = NativeState();
            await CheckDisplayed("ordinary");
            Check(untouched == NativeState(), "native focus and worker publication do not change RNG, relics, gold or purchase count");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hextech-forge-choice-ui.png")) == Error.Ok,
                "ordinary three-option merchant preview screenshot saved");
            Ordinal(3); await CheckDisplayed("purchase-count-refresh");
            Config("RandomForgeDirectGrant", true); ApplyConfig(config); await CheckDisplayed("direct-grant-refresh");
            var added = ModelDb.Relic<Anchor>().ToMutable(); player.AddRelicInternal(added, silent: true);
            await CheckDisplayed("relic-count-refresh"); relicList.Remove(added);
            preferences.SetOperationPredictionMode(HextechShopForgeOperationPreview.FeatureId, OperationPredictionMode.Off);
            await Until(() => View() == null, "turning the forge preference off removes the current preview");
            preferences.SetOperationPredictionMode(HextechShopForgeOperationPreview.FeatureId, OperationPredictionMode.Auto);
            await CheckDisplayed("auto-restored");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hextech-forge-ui.png")) == Error.Ok,
                "native merchant preview screenshot saved");
            OnlyForge("UpgradeForge"); await CheckDisplayed("silver-upgrade-associated");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hextech-forge-silver-upgrade-ui.png")) == Error.Ok,
                "silver forge and associated upgraded card faces screenshot saved");
            string beforeUpgrade = HextechShopForgeCapture.Capture(run, placeholder).Fingerprint;
            CardCmd.Upgrade(player.Deck.Cards.First(card => card.IsUpgradable));
            Check(beforeUpgrade != HextechShopForgeCapture.Capture(run, placeholder).Fingerprint, "deck upgrade changes forge fingerprint");
            await CheckDisplayed("deck-upgrade-refresh");
            OnlyForge("GoldUpgradeForge"); await CheckDisplayed("gold-upgrade-associated");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hextech-forge-gold-upgrade-ui.png")) == Error.Ok,
                "gold forge and associated upgraded card faces screenshot saved");
            Config("RandomForgeDirectGrant", true); ApplyConfig(config); await CheckDisplayed("gold-direct-upgrade");
            var vision = Relic("DoubleVisionRune");
            var starter = Relic("StrikeUpgradeRune");
            player.AddRelicInternal(vision, silent: true);
            try
            {
                FixtureDeck(Strike(), Strike(), Strike(), Strike(), BashCard(), BashCard());
                OnlyForge("UpgradeForge"); await CheckDisplayed("double-vision-complete-purchase");
                var complete = HextechShopForgePrediction.Predict(HextechShopForgeCapture.Capture(run, placeholder)).Upgrades.Single();
                Check(complete.GrantCount == 2 && complete.Targets.Length == 4,
                    "DoubleVision screenshot displays all four upgrade targets from this purchase");
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hextech-forge-double-vision-ui.png")) == Error.Ok,
                    "complete DoubleVision purchase screenshot saved");
                FixtureDeck(Strike(), Strike(), BashCard(), Strike(), BashCard(), ModelDb.Card<Anger>().ToMutable(), Strike(), Strike(), BashCard());
                OnlyForge("GoldUpgradeForge"); await CheckDisplayed("gold-double-vision-eight-final-faces");
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hextech-forge-gold-double-vision-ui.png")) == Error.Ok,
                    "gold DoubleVision eight final card faces screenshot saved");
                player.AddRelicInternal(starter, silent: true);
                FixtureDeck(Upgraded(Strike()), Strike());
                OnlyForge("UpgradeForge");
                await CheckDisplayed("double-vision-starter-final-levels");
                var repeated = HextechShopForgePrediction.Predict(HextechShopForgeCapture.Capture(run, placeholder)).Upgrades.Single();
                Check(repeated.Cards.Select(card => card.UpgradeLevel).Order().SequenceEqual([2, 3]),
                    "starter-rune screenshot predicts final +2 and +3 faces after the whole purchase");
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hextech-forge-double-vision-starter-ui.png")) == Error.Ok,
                    "DoubleVision starter +2 and +3 final faces screenshot saved");
            }
            finally { relicList.Remove(starter); relicList.Remove(vision); }
            passed = true;
            GD.Print($"HEXTECH_FORGE_PASS checks={checks};installedOracle=true;nativeHover=true;captureReadOnly=true");
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            if (shop != null && GodotObject.IsInstanceValid(shop)) shop.Free();
            Controller()?.QueueFree();
            fixture.UnpatchAll(fixture.Id);
            _hextechUpgradeSelections = null;
            foreach (var card in player.Deck.Cards.ToArray()) player.Deck.RemoveInternal(card, silent: true);
            foreach (var card in addedCards) run.RemoveCard(card);
            foreach (var card in originalDeck) player.Deck.AddInternal(card, silent: true);
            if (merchant != null && ReferenceEquals(run.CurrentRoom, merchant)) run.PopCurrentRoom();
            relicList.Clear(); relicList.AddRange(originalRelics);
            set(run, nameof(RunState.Modifiers), originalModifiers);
            run.Rng.LoadFromSerializable(originalRunRng); player.PlayerRng.LoadFromSerializable(originalPlayerRng); player.Gold = originalGold;
            preferences.SetOperationPredictionMode(HextechShopForgeOperationPreview.FeatureId, originalMode);
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed, checks, failure, cases = evidence,
                installedNativeHelperOracle = true, fullModInitializer = false, actualPurchaseExecuted = false,
                ownerRuntimeAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
