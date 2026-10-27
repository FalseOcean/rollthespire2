using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class FamilyCpuIntegrationChecks
{
    // Optional preparation/checking within the existing functional entry point.
    // Captured immutable authority is reused verbatim; no native game or benchmark is launched.
    private static void CheckResumeManifest(Action<bool, string> check)
    {
        string? input = Environment.GetEnvironmentVariable("RT2_CPU_RESUME_VALIDATION_MANIFEST");
        if (string.IsNullOrWhiteSpace(input)) return;
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("RT2_CPU_RESUME_VALIDATION_OUT")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, "validated"));
        Directory.CreateDirectory(output);
        var manifest = JsonNode.Parse(File.ReadAllText(input))!.AsObject();
        var cases = manifest["Cases"]!.AsArray();
        var selected = Environment.GetEnvironmentVariable("RT2_CPU_RESUME_VALIDATION_CASES")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int passed = 0, pending = 0;
        foreach (var node in cases)
        {
            var row = node!.AsObject(); string id = row["Id"]!.GetValue<string>();
            if (selected is { Length: > 0 } && !selected.Contains(id, StringComparer.Ordinal)) continue;
            var watch = Stopwatch.StartNew();
            string contextPath = row["Context"]!.GetValue<string>();
            string workloadPath = row["Workload"]!.GetValue<string>();
            try
            {
                var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(contextPath);
                var original = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(workloadPath);
                row["ContextSha256"] = ResumeHash(contextPath);
                row["OriginalWorkload"] = workloadPath;
                row["OriginalWorkloadSha256"] = ResumeHash(workloadPath);
                row["SourceHead"] = manifest["SourceHead"]?.DeepClone();
                // Compile the preserved original before constructing any independent author baseline.
                try
                {
                    var originalRequest = ResumeCompile(original, captured.Context, original.Options.StartSeed);
                    row["OriginalCompile"] = "pass";
                    row["OriginalFamilies"] = ResumeStrings(FamilyExecutionCoordinator.Plan(originalRequest, false).OrderedFamilies.Select(f => f.FamilyId));
                }
                catch (Exception ex) { row["OriginalCompile"] = "failed"; row["OriginalCompileException"] = ex.ToString(); }
                bool author = row["NeedsNativePositiveAuthoring"]?.GetValue<bool>() == true;
                bool independentSoloNwae = row["AuthoringMode"]?.GetValue<string>() == "SoloNwaeObserved";
                if (independentSoloNwae && (captured.Context.Party is not null || row["IndependentAuthoredCase"]?.GetValue<bool>() != true))
                    throw new InvalidDataException("Resume.SoloNwaeRequiresExplicitIndependentSoloCase");
                var known = row["PositiveSeeds"]?.AsArray().Select(s => s!.GetValue<string>()).ToArray() ?? [];
                ulong authorStart = 0;
                if (independentSoloNwae && !VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance,
                    original.Options.StartSeed, out authorStart, out _, out _))
                    throw new InvalidDataException("Resume.InvalidAuthoringWindowStart");
                bool found = false; int examined = 0; string last = "NoPositiveWithinBound";
                foreach (string seed in known.Concat([original.Options.StartSeed]).Concat(Enumerable.Range(0, 4096).Select(i =>
                    VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, authorStart + (ulong)i))).Distinct(StringComparer.Ordinal))
                {
                    if (++examined > 4096 || watch.Elapsed.TotalSeconds > 45) { last = "PreparationBoundReached:" + last; break; }
                    ProductionSearchReplayWorkload candidate = original;
                    bool shapeEqual = true; string shapeDifference = "";
                    if (author)
                    {
                        if (!ResumeAuthor(original, captured.Context, row["ActiveSlot"]!.GetValue<int>(), seed,
                            out candidate, out shapeEqual, out shapeDifference, out last)) continue;
                    }
                    var request = ResumeCompile(candidate, captured.Context, seed);
                    var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
                    var exact = ProductionExactSearchEvaluator.Evaluate(request, root);
                    last = exact.Evaluation.Disposition + ":" + exact.FailureCode;
                    if (!exact.IsMatch) continue;
                    var plan = FamilyExecutionCoordinator.Plan(request, gpuAvailable: false);
                    string[] families = plan.OrderedFamilies.Select(f => f.FamilyId).ToArray();
                    string[] expected = row["ExpectedFamilies"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
                    check(expected.All(families.Contains), "resume expected actual Family participation: " + id);
                    check(plan.OrderedFamilies.All(f => !f.ConditionPerformance.UsesGpu), "resume CPU Only plan: " + id);
                    check(request.WorkerCount == 16, "resume shared requested budget preserved: " + id);
                    check(VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, seed, out ulong ordinal, out _, out _), "resume positive seed ordinal: " + id);
                    var execution = new FamilyExecutionContext(requiresGpu: false, failOnPhysicalRecovery: true);
                    try
                    {
                        var batch = new SearchBatch(ordinal, 1);
                        var survivors = LinearFamilyPipeline.FilterAsync(execution, new(batch, request), plan.OrderedFamilies, default).GetAwaiter().GetResult();
                        check(survivors.Count == 1 && execution.CaptureFilterIsolations(batch).Count == 0,
                            "resume positive survives actual CPU Family plan without fault isolation: " + id);
                    }
                    finally
                    {
                        foreach (var family in plan.OrderedFamilies) family.DisposeAsync(execution).GetAwaiter().GetResult();
                        execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    string authoredPath = Path.Combine(output, id + (author ? "-authored-positive.json" : "-positive.json"));
                    // Timing input retains the original interval/options; the positive seed is separate evidence.
                    ProductionSearchReplay.Write(authoredPath, candidate);
                    row["Workload"] = Path.GetRelativePath(Environment.CurrentDirectory, authoredPath).Replace('\\', '/');
                    row["WorkloadSha256"] = ResumeHash(authoredPath);
                    row["AuthoredBenchmark"] = author;
                    row["SameShapeAsTemplate"] = shapeEqual;
                    row["IndependentAuthoredCase"] = independentSoloNwae || author && !shapeEqual;
                    row["ShapeDifference"] = shapeDifference;
                    row["AuthoringSource"] = author ? "Existing pure source projectors over the unchanged captured owner/shared-world authority; one root" : "Original complete query; existing or bounded positive seed";
                    row["PositiveSeeds"] = ResumeStrings([seed]);
                    row["PositiveExactValidated"] = true;
                    row["ValidationStatus"] = "positive-exact";
                    row["ExactEvidence"] = JsonSerializer.SerializeToNode(exact.Evaluation, ProductionSearchReplay.JsonOptions);
                    row["ActualFamilies"] = ResumeStrings(families);
                    row["ActualRequestedWorkers"] = request.WorkerCount;
                    row["ActualPlan"] = plan.FormatSummary();
                    row["ActualStages"] = JsonSerializer.SerializeToNode(plan.OrderedFamilies.Select(f => new {
                        f.FamilyId, Workers = (f as FamilyCpuExecution)?.Workers,
                        f.ConditionPerformance.PhysicalImplementationRevision, f.ConditionPerformance.UsesGpu
                    }), ProductionSearchReplay.JsonOptions);
                    row["CompileEvidence"] = "Production compiler, normal CPU Only plan, positive whole-table Production Exact";
                    found = true; passed++; break;
                }
                row["SeedCandidatesExamined"] = examined;
                if (!found) { pending++; row["ValidationStatus"] = "pending-positive"; row["LastOutcome"] = last; row["PositiveExactValidated"] = false; }
            }
            catch (Exception ex)
            {
                pending++; row["ValidationStatus"] = "validation-failed"; row["Exception"] = ex.ToString();
                row["PositiveExactValidated"] = false;
            }
            row["PreparationSeconds"] = watch.Elapsed.TotalSeconds;
            // Persist after each bounded case so a later failure never erases completed evidence.
            File.WriteAllText(Path.Combine(output, "validated-manifest.json"), manifest.ToJsonString(new() { WriteIndented = true }));
            Console.WriteLine($"CPU_RESUME_CASE id={id};status={row["ValidationStatus"]};seconds={watch.Elapsed.TotalSeconds:F3}");
        }
        Console.WriteLine($"CPU_RESUME_VALIDATION positive={passed};pending={pending};output={output};nativeExecution=false;throughputMeasured=false");
        check(pending == 0, "resume manifest retains unresolved compile/positive cases; inspect validated-manifest.json");
    }

    private static ExactSearchExecutionRequest ResumeCompile(ProductionSearchReplayWorkload workload, SearchContext context, string seed)
    {
        var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context),
            workload.Options with { StartSeed = seed, ScanCount = 1, TargetMatchCount = 1 });
        return result.Plan ?? throw new InvalidDataException("Resume.Compile:" + result.Issue);
    }

    private static bool ResumeAuthor(ProductionSearchReplayWorkload source, SearchContext context, int slot, string seed,
        out ProductionSearchReplayWorkload candidate, out bool sameShape, out string difference, out string issue)
    {
        candidate = source; sameShape = true; difference = ""; issue = "AuthorProjectionUnavailable";
        bool solo = context.Party is null;
        var owner = solo ? context.Authority : context.Party!.Players[slot];
        var personal = solo ? new PlayerOfferQuery(0, ModelKeySetFilter.Empty) {
            Conditions = source.Query, AncientPremises = context.EvaluationAssumptions.AncientEligibilityAssumptions
        } : source.Query.Players.Single(p => p.Slot == slot);
        var q = personal.Conditions;
        ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
        if (solo)
        {
            // This producer is deliberately scoped to the separately named NWAE benchmark.
            // The sparse original is never edited or relabeled as a positive.
            if (q.OpeningRoute?.RouteRelicKey != BaseGameModelKeys.Relics.ArcaneScroll ||
                q.StructuredOpeningEffects.Count != 1 || q.StructuredOpeningEffects[0] is not {
                    Kind: NeowStructuredConditionKind.ExactSingle, OutputKind: NeowStructuredOutputKind.Card, OutputKeys.Count: 1 } ||
                q.AncientBranches.Count != 2 || q.AncientBranches.Any(b => b.OptionAny.Count != 1 || b.SeaGlassTargetAny.Count != 0) ||
                !q.AncientBranches.Any(b => b.Act == 2 && b.AncientKey == new ModelKey("EVENT", "TEZCATARA")) ||
                !q.AncientBranches.Any(b => b.Act == 3 && b.AncientKey == new ModelKey("EVENT", "DARV")) ||
                q.EventSequenceConstraints.Count != 4 || q.EventSequenceConstraints.Count(c => c.Act == 1) != 1 ||
                q.EventSequenceConstraints.Count(c => c.Act == 2) != 3 ||
                q.EventSequenceConstraints.Any(c => c.RangeMode != SearchSequenceRangeMode.FirstN || c.RangeValue != 3 ||
                    c.Keys.Any.Count != 1 || c.Keys.All.Count != 0 || c.Keys.Ban.Count != 0) ||
                q.EventResultConditions.Count != 1 || q.EventResultConditions[0].Kind != EventResultConditionKind.TrashHeapGrabCard)
                throw new InvalidDataException("Resume.UnexpectedIndependentSoloNwaeTemplate");
            var offers = ModernNeowIdentityPredictor.PredictModernCore(root, owner, Beta111Profile.Instance,
                owner.IsBeta111NeowIdentityAuthorityExact);
            if (!offers.RelicKeys.Contains(BaseGameModelKeys.Relics.ArcaneScroll)) { issue = "ActualArcaneOfferAbsent"; return false; }
            var arcane = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.ArcaneScroll,
                PredictionPrecision.Exact, owner, root);
            var cards = arcane.EffectGroups.SelectMany(g => g.OrderedItems)
                .Where(i => i.Kind == PredictedEffectKind.AddCard && i.TargetKey.HasValue && i.IsProductRelevant)
                .Select(i => i.TargetKey!.Value).Distinct().ToArray();
            if (cards.Length != 1) { issue = "ActualArcaneSingleCardUnavailable"; return false; }
            q = q with { StructuredOpeningEffects = [q.StructuredOpeningEffects[0] with { OutputKeys = [cards[0]] }] };
        }
        if (q.MerchantColorlessSequenceConditions.Count > 0)
        {
            var shops = Beta111NormalMerchantColorlessSequenceProjector.Project(root, Beta111MerchantColorlessAuthority.From(owner));
            if (shops.Merchants.Count != 5) { issue = "MerchantProjectionIncomplete:" + shops.EvidenceCode; return false; }
            q = q with { MerchantColorlessSequenceConditions = q.MerchantColorlessSequenceConditions.Select(c => c with {
                Slots = c.Slots.Select((target, i) => target.HasValue ? (ModelKey?)(c.Slot == MerchantColorlessSlot.Uncommon
                    ? shops.Merchants[i].UncommonColorless : shops.Merchants[i].RareColorless) : null).ToArray()
            }).ToArray() };
            // Ordered count/slot/null positions remain. Original repeated identity equality may differ.
            sameShape = personal.Conditions.MerchantColorlessSequenceConditions.Zip(q.MerchantColorlessSequenceConditions)
                .All(pair => ResumeEqualityShape(pair.First.Slots, pair.Second.Slots));
            if (!sameShape) difference = "Independent ordered five-shop benchmark: observed target multiplicities differ from repeated AUTOMATION template; same count/order/rarity/null positions. Original template remains unvalidated.";
        }
        if (q.EventResultConditions.Count > 0)
        {
            var e = Beta111EventResultProjector.Project(root, slot, Beta111EventResultAuthority.From(owner));
            int fake = 0, color = 0;
            q = q with { EventResultConditions = q.EventResultConditions.Select(c => c.Kind switch {
                EventResultConditionKind.TrashHeapGrabCard => c with { TargetKey = e.TrashHeapGrabCard },
                EventResultConditionKind.TrashHeapDiveRelic => c with { TargetKey = e.TrashHeapDiveRelic },
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => c with { TargetKey = e.FakeMerchantInventory[fake++] },
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => c with { TargetKey = e.ColorfulOfferedColors[color++] },
                EventResultConditionKind.TrialCase => c with { TrialCase = (TrialCaseTarget)Beta111TrialTinkerProjector.TrialCase(root, slot) },
                EventResultConditionKind.TinkerTimeTypeAndRider => ResumeTinker(c, root, slot),
                _ => throw new InvalidDataException("Resume.UnsupportedAuthoredEventKind:" + c.Kind)
            }).ToArray() };
        }
        if (q.VariantBossBranches.Count > 0 || q.EventSequenceConstraints.Count > 0 || q.AncientBranches.Count > 0)
        {
            var world = Beta109WorldPredictionProvider.Predict(seed, owner.Ascension, slot, owner.Character.CharacterKey,
                owner.WorldAuthority!, personal.AncientPremises);
            var projected = Beta109WorldSnapshotProjector.ProjectForSeed(owner.WorldAuthority!.Beta109Generation!, seed);
            q = q with { VariantBossBranches = q.VariantBossBranches.Select(b => b with {
                VariantKey = projected.SelectedActs[b.Act - 1],
                FirstBoss = ResumeObservedFilter(b.FirstBoss, world.Bosses.Where(x => x.Act == b.Act && x.Ordinal == 1).Select(x => x.BossKey).ToArray()),
                SecondBoss = ResumeObservedFilter(b.SecondBoss, world.Bosses.Where(x => x.Act == b.Act && x.Ordinal == 2).Select(x => x.BossKey).ToArray())
            }).ToArray() };
            var mapped = new Dictionary<(int, ModelKey), ModelKey>(); var used = new HashSet<(int, ModelKey)>();
            var authoredEvents = new List<EventSequenceSearchCondition>();
            foreach (var c in q.EventSequenceConstraints)
            {
                var entries = world.EventPoolSequencePrediction?.Acts.FirstOrDefault(a => a.Act == c.Act)?.Entries
                    .Where(e => (c.RangeMode == SearchSequenceRangeMode.FirstN ? e.Ordinal <= c.RangeValue : e.Ordinal == c.RangeValue) &&
                        (!c.Source.HasValue || e.Source == c.Source)).Select(e => e.EventKey).Distinct().ToArray() ?? [];
                if (entries.Length == 0) return false;
                var targets = new List<ModelKey>();
                foreach (var target in c.Keys.Any)
                {
                    if (!mapped.TryGetValue((c.Act, target), out var actual))
                    {
                        var available = entries.Where(k => !used.Contains((c.Act, k))).ToArray();
                        if (available.Length == 0) { issue = "SameShapeDistinctEventTargetsUnavailable"; return false; }
                        actual = available[0]; mapped[(c.Act, target)] = actual; used.Add((c.Act, actual));
                    }
                    if (!entries.Contains(actual)) return false;
                    targets.Add(actual);
                }
                if (c.Keys.All.Count > 0 || c.Keys.Ban.Count > 0) { issue = "AuthoringNonAnyEventTemplateNotImplemented"; return false; }
                authoredEvents.Add(c with { Keys = c.Keys with { Any = targets } });
            }
            q = q with { EventSequenceConstraints = authoredEvents };
            if (solo)
            {
                var branches = new List<AncientSearchBranchCondition>();
                foreach (var b in q.AncientBranches)
                {
                    var actual = world.Ancients.FirstOrDefault(a => a.Act == b.Act && a.AncientKey == b.AncientKey);
                    var options = actual?.Options.Where(o => o.IsVisible && o.IsSelectable).Select(o => o.OptionKey).ToArray() ?? [];
                    if (options.Length == 0) { issue = "ActualRequiredAncientIdentityOrVisibleOptionAbsent:" + b.Act + ":" + b.AncientKey; return false; }
                    branches.Add(b with { OptionAny = [options[0]] });
                }
                q = q with { AncientBranches = branches };
            }
            if (q.LegacyWorld.AncientOptionFilters.Any(f => !f.IsEmpty))
            {
                // Bind the actual appearance AND options to the same captured owner/Ancient row.
                var branch = q.AncientBranches.Single();
                var ancient = world.Ancients.FirstOrDefault(a => a.Act == branch.Act && a.AncientKey == branch.AncientKey);
                var visible = ancient?.Options.Where(o => o.IsVisible).Select(o => o.OptionKey).Distinct().ToArray() ?? [];
                if (visible.Length < 3) { issue = "ActualRequiredAncientRowOrOptionsAbsent"; return false; }
                var catalog = projected.AncientEventContexts.First(c => c.Act == branch.Act && c.AncientKey == branch.AncientKey && c.PlayerSlot == slot).Catalog!;
                var excluded = catalog.Pools.SelectMany(p => p.OrderedOptions).Distinct().Where(k => !visible.Contains(k)).ToArray();
                if (excluded.Length == 0) { issue = "NoNonvisiblePaelBanTarget"; return false; }
                if (q.LegacyWorld.AncientOptionFilters.Any(f => f.Keys.Any.Count != 2 || f.Keys.All.Count != 2 || f.Keys.Ban.Count != 1))
                    throw new InvalidDataException("Resume.UnexpectedLegacyTemplateShape");
                var oldFilters = q.LegacyWorld.AncientOptionFilters;
                q = q with { LegacyWorld = q.LegacyWorld with { AncientOptionFilters = q.LegacyWorld.AncientOptionFilters.Select(f => f with {
                    Keys = new([visible[0], excluded[0]], [visible[1], visible[2]], [excluded[0]])
                }).ToArray() } };
                // Compare all cross-channel identity relations, not just channel arity.
                // The retained template has Any[1] == Ban[0], disjoint from both All targets.
                if (!oldFilters.Zip(q.LegacyWorld.AncientOptionFilters).All(pair => ResumeEqualityShape(
                    pair.First.Keys.Any.Concat(pair.First.Keys.All).Concat(pair.First.Keys.Ban).Select(k => (ModelKey?)k).ToArray(),
                    pair.Second.Keys.Any.Concat(pair.Second.Keys.All).Concat(pair.Second.Keys.Ban).Select(k => (ModelKey?)k).ToArray())))
                {
                    sameShape = false;
                    difference = "Independent actual-PAEL-row benchmark: Any/All/Ban cross-channel identity equality differs from source template; original template remains unvalidated.";
                }
            }
        }
        var query = solo ? q : source.Query with { Players = source.Query.Players.Select(p => p.Slot == slot ? p with { Conditions = q } : p).ToArray() };
        candidate = source with { Query = query }; issue = "AuthoredProjectionReady"; return true;
    }

    private static ModelKeySetFilter ResumeObservedFilter(ModelKeySetFilter original, ModelKey[] observed) => original.IsEmpty
        ? original : original.Any.Count == 1 && original.All.Count == 0 && original.Ban.Count == 0 && observed.Length == 1
            ? original with { Any = observed } : throw new InvalidDataException("Resume.UnexpectedBossFilterShapeOrMissingObservation");
    private static EventResultSearchCondition ResumeTinker(EventResultSearchCondition c, ulong root, int slot)
    {
        var option = Beta111TrialTinkerProjector.TinkerOptions(root, slot)[0];
        return c with { TinkerCardType = (TinkerCardTypeTarget)option.CardType,
            TinkerRider = c.TinkerRider.HasValue ? (TinkerRiderTarget)option.Riders[0] : null };
    }
    private static bool ResumeEqualityShape(IReadOnlyList<ModelKey?> a, IReadOnlyList<ModelKey?> b) =>
        Enumerable.Range(0, a.Count).All(i => Enumerable.Range(0, a.Count).All(j => (a[i] == a[j]) == (b[i] == b[j])));
    private static JsonArray ResumeStrings(IEnumerable<string> values) => new(values.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
    private static string ResumeHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
