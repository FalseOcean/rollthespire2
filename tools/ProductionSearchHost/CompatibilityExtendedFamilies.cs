using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class CompatibilityChecks
{
    private const int ExtendedWitnessLimit = 131072;

    // New requests, using the caller's unchanged immutable capture. These are
    // separate fixtures, not a weakening of the caller's original workload.
    internal static async Task CheckExtendedFamiliesAsync(PreparedSearch template, string output)
    {
        if (template.Model.ProfileId != RuntimeProfileId.Beta111 || template.Model.CompiledSearch.Context.Party is not null ||
            template.Model.Authority.PlayersCount != 1 || template.RunOptions.SkipExactValidation)
            throw new ArgumentException("CompatibilityExtended.RequiresCapturedSoloBeta111WithExact");
        string directory = Path.Combine(output, "extended-family-parity");
        Directory.CreateDirectory(directory);
        var context = template.Model.CompiledSearch.Context;
        var options = template.RunOptions with { StartSeed = "000000000000", ScanCount = 4099,
            TargetMatchCount = int.MaxValue, WorkerCount = 1 };
        var cases = new List<(string Label, PreparedSearch Request, object Plan)>();

        // Existing maintained NR fixture shape; use its query without inventing
        // or replacing either World or Effect authority in the caller's capture.
        string capsuleCorpus = ProjectSettings.GlobalizePath("res://Corpus/v1/cases/NR-small-capsule.json");
        var capsule = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(capsuleCorpus);
        var capsuleRequest = Compile(new(ProductionSearchReplay.SchemaVersion, capsule.Query, options), context);
        capsuleRequest = FindExtendedCapsuleWitness(capsuleRequest, requireWholeExact: true);
        cases.Add(("rfull-tracked-small", capsuleRequest, CheckExtendedCapsulePlan(capsuleRequest, generic: false)));

        // Same opening-only shapes as TransformationAggregateChecks. Full target
        // multisets come from Core's actual projection at an admitted identity,
        // including repeated target instances; nothing is assigned per source.
        foreach (var opening in new[] { TransformationOpening.LeafyPoultice,
            TransformationOpening.NewLeaf, TransformationOpening.BonesLeafyNewLeaf })
        {
            var request = FindExtendedTransformationWitness(context, options, opening);
            cases.Add(("t-" + opening, request, CheckExtendedTransformationPlan(request)));
        }

        var rows = new List<object>();
        foreach (var item in cases)
        {
            string caseDirectory = Path.Combine(directory, item.Label);
            Directory.CreateDirectory(caseDirectory);
            ExportExtendedRequest(item.Request, caseDirectory, "CallerCapturedImmutableContext;NewBoundedExtendedFixture");
            var exact = ProductionExactSearchEvaluator.Evaluate(item.Request.Model,
                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, item.Request.CanonicalStartSeed));
            Require(exact.IsMatch, "ExtendedFixtureWholeExactPositive:" + item.Label);
            await CheckPublicFamilyParityAsync(item.Request, caseDirectory);
            rows.Add(new { item.Label, item.Request.CanonicalStartSeed, item.Request.SnapshotFingerprint,
                WholeExactStartDisposition = exact.Evaluation.Disposition.ToString(), item.Plan,
                ParityReceipt = Path.Combine(item.Label, "public-family-parity.json") });
        }
        WriteEvidence(Path.Combine(directory, "extended-families.json"), new
        {
            Passed = true, Cases = rows,
            FixtureSources = new[] { capsuleCorpus, "tests/functional/Rt2FunctionalChecks/TransformationAggregateChecks.cs" },
            AuthoritySource = "Caller immutable context; no authority fields replaced",
            OriginalRequestSnapshotFingerprint = template.SnapshotFingerprint,
            CompleteOrderedAbi1Compared = true, WholeExactStartPositiveRequired = true,
            OriginalWorkloadModified = false, TargetMetalExecutionClaimed = false, ThroughputClaimed = false
        });
        GD.Print($"COMPATIBILITY_EXTENDED_FAMILIES_PASS cases={cases.Count};wholeExactPositive=true;targetMetalClaimed=false");
    }

    // Call only after the existing PartyPhase1 host has initialized the game
    // data. No ModelDb/SaveManager setup or synthetic authority lives here.
    internal static async Task CheckExtendedPartyFamiliesAsync(GameVersionDetection detection, string output)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        if (RuntimeProfileRegistry.Select(detection).ProfileId != RuntimeProfileId.Beta111)
            throw new ArgumentException("CompatibilityExtendedParty.RequiresBeta111");
        const string seed = "000000000000";
        var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, seed,
            BaseGameModelKeys.Characters.All.Take(2).ToArray(),
            [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, detection.NormalizedVersion);
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
            10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };

        // This real Any-order K+Large arrival cannot use tracked Rfull's neutral
        // companion checkpoint, so the existing factory must select GenericReplay.
        // Anchor is the maintained NR corpus target, not a made-up pool member.
        var local = SearchQuery.Empty with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                [BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.Kaleidoscope]),
            StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.LargeCapsule,
                NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "ANCHOR")])]
        };
        var query = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = local },
            new(1, ModelKeySetFilter.Empty)] };
        var options = new SearchRunOptions(seed, 4099, int.MaxValue, 1);
        var parent = Compile(new(ProductionSearchReplay.SchemaVersion, query, options), context);
        var child = ExtendedPersonalRun(parent, parent.Model.CompiledSearch.PlayerSearches[0]);
        child = FindExtendedCapsuleWitness(child, requireWholeExact: false, bindStart: start =>
        {
            var boundParent = Compile(new(ProductionSearchReplay.SchemaVersion, query,
                options with { StartSeed = start }), context);
            return ExtendedPersonalRun(boundParent, boundParent.Model.CompiledSearch.PlayerSearches[0]);
        });
        // Recompile the same captured party and project the same seat at the
        // selected start. Never splice an independent personal authority into it.
        parent = Compile(new(ProductionSearchReplay.SchemaVersion, query,
            options with { StartSeed = child.CanonicalStartSeed }), context);
        child = ExtendedPersonalRun(parent, parent.Model.CompiledSearch.PlayerSearches[0]);
        Require(child.Model.Authority.PlayersCount == 2 && child.Model.Authority.PlayerSlotIndex == 0 &&
            ReferenceEquals(child.Model.Authority, party.Players[0]), "ExtendedGenericUsesCapturedPartySeat");
        object plan = CheckExtendedCapsulePlan(child, generic: true);
        string directory = Path.Combine(output, "extended-party-rfull-generic");
        Directory.CreateDirectory(directory);
        ExportExtendedRequest(parent, directory, "GameRuntimeCapture;ExplicitFullUnlockFixturePremise;SameCapturedParty");
        string personalDirectory = Path.Combine(directory, "personal-module");
        Directory.CreateDirectory(personalDirectory);
        ExportExtendedRequest(child, personalDirectory, "ProductionForPlayerProjectionOfSameCapturedParty;Seat0");
        var exact = ProductionExactSearchEvaluator.Evaluate(parent.Model,
            TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, child.CanonicalStartSeed));
        await CheckPublicFamilyParityAsync(child, personalDirectory, float64Available =>
        {
            if (!CapsuleRelicFamily.TryPrepare(child.Model, FilterExecutionPreferences.From(child.RunOptions), out var prepared, preferGpu: true,
                numericalVariant: float64Available ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne) ||
                prepared is not { GpuPlan.GenericReplay: true } || !prepared.Body.UsesGpu)
                throw new InvalidDataException("CompatibilityExtendedParty.ActualGenericGpuFactoryNotAdmitted");
            // This is the actual party child factory used by the registrar,
            // not a whole-query request sent through the solo registrar.
            return new[] { prepared.Body };
        });
        WriteEvidence(Path.Combine(directory, "extended-party-generic.json"), new
        {
            Passed = true, child.CanonicalStartSeed, Plan = plan, PartyFingerprint = party.Fingerprint,
            ParentRequestSnapshotFingerprint = parent.SnapshotFingerprint,
            PersonalRequestSnapshotFingerprint = child.SnapshotFingerprint,
            ParentWholeExactStartDisposition = exact.Evaluation.Disposition.ToString(),
            ParentWholeExactPositive = exact.IsMatch,
            Scope = "Actual captured two-player seat-0 Rfull GenericReplay public module; native/IntegerRne/unconditioned CPU ordered ABI1",
            WholePartyPipelineParityClaimed = false, WholePartyCompletionClaimed = false,
            ExplicitUnlockPremise = "Both seats UnlockState.all, as in existing PartyPhase1 checks",
            TargetMetalExecutionClaimed = false, ThroughputClaimed = false
        });
        GD.Print("COMPATIBILITY_EXTENDED_PARTY_RFULL_GENERIC_PASS personalModule=true;wholePartyPipelineClaimed=false");
    }

    private static PreparedSearch FindExtendedTransformationWitness(SearchContext context,
        SearchRunOptions options, TransformationOpening opening)
    {
        var condition = new TransformationAggregateCondition(opening, TransformationPickupOrder.Any,
            false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, []);
        var probe = Compile(new(ProductionSearchReplay.SchemaVersion,
            SearchQuery.Empty with { TransformationAggregate = condition }, options), context);
        var numerical = new TransformationAggregateNumericalPlan(probe.Model);
        Require(numerical.Neow is not null, "ExtendedTRequiresActualOpeningPlan");
        var source = opening switch
        {
            TransformationOpening.LeafyPoultice => BaseGameModelKeys.Relics.LeafyPoultice,
            TransformationOpening.NewLeaf => BaseGameModelKeys.Relics.NewLeaf,
            _ => BaseGameModelKeys.Relics.NeowsBones
        };
        for (ulong ordinal = 0; ordinal < ExtendedWitnessLimit; ordinal++)
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            if (!NeowFamilyReplay.Observe(root, numerical.Neow!).IdentityPass) continue;
            var projection = new NeowEffectProjectionEngine(Beta111Profile.Instance,
                "compatibility-extended-t", root, context.Authority).Project(source);
            ModelKey[] cards = opening == TransformationOpening.BonesLeafyNewLeaf
                ? projection.BonesOutcome!.OriginalRoutes[0].RelicScopedResults.SelectMany(r => r.EffectGroups)
                    .SelectMany(g => g.OrderedItems).Where(i => i.Kind == PredictedEffectKind.TransformCard)
                    .Select(i => i.TargetKey!.Value).ToArray()
                : projection.EffectGroups.SelectMany(g => g.OrderedItems)
                    .Where(i => i.Kind == PredictedEffectKind.TransformCard).Select(i => i.TargetKey!.Value).ToArray();
            Require(cards.Length == condition.OpportunityCount, "ExtendedTActualCoreOutputCount:" + opening);
            var query = SearchQuery.Empty with { TransformationAggregate = condition with
                { Predicate = TransformationAggregatePredicate.ContainsMultiset, MinimumRareCount = 0, TargetMultiset = cards } };
            var request = Compile(new(ProductionSearchReplay.SchemaVersion, query, options with { StartSeed = seed }), context);
            if (new TransformationAggregateNumericalPlan(request.Model).Matches(root) &&
                ProductionExactSearchEvaluator.Evaluate(request.Model,
                    TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch) return request;
        }
        throw new InvalidDataException("CompatibilityExtended.NoBoundedWholeExactTWitness:" + opening);
    }

    private static PreparedSearch FindExtendedCapsuleWitness(PreparedSearch probe, bool requireWholeExact,
        Func<string, PreparedSearch>? bindStart = null)
    {
        Require(CapsuleRelicFamily.TryPrepare(probe.Model, FilterExecutionPreferences.From(probe.RunOptions), out var cpu, preferGpu: false) && cpu is not null, "ExtendedCapsuleCpuOwnerAvailable");
        var route = NeowReplayPlan.Compile(probe.Model, true);
        for (ulong ordinal = 0; ordinal < ExtendedWitnessLimit; ordinal++)
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            if (!NeowFamilyReplay.Observe(root, route).IdentityPass || !cpu!.Replay.Matches(root)) continue;
            var request = bindStart is not null ? bindStart(seed) :
                Compile(new(ProductionSearchReplay.SchemaVersion, probe.Model.CompiledSearch.Query,
                    probe.RunOptions with { StartSeed = seed }), probe.Model.CompiledSearch.Context);
            if (!requireWholeExact || ProductionExactSearchEvaluator.Evaluate(request.Model,
                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch) return request;
        }
        throw new InvalidDataException("CompatibilityExtended.NoBoundedCapsuleWitness:wholeExactRequired=" + requireWholeExact);
    }

    private static object CheckExtendedCapsulePlan(PreparedSearch request, bool generic)
    {
        Require(CapsuleRelicFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var native, preferGpu: true) &&
            native is { GpuPlan: not null }, "ExtendedRfullNativeGpuRequired");
        Require(CapsuleRelicFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var integer, preferGpu: true,
            numericalVariant: GpuNumericalVariant.IntegerRne) &&
            integer is { GpuPlan: not null }, "ExtendedRfullIntegerGpuRequired");
        var n = native!.GpuPlan!;
        var i = integer!.GpuPlan!;
        Require(n.GenericReplay == generic && i.GenericReplay == generic &&
            i.NumericalVariant == GpuNumericalVariant.IntegerRne &&
            integer!.Body.NumericRequirements == GpuNumericRequirements.Int64 &&
            (integer.Body.QuotePhysicalWork(new(false, false, 4099)) is not { } rQuote || FamilyCoarsePricing.IsEstimate(rQuote)),
            "ExtendedRfullActualVariantAndHonestEstimateOrUnknownPrice");
        return new { Family = native!.Body.FamilyId, GenericReplay = generic, NativeRevision = n.Revision(false),
            IntegerRevision = i.Revision(false), i.ConfiguredGenericBagCapacity,
            HistoricalGlobalNextIntPreserved = true, ActualGpuExecutionReceipt = "public-family-parity.json" };
    }

    private static object CheckExtendedTransformationPlan(PreparedSearch request)
    {
        Require(TransformationAggregateFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var native, true) &&
            native is { GpuPlan.Supported: true }, "ExtendedTNativeGpuRequired");
        Require(TransformationAggregateFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var integer, true,
            GpuNumericalVariant.IntegerRne) && integer is { GpuPlan.Supported: true },
            "ExtendedTIntegerGpuRequired");
        var n = native!.GpuPlan;
        var i = integer!.GpuPlan;
        bool fullTarget = request.Model.Evaluation.TransformationAggregate!.Opening != TransformationOpening.NewLeaf;
        Require(n.FullTarget == fullTarget && i.FullTarget == fullTarget && n.BonesOptimized == i.BonesOptimized,
            "ExtendedTRequiresNewLeafGenericAndLeafyBonesFullTarget;BaselineExperimentControlsNotApplicable");
        Require(i.NumericalVariant == GpuNumericalVariant.IntegerRne &&
            integer!.Body.NumericRequirements == GpuNumericRequirements.Int64 &&
            (integer.Body.QuotePhysicalWork(new(false, false, 4099)) is not { } tQuote || FamilyCoarsePricing.IsEstimate(tQuote)),
            "ExtendedTActualVariantAndHonestEstimateOrUnknownPrice");
        return new { Family = native!.Body.FamilyId, request.Model.Evaluation.TransformationAggregate!.Opening,
            n.FullTarget, n.BonesOptimized, NativeRevision = n.Revision(false), IntegerRevision = i.Revision(false),
            ActualGpuExecutionReceipt = "public-family-parity.json" };
    }

    private static PreparedSearch ExtendedPersonalRun(PreparedSearch parent, CompiledSearch player)
    {
        var admitted = SearchPreparationCompiler.Compile(player,parent.RunOptions).Plan
            ?? throw new InvalidDataException("CompatibilityExtended.PersonalRunAdmission");
        return admitted with { Model = SearchPreparationCompiler.ForPlayer(parent.Model,player) };
    }

    private static void ExportExtendedRequest(PreparedSearch request, string directory, string provenance)
    {
        ProductionSearchReplay.Write(Path.Combine(directory, "context.json"),
            new ProductionSearchReplayContext(ProductionSearchReplay.SchemaVersion, provenance, request.Model.CompiledSearch.Context));
        ProductionSearchReplay.Write(Path.Combine(directory, "workload.json"),
            new ProductionSearchReplayWorkload(ProductionSearchReplay.SchemaVersion, request.Model.CompiledSearch.Query,
                request.RunOptions with { StartSeed = request.CanonicalStartSeed, ScanCount = request.ScanCount }));
    }
}
