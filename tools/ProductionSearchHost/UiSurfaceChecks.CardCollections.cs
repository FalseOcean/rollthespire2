using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Presentation.Localization;

internal static partial class UiSurfaceChecks
{
    // Called after the preset host's isolated game/catalog/workspace setup.
    // No search session, seed scan or operation surface is started.
    internal static async Task RunCardCollectionsAsync(Node host, WorkspaceShell shell, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        T Field<T>(object value, string name) => (T)(value.GetType().GetField(name, flags)
            ?? throw new MissingFieldException(value.GetType().Name, name)).GetValue(value)!;
        object? Call(object value, string name, params object?[] args) =>
            (value.GetType().GetMethod(name, flags) ?? throw new MissingMethodException(value.GetType().Name, name)).Invoke(value, args);
        bool editorOnly = OS.GetCmdlineUserArgs().Contains("--card-collection-editor-only", StringComparer.Ordinal);
        int assertions = 0;
        void Require(bool ok, string reason)
        { assertions++; if (!ok) throw new InvalidOperationException("CardCollectionsSurface: " + reason); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Navigate(string name)
        {
            MethodInfo method = shell.GetType().GetMethod("SelectTask", flags)!;
            method.Invoke(shell, [Enum.Parse(method.GetParameters()[0].ParameterType, name), null, null]);
        }

        Directory.CreateDirectory(output);
        var persistence = Field<SearchWorkspace>(shell, "_persistence");
        string stateDirectory = Field<string>(persistence, "_directory");
        string outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output)) + Path.DirectorySeparatorChar;
        Require(Path.GetFullPath(stateDirectory).StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase),
            "UI assets must stay inside this host's output fixture directory");
        var canvas = shell.GetParent<Control>();
        Vector2 originalCanvasSize = canvas.Size;
        Vector2I originalWindowSize = host.GetWindow().Size;
        string? originalGameLanguage = SaveManager.Instance.SettingsSave.Language;
        string? originalUiLanguage = persistence.Preferences.LanguageOverride;
        string originalWorkspace = Field<object>(shell, "_workspace").ToString()!;
        var screenshots = new List<string>();
        var conclusions = new List<string>();
        var searchDiagnostics = new List<object>();
        var cleanupIssues = new List<string>();
        var created = new List<string>();
        SeedLibraryCanvas? library = null;
        CardCollectionStore? store = null;
        CardCollectionEditorOverlay? editor = null;
        QueryWorkbenchFrame? searchFrame = null;
        WorkbenchSearchDraft? originalSearchDraft = null;
        string? originalSearchDomain = null;
        bool succeeded = false;
        Exception? failure = null;

        async Task Ready()
        {
            for (int i = 0; i < 1200 && (!library!.IsVisibleInTree() || !library.IsPresentationReady); i++) await Frames(1);
            Require(library!.IsVisibleInTree() && library.IsPresentationReady && !library.HasPresentationFailure,
                "Library completes its real presentation frames");
            await Frames();
        }
        Button LibraryAction(string name) => library!.FindChild(name, true, false) as Button
            ?? throw new InvalidOperationException("CardCollectionsSurface: missing action " + name);
        void Press(Button button)
        {
            Require(button.IsVisibleInTree() && !button.Disabled, "Action is available: " + button.Name);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        void Fits(Control control, string description)
        {
            Transform2D transform = control.GetGlobalTransformWithCanvas();
            Vector2 start = transform * Vector2.Zero, end = transform * control.Size;
            Rect2 viewport = host.GetViewport().GetVisibleRect();
            Require(start.X >= viewport.Position.X - 1 && start.Y >= viewport.Position.Y - 1 &&
                end.X <= viewport.End.X + 1 && end.Y <= viewport.End.Y + 1 && end.X > start.X && end.Y > start.Y,
                description + " fits the visible viewport");
        }
        async Task Screenshot(string name)
        {
            await Frames(); RenderingServer.ForceDraw();
            string filename = name + ".png";
            Require(host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, filename)) == Error.Ok,
                "Screenshot saves: " + filename);
            screenshots.Add(filename);
        }
        async Task Resize(Vector2I size)
        {
            host.GetWindow().Size = size; canvas.Size = size;
            await Frames(5);
        }
        async Task Select(string id)
        {
            await Ready();
            var item = library!.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>()
                .Single(button => button.GetMeta("entry_id").AsString() == id);
            Press(item); await Ready();
        }
        Button[] Rows(bool selectedSide = false) => Field<Control>(editor!, selectedSide ? "_selectedRows" : "_availableRows")
            .GetChildren().OfType<Button>().Where(button => button.Visible).ToArray();
        int AllocatedRows(bool selectedSide) => Field<Control>(editor!, selectedSide ? "_selectedRows" : "_availableRows")
            .GetChildren().OfType<Button>().Count();
        string KeyOf(Button button) => button.GetMeta("card_key").AsString();
        ModelKey CardKey(CardModel card) => new(card.Id.Category, card.Id.Entry);
        bool AllowedDimensions(CardModel card) =>
            card.Rarity.ToString() is "Common" or "Uncommon" or "Rare" &&
            card.Type.ToString() is "Attack" or "Skill" or "Power";
        HashSet<ModelKey> EligibleKeys() => new(ModelDb.AllCharacters.SelectMany(character =>
            character.CardPool.AllCards.Concat(character.StartingDeck)).Concat(ModelDb.CardPool<ColorlessCardPool>().AllCards)
            .Where(AllowedDimensions).Select(CardKey), ModelKeyComparer.Instance);
        ModelKey[] IndexedKeys(string field) => Field<System.Collections.IEnumerable>(editor!, field)
            .Cast<object>().Select(row => (ModelKey)(row.GetType().GetProperty("Key", flags)
                ?? throw new MissingMemberException("CardRow.Key")).GetValue(row)!).ToArray();
        ModelKey Identity(Button button) => ModelKey.TryParseExact(KeyOf(button), out var key) ? key
            : throw new InvalidOperationException("CardCollectionsSurface: row has no exact identity");
        async Task SearchCards(string query)
        {
            var search = Field<LineEdit>(editor!, "_search");
            search.Text = query;
            // LineEdit.Text does not emit the user-facing signal.
            search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
            await Frames();
        }
        Button[] FilterChoices(string field) => Field<VBoxContainer>(editor!, field).GetChildren().OfType<Button>().ToArray();
        int FilterSelection(string field) => Array.FindIndex(FilterChoices(field), button => button.ButtonPressed);
        async Task SetFilter(string field, int index)
        {
            Button[] choices = FilterChoices(field);
            var scroll = Field<ScrollContainer>(editor!, "_filterScroll");
            scroll.EnsureControlVisible(choices[index]); await Frames();
            Fits(choices[index], "Expanded filter choice " + field + "/" + index);
            Require(scroll.GetGlobalRect().Grow(1).Encloses(choices[index].GetGlobalRect()),
                "A filter choice can be reached inside its own scrolling rail");
            Press(choices[index]);
            await Frames();
            Require(choices.Count(button => button.ButtonPressed) == 1 && FilterSelection(field) == index,
                "Direct choices retain exactly one selected option in each group");
        }
        async Task ToggleCard(ModelKey key)
        {
            if (Field<List<ModelKey>>(editor!, "_selected").Contains(key, ModelKeyComparer.Instance))
            {
                var scroll = Field<ScrollContainer>(editor!, "_selectedScroll");
                scroll.ScrollVertical = 100000; await Frames();
                Press(Rows(true).Single(button => KeyOf(button) == key.Serialized));
                await Frames(); return;
            }
            await SearchCards(key.Serialized);
            Button[] matches = Rows();
            searchDiagnostics.Add(new { Stage = "UserTextChangedSignal", RequestedKey = key.Serialized,
                CatalogCount = Field<System.Collections.IDictionary>(editor!, "_catalog").Count,
                RowKeys = matches.Select(KeyOf).ToArray() });
            Require(matches.Length == 1 && KeyOf(matches[0]) == key.Serialized,
                "Searching exact identity isolates the actual card: " + key.Serialized);
            Press(matches[0]); await Frames();
        }
        void EditorFits()
        {
            Fits(Field<PanelContainer>(editor!, "_panel"), "Editor panel");
            foreach (string field in new[] { "_name", "_save", "_cancel", "_filterScroll", "_availableScroll", "_selectedScroll" })
                Fits(Field<Control>(editor!, field), "Editor " + field);
            var rail = Field<VBoxContainer>(editor!, "_filterRail");
            var filterScroll = Field<ScrollContainer>(editor!, "_filterScroll");
            var availableScroll = Field<ScrollContainer>(editor!, "_availableScroll");
            var selectedScroll = Field<ScrollContainer>(editor!, "_selectedScroll");
            Rect2 railRect = filterScroll.GetGlobalRect(), availableRect = availableScroll.GetGlobalRect(), selectedRect = selectedScroll.GetGlobalRect();
            Require(railRect.Size.X > 0 && railRect.End.X <= availableRect.Position.X + .5f &&
                availableRect.End.X <= selectedRect.Position.X + .5f && availableRect.Size.X > 0 && selectedRect.Size.X > 0 &&
                railRect.Position.Y < availableRect.End.Y && railRect.End.Y > availableRect.Position.Y,
                "The independent filter rail, three-column catalog and selected list appear left to right");
            Require(rail.GetParent() == filterScroll && filterScroll.FollowFocus &&
                !rail.FindChildren("*", "OptionButton", true, false).Any(),
                "All specific choices are expanded controls in an independent focus-following scroll, without dropdowns");
            float previousFilterBottom = rail.GetGlobalRect().Position.Y;
            foreach (string field in new[] { "_search", "_pool", "_rarity", "_type" })
            {
                var filter = Field<Control>(editor!, field);
                Rect2 filterRect = filter.GetGlobalRect();
                Require(filter.GetParent() == rail && rail.GetGlobalRect().Grow(.5f).Encloses(filterRect) &&
                    filterRect.End.X <= availableRect.Position.X + .5f && filterRect.Position.Y >= previousFilterBottom - .5f,
                    "Expanded filter occupies its own vertical group inside the left rail: " + field);
                previousFilterBottom = filterRect.End.Y;
            }
            foreach (string field in new[] { "_pool", "_rarity", "_type" })
                Require(FilterChoices(field).Length > 1 && FilterChoices(field).All(button => button.IsVisibleInTree() && button.ToggleMode) &&
                    FilterChoices(field).Count(button => button.ButtonPressed) == 1,
                    "Every specific option is present without opening or expanding a group: " + field);
            Transform2D editorTransform = editor!.GetGlobalTransformWithCanvas();
            Vector2 editorOrigin = editorTransform * Vector2.Zero;
            float screenScaleX = ((editorTransform * Vector2.Right) - editorOrigin).Length();
            float screenScaleY = ((editorTransform * Vector2.Down) - editorOrigin).Length();
            Rect2 viewportRect = host.GetViewport().GetVisibleRect();
            Require(Math.Abs(screenScaleX - 1) < .01f && Math.Abs(screenScaleY - 1) < .01f &&
                editor.Size.IsEqualApprox(viewportRect.Size) && editorOrigin.DistanceTo(viewportRect.Position) < 1,
                "The modal occupies actual viewport coordinates at scale one, independently of the Shell canvas");
            if (host.GetWindow().Size.X <= 800)
                Require(shell.Scale.X < .99f && Math.Abs(screenScaleX - 1) < .01f,
                    "The actual narrow modal remains unscaled while its real Shell parent is scaled down");
            Require(AllocatedRows(false) <= 60 && AllocatedRows(true) <= 60,
                "Each scrolling surface creates only visible rows and bounded overscan");
            var firstLine = Rows().OrderBy(button => button.Position.Y).ThenBy(button => button.Position.X).Take(3).ToArray();
            Require(firstLine.Length == 3 && Math.Abs(firstLine[0].Position.Y - firstLine[2].Position.Y) < 1 &&
                firstLine[0].Position.X < firstLine[1].Position.X && firstLine[1].Position.X < firstLine[2].Position.X &&
                firstLine[0].GetRect().End.X <= firstLine[1].Position.X + 1 &&
                firstLine[1].GetRect().End.X <= firstLine[2].Position.X + 1,
                "Available cards occupy three distinct non-overlapping columns on an actual row");
            var right = Rows(true);
            Require(right.Length < 2 || right.All(button => Math.Abs(button.Position.X - right[0].Position.X) < 1),
                "Selected cards use one column");
            foreach (var side in new[] { (Views: "_availableViews", Scroll: "_availableScroll"),
                (Views: "_selectedViews", Scroll: "_selectedScroll") })
            {
                var scroll = Field<ScrollContainer>(editor!, side.Scroll);
                int visibleNames = 0, visibleImages = 0;
                float smallestDrawnWidth = float.MaxValue;
                foreach (object view in Field<System.Collections.IEnumerable>(editor!, side.Views))
                {
                    var button = Field<Button>(view, "Button");
                    if (!button.Visible) continue;
                    var title = Field<Label>(view, "Title");
                    var detail = Field<Label>(view, "Detail");
                    var icon = Field<TextureRect>(view, "Icon");
                    Rect2 titleRect = title.GetGlobalRect(), detailRect = detail.GetGlobalRect();
                    string evidence = KeyOf(button) + "; title=" + title.Text + "; titleRect=" + titleRect +
                        "; detailRect=" + detailRect + "; buttonRect=" + button.GetGlobalRect();
                    Transform2D titleTransform = title.GetGlobalTransformWithCanvas();
                    float titleScale = ((titleTransform * Vector2.Down) - (titleTransform * Vector2.Zero)).Length();
                    Require(Math.Abs(titleScale - 1) < .01f && title.GetThemeFontSize("font_size") * titleScale >= 15.5f,
                        "The card name keeps a normal screen-space font size, including at 800×600: " + evidence);
                    Require(!string.IsNullOrWhiteSpace(title.Text) && title.IsVisibleInTree() &&
                        title.Size.X > 0 && title.Size.Y > 0 && button.GetGlobalRect().Grow(.5f).Encloses(titleRect),
                        "A rendered card name has positive width/height inside its button: " + evidence);
                    Require(!string.IsNullOrWhiteSpace(detail.Text) && detail.Size.X > 0 && detail.Size.Y > 0 &&
                        detail.GetThemeFontSize("font_size") * titleScale >= 12.5f &&
                        titleRect.End.Y <= detailRect.Position.Y + .5f && !titleRect.Intersects(detailRect),
                        "The card name and metadata occupy separate visible rows: " + evidence);
                    bool onScreen = scroll.GetGlobalRect().Encloses(button.GetGlobalRect());
                    if (icon.Texture is { } texture)
                    {
                        Vector2 sourceSize = texture.GetSize();
                        Rect2 iconRect = icon.GetGlobalRect();
                        Require(sourceSize.X > 0 && sourceSize.Y > 0 && icon.Size.X > 0 && icon.Size.Y > 0 &&
                            icon.StretchMode is TextureRect.StretchModeEnum.KeepAspect or TextureRect.StretchModeEnum.KeepAspectCentered,
                            "A nonempty card texture uses proportional drawing: " + evidence);
                        float fit = Math.Min(iconRect.Size.X / sourceSize.X, iconRect.Size.Y / sourceSize.Y);
                        Vector2 drawnSize = sourceSize * fit;
                        Vector2 drawOrigin = iconRect.Position + (icon.StretchMode == TextureRect.StretchModeEnum.KeepAspectCentered
                            ? (iconRect.Size - drawnSize) / 2 : Vector2.Zero);
                        Rect2 drawnRect = new(drawOrigin, drawnSize);
                        Require(drawnSize.X >= 26 * 1.5f && drawnSize.Y > 0 &&
                            button.GetGlobalRect().Grow(.5f).Encloses(drawnRect) &&
                            !drawnRect.Intersects(titleRect) && !drawnRect.Intersects(detailRect),
                            "Actual proportional card art is visibly larger than the old 26px icon and separate from text; drawn=" + drawnSize + "; " + evidence);
                        if (onScreen) { visibleImages++; smallestDrawnWidth = Math.Min(smallestDrawnWidth, drawnSize.X); }
                    }
                    if (onScreen)
                    {
                        Require(scroll.GetGlobalRect().Encloses(titleRect),
                            "The card name of an on-screen row is inside the scroll viewport: " + evidence);
                        visibleNames++;
                    }
                }
                bool populated = (side.Views == "_selectedViews" ? Rows(true).Length : Rows().Length) > 0;
                Require(!populated || visibleNames > 0, "A populated surface has an actual on-screen card name: " + side.Scroll);
                Require(!populated || visibleImages > 0, "A populated real-card surface has enlarged on-screen card art: " + side.Scroll);
                if (visibleImages > 0) searchDiagnostics.Add(new { Stage = "EnlargedArtGeometry", Surface = side.Scroll,
                    Window = host.GetWindow().Size.ToString(), VisibleImages = visibleImages, SmallestDrawnWidth = smallestDrawnWidth });
            }
        }

        async Task EditorExperience(string language)
        {
            var catalog = Field<System.Collections.IDictionary>(editor!, "_catalog");
            var actualCards = ModelDb.AllCards.ToDictionary(card => new ModelKey(card.Id.Category, card.Id.Entry), ModelKeyComparer.Instance);
            Require(new HashSet<ModelKey>(catalog.Keys.Cast<ModelKey>(), ModelKeyComparer.Instance).SetEquals(actualCards.Keys),
                "The identity lookup retains registered cards for legacy member presentation");
            var eligible = EligibleKeys();
            Require(eligible.Count > 24 && new HashSet<ModelKey>(IndexedKeys("_sortedCatalog"), ModelKeyComparer.Instance).SetEquals(eligible) &&
                new HashSet<ModelKey>(IndexedKeys("_filtered"), ModelKeyComparer.Instance).SetEquals(eligible),
                "Only registered character/strict-colorless cards satisfying all rarity and type dimensions can be newly added");
            Require(eligible.All(key => AllowedDimensions(actualCards[key])), "Every independently eligible identity satisfies both metadata dimensions");
            var selected = Field<List<ModelKey>>(editor!, "_selected");
            ModelKey[] originalMembers = selected.ToArray();
            var pool = FilterChoices("_pool");
            int PoolIndex(string value) => Enumerable.Range(0, pool.Length).Single(index => pool[index].GetMeta("filter_value").AsString() == value);
            var ironclad = ModelDb.AllCharacters.Single(character => character.Id.Entry == "IRONCLAD");
            var silent = ModelDb.AllCharacters.Single(character => character.Id.Entry == "SILENT");
            int ironcladIndex = PoolIndex("character:" + new ModelKey(ironclad.Id.Category, ironclad.Id.Entry).Serialized);
            int silentIndex = PoolIndex("character:" + new ModelKey(silent.Id.Category, silent.Id.Entry).Serialized);
            var strictColorless = ModelDb.CardPool<ColorlessCardPool>();
            string colorlessOption = "pool:" + new ModelKey(strictColorless.Id.Category, strictColorless.Id.Entry).Serialized;
            var expectedPools = ModelDb.AllCharacters.Select(character => "character:" + new ModelKey(character.Id.Category, character.Id.Entry).Serialized)
                .Append(colorlessOption).Prepend("").ToArray();
            string[] actualPools = pool.Select(button => button.GetMeta("filter_value").AsString()).ToArray();
            Require(actualPools.Length == expectedPools.Length && new HashSet<string>(actualPools, StringComparer.Ordinal).SetEquals(expectedPools),
                "Pool options contain only All, registered character pools and the strict colorless pool");
            foreach (var character in ModelDb.AllCharacters)
            {
                string option = "character:" + new ModelKey(character.Id.Category, character.Id.Entry).Serialized;
                var expected = character.CardPool.AllCards.Concat(character.StartingDeck).Where(AllowedDimensions).Select(CardKey);
                await SetFilter("_pool", PoolIndex(option));
                Require(new HashSet<ModelKey>(IndexedKeys("_filtered"), ModelKeyComparer.Instance).SetEquals(expected) &&
                    selected.SequenceEqual(originalMembers), "Each registered character option selects exactly its eligible members: " + option);
            }
            await SetFilter("_pool", PoolIndex(colorlessOption));
            Require(new HashSet<ModelKey>(IndexedKeys("_filtered"), ModelKeyComparer.Instance)
                .SetEquals(strictColorless.AllCards.Where(AllowedDimensions).Select(CardKey)),
                "The colorless option is exactly the strict colorless pool intersected with the allowed metadata");
            await SetFilter("_pool", 0);
            searchDiagnostics.Add(new { Stage = "EligibleCatalog", Language = language, RegisteredIdentities = actualCards.Count,
                EligibleCount = eligible.Count, PoolOptions = actualPools,
                ExcludedQuestKeys = actualCards.Values.Where(card => card.Rarity.ToString() == "Quest").Select(card => CardKey(card).Serialized).ToArray() });
            foreach (var fixture in new[] { (Index: ironcladIndex, Own: originalMembers[0], Other: originalMembers[1]),
                (Index: silentIndex, Own: originalMembers[1], Other: originalMembers[0]) })
            {
                await SetFilter("_pool", fixture.Index); await SearchCards(fixture.Own.Serialized);
                Require(Rows().Length == 1 && Identity(Rows()[0]) == fixture.Own,
                    "Character filter includes its real eligible pool member");
                await SearchCards(fixture.Other.Serialized);
                Require(Rows().Length == 0 && selected.SequenceEqual(originalMembers),
                    "Character filtering hides the other character without deleting draft members");
            }
            await SetFilter("_pool", 0); await SearchCards("");
            foreach (string field in new[] { "_rarity", "_type" })
            {
                string[] codes = Field<string[]>(editor!, field == "_rarity" ? "_rarities" : "_types");
                string[] allowed = field == "_rarity" ? ["Common", "Uncommon", "Rare"] : ["Attack", "Skill", "Power"];
                Require(FilterChoices(field).Length == 4 && codes.Length == 3 &&
                    new HashSet<string>(codes, StringComparer.Ordinal).SetEquals(allowed),
                    "The " + field + " options contain All and exactly the three allowed values");
                for (int index = 1; index <= codes.Length; index++)
                {
                    await SetFilter(field, index);
                    string code = codes[index - 1];
                    var expected = eligible.Where(key => (field == "_rarity" ? actualCards[key].Rarity.ToString() : actualCards[key].Type.ToString()) == code);
                    Require(new HashSet<ModelKey>(IndexedKeys("_filtered"), ModelKeyComparer.Instance).SetEquals(expected) &&
                        Rows().All(button => eligible.Contains(Identity(button))) && selected.SequenceEqual(originalMembers),
                        "The real " + field + " option filters only eligible identities and retains the draft: " + code);
                }
                await SetFilter(field, 0);
            }
            CardModel sharedCard = strictColorless.AllCards.First(AllowedDimensions);
            ModelKey sharedKey = CardKey(sharedCard);
            await SetFilter("_pool", PoolIndex(colorlessOption));
            await SearchCards(sharedKey.Serialized);
            Require(Rows().Length == 1 && Identity(Rows()[0]) == sharedKey,
                "The strict colorless option includes an eligible actual game card");
            await SearchCards(originalMembers[0].Serialized);
            Require(Rows().Length == 0 && selected.SequenceEqual(originalMembers),
                "A colorless filter excludes an unrelated character card and preserves cross-character members");
            await SetFilter("_pool", 0);
            foreach (CardModel excluded in new[] { actualCards.Values.First(card => card.Rarity.ToString() == "Quest"),
                actualCards.Values.First(card => card.Id.Entry == "BASH") })
            {
                await SearchCards(CardKey(excluded).Serialized);
                Require(Rows().Length == 0 && catalog.Contains(CardKey(excluded)) && selected.SequenceEqual(originalMembers),
                    "An out-of-range identity remains in legacy lookup but cannot be newly added: " + CardKey(excluded));
            }
            await SearchCards("__card_collection_no_match__");
            Require(Rows().Length == 0 && selected.SequenceEqual(originalMembers), "A zero-result search never removes selected identities");
            await SearchCards("");
            var left = Field<ScrollContainer>(editor!, "_availableScroll");
            string[] initialKeys = Rows().Select(KeyOf).ToArray();
            Require(catalog.Count > 24, "The actual catalog is larger than the former 24-card page");
            left.ScrollVertical = 100000; await Frames(); EditorFits();
            Require(left.ScrollVertical > 0 && Rows().Any(button => button.GetMeta("catalog_index").AsInt32() >= 24) &&
                Rows().Any(button => !initialKeys.Contains(KeyOf(button), StringComparer.Ordinal)),
                "Scrolling reaches cards beyond the old first page without creating the whole directory");
            searchDiagnostics.Add(new { Stage = "VirtualBrowse", Language = language, CatalogCount = catalog.Count,
                EligibleCount = eligible.Count, LiveAvailableButtons = Rows().Length, AllocatedAvailableButtons = AllocatedRows(false), Scroll = left.ScrollVertical, RowKeys = Rows().Select(KeyOf).ToArray() });
            left.ScrollVertical = 0; await Frames();
            await Screenshot("card-collection-editor-browse-" + language);

            // Keep a meaningful role filter, nonempty identity search and a scrolled list
            // while adding twice. Signals use the real product button handlers.
            await SetFilter("_pool", ironcladIndex); await SearchCards("CARD:");
            left.ScrollVertical = Math.Max(1, (int)(left.Size.Y / 2)); await Frames();
            Require(left.ScrollVertical > 0, "Filtered character catalog still scrolls");
            var additions = Rows().Where(button => !button.Disabled && left.GetGlobalRect().Encloses(button.GetGlobalRect())).Take(2).ToArray();
            Require(additions.Length == 2, "Two unselected real cards are visible for continuous addition");
            var stableRows = Rows().Select(button => (Button: button, Key: KeyOf(button), Position: button.Position)).ToArray();
            int scrollBefore = left.ScrollVertical, poolBefore = FilterSelection("_pool");
            int rarityBefore = FilterSelection("_rarity"), typeBefore = FilterSelection("_type");
            Control focus = Field<LineEdit>(editor!, "_name");
            foreach (Button addition in additions)
            {
                if (addition == additions[1]) focus = Field<LineEdit>(editor!, "_search");
                focus.GrabFocus(); await Frames();
                Press(addition); await Frames();
                Require(addition.Disabled && selected.Count(key => key == Identity(addition)) == 1,
                    "Added card stays disabled at its original available position and occurs once");
                foreach (var prior in stableRows)
                    Require(GodotObject.IsInstanceValid(prior.Button) && Rows().Contains(prior.Button) &&
                        KeyOf(prior.Button) == prior.Key && prior.Button.Position.IsEqualApprox(prior.Position),
                        "Continuous addition preserves the original available button instance and position");
                Require(left.ScrollVertical == scrollBefore && Field<LineEdit>(editor!, "_search").Text == "CARD:" &&
                    FilterSelection("_pool") == poolBefore && FilterSelection("_rarity") == rarityBefore &&
                    FilterSelection("_type") == typeBefore && host.GetViewport().GuiGetFocusOwner() == focus,
                    "Addition retains left scroll, search, all filters and the existing text focus");
                var right = Field<ScrollContainer>(editor!, "_selectedScroll");
                Require(Rows(true).Any(button => KeyOf(button) == KeyOf(addition) && right.GetGlobalRect().Encloses(button.GetGlobalRect())),
                    "The newly appended selected card is visible immediately");
                // Even a manually emitted signal cannot weaken disabled duplicate protection.
                addition.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Require(selected.Count(key => key == Identity(addition)) == 1, "Disabled left member cannot be duplicated or toggled off");
            }
            Require(selected.SequenceEqual(originalMembers.Concat(additions.Select(Identity))), "Continuous additions append in click order");
            Press(Rows(true).Single(button => KeyOf(button) == KeyOf(additions[0]))); await Frames();
            Require(!additions[0].Disabled && !selected.Contains(Identity(additions[0])) &&
                left.ScrollVertical == scrollBefore && Field<LineEdit>(editor!, "_search").Text == "CARD:" &&
                FilterSelection("_pool") == poolBefore && FilterSelection("_rarity") == rarityBefore &&
                FilterSelection("_type") == typeBefore && host.GetViewport().GuiGetFocusOwner() == focus,
                "Removing on the right restores the same available button without disturbing the left list or focus");
            Require(catalog.Count == actualCards.Count, "Filtering and membership edits never shrink the registered catalog");
            await Screenshot("card-collection-editor-continuous-" + language);
            await SetFilter("_pool", 0); await SearchCards("");
            await Screenshot("card-collection-editor-selected-" + language);
            await Resize(new(800, 600)); EditorFits();
            Field<ScrollContainer>(editor!, "_filterScroll").ScrollVertical = 0; await Frames();
            await Screenshot("card-collection-editor-browse-small-" + language);
            ModelKey[] membersBeforeFilters = selected.ToArray();
            foreach (string field in new[] { "_pool", "_rarity", "_type" })
            {
                await SetFilter(field, FilterChoices(field).Length - 1);
                await SetFilter(field, 0);
            }
            int catalogScroll = left.ScrollVertical;
            FilterChoices("_type")[3].GrabFocus(); await Frames();
            var filterScroll = Field<ScrollContainer>(editor!, "_filterScroll");
            Require(filterScroll.ScrollVertical > 0 && filterScroll.GetGlobalRect().Grow(1).Encloses(FilterChoices("_type")[3].GetGlobalRect()),
                "Focus reaches the narrow filter's last option: scroll=" + filterScroll.ScrollVertical +
                "; rail=" + filterScroll.GetGlobalRect() + "; choice=" + FilterChoices("_type")[3].GetGlobalRect());
            Require(left.ScrollVertical == catalogScroll && selected.SequenceEqual(membersBeforeFilters),
                "Filter scrolling/focus is independent of catalog scrolling and keeps the same draft identities");
            await Screenshot("card-collection-editor-filters-small-" + language);
            Field<LineEdit>(editor!, "_name").GrabFocus(); await Frames();
            await Resize(new(1600, 900));

            // Persist a real older asset containing a Basic, a Quest and one unavailable
            // identity. The editor must keep them even though none can be added now.
            ModelKey missing = new("CARD", "UI_FIXTURE_MISSING_" + Guid.NewGuid().ToString("N"));
            ModelKey oldBasic = CardKey(actualCards.Values.Single(card => card.Id.Entry == "BASH"));
            ModelKey oldQuest = CardKey(actualCards.Values.First(card => card.Rarity.ToString() == "Quest"));
            ModelKey[] legacyKeys = [oldBasic, oldQuest, missing];
            ModelKey[] retainedEligible = eligible.Take(32).ToArray();
            ModelKey[] manyMembers = retainedEligible.Concat(legacyKeys).ToArray();
            editor!.Cancel();
            CardCollectionSnapshot legacy = store!.Save(null, "UI legacy members · " + language, manyMembers);
            created.Add(legacy.Id);
            library!.Refresh(language); library.ShowSection(SeedLibraryCanvas.LibrarySection.CardCollections);
            await Select(legacy.Id); Press(LibraryAction("EditCardCollection")); await Frames();
            selected = Field<List<ModelKey>>(editor!, "_selected");
            Require(selected.SequenceEqual(manyMembers), "Opening a saved older asset retains every eligible, excluded and missing member");
            var selectedScroll = Field<ScrollContainer>(editor!, "_selectedScroll");
            selectedScroll.ScrollVertical = 100000; await Frames();
            Require(selectedScroll.ScrollVertical > 0 && AllocatedRows(true) <= 60,
                "The selected surface scrolls and virtualizes an older longer draft");
            foreach (ModelKey legacyKey in legacyKeys)
            {
                Require(Rows(true).Any(button => Identity(button) == legacyKey), "The legacy member has a removable right-side row: " + legacyKey);
                object view = Field<System.Collections.IEnumerable>(editor!, "_selectedViews").Cast<object>()
                    .Single(value => Field<Button>(value, "Button").Visible && Identity(Field<Button>(value, "Button")) == legacyKey);
                Require(Field<Label>(view, "Title").Text == (actualCards.TryGetValue(legacyKey, out var actual) ? actual.Title : legacyKey.Serialized),
                    "Legacy lookup preserves the actual localized card name or the unavailable authored identity: " + legacyKey);
            }
            await SearchCards("__card_collection_no_match__");
            Require(selected.SequenceEqual(manyMembers), "Filtering cannot remove any out-of-range or unavailable saved member");
            await Screenshot("card-collection-editor-legacy-" + language);
            Press(Field<Button>(editor!, "_save")); await Ready();
            Require(store.TryGet(legacy.Id, out var preservedLegacy) && preservedLegacy.Cards.SequenceEqual(manyMembers) &&
                new CardCollectionStore(stateDirectory).TryGet(legacy.Id, out var persistedLegacy) && persistedLegacy.Cards.SequenceEqual(manyMembers),
                "Saving a filtered older asset preserves all members in both memory and the reopened disk store");
            async Task ReopenLegacy()
            {
                await Select(legacy.Id); Press(LibraryAction("EditCardCollection")); await Frames();
                selected = Field<List<ModelKey>>(editor!, "_selected");
                selectedScroll = Field<ScrollContainer>(editor!, "_selectedScroll");
            }
            async Task RemoveLegacyMembers()
            {
                foreach (ModelKey legacyKey in legacyKeys)
                {
                    selectedScroll.ScrollVertical = 100000; await Frames();
                    Press(Rows(true).Single(button => Identity(button) == legacyKey)); await Frames();
                    Require(!selected.Contains(legacyKey), "A legacy member is removed only by an explicit right-side action: " + legacyKey);
                }
            }
            await ReopenLegacy();
            Require(selected.SequenceEqual(manyMembers), "Reopening after Save restores all older members");
            await RemoveLegacyMembers();
            Press(Field<Button>(editor!, "_cancel")); await Ready();
            Require(store.TryGet(legacy.Id, out var cancelledLegacy) && cancelledLegacy.Cards.SequenceEqual(manyMembers),
                "Cancelling explicit legacy-member removals leaves the saved asset intact");
            await ReopenLegacy();
            Require(selected.SequenceEqual(manyMembers), "Reopening after Cancel retains excluded and unavailable identities");
            await RemoveLegacyMembers();
            Press(Field<Button>(editor!, "_save")); await Ready();
            Require(store.TryGet(legacy.Id, out var removedLegacy) && removedLegacy.Cards.SequenceEqual(retainedEligible),
                "Saving explicit right-side removals changes only those authored member identities");
            await ReopenLegacy();
            Require(selected.SequenceEqual(retainedEligible), "Reopening restores the explicitly edited legacy asset");
            ModelKey extra = eligible.First(key => !selected.Contains(key));
            selectedScroll.ScrollVertical = 0; await Frames();
            await SearchCards(extra.Serialized); Press(Rows().Single()); await Frames(5);
            Require(selected.Last() == extra && selectedScroll.ScrollVertical > 0 && Rows(true).Any(button =>
                Identity(button) == extra && selectedScroll.GetGlobalRect().Encloses(button.GetGlobalRect())),
                "Adding an eligible card to a longer draft automatically reveals its last selected row");
            await SearchCards(""); EditorFits();
            await Screenshot("card-collection-editor-selected-scroll-" + language);
            await Resize(new(800, 600)); EditorFits();
            await Screenshot("card-collection-editor-selected-small-" + language);
            conclusions.Add(language + ": character/strict-colorless eligibility, exact three-rarity/three-type options, enlarged proportional card art, stable scrolling additions and persisted legacy-member save/cancel/reopen/removal passed.");
        }

        async Task SearchCollection(string language, CardCollectionSnapshot collection)
        {
            await Resize(new(1600, 900));
            Press(Field<Button>(shell, "_search")); await AwaitPageReady(host, shell);
            searchFrame ??= Field<QueryWorkbenchFrame>(shell, "_references");
            originalSearchDraft ??= searchFrame.CaptureDraft();
            originalSearchDomain ??= Field<string>(searchFrame, "_selectedDomain");
            Require(!searchFrame.HasActiveSearch, "Collection authoring starts without a running search");
            // Use a clean solo condition page in this isolated fixture. The collection
            // itself is selected through the product control, never injected here.
            searchFrame.RestoreDraft(new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
                SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault), render: false);
            searchFrame.Refresh(language, JsonUiTextProvider.CreateUi13(language));
            Press((Button)searchFrame.FindChild("SearchCategory_combat", true, false)); await Frames();
            var combat = Field<CombatRewardSearchEditor>(searchFrame, "_combatEditor");
            Require(combat.IsVisibleInTree(), "The real combat reward condition page is visible");
            T Control<T>(string name) where T : Godot.Control => combat.FindChild(name, true, false) as T
                ?? throw new InvalidOperationException("CardCollectionsSurface: missing Search control " + name);
            async Task ScrollToExclusion()
            {
                await Frames();
                var scroll = Field<Godot.Control>(combat, "_body").GetChildren().OfType<ScrollContainer>().Single();
                scroll.ScrollVertical = 100000;
                await Frames();
                foreach (Godot.Control control in new Godot.Control[] { Control<OptionButton>("CombatCardExclusionCollection"),
                    Control<Button>("CombatCardExclusionIncrease"), Control<Button>("CombatCardExclusionDecrease") })
                {
                    Fits(control, "Search " + control.Name);
                    Require(scroll.GetGlobalRect().Encloses(control.GetGlobalRect()), "Search control is inside its visible scroll area: " + control.Name);
                }
            }
            await ScrollToExclusion();
            var selector = Control<OptionButton>("CombatCardExclusionCollection");
            Require(selector.GetItemText(0) == (language == "zh" ? "未选牌集 · 关闭" : "No collection · off"),
                "Search collection selector follows the chosen UI language");
            int selectedIndex = Enumerable.Range(1, selector.ItemCount - 1).Single(index =>
                selector.GetItemText(index).Contains(collection.Name, StringComparison.Ordinal));
            Require(!selector.IsItemDisabled(selectedIndex), "The Library-created collection is available in Search");
            selector.Select(selectedIndex);
            selector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)selectedIndex);
            await ScrollToExclusion();
            void CheckRange(int count, string stage)
            {
                var draft = searchFrame.CaptureDraft();
                var visibleRange = Control<Button>("CombatCardExclusionIncrease").GetParent().GetChildren().OfType<Label>()
                    .Single(label => int.TryParse(label.Text, out _));
                Require(visibleRange.Text == count.ToString(), stage + " shows the current range beside the +/− buttons");
                Fits(visibleRange, "Search visible battle range");
                Require(draft.Query.CombatCardExclusion is { } condition && condition.BattleCount == count &&
                    new HashSet<ModelKey>(condition.CardKeys, ModelKeyComparer.Instance).SetEquals(collection.Cards) &&
                    draft.CombatRewardEditor?.ExcludedCollection is { } snapshot && snapshot.Id == collection.Id && snapshot.Revision == collection.Revision,
                    stage + " exports the selected collection snapshot and visible battle range");
                Require(!searchFrame.HasActiveSearch, "Authoring never starts a search");
                searchDiagnostics.Add(new { Stage = stage, Language = language, CollectionId = collection.Id,
                    Revision = collection.Revision, BattleCount = count,
                    VisibleRange = visibleRange.Text,
                    SelectedLabel = Control<OptionButton>("CombatCardExclusionCollection").GetItemText(0),
                    CardKeys = draft.Query.CombatCardExclusion!.CardKeys.Select(key => key.Serialized).ToArray() });
            }
            CheckRange(3, "SearchCollectionSelected");
            Press(Control<Button>("CombatCardExclusionIncrease")); await ScrollToExclusion();
            CheckRange(4, "SearchCollectionRangeIncreased");
            Press(Control<Button>("CombatCardExclusionDecrease")); await ScrollToExclusion();
            CheckRange(3, "SearchCollectionRangeDecreased");
            await Screenshot("card-collections-search-" + language);
        }

        try
        {
            await Resize(new(1600, 900));
            persistence.SetLanguageOverride("zh"); Call(shell, "RefreshLanguage");
            Navigate("Seeds"); Call(shell, "EnsureSeedLibrary");
            library = Field<SeedLibraryCanvas>(shell, "_seedLibrary");
            library.ShowSection(SeedLibraryCanvas.LibrarySection.CardCollections); await Ready();
            store = Field<CardCollectionStore>(library, "_cardCollections");
            editor = Field<CardCollectionEditorOverlay>(library, "_collectionEditor");
            int initialCount = store.GetAll().Count;
            CardModel CharacterFixture(string entry) => ModelDb.AllCharacters.Single(character => character.Id.Entry == entry)
                .CardPool.AllCards.First(AllowedDimensions);
            ModelKey first = CardKey(CharacterFixture("IRONCLAD")), second = CardKey(CharacterFixture("SILENT"));
            Require(first != second && EligibleKeys().Contains(first) && EligibleKeys().Contains(second),
                "Both cross-character fixture cards are real currently eligible pool members");

            Press(LibraryAction("NewCardCollection")); await Frames();
            Require(editor.IsOpen, "New collection opens the shared editor");
            Field<LineEdit>(editor, "_name").Text = "跨角色牌集 · UI fixture";
            await ToggleCard(first); await ToggleCard(second);
            Require(Field<List<ModelKey>>(editor, "_selected").SequenceEqual(new[] { first, second }),
                "Continuous card selection retains both characters in one draft");
            Press(Field<Button>(editor, "_cancel")); await Ready();
            Require(!editor.IsOpen && store.GetAll().Count == initialCount, "Cancelling a new draft writes no collection");

            Press(LibraryAction("NewCardCollection")); await Frames();
            Field<LineEdit>(editor, "_name").Text = "跨角色牌集 · UI fixture";
            await ToggleCard(first); await ToggleCard(second);
            Press(Field<Button>(editor, "_save")); await Ready();
            CardCollectionSnapshot saved = store.GetAll().Single(collection => collection.Name == "跨角色牌集 · UI fixture");
            created.Add(saved.Id);
            Require(!editor.IsOpen && saved.Revision == 1 && saved.Cards.SequenceEqual(new[] { first, second }),
                "Saving through Library commits both card identities to the actual asset store");
            var reopenedStore = new CardCollectionStore(stateDirectory);
            Require(reopenedStore.TryGet(saved.Id, out var diskSaved) && diskSaved.Cards.SequenceEqual(saved.Cards),
                "The UI-created collection survives reopening its disk store");
            conclusions.Add("A cross-character collection can be created and reopened; cancelling the draft writes nothing.");

            await Select(saved.Id); Press(LibraryAction("EditCardCollection")); await Frames();
            Field<LineEdit>(editor, "_name").Text = "cancelled rename"; await ToggleCard(first);
            editor._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Ready();
            Require(!editor.IsOpen && store.TryGet(saved.Id, out var afterCancel) && afterCancel.Name == saved.Name &&
                afterCancel.Revision == 1 && afterCancel.Cards.SequenceEqual(saved.Cards),
                "Escape discards both member and name changes from an existing draft");
            Press(LibraryAction("EditCardCollection")); await Frames();
            Field<LineEdit>(editor, "_name").Text = "已编辑牌集 · Edited collection";
            Press(Field<Button>(editor, "_save")); await Ready();
            Require(store.TryGet(saved.Id, out var edited) && edited.Revision == 2 && edited.Name == "已编辑牌集 · Edited collection" &&
                edited.Cards.SequenceEqual(saved.Cards), "Saving an edit changes the same asset at the next revision");
            await Select(saved.Id); Press(LibraryAction("DuplicateCardCollection")); await Frames();
            Field<LineEdit>(editor, "_name").Text = "UI fixture copy";
            Press(Field<Button>(editor, "_save")); await Ready();
            CardCollectionSnapshot copy = store.GetAll().Single(collection => collection.Name == "UI fixture copy");
            created.Add(copy.Id);
            Require(copy.Id != saved.Id && copy.Revision == 1 && copy.Cards.SequenceEqual(saved.Cards), "Duplicate saves an independent identity");
            await Select(copy.Id); Press(LibraryAction("DeleteCardCollection")); await Frames();
            var confirmation = Field<SearchConfirmationTransactionOverlay>(library, "_confirmation");
            Require(confirmation.IsOpen, "Delete presents its confirmation before modifying storage");
            Press(Field<Button>(confirmation, "_confirm")); await Ready();
            Require(!store.TryGet(copy.Id, out _) && store.TryGet(saved.Id, out var originalAfterCopyDelete) && originalAfterCopyDelete.Revision == 2,
                "Deleting a duplicate preserves the edited source collection");
            conclusions.Add("Editing advances revision; Escape preserves the prior asset; duplicate and confirmed delete preserve the source.");

            foreach (string language in new[] { "zh", "en" })
            {
                SaveManager.Instance.SettingsSave.Language = language == "zh" ? "zhs" : "eng";
                MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
                persistence.SetLanguageOverride(language); Call(shell, "RefreshLanguage");
                await Resize(new(1600, 900)); Navigate("Seeds");
                // UI override can already equal this locale. Rebind the page's
                // game-name resolver after changing the actual game language.
                library.Refresh(language);
                library.ShowSection(SeedLibraryCanvas.LibrarySection.CardCollections); await Select(saved.Id);
                string[] expectedTitles = saved.Cards.Select(key => ModelDb.AllCards.Single(card =>
                    card.Id.Category == key.Category && card.Id.Entry == key.Entry).Title).ToArray();
                string[] detailLabels = Field<VBoxContainer>(library, "_detail").FindChildren("*", "Label", true, false)
                    .OfType<Label>().Select(label => label.Text).ToArray();
                Require(expectedTitles.All(title => detailLabels.Contains(title, StringComparer.Ordinal)),
                    "Library detail displays current game-owned card titles in " + language);
                searchDiagnostics.Add(new { Stage = "LibraryLocalizedDetail", Language = language,
                    ExpectedCardTitles = expectedTitles, DetailLabels = detailLabels });
                Require(LibraryAction("NewCardCollection").Text == (language == "zh" ? "新建牌集" : "New collection"),
                    "Library action follows the chosen UI language");
                Fits(LibraryAction("NewCardCollection"), "New action"); Fits(LibraryAction("EditCardCollection"), "Edit action");
                Fits(LibraryAction("DeleteCardCollection"), "Delete action");
                await Screenshot("card-collections-library-" + language);
                Press(LibraryAction("EditCardCollection")); await Frames(); EditorFits();
                Require(Field<Button>(editor, "_save").Text == (language == "zh" ? "保存" : "Save"), "Editor action follows the chosen language");
                await Screenshot("card-collections-editor-" + language);
                await EditorExperience(language);
                await Screenshot("card-collections-editor-small-" + language);
                Press(Field<Button>(editor, "_cancel")); await Ready();
                Fits(LibraryAction("NewCardCollection"), "Small viewport new action");
                Fits(LibraryAction("EditCardCollection"), "Small viewport edit action");
                Fits(LibraryAction("DeleteCardCollection"), "Small viewport delete action");
                await Screenshot("card-collections-library-small-" + language);
                if (!editorOnly) await SearchCollection(language, edited);
            }
            conclusions.Add("Chinese and English Library/editor controls fit and remain available at 1600×900 and 800×600.");
            if (!editorOnly) conclusions.Add("The Library-created collection can be selected from Search → combat rewards; visible +/− controls export 3→4→3 combats in Chinese and English at 1600×900, without starting a search.");
            succeeded = true;
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            void Restore(Action action, string stage)
            { try { action(); } catch (Exception exception) { cleanupIssues.Add(stage + ": " + exception); } }
            Restore(() => { if (editor?.IsOpen == true) editor.Cancel(); }, "Close editor");
            Restore(() => { if (searchFrame is not null && originalSearchDraft is not null)
                { searchFrame.RestoreDraft(originalSearchDraft, render: false);
                    if (originalSearchDomain is not null) Call(searchFrame, "SelectDomain", originalSearchDomain); } }, "Restore Search draft and category");
            Restore(() => { SaveManager.Instance.SettingsSave.Language = originalGameLanguage;
                MegaCrit.Sts2.Core.Localization.LocManager.Initialize(); }, "Restore game language");
            Restore(() => { persistence.SetLanguageOverride(originalUiLanguage); Call(shell, "RefreshLanguage"); }, "Restore UI language");
            Restore(() => { canvas.Size = originalCanvasSize; host.GetWindow().Size = originalWindowSize;
                Navigate(originalWorkspace); }, "Restore window and navigation");
            File.WriteAllText(Path.Combine(output, "card-collections-ui.json"), JsonSerializer.Serialize(new
            {
                Passed = succeeded,
                Assertions = assertions, EditorOnly = editorOnly,
                Scope = editorOnly ? "Library CRUD; always-expanded single-choice filters with independent scroll/focus; character/strict-colorless plus Common/Uncommon/Rare and Attack/Skill/Power eligibility; persisted legacy members; enlarged proportional art; real normal/narrow bilingual editor; no Search authoring or execution"
                    : "Library CRUD, restricted eligible additions, persisted legacy members, enlarged proportional art, bilingual normal/narrow editor and Search combat-reward authoring; no prediction/search execution",
                Conclusions = conclusions, Screenshots = screenshots, CreatedCollectionIds = created, SearchDiagnostics = searchDiagnostics,
                StateDirectory = stateDirectory, Failure = failure?.ToString(), CleanupIssues = cleanupIssues
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
