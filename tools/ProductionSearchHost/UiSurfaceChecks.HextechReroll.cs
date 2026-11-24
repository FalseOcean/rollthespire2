using System.Collections;
using System.Reflection;
using Expression = System.Linq.Expressions.Expression;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility.ModPrediction;
using RolltheSpire2.Compatibility.ModPrediction.Hextech;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static NOverlayStack? _hextechRerollOverlay;
    private static int _hextechChaosInvocations;
    private static List<RelicModel> HextechFixtureChaos(IReadOnlyList<RelicModel> options)
    { _hextechChaosInvocations++; return options.ToList(); }
    private static bool HextechRerollOverlayInstance(ref NOverlayStack? __result)
    { __result = _hextechRerollOverlay; return false; }
    private static bool HextechRerollStackLifecycle(NOverlayStack __instance) => !ReferenceEquals(__instance, _hextechRerollOverlay);
    private static bool HextechRerollFixturePush(NOverlayStack __instance, IOverlayScreen screen)
    {
        // The host omits global navigation/backstop animation. It retains the
        // actual stack ownership, native screen, buttons and refresh callbacks.
        __instance.AddChild((Node)screen);
        PredictorOperationCapture.Read<List<IOverlayScreen>>(__instance, "_overlays").Add(screen);
        return false;
    }
    private static void LoadHextechRerollCheckModels(string output)
    {
        LoadHextechForgeCheckModels(output);
        var uiConfig = HextechCheckMethod("HextechUiPreferences", "CreateCurrentUiConfig").Invoke(null, null);
        HextechCheckType("HextechUiPreferences").GetField("_config", HextechMembers)!.SetValue(null, uiConfig);
        // Prevent this isolated screen constructor from rolling its optional
        // cosmetic mystery event or accessing that event's progress file.
        HextechCheckType("HextechSignalNoise").GetField("_slain", HextechMembers)!.SetValue(null, true);
        var assembly = _hextechForgeCheckAssembly!;
        var registered = assembly.GetTypes().Where(type => !type.IsAbstract &&
            (HextechCheckType("HextechForgeBase").IsAssignableFrom(type) || type.Name is "RandomForgeShopRelic" or "DiceManiacRune" or
                "HextechMayhemModifier" or "DoubleVisionRune" or "StrikeUpgradeRune" or "DefendUpgradeRune")).ToHashSet();
        foreach (var type in ((IEnumerable)HextechCheckType("HextechCustomModelRegistry")
            .GetProperty("CustomCardTypes", HextechMembers)!.GetValue(null)!).Cast<Type>()) ModelDb.Inject(type);
        foreach (var type in ((IEnumerable)HextechCheckType("HextechContentRegistry")
            .GetProperty("AllCustomRelicTypes", HextechMembers)!.GetValue(null)!).Cast<Type>().Where(type => !registered.Contains(type)))
        { ModelDb.Inject(type); ModHelper.AddModelToPool(typeof(SharedRelicPool), type); }
    }

    private static async Task RunHextechRerollChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        Action<object, string, object?> set)
    {
        int checks = 0; bool passed = false; string? failure = null;
        var cases = new List<object>();
        void Check(bool condition, string reason)
        { checks++; if (!condition) throw new InvalidOperationException("HextechReroll: " + reason); }
        var fixture = new Harmony("rt2.hextech.reroll.fixture");
        var originalModifiers = run.Modifiers;
        var originalRng = run.Rng.ToSerializable();
        var originalPlayerRng = player.PlayerRng.ToSerializable();
        var originalMode = InRunFeaturePreferences.GetOperationMode("hextech:selection-reroll");
        var preferences = new SearchWorkspace(OS.GetUserDataDir(), false);
        var screenType = HextechCheckType("HextechRuneSelectionScreen");
        var coordinator = HextechCheckType("HextechRuneSelectionCoordinator");
        var rarityType = HextechCheckType("HextechRarityTier");
        Control? screen = null;
        object? config = null;
        var modifier = ModelDb.GetById<ModifierModel>(ModelDb.GetId(HextechCheckType("HextechMayhemModifier"))).ToMutable();
        object? Invoke(string name, params object?[] args) => coordinator.GetMethods(HextechMembers)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(null, args);
        object Field(object owner, string name) => owner.GetType().GetField(name, HextechMembers)!.GetValue(owner)!;
        ModelKey Key(RelicModel model) => new(model.Id.Category, model.Id.Entry);
        Button Button(int slot, bool enemy = false) => ((IEnumerable)Field(screen!, enemy ? "_enemyHexRerollButtons" : "_rerollButtons")).Cast<Button>().ElementAt(slot);
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        object? ClosureField(bool enemy, string name)
        {
            if (screen == null || Field(screen, enemy ? "_enemyHexRerollFunc" : "_rerollFunc") is not Delegate callback) return null;
            var pending = new List<object> { callback.Target! };
            for (int index = 0; index < pending.Count; index++)
            {
                if (pending[index].GetType().GetField(name, HextechMembers) is { } wanted) return wanted.GetValue(pending[index]);
                foreach (var field in pending[index].GetType().GetFields(HextechMembers).Where(field => !field.IsStatic))
                    if (field.GetValue(pending[index]) is { } value && value.GetType().Assembly == _hextechForgeCheckAssembly &&
                        value.GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) &&
                        !pending.Any(item => ReferenceEquals(item, value))) pending.Add(value);
            }
            throw new MissingFieldException("actual native reroll closure", name);
        }
        object ScreenState() => new
        {
            options = screen == null ? [] : ((IEnumerable)Field(screen, "_relics")).Cast<RelicModel>().Select(Key).ToArray(),
            history = screen == null ? [] : ((IEnumerable)Field(screen, "_rerollHistory")).Cast<int>().ToArray(),
            playerCounts = screen == null ? [] : ((IEnumerable)Field(screen, "_playerRuneRerollCounts")).Cast<int>().ToArray(),
            enemyCounts = screen == null ? [] : ((IEnumerable)Field(screen, "_enemyHexRerollCounts")).Cast<int>().ToArray(),
            enemies = screen == null ? [] : ((IEnumerable)Field(screen, "_monsterHexKinds")).Cast<object?>().Select(value => value == null ? -1 : Convert.ToInt32(value)).ToArray(),
            seen = ClosureField(false, "seenOptionIds") is IEnumerable seen ? seen.Cast<ModelId>().Select(id => id.ToString()).OrderBy(id => id, StringComparer.Ordinal).ToArray() : [],
            enemySeen = ClosureField(true, "seenEnemyHexes") is IEnumerable enemySeen ? enemySeen.Cast<object>().Select(Convert.ToInt32).Order().ToArray() : [],
            weight = screen == null ? 0 : (int)HextechCheckMethod("HextechWeightedRuneOptions", "GetWeight", typeof(IReadOnlyList<RelicModel>))
                .Invoke(null, [Field(screen, "_relics")])!,
            golden = screen != null && Field(screen, "_goldenRerollSession") is { } session &&
                (bool)session.GetType().GetProperty("IsActive")!.GetValue(session)!,
            noise = screen == null || Field(screen, "_signalNoise") is not { } noise ? -1 : (int)noise.GetType().GetProperty("Slot")!.GetValue(noise)!
        };
        string Signature() => JsonSerializer.Serialize(new { run = run.Rng.ToSerializable(), personal = player.PlayerRng.ToSerializable(),
            player = player.ToSerializable(), modifiers = run.Modifiers.Select(m => m.ToSerializable()),
            screen = ScreenState() });
        void Close()
        {
            hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName)?.Free();
            if (screen != null && GodotObject.IsInstanceValid(screen)) screen.Free();
            screen = null;
            if (_hextechRerollOverlay != null) PredictorOperationCapture.Read<List<IOverlayScreen>>(_hextechRerollOverlay, "_overlays").Clear();
        }
        async Task NewScreen(bool multiplayer, bool golden = false, bool enemy = false, int limit = 4, bool forceUpgrade = false)
        {
            Close();
            object rarity = Enum.ToObject(rarityType, 0);
            config = HextechCheckMethod("HextechRuneConfiguration", "GetDefaultSnapshot").Invoke(null, null)!;
            config.GetType().GetProperty("GoldenRerollChancePercent")!.SetValue(config, golden ? 100 : 0);
            config.GetType().GetProperty("PlayerRuneRerollLimit")!.SetValue(config, limit);
            config.GetType().GetProperty("MonsterHexRerollLimit")!.SetValue(config, limit);
            HextechCheckMethod("HextechMayhemModifier", "SetRunConfigurationSnapshot", config.GetType(), typeof(string))
                .Invoke(modifier, [config, "isolated current-screen reroll check"]);
            HextechCheckMethod("HextechMayhemModifier", "SetRarityForAct", typeof(int), rarityType).Invoke(modifier, [run.CurrentActIndex, rarity]);
            var options = (IReadOnlyList<RelicModel>)HextechCheckType("HextechRunePoolBuilder").GetMethod("BuildSelectableRunesForRarity", HextechMembers)!
                .Invoke(null, [player, rarity, run, null, false])!;
            if (forceUpgrade)
            {
                var builder = HextechCheckType("HextechRunePoolBuilder");
                var pool = ((IEnumerable)builder.GetMethod("BuildSelectableRunePool", HextechMembers)!.Invoke(null, [player, rarity, run, null])!)
                    .Cast<RelicModel>().ToArray();
                var upgrade = pool.First(model => model.GetType().Name.EndsWith("UpgradeRune", StringComparison.Ordinal));
                var fixedOptions = new[] { upgrade }.Concat(pool.Where(model => !model.GetType().Name.EndsWith("UpgradeRune", StringComparison.Ordinal)).Take(2))
                    .Select(model => (RelicModel)builder.GetMethod("CreateSelectableRuneOption", HextechMembers)!.Invoke(null, [player, model])!).ToList();
                options = (IReadOnlyList<RelicModel>)Activator.CreateInstance(HextechCheckType("HextechWeightedRuneOptions"), fixedOptions, 150)!;
            }
            object? enemyOptions = null;
            if (enemy)
            {
                var enemyPool = ((IEnumerable)HextechCheckType("HextechMonsterHexRoller").GetMethods(HextechMembers)
                    .Single(method => method.Name == "GetConfiguredRarityPool" && method.GetParameters().Length == 2)
                    .Invoke(null, [rarity, new HashSet<string>()])!).Cast<object>().Take(2).ToArray();
                var initial = Array.CreateInstance(HextechCheckType("MonsterHexKind"), enemyPool.Length);
                for (int i = 0; i < enemyPool.Length; i++) initial.SetValue(enemyPool[i], i);
                enemyOptions = Invoke("CreateEnemyHexAdjustmentOptions", modifier, rarity, run, run.CurrentActIndex, initial, true, null, CancellationToken.None);
            }
            var offered = multiplayer ? options.Select(model => model.CanonicalInstance?.Id ?? model.Id).ToHashSet() : null;
            var task = (Task)Invoke("CreateLocalRuneSelectionScreenAsync", modifier, player, run.CurrentActIndex, 0,
                options, null, enemyOptions, offered, CancellationToken.None)!;
            await task;
            screen = (Control)task.GetType().GetProperty("Result")!.GetValue(task)!;
            await Frames(5);
        }
        async Task<HextechRuneRerollResult> Verify(string name, int slot, bool multiplayer, bool enemy = false)
        {
            var button = Button(slot, enemy);
            string before = Signature();
            var snapshot = HextechRuneRerollCapture.Capture(button, run);
            var result = await Task.Run(() => HextechRuneRerollPrediction.Predict(snapshot));
            Check(Signature() == before, name + " capture and background prediction leave actual RNG/player/screen unchanged");
            Check(snapshot.Enemy == enemy && (enemy || snapshot.Multiplayer == multiplayer), name + " distinguishes the actual native callback route");
            int cursor = run.Rng.Niche.ToSerializable().counter;
            screenType.GetMethod(enemy ? "OnEnemyHexRerollPressed" : "OnRerollPressed", HextechMembers)!.Invoke(screen, [slot]);
            var actual = enemy
                ? ((IEnumerable)Field(screen!, "_monsterHexKinds")).Cast<object?>().Select(value => value == null ? default :
                    Key(ModelDb.GetById<RelicModel>((ModelId)Invoke("GetMonsterHexIconRelicId", value)!))).ToArray()
                : ((IEnumerable)Field(screen!, "_relics")).Cast<RelicModel>().Select(Key).ToArray();
            Check(result.NextPanel.SequenceEqual(actual), name + " complete next panel matches installed native button effect");
            Check((enemy || multiplayer || snapshot.Noise || snapshot.Candidates.IsEmpty)
                ? run.Rng.Niche.ToSerializable().counter == cursor : run.Rng.Niche.ToSerializable().counter == cursor + 1,
                name + " native RNG consumption matches mode and real operation");
            cases.Add(new { name, slot, enemy, snapshot.Multiplayer, snapshot.Golden, snapshot.Noise, snapshot.Ordinal,
                expected = result.NextPanel.Select(key => key.Entry), actual = actual.Select(key => key.Entry), result.Outcome });
            await Frames();
            return result;
        }
        async Task VerifySequences(string name, bool multiplayer, bool golden = false, bool enemy = false,
            int limit = 4, bool forceUpgrade = false, int? noiseSlot = null, bool exhaustedNoise = false, int hoveredSlot = 0, bool singleActive = false)
        {
            var initialRng = run.Rng.ToSerializable();
            var initialPersonal = player.PlayerRng.ToSerializable();
            var seenProperty = modifier.GetType().GetProperty("SavedSeenPlayerRuneIdsJson", HextechMembers)!;
            string initialSeen = (string)seenProperty.GetValue(modifier)!;
            async Task Fresh()
            {
                Close(); run.Rng.LoadFromSerializable(initialRng); player.PlayerRng.LoadFromSerializable(initialPersonal);
                seenProperty.SetValue(modifier, initialSeen);
                await NewScreen(multiplayer, golden, enemy, limit, forceUpgrade);
                if (noiseSlot is { } noisy)
                {
                    set(screen!, "_signalNoise", Activator.CreateInstance(HextechCheckType("HextechSignalNoise+Noise"), noisy, false, 0)!);
                    if (exhaustedNoise) ((IList)Field(screen!, "_playerRuneRerollCounts"))[noisy] = limit;
                }
                if (singleActive)
                {
                    var counts = (IList)Field(screen!, enemy ? "_enemyHexRerollCounts" : "_playerRuneRerollCounts");
                    for (int slot = 0; slot < counts.Count; slot++) if (slot != hoveredSlot) counts[slot] = limit;
                }
                if (noiseSlot != null || singleActive)
                {
                    screenType.GetMethod("RebuildCards", HextechMembers)!.Invoke(screen, null);
                    await Frames();
                }
            }
            ModelKey[] Panel() => enemy
                ? ((IEnumerable)Field(screen!, "_monsterHexKinds")).Cast<object?>().Select(value => value == null ? default :
                    Key(ModelDb.GetById<RelicModel>((ModelId)Invoke("GetMonsterHexIconRelicId", value)!))).ToArray()
                : ((IEnumerable)Field(screen!, "_relics")).Cast<RelicModel>().Select(Key).ToArray();
            HashSet<ModelKey> Seen() => enemy
                ? ((IEnumerable)ClosureField(true, "seenEnemyHexes")!).Cast<object>().Select(value =>
                    Key(ModelDb.GetById<RelicModel>((ModelId)Invoke("GetMonsterHexIconRelicId", value)!))).ToHashSet()
                : ((IEnumerable)ClosureField(false, "seenOptionIds")!).Cast<ModelId>().Select(id => new ModelKey(id.Category, id.Entry)).ToHashSet();
            void CompareState(HextechRuneRerollState state, string stepName)
            {
                Check(state.Panel.Select(option => option.Key).SequenceEqual(Panel()), stepName + " detached complete panel matches native state");
                Check(state.Counts.SequenceEqual(((IEnumerable)Field(screen!, enemy ? "_enemyHexRerollCounts" : "_playerRuneRerollCounts")).Cast<int>()),
                    stepName + " all per-slot charges match native state");
                Check(state.Seen.SetEquals(Seen()), stepName + " shared seen history matches native closure");
                if (!enemy)
                {
                    var models = ((IEnumerable)Field(screen!, "_relics")).Cast<RelicModel>().ToArray();
                    var builder = HextechCheckType("HextechRunePoolBuilder");
                    Check(state.Panel.Zip(models).All(pair => pair.First.Rarity == Convert.ToInt32(builder.GetMethod("GetRarityForOptions", HextechMembers)!
                            .Invoke(null, [new[] { pair.Second }])) &&
                        pair.First.Upgrade == (bool)builder.GetMethod("IsUpgradeRune", HextechMembers)!.Invoke(null, [pair.Second])! &&
                        pair.First.Guaranteed == (bool)builder.GetMethod("IsGuaranteedFirstPickRune", HextechMembers)!.Invoke(null, [pair.Second])!),
                        stepName + " per-slot rarity, upgrade exclusion and guaranteed-rune metadata match native panel");
                    Check(state.Ordinal == ((ICollection)Field(screen!, "_rerollHistory")).Count, stepName + " shared successful-click ordinal matches native history");
                    Check(state.CharacterWeight == (int)HextechCheckMethod("HextechWeightedRuneOptions", "GetWeight", typeof(IReadOnlyList<RelicModel>))
                        .Invoke(null, [Field(screen!, "_relics")])!, stepName + " shared character weight matches native options");
                    bool actualGolden = Field(screen!, "_goldenRerollSession") is { } session &&
                        (bool)session.GetType().GetProperty("IsActive")!.GetValue(session)!;
                    Check(state.Golden == actualGolden, stepName + " golden activation and consumption match native session");
                    int? actualNoise = Field(screen!, "_signalNoise") is { } noise ? (int)noise.GetType().GetProperty("Slot")!.GetValue(noise)! : null;
                    Check(state.NoiseSlot == actualNoise, stepName + " noise restoration state matches native screen");
                }
                else Check(state.Panel.Select(option => option.NativeValue).SequenceEqual(((IEnumerable)Field(screen!, "_monsterHexKinds"))
                        .Cast<object?>().Select(value => value == null ? -1 : Convert.ToInt32(value))),
                    stepName + " enemy hash values match actual enum slots");
                if (state.Niche is { } niche)
                    Check(niche == CrystalSphereLiveCapture.RngState(PredictorStream.Niche, run.Rng.Niche),
                        stepName + " detached Niche state and cursor match native stream");
            }
            await Fresh();
            string before = Signature();
            var snapshot = HextechRuneRerollCapture.Capture(Button(hoveredSlot, enemy), run);
            Check(snapshot.FullState != null, name + " captures detached sequence state");
            var liveCounts = (IList)Field(screen!, enemy ? "_enemyHexRerollCounts" : "_playerRuneRerollCounts");
            int otherSlot = hoveredSlot == 0 ? 1 : 0, savedCount = (int)liveCounts[otherSlot]!;
            liveCounts[otherSlot] = savedCount == 0 ? 1 : savedCount - 1;
            var changedCount = HextechRuneRerollCapture.Capture(Button(hoveredSlot, enemy), run);
            Check(changedCount.Fingerprint != snapshot.Fingerprint && HextechRuneRerollPrediction.Predict(changedCount).NextPanel
                .SequenceEqual(HextechRuneRerollPrediction.Predict(snapshot).NextPanel),
                name + " a nonhover charge change invalidates routed predictions while immediate result stays the same");
            liveCounts[otherSlot] = savedCount;
            var sequence = await Task.Run(() => HextechRuneRerollSequence.Predict(snapshot));
            Check(before == Signature(), name + " sequence capture and enumeration preserve full live state");
            var orders = sequence.Groups.SelectMany(group => group.Orders).ToArray();
            Check(sequence.Groups.Length <= 8 && orders.All(order => order.Length is >= 1 and <= 5 && order[^1] == hoveredSlot),
                name + " default grouping respects complete row/depth bounds and hovered-last ownership");
            Check(orders.Select(order => string.Join(",", order)).Distinct().Count() == orders.Length,
                name + " each admitted route is retained exactly once");
            foreach (var group in sequence.Groups)
                Check(group.Orders.All(order => order.Length == group.Depth), name + " groups by click depth before outcome");
            // These fixtures have large nonempty native candidate pools. Their
            // click legality depends only on charge counts and noise restoration,
            // so enumerate legal orders independently of prediction or grouping.
            IEnumerable<string> LegalOrders(int depth)
            {
                var pending = new List<(int[] Counts, int? Noise, int[] Order)>
                    { (snapshot.FullState!.Counts.ToArray(), snapshot.FullState.NoiseSlot, []) };
                for (int click = 0; click < depth; click++)
                {
                    var next = new List<(int[], int?, int[])>();
                    foreach (var prefix in pending)
                    for (int slot = 0; slot < snapshot.FullState!.Panel.Length; slot++)
                    {
                        if (click == depth - 1 && slot != hoveredSlot || enemy && snapshot.FullState.Panel[slot].NativeValue < 0) continue;
                        bool restore = !enemy && prefix.Noise == slot;
                        if (!restore && limit != -1 && prefix.Counts[slot] >= limit) continue;
                        var counts = (int[])prefix.Counts.Clone();
                        if (!restore) counts[slot]++;
                        next.Add((counts, restore ? null : prefix.Noise, prefix.Order.Append(slot).ToArray()));
                    }
                    pending = next;
                }
                return pending.Select(prefix => string.Join(",", prefix.Order));
            }
            foreach (int depth in sequence.Groups.Select(group => group.Depth).Distinct())
                Check(orders.Where(order => order.Length == depth).Select(order => string.Join(",", order)).ToHashSet().SetEquals(LegalOrders(depth)),
                    name + " every admitted layer retains all legal orders without merging continuation states");
            if (limit == 1 && noiseSlot == null && !enemy)
            {
                var expected = new[] { "0", "1,0", "2,0", "1,2,0", "2,1,0" };
                Check(orders.Select(order => string.Join(",", order)).ToHashSet().SetEquals(expected),
                    name + " ordinary one-charge slots retain all five valid hovered-last orders");
            }
            if (limit == 1 && noiseSlot == null && enemy)
                Check(orders.Select(order => string.Join(",", order)).ToHashSet().SetEquals(["0", "1,0"]),
                    name + " enemy one-charge slots retain both valid hovered-last orders");
            if (limit == 4 && noiseSlot == null)
            {
                var rowBound = HextechRuneRerollSequence.Predict(snapshot, maxRows: 1);
                Check(rowBound.Groups.Length == 1 && rowBound.Groups[0].Depth == 1 && rowBound.Groups[0].Orders.Single().SequenceEqual([hoveredSlot]) && rowBound.Truncated,
                    name + " row budget stops before an entire later layer rather than admitting part of it");
                var depthBound = HextechRuneRerollSequence.Predict(snapshot, maxDepth: 2, maxRows: 100);
                Check(depthBound.Groups.SelectMany(group => group.Orders).Count() == (enemy ? 3 : 4) && depthBound.Truncated,
                    name + " complete second layer retains every cross-slot and repeated-slot order");
            }
            if (singleActive)
                Check(orders.Length == 5 && sequence.Groups.Select(group => group.Depth).SequenceEqual([1, 2, 3, 4, 5]) &&
                    orders.All(order => order.All(slot => slot == hoveredSlot)), name + " one legal repeat slot reaches every depth through the five-click ceiling");
            var routes = orders.Select(order => order.ToArray()).ToList();
            if (limit == 4 && noiseSlot == null)
                routes.AddRange(enemy ? new[] { new[] { 0, 1, 0 }, new[] { 1, 1, 0 } } :
                    new[] { new[] { 0, 1, 0 }, new[] { 2, 1, 0 }, new[] { 0, 0, 0 } });
            if (noiseSlot is { } noisySlot)
                routes.Add(exhaustedNoise ? [noisySlot, hoveredSlot] : [noisySlot, noisySlot, hoveredSlot]);
            foreach (var route in routes.DistinctBy(route => string.Join(",", route)))
            {
                await Fresh();
                var state = snapshot.FullState!;
                CompareState(state, name + " initial");
                HextechRuneRerollResult? final = null;
                foreach (int slot in route)
                {
                    string untouched = Signature();
                    var step = HextechRuneRerollPrediction.Step(snapshot, state, slot);
                    Check(Signature() == untouched, name + " detached Step leaves live state unchanged");
                    Check(step.Legal && !Button(slot, enemy).Disabled, name + " admitted step is legal in actual native screen");
                    screenType.GetMethod(enemy ? "OnEnemyHexRerollPressed" : "OnRerollPressed", HextechMembers)!.Invoke(screen, [slot]);
                    Check(step.Result.NextPanel.SequenceEqual(Panel()), name + " each full step panel matches actual native callback");
                    state = step.State; final = step.Result;
                    CompareState(state, name + " route=" + string.Join(",", route) + " slot=" + slot);
                }
                foreach (var group in sequence.Groups.Where(group => group.Orders.Any(order => order.SequenceEqual(route))))
                    Check(final != null && final.Outcome == group.Result.Outcome && final.Issue == group.Result.Issue &&
                        final.NextPanel[hoveredSlot] == group.Result.NextPanel[hoveredSlot],
                        name + " grouped display result preserves each native route behind it");
            }
            cases.Add(new { name, multiplayer, enemy, golden, limit, forceUpgrade, noiseSlot, exhaustedNoise,
                routes = orders.Select(order => order.ToArray()).ToArray(), singleActive, sequence.Truncated, sequence.Unknown });
        }
        try
        {
            foreach (string language in new[] { "eng", LocManager.Instance.Language }.Distinct())
            foreach (string table in new[] { "relics", "relic_collection" })
            {
                string resource = $"res://HextechRunes/localization/{language}/{table}.json";
                if (!Godot.FileAccess.FileExists(resource)) continue;
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(Godot.FileAccess.GetFileAsString(resource))!;
                var native = PredictorOperationCapture.Read<Dictionary<string, string>>(LocManager.Instance.GetTable(table), "_translations");
                foreach (var (key, value) in values) native[key] = value;
            }
            preferences.SetOperationPredictionMode("hextech:selection-reroll", OperationPredictionMode.Auto);
            foreach (var oldLayout in game.GetChildren().OfType<NEventLayout>()) oldLayout.Hide();
            _hextechRerollOverlay = new NOverlayStack { Name = "HextechNativeOverlayStack" };
            foreach (string lifecycleMethod in new[] { "_Ready", "_EnterTree", "_ExitTree" })
                fixture.Patch(AccessTools.Method(typeof(NOverlayStack), lifecycleMethod), prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(HextechRerollStackLifecycle)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(NOverlayStack), nameof(NOverlayStack.Instance)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(HextechRerollOverlayInstance)));
            fixture.Patch(AccessTools.Method(typeof(NOverlayStack), nameof(NOverlayStack.Push)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(HextechRerollFixturePush)));
            game.AddChild(_hextechRerollOverlay); _hextechRerollOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            game.MoveChild(hoverRoot, game.GetChildCount() - 1);
            run.AddModifierDebug(modifier); set(modifier, "_runState", run);
            ModOperationSurfaceObservers.Attach(root.GetTree(), runtime.Detection.NormalizedVersion);
            await NewScreen(false);
            for (int slot = 0; slot < 3; slot++) await Verify("single-current-slot-" + slot, slot, false);
            await NewScreen(true);
            for (int slot = 0; slot < 3; slot++) await Verify("multi-current-slot-" + slot, slot, true);
            await NewScreen(false, golden: true); await Verify("single-golden", 1, false);
            Check(!(bool)HextechCheckType("HextechGoldenRerollSession").GetProperty("IsActive")!.GetValue(Field(screen!, "_goldenRerollSession"))!,
                "actual successful golden refresh consumes session once");
            await NewScreen(true, golden: true); await Verify("multi-golden", 0, true);
            await NewScreen(false);
            var noise = Activator.CreateInstance(HextechCheckType("HextechSignalNoise+Noise"), 0, false, 0)!;
            set(screen!, "_signalNoise", noise);
            Check((await Verify("noise-restores-original", 0, false)).Outcome == HextechRuneRerollOutcome.NoiseRestored,
                "noise next click is restoration rather than a fabricated new draw");
            await NewScreen(false, enemy: true);
            await Verify("enemy-current-slot-0", 0, false, enemy: true);
            await Verify("enemy-current-slot-1", 1, false, enemy: true);
            await NewScreen(false);
            // The closure is the live native wrapper from CreateLocal...; fill
            // its seen set to exercise the real exhaustion reset independently.
            var callback = (Delegate)Field(screen!, "_rerollFunc");
            var closure = callback.Target!;
            var seenField = closure.GetType().GetField("seenOptionIds", HextechMembers);
            if (seenField != null)
            {
                var seen = (HashSet<ModelId>)seenField.GetValue(closure)!;
                foreach (var type in ((IEnumerable)HextechCheckType("HextechCatalog").GetMethod("GetAllConfigurableRuneTypes", HextechMembers)!.Invoke(null, null)!).Cast<Type>())
                    seen.Add(ModelDb.GetId(type));
                await Verify("seen-history-exhaustion", 2, false);
            }
            else throw new MissingFieldException("actual native closure", "seenOptionIds");
            await NewScreen(false);
            var transformType = HextechCheckType("HextechRuneGeneration+CandidateTransform");
            var parameters = transformType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            var transform = Expression.Lambda(transformType, Expression.Call(typeof(UiSurfaceChecks).GetMethod(nameof(HextechFixtureChaos), HextechMembers)!, parameters[5]), parameters).Compile();
            _hextechChaosInvocations = 0;
            HextechCheckMethod("HextechRuneGeneration", "RegisterChaosTransform", transformType).Invoke(null, [transform]);
            try
            {
                var unknown = HextechRuneRerollPrediction.Predict(HextechRuneRerollCapture.Capture(Button(0), run));
                Check(unknown.Outcome == HextechRuneRerollOutcome.Unknown && unknown.Issue == "HextechRuneRerollChaosUnknown" && unknown.NextPanel.IsEmpty,
                    "active external Chaos transform retains explicit unknown final panel");
                Check(_hextechChaosInvocations == 0, "capture and prediction never execute the external transform on a live player");
            }
            finally { HextechCheckMethod("HextechRuneGeneration", "UnregisterChaosTransform", transformType).Invoke(null, [transform]); }
            await VerifySequences("sequence-single-upgrade-cross-slot", false, forceUpgrade: true);
            await VerifySequences("sequence-multi-upgrade-cross-slot", true, forceUpgrade: true);
            await VerifySequences("sequence-single-golden-then-normal", false, golden: true);
            await VerifySequences("sequence-multi-golden-then-normal", true, golden: true);
            await VerifySequences("sequence-ordinary-one-charge", false, limit: 1);
            await VerifySequences("sequence-noise-then-reroll", false, noiseSlot: 0);
            await VerifySequences("sequence-exhausted-noise-then-other", false, limit: 1, noiseSlot: 0, exhaustedNoise: true, hoveredSlot: 1);
            await VerifySequences("sequence-enemy-cross-slot-counts", false, enemy: true);
            await VerifySequences("sequence-enemy-one-charge", false, enemy: true, limit: 1);
            await VerifySequences("sequence-five-step-single-active", false, limit: 5, singleActive: true);
            async Task Screenshot(string name, int limit)
            {
                await NewScreen(false, limit: limit);
                var hovered = Button(0);
                viewport.PushInput(new InputEventMouseMotion { Position = hovered.GetGlobalRect().GetCenter(), GlobalPosition = hovered.GetGlobalRect().GetCenter() }, true);
                hovered.GrabFocus();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                NeowOperationOverlay? view = null;
                while (watch.Elapsed < TimeSpan.FromSeconds(8))
                {
                    view = hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName)?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
                    if (view is { Visible: true } && PredictorOperationCapture.ReadNullable(view, "_preview") is OperationPreview { Faults.IsEmpty: true }) break;
                    await Frames(1);
                }
                Check(view is { Visible: true }, name + " native current-screen reroll focus publishes the shared preview");
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, name)) == Error.Ok, name + " native reroll screenshot saved");
            }
            await Screenshot("hextech-reroll-ui.png", 4);
            await Screenshot("hextech-reroll-ordinary-one-charge-ui.png", 1);
            passed = true; GD.Print($"HEXTECH_REROLL_PASS checks={checks};nativeOracle=true;captureReadOnly=true");
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            Close();
            _hextechRerollOverlay?.Free(); _hextechRerollOverlay = null;
            fixture.UnpatchAll(fixture.Id);
            set(run, nameof(RunState.Modifiers), originalModifiers);
            run.Rng.LoadFromSerializable(originalRng); player.PlayerRng.LoadFromSerializable(originalPlayerRng);
            preferences.SetOperationPredictionMode("hextech:selection-reroll", originalMode);
            File.WriteAllText(Path.Combine(output, "hextech-reroll-result.json"), JsonSerializer.Serialize(new { passed, checks, failure, cases,
                fullModInitializer = false, networkSessionExecuted = false, nativeMultiplayerCallback = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
