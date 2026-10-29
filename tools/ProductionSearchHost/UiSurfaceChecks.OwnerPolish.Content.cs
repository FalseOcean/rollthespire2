using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Call after InitializeNativeWorkspaceAsync. These are product component
    // presentation fixtures; the reroll fixture makes no RNG/parity assertion.
    internal static async Task RunOwnerPolishContentChecks(Node root, string output, ModRuntimeSnapshot runtime, bool referenceCopyOnly = false)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Field(object instance, string name) => instance.GetType().GetField(name, flags)!.GetValue(instance)!;
        object? Call(object instance, string name, params object?[] args)
        {
            var method = instance.GetType().GetMethod(name, flags)!;
            return method.Invoke(instance, args.Concat(method.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray());
        }
        IEnumerable<Node> Nodes(Node node)
        {
            yield return node;
            foreach (var child in node.GetChildren()) foreach (var nested in Nodes(child)) yield return nested;
        }
        int checks = 0;
        var screenshots = new List<string>();
        var namesEvidence = new List<object>();
        void Check(bool condition, string description)
        { checks++; if (!condition) throw new InvalidOperationException("OwnerPolishContent:" + description); }
        async Task Frames(int count = 5)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task MovePointer(Vector2 position)
        {
            root.GetWindow().GrabFocus();
            await Frames(2);
            for (int i = 0; i < 3; i++)
            {
                root.GetViewport().WarpMouse(position);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position });
                Input.FlushBufferedEvents();
                root.GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
                await Frames(1);
            }
        }
        async Task Snapshot(string name)
        {
            await Frames();
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string file = Path.Combine(output, name + ".png");
            Check(root.GetViewport().GetTexture().GetImage().SavePng(file) == Error.Ok, "save " + name);
            screenshots.Add(file);
        }

        Directory.CreateDirectory(output);
        root.GetWindow().Size = new(1600, 900);
        var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "owner-content-state", Guid.NewGuid().ToString("N")));
        host.AddChild(shell);
        var frame = (QueryWorkbenchFrame)Field(shell, "_references");
        var store = (SearchWorkspace)Field(shell, "_persistence");
        void Navigate(string name)
        {
            var method = shell.GetType().GetMethod("SelectTask", flags)!;
            method.Invoke(shell, [Enum.Parse(method.GetParameters()[0].ParameterType, name), null, null]);
        }
        var icons = new ReflectionGameIconResolver("owner-content-ui");
        using var characterIcons = new NativeCharacterPoolIconProvider(icons);
        var tooltip = new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(), new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
        host.AddChild(tooltip); tooltip.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var analysis = new AnalysisPage(runtime, icons, characterIcons, BaseGameModelKeys.Characters.All, tooltip) { Visible = false };
        host.AddChild(analysis);
        string originalNativeLanguage = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language
            ?? throw new InvalidOperationException("OwnerPolishContent: native fixture language not initialized");
        string? originalOverride = InRunFeaturePreferences.LanguageOverride;
        var runtimeDirectory = (string)typeof(InRunFeaturePreferences).GetField("_runtimeDirectory", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            shell.Open(); await AwaitPageReady(root, shell); await Frames();
            foreach (string locale in new[] { "zh", "en" })
            {
                LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                InRunFeaturePreferences.SetLanguage(runtimeDirectory, locale);
                store.SetLanguageOverride(locale);
                shell.GetType().GetField("_languageCode", flags)!.SetValue(shell, ""); Call(shell, "RefreshLanguage");
                var text = JsonUiTextProvider.CreateUi13(locale);
                var names = RuntimeGameContentNameResolver.Create(locale);
                var swiftKey = new ModelKey("ENCHANTMENT", "SWIFT");
                string expectedSwift = ModelDb.GetById<EnchantmentModel>(new("ENCHANTMENT", "SWIFT")).Title.GetFormattedText();
                Check(names.Resolve(swiftKey, GameContentKind.Enchantment) == expectedSwift, "Swift follows the current native language " + locale);
                var missing = new ModelKey("ENCHANTMENT", "RT2_MISSING_PRESENTATION_FIXTURE");
                Check(names.Resolve(missing, GameContentKind.Enchantment) == missing.Serialized, "missing enchantment retains full ID " + locale);
                namesEvidence.Add(new { locale, expectedSwift, actualSwift = names.Resolve(swiftKey, GameContentKind.Enchantment) });

                Navigate("Search"); Call(frame, "SelectDomain", "ancient");
                var ancient = (AncientSearchEditor)Field(frame, "_ancientEditor");
                var draft = Field(ancient, "_draft");
                draft.GetType().GetField("EligibilityExpanded", flags)!.SetValue(draft, true); Call(ancient, "Render");
                await Frames();
                var settings = ancient.FindChild("AncientMatchingSettings", true, false)!;
                var options = settings.GetChildren().OfType<Button>().Where(b => b.Size.X <= 45).OrderBy(b => b.Position.X).ToArray();
                var ancientTooltip = (AnchoredTooltipHost)Field(ancient, "_eligibilityTooltip");
                // This slice snapshots content; the regular content suite keeps
                // physical hover/dismissal coverage. Freeze pointer monitoring here.
                if (referenceCopyOnly) ancientTooltip.SetProcess(false);
                foreach ((string option, string owner) in new[]
                {
                    ("PAELS_CLAW", "PAEL"), ("PAELS_LEGION", "PAEL"), ("PAELS_TOOTH", "PAEL"),
                    ("ARCHAIC_TOOTH", "OROBAS"), ("TOUCH_OF_OROBAS", "OROBAS"),
                    ("NUTRITIOUS_SOUP", "TEZCATARA"), ("BEAUTIFUL_BRACELET", "NONUPEIPE"),
                    ("TRI_BOOMERANG", "TANX"), ("PANDORAS_BOX", "DARV")
                })
                {
                    int index = AncientOptionConditionProfile.AuthoredOptionKeys.ToList().FindIndex(k => k.Entry == option);
                    if (referenceCopyOnly) { options[index].EmitSignal(Control.SignalName.MouseEntered); await Frames(); }
                    else await MovePointer(options[index].GetGlobalRect().GetCenter());
                    string expectedAncient = ModelDb.GetById<AncientEventModel>(new("EVENT", owner)).Title.GetFormattedText();
                    string description = ((Label)Field(ancientTooltip, "_description")).Text;
                    Check(((PanelContainer)Field(ancientTooltip, "_panel")).IsVisibleInTree() &&
                        description.Contains(expectedAncient, StringComparison.Ordinal) && !description.Contains('{'),
                        $"eligibility tooltip uses its actual Ancient's native name {locale}/{option};expected={expectedAncient};actual={description};visible={((PanelContainer)Field(ancientTooltip, "_panel")).IsVisibleInTree()}");
                    namesEvidence.Add(new { locale, option, owner, expectedAncient, description });
                    if (option == "BEAUTIFUL_BRACELET")
                    {
                        Check(description.Contains(expectedSwift), "eligibility tooltip embeds native Swift name " + locale);
                        await Snapshot("owner-content-" + locale + "-swift");
                    }
                    if (referenceCopyOnly) { options[index].EmitSignal(Control.SignalName.MouseExited); await Frames(); }
                    else await MovePointer(new(4, 4));
                }
                Check(!((PanelContainer)Field(ancientTooltip, "_panel")).Visible, "eligibility tooltip dismisses on pointer exit " + locale);
                var missingAncient = new ModelKey("EVENT", "RT2_MISSING_PRESENTATION_FIXTURE");
                Check(names.Resolve(missingAncient, GameContentKind.Ancient) == missingAncient.Serialized,
                    "missing Ancient retains full ID " + locale);

                if (referenceCopyOnly)
                {
                    Navigate("Encyclopedia");
                    var familyPage = (EncyclopediaCanvas)Field(shell, "_encyclopediaCanvas"); familyPage.Open("families");
                    await Frames();
                    var main = (Control)Field(familyPage, "_main");
                    var scroll = (ScrollContainer)Field(familyPage, "_articleScroll");
                    var articleText = Nodes(scroll).OfType<RichTextLabel>().ToArray();
                    Check((string)Field(familyPage, "_articleId") == "families" && articleText.Length > 20 &&
                        articleText.All(l => !l.GetParsedText().Contains("translation pending", StringComparison.OrdinalIgnoreCase)),
                        "full localized family article renders " + locale);
                    Check(Nodes((Control)Field(familyPage, "_rail")).OfType<Button>().Any(b =>
                        b.Text.Contains(locale == "zh" ? "家族" : "Families", StringComparison.OrdinalIgnoreCase)),
                        "family article is reachable in the reading-guide directory " + locale);
                    var sectionButtons = Nodes(main).OfType<Button>().Where(b =>
                        b.GetParent() is VBoxContainer && b.GetParent().GetParent() is ScrollContainer).ToArray();
                    Check(sectionButtons.Length == 7, "family article exposes its overview and six sections " + locale);
                    foreach (var sectionButton in sectionButtons)
                    {
                        sectionButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                        Check(sectionButton == sectionButtons[0] ? scroll.ScrollVertical == 0 : scroll.ScrollVertical > 0,
                            "family table of contents navigates " + locale + "/" + sectionButton.TooltipText);
                    }
                    sectionButtons[0].EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                    await Snapshot("owner-family-" + locale + "-top");
                    var formula = articleText.Single(l => l.GetParsedText().Contains("c / (1-s)", StringComparison.Ordinal));
                    scroll.EnsureControlVisible(formula); await Frames();
                    await Snapshot("owner-family-" + locale + "-formula");
                    scroll.ScrollVertical = (int)Math.Max(0, scroll.GetVScrollBar().MaxValue - scroll.GetVScrollBar().Page);
                    await Snapshot("owner-family-" + locale + "-end");
                    continue;
                }

                Navigate("Encyclopedia");
                var encyclopedia = (EncyclopediaCanvas)Field(shell, "_encyclopediaCanvas"); encyclopedia.Open("reading-guide");
                await Frames();
                var article = Nodes((Control)Field(encyclopedia, "_main")).OfType<RichTextLabel>().ToArray();
                var palette = WorkspacePalette.MainPanel;
                Check(article.Any(l => l.GetThemeColor("default_color") == palette.Color(palette.Selected)) &&
                    article.Any(l => l.GetThemeColor("default_color") == palette.Color(palette.Text)), "article retains gold headings and ordinary body text " + locale);
                await Snapshot("owner-content-" + locale + "-encyclopedia");

                shell.Hide();
                var queue = new HBoxContainer { Position = new(160, 120) }; host.AddChild(queue);
                var elite = ModelDb.AllEncounters.First(e => e.RoomType == RoomType.Elite);
                var eliteKey = new ModelKey(elite.Id.Category, elite.Id.Entry);
                string eliteName = names.Resolve(eliteKey, GameContentKind.Encounter);
                var tile = (Control)Call(analysis, "WQueueTile", eliteKey, true, 1, eliteName)!; queue.AddChild(tile);
                await Frames();
                Check(string.IsNullOrEmpty(tile.TooltipText), "elite no longer owns a native text tooltip " + locale);
                await MovePointer(tile.GetGlobalRect().GetCenter());
                Check(((PanelContainer)Field(tooltip, "_panel")).Visible && ((Label)Field(tooltip, "_title")).Text.Contains(eliteName), "elite pointer opens existing content host " + locale);
                await Snapshot("owner-content-" + locale + "-elite-hover");
                queue.QueueFree(); await Frames();
                Check(!((PanelContainer)Field(tooltip, "_panel")).Visible, "elite host dismisses when tile exits tree " + locale);

                var overlay = new NeowOperationOverlay(); host.AddChild(overlay);
                var card = new PredictorCard(1, new("CARD", "STRIKE_IRONCLAD"), EffectCardType.Attack, 0, 1, true, 0);
                var groups = Enumerable.Range(1, 3).Select(i => new OperationPreviewCardGroup(i, [card with { Id = i }], 1, 1, true, false)
                    { Label = "RewardRerollStep:" + i, EarliestFirst = true }).ToImmutableArray();
                ModelKey source = new("RELIC", "DRIFTWOOD");
                var style = new OperationDisplayStyle(OperationTextMode.None, OperationCardLayout.Groups);
                overlay.SetPreview(new(source, [], [], false, [new([], [new(source, [], groups) { Label = "RewardRerollSameRecipe", Style = style }], [], 1, true)], [])
                    { OptionKey = "REROLL", Style = style });
                overlay.SetLayout(new(new(40, 30), new(0, 0)), new(new(120, 100), new(1360, 700)));
                await Frames();
                var captions = ((IEnumerable)Field(overlay, "_stacks")).Cast<object>().Select(s => (string)Field(s, "Caption")).ToArray();
                Check(captions.SequenceEqual(Enumerable.Range(1, 3).Select(i => text.Format("operation.rerollStep", i))), "reroll retains each group and only its concise ordinal caption " + locale);
                await Snapshot("owner-content-" + locale + "-reroll-groups");
                overlay.QueueFree(); await Frames(); shell.Show();
            }
            File.WriteAllText(Path.Combine(output, "owner-content-checks.json"), JsonSerializer.Serialize(new
                { status = "PASS", checks, screenshots, namesEvidence, rerollEvidence = referenceCopyOnly ? "not run" : "presentation fixture; no native reward generation or RNG claim" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            InRunFeaturePreferences.SetLanguage(runtimeDirectory, originalOverride);
            LocManager.Instance.SetLanguage(originalNativeLanguage);
            host.QueueFree(); await Frames();
        }
    }
}
