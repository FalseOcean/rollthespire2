using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Workspace;

internal static class NeowOfferEditorChecks
{
    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("NeowOfferEditor");
        var editor = new AncientSearchEditor(runtime) { Size = new(1000, 760) };
        var reopened = new AncientSearchEditor(runtime) { Size = new(1000, 760) };
        int checks = 0;
        void Check(bool ok, string reason) { checks++; if (!ok) throw new InvalidDataException("NeowOfferEditor." + reason); }
        var pearl = BaseGameModelKeys.Relics.GoldenPearl;
        var arcane = BaseGameModelKeys.Relics.ArcaneScroll;
        var curse = BaseGameModelKeys.Relics.CursedPearl;
        var q = SearchQuery.Empty with { NeowOfferWhitelist = new([pearl, arcane, curse]) };
        var saved = new AncientEditorStateSnapshot(0, 2, [new([], false, [], 1), new([], false, [], 1), new([], false, [], 1)])
        { NeowOffers = new(true, 2, [pearl], [[pearl, arcane], [pearl, arcane], [pearl, arcane, curse], [curse]]) };
        try
        {
            var text = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en");
            editor.Refresh("en", text, BaseGameModelKeys.Characters.Ironclad, 10, 1, 0, render: false);
            reopened.Refresh("en", text, BaseGameModelKeys.Characters.Ironclad, 10, 1, 0, render: false);
            editor.ImportQuery(q, AncientOptionConditionProfile.BroadDefault, saved);
            Check(JsonSerializer.Serialize(editor.ExportEditorState()) == JsonSerializer.Serialize(saved), "EveryInactiveRelationRetained");
            typeof(AncientSearchEditor).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, []);
            Check(editor.FindChildren("AncientMode*", "BaseButton", true, false).Count == 3 &&
                editor.FindChild("AncientAdvancedDisclosure", true, false) is null &&
                editor.FindChild("NeowOffersRelation0", true, false) is null, "DirectAncientModesAndSeparateNeowResearch");
            editor.SetNeowResearchPage(true);
            void Press(string name)
            {
                typeof(AncientSearchEditor).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, []);
                var button = editor.FindChildren(name, "BaseButton", true, false).OfType<BaseButton>().Single();
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
            Press("RestoreSavedSingleNeowOffer");
            var basic = editor.ExportQuery(SearchQuery.Empty);
            Check(basic.OpeningRoute is null && basic.LegacyNeow.NeowRelics.Any.SequenceEqual([pearl]) && basic.NeowOfferWhitelist is null,
                "OrdinarySingleOfferDoesNotCommitPickup");
            Press("NeowOffersRelation2");
            Check(editor.ExportQuery(SearchQuery.Empty).NeowOfferWhitelist!.AllowedKeys.SequenceEqual([pearl, arcane, curse]),
                "AdvancedWhitelistSurvivesBasicSwitch");
            Press("NeowOffersRelation3");
            Check(editor.ExportQuery(SearchQuery.Empty).LegacyNeow.NeowRelics.Ban.SequenceEqual([curse]), "BanIsReachable");
            Press("NeowOffersRelation1");
            Check(editor.ExportQuery(SearchQuery.Empty).LegacyNeow.NeowRelics.All.SequenceEqual([pearl, arcane]), "AllIsPreserved");
            var active = editor.ExportQuery(SearchQuery.Empty);
            var state = editor.ExportEditorState();
            reopened.ImportQuery(active, AncientOptionConditionProfile.BroadDefault, state);
            Check(JsonSerializer.Serialize(reopened.ExportEditorState()) == JsonSerializer.Serialize(state), "SaveReopenAllDrafts");
            var undo = editor.ClearConditions(false);
            Check(editor.ExportQuery(SearchQuery.Empty).LegacyNeow.NeowRelics.IsEmpty && !editor.HasConditions(false), "ClearIncludesInactiveRelations");
            undo();
            Check(JsonSerializer.Serialize(editor.ExportEditorState()) == JsonSerializer.Serialize(state), "UndoRestoresEveryRelation");
            var old = new AncientEditorStateSnapshot(1, 1,
                [new([], false, [], 1), new([], true, [pearl], 1), new([], true, [pearl, arcane], 1)]);
            var oldQuery = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = new([pearl], [], []) } };
            editor.ImportQuery(oldQuery, AncientOptionConditionProfile.BroadDefault, old);
            Check(JsonSerializer.Serialize(editor.ExportEditorState()) == JsonSerializer.Serialize(old), "LegacySnapshotNotRewrittenOnRead");
            Check(editor.ExportQuery(SearchQuery.Empty).LegacyNeow.NeowRelics.Any.SequenceEqual([pearl]), "LegacyAnyMeaningPreserved");
            typeof(AncientSearchEditor).GetMethod("SelectMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [0]);
            Check(editor.ExportQuery(SearchQuery.Empty).LegacyNeow.NeowRelics.Any.SequenceEqual([pearl]), "LaterAncientModeDoesNotOverwriteNeow");
            bool refused = false;
            try { editor.ImportQuery(SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault, old); }
            catch (InvalidOperationException ex) { refused = ex.Message == "integration.load_shape"; }
            Check(refused, "ConflictingLegacySidecarCannotReplaceQuery");
            var empty = SearchQuery.Empty with { NeowOfferWhitelist = new([]) };
            typeof(RolltheSpire2.Ui.Pages.Search.QueryWorkbenchFrame)
                .GetMethod("ValidateRepresentable", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [empty, false]);
            editor.ImportQuery(empty, AncientOptionConditionProfile.BroadDefault);
            Check(editor.ExportQuery(SearchQuery.Empty).NeowOfferWhitelist is { AllowedKeys.Count: 0 } &&
                editor.HasConditions(false) && editor.NeowResearchConditionCount(false) == 1, "EmptyWhitelistImportRetainsContradiction");
            var emptyState = editor.ExportEditorState();
            reopened.ImportQuery(empty, AncientOptionConditionProfile.BroadDefault, emptyState);
            Check(reopened.ExportQuery(SearchQuery.Empty).NeowOfferWhitelist is { AllowedKeys.Count: 0 } &&
                reopened.ExportEditorState().NeowOffers!.EmptyWhitelistEnabled, "EmptyWhitelistSidecarRoundTrip");
            var restoreEmpty = editor.ClearConditions(false);
            Check(editor.ExportQuery(SearchQuery.Empty).NeowOfferWhitelist is null && !editor.HasConditions(false), "ClearEmptyWhitelist");
            restoreEmpty();
            Check(editor.ExportQuery(SearchQuery.Empty).NeowOfferWhitelist is { AllowedKeys.Count: 0 }, "UndoEmptyWhitelist");
            refused = false;
            try { editor.ImportQuery(empty, AncientOptionConditionProfile.BroadDefault, old); }
            catch (InvalidOperationException ex) { refused = ex.Message == "integration.load_shape"; }
            Check(refused, "LegacySidecarCannotEraseEmptyWhitelist");
            var missing = new ModelKey("RELIC", "AUTHOR_ALLOWED_MISSING_ICON");
            var missingQuery = SearchQuery.Empty with { NeowOfferWhitelist = new([missing]) };
            editor.ImportQuery(missingQuery, AncientOptionConditionProfile.BroadDefault);
            foreach (float width in new[] { 280f, 480f, 900f })
            {
                var surface = new Control();
                try
                {
                    typeof(AncientSearchEditor).GetMethod("RenderNeowOffers", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(editor, [surface, width, 0f]);
                    var buttons = surface.GetChildren().OfType<BaseButton>().ToArray();
                    Check(buttons.All(button => button.Position.X >= 0 && button.Position.X + button.Size.X <= width + .01f),
                        "NeowButtonsStayInsideWidth" + width);
                    var missingButton = buttons.Last(button => button.Name.ToString().StartsWith("NeowOffer") &&
                        !button.Name.ToString().StartsWith("NeowOffers"));
                    Check(!missingButton.GetChildren().OfType<TextureRect>().Any() &&
                        missingButton.GetChildren().OfType<Label>().Any(label => !string.IsNullOrWhiteSpace(label.Text)),
                        "MissingIconStillHasVisibleIdentity" + width);
                }
                finally { surface.Free(); }
            }
            Check(editor.ExportQuery(SearchQuery.Empty).NeowOfferWhitelist!.AllowedKeys.SequenceEqual([missing]),
                "MissingCatalogIdentitySurvivesRendering");
            reopened.ImportQuery(editor.ExportQuery(SearchQuery.Empty), AncientOptionConditionProfile.BroadDefault, editor.ExportEditorState());
            Check(reopened.ExportQuery(SearchQuery.Empty).NeowOfferWhitelist!.AllowedKeys.SequenceEqual([missing]),
                "MissingCatalogIdentitySidecarRoundTrip");
            Console.WriteLine($"NEOW_OFFER_EDITOR=PASS;checks={checks};ordinaryAdvanced=true;legacyRestore=true;clearUndo=true");
            return 0;
        }
        finally { reopened.Free(); editor.Free(); }
    }
}
