using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Controls.Pickers;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;

internal static partial class PartyPhase1Checks
{
    private static async Task CheckNeowAuthoring(Node host, GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Party N authoring: " + why); }
        var profile = Beta111Profile.Instance;
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var massive = BaseGameModelKeys.Relics.MassiveScroll;
        var large = BaseGameModelKeys.Relics.LargeCapsule;
        var small = BaseGameModelKeys.Relics.SmallCapsule;
        var party = PartyRuntimeAuthorityCapture.Capture(profile, "000000000000",
            [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Ironclad],
            [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
        var context = SearchContextFactory.From(profile.ProfileId, BaseGameModelKeys.Characters.Ironclad,
            10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        SearchQuery Table(params SearchQuery[] players) => SearchQuery.Empty with
        { Players = Enumerable.Range(0, 2).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
            { Conditions = slot < players.Length ? players[slot] : SearchQuery.Empty }).ToArray() };
        ExactSearchExecutionRequest Compile(SearchQuery table, string seed, int count = 33) =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(table, context), new(seed, count, 1, 1)).Plan!;
        SearchQuery Bones(ModelKey a, ModelKey b, NeowStructuredEffectSearchCondition result) => SearchQuery.Empty with
        { OpeningRoute = new(bones), OpeningRouteRelicRequirement = new(bones, [a,b], BonesRouteOrderMode.AnyOrder),
            StructuredOpeningEffects = [result] };
        // Exact authoring from the Owner's 21:50 log. Previously planning threw
        // Party.WholeTableEnvelopeRequired before Search could start.
        var logged = Table(
            Bones(large, massive, new(large, NeowStructuredConditionKind.ExactUnorderedPair,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [new("RELIC", "CHARONS_ASHES")])),
            Bones(small, BaseGameModelKeys.Relics.DowsingRod, new(small, NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [new("RELIC", "AMETHYST_AUBERGINE")])));
        foreach (bool gpu in new[] { false, true })
        {
            await using var execution = new FamilyExecutionContext(requiresGpu: gpu, failOnPhysicalRecovery: true);
            var request = Compile(logged, "R93FW4MT75L8");
            var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpu);
            try
            {
                Check(families.Any(f => f.FamilyId == "R.Relic"), "Capsule R registered");
                _ = FamilyPlanner.Plan(families);
                foreach (var family in families)
                    foreach (var passed in new[] { new HashSet<string>(), new HashSet<string> { "N.Neow" }, new HashSet<string> { "R.Relic" } })
                        _ = family.ResolveSurvival(passed);
                checks++;
            }
            finally { foreach (var family in families) await family.DisposeAsync(execution); }
            await using var session = FamilyExecutionCoordinator.Start(request, failOnPhysicalRecovery: true, gpuAvailable: gpu);
            await session.Completion;
            Check(session.GetProgress().State == SearchRunState.Completed, "logged Capsule query starts and finishes");
        }
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = profile, Detection = detection,
            Compatibility = RuntimeProfileRegistry.Resolve(detection) };
        var picker = new ObjectPickerPanel();
        var editor = new NeowSearchEditor(runtime, picker) { Size = new(960, 700) };
        host.AddChild(picker);
        host.AddChild(editor);
        var names = RuntimeGameContentNameResolver.Create("en");
        await using var gpuExecution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        try
        {
            foreach (bool nested in new[] { false, true })
            {
                var local = SearchQuery.Empty with { OpeningRoute = new(nested ? bones : massive),
                    OpeningRouteRelicRequirement = nested ? new(bones, [massive, large], BonesRouteOrderMode.ExactOrder) : null };
                var plainRequest = Compile(Table(local), "000000000000");
                var child = ExactSearchExecutionRequestFactory.ForPlayer(plainRequest, plainRequest.CompiledSearch.PlayerSearches[0]);
                var replay = NeowReplayPlan.Compile(child, false);
                ulong ordinal = Enumerable.Range(0, 50000).Select(i => (ulong)i).First(i => NeowPartyReplay.Observe(
                    profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, i)), replay).IdentityPass);
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(profile, ordinal);
                ulong root = profile.ComputeRootSeed(seed);
                var projection = new NeowEffectProjectionEngine(profile, "party-authoring", root, party.Players[0], true)
                    .Project(nested ? bones : massive);
                var groups = nested ? projection.BonesOutcome!.OriginalRoutes.First(r => r.AcquisitionOrder.SequenceEqual([massive, large]))
                    .RelicScopedResults.Single(r => r.SourceRelicKey == massive).EffectGroups : projection.EffectGroups;
                var offered = groups.SelectMany(g => g.OrderedItems).Where(e => e.Kind == PredictedEffectKind.AddCard)
                    .Select(e => e.TargetKey!.Value).Distinct().ToArray();
                var target = offered[0];
                var absent = party.Players[0].EffectAuthority!.CharacterRewardPool!
                    .Concat(party.Players[0].EffectAuthority!.ColorlessRewardPool!).Where(c => c.IsMultiplayerOnly)
                    .Select(c => c.CardKey).First(k => !offered.Contains(k));
                var condition = new NeowStructuredEffectSearchCondition(massive, NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [target]);
                var selected = local with { StructuredOpeningEffects = [condition] };
                editor.Refresh("en", BaseGameModelKeys.Characters.Ironclad, 10, 2, 0, UnlockState.all.ToSerializable());
                editor.ImportQuery(selected);
                editor.Refresh("en", BaseGameModelKeys.Characters.Ironclad, 10, 2, 0, UnlockState.all.ToSerializable());
                Check(editor.FindChildren("*", "Button", true, false).OfType<Button>().Any(b =>
                    b.AccessibilityName == names.Resolve(target, GameContentKind.Card)), "restored scroll result is visibly selected");
                var exported = editor.ExportPartyConditions(0);
                Check(exported.StructuredOpeningEffects.Single(c => c.SourceRelicKey == massive).OutputKeys.SequenceEqual([target]), "scroll result exports");
                editor.Refresh("en", BaseGameModelKeys.Characters.Ironclad, 10, 2, 1, UnlockState.all.ToSerializable());
                editor.Refresh("en", BaseGameModelKeys.Characters.Ironclad, 10, 2, 0, UnlockState.all.ToSerializable());
                Check(JsonSerializer.Serialize(editor.ExportPartyConditions(0)) == JsonSerializer.Serialize(exported), "seat switch retains result");
                var roundtrip = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(exported, ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
                editor.ImportQuery(roundtrip);
                Check(JsonSerializer.Serialize(editor.ExportQuery()) == JsonSerializer.Serialize(exported), "query persistence roundtrip");
                var positive = Compile(Table(exported), seed);
                var plain = Compile(Table(local), seed);
                double? a = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(plain.CompiledSearch)).Probability;
                double? b = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(positive.CompiledSearch)).Probability;
                Check(a is > 0 && b is > 0 && b < a, "scroll card result contributes rarity");
                foreach (bool hit in new[] { true, false })
                {
                    var query = exported with { StructuredOpeningEffects = [condition with { OutputKeys = [hit ? target : absent] }] };
                    var request = Compile(Table(query), seed);
                    Check(ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(profile, seed)).IsMatch == hit,
                        "Exact enforces scroll target");
                    var cpu = new NeowFamily.PartyInitial(request);
                    var gpu = new NeowFamily.PartyInitial(request, preferGpu: true);
                    try
                    {
                        Check(gpu.ConditionPerformance.UsesGpu, "scroll GPU active");
                        var batch = new SearchBatch(ordinal, 33);
                        var input = FamilyCandidateSet.Dense(batch);
                        var x = await cpu.InvokeAsync(gpuExecution, new(batch, request), input, default);
                        var y = await gpu.InvokeAsync(gpuExecution, new(batch, request), input, default);
                        Check(x.EnumerateLogicalOrdinals().SequenceEqual(y.EnumerateLogicalOrdinals()), "scroll CPU/GPU survivors agree");
                        Check(x.EnumerateLogicalOrdinals().Contains(0UL) == hit, "scroll target changes actual filtering");
                    }
                    finally { await cpu.DisposeAsync(gpuExecution); await gpu.DisposeAsync(gpuExecution); }
                }
            }
        }
        finally { host.RemoveChild(editor); editor.QueueFree(); host.RemoveChild(picker); picker.QueueFree(); }
        GD.Print($"PARTY_NEOW_AUTHORING_PASS checks={checks};capsuleLoggedQuery=true;massiveDirectAndBones=true;uiPersistence=true;probability=true;cpuGpuExact=true");
    }
}
