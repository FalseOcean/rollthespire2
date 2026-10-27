using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunOperationPreferenceChecks(Node root, string output, ModRuntimeSnapshot runtime,
        Action<bool, string> check)
    {
        RunOperationPolicyChecks(check);
        var categories = Enum.GetValues<OperationPredictionCategory>();
        var entries = OperationPredictionCatalog.Entries;
        var defaults = new OperationPredictionPreferences();
        check(entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() == entries.Length &&
            entries.All(entry => defaults.Get(entry.Id) == OperationPredictionMode.Auto),
            "all concrete sources and supplements have unique IDs and default to Auto");
        check(OperationPredictionCatalog.ForSource(new("EVENT", "POTION_COURIER")) == "event:POTION_COURIER" &&
            entries.Count(entry => entry.Id == "event:POTION_COURIER") == 1,
            "one Potion Courier source setting governs its operation stages");
        foreach (var entry in entries)
        {
            foreach (var mode in new[] { OperationPredictionMode.Off, OperationPredictionMode.On })
            {
                var changed = defaults.With(entry.Id, mode);
                check(entries.All(other => changed.Get(other.Id) ==
                    (other.Id == entry.Id ? mode : OperationPredictionMode.Auto)),
                    entry.Id + " changes only its own source or supplement");
            }
        }
        foreach (var category in categories)
        {
            var members = OperationPredictionCatalog.ForCategory(category).ToArray();
            var firstId = members.First().Id;
            var mixed = defaults.With(category, OperationPredictionMode.Off).With(firstId, OperationPredictionMode.On);
            check(entries.All(entry => mixed.Get(entry.Id) == (entry.Id == firstId ? OperationPredictionMode.On :
                entry.Category == category ? OperationPredictionMode.Off : OperationPredictionMode.Auto)),
                category + " batch changes its concrete members and allows an individual override");
            var reset = mixed.With(category, OperationPredictionMode.Auto).With(firstId, OperationPredictionMode.On);
            check(entries.All(entry => reset.Get(entry.Id) ==
                (entry.Id == firstId ? OperationPredictionMode.On : OperationPredictionMode.Auto)),
                category + " batch reset leaves no inherited category setting");
        }

        string state = Path.Combine(output, "operation-preferences-" + Guid.NewGuid().ToString("N"));
        var first = new SearchWorkspace(state, false);
        var second = new SearchWorkspace(state, false);
        check(entries.All(entry => first.Preferences.OperationPredictionModes.Get(entry.Id) == OperationPredictionMode.Auto),
            "a fresh workspace defaults every concrete source and supplement to Auto");
        foreach (string id in new[] { "reward:reroll", "reward:sacrifice", "shop:restock", "event:POTION_COURIER", "extra:obtain-chain" })
        {
            foreach (var mode in new[] { OperationPredictionMode.Off, OperationPredictionMode.On })
            {
                first.SetOperationPredictionMode(id, mode);
                second.SyncFeaturePreferences();
                check(entries.All(entry => second.Preferences.OperationPredictionModes.Get(entry.Id) ==
                    (entry.Id == id ? mode : OperationPredictionMode.Auto)),
                    id + " changes only its leaf across two existing workspaces");
                check(new SearchWorkspace(state, false).Preferences.OperationPredictionModes.Get(id) == mode,
                    id + " persists " + mode + " for a reopened workspace");
                using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(state, "RolltheSpire2", "state", "user_preferences.json")));
                check(saved.RootElement.GetProperty("OperationPredictionModes").GetProperty("Entries").GetProperty(id).GetString() == mode.ToString(),
                    id + " writes the selected tri-state leaf to disk");
            }
            // A stale surface flushing another preference must preserve the
            // latest directory-owned source selection.
            second.SetMapPredictionEnabled(false);
            check(new SearchWorkspace(state, false).Preferences.OperationPredictionModes.Get(id) == OperationPredictionMode.On,
                id + " survives another workspace preference flush");
            second.SetMapPredictionEnabled(true);
            second.SetOperationPredictionMode(id, OperationPredictionMode.Auto);
            first.SyncFeaturePreferences();
        }
        first.SetOperationPredictionMode(OperationPredictionCategory.EventOption, OperationPredictionMode.Off);
        second.SyncFeaturePreferences();
        second.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.On);
        first.SyncFeaturePreferences();
        check(entries.All(entry => first.Preferences.OperationPredictionModes.Get(entry.Id) ==
            (entry.Id == "event:POTION_COURIER" ? OperationPredictionMode.On :
                entry.Category == OperationPredictionCategory.EventOption ? OperationPredictionMode.Off : OperationPredictionMode.Auto)),
            "a persisted group batch remains overridable by one source in another workspace");
        first.SetOperationPredictionMode(OperationPredictionCategory.EventOption, OperationPredictionMode.Auto);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(object value, string name) => value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        string Token(string id) => id.Replace(':', '_').Replace('-', '_');
        var host = new Control { Size = new(1600, 900) };
        root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, state);
        host.AddChild(shell);
        string oldGameLanguage = LocManager.Instance.Language;
        try
        {
            shell.Open(); Call(shell, "OpenSettings");
            await AwaitPageReady(root, shell);
            var store = (SearchWorkspace)Field(shell, "_persistence");
            var configure = (Button)shell.FindChild("SettingsOperationConfigure", true, false);
            check(shell.FindChild("SettingsOperationConfiguration", true, false) == null,
                "operation source configuration is absent until its configure button is used");
            string? previousSourceName = null;
            foreach (string language in new[] { "zh", "en" })
            {
                // Content names follow the live game locale; the RT2 override
                // localizes the surrounding settings controls independently.
                LocManager.Instance.SetLanguage(language == "zh" ? "zhs" : "eng");
                store.SetLanguageOverride(language); Call(shell, "RefreshLanguage");
                var text = JsonUiTextProvider.CreateUi13(language);
                configure.EmitSignal(BaseButton.SignalName.Pressed);
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                var panel = (Control)shell.FindChild("SettingsOperationConfiguration", true, false);
                var search = (LineEdit)shell.FindChild("SettingsOperationSearch", true, false);
                var rows = entries.ToDictionary(entry => entry.Id,
                    entry => (Control)shell.FindChild("SettingsOperationEntry_" + Token(entry.Id), true, false));
                var controls = entries.ToDictionary(entry => entry.Id,
                    entry => (OptionButton)shell.FindChild("SettingsOperationMode_" + Token(entry.Id), true, false));
                check(panel.Visible && configure.Text == text.Get("settings.operation.configure") &&
                    search.PlaceholderText == text.Get("settings.operation.search"),
                    language + " opens the separate source configuration panel with localized search");
                check(controls.Count == entries.Length && controls.Values.All(control => control.ItemCount == 3 &&
                    control.Selected == (int)OperationPredictionMode.Auto && string.IsNullOrEmpty(control.TooltipText)),
                    language + " every concrete source and supplement has three default-Auto choices without tooltips");
                check(controls.Values.All(control => Enum.GetValues<OperationPredictionMode>().All(mode =>
                    control.GetItemText((int)mode) == text.Get("settings.operation.mode." + mode))),
                    language + " localizes all concrete tri-state selectors");
                foreach (var category in categories)
                {
                    var group = (Control)shell.FindChild("SettingsOperationGroup" + category, true, false);
                    check(Descendants(group).OfType<Label>().Any(label => label.Text == text.Get("settings.operation.category." + category)),
                        language + " localizes source group " + category);
                    foreach (var mode in Enum.GetValues<OperationPredictionMode>())
                        check(((Button)shell.FindChild("SettingsOperationBatch" + category + mode, true, false)).Text ==
                            text.Get("settings.operation.mode." + mode), language + " localizes group batch " + category + mode);
                }
                foreach (var entry in entries.Where(entry => entry.NameKey != null))
                {
                    string expected = text.Get(entry.NameKey!);
                    if (entry.IsSupplement) expected = text.Format("settings.operation.supplement", expected);
                    check(Descendants(rows[entry.Id]).OfType<Label>().Any(label => label.Text == expected),
                        language + " localizes the named source or supplement " + entry.Id);
                }
                string SourceName(string id) => rows[id].GetChildren().OfType<Label>().Single().Text;
                string sourceName = SourceName("event:POTION_COURIER");
                check(!string.IsNullOrWhiteSpace(sourceName) && sourceName != "event:POTION_COURIER" &&
                    sourceName == ModelDb.Event<PotionCourier>().Title.GetFormattedText() &&
                    (previousSourceName == null || previousSourceName != sourceName),
                    language + " resolves Potion Courier to its localized game name");
                previousSourceName = sourceName;
                void Search(string query)
                {
                    search.Text = query;
                    search.EmitSignal(LineEdit.SignalName.TextChanged, query);
                }
                Search("event:POTION_COURIER");
                check(rows["event:POTION_COURIER"].Visible && rows.Count(pair => pair.Value.Visible) == 1 &&
                    categories.All(category => ((Control)shell.FindChild("SettingsOperationGroup" + category, true, false)).Visible ==
                        (category == OperationPredictionCategory.EventOption)),
                    language + " stable-ID search shows the matching source and hides empty groups");
                ((Button)shell.FindChild("SettingsOperationBatchEventOptionOff", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                check(entries.All(entry => store.Preferences.OperationPredictionModes.Get(entry.Id) ==
                    (entry.Category == OperationPredictionCategory.EventOption ? OperationPredictionMode.Off : OperationPredictionMode.Auto)),
                    language + " a filtered group batch updates hidden members and leaves other groups unchanged");
                var courier = controls["event:POTION_COURIER"];
                courier.Select((int)OperationPredictionMode.On);
                courier.EmitSignal(OptionButton.SignalName.ItemSelected, (long)OperationPredictionMode.On);
                check(entries.All(entry => store.Preferences.OperationPredictionModes.Get(entry.Id) ==
                    (entry.Id == "event:POTION_COURIER" ? OperationPredictionMode.On :
                        entry.Category == OperationPredictionCategory.EventOption ? OperationPredictionMode.Off : OperationPredictionMode.Auto)) &&
                    new SearchWorkspace(state, false).Preferences.OperationPredictionModes.Get("event:POTION_COURIER") == OperationPredictionMode.On,
                    language + " a real source selector overrides and persists its filtered group batch");
                ((Button)shell.FindChild("SettingsOperationBatchEventOptionAuto", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                Search(sourceName);
                check(rows["event:POTION_COURIER"].Visible,
                    language + " search finds a source by its localized game name");
                string supplementName = SourceName("extra:obtain-chain");
                Search(supplementName);
                check(rows["extra:obtain-chain"].Visible,
                    language + " search finds the localized standalone obtain-chain supplement");
                Search(text.Get("settings.operation.category.EventOption"));
                check(entries.Where(entry => entry.Category == OperationPredictionCategory.EventOption).All(entry => rows[entry.Id].Visible),
                    language + " localized group search includes all its concrete sources and supplements");
                Search("no-such-operation-" + Guid.NewGuid().ToString("N"));
                check(rows.Values.All(row => !row.Visible) && ((Control)shell.FindChild("SettingsOperationNoMatches", true, false)).Visible,
                    language + " an unmatched search displays an explicit empty result");
                Search("");
                check(rows.Values.All(row => row.Visible) && !((Control)shell.FindChild("SettingsOperationNoMatches", true, false)).Visible,
                    language + " clearing search restores all source and supplement rows");
                check(((Label)shell.FindChild("SettingsOperationRfStatus", true, false)).Text ==
                    text.Get("settings.operation.rf." + (RandomForeseerCompatibility.CaptureStatus() switch
                    {
                        RandomForeseerStatus.NotLoaded => "not_loaded",
                        RandomForeseerStatus.Known => "known",
                        _ => "unknown"
                    })), language + " aggregate RF status reports loading without reading RF settings");
                Search("event:POTION_COURIER");
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "operation-preferences-" + language + ".png"));
                ((Button)shell.FindChild("SettingsOperationConfigurationBack", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                check(!panel.Visible, language + " closes configuration through its real back button");
            }
        }
        finally
        {
            host.Free();
            LocManager.Instance.SetLanguage(oldGameLanguage);
        }
    }
    private static async Task RunDisabledOperationSourceCheck(Node root, SubViewport viewport, Control hoverRoot,
        Control owner, string version, Func<string> signature, Action<bool, string> check)
    {
        var preferences = new SearchWorkspace(OS.GetUserDataDir(), false);
        var original = preferences.Preferences.OperationPredictionModes.Get("event:POTION_COURIER");
        bool originalEnabled = preferences.Preferences.OperationPredictionEnabled;
        string before = signature();
        int seen = _neowSeenCalls;
        try
        {
            preferences.SetOperationPredictionEnabled(true);
            preferences.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.Auto);
            NeowOperationHoverController.Focus(owner, version);
            var demandController = hoverRoot.GetNode<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
            demandController.SetProcess(false);
            var captureDemand = typeof(NeowOperationHoverController).GetMethod("CaptureDemand",
                BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(RandomForeseerSnapshot)], null)!;
            OperationDemand Plan(RandomForeseerSnapshot snapshot) => (OperationDemand)captureDemand.Invoke(demandController, [snapshot])!;
            var known = new RandomForeseerSnapshot(RandomForeseerStatus.Known, "0.13.19", "Loaded");
            var unknown = new RandomForeseerSnapshot(RandomForeseerStatus.Unknown, "", "LoadDetectionFailed");
            var absent = new RandomForeseerSnapshot(RandomForeseerStatus.NotLoaded, "", "NotLoaded");
            check(!Plan(known).MayPredict,
                "automatic known-RF Ransack demand stops before capture for a confirmed duplicate");
            check(Plan(unknown).MayPredict && Plan(absent).MayPredict,
                "automatic unknown or absent RF retains Ransack prediction demand");
            preferences.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.On);
            check(Plan(known).MayPredict, "explicit On keeps Ransack prediction demand with known RF");
            preferences.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.Off);
            check(!Plan(known).MayPredict && !Plan(unknown).MayPredict && !Plan(absent).MayPredict,
                "explicit Off stops Ransack demand for every RF status");
            demandController.SetProcess(true);
            viewport.PushInput(new InputEventMouseMotion { Position = owner.GetGlobalRect().GetCenter(),
                GlobalPosition = owner.GetGlobalRect().GetCenter() }, true);
            NeowOperationHoverController.Focus(owner, version);
            for (int frame = 0; frame < 4; frame++)
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            var controller = hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
            if (controller != null)
            {
                typeof(NeowOperationHoverController).GetMethod("BeginPrediction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                controller._Process(.75);
            }
            check(controller == null || PredictorOperationCapture.ReadNullable(controller, "_snapshot") == null &&
                PredictorOperationCapture.ReadNullable(controller, "_work") == null &&
                PredictorOperationCapture.Read<NeowOperationHoverController.HoverTiming>(controller, "_timing").CaptureMs == 0,
                "Potion Courier source Off admits no native scene capture or prediction worker for Potion Courier Ransack");
            check(controller?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay") == null,
                "Potion Courier source Off leaves no operation preview overlay");
            check(signature() == before && _neowSeenCalls == seen,
                "disabled native source leaves run, RNG, offers and seen cards unchanged");
        }
        finally
        {
            NeowOperationHoverController.Leave(owner);
            viewport.PushInput(new InputEventMouseMotion { Position = new(4, viewport.Size.Y - 4),
                GlobalPosition = new(4, viewport.Size.Y - 4) }, true);
            preferences.SetOperationPredictionMode("event:POTION_COURIER", original);
            preferences.SetOperationPredictionEnabled(originalEnabled);
        }
    }

    private static void RunOperationPolicyChecks(Action<bool, string> check)
    {
        var known = new RandomForeseerSnapshot(RandomForeseerStatus.Known, "0.13.19", "Loaded");
        check(known.IsLoaded && (known with { Version = "unrecognized-version" }).IsLoaded,
            "confirmed RF loading enables the coexistence assumption independently of its version");
        foreach (var status in new[] { RandomForeseerStatus.Unknown, RandomForeseerStatus.NotLoaded })
            check(!(known with { Status = status }).IsLoaded,
                status + " preserves RT2 base prediction availability");

        check(!OperationPredictionPolicy.Wants(OperationPredictionMode.Off, true, true) &&
            !OperationPredictionPolicy.Wants(OperationPredictionMode.Auto, true, false) &&
            OperationPredictionPolicy.Wants(OperationPredictionMode.Auto, true, true) &&
            OperationPredictionPolicy.Wants(OperationPredictionMode.Auto, false, false) &&
            OperationPredictionPolicy.Wants(OperationPredictionMode.On, true, false),
            "source demand respects Off, loaded-RF Auto, independent supplement Auto, absent-RF Auto and forced On");
        var preferences = new OperationPredictionPreferences();
        OperationDemand Demand(OperationPredictionPreferences selected, string primary) =>
            new(selected, primary, true, OperationSurfaceDemand.All, "fixture");
        var policy = Demand(preferences, "event:POTION_COURIER") with
        { VisibleFeatures = ImmutableHashSet.Create("event:POTION_COURIER", "extra:event-combat") };
        var samePolicy = policy with
        { VisibleFeatures = ImmutableHashSet.Create("extra:event-combat", "event:POTION_COURIER") };
        check(!ReferenceEquals(policy.VisibleFeatures, samePolicy.VisibleFeatures) && policy.SamePolicy(samePolicy) &&
            !policy.SamePolicy(samePolicy with { Preferences = preferences.With("event:POTION_COURIER", OperationPredictionMode.Off) }),
            "policy refresh compares independently allocated visible sets by content and still detects a relevant mode change");
        var leaf = new ModelKey("RELIC", "NEW_LEAF");
        var tablet = new ModelKey("RELIC", "HEFTY_TABLET");
        var bones = new ModelKey("RELIC", "NEOWS_BONES");
        var transform = new OperationPreview(bones, [], [], false, [new([], [
            new(leaf, [], [new(1, [], 1, 1, false, false)]),
            new(leaf, [new(OperationPreviewAction.TransformCard, [], [], false)], []),
            new(tablet, [new(OperationPreviewAction.AddCard, [], [], false)], [])], [], 1, true)], []);
        var sourceOff = OperationPredictionPolicy.Filter(transform,
            Demand(preferences.With("relic:NEW_LEAF", OperationPredictionMode.Off), "relic:NEOWS_BONES"));
        check(sourceOff.Branches.Single().Sections is { Length: 1 } unaffected && unaffected[0].Source == tablet,
            "one source Off removes its card groups and rows while retaining an independent obtained relic's output");

        var wing = new ModelKey("RELIC", "PAELS_WING");
        var courier = new ModelKey("RELIC", "THE_COURIER");
        var blockedPurchase = new OperationPreview(courier, [], [], false, [],
            ["CourierRestockAwaitingPlayerChoice:purchase", "UnrelatedOrigin"])
        { Notices = ["Purchase:TooFull", "UnrelatedOrigin"] };
        var restockOnly = Demand(preferences, "relic:NEOWS_BONES") with
        { ShowPrimary = false, ChainOnly = true, VisibleFeatures = ["shop:restock"] };
        var blockedRestock = OperationPredictionPolicy.Filter(blockedPurchase, restockOnly);
        check(blockedRestock.Notices.SequenceEqual(["Purchase:TooFull"]) &&
            blockedRestock.PendingReasons.SequenceEqual(["CourierRestockAwaitingPlayerChoice:purchase"]) &&
            blockedRestock.Branches.IsEmpty,
            "notice-only restock keeps its real purchase conditions without resurrecting suppressed origin context");
        var noRestock = OperationPredictionPolicy.Filter(blockedPurchase, restockOnly with { VisibleFeatures = [] });
        check(noRestock.Notices.IsEmpty && noRestock.PendingReasons.IsEmpty,
            "a suppressed origin without an admitted restock keeps purchase context hidden");
        var linked = new OperationPreview(wing, [], [], false, [new([], [
            new(wing, [new(OperationPreviewAction.ObtainRelic, [], [], false)], []) { FeatureId = "reward:sacrifice" },
            new(courier, [new(OperationPreviewAction.Restock, [], [], false)], []) { FeatureId = "extra:conditional-restock" }], [], 1, true)], []);
        var sacrificeOff = OperationPredictionPolicy.Filter(linked,
            Demand(preferences.With("reward:sacrifice", OperationPredictionMode.Off), "reward:sacrifice") with
            { ShowPrimary = false, VisibleFeatures = ["extra:conditional-restock"] });
        check(sacrificeOff.Branches.Single().Sections is { Length: 1 } restock &&
            restock[0].FeatureId == "extra:conditional-restock" && restock[0].Rows.Single().Action == OperationPreviewAction.Restock,
            "disabling basic sacrifice preserves its independently enabled conditional Courier supplement");
        var sequence = new OperationPreview(new("RELIC", "DRIFTWOOD"), [], [], false, [new([], [
            new(new("RELIC", "DRIFTWOOD"), [], [new(1, [], 1, 1, false, false), new(2, [], 1, 1, false, false)])
                { FeatureId = "extra:reroll-sequence" }], [], 1, true)], []);
        var fullSequence = OperationPredictionPolicy.Filter(sequence,
            Demand(preferences.With("reward:reroll", OperationPredictionMode.Off), "reward:reroll") with
            { ShowPrimary = false, VisibleFeatures = ["extra:reroll-sequence"] });
        check(fullSequence.Branches.Single().Sections.Single().CardGroups.Select(group => group.Id).SequenceEqual([1L, 2L]),
            "the independent reroll sequence retains its complete order including the first group");

        var identity = new OperationPreviewSection(bones, [new(OperationPreviewAction.ObtainRelic, [tablet], [], false)], []);
        var effect = new OperationPreviewSection(tablet, [new(OperationPreviewAction.AddCard, [], [], false)], []);
        var chain = new OperationPreview(bones, [tablet], [], false, [new([], [identity, effect], [], 1, true)], []);
        var chainDemand = Demand(preferences, "relic:NEOWS_BONES") with { ChainOnly = true };
        var identityOnly = OperationPredictionPolicy.Filter(chain with { Branches = [new([], [identity], [], 1, true)] }, chainDemand);
        var actualChain = OperationPredictionPolicy.Filter(chain, chainDemand);
        check(identityOnly.Branches.Single().Sections.IsEmpty && identityOnly.ContainedRelics.IsEmpty &&
            actualChain.Branches.Single().Sections.Length == 2 && actualChain.ContainedRelics.SequenceEqual([tablet]),
            "Auto RF settlement hides a plain duplicate but preserves full context when a visible obtain consequence exists");
        var trash = new ModelKey("EVENT", "TRASH_HEAP");
        var ordinaryEvent = new OperationPreview(trash, [], [], false, [new([], [
            new(trash, [new(OperationPreviewAction.ObtainRelic, [tablet], [], false)], []), effect], [], 1, true)], []);
        var ordinaryResult = OperationPredictionPolicy.Filter(ordinaryEvent,
            Demand(preferences, "event:TRASH_HEAP") with { IncludeChain = false, IncludeTransformEffects = false });
        check(ordinaryResult.Branches.Single().Sections.Length == 2 &&
            ordinaryResult.Branches.Single().Sections[1].Rows.Single().Action == OperationPreviewAction.AddCard,
            "ordinary event obtain consequences remain part of its complete basic result when global supplements are off");
        var transformEffects = new OperationPreview(leaf, [], [], false, [new([], [
            new(leaf, [], [new(1, [], 1, 1, false, false)]) { Category = OperationPredictionCategory.CardTransform },
            new(leaf, [new(OperationPreviewAction.TransformCard, [], [], false), new(OperationPreviewAction.AddCard, [], [], false)], []),
            effect], [], 1, true)], []);
        var transformDemand = Demand(preferences, "relic:NEW_LEAF") with
        { TransformSource = true, IncludeChain = false, IncludeTransformEffects = false };
        var basicTransform = OperationPredictionPolicy.Filter(transformEffects, transformDemand);
        check(basicTransform.Branches.Single().Sections is { Length: 2 } basicSections &&
            basicSections[0].CardGroups.Length == 1 && basicSections[1].Rows.Single().Action == OperationPreviewAction.TransformCard,
            "a transform source without supplements or a parent chain retains transform groups and rows while removing extra effects");
        var parentTransform = OperationPredictionPolicy.Filter(transformEffects,
            transformDemand with { HasParentChain = true, IncludeChain = true });
        check(parentTransform.Branches.Single().Sections.Length == 3 &&
            parentTransform.Branches.Single().Sections[1].Rows.Length == 2,
            "an enabled parent obtain chain preserves the transform's related consequences");
    }
}
