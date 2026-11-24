using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

// An acceptance branch of the existing replay host, sharing its receipt and
// semantic comparison contracts. The reference session has no numerical filters
// and uses the original Production Exact evaluator, without an override.
internal static partial class CompatibilityChecks
{
    // Explicit validation plans on the unchanged request. Neither path can
    // reject through C, and both retain the real Production Exact evaluator.
    internal static ProductionSearchSession StartCombatTruthReference(PreparedSearch request,
        string mode, bool gpuAvailable, bool float64Available)
    {
        Require(!request.RunOptions.SkipExactValidation, "CombatTruth.ExactMustBeEnabled");
        FilterBodyDescription[] bodies = mode switch {
            "direct" => [],
            "without-c" => FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable, float64Available)
                .Where(body => body.FamilyId == "N.Neow" && body.Coverage.Count == 1 && body.Coverage.Contains("N.Neow")).ToArray(),
            _ => throw new ArgumentException("CombatTruth.UnknownReferenceMode:" + mode)
        };
        Require(mode != "without-c" || bodies.Length == 1, "CombatTruth.IndependentNBodyRequired");
        var execution = FamilyPlanner.InOrder(bodies) with {
            AllowSmallCandidateExact = false,
            FilterRemainders = ["ValidationReference:OnlyIndependentNOrNoFilter;OriginalQueryExact"]
        };
        Require(!execution.FinalValidationSkipped && !execution.OrderedFamilies.Any(body => body.Coverage.Contains("C.CombatReward")),
            "CombatTruth.NoCOrSkippedExactInReference");
        Console.WriteLine($"COMBAT_TRUTH_REFERENCE mode={mode};filters={string.Join(',', bodies.Select(body => body.FamilyId))};originalQueryUnchanged=true;exactOverride=false;smallExact=false;requestSnapshot={request.SnapshotFingerprint}");
        return new ProductionSearchSession(request, execution,
            FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(request, execution), execution),
            FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, bodies), 64, null, "", failOnPhysicalRecovery: true);
    }

    private sealed record CombatRecallFixture(string Name, ProductionSearchReplayContext Context,
        ProductionSearchReplayWorkload Workload, string Provenance, string? Anchor = null,
        SearchDisposition? AnchorDisposition = null, bool PreserveNeutralFiltering = false);

    internal static async Task RunCombatRecallAsync(ProductionSearchReplayContext captured,
        ProductionSearchReplayWorkload source, string output, string fixtureRoot, string soloContextPath,
        Action<ProductionSearchSession?> observeSession, string fixtureSelection = "")
    {
        Require(Path.IsPathFullyQualified(output) && Path.IsPathFullyQualified(fixtureRoot) && Path.IsPathFullyQualified(soloContextPath),
            "CombatRecall.ExplicitAbsoluteOutputAndFixturePathsRequired");
        fixtureRoot = Path.GetFullPath(fixtureRoot);
        soloContextPath = Path.GetFullPath(soloContextPath);
        Directory.CreateDirectory(output);
        Require(captured.Context.Party is not null && source.Options.ScanCount == 65539 &&
            source.Options.TargetMatchCount >= source.Options.ScanCount && !source.Options.SkipExactValidation &&
            source.Options.MapLossy?.Enabled != true, "CombatRecall.RequiresOriginalFull65539RangeWithExactAndNoTargetTruncation");
        Require(source.Query.Players.Any(p => p.Conditions.HasCombatRewardConstraints), "CombatRecall.OriginalPartyCObligation");
        var watch = Stopwatch.StartNew();
        var failures = new List<Exception>();
        var outcomes = new List<object>();
        var fixtures = new List<CombatRecallFixture> {
            new("ft01-original-65539", captured, source, "Caller-supplied original FT01 query/context/options", "00000000006B", SearchDisposition.Match)
        };
        AddSmallFixtures();
        if (fixtureSelection.Length > 0)
        {
            var selected = fixtureSelection.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            Require(selected.Count > 0 && selected.All(name => fixtures.Any(fixture => fixture.Name == name)),
                "CombatRecall.UnknownExplicitFixtureSelection:" + fixtureSelection);
            fixtures.RemoveAll(fixture => !selected.Contains(fixture.Name));
        }
        WriteEvidence(Path.Combine(output, "combat-recall-fixtures.json"), new {
            FixtureRoot = fixtureRoot, SoloContextPath = soloContextPath,
            Fixtures = fixtures.Select(f => new {
                f.Name, f.Provenance, f.Anchor, f.AnchorDisposition, f.PreserveNeutralFiltering, f.Workload.Options,
                CapturedContextPath = Path.Combine(output, f.Name, "context.json"),
                WorkloadPath = Path.Combine(output, f.Name, "workload.json"),
                Scope = "Independent fixture; no result or reachability inferred from design alone"
            }).ToArray()
        });
        foreach (var fixture in fixtures)
        {
            string directory = Path.Combine(output, fixture.Name);
            Directory.CreateDirectory(directory);
            try
            {
                Require(watch.Elapsed < TimeSpan.FromSeconds(1800), "CombatRecall.GroupDeadlineBeforeFixture:" + fixture.Name);
                ProductionSearchReplay.Write(Path.Combine(directory, "context.json"), fixture.Context);
                ProductionSearchReplay.Write(Path.Combine(directory, "workload.json"), fixture.Workload);
                var request = Compile(fixture.Workload, fixture.Context.Context);
                string originalQuery = JsonSerializer.Serialize(request.Model.CompiledSearch.Query, Compact);
                Require(!request.RunOptions.SkipExactValidation && request.TargetMatchCount >= request.ScanCount,
                    "CombatRecall.FixedRangeNotTargetTruncated:" + fixture.Name);
                var reference = await Collect(request, directory, "direct-original-query-exact", directExact: true, cpuOnly: true);
                if (fixture.Anchor is not null)
                    Require(reference.SemanticRecords.Any(record => HasDisposition(record, fixture.Anchor, fixture.AnchorDisposition!.Value)),
                        "CombatRecall.OriginalSourceAnchor:" + fixture.Name);
                if (fixture.Name == "party-gold-impossible")
                    Require(reference.Progress.MatchCount == 0, "CombatRecall.NegativeHasNoConfirmedMatch");
                var defaultRequest = request with { RunOptions = request.RunOptions with { ManualFamilyOrder = null } };
                var cpuBodies = FamilyExecutionCoordinator.CreateRegisteredFamilies(defaultRequest, gpuAvailable: false);
                var cFirstOrder = cpuBodies.OrderBy(body => body.FamilyId == "C.CombatReward" ? 0 : 1)
                    .Select(FamilyExecutionCoordinator.FamilyOrderKey).ToArray();
                Require(cFirstOrder.Any(key => key.Split('+').Contains("C.CombatReward")), "CombatRecall.CFirstHasRegisteredC:" + fixture.Name);
                var cFirstRequest = request with { RunOptions = request.RunOptions with { ManualFamilyOrder = cFirstOrder } };
                foreach (var mode in new[] {
                    (Label: "default-gpu", Request: defaultRequest, Cpu: false, Float64: (bool?)null),
                    (Label: "c-first-gpu-native", Request: cFirstRequest, Cpu: false, Float64: (bool?)true),
                    (Label: "c-first-gpu-integer", Request: cFirstRequest, Cpu: false, Float64: (bool?)false),
                    (Label: "c-first-cpu", Request: cFirstRequest, Cpu: true, Float64: (bool?)null),
                    (Label: "default-cpu", Request: defaultRequest, Cpu: true, Float64: (bool?)null) })
                {
                    try
                    {
                        var actual = await Collect(mode.Request, directory, mode.Label, directExact: false, cpuOnly: mode.Cpu, float64: mode.Float64);
                        var c = Flatten(actual.Invocations).Where(row => row.FamilyId == "C.CombatReward").ToArray();
                        bool cExecuted = c.Any(row => row.Batches > 0 && row.Inputs > 0);
                        var missing = reference.SemanticRecords.Except(actual.SemanticRecords).ToArray();
                        var proofs = new List<object>();
                        var independentlyRejected = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string record in missing)
                            if (TryProveIndependentCombatRejection(request, record, out var proof))
                            { independentlyRejected.Add(record); proofs.Add(proof!); }
                        var required = reference.SemanticRecords.Where(record => !independentlyRejected.Contains(record)).ToArray();
                        WriteEvidence(Path.Combine(directory, mode.Label + ".comparison.json"), new {
                            WholeQuerySemanticMultisetEqual = reference.SemanticRecords.SequenceEqual(actual.SemanticRecords),
                            Missing = missing,
                            Extra = actual.SemanticRecords.Except(reference.SemanticRecords).ToArray(),
                            UnprovedMissing = missing.Where(record => !independentlyRejected.Contains(record)).ToArray(),
                            IndependentlyRejectedUnknowns = proofs,
                            SemanticsEqualAfterProvedIndependentRejections = required.SequenceEqual(actual.SemanticRecords),
                            ReferenceDigest = Digest(reference.SemanticRecords), ActualDigest = Digest(actual.SemanticRecords),
                            Reference = reference.Progress, Actual = actual.Progress,
                            ReferenceWarningRecords = reference.WarningRecords, ActualWarningRecords = actual.WarningRecords,
                            OriginalQuery = request.Model.CompiledSearch.Query,
                            OriginalSemanticFingerprint = request.Model.CompiledSearch.SemanticFingerprint,
                            ReferenceCursor = reference.SafeNextOrdinal, ActualCursor = actual.SafeNextOrdinal,
                            ActualC = c, ActualCExecuted = cExecuted,
                            NoCDispatch = !cExecuted, CFilteringObserved = c.Any(row => row.Inputs > row.Outputs),
                            CRecallExecutionClaimed = cExecuted && c.Any(row => row.Inputs > row.Outputs),
                            Boundary = "All delivered Match/Unknown/Unsupported records, evidence and witnesses; no CPU-C recall oracle"
                        });
                        // Partial filters can truthfully disclose remainders.
                        // Warnings retain their original cause in both receipts;
                        // original-query Exact outcomes own acceptance, not an
                        // assertion that every optional fast path was complete.
                        Require(ReferenceEquals(request.Model, mode.Request.Model) &&
                            JsonSerializer.Serialize(request.Model.CompiledSearch.Query, Compact) == originalQuery &&
                            reference.RequestSnapshotFingerprint == actual.RequestSnapshotFingerprint &&
                            reference.ContinuationFingerprint == actual.ContinuationFingerprint &&
                            required.SequenceEqual(actual.SemanticRecords) &&
                            reference.SafeNextOrdinal == actual.SafeNextOrdinal &&
                            reference.Progress.MatchCount == actual.Progress.MatchCount &&
                            reference.Progress.UnverifiedCandidateCount == actual.Progress.UnverifiedCandidateCount + independentlyRejected.Count,
                            "CombatRecall.OriginalObligationsCompleteSemanticMultisetAndCursor:" + fixture.Name + "/" + mode.Label);
                        Require(actual.Continuation.SemanticFingerprint == reference.Continuation.SemanticFingerprint &&
                            actual.Continuation.SnapshotFingerprint == reference.Continuation.SnapshotFingerprint &&
                            actual.Continuation.PendingCandidates.Count == 0 && reference.Continuation.PendingCandidates.Count == 0 &&
                            (actual.Continuation.UnreceivedOrdinals?.Count ?? 0) == 0 &&
                            actual.Continuation.PublishedOrdinals.Order().SequenceEqual(reference.Continuation.PublishedOrdinals.Order()),
                            "CombatRecall.ContinuationAndAcknowledgedDeliveries:" + fixture.Name + "/" + mode.Label);
                        if (fixture.Name == "ft01-original-65539" && mode.Label.StartsWith("c-first-", StringComparison.Ordinal))
                            Require(cExecuted && c.Any(row => row.UsesGpu == !mode.Cpu && row.Inputs > row.Outputs),
                                "CombatRecall.PrimaryRequiresActualCDispatchNotSmallExact:" + mode.Label);
                        if (fixture.PreserveNeutralFiltering && mode.Label == "c-first-cpu")
                            Require(cExecuted && c.Any(row => row.Inputs > row.Outputs), "CombatRecall.NeutralCStillRejects:");
                        outcomes.Add(new { Fixture = fixture.Name, Mode = mode.Label, Status = "Passed", ActualCExecuted = cExecuted,
                            WholeQuerySemanticMultisetEqual = independentlyRejected.Count == 0,
                            IndependentlyProvedUnknownRejections = independentlyRejected.Count });
                    }
                    catch (Exception error) { Remember(fixture.Name, mode.Label, error); }
                }
            }
            catch (Exception error)
            {
                Remember(fixture.Name, "reference-or-preparation", error);
                outcomes.Add(new { Fixture = fixture.Name, Status = "Blocked", Cause = "Dependent comparisons require this fixture's complete original-query Exact reference" });
            }
            WriteEvidence(Path.Combine(output, "combat-recall-results.json"), new { ElapsedSeconds = watch.Elapsed.TotalSeconds, Outcomes = outcomes });
        }
        if (failures.Count != 0) throw new AggregateException("Combat recall acceptance failed; original errors and independent receipts retained", failures);
        Console.WriteLine("COMBAT_RECALL_SEARCH_PASS fixtures=" + fixtures.Count + ";originalQueryDirectExact=true;allResultsAndCursors=true;elapsedSeconds=" + watch.Elapsed.TotalSeconds);

        void Remember(string fixture, string mode, Exception error)
        {
            failures.Add(error);
            outcomes.Add(new { Fixture = fixture, Mode = mode, Status = "Failed", Exception = error.ToString() });
            Console.Error.WriteLine("COMBAT_RECALL_FAILURE fixture=" + fixture + ";mode=" + mode + Environment.NewLine + error);
        }

        async Task<Receipt> Collect(PreparedSearch request, string directory, string label, bool directExact, bool cpuOnly, bool? float64 = null)
        {
            ProductionSearchReplay.LogSessionWorkload(request, label);
            ProductionSearchSession session;
            if (directExact)
            {
                var noFilters = FamilyPlanner.InOrder([]);
                Require(noFilters.Stages.Count == 0 && !noFilters.AllowSmallCandidateExact && !noFilters.FinalValidationSkipped,
                    "CombatRecall.NoFilterNoSmallExactReference");
                session = new ProductionSearchSession(request, noFilters,
                    FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(request, noFilters), noFilters),
                    FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, []), 64, null, "", failOnPhysicalRecovery: true);
            }
            else session = FamilyExecutionCoordinator.Start(request, gpuAvailable: cpuOnly ? false : null,
                gpuFloat64Available: float64, failOnPhysicalRecovery: true);
            await using var owned = session;
            observeSession(session);
            var semantic = new List<string>();
            var warnings = new List<string>();
            using var matches = new StreamWriter(Path.Combine(directory, label + ".matches.jsonl"));
            double nextLog = 0;
            try
            {
                while (!session.Completion.IsCompleted)
                {
                    Drain();
                    if (watch.Elapsed.TotalSeconds >= nextLog)
                    {
                        var runningProgress = session.GetProgress();
                        Console.WriteLine($"COMBAT_RECALL_PROGRESS case={Path.GetFileName(directory)};mode={label};scanned={runningProgress.ScannedCount}/{request.ScanCount};match={runningProgress.MatchCount};unverified={runningProgress.UnverifiedCandidateCount};pending={runningProgress.PendingCandidateCount};groupSeconds={watch.Elapsed.TotalSeconds:F1}");
                        nextLog = watch.Elapsed.TotalSeconds + 10;
                    }
                    if (watch.Elapsed > TimeSpan.FromSeconds(1800))
                    { session.Cancel(); throw new TimeoutException("CombatRecall.GroupDeadline:" + label); }
                    await Task.WhenAny(session.Completion, Task.Delay(5)).ConfigureAwait(false);
                }
                await session.Completion.ConfigureAwait(false);
                Drain();
                var progress = session.GetProgress();
                var continuation = session.CaptureContinuation() ?? throw new InvalidDataException("CombatRecall.MissingContinuation:" + label);
                var invocations = WriteInvocationReceipt(session, directory, label, compatNoFp64: !cpuOnly && float64 == false);
                Require(!session.ExecutionPlan.FinalValidationSkipped && !Flatten(invocations).Any(row => row.Faulted == true),
                    "CombatRecall.NoSkippedExactOrHiddenPhysicalFailure:" + label);
                var diagnostics = session.GetDiagnosticSummary();
                Require(VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, request.CanonicalStartSeed,
                    out ulong start, out _, out _), "CombatRecall.OriginalCanonicalStart:" + label);
                ulong end = checked(start + (ulong)request.ScanCount);
                ProductionSearchReplay.Write(Path.Combine(directory, label + ".continuation.json"), continuation);
                WriteEvidence(Path.Combine(directory, label + ".summary.json"), new {
                    progress, diagnostics, families = session.FamilyIds, DirectOriginalQueryExact = directExact,
                    safeNextOrdinal = session.GetSafeNextOrdinal(), DurableNextOrdinal = continuation.NextOrdinal,
                    SemanticSha256 = Digest(semantic), UnknownsRetainedAsUnverified = true
                });
                Require(progress.State == SearchRunState.Completed && progress.ScannedCount == request.ScanCount &&
                    progress.PendingCandidateCount == 0 && progress.CleanupIssue.Length == 0 && progress.FailureCode.Length == 0 &&
                    continuation.PendingCandidates.Count == 0 &&
                    (continuation.UnreceivedOrdinals?.Count ?? 0) == 0 && continuation.NextOrdinal == end &&
                    session.GetSafeNextOrdinal() == end,
                    "CombatRecall.CompleteRangeWithoutPendingOrHiddenFailure:" + label);
                if (directExact)
                    Require(invocations.Length == 0 && session.FamilyIds.Count == 0 &&
                        diagnostics.DispositionCounts.Values.Sum() == request.ScanCount,
                        "CombatRecall.ReferenceEvaluatedEveryOriginalRootWithRealExact:" + label);
                return new(label, request.SnapshotFingerprint, SearchContinuationIdentity.For(request),
                    semantic.Order(StringComparer.Ordinal).ToArray(), warnings.Order(StringComparer.Ordinal).ToArray(),
                    progress, session.GetSafeNextOrdinal(), continuation, invocations);
            }
            finally { observeSession(null); }

            void Drain()
            {
                var received = new List<SearchCandidate>();
                while (session.TryReadCandidate(out var candidate))
                {
                    if (candidate is null) continue;
                    string identity = candidate.IsUnverified ? request.SnapshotFingerprint : string.Join("|", new[] {
                        request.SnapshotFingerprint, candidate.Authority.EffectSnapshotFingerprint,
                        candidate.Authority.WorldSnapshotFingerprint }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    Require(candidate.SnapshotFingerprint == identity, "CombatRecall.PublishedOriginalAttemptIdentity:" + label);
                    // Production's IsUnfiltered means Exact was absent, not
                    // that the authored request had zero conditions or that a
                    // disclosed optional filter left work for Exact.
                    Require(!candidate.IsUnfiltered, "CombatRecall.RealExactCandidateNeverClaimsValidationSkipped:" + label);
                    matches.WriteLine(FullRecord(candidate));
                    received.Add(candidate);
                }
                if (received.Count == 0) return;
                FlushReceipt(matches);
                foreach (var candidate in received)
                {
                    session.AcknowledgeCandidate(candidate.Seed);
                    semantic.Add(SemanticRecord(candidate));
                    warnings.Add(JsonSerializer.Serialize(new { candidate.Seed, candidate.ExecutionWarnings }, Compact));
                }
            }
        }

        void AddSmallFixtures()
        {
            const int roots = 129;
            string ft01 = fixtureRoot;
            foreach (string name in new[] { "BonesLeafThenSmall", "BonesUnbound", "BonesLeafKaleidoscopeAnyOrder", "NoNNeutral", "SmallCapsuleAfterNewLeaf-original-route-only" })
            {
                var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(ft01, name + "-context.json"));
                var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(ft01, name + "-workload.json"));
                fixtures.Add(new(name, context, workload with { Options = workload.Options with {
                    ScanCount = roots, TargetMatchCount = int.MaxValue, ManualFamilyOrder = null } },
                    "FT01 frozen source query/context; only bounded scan scheduling changed: " + ft01,
                    workload.Options.StartSeed, name.EndsWith("original-route-only", StringComparison.Ordinal) ? SearchDisposition.Unknown : SearchDisposition.Match,
                    PreserveNeutralFiltering: name == "NoNNeutral"));
            }
            var large = BaseGameModelKeys.Relics.LargeCapsule;
            var small = BaseGameModelKeys.Relics.SmallCapsule;
            var prayer = new ModelKey("RELIC", "PRAYER_WHEEL");
            var white = new ModelKey("RELIC", "WHITE_BEAST_STATUE");
            NeowStructuredEffectSearchCondition Nested(ModelKey parent, ModelKey target) => new(parent,
                NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [target]);
            SearchQuery PartyQuery(int slot, SearchQuery personal) => SearchQuery.Empty with {
                Players = captured.Context.Party!.Players.Select((_, index) => new PlayerOfferQuery(index, ModelKeySetFilter.Empty)
                    { Conditions = index == slot ? personal : SearchQuery.Empty }).ToArray() };
            var prayerPersonal = SearchQuery.Empty with { OpeningRoute = new(large),
                CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [new ModelKey("CARD", "NOXIOUS_FUMES")]) };
            string prayerSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, 1867);
            Add("unlisted-prayer-wheel", captured, PartyQuery(1, prayerPersonal), prayerSeed, SearchDisposition.Match,
                "FT01 host-r3 runtime.log:301: actual extra-only NOXIOUS_FUMES witness at ordinal1867; query deliberately has no nested target");
            Add("declared-prayer-wheel", captured, PartyQuery(1, prayerPersonal with { LegacyNeow = prayerPersonal.LegacyNeow with {
                CapsuleContainedRelics = new([], [prayer], []) } }),
                prayerSeed, SearchDisposition.Match, "Separate stronger known-held query; original unlisted query remains separate");
            var first = source.Query.Players.Single(p => p.Slot == 1).Conditions;
            Add("declared-white-beast-original-order", captured, PartyQuery(1, first with { StructuredOpeningEffects = [Nested(small, white)] }),
                "00000000006B", SearchDisposition.Match, "FT01 original root215 contains actual WhiteBeast; separate explicit held obligation");
            for (int slot = 0; slot < Math.Min(2, captured.Context.Party!.Players.Count); slot++)
            {
                var card = captured.Context.Party.Players[slot].EffectAuthority!.CharacterRewardPool!
                    .First(row => row.IsUnlockedInCapturedPool && row.Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common).CardKey;
                Add("party-slot" + slot + "-neutral", captured, PartyQuery(slot, SearchQuery.Empty with {
                    CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [card]) }), "000000000000", null,
                    "Actual captured roster/slot authority; no selected N; no hypothetical held inventory", neutral: true);
            }
            var solo = ProductionSearchReplay.Read<ProductionSearchReplayContext>(soloContextPath);
            var soloCard = solo.Context.Authority.EffectAuthority!.CharacterRewardPool!
                .First(row => row.IsUnlockedInCapturedPool && row.Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common).CardKey;
            var soloQuery = SearchQuery.Empty with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [soloCard]) };
            Add("solo-neutral", solo, soloQuery, "000000000000", null, "Existing solo GameRuntimeCapture corpus: " + soloContextPath, neutral: true);
            Add("solo-small-capsule", solo, soloQuery with { OpeningRoute = new(small) }, "000000000000", null,
                "Existing solo authority: " + soloContextPath + "; latent Capsule held effects; no guarantee of a positive in this short interval");
            Add("solo-declared-white-beast", solo, soloQuery with { OpeningRoute = new(small), StructuredOpeningEffects = [Nested(small, white)] },
                "000000000000", null, "Separate authored held fixture from " + soloContextPath + "; no bag or prediction-state fabrication");
            Add("party-gold-impossible", captured, PartyQuery(1, first with { LegacyCombatRewardConstraints = [new(1,
                ModelKeySetFilter.Empty, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, 0, 0)] }),
                "00000000006B", null, "Independent negative: ordinary full-kill first Monster gold cannot be zero");

            void Add(string name, ProductionSearchReplayContext context, SearchQuery query, string start,
                SearchDisposition? disposition, string provenance, bool neutral = false) => fixtures.Add(new(name, context,
                    new(ProductionSearchReplay.SchemaVersion, query, source.Options with { StartSeed = start, ScanCount = roots,
                        TargetMatchCount = int.MaxValue, ManualFamilyOrder = null }), provenance,
                    disposition.HasValue ? start : null, disposition, neutral));
        }
    }

    // One source-scoped independence theorem, never a seed allowlist. Earlier
    // NewLeaf changes its owner's deck/shared Niche. Direct SmallCapsule's
    // personal Rewards and root UpFront bag have no such dependency under the
    // declared party obtain premise. Every missing Unknown must supply its own
    // complete numerical rejection and source/context identity certificate.
    private static bool TryProveIndependentCombatRejection(PreparedSearch request, string record, out object? proof)
    {
        proof = null;
        using var json = JsonDocument.Parse(record);
        var value = json.RootElement;
        var disposition = JsonSerializer.Deserialize<SearchDisposition>(value.GetProperty("ValidationDisposition").GetRawText(), Compact);
        string issue = value.GetProperty("ValidationIssue").GetString() ?? "";
        if (disposition != SearchDisposition.Unknown ||
            !issue.Contains("NoReliableOpeningRouteMatched", StringComparison.Ordinal) ||
            !issue.Contains("OpeningRewardContinuationPrecisionUnknown", StringComparison.Ordinal)) return false;
        var model = request.Model;
        var children = model.CompiledSearch.PlayerSearches;
        if (model.ProfileId != RuntimeProfileId.Beta111 || model.CompiledSearch.Context.Party?.Players.Count != 2 ||
            children.Count < 2 || children[0].NormalizedQuery.OpeningRoute?.RouteRelicKey != BaseGameModelKeys.Relics.NewLeaf ||
            children[1].NormalizedQuery.OpeningRoute?.RouteRelicKey != BaseGameModelKeys.Relics.SmallCapsule) return false;
        var premise = PartyInitialQuery.CapsuleEffectPremise(children[1].NormalizedQuery);
        if (premise is null || premise.Values.Any(keys => keys.Count != 0)) return false;
        var child = SearchPreparationCompiler.ForPlayer(model, children[1]);
        if (child.Authority.PlayerSlotIndex != 1 || child.Authority.NoRunModifiers != true ||
            !child.Evaluation.NormalCombatRewardConditions.Any(condition => !condition.IsEmpty)) return false;
        string seed = value.GetProperty("Seed").GetString() ?? "";
        ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
        var known = new CombatRewardReplay(child, 1, cpuAssignmentEarly: false);
        var unknown = new CombatRewardReplay(child, null, cpuAssignmentEarly: false);
        if (known.ConservativelyKeeps || unknown.ConservativelyKeeps || !known.UsesObservedOpening ||
            !unknown.UsesObservedOpening || known.RouteCount != 1 || unknown.RouteCount != 1) return false;
        if (known.Plan.Predicates.Length == 0 || known.Plan.Predicates.Any(predicate =>
            predicate.BattleOrdinal != 1 || !predicate.HasCardPredicate || predicate.HasGoldPredicate ||
            predicate.HasPotionDropPredicate || predicate.HasPotionIdentityPredicate)) return false;
        if (!known.Plan.CardPoolAuthorityExact || !known.Plan.PotionPoolAuthorityExact) return false;
        var left = known.Opening(root, 0);
        var right = unknown.Opening(root, 0);
        if (!left.ExactProjectionAuthority || !right.ExactProjectionAuthority || !left.EquivalentTo(right) ||
            known.MatchesReference(root) || unknown.MatchesReference(root)) return false;
        var leftNumeric = Beta110CombatRewardFastStage.EvaluateForFamily(left, known.Plan, known.Catalog,
            child.Ascension, new CombatRewardCardSelection(known.Catalog));
        var rightNumeric = Beta110CombatRewardFastStage.EvaluateForFamily(right, unknown.Plan, unknown.Catalog,
            child.Ascension, new CombatRewardCardSelection(unknown.Catalog));
        if (leftNumeric.Status != Beta110CombatRewardRouteProjectionStatus.Rejected ||
            rightNumeric.Status != Beta110CombatRewardRouteProjectionStatus.Rejected) return false;
        if (!RelicFamilyPlanCompiler.TryCompilePool(child, out var pool, out var dense, out _, out _)) return false;
        var keysById = dense.ToDictionary(pair => pair.Value, pair => pair.Key);
        var upfront = new Beta110FastRng(unchecked(root + RolltheSpire2.Core.Seed.XxHash64.Hash("up_front"u8, 0)));
        var bags = new ushort[3][];
        for (int bucket = 0; bucket < pool!.BucketCount; bucket++)
        {
            int count = pool.BucketLengths[bucket], kind = pool.BucketKinds[bucket];
            if (pool.BucketScopes[bucket] == 0 || kind == 4) { upfront.ConsumeUnstableShuffle(count); continue; }
            if (kind is < 1 or > 3) return false;
            var entries = pool.DenseRelicIds.AsSpan(pool.BucketOffsets[bucket], count).ToArray();
            upfront.UnstableShuffle(entries.AsSpan()); bags[kind - 1] = entries;
        }
        var rewards = new Beta110FastRng(unchecked(root + 1 + RolltheSpire2.Core.Seed.XxHash64.Hash("rewards"u8, 0)));
        float roll = rewards.NextFloat();
        int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
        while (lane < 3 && bags[lane].Length == 0) lane++;
        var held = lane < 3 ? keysById[bags[lane][0]] : BaseGameModelKeys.OrdinaryRelics.Circlet;
        proof = new {
            Rule = "P1NewLeaf.P2DirectSmallCapsule.NoAuthoredObtain.IndependentCardContradiction.20261102.v1",
            Seed = seed, RootHash = root.ToString("X16"), OriginalDisposition = disposition, OriginalIssue = issue,
            OriginalSemanticRecord = record, OriginalQuery = model.CompiledSearch.Query,
            model.CompiledSearch.SemanticFingerprint, request.SnapshotFingerprint,
            child.Authority.EffectSnapshotFingerprint, child.Authority.WorldSnapshotFingerprint,
            ProductMvid = typeof(CombatRewardReplay).Assembly.ManifestModule.ModuleVersionId,
            NumericRevision = known.CpuRevision, ActualHeld = held, CapsuleRarityRoll = roll,
            KnownNicheNumericStatus = leftNumeric.Status, UnknownNicheNumericStatus = rightNumeric.Status,
            BothCompleteNumericEvaluationsReject = true, BothProjectionAuthoritiesExact = true,
            RewardStateIdenticalWithUnknownNiche = true,
            RewardRng = left.Rewards.CaptureCheckpoint(), left.InfluenceFlags, left.AdditionalCardRewardCount,
            left.PotionOdds, left.CardRarityOffset, left.LastingCandyCounter, left.FixedGoldAmount,
            Boundary = "Earlier source affects own deck/shared Niche only; direct Capsule personal Rewards/root UpFront independent; all allowed prior transforms covered; original Exact Unknown retained, not rewritten; no game execution claimed"
        };
        return true;
    }

    private static bool HasDisposition(string record, string seed, SearchDisposition disposition)
    {
        using var json = JsonDocument.Parse(record);
        return json.RootElement.GetProperty("Seed").GetString() == seed &&
            JsonSerializer.Deserialize<SearchDisposition>(json.RootElement.GetProperty("ValidationDisposition").GetRawText(), Compact) == disposition;
    }
}
