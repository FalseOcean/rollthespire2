using RolltheSpire2.Ui.Workspace;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.InRunConsole;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Persistence;

internal static partial class UiSurfaceChecks
{
    private static readonly List<string> _consoleSubmitted = [];
    private static bool ConsoleCaptureSubmission(string command, ref Task<ConsoleActionResult> __result)
    {
        _consoleSubmitted.Add(command);
        __result = Task.FromResult(new ConsoleActionResult(true, "Isolated mapping receipt"));
        return false;
    }
    // Existing native UI host and MockSave only. Full game startup and native
    // top-bar inventory subscriptions are outside this controlled fixture.
    private static async Task RunInRunConsoleChecks(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        void Set(object instance, string name, object? value)
        {
            if (instance.GetType().GetProperty(name, members) is { } property) property.SetValue(instance, value);
            else instance.GetType().GetField(name, members)!.SetValue(instance, value);
        }
        object Field(object instance, string name) => instance.GetType().GetField(name, members)!.GetValue(instance)!;
        Control Named(Node parent, string name) => Descendants(parent).OfType<Control>().Single(c => c.Name == name);
        InRunConsolePanel PanelFor(InRunConsoleOverlay screen) => Descendants(screen).OfType<InRunConsolePanel>().Single();
        int checks = 0;
        void Check(bool condition, string reason)
        { checks++; if (!condition) throw new InvalidOperationException("InRunConsole: " + reason); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        Directory.CreateDirectory(output);
        string receipt = Path.Combine(output, "in-run-console-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(NTopBar).Assembly);
        Check(!RunManager.Instance.IsInProgress, "isolated host has no pre-existing run");
        var oldGame = NGame.Instance;
        var oldLocal = LocalContext.NetId;
        var oldRuntime = ModRuntime.Snapshot;
        string consolePreferencesPath = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state", "console_preferences.json");
        byte[]? oldConsolePreferences = File.Exists(consolePreferencesPath) ? File.ReadAllBytes(consolePreferencesPath) : null;
        if (File.Exists(consolePreferencesPath)) File.Delete(consolePreferencesPath);
        var preferences = new SearchWorkspace(OS.GetUserDataDir(), false);
        string oldLanguage = preferences.Preferences.LanguageOverride;
        string oldGameLanguage = MegaCrit.Sts2.Core.Localization.LocManager.Instance.Language;
        var consoleInstance = typeof(NDevConsole).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldConsole = consoleInstance.GetValue(null);
        bool oldTest = TestMode.IsOn;
        var fixture = new Harmony("rt2.inrunconsole.ui.fixture");
        var detached = new List<Node>();
        NGlobalUi? global = null;
        try
        {
            string preferenceFixture = Path.Combine(output, "preference-roundtrip");
            var preferenceStore = new ConsolePreferencesStore(preferenceFixture);
            CheckConsoleCategoryPreferenceStore(output, Check);
            Check(preferenceStore.LoadCompactIcons(0), "missing console preference defaults to compact");
            preferenceStore.SaveCompactIcons(0, false);
            Check(!new ConsolePreferencesStore(preferenceFixture).LoadCompactIcons(0), "text preference survives store recreation");
            preferenceStore.SaveCompactIcons(0, true);
            Check(new ConsolePreferencesStore(preferenceFixture).LoadCompactIcons(0), "compact preference survives store recreation");
            string fixturePreferencePath = Path.Combine(preferenceFixture, "RolltheSpire2", "state", "console_preferences.json");
            foreach (string invalid in new[] { "{broken", "{\"version\":2,\"compactIcons\":false}" })
            {
                File.WriteAllText(fixturePreferencePath, invalid);
                var unreadableStore = new ConsolePreferencesStore(preferenceFixture);
                Check(unreadableStore.LoadCompactIcons(0), "unreadable preference uses compact default");
                unreadableStore.SaveCompactIcons(0, false);
                Check(File.ReadAllText(fixturePreferencePath) == invalid, "unreadable preference remains intact after a preference edit");
            }
            TestMode.IsOn = true;
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var run = RunState.CreateForTest([player], seed: "000000067C0S", ascensionLevel: 0);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), false);
            RunManager.Instance.GenerateRooms();
            LocalContext.NetId = player.NetId;
            typeof(ModRuntime).GetProperty(nameof(ModRuntime.Snapshot))!.SetValue(null, runtime);
            // Two genuine instances with equal model ID make accidental catalog
            // replacement or first-by-ID resolution observable.
            var duplicateCards = player.Deck.Cards.GroupBy(c => c.Id).First(g => g.Count() >= 2).Take(2).ToArray();
            Check(!ReferenceEquals(duplicateCards[0], duplicateCards[1]), "fixture has distinct equal-ID master cards");
            CardCmd.Upgrade(duplicateCards[0]);
            duplicateCards[0].EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(), 2);
            duplicateCards[1].EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(), 5);
            var potionModel = ModelDb.AllPotions.First();
            var duplicatePotions = new[] { potionModel.ToMutable(), potionModel.ToMutable() };
            player.AddPotionInternal(duplicatePotions[0], 0, true);
            player.AddPotionInternal(duplicatePotions[1], 1, true);
            var devConsole = new NDevConsole();
            detached.Add(devConsole);
            Set(devConsole, "_devConsole", new DevConsole(true));
            consoleInstance.SetValue(null, devConsole);
            _consoleSubmitted.Clear();
            fixture.Patch(AccessTools.Method(typeof(ConsoleActions), "SubmitAsync"),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(ConsoleCaptureSubmission)));
            InRunConsolePresencePatch.Install(fixture);
            // Stock Godot host has no FMOD runtime; leave native gold state and
            // hooks intact while bypassing only its unrelated sound playback.
            fixture.Patch(AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)]),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));

            var game = new NGame();
            var scene = new NSceneContainer();
            var runNode = new NRun();
            var rooms = new NSceneContainer();
            var hotkeys = new NHotkeyManager();
            var map = new NMapScreen();
            var capstone = new NCapstoneContainer();
            detached.AddRange([game, scene, runNode, rooms, hotkeys, map, capstone]);
            global = new NGlobalUi();
            var topbar = new NTopBar();
            var stack = new NOverlayStack();
            var backstop = new ColorRect { Name = "OverlayBackstop", Color = new(0, 0, 0, .5f) };
            stack.AddChild(backstop);
            Set(stack, "_backstop", backstop);
            Set(game, nameof(NGame.RootSceneContainer), scene);
            Set(game, nameof(NGame.HotkeyManager), hotkeys);
            Set(scene, nameof(NSceneContainer.CurrentScene), runNode);
            Set(runNode, nameof(NRun.GlobalUi), global);
            Set(runNode, "_roomContainer", rooms);
            Set(global, nameof(NGlobalUi.TopBar), topbar);
            Set(global, nameof(NGlobalUi.Overlays), stack);
            Set(global, nameof(NGlobalUi.MapScreen), map);
            Set(global, nameof(NGlobalUi.CapstoneContainer), capstone);
            typeof(NGame).GetProperty(nameof(NGame.Instance))!.SetValue(null, game);
            foreach (var (type, method) in new[] { (typeof(NGlobalUi), "_Ready"), (typeof(NTopBar), "_Ready"),
                (typeof(NTopBar), "_EnterTree"), (typeof(NTopBar), "_ExitTree"), (typeof(NOverlayStack), "_Ready") })
                fixture.Patch(AccessTools.Method(type, method),
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            root.GetWindow().Size = new(1600, 900);
            global.AddChild(topbar); global.AddChild(stack);
            root.AddChild(global);
            global.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            TestMode.IsOn = false;
            string Signature() => JsonSerializer.Serialize(new
            {
                run.CurrentActIndex, run.TotalFloor, run.CurrentMapCoord, run.MapPointHistory,
                rng = run.Rng.ToSerializable(), odds = run.Odds.ToSerializable(),
                extra = run.ExtraFields.ToSerializable(), players = run.Players.Select(p => p.ToSerializable())
            });
            string before = Signature();
            var deckBefore = player.Deck.Cards.ToArray();
            var relicsBefore = player.Relics.ToArray();
            var potionsBefore = player.PotionSlots.ToArray();
            InRunConsoleLauncher.EnsureAttached(topbar, run, runtime);
            InRunConsoleLauncher.EnsureAttached(topbar, run, runtime);
            Check(Descendants(global).OfType<InRunConsoleLauncher>().Count() == 1,
                "repeated native initialization attaches exactly one launcher");
            var launcher = Descendants(global).OfType<InRunConsoleLauncher>().Single();
            InRunConsoleLauncher.ApplyPreferences(root.GetTree(), true, 50, 100);
            launcher._Process(1);
            Check(launcher.Visible && !launcher.Disabled && launcher.Position.X == 0 && launcher.TooltipText == "",
                "enabled entry is available on the left edge without a tooltip");
            launcher.EmitSignal(Button.SignalName.Pressed);
            bool entryHiddenImmediately = !launcher.Visible;
            await Frames();
            Check(stack.ScreenCount == 1 && stack.Peek() is InRunConsoleOverlay,
                "entry opens the product console through the native overlay stack");
            var overlay = (InRunConsoleOverlay)stack.Peek()!;
            var panel = PanelFor(overlay);
            Check(entryHiddenImmediately, "opening the rail immediately hides its entry without a polling overlap");
            Check(((IDictionary)Field(hotkeys, "_blockingScreens")).Contains(overlay),
                "visible console registers native hotkey blocking");
            launcher.EmitSignal(Button.SignalName.Pressed);
            Check(stack.ScreenCount == 1 && ReferenceEquals(stack.Peek(), overlay), "repeated activation cannot duplicate an open console");
            Check(Signature() == before && player.Deck.Cards.SequenceEqual(deckBefore) &&
                player.Relics.SequenceEqual(relicsBefore) && player.PotionSlots.SequenceEqual(potionsBefore),
                "opening and catalog enumeration preserve serialized state, RNG and every inventory instance");
            Check(!panel.IsBusy, "opening does not submit a game mutation");
            Check(_consoleSubmitted.Count == 0, "opening does not dispatch native console commands");
            Check(!panel.ContentOpen && panel.Size.X < 250 && Named(panel, "ConsoleRail").Visible,
                "entry initially opens only the narrow command rail");
            var holdings = (CheckButton)Field(panel, "_holdings");
            var search = (LineEdit)Field(panel, "_search");
            var grid = (HFlowContainer)Field(panel, "_grid");
            var tabs = (Dictionary<int, Button>)Field(panel, "_tabs");
            foreach (var category in tabs.Values)
                Check(Descendants(category).OfType<TextureRect>().All(icon => category.GetGlobalRect().Grow(1).Encloses(icon.GetGlobalRect())),
                    "native category icon fits inside its rail button " + category.Name);
            tabs[0].EmitSignal(Button.SignalName.Pressed);
            await Frames();
            Check(panel.ContentOpen && Named(panel, "ConsoleContent").IsVisibleInTree(),
                "category selection opens content alongside the persistent rail");
            foreach (var control in new[] { panel.CloseButton, (Button)Named(panel, "ConsoleCollapse"),
                (Button)Named(panel, "ConsoleRefresh"), (Button)Field(panel, "_previous"), (Button)Field(panel, "_next") })
            {
                float textWidth = control.GetThemeFont("font").GetStringSize(control.Text, HorizontalAlignment.Left,
                    -1, control.GetThemeFontSize("font_size")).X;
                var style = control.GetThemeStylebox("normal");
                Check(control.Size.X + 2 >= textWidth + style.GetContentMargin(Side.Left) + style.GetContentMargin(Side.Right),
                    "close, collapse, refresh and page button text remains visible " + control.Name);
            }
            Check(((OptionButton)Field(panel, "_viewMode")).Selected == 1,
                "fresh category defaults to compact icons");
            // Instance detail assertions below exercise the optional text + image view.
            ((OptionButton)Field(panel, "_viewMode")).Select(0);
            ((OptionButton)Field(panel, "_viewMode")).EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            var attackFilter = (Button)Named(panel, "ConsoleFilterType_Attack");
            var ironcladFilter = (Button)Named(panel, "ConsoleFilterCharacter_IRONCLAD");
            Check(Descendants(attackFilter).OfType<TextureRect>().Any(icon => icon.Texture?.ResourcePath.StartsWith("res://", StringComparison.Ordinal) == true) &&
                Descendants(ironcladFilter).OfType<TextureRect>().Any(icon => icon.Texture != null),
                "card type and character filters use actual game texture resources");
            attackFilter.EmitSignal(Button.SignalName.Pressed);
            ironcladFilter.EmitSignal(Button.SignalName.Pressed);
            void Search(string text) { search.Text = text; search.EmitSignal(LineEdit.SignalName.TextChanged, text); }
            holdings.ButtonPressed = true;
            Search(duplicateCards[0].Id.ToString());
            Button[] CardTiles() => grid.GetChildren().OfType<Button>().ToArray();
            Check(CardTiles().Length >= 2, "equal-ID master cards remain separate holding rows");
            Check(CardTiles()[0].GetNode<Label>("ConsoleCardInstanceDetails").Text.Contains("×2", StringComparison.Ordinal) &&
                CardTiles()[1].GetNode<Label>("ConsoleCardInstanceDetails").Text.Contains("×5", StringComparison.Ordinal) &&
                CardTiles()[0].GetNode<Label>("ConsoleCardInstanceDetails").Text.Contains(duplicateCards[0].Enchantment!.Title.GetFormattedText(), StringComparison.Ordinal),
                "holding rows display their actual differing enchantment amounts and native titles");
            await CheckConsoleTooltip(root, panel, CardTiles()[0], duplicateCards[0].Title, null,
                Check, Frames, null, "upgraded-held-card");
            CardTiles()[1].EmitSignal(Button.SignalName.Pressed);
            var selection = (HashSet<AbstractModel>)Field(panel, "_selected");
            Check(selection.Count == 1 && ReferenceEquals(selection.Single(), duplicateCards[1]),
                "selecting second equal-ID row retains its exact live master instance");
            var viewMode = (OptionButton)Field(panel, "_viewMode");
            string selectedQuery = search.Text;
            string filtersBeforeMode = JsonSerializer.Serialize(Field(panel, "_cardFilters"));
            foreach (int mode in new[] { 1, 0 })
            {
                viewMode.Select(mode); viewMode.EmitSignal(OptionButton.SignalName.ItemSelected, (long)mode);
                Check(search.Text == selectedQuery && JsonSerializer.Serialize(Field(panel, "_cardFilters")) == filtersBeforeMode &&
                    selection.Count == 1 && ReferenceEquals(selection.Single(), duplicateCards[1]),
                    "display mode preserves search, card filters and exact selection");
            }
            ((Button)Field(panel, "_remove")).EmitSignal(Button.SignalName.Pressed);
            ConsoleActionRequest LastRequest()
            {
                string submitted = _consoleSubmitted.Last();
                Check(submitted.StartsWith("rt2_action ", StringComparison.Ordinal), "inventory actions use original console command transport");
                return JsonSerializer.Deserialize<ConsoleActionRequest>(Encoding.UTF8.GetString(
                    Convert.FromBase64String(submitted[11..])))!;
            }
            var remove = LastRequest();
            Check(remove.Operation == "card-remove" && remove.Pile == PileType.Deck &&
                remove.Indices.SequenceEqual(new[] { Array.IndexOf(deckBefore, duplicateCards[1]) }),
                "holding removal maps exact row to permanent-deck ordinal through serialized transport");
            var resolve = typeof(ConsoleActions).GetMethod("ResolveIndices", BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(CardModel));
            var resolved = (CardModel[])resolve.Invoke(null, new object[] { player.Deck.Cards, remove })!;
            Check(resolved.Length == 1 && ReferenceEquals(resolved[0], duplicateCards[1]),
                "execution resolver receives the selected duplicate instance after transport roundtrip");
            int submittedBefore = _consoleSubmitted.Count;
            var staleCard = ModelDb.GetById<CardModel>(duplicateCards[1].Id).ToMutable();
            var staleResult = await ConsoleActions.RemoveCardsAsync([staleCard], PileType.Deck);
            Check(!staleResult.Success && _consoleSubmitted.Count == submittedBefore,
                "equal-ID detached card cannot impersonate a currently held instance");
            var inactiveHand = await ConsoleActions.RemoveCardsAsync([duplicateCards[1]], PileType.Hand);
            Check(!inactiveHand.Success && _consoleSubmitted.Count == submittedBefore,
                "inactive hand request cannot silently remove from permanent deck");
            holdings.ButtonPressed = false;
            Search(duplicateCards[0].Id.ToString());
            CardTiles()[0].EmitSignal(Button.SignalName.Pressed);
            ((SpinBox)Field(panel, "_quantity")).Value = 3;
            ((CheckButton)Field(panel, "_upgraded")).ButtonPressed = true;
            ((Button)Field(panel, "_apply")).EmitSignal(Button.SignalName.Pressed);
            var addCards = LastRequest();
            Check(addCards.Operation == "card-add" && addCards.Pile == PileType.Deck && addCards.Quantity == 3 &&
                addCards.Upgraded && addCards.Models.SequenceEqual(new[] { duplicateCards[0].Id.ToString() }),
                "card catalog add carries explicit permanent target, quantity, upgrade choice and full model ID");

            tabs[2].EmitSignal(Button.SignalName.Pressed); Search(""); holdings.ButtonPressed = true;
            var potionSnapshot = (PotionModel?[])Field(panel, "_potionHoldings");
            Check(ReferenceEquals(potionSnapshot[0], duplicatePotions[0]) && ReferenceEquals(potionSnapshot[1], duplicatePotions[1]),
                "potion holdings preserve equal-ID live objects at their original slots");
            CardTiles()[1].EmitSignal(Button.SignalName.Pressed);
            ((Button)Field(panel, "_remove")).EmitSignal(Button.SignalName.Pressed);
            var removePotion = LastRequest();
            Check(removePotion.Operation == "potion-remove" && removePotion.Slot == 1,
                "second equal-ID potion dispatches its precise slot");
            submittedBefore = _consoleSubmitted.Count;
            var wrongPotion = await ConsoleActions.RemovePotionAsync(1, duplicatePotions[0]);
            Check(!wrongPotion.Success && _consoleSubmitted.Count == submittedBefore,
                "same-model potion from another slot cannot satisfy selected slot identity");
            Check(Signature() == before, "selection and intercepted action mapping never execute a live mutation");
            Check((await ConsoleActions.AddRelicsAsync([ModelDb.AllRelics.First()])).Success && LastRequest().Operation == "relic-add",
                "relic addition maps selected native model through original console transport");
            Check((await ConsoleActions.AddPotionsAsync([potionModel])).Success && LastRequest().Models.Single() == potionModel.Id.ToString() &&
                LastRequest().Operation == "potion-add", "potion addition maps full selected native model ID");
            foreach (var (action, expected) in new (Func<Task<ConsoleActionResult>>, string)[]
                { (ConsoleActions.ToggleTravelAsync, "travel"), (ConsoleActions.ToggleGodModeAsync, "godmode"), (ConsoleActions.WinAsync, "win") })
                Check((await action()).Success && _consoleSubmitted.Last() == expected, "native tool maps to original command " + expected);
            var encounter = ModelDb.AllEncounters.First();
            ((SpinBox)Named(panel, "ConsoleGoldAmount")).Value = 17;
            ((Button)Named(panel, "ConsoleGoldAdd")).EmitSignal(Button.SignalName.Pressed);
            var goldAdd = LastRequest();
            Check(goldAdd.Operation == "gold-adjust" && goldAdd.GoldDelta == 17,
                "gold addition preserves its positive amount through original custom dispatcher");
            ((SpinBox)Named(panel, "ConsoleGoldAmount")).Value = 9;
            ((Button)Named(panel, "ConsoleGoldRemove")).EmitSignal(Button.SignalName.Pressed);
            var goldRemove = LastRequest();
            Check(goldRemove.Operation == "gold-adjust" && goldRemove.GoldDelta == -9,
                "gold removal preserves its signed amount instead of using native no-op gain");
            Check((await ConsoleActions.AdjustGoldAsync(-99999)).Success, "large native gold loss is admitted explicitly");
            var goldClamp = LastRequest();
            var ordinaryEvent = ModelDb.AllEvents.First(e => e is not AncientEventModel &&
                !string.IsNullOrWhiteSpace(RolltheSpire2.Presentation.Ui1.Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey(
                    runtime.Profile.ProfileId, new(e.Id.Category, e.Id.Entry), 1)));
            Check((await ConsoleActions.EnterEventAsync(ordinaryEvent)).Success &&
                _consoleSubmitted.Last() == "event " + ordinaryEvent.Id.Entry,
                "event maps selected original identity through native console");
            Check((await ConsoleActions.FightAsync(encounter)).Success && _consoleSubmitted.Last() == "fight " + encounter.Id.Entry,
                "fight maps original encounter identity");
            var optionCases = ModelDb.AllAncients.SelectMany(a => a.AllPossibleOptions.Select(o => (Ancient: a, Option: o)))
                .Where(item => item.Option.TextKey.Contains(".pages.", StringComparison.Ordinal)).Take(1).ToArray();
            Check(optionCases.Length == 1, "native Ancient fixture includes a lowercase .pages. option key");
            foreach (var item in optionCases)
            {
                var entered = await ConsoleActions.EnterAncientAsync(item.Ancient, item.Option.TextKey);
                string[] nativeArgs = _consoleSubmitted.Last().Split(' ').Skip(1).ToArray();
                Check(entered.Success && nativeArgs.Length == 2 && nativeArgs[0] == item.Ancient.Id.Entry &&
                    new AncientConsoleCmd().GetArgumentCompletions(player, [nativeArgs[0], ""]).Candidates.Contains(nativeArgs[1]) &&
                    item.Ancient.AllPossibleOptions.First(o => o.TextKey.Contains(nativeArgs[1].ToUpperInvariant(), StringComparison.Ordinal)).TextKey == item.Option.TextKey,
                    "forced Ancient option token is a native completion and native first-match resolves the exact .pages. option");
            }
            foreach (var (vertical, scale) in new[] { (10d, 75d), (90d, 125d) })
            {
                InRunConsoleLauncher.ApplyPreferences(root.GetTree(), true, vertical, scale);
                await Frames();
                Check(overlay.GetGlobalRect().Encloses(panel.GetGlobalRect()), "position/scale keeps the console inside its viewport");
            }
            overlay.AfterOverlayHidden();
            Check(!((IDictionary)Field(hotkeys, "_blockingScreens")).Contains(overlay), "covering console releases native hotkeys");
            overlay.AfterOverlayShown();
            Check(((IDictionary)Field(hotkeys, "_blockingScreens")).Contains(overlay), "showing console restores native hotkeys once");
            void Escape(InRunConsoleOverlay screen)
            {
                screen._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                screen._Input(new InputEventKey { Keycode = Key.Escape, Pressed = false });
            }
            Escape(overlay); await Frames();
            Check(stack.ScreenCount == 1 && !panel.ContentOpen && Named(panel, "ConsoleRail").IsVisibleInTree(),
                "first Escape collapses content while retaining the live rail and overlay");
            tabs[0].EmitSignal(Button.SignalName.Pressed); await Frames();
            ((Button)Named(panel, "ConsoleClearFilters")).EmitSignal(Button.SignalName.Pressed);
            ((LineEdit)Field(panel, "_search")).Text = duplicateCards[0].Id.Entry;
            ((CheckButton)Field(panel, "_holdings")).ButtonPressed = true;
            ((OptionButton)Field(panel, "_pile")).Select(0);
            ((OptionButton)Field(panel, "_pile")).EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            ((SpinBox)Field(panel, "_quantity")).Value = 4;
            ((OptionButton)Field(panel, "_viewMode")).Select(0);
            ((OptionButton)Field(panel, "_viewMode")).EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            ((HFlowContainer)Field(panel, "_grid")).GetChildren().OfType<Button>().First().EmitSignal(Button.SignalName.Pressed);
            var rememberedCard = ((HashSet<AbstractModel>)Field(panel, "_selected")).Single();
            panel.CloseButton.EmitSignal(Button.SignalName.Pressed);
            await Frames();
            Check(stack.ScreenCount == 0 && !GodotObject.IsInstanceValid(overlay) &&
                ((IDictionary)Field(hotkeys, "_blockingScreens")).Count == 0, "close removes overlay, frees nodes and releases hotkeys");
            launcher._Process(1);
            launcher.EmitSignal(Button.SignalName.Pressed);
            await Frames();
            Check(stack.Peek() is InRunConsoleOverlay, "console can reopen after cleanup");
            var reopenedPanel = PanelFor((InRunConsoleOverlay)stack.Peek()!);
            Check(reopenedPanel.ContentOpen && (int)Field(reopenedPanel, "_tab") == 0,
                "closing and reopening restores the expanded category");
            Check(!new ConsolePreferencesStore(OS.GetUserDataDir()).LoadCompactIcons(0),
                "actual display control persists text preference to disk");
            var freshRunPanel = new InRunConsolePanel();
            freshRunPanel.Initialize(RunState.CreateForTest(seed: "CONSOLE_NEW_RUN"), runtime);
            root.AddChild(freshRunPanel);
            Check(!freshRunPanel.ContentOpen && freshRunPanel.SelectedCount == 0 &&
                ((OptionButton)Field(freshRunPanel, "_viewMode")).Selected == 0,
                "new run keeps display preference but does not inherit prior run browsing or inventory selection");
            freshRunPanel.Free();
            Check(((OptionButton)Field(reopenedPanel, "_viewMode")).Selected == 0 &&
                ((CheckButton)Field(reopenedPanel, "_holdings")).ButtonPressed &&
                ((LineEdit)Field(reopenedPanel, "_search")).Text == duplicateCards[0].Id.Entry &&
                ((SpinBox)Field(reopenedPanel, "_quantity")).Value == 4 &&
                ReferenceEquals(((HashSet<AbstractModel>)Field(reopenedPanel, "_selected")).Single(), rememberedCard),
                "reopen restores display preference, controls and exact inventory instance selection");
            reopenedPanel.OpenCategory(1);
            var relicDisplay = (OptionButton)Field(reopenedPanel, "_viewMode");
            relicDisplay.Select(0); relicDisplay.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            ((Button)Named(reopenedPanel, "ConsoleRarity_Ancient")).EmitSignal(Button.SignalName.Pressed);
            ((Button)Field(reopenedPanel, "_next")).EmitSignal(Button.SignalName.Pressed);
            await Frames();
            ((ScrollContainer)Field(reopenedPanel, "_scroll")).ScrollVertical = 100;
            await Frames();
            int rememberedScroll = ((ScrollContainer)Field(reopenedPanel, "_scroll")).ScrollVertical;
            reopenedPanel.CloseButton.EmitSignal(Button.SignalName.Pressed); await Frames();
            launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed); await Frames();
            reopenedPanel = PanelFor((InRunConsoleOverlay)stack.Peek()!);
            Check((int)Field(reopenedPanel, "_page") == 1 && (string)Field(reopenedPanel, "_selectedRarity") == "Ancient" &&
                ((ScrollContainer)Field(reopenedPanel, "_scroll")).ScrollVertical == rememberedScroll,
                $"reopen restores rarity, page and actual scroll position: page={Field(reopenedPanel, "_page")},rarity={Field(reopenedPanel, "_selectedRarity")},scroll={((ScrollContainer)Field(reopenedPanel, "_scroll")).ScrollVertical},expected={rememberedScroll}");
            Check(rememberedScroll > 0, "scroll race checks start with a nonzero remembered position");
            reopenedPanel.CloseButton.EmitSignal(Button.SignalName.Pressed); await Frames();
            launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed);
            reopenedPanel = PanelFor((InRunConsoleOverlay)stack.Peek()!);
            var restoredScroll = (ScrollContainer)Field(reopenedPanel, "_scroll");
            using (var wheel = new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true })
                restoredScroll.EmitSignal(Control.SignalName.GuiInput, wheel);
            restoredScroll.ScrollVertical = 0;
            await Frames();
            Check(restoredScroll.ScrollVertical == 0,
                "scroll input before layout restoration keeps the player's position instead of the old snapshot");
            restoredScroll.ScrollVertical = rememberedScroll;
            await Frames();
            reopenedPanel.CloseButton.EmitSignal(Button.SignalName.Pressed); await Frames();
            launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed);
            reopenedPanel = PanelFor((InRunConsoleOverlay)stack.Peek()!);
            ((Button)Named(reopenedPanel, "ConsoleClearFilters")).EmitSignal(Button.SignalName.Pressed);
            await Frames();
            Check((int)Field(reopenedPanel, "_page") == 0 && Field(reopenedPanel, "_selectedRarity") is null &&
                ((ScrollContainer)Field(reopenedPanel, "_scroll")).ScrollVertical == 0,
                "same-category filter changes before layout restoration keep their new page and top position");
            Escape((InRunConsoleOverlay)stack.Peek()!); await Frames();
            Check(stack.ScreenCount == 1, "restored content first collapses on Escape");
            Escape((InRunConsoleOverlay)stack.Peek()!); await Frames();
            Check(stack.ScreenCount == 0, "Escape from the rail closes the entire console overlay");
            launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed); await Frames();
            InRunConsoleLauncher.ApplyPreferences(root.GetTree(), false, 50, 100);
            await Frames();
            Check(!launcher.Visible && stack.ScreenCount == 0 && ((IDictionary)Field(hotkeys, "_blockingScreens")).Count == 0,
                "disabling the setting closes an open console and hides its entry");
            Check(Signature() == before, "all passive lifecycle and layout operations preserve run and RNG");
            await RunConsoleMapPresenceChecks(root, launcher, stack, map, Check, Frames);
            Check(Signature() == before, "map visibility and input routing preserve run and RNG");
            var screenshots = new List<string>();
            async Task SavePicture(string label)
            {
                await Frames(3); RenderingServer.ForceDraw();
                using var picture = root.GetViewport().GetTexture().GetImage();
                string filename = "in-run-console-" + label + ".png";
                Check(picture != null && !picture.IsEmpty() && picture.SavePng(Path.Combine(output, filename)) == Error.Ok,
                    "rendered screenshot saved " + label);
                screenshots.Add(filename);
            }
            async Task CaptureConsole(string language, Vector2I window, int tab, bool showHoldings, bool hand, string label)
            {
                preferences.SetLanguageOverride(language);
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(language == "zh" ? "zhs" : "eng");
                root.GetWindow().Size = window;
                InRunConsoleLauncher.ApplyPreferences(root.GetTree(), true, 50, 100);
                launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed);
                await Frames();
                Check(stack.Peek() is InRunConsoleOverlay, "screenshot scenario opens through native stack " + label);
                var screen = (InRunConsoleOverlay)stack.Peek()!;
                var contents = PanelFor(screen);
                // Each screenshot explicitly starts its scenario; normal reopen now restores UI.
                contents.CollapseContent();
                if (label == "zh-1920-relics")
                {
                    await SavePicture("zh-1920-rail");
                    var goldAmount = (SpinBox)Named(contents, "ConsoleGoldAmount"); goldAmount.Value = 99999;
                    await Frames();
                    var railRect = Named(contents, "ConsoleRail").GetGlobalRect().Grow(1);
                    Check(railRect.Encloses(Named(contents, "ConsoleGoldAdd").GetGlobalRect()) &&
                        railRect.Encloses(Named(contents, "ConsoleGoldRemove").GetGlobalRect()),
                        "maximum five-digit gold amount keeps both action buttons inside the narrow rail");
                    await SavePicture("zh-1920-rail-max-gold"); goldAmount.Value = 100;
                }
                var display = (OptionButton)Field(contents, "_viewMode");
                contents.OpenCategory(tab == 0 ? 1 : 0);
                contents.OpenCategory(tab);
                display.Select(1); display.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                Check(display.Selected == 1, "scenario explicitly starts in compact mode " + label);
                if (tab is 1 or 2)
                {
                    await Frames();
                    var rarityRail = (VBoxContainer)Named(contents, "ConsoleRarityFilter");
                    var choices = rarityRail.GetChildren().OfType<Button>().ToArray();
                    var models = tab == 1 ? ModelDb.AllRelics.Cast<AbstractModel>().ToArray() : ModelDb.AllPotions.Cast<AbstractModel>().ToArray();
                    string Rarity(AbstractModel model) => model is RelicModel relic ? relic.Rarity.ToString() : ((PotionModel)model).Rarity.ToString();
                    string[] encyclopediaOrder = tab == 1
                        ? ["Starter", "Common", "Uncommon", "Rare", "Shop", "Ancient", "Event", "None"]
                        : ["Common", "Uncommon", "Rare", "Event", "Token", "None"];
                    var rarities = encyclopediaOrder.Where(rarity => models.Any(model => Rarity(model) == rarity)).ToArray();
                    Check(choices.Select(choice => choice.Name.ToString()).SequenceEqual(new[] { "ConsoleRarity_All" }.Concat(rarities.Select(r => "ConsoleRarity_" + r))) &&
                        choices.All(choice => choice.Text.Length > 0 && choice.IsVisibleInTree() && !Descendants(choice).OfType<TextureRect>().Any()),
                        "all native rarity choices are directly visible text buttons " + label);
                    Check(choices[0].ButtonPressed && choices.Count(choice => choice.ButtonPressed) == 1,
                        "All is the sole highlighted default rarity " + label);
                    var rarityRects = choices.Select(choice => choice.GetRect()).ToArray();
                    foreach (string rarity in rarities)
                    {
                        var choice = (Button)Named(contents, "ConsoleRarity_" + rarity);
                        root.GetViewport().PushInput(new InputEventMouseMotion { Position = choice.GetGlobalRect().GetCenter() }, true);
                        choice.EmitSignal(Button.SignalName.Pressed);
                        await Frames(2);
                        Check(choices.Select(item => item.GetRect()).SequenceEqual(rarityRects) &&
                            new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" }
                                .Select(state => choice.GetThemeStylebox(state).GetContentMargin(Side.Top) +
                                    choice.GetThemeStylebox(state).GetContentMargin(Side.Bottom)).Distinct().Count() == 1,
                            "rarity selection and hover preserve every row height and position " + label + " " + rarity);
                        Check(choice.ButtonPressed && choices.Count(item => item.ButtonPressed) == 1 &&
                            ((Label)Field(contents, "_paging")).Text.EndsWith("· " + models.Count(model => Rarity(model) == rarity), StringComparison.Ordinal),
                            "direct rarity selection highlights and filters actual native catalog " + label + " " + rarity);
                    }
                    var selectedRarity = rarities.First();
                    ((Button)Named(contents, "ConsoleRarity_" + selectedRarity)).EmitSignal(Button.SignalName.Pressed);
                    ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().First().EmitSignal(Button.SignalName.Pressed);
                    var selectedModel = ((HashSet<AbstractModel>)Field(contents, "_selected")).Single();
                    foreach (int mode in new[] { 0, 1 })
                    {
                        display.Select(mode); display.EmitSignal(OptionButton.SignalName.ItemSelected, (long)mode);
                        Check((string)Field(contents, "_selectedRarity") == selectedRarity &&
                            ReferenceEquals(((HashSet<AbstractModel>)Field(contents, "_selected")).Single(), selectedModel),
                            "rarity and actual selection survive both display modes " + label);
                    }
                    await SavePicture(label + "-rarity-compact");
                    ((Button)Named(contents, "ConsoleRarity_All")).EmitSignal(Button.SignalName.Pressed);
                    Check(choices[0].ButtonPressed && ((Label)Field(contents, "_paging")).Text.EndsWith("· " + models.Length, StringComparison.Ordinal),
                        "All restores the full native catalog " + label);
                    ((Button)Named(contents, "ConsoleRarity_" + selectedRarity)).EmitSignal(Button.SignalName.Pressed);
                    ((Button)Named(contents, "ConsoleClearFilters")).EmitSignal(Button.SignalName.Pressed);
                    Check(choices[0].ButtonPressed && choices.Count(choice => choice.ButtonPressed) == 1 &&
                        ((Label)Field(contents, "_paging")).Text.EndsWith("· " + models.Length, StringComparison.Ordinal),
                        "clear filters restores All and all native results " + label);
                    ((Button)Named(contents, "ConsoleClearSelection")).EmitSignal(Button.SignalName.Pressed);
                    if (tab is 1 or 2)
                    {
                        CheckConsoleCatalogOrder(contents, Check);
                        var expected = ((IEnumerable<AbstractModel>)contents.GetType().GetMethod("Candidates", members)!.Invoke(contents, null)!)
                            .Select(model => (string)contents.GetType().GetMethod("NameOf", members)!.Invoke(contents, [model])!).ToArray();
                        foreach (int mode in new[] { 0, 1 })
                        {
                            display.Select(mode); display.EmitSignal(OptionButton.SignalName.ItemSelected, (long)mode);
                            var actual = new List<string>();
                            while (true)
                            {
                                actual.AddRange(((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().Select(tile => tile.AccessibilityName));
                                var next = (Button)Field(contents, "_next");
                                if (next.Disabled) break;
                                next.EmitSignal(Button.SignalName.Pressed);
                            }
                            Check(actual.SequenceEqual(expected), "All item pages follow native encyclopedia order " + label);
                            ((Button)Named(contents, "ConsoleClearFilters")).EmitSignal(Button.SignalName.Pressed);
                        }
                        await SavePicture(label + "-all-rarity-order");
                    }
                }
                // Keep prior text-layout screenshot and shared event crop checks explicit.
                display.Select(0); display.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                if (hand)
                {
                    var target = (OptionButton)Field(contents, "_pile");
                    target.Select(1); target.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                }
                ((CheckButton)Field(contents, "_holdings")).ButtonPressed = showHoldings;
                if (tab == 4)
                {
                    var actButtons = ((VBoxContainer)Named(contents, "ConsoleActChoices")).GetChildren().OfType<Button>().ToArray();
                    var roomButtons = ((VBoxContainer)Named(contents, "ConsoleFightTypeChoices")).GetChildren().OfType<Button>().ToArray();
                    var roomTypes = (List<string?>)Field(contents, "_roomTypes");
                    Check(!((OptionButton)Field(contents, "_actFilter")).IsVisibleInTree() &&
                        !((OptionButton)Field(contents, "_roomFilter")).IsVisibleInTree() &&
                        actButtons.Concat(roomButtons).All(button => button.IsVisibleInTree()),
                        "fight act and room-type choices are both directly displayed " + label);
                    var acts = ModelDb.Acts.ToArray();
                    for (int actIndex = 0; actIndex < actButtons.Length; actIndex++)
                    {
                        actButtons[actIndex].EmitSignal(Button.SignalName.Pressed);
                        for (int roomIndex = 0; roomIndex < roomButtons.Length; roomIndex++)
                        {
                            roomButtons[roomIndex].EmitSignal(Button.SignalName.Pressed);
                            int expectedCount = ModelDb.AllEncounters.Count(encounter =>
                                (roomTypes[roomIndex] is not { } room || encounter.RoomType.ToString() == room) &&
                                (actIndex == 0 || (actIndex == actButtons.Length - 1
                                    ? !acts.Any(act => act.AllEncounters.Any(candidate => candidate.Id == encounter.Id))
                                    : acts[actIndex - 1].AllEncounters.Any(candidate => candidate.Id == encounter.Id))));
                            Check(((Label)Field(contents, "_paging")).Text.EndsWith("· " + expectedCount, StringComparison.Ordinal),
                                "combined direct fight filters retain the matching native encounters " + label + " " + actIndex + "/" + roomIndex);
                        }
                    }
                    ((Button)Named(contents, "ConsoleClearFilters")).EmitSignal(Button.SignalName.Pressed);
                    var tile = ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().First();
                    await CheckConsoleTooltip(root, contents, tile, tile.AccessibilityName, "", Check, Frames, SavePicture, label);
                }
                if (tab == 5)
                {
                    Check(((Dictionary<int, Button>)Field(contents, "_tabs")).Count == 6,
                        "Events remains a sixth peer console tab " + label);
                    var candidates = (IEnumerable<AbstractModel>)typeof(InRunConsolePanel)
                        .GetMethod("Candidates", members)!.Invoke(contents, null)!;
                    Check(candidates.Any() && candidates.All(model => model is EventModel and not AncientEventModel),
                        "Events catalog contains ordinary native events and excludes Ancients " + label);
                    var actChoice = (OptionButton)Field(contents, "_actFilter");
                    var actButtons = ((VBoxContainer)Named(contents, "ConsoleActChoices")).GetChildren().OfType<Button>().ToArray();
                    Check(!actChoice.IsVisibleInTree() && actButtons.Length == actChoice.ItemCount && actButtons.All(button => button.IsVisibleInTree()),
                        "event act-pool choices are directly displayed instead of a dropdown " + label);
                    for (int index = 0; index < actButtons.Length; index++)
                    {
                        actButtons[index].EmitSignal(Button.SignalName.Pressed);
                        int expectedCount = candidates.Count(model => index == 0 ||
                            (index == actButtons.Length - 1
                                ? !ModelDb.Acts.Any(act => act.AllEvents.Any(candidate => candidate.Id == model.Id))
                                : ModelDb.Acts.ElementAt(index - 1).AllEvents.Any(candidate => candidate.Id == model.Id)));
                        Check(actChoice.Selected == index && ((Label)Field(contents, "_paging")).Text.EndsWith("· " + expectedCount, StringComparison.Ordinal),
                            "direct event act choice filters the original native pool " + label + " " + index);
                    }
                    ((Button)Named(contents, "ConsoleClearFilters")).EmitSignal(Button.SignalName.Pressed);
                    var eventSearch = (LineEdit)Field(contents, "_search");
                    eventSearch.Text = ordinaryEvent.Id.ToString();
                    eventSearch.EmitSignal(LineEdit.SignalName.TextChanged, eventSearch.Text);
                    var eventTiles = ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().ToArray();
                    Check(eventTiles.Length == 1, "Events native ID search isolates its catalog row " + label);
                    string eventTitle = eventTiles[0].AccessibilityName;
                    eventSearch.Text = eventTitle;
                    eventSearch.EmitSignal(LineEdit.SignalName.TextChanged, eventTitle);
                    eventTiles = ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().ToArray();
                    Check(eventTiles.Length >= 1, "Events localized name search retains the catalog row " + label);
                    await CheckConsoleTooltip(root, contents, eventTiles[0], eventTitle, "", Check, Frames, SavePicture, label);
                    eventTiles[0].EmitSignal(Button.SignalName.Pressed);
                    Check(ReferenceEquals(((HashSet<AbstractModel>)Field(contents, "_selected")).Single(), ordinaryEvent),
                        "Events selection retains the actual native event model " + label);
                    eventSearch.Text = "";
                    eventSearch.EmitSignal(LineEdit.SignalName.TextChanged, "");
                }
                if (tab == 3)
                    await CheckConsoleAncientIcons(contents, Check, Frames, SavePicture, label);
                if (hand)
                {
                    var handTiles = ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().ToArray();
                    Check(handTiles.Length == player.PlayerCombatState!.Hand.Cards.Count, "hand holdings list exactly the live hand " + label);
                    handTiles[1].EmitSignal(Button.SignalName.Pressed);
                    Check(ReferenceEquals(((HashSet<AbstractModel>)Field(contents, "_selected")).Single(), player.PlayerCombatState.Hand.Cards[1]),
                        "hand holding selection retains its actual combat instance " + label);
                }
                if (label == "zh-1920-deck")
                {
                    ((CheckButton)Field(contents, "_holdings")).ButtonPressed = false;
                    ((Button)Named(contents, "ConsoleFilterCharacter_IRONCLAD")).EmitSignal(Button.SignalName.Pressed);
                    ((Button)Named(contents, "ConsoleFilterRarity_Common")).EmitSignal(Button.SignalName.Pressed);
                    ((Button)Named(contents, "ConsoleFilterType_Attack")).EmitSignal(Button.SignalName.Pressed);
                    Check(((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().Any(),
                        "combined character, rarity and type filters retain matching native cards");
                    await SavePicture("zh-1920-card-filters");
                    var selectedCard = ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().First();
                    selectedCard.EmitSignal(Button.SignalName.Pressed);
                    var mode = (OptionButton)Field(contents, "_viewMode");
                    string filterSnapshot = JsonSerializer.Serialize(Field(contents, "_cardFilters"));
                    var cardSelection = ((HashSet<AbstractModel>)Field(contents, "_selected")).Single();
                    mode.Select(1); mode.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                    Check(JsonSerializer.Serialize(Field(contents, "_cardFilters")) == filterSnapshot &&
                        ReferenceEquals(((HashSet<AbstractModel>)Field(contents, "_selected")).Single(), cardSelection),
                        "card compact mode retains combined filters and native selection");
                    await SavePicture("zh-1920-card-filters-compact");
                }
                await Frames(5);
                Check(screen.GetGlobalRect().Encloses(contents.GetGlobalRect()), "rendered panel remains inside viewport " + label);
                var filterRect = Named(contents, "ConsoleFilters").GetGlobalRect();
                var resultsRect = Named(contents, "ConsoleResults").GetGlobalRect();
                Check(filterRect.End.X <= resultsRect.Position.X + 1 && filterRect.Size.X > 100 && resultsRect.Size.X > filterRect.Size.X,
                    "filters stay in the left column and results occupy the wider right column " + label);
                foreach (var tile in ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>())
                    foreach (var title in tile.GetChildren().OfType<Label>())
                    {
                        if (!tile.GetGlobalRect().Grow(1).Encloses(title.GetGlobalRect()))
                        {
                            RenderingServer.ForceDraw();
                            using var diagnostic = root.GetViewport().GetTexture().GetImage();
                            diagnostic.SavePng(Path.Combine(output, "tile-label-failure.png"));
                        }
                        Check(tile.GetGlobalRect().Grow(1).Encloses(title.GetGlobalRect()),
                            "catalog title and instance details fit their tile " + label + " " + tile.AccessibilityName +
                            ";tile=" + tile.GetGlobalRect() + ";label=" + title.GetGlobalRect() +
                            ";localSize=" + title.Size + ";minimum=" + title.GetCombinedMinimumSize());
                    }
                foreach (Control control in Descendants(contents).OfType<Control>().Where(c => c.IsVisibleInTree() && c is BaseButton or LineEdit or SpinBox))
                {
                    bool catalog = false;
                    for (Node? ancestor = control; ancestor != null && ancestor != contents; ancestor = ancestor.GetParent())
                        if (ancestor is ScrollContainer) { catalog = true; break; }
                    if (!catalog)
                        Check(contents.GetGlobalRect().Grow(1).Encloses(control.GetGlobalRect()),
                            "console action remains within the panel " + label + " " + control.Name);
                }
                if (tab is 0 or 5 && display.Selected == 0)
                    await CheckConsoleTileLayout(contents, Check, Frames, SavePicture, label);
                await SavePicture(label);
                if (language == "zh" && tab is 1 or 2)
                {
                    var hoverTile = ((HFlowContainer)Field(contents, "_grid")).GetChildren().OfType<Button>().First();
                    if (tab == 1)
                    {
                        var model = (RelicModel)((IEnumerable<AbstractModel>)contents.GetType().GetMethod("Candidates", members)!.Invoke(contents, null)!).First();
                        var tip = new RolltheSpire2.Infrastructure.ContentNames.RuntimeRelicTooltipResolver()
                            .Resolve(new(model.Id.Category, model.Id.Entry), hoverTile.AccessibilityName);
                        await CheckConsoleTooltip(root, contents, hoverTile, tip.Title, tip.Description, Check, Frames, SavePicture, label);
                    }
                    else
                    {
                        var model = (PotionModel)((IEnumerable<AbstractModel>)contents.GetType().GetMethod("Candidates", members)!.Invoke(contents, null)!).First();
                        var tip = new RolltheSpire2.Infrastructure.ContentNames.RuntimePotionTooltipResolver()
                            .Resolve(new(model.Id.Category, model.Id.Entry), hoverTile.AccessibilityName);
                        await CheckConsoleTooltip(root, contents, hoverTile, tip.Title, tip.Description, Check, Frames, SavePicture, label);
                    }
                }
                if (label == "zh-1920-relics")
                {
                    var relicGrid = (HFlowContainer)Field(contents, "_grid");
                    relicGrid.GetChildren().OfType<Button>().First().EmitSignal(Button.SignalName.Pressed);
                    var kept = ((HashSet<AbstractModel>)Field(contents, "_selected")).Single();
                    var mode = (OptionButton)Field(contents, "_viewMode");
                    mode.Select(1); mode.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                    Check(ReferenceEquals(((HashSet<AbstractModel>)Field(contents, "_selected")).Single(), kept),
                        "relic compact mode preserves exact selection");
                    await SavePicture("zh-1920-relics-compact");
                    contents.CollapseContent(); await Frames();
                    Check(!contents.ContentOpen && Named(contents, "ConsoleRail").IsVisibleInTree(),
                        "explicit drawer collapse retains rail");
                    contents.OpenCategory(1); await Frames();
                    Check(contents.ContentOpen && mode.Selected == 1 &&
                        ReferenceEquals(((HashSet<AbstractModel>)Field(contents, "_selected")).Single(), kept),
                        "reopening the same category retains display mode and selection");
                }
                if (tab == 5)
                {
                    var eventArt = Descendants((HFlowContainer)Field(contents, "_grid")).OfType<EventThumbnailView>().First();
                    Check(Descendants(eventArt).OfType<TextureRect>().Any(image => image.Texture is AtlasTexture { Atlas: not null }),
                        "ordinary event result uses the shared picker crop on the native resource " + label);
                }
                if (tab == 5)
                {
                    ((Button)Field(contents, "_apply")).EmitSignal(Button.SignalName.Pressed);
                    await Frames();
                    Check(_consoleSubmitted.Last() == "event " + ordinaryEvent.Id.Entry && stack.ScreenCount == 0,
                        "Events explicit apply submits selected original ID then closes the transition overlay " + label);
                    Check(Signature() == before, "Events browsing, search and intercepted apply preserve run state and RNG " + label);
                    return;
                }
                if (label == "zh-1280-relics")
                {
                    var status = (Label)Field(contents, "_status");
                    status.Text = string.Join("\n", Enumerable.Repeat("Long native error detail / 原始错误说明，保留原因和已完成数量。", 25));
                    await Frames();
                    var statusScroll = (ScrollContainer)status.GetParent();
                    Check(statusScroll.Size.Y <= 75 && statusScroll.GetVScrollBar().Visible &&
                        screen.GetGlobalRect().Encloses(contents.GetGlobalRect()),
                        "long failure text scrolls inside a bounded status area at 1280 without enlarging the console");
                    RenderingServer.ForceDraw();
                    using var longError = root.GetViewport().GetTexture().GetImage();
                    Check(longError.SavePng(Path.Combine(output, "in-run-console-zh-1280-long-error.png")) == Error.Ok,
                        "long error layout screenshot saved");
                    screenshots.Add("in-run-console-zh-1280-long-error.png");
                }
                string? rememberedAncientKey = tab == 3 ? (string?)Field(contents, "_selectedAncientOptionKey") : null;
                contents.CloseButton.EmitSignal(Button.SignalName.Pressed);
                await Frames();
                if (tab == 3)
                {
                    launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed); await Frames();
                    var restored = PanelFor((InRunConsoleOverlay)stack.Peek()!);
                    Check(rememberedAncientKey is not null && restored.ContentOpen &&
                        (string?)Field(restored, "_selectedAncientOptionKey") == rememberedAncientKey,
                        "closing and reopening restores Ancient identity and exact relic option " + label);
                    restored.CloseButton.EmitSignal(Button.SignalName.Pressed); await Frames();
                }
            }
            // Open through the real launcher under opposing language settings;
            // filters retain their native values while their RT2 labels translate.
            foreach (string language in new[] { "en", "zh" })
            {
                preferences.SetLanguageOverride(language);
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(language == "en" ? "zhs" : "eng");
                // The preceding map-presence fixture disables the entry on cleanup.
                InRunConsoleLauncher.ApplyPreferences(root.GetTree(), true, 50, 100);
                launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed); await Frames();
                Check(stack.Peek() is InRunConsoleOverlay,
                    "opposing-language scenario opens through the native launcher and stack " + language);
                var localized = PanelFor((InRunConsoleOverlay)stack.Peek()!);
                localized.CollapseContent();
                localized.OpenCategory(1); await Frames();
                var common = (Button)Named(localized, "ConsoleRarity_Common");
                Check(common.Text == (language == "en" ? "Common" : "普通"),
                    "actual console rarity filters follow RT2 override with opposite native language " + language);
                common.EmitSignal(Button.SignalName.Pressed); await Frames();
                Check((string?)Field(localized, "_selectedRarity") == "Common",
                    "translated rarity choice retains its semantic filter identity " + language);
                localized.OpenCategory(3); await Frames();
                Check(Descendants(localized).OfType<Label>().Any(label => label.Text ==
                        (language == "en" ? "Force one option (optional)" : "指定一个选项（可选）")) &&
                    !Descendants(localized).OfType<Label>().Any(label => label.Text.Contains("Missing translation", StringComparison.Ordinal)),
                    "actual Ancient controls use resource labels under opposing languages " + language);
                localized.CloseButton.EmitSignal(Button.SignalName.Pressed); await Frames();
                Check(Signature() == before, "localized browsing and semantic filtering preserve run state and RNG " + language);
            }
            foreach (string language in new[] { "en", "zh" })
            {
                await CaptureConsole(language, new(1920, 1080), 0, false, false, language + "-1920-card-catalog");
                await CaptureConsole(language, new(1280, 720), 0, false, false, language + "-1280-card-catalog");
                await CaptureConsole(language, new(1920, 1080), 0, true, false, language + "-1920-deck");
                if (language == "zh") await CaptureConsole(language, new(1920, 1080), 1, false, false, "zh-1920-relics");
                await CaptureConsole(language, new(1280, 720), 1, false, false, language + "-1280-relics");
                await CaptureConsole(language, new(1280, 720), 2, false, false, language + "-1280-potions");
                await CaptureConsole(language, new(1920, 1080), 3, false, false, language + "-1920-ancient");
                await CaptureConsole(language, new(1280, 720), 3, false, false, language + "-1280-ancient");
                await CaptureConsole(language, new(1280, 720), 5, false, false, language + "-1280-events");
                await CaptureConsole(language, new(1280, 720), 4, false, false, language + "-1280-fights");
            }
            launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed); await Frames();
            var preferencePanel = PanelFor((InRunConsoleOverlay)stack.Peek()!);
            CheckConsoleCategoryPreferenceSwitching(preferencePanel, Check);
            preferencePanel.CloseButton.EmitSignal(Button.SignalName.Pressed); await Frames();
            Check(Signature() == before, "all localized rendered browsing preserves run state and RNG");

            var singleplayer = RunManager.Instance.NetService;
            Set(RunManager.Instance, nameof(RunManager.NetService), new NetHostGameService(PeerVersionInfo.LocalDefault()));
            try
            {
                InRunConsoleLauncher.ApplyPreferences(root.GetTree(), true, 50, 100);
                launcher._Process(1);
                launcher.EmitSignal(Button.SignalName.Pressed);
                Check(!ConsoleActions.CanUseConsole && !launcher.Visible && launcher.Disabled && stack.ScreenCount == 0,
                    "native host multiplayer service hides and disables the entry, including direct activation");
                submittedBefore = _consoleSubmitted.Count;
                var canonicalCard = ModelDb.GetById<CardModel>(duplicateCards[0].Id);
                var canonicalRelic = ModelDb.AllRelics.First();
                var canonicalAncient = ModelDb.AllAncients.First();
                var canonicalEncounter = ModelDb.AllEncounters.First();
                Func<Task<ConsoleActionResult>>[] actions =
                [
                    () => ConsoleActions.AddCardsAsync([canonicalCard], PileType.Deck, 1, false),
                    () => ConsoleActions.RemoveCardsAsync([duplicateCards[0]], PileType.Deck),
                    () => ConsoleActions.AddRelicsAsync([canonicalRelic]),
                    () => ConsoleActions.RemoveRelicsAsync([player.Relics.First()]),
                    () => ConsoleActions.AddPotionsAsync([potionModel]),
                    () => ConsoleActions.RemovePotionAsync(0, duplicatePotions[0]),
                    () => ConsoleActions.ReplacePotionAsync(0, duplicatePotions[0], potionModel),
                    () => ConsoleActions.EnterAncientAsync(canonicalAncient),
                    () => ConsoleActions.FightAsync(canonicalEncounter),
                    () => ConsoleActions.EnterEventAsync(ordinaryEvent),
                    () => ConsoleActions.AdjustGoldAsync(17),
                    () => ConsoleActions.AdjustGoldAsync(-9),
                    ConsoleActions.ToggleTravelAsync, ConsoleActions.ToggleGodModeAsync, ConsoleActions.WinAsync
                ];
                foreach (var action in actions)
                    Check(!(await action()).Success, "every public action rejects native multiplayer admission");
                var direct = new ConsoleActionCommand().Process(player,
                    [Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(remove))]);
                Check(!direct.success && direct.task == null && !new ConsoleActionCommand().IsNetworked &&
                    _consoleSubmitted.Count == submittedBefore && Signature() == before,
                    "direct custom command and public actions reject multiplayer before submission or mutation");
            }
            finally { Set(RunManager.Instance, nameof(RunManager.NetService), singleplayer); }
            var roster = (List<Player>)Field(run, "_players");
            TestMode.IsOn = true;
            var secondPlayer = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            roster.Add(secondPlayer);
            try
            {
                launcher._Process(1);
                Check(!ConsoleActions.CanUseConsole && !launcher.Visible && launcher.Disabled,
                    "singleplayer transport cannot admit a synthetic two-player party");
                Check(!(await ConsoleActions.ToggleTravelAsync()).Success &&
                    !new ConsoleActionCommand().Process(player, ["not-decoded-in-multiplayer"]).success,
                    "two-player party rejects both native and direct custom actions");
            }
            finally { roster.Remove(secondPlayer); TestMode.IsOn = false; }
            Check(Signature() == before, "multiplayer rejection tests retain original singleplayer state and RNG");

            async Task ExecuteOriginal(ConsoleActionRequest request)
            {
                var result = new ConsoleActionCommand().Process(player,
                    [Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request))]);
                Check(result.success, "original custom command admits isolated singleplayer action " + request.Operation);
                if (result.task != null) await result.task;
            }
            TestMode.IsOn = true;
            int goldBefore = player.Gold;
            await ExecuteOriginal(goldAdd);
            Check(player.Gold == goldBefore + 17, "actual native gain command applies requested positive gold");
            await ExecuteOriginal(goldRemove);
            Check(player.Gold == goldBefore + 8, "actual native loss command applies requested negative gold");
            await ExecuteOriginal(goldClamp);
            Check(player.Gold == 0, "native gold loss clamps at zero for a larger signed deduction");
            await ExecuteOriginal(remove);
            Check(player.Deck.Cards.Count == deckBefore.Length - 1 && player.Deck.Cards.Contains(duplicateCards[0]) &&
                !player.Deck.Cards.Contains(duplicateCards[1]) && duplicateCards[1].HasBeenRemovedFromState,
                "actual original deck removal removes only the selected second duplicate instance");
            await ExecuteOriginal(removePotion);
            Check(ReferenceEquals(player.PotionSlots[0], duplicatePotions[0]) && player.PotionSlots[1] == null &&
                duplicatePotions[1].HasBeenRemovedFromState && !duplicatePotions[0].HasBeenRemovedFromState,
                "actual original potion discard removes only the selected second duplicate slot");
            var combat = new CombatState(runState: run);
            combat.AddPlayer(player);
            CombatManager.Instance.SetUpCombat(combat);
            Set(Field(CombatManager.Instance, "_turnState"), "IsInProgress", true);
            var handCards = player.PlayerCombatState!.DrawPile.Cards.GroupBy(c => c.Id).First(g => g.Count() >= 2).Take(2).ToArray();
            foreach (var card in handCards)
            { player.PlayerCombatState.DrawPile.RemoveInternal(card); player.PlayerCombatState.Hand.AddInternal(card); }
            foreach (string language in new[] { "en", "zh" })
                await CaptureConsole(language, new(1280, 720), 0, true, true, language + "-1280-hand");
            var masterBeforeHandRemoval = player.Deck.Cards.ToArray();
            var handMapResult = await ConsoleActions.RemoveCardsAsync([handCards[1]], PileType.Hand);
            Check(handMapResult.Success, "actual combat fixture admits explicit hand selection");
            var removeHand = LastRequest();
            Check(removeHand.Pile == PileType.Hand && removeHand.CombatIds.Length == 1,
                "hand removal transport includes original combat identity");
            await ExecuteOriginal(removeHand);
            Check(player.PlayerCombatState.Hand.Cards.SequenceEqual(new[] { handCards[0] }) &&
                player.Deck.Cards.SequenceEqual(masterBeforeHandRemoval),
                "actual hand removal preserves its equal-ID neighbor and every permanent-deck instance");
            InRunConsoleLauncher.ApplyPreferences(root.GetTree(), true, 50, 100);
            launcher._Process(1); launcher.EmitSignal(Button.SignalName.Pressed); await Frames();
            var staleHandPanel = PanelFor((InRunConsoleOverlay)stack.Peek()!);
            Check(staleHandPanel.ContentOpen && ((OptionButton)Field(staleHandPanel, "_pile")).Selected == 1 &&
                staleHandPanel.SelectedCount == 0,
                "reopening after removal drops stale hand selection instead of selecting its equal-ID neighbor");
            staleHandPanel.CloseButton.EmitSignal(Button.SignalName.Pressed); await Frames();
            CombatManager.Instance.Reset(false);
            TestMode.IsOn = false;
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = true, checks,
                nativeOverlayStack = true, readOnlyLifecycle = true, isolatedMockSave = true,
                singleplayerAdmission = true, actualExactDeckRemoval = true, actualExactHandRemoval = true, actualExactPotionDiscard = true,
                drawerRailAndContent = true, displayModesPreserveSelection = true, actualGoldGainLossClamp = true,
                screenshots,
                ownerRuntimeAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"IN_RUN_CONSOLE_UI_PASS checks={checks};nativeOverlayStack=true;passiveBrowsingRunRngUnchanged=true;actualIsolatedMutations=true;OwnerRuntimeAcceptance=false");
        }
        finally
        {
            if (global != null && GodotObject.IsInstanceValid(global)) global.Free();
            if (oldConsolePreferences is null) { if (File.Exists(consolePreferencesPath)) File.Delete(consolePreferencesPath); }
            else File.WriteAllBytes(consolePreferencesPath, oldConsolePreferences);
            foreach (var node in detached) if (GodotObject.IsInstanceValid(node)) node.Free();
            fixture.UnpatchAll(fixture.Id);
            _consoleSubmitted.Clear();
            consoleInstance.SetValue(null, oldConsole);
            typeof(NGame).GetProperty(nameof(NGame.Instance))!.SetValue(null, oldGame);
            RunManager.Instance.CleanUp(); LocalContext.NetId = oldLocal;
            typeof(ModRuntime).GetProperty(nameof(ModRuntime.Snapshot))!.SetValue(null, oldRuntime);
            preferences.SetLanguageOverride(oldLanguage);
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(oldGameLanguage);
            TestMode.IsOn = oldTest;
        }
    }
}
