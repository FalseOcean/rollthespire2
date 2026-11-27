using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Unlocks;
using System.IO.Compression;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class PresetShareChecks
{
    public static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("PresetShare");
        var store = SearchWorkspace.Open(OS.GetUserDataDir());
        var frame = new QueryWorkbenchFrame(runtime, store);
        var library = new SeedLibraryCanvas(new SeedLibraryStore(OS.GetUserDataDir()), store, "zh");
        var canvas = new Control { Size = new(1600, 900) };
        ((SceneTree)Engine.GetMainLoop()).CurrentScene.AddChild(canvas);
        canvas.AddChild(library);
        library.ShowSection(SeedLibraryCanvas.LibrarySection.Presets);
        library.PresetUseRequested += preset => frame.ApplyPreset(preset, render: false);
        int checks = 0;
        void Check(bool condition, string issue)
        {
            if (!condition) throw new InvalidOperationException(issue);
            checks++;
        }
        void Present()
        {
            for (int i = 0; i < 4000 && AccessTools.Field(library.GetType(), "_presentation").GetValue(library) != null; i++)
                AccessTools.Method(library.GetType(), "TickPresentation").Invoke(library, null);
        }
        int CompareSeeds(CompiledSearch original, CompiledSearch imported, string sample)
        {
            const int count = 128;
            var left = SearchPreparationCompiler.Compile(original, new("000000000000", count, 1, 1));
            var right = SearchPreparationCompiler.Compile(imported, new("000000000000", count, 1, 1));
            Check(left.Success == right.Success && left.Disposition == right.Disposition && left.Issue == right.Issue,
                $"Different search admission for {sample}: {left.Issue} -> {right.Issue}");
            if (left.Plan is null)
            {
                Console.WriteLine($"PRESET_SHARE_EXACT sample={sample};seeds=0;admission={left.Disposition};issue={left.Issue}");
                return 0;
            }
            int matches = 0;
            for (ulong ordinal = 0; ordinal < count; ordinal++)
            {
                var seed = Beta110SeedCodec.FormatOrdinal(ordinal);
                var input = TrustedRootHashInput.FromCanonicalSeed(runtime.Profile, seed);
                var before = ProductionExactSearchEvaluator.Evaluate(left.Plan.Model, input).Evaluation.Disposition;
                var after = ProductionExactSearchEvaluator.Evaluate(right.Plan!.Model, input).Evaluation.Disposition;
                Check(before == after, $"Different Exact result for {seed}: {before} -> {after}");
                if (before == SearchDisposition.Match) matches++;
            }
            Console.WriteLine($"PRESET_SHARE_EXACT sample={sample};seeds={count};matches={matches}");
            return matches;
        }
        try
        {
            foreach (int seats in new[] { 1, 2, 4 })
            {
                var source = PresetWorkbenchChecks.BuildDraft(runtime, seats);
                source = source with { Players = source.Players.Select(p => p with { UnlockSource = "CapturedLobbySlot" }).ToArray() };
                var saved = store.Presets.SaveUserPreset("Example" + seats, "test", [], frame.CapturePreset(source));
                var code = SearchPresetShare.Encode(saved);
                var oldCode = LegacyCode(saved);
                var oldPreview = SearchPresetShare.Decode(oldCode);
                var preview = SearchPresetShare.Decode(code);
                Check(code.Length < oldCode.Length && preview.Description == "" && preview.VisualIcons.Count == 0 &&
                    SearchPresetShare.Encode(saved with { Title = "Other title", Description = "Other text",
                        Workbench = source with { AncientEditor = null, PartyAncientEditor = null, ManualFamilyOrder = ["R", "N"] }
                    }) == code, "Non-search fields affected compact code");
                Check(preview.Workbench is { UseFullUnlocks: true } intent && intent.Players.All(p =>
                    p.Unlocks == null && p.UnlockSource == "AssumedFullyUnlocked"), "Wire contains an unbound full-unlock template");
                Check(preview.Workbench!.Players.Select(p => p.Character).SequenceEqual(source.Players.Select(p => p.Character)),
                    "Roster order changed");
                library.PreviewSharedPreset(code);
                Present();
                var use = library.FindChild("UseLibraryPreset", owned: false) as Button;
                Check(use is { Disabled: false }, "Shared editor transaction unavailable");
                use!.EmitSignal(Button.SignalName.Pressed);
                var rebound = frame.CaptureDraft();
                Check(rebound.UseFullUnlocks && rebound.Ascension == source.Ascension && rebound.Character == source.Character &&
                    rebound.Players.Count == source.Players.Count, "Imported context changed");
                var actual = rebound.Compile(runtime, out var actualAuthority);
                var expected = oldPreview.Workbench!.BindPresetIntent(runtime, _ => throw new Exception("Read a local seat"));
                var compiled = expected.Compile(runtime, out _);
                Check(actual.SemanticFingerprint == compiled.SemanticFingerprint, "Search semantics changed");
                Check(actualAuthority.AllCharacterCardPoolsUnlocked == true, "Production authority missing full unlocks");
                var resultContext = SeedLibraryContextCapture.ForSearch(rebound, runtime);
                Check(resultContext.Players.All(p => p.UnlockSource == "AssumedFullyUnlocked" &&
                    p.Unlocks.UnlockedEpochs.SequenceEqual(UnlockState.all.ToSerializable().UnlockedEpochs)),
                    "Result analysis did not retain full unlocks");
                var restartDraft = (WorkbenchSearchDraft)AccessTools.Method(frame.GetType(), "PrepareSavedEditorParty").Invoke(frame, [rebound])!;
                Check(restartDraft.Players.All(p => p.UnlockSource == "AssumedFullyUnlocked"), "P1 lost full unlocks on restore");
                Check(QueryHistoryIdentity.Key(source) != QueryHistoryIdentity.Key(rebound), "Histories unexpectedly merged");
                ((Button)library.FindChild("SaveSharedPreset", owned: false)).EmitSignal(Button.SignalName.Pressed);
                var disk = new SearchPresetStore(Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state"));
                Check(store.Presets.TryGetUserByName(seats == 1 ? "分享预设" : seats == 2 ? "分享预设 (2)" : "分享预设 (3)", out var copy) &&
                    copy.Workbench?.UseFullUnlocks == true && copy.Id != saved.Id, "Import overwrote an existing preset");
                Check(disk.GetPresets().Any(p => p.Id == copy.Id && p.Workbench?.UseFullUnlocks == true),
                    "Shared preset was not readable after restart");
                CompareSeeds(compiled, actual, "original-" + seats);
                Console.WriteLine($"PRESET_SHARE seats={seats};oldChars={oldCode.Length};newChars={code.Length};checks={checks}");
                // A reachable condition from the same roster also exercises positive results.
                var rewards = SearchQuery.Empty with { CombatPotionRewards = (seats == 1 ? source.Query :
                    source.Query.Players[0].Conditions).CombatPotionRewards };
                var reachable = expected with { AncientEditor = null, PartyAncientEditor = null, Query = seats == 1 ? rewards :
                    SearchQuery.Empty with { Players = source.Query.Players.Select(p => new PlayerOfferQuery(p.Slot, ModelKeySetFilter.Empty)
                        { Conditions = p.Slot == 0 ? rewards : SearchQuery.Empty }).ToArray() } };
                library.PreviewSharedPreset(SearchPresetShare.Encode(saved with { Workbench = reachable }));
                Present();
                ((Button)library.FindChild("UseLibraryPreset", owned: false)).EmitSignal(Button.SignalName.Pressed);
                int hits = CompareSeeds(reachable.Compile(runtime, out _), frame.CaptureDraft().Compile(runtime, out _), "potion-" + seats);
                Check(hits > 0 && hits < 128, "Expected positive and negative potion results");
            }
            var easy = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
                SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones) },
                AncientOptionConditionProfile.BroadDefault);
            var simple = store.Presets.SaveUserPreset("骨骰开局", "", [], frame.CapturePreset(easy));
            var box = new ModelKey("RELIC", "PANDORAS_BOX");
            var offering = new ModelKey("CARD", "OFFERING");
            frame.RestoreDraft(easy with { UseFullUnlocks = true, Query = SearchQuery.Empty with {
                AncientBranches = [new(2, new("EVENT", "DARV"), [box], [])] } }, render: false);
            var ancient = (AncientSearchEditor)AccessTools.Field(frame.GetType(), "_ancientEditor").GetValue(frame)!;
            Check(ancient.TryRestorePandoraCount(2, 8) &&
                ancient.TryRestoreTransformationTargets(TransformationSearchSource.PandoraId, 2, [offering, offering]),
                "Could not author the Pandora example");
            AccessTools.Method(frame.GetType(), "RefreshTransformationContext").Invoke(frame, [null, false]);
            var pandora = frame.CaptureDraft();
            var pandoraCode = SearchPresetShare.Encode(simple with { Workbench = pandora });
            library.PreviewSharedPreset(pandoraCode);
            Present();
            ((Button)library.FindChild("UseLibraryPreset", owned: false)).EmitSignal(Button.SignalName.Pressed);
            var boxDraft = frame.CaptureDraft();
            Check(boxDraft.Query.TransformationAggregate is { PandoraCountOverride: 8, AncientAct: 2 } boxCondition &&
                boxCondition.TargetMultiset.SequenceEqual(new[] { offering, offering }) &&
                boxDraft.Compile(runtime, out _).SemanticFingerprint == pandora.Compile(runtime, out _).SemanticFingerprint,
                "Changed the source, Pandora material count or repeated target after importing conditions alone");
            library.PreviewSharedPreset(SearchPresetShare.Encode(simple));
            Present();
            ((Button)library.FindChild("UseLibraryPreset", owned: false)).EmitSignal(Button.SignalName.Pressed);
            var loaded = frame.CaptureDraft();
            int easyHits = CompareSeeds((easy with { UseFullUnlocks = true }).Compile(runtime, out _), loaded.Compile(runtime, out _), "bones");
            Check(easyHits > 0 && easyHits < 128, "Expected a mix of matching and rejected seeds");
            Console.WriteLine($"PRESET_SHARE_BONES oldChars={LegacyCode(simple).Length};newChars={SearchPresetShare.Encode(simple).Length};seeds=128;matches={easyHits}");
            store.Preferences.SearchMode = "CPU";
            store.Runtime.Start(loaded, runtime, 1, 1, canvas.GetTree());
            Check(SpinWait.SpinUntil(() => { store.Runtime.Tick(.01); return !store.Runtime.Receiving; }, 30000)
                && store.DisplayResults.Any(r => r.EffectiveValidationDisposition == SearchDisposition.Match
                    && r.Context?.Players.Single().UnlockSource == "AssumedFullyUnlocked"), "Shared search did not deliver a full-unlock match");
            frame.SetSearchMode(true, render: false);
            Check(frame.CaptureDraft().Players.All(p => p.UnlockSource == "AssumedFullyUnlocked"), "Switching modes recovered a local seat");
            var missing = simple with { Workbench = easy with
                { Query = SearchQuery.Empty with { OpeningRoute = new(new("RELIC", "MISSING_SHARED_RELIC")) } } };
            library.PreviewSharedPreset(SearchPresetShare.Encode(missing));
            Present();
            Check(library.FindChild("UseLibraryPreset", owned: false) is Button { Disabled: true }
                && library.FindChild("SaveSharedPreset", owned: false) is Button { Disabled: false },
                "Unavailable mod content must preserve the preset and prevent an incomplete search");
            try { SearchPresetShare.Decode("RT2P1-broken"); throw new Exception("Invalid code accepted"); }
            catch (FormatException) { }
        }
        finally { store.Runtime.Cancel(); canvas.Free(); frame.Free(); }
        Console.WriteLine($"PRESET_SHARE_PASS checks={checks}");
        return 0;
    }

    // Reproduce the V1 encoder used by the original three measurements.
    private static string LegacyCode(SearchPresetDefinition preset)
    {
        var json = SearchPresetStore.CreateJsonOptions();
        json.WriteIndented = false;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { preset.Title, preset.Description, Icons = preset.VisualIcons,
            Draft = (preset.Workbench! with { UseFullUnlocks = true }).ToPresetIntent() }, json);
        using var packed = new MemoryStream();
        using (var writer = new BinaryWriter(packed, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write(bytes.Length);
        using (var compressor = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(bytes);
        return "RT2P1-" + Convert.ToBase64String(packed.ToArray());
    }
}
