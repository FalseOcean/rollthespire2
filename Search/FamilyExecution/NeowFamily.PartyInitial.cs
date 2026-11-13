using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record NeowPartyStage(int Slot, FilterBodyDescription Family, NeowReplayPlan Replay, NeowFamilyGpuPlan? Gpu);

internal sealed partial class NeowFamily
{
    internal static bool TryCreateParty(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu, GpuPlanFactory? gpuPlanFactory = null)
    {
        body = null;
        if (!model.CompiledSearch.NormalizedQuery.Players.Any(PartyInitial.HasConditions)) return false;
        try { body = new PartyInitial(model, preferences, preferGpu, gpuPlanFactory: gpuPlanFactory).Describe(); return true; }
        catch (InvalidOperationException error) when (error.Message is "NeowFamilyReplayAuthorityUnavailable" or "NeowFamilyUnmappedIdentity" ||
            error.Message.StartsWith("DonorContinuationAuthorityUnavailable:", StringComparison.Ordinal))
        {
            RuntimeLog.TryBackgroundWarning("partyNeowFamilyUnavailable=true;phase=CpuReplayCompilation;replacement=None;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed;exceptionJson=" + RuntimeLog.SafeJson(error.ToString()));
            return false;
        }
    }
    // Pure ordered-arrival preparation, not a running invocation.
    internal sealed partial class PartyInitial
    {
        private const string CpuRevision = "N.Neow.PartyEffects.Cpu.20261016.v6";
        private const int CpuCapacity = 65536;
        private readonly string _identity;
        private readonly IRuntimeProfile _profile;
        private readonly int _workers, _requestedWorkers, _playersCount;
        private readonly bool _optimize, _skipExactValidation;
        private readonly string _rulesRevision;
        private readonly bool? _fuse;
        private readonly FamilySurvivalProjection _survival;
        private readonly NeowPartyProgram _program;
        private readonly List<string> _remainders = [];
        private readonly List<NeowReplayPlan> _cpuPlans = [];
        private readonly List<NeowPartyPlayer> _cpuPlayers = [];
        private readonly List<NeowPartyStage> _gpuStages = [];
        internal string GpuPreparationFailureDetail { get; private set; } = "";
        private sealed record PlayerPreparation(PlayerOfferQuery Player, SearchModel Personal, NeowReplayPlan[] Predicates);
        public string FamilyId => "N.Neow";
        internal int CpuWindowCapacity => CpuCapacity;
        private bool UsesCompatibilityGpu => _gpuStages.Any(s => s.Gpu?.NumericalVariant == NeowGpuNumericalVariant.IntegerUnitFloat);
        private bool UsesPrivate => _optimize && _gpuStages.Count > 1 && _gpuStages.All(s => s.Family.CanBindPrivateSerial);
        private bool UsesFused => _optimize && _fuse != false && _gpuStages.Count > 1 &&
            _gpuStages.All(s => s.Gpu?.NumericalVariant == NeowGpuNumericalVariant.NativeFloat64) && (_fuse == true || PreferFused());
        private bool HasOrderedKBody => _gpuStages.Any(s => s.Replay.StructuredConditions.Any(c => c.OrderedKaleidoscope));
        private bool PreferFused()
        {
            var first = _gpuStages[0].Gpu!;
            return first.Meta[82] is 0 or 4 && (first.Meta[56] & 2) == 0 &&
                _gpuStages.All(s => s.Gpu is { } plan && plan.Meta[82] == first.Meta[82] &&
                    plan.Meta[81] == first.Meta[81] && (plan.Meta[56] & 2) == 0);
        }
        internal PartyInitial(SearchModel request, FilterExecutionPreferences preferences, bool preferGpu = false, bool optimize = true, bool? fuse = null,
            GpuPlanFactory? gpuPlanFactory = null)
        {
            _optimize = optimize; _fuse = fuse;
            _identity = request.SnapshotFingerprint; _profile = RuntimeProfileRegistry.Select(request.Detection);
            _rulesRevision = request.Authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113 ? ".Modern113" : "";
            _workers = Math.Clamp(preferences.WorkerCount, 1, 32); _requestedWorkers = preferences.WorkerCount;
            _skipExactValidation = preferences.SkipExactValidation;
            _playersCount = request.CompiledSearch.NormalizedQuery.Players.Count;
            var offers = PartyStageSurvival.Project(request, FamilyId);
            _survival = offers.SurvivalProbability is double p ? FamilySurvivalProjection.Resolved(FamilyId, p,
                "PartyN.OfferGateUpperEnvelope;ResultArrivalUnionsAndExactRemaindersNotPaid;" + offers.Evidence) :
                FamilySurvivalProjection.Unresolved(FamilyId, "PartyN.OfferProbabilityUnavailable;" + offers.Evidence);
            var preparations = new List<PlayerPreparation>();
            {
                (int Niche, int Potions)[]? arrivals = [(0, 0)];
                foreach (var player in request.CompiledSearch.NormalizedQuery.Players)
                {
                    if (!HasConditions(player)) continue; // Unselected N is a neutral shared prefix.
                    // Initial offers are independent. Result sieves below union possible
                    // arrivals; only whole-table Exact selects a joint witness.
                    var route = player.Conditions.OpeningRoute ?? (player.SelectedOption is { } selected ? new(selected.Option) : null);
                    var compiledPersonal = request.CompiledSearch.PlayerSearches[player.Slot];
                    if (player.SelectedOption is { } selectedOption)
                        compiledPersonal = SearchCompiler.CompilePlayer(player.Conditions with
                        { OpeningRoute = new(selectedOption.Option), StructuredOpeningEffects = player.Results }, compiledPersonal.Context);
                    var personal = SearchPreparationCompiler.ForPlayer(request, compiledPersonal);
                    var effectPlan = NeowReplayPlan.Compile(personal, false);
                    var predicateArrivals = route is null && !compiledPersonal.NormalizedQuery.HasCombatRewardConstraints
                        ? new[] { (Niche: 0, Potions: 0) } : arrivals;
                    bool hasResults = effectPlan.StructuredConditions.Length > 0 || effectPlan.HasFinalCurseFastProjection;
                    if (hasResults && effectPlan.ContinuationIssue() is { Length: > 0 } continuationIssue)
                        throw new InvalidOperationException(continuationIssue);
                    bool hasPairPredicate = (effectPlan.BonesAny | effectPlan.BonesAll | effectPlan.BonesBan) != 0 ||
                        effectPlan.First != 255 || effectPlan.Second != 255;
                    // Route presence is already part of the initial-offer gates.
                    // A plain Bones selection needs no child-pair replay here.
                    bool hasPredicate = hasResults || hasPairPredicate || effectPlan.RequireBones ||
                        effectPlan.Filter.RequireSmallCapsule || effectPlan.Filter.RequireLargeCapsule;
                    bool hooksKnown = !hasResults || AuthoredOpeningHooksSupported(personal);
                    NeowReplayPlan[] parts = [];
                    if (hasPredicate)
                    {
                        var predicate = effectPlan;
                        if (!hasResults && !hasPairPredicate)
                            predicate = predicate with { Bones = false, RequireBones = effectPlan.RequireBones || effectPlan.Bones };
                        if (!hooksKnown)
                            predicate = IdentityOnly(predicate);
                        else if (predicateArrivals is null)
                        {
                            // Private Rewards/Transformations predicates remain usable
                            // when a preceding player's shared arrival is unresolved.
                            predicate = predicate with
                            {
                                StructuredConditions = predicate.StructuredConditions.Where(c => !ReadsShared(c)).ToArray(),
                                EnabledDomains = predicate.EnabledDomains & ~Beta110FastDomain.FinalCurse,
                                RequiredFinalCurseIds = [], BannedFinalCurseIds = []
                            };
                        }
                        bool sharedDependent = predicate.HasFinalCurseFastProjection || predicate.StructuredConditions.Any(ReadsShared);
                        predicate = predicate with { SharedArrivals = sharedDependent ? predicateArrivals! : [] };
                        parts = PredicateParts(predicate).ToArray();
                        _cpuPlans.AddRange(parts);
                        if (!hooksKnown || predicateArrivals is null && (effectPlan.HasFinalCurseFastProjection || effectPlan.StructuredConditions.Any(ReadsShared)))
                        {
                            _remainders.Add($"slot={player.Slot};UnmodeledAuthoredHookOrPrefixAuthority;ProductionExactOnly");
                            RuntimeLog.TryBackgroundInfo($"partyNeowEffectRemainder=true;slot={player.Slot};reason=UnmodeledAuthoredHookOrPrefixAuthority;owner=WholeTableExact");
                        }
                    }
                    if (effectPlan.ExactOnly.Length > 0)
                    {
                        _remainders.AddRange(effectPlan.ExactOnly.Select(reason => $"slot={player.Slot};{reason};ProductionExactOnly"));
                        RuntimeLog.TryBackgroundInfo($"partyNeowEffectRemainder=true;slot={player.Slot};reason={string.Join(',', effectPlan.ExactOnly)};owner=WholeTableExact");
                    }
                    var advances = SharedAdvanceOptions(personal, effectPlan);
                    arrivals = arrivals is null || advances is null ? null : arrivals.SelectMany(a => advances.Select(b =>
                        (Niche: a.Niche + b.Niche, Potions: a.Potions + b.Potions))).Distinct().OrderBy(x => x.Niche).ThenBy(x => x.Potions).ToArray();
                    if (arrivals?.Length > 4096) arrivals = null;
                    preparations.Add(new(player, personal, parts));
                    var gates = new[] { player.Offers, personal.Evaluation.NeowRelics }
                        .Concat(player.Conditions.LegacyWorld.AncientOptionFilters.Where(f => f.Act == 1).Select(f => f.Keys)).ToArray();
                    var masks = new List<(ulong Any, ulong All, ulong Ban)>();
                    bool numerical = true;
                    foreach (var gate in gates)
                    {
                        if (!NeowNumericCompilation.TryBuildMask(gate.Any, out ulong any) ||
                            !NeowNumericCompilation.TryBuildMask(gate.All, out ulong all) ||
                            !NeowNumericCompilation.TryBuildMask(gate.Ban, out ulong ban)) { numerical = false; break; }
                        masks.Add((any, all, ban));
                    }
                    // Keep selected and authored route as separate obligations.
                    foreach (var key in new ModelKey?[] { player.SelectedOption?.Option, player.Conditions.OpeningRoute?.RouteRelicKey })
                    {
                        if (key is not { } required) continue;
                        if (!Beta110FastRelicCatalog.TryGetId(required, out byte id)) { numerical = false; break; }
                        masks.Add((0, Beta110FastRelicCatalog.Bit(id), 0));
                    }
                    _cpuPlayers.Add(new(request.CompiledSearch.Context.Party!.Players[player.Slot], gates,
                        new ModelKey?[] { player.SelectedOption?.Option, player.Conditions.OpeningRoute?.RouteRelicKey }
                            .Where(key => key.HasValue).Select(key => key!.Value).ToArray(),
                        IdentityOnly(effectPlan) with { Bones = false }, parts, numerical ? masks.ToArray() : null));
                }
            }
            if (_cpuPlans.Any(plan => plan.CapsulesWithKnownZeroNonCombatAdvance != 0))
                _rulesRevision += NeowCapsuleNonCombatContinuation.Revision;
            if (preferGpu) PrepareGpuStages(request, preferences, preparations, gpuPlanFactory);
            _program = new(_cpuPlayers.ToArray(), _cpuPlans.ToArray(), optimize);
        }
        private void PrepareGpuStages(SearchModel request, FilterExecutionPreferences preferences, IReadOnlyList<PlayerPreparation> preparations, GpuPlanFactory? gpuPlanFactory)
        {
            // Every slot's CPU predicate already exists. Only optional physical
            // stages are discarded if their preparation fails; no partial CPU takeover.
            void UseCompleteCpu(string reason) {
                _gpuStages.Clear();
                RuntimeLog.TryBackgroundInfo($"partyNeowGpuUnavailable=true;selected=CPU;scope=N.Neow;cpuPlans={_cpuPlans.Count};allSlotsCompiled=true;remainder=WholeTableExact;reason={reason}");
            }
            bool Add(SearchModel child, NeowReplayPlan plan)
            {
                var family = new NeowFamily(child, plan, gpuPlanFactory: gpuPlanFactory);
                if (family.GpuPreparationFailed)
                {
                    GpuPreparationFailureDetail = $"slot={plan.Authority.PlayerSlotIndex};" + family.GpuPreparationFailureDetail;
                    UseCompleteCpu("GpuStageConstructionFailed"); return false;
                }
                _gpuStages.Add(new(plan.Authority.PlayerSlotIndex, family.Describe(preferences), plan, family.GpuPlan));
                if (family.GpuPlan is null)
                    RuntimeLog.TryBackgroundInfo($"partyNeowCpuStage=true;slot={plan.Authority.PlayerSlotIndex};reason=GpuPlanNotAdmitted;predicate=NumericalReplay;owner=WholeTableExact");
                return true;
            }
            try
            {
                foreach (var preparation in preparations)
                {
                    var player = preparation.Player;
                    int gateStart = _gpuStages.Count;
                    var route = player.Conditions.OpeningRoute ?? (player.SelectedOption is { } selected ? new(selected.Option) : null);
                    var gates = new[] { player.Offers, preparation.Personal.Evaluation.NeowRelics }
                        .Concat(player.Conditions.LegacyWorld.AncientOptionFilters.Where(f => f.Act == 1).Select(f => f.Keys))
                        .Where(f => !f.IsEmpty).ToArray();
                    if (gates.Length == 0 && route is not null) gates = [ModelKeySetFilter.Empty];
                    foreach (var offers in gates)
                    {
                        var required = route is null ? offers.All : offers.All.Append(route.RouteRelicKey).Distinct().ToArray();
                        var query = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with
                            { NeowRelics = new(offers.Any, required, offers.Ban) } };
                        var compiled = SearchCompiler.CompilePlayer(query, request.CompiledSearch.PlayerSearches[player.Slot].Context);
                        var child = SearchPreparationCompiler.ForPlayer(request, compiled);
                        if (!Add(child, NeowReplayPlan.Compile(child, false))) return;
                    }
                    int gateCount = _gpuStages.Count - gateStart;
                    foreach (var part in preparation.Predicates)
                        if (!Add(preparation.Personal, part)) return;
                    if (_optimize && preparation.Predicates.Length > 0 && player.Offers.IsEmpty &&
                        !player.Conditions.LegacyWorld.AncientOptionFilters.Any(f => f.Act == 1 && !f.IsEmpty))
                        _gpuStages.RemoveRange(gateStart, gateCount);
                }
                if (!_gpuStages.Any(s => s.Family.ConditionPerformance.UsesGpu)) UseCompleteCpu("NoGpuStagesAdmitted");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                GpuPreparationFailureDetail = ex.ToString();
                UseCompleteCpu("GpuStagePreparationFailed");
                RuntimeLog.TryBackgroundWarning("partyNeowGpuPreparationFailed=true;exceptionJson=" + RuntimeLog.SafeJson(ex.ToString()));
            }
        }
        private static bool ReadsShared(Beta110FastStructuredCondition condition) => ReadsSharedSource(condition.SourceRelicId);
        private static bool ReadsSharedSource(byte source) => source is
            Beta110FastRelicCatalog.NewLeaf or Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.PhialHolster;

        private static NeowReplayPlan IdentityOnly(NeowReplayPlan plan) => plan with
        { StructuredConditions = [], EnabledDomains = Beta110FastDomain.None,
        RequiredFinalCurseIds = [], BannedFinalCurseIds = [], AuthoredUpgrades = null,
        CapsulesWithKnownZeroNonCombatAdvance = 0 };

        // The GPU body stores one predicate per source. Repeated authored rows
        // form a conjunction of necessary route unions; Exact binds one witness.
        private static IEnumerable<NeowReplayPlan> PredicateParts(NeowReplayPlan plan)
        {
            var groups = plan.StructuredConditions.GroupBy(c => c.SourceRelicId).Select(g => g.ToArray()).ToArray();
            int count = groups.Select(g => g.Length).DefaultIfEmpty(1).Max();
            bool packed = groups.All(g => g.Length == 1 ||
                g[0].SourceRelicId is Beta110FastRelicCatalog.LostCoffer or Beta110FastRelicCatalog.ScrollBoxes &&
                g.Select(c => c.Kind).Distinct().Count() == g.Length);
            if (packed) { yield return plan; yield break; }
            for (int i = 0; i < count; i++)
                yield return plan with { StructuredConditions = groups.Where(g => i < g.Length).Select(g => g[i]).ToArray() };
        }
        private static byte[] KnownBonesPair(NeowReplayPlan plan) => plan.First != 255 && plan.Second != 255
            ? [plan.First, plan.Second]
            : plan.Authority.BonesEligibleRelicIds.Where(id => (plan.BonesAll & Beta110FastRelicCatalog.Bit(id)) != 0).ToArray();

        private static bool AuthoredOpeningHooksSupported(SearchModel request)
        {
            var a = request.Authority.EffectAuthority;
            var relics = (a?.OrderedRelicBag ?? []).Concat(a?.SharedRelicPoolSource ?? []).Concat(a?.CharacterRelicPoolSource ?? []);
            return PartyInitialQuery.CapsuleEffectPremise(request.CompiledSearch.NormalizedQuery).Values
                .SelectMany(keys => keys).All(k => k == BaseGameModelKeys.OrdinaryRelics.Whetstone ||
                    k == BaseGameModelKeys.OrdinaryRelics.WarPaint ||
                    request.Authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113 &&
                        NeowCapsuleNonCombatContinuation.HasZeroAdvance(request.Authority, k) ||
                    relics.Any(r => r.RelicKey == k &&
                        r.NestedClassificationExact && r.NestedEffectKind == NestedRelicEffectKind.NoTrackedImmediateEffect));
        }

        // A necessary-condition union of possible shared arrivals. It does not
        // select a player-local witness; whole-table Exact binds the real route.
        internal static (int Niche, int Potions)[]? SharedAdvanceOptions(SearchModel request, NeowReplayPlan plan)
        {
            var values = new HashSet<(int Niche, int Potions)>();
            bool Add(NeowReplayPlan order)
            {
                var d = SharedAdvance(request, order);
                if (d.Potions is not { } p) return false;
                if (d.Niche is { } n) values.Add((n, p));
                else if (plan.CapsuleUpgradeUpperBound >= 0)
                    for (int i = 0; i <= 2 * plan.CapsuleUpgradeUpperBound + 2 * plan.Authority.EffectCatalog.OtherCharacterPools.Length + 2; i++) values.Add((i, p));
                else return false;
                return true;
            }
            if (!plan.Bones || plan.Selected == 255) return Add(plan) ? values.ToArray() : null;
            foreach (byte a in plan.Authority.BonesEligibleRelicIds)
            foreach (byte b in plan.Authority.BonesEligibleRelicIds)
            {
                ulong mask = Beta110FastRelicCatalog.Bit(a) | Beta110FastRelicCatalog.Bit(b);
                if (a == b || (mask & plan.BonesAll) != plan.BonesAll || (mask & plan.BonesBan) != 0 ||
                    plan.BonesAny != 0 && (mask & plan.BonesAny) == 0 ||
                    plan.First != 255 && (a != plan.First || b != plan.Second)) continue;
                if (!Add(plan with { First = a, Second = b })) return null;
            }
            return values.ToArray();
        }

        internal static (int? Niche, int? Potions) SharedAdvance(SearchModel request, NeowReplayPlan plan)
        {
            if (plan.Authority.UsesChildShuffle) return (0, 0);
            if (plan.Selected == 255) return (0, 0);
            var pair = plan.Bones ? KnownBonesPair(plan) : [];
            if (plan.Bones && pair.Length != 2) return (null, null);
            var capsules = (plan.Bones ? pair : new[] { plan.Selected })
                .Where(id => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule).ToArray();
            var effects = request.Evaluation.StructuredNeowEffects;
            // A Bones-parented nested target binds unambiguously only when the
            // fixed pair contains one Capsule. Two Capsule allocations stay unknown.
            if (capsules.Length == 1)
            {
                var source = capsules[0] == Beta110FastRelicCatalog.SmallCapsule
                    ? BaseGameModelKeys.Relics.SmallCapsule : BaseGameModelKeys.Relics.LargeCapsule;
                effects = effects.Select(c => c.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
                    c.Scope == NeowStructuredEffectScope.NestedRelics ? c with { SourceRelicKey = source } : c).ToArray();
            }
            var upgrades = NeowAuthoredUpgradeContinuation.Compile(request with { Evaluation = request.Evaluation with
            { StructuredNeowEffects = effects } });
            var authoredCapsules = PartyInitialQuery.CapsuleEffectPremise(request.CompiledSearch.NormalizedQuery);
            if (!plan.Bones) return Direct(plan.Selected, 255);
            var forward = Order(pair[0], pair[1]);
            if (plan.First != 255 && plan.Second != 255) return forward;
            var reverse = Order(pair[1], pair[0]);
            return (forward.Niche == reverse.Niche ? forward.Niche : null,
                forward.Potions == reverse.Potions ? forward.Potions : null);

            (int? Niche, int? Potions) Order(byte first, byte second)
            {
                var x = Direct(first, 255); var y = Direct(second, first);
                return (x.Niche + y.Niche + 1, x.Potions + y.Potions);
            }
            (int? Niche, int? Potions) Direct(byte id, byte prior)
            {
                if (id == Beta110FastRelicCatalog.NewLeaf) return (1, 0);
                if (id == Beta110FastRelicCatalog.Kaleidoscope) return (2 * Math.Max(0, plan.Authority.EffectCatalog.OtherCharacterPools.Length - 1), 0);
                if (id == Beta110FastRelicCatalog.PhialHolster) return (0, 4);
                if (id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule)
                {
                    var source = id == Beta110FastRelicCatalog.SmallCapsule ? BaseGameModelKeys.Relics.SmallCapsule : BaseGameModelKeys.Relics.LargeCapsule;
                    var keys = authoredCapsules[source].ToArray();
                    if (keys.Length == 0) return (0, 0);
                    var authority = request.Authority.EffectAuthority;
                    foreach (var key in keys)
                        if (key != BaseGameModelKeys.OrdinaryRelics.Whetstone && key != BaseGameModelKeys.OrdinaryRelics.WarPaint &&
                            !(authority?.OrderedRelicBag ?? []).Concat(authority?.SharedRelicPoolSource ?? [])
                                .Concat(authority?.CharacterRelicPoolSource ?? []).Any(r => r.RelicKey == key && r.NestedClassificationExact &&
                                    r.NestedEffectKind == NestedRelicEffectKind.NoTrackedImmediateEffect)) return (null, null);
                    int advance = upgrades?.Advance(id, prior) ?? 0;
                    return (advance < 0 ? null : advance, 0);
                }
                return id <= Beta110FastRelicCatalog.StoneHumidifier ? (0, 0) : (null, null);
            }
        }

        internal static bool HasConditions(PlayerOfferQuery p)
        {
            var q = p.Conditions; var n = q.LegacyNeow;
            return !p.Offers.IsEmpty || p.SelectedOption is not null || p.Results.Count > 0 ||
                q.NeowOfferWhitelist is not null || q.OpeningRoute is not null || q.OpeningRouteRelicRequirement is not null ||
                q.StructuredOpeningEffects.Any(c => !c.IsEmpty) || !n.NeowRelics.IsEmpty || n.RequireNeowsBones ||
                !n.BonesRelics.IsEmpty || n.RequiredBonesCombination.Count > 0 || n.RequiredBonesAcquisitionOrder.Count > 0 ||
                n.RequireSmallCapsule || n.RequireLargeCapsule || !n.CapsuleContainedRelics.IsEmpty ||
                n.RequireWhetstone || n.RequireWarPaint || n.RequiredFinalCurse.HasValue || n.BannedFinalCurses.Count > 0 ||
                n.Preset != NeowSearchPreset.None || n.EffectOutputConditions.Any(c => !c.IsEmpty) ||
                q.LegacyWorld.AncientOptionFilters.Any(f => f.Act == 1 && !f.IsEmpty);
        }
        internal FamilyUnitCost? QuoteUnit(double inputs, bool compact, bool computationOnly = false, GpuCostSnapshot? costs = null)
        {
            double outputRate = Survival.SurvivalProbability ?? 1;
            if (computationOnly) return QuoteCoarseUnit(inputs, compact, outputRate, computationOnly: true, costs);
            if (_gpuStages.Count == 0)
            {
                double work = _playersCount +
                    _cpuPlans.Sum(plan => 1 + plan.StructuredConditions.Length * 3 +
                        (plan.HasFinalCurseFastProjection ? 1 : 0));
                var cpuMeasured = PartyMeasuredCost.Price(FamilyId, inputs,
                    _playersCount, false, false,
                    CpuCapacity, work, outputRate, _requestedWorkers,
                    cpuPhysicalRevision: CpuCondition(compact).PhysicalImplementationRevision);
                return cpuMeasured is { } cpuPrice
                    ? new(cpuPrice.Ms, cpuPrice.SetupMs,
                        cpuPrice.Evidence.Replace("LocalMeasuredCPU;", "HistoricalCpuWrapperReference;", StringComparison.Ordinal) +
                        $";CpuNPlans={_cpuPlans.Count};Work={work:G9};SingleFamilyWrapper;PriceQuality=Estimate;CurrentWrapperNotMeasured;SharedArrivalWitnessUnchanged")
                    : QuoteCoarseUnit(inputs, compact, outputRate);
            }
            FilterBodyDescription[] children = _gpuStages.Select(s => s.Family).ToArray();
            bool privateEdges = UsesPrivate || UsesFused;
            if (_rulesRevision.Length == 0 && !UsesCompatibilityGpu && privateEdges)
            {
                double work = _gpuStages.Sum(stage => 1 + stage.Replay.StructuredConditions.Length * 3 + (stage.Replay.HasFinalCurseFastProjection ? 1 : 0));
                var gpuMeasured = PartyMeasuredCost.Price(FamilyId, inputs, children.Length,
                    true, true, NeowFamilyGpuExecutor.Capacity, work, outputRate);
                if (gpuMeasured is { } gpuPrice)
                {
                    string evidence = gpuPrice.Evidence.Replace("LocalMeasuredGPU;", "HistoricalGpuReferenceEnvelope;", StringComparison.Ordinal) +
                        ";HistoricalCoefficientsAppliedAsCoarseEnvelope;PriceQuality=Estimate;CurrentWrapperBoundaryUnmeasured;NoSameRunFitting;SharedArrivalWitnessUnchanged" +
                        (HasOrderedKBody ? ";CurrentPartyOrderedKBody=20261016.v1" : "");
                    return new(gpuPrice.Ms, gpuPrice.SetupMs,
                        evidence + $";GpuNStages={children.Length};Work={work:G9};SingleFamilyWrapper;" +
                            (UsesFused ? "ExistingFusedHomogeneous" : "PrivateOrdinalChain"));
                }
            }
            // Same-family stages are a cost envelope, never an independent-player
            // probability product or a replacement for the shared arrival witness.
            return QuoteCoarseUnit(inputs, compact, outputRate);
        }
        public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => _gpuStages.Count == 0 ? CpuCondition(compact) :
            new(FamilyId, "N.Neow.PartyEffects.Gpu.v6." + (UsesFused ? "Fused." : UsesPrivate ? "PrivateOrdinal." : "Public.") + (compact ? "CompactAbi1" : "Dense") +
                (UsesPrivate && compact ? ".DirectCompactCast.20261016.v1" : "") + (HasOrderedKBody ? PartyOrderedKRevision : "") +
                (UsesCompatibilityGpu ? ".IntegerUnitFloat.Rne.20261025.v1" : "") + NeowIdentityOfferFacts.Revision + GpuRngRotation.Revision + _rulesRevision,
                "OrderedSlotInitialOffers", 1, "InitialOffersAndResolvedEffects;SharedArrival=AuthoredDrawUnion;Remainder=WholeTableExact;" +
                    (UsesCompatibilityGpu ? "GpuNumericalVariant=IntegerUnitFloat;PhysicalQuoteUnavailable=UnmeasuredVariant" : "BoundedLocalPhysicalQuote"),
                usesGpu: _gpuStages.Any(s => s.Family.ConditionPerformance.UsesGpu));
        private FamilyConditionPerformanceProjection CpuCondition(bool compact) =>
            CpuFilterExecution.Describe(FamilyId, CpuRevision + _rulesRevision, _workers, CpuCapacity, true, false, compact);
        public FamilySurvivalProjection Survival => _survival;
        internal bool MatchesInitialOffers(ulong root) => _program.MatchesInitialOffers(root);
        internal bool MatchesInitialOffersReference(ulong root) => _program.MatchesInitialOffersReference(root);
        internal bool Matches(ulong root) => _program.Matches(root);
        private PartyInitial(PartyInitial source)
        {
            _identity = source._identity; _profile = source._profile; _workers = source._workers;
            _requestedWorkers = source._requestedWorkers; _playersCount = source._playersCount;
            _rulesRevision = source._rulesRevision;
            _optimize = source._optimize; _skipExactValidation = source._skipExactValidation; _fuse = source._fuse;
            _survival = source._survival; _program = source._program;
            _remainders.AddRange(source._remainders);
            _cpuPlans.AddRange(source._cpuPlans); _cpuPlayers.AddRange(source._cpuPlayers);
        }
        internal FilterBodyDescription Describe()
        {
            var prepared = new PreparedFilter(FamilyId)
            {
                AnalyticalCost = new(FamilyId, _gpuStages.Count == 0 ? CpuCapacity : CpuFilterExecution.Capacity,
                    [], "PartyInitialAnalyticalWorkUnavailable;PhysicalQuoteBounded"),
                Survival = _survival, Remainders = _remainders.Distinct(StringComparer.Ordinal).ToArray(),
                Obligations = _cpuPlayers.Select(p => new FilterObligation(FamilyId, p.Authority.PlayerSlotIndex)).ToArray(),
                Issues = string.IsNullOrEmpty(GpuPreparationFailureDetail) ? [] :
                    [new("GpuPreparation", "PartyNeowGpuPreparationFailed", GpuPreparationFailureDetail)]
            };
            var stages = _gpuStages.ToArray();
            bool usesGpu = stages.Any(s => s.Family.UsesGpu);
            return new(prepared, ResolveConditionPerformance(false).PhysicalImplementationRevision, _profile.ProfileId)
            {
                UsesGpu = usesGpu, WindowCapacity = stages.Length == 0 ? CpuCapacity : FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize,
                PreferredRootWindowSize = usesGpu ? null : CpuCapacity,
                CpuWorkers = _workers, PlayersCount = _playersCount, InputIdentity = _identity,
                NumericRequirements = stages.Aggregate(GpuNumericRequirements.None, (value, s) => value | s.Family.NumericRequirements),
                PhysicalBodies = stages.Select(s => s.Family).ToArray(),
                CpuAlternatives = stages.Length == 0 ? [] : [new PartyInitial(this).Describe()],
                PerformanceProjection = ResolveConditionPerformance,
                Cost = new()
                {
                    ComposedPopulationReference = true,
                    CompleteCallQuote = (geometry, passed) => QuoteUnit(geometry.MeanInputPopulation, geometry.CompactInput),
                    Quote = QuotePhysicalWork
                },
                CreateExecution = RuntimeFactory(_program, stages, UsesFused, UsesPrivate, _skipExactValidation, _profile)
            };
        }
        private FamilyPhysicalQuote? QuotePhysicalWork(FilterBodyDescription body, FamilyPhysicalQuoteRequest geometry, IReadOnlySet<string> passed)
        {
            if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.PrivateInput || geometry.PrivateOutput || geometry.MeanInputPopulation <= 0) return null;
            var price = QuoteUnit(geometry.MeanInputPopulation, geometry.CompactInput, geometry.ComputationOnly, geometry.CostSnapshot);
            var quote = price is null ? null : new FamilyPhysicalQuote(FamilyId,
                geometry.ComputationOnly ? "PartyNComputationReference" : "PartyNCompleteReference",
                price.Milliseconds * 1e6 / geometry.MeanInputPopulation, body.WindowCapacity, price.SetupMilliseconds,
                price.Evidence + (geometry.ComputationOnly ? ";FamilyComputationBoundary" : ";CompleteCanonicalAbi1;PlannerRankingReference"),
                OutputElementBytes: 8, OutputAlreadyOrdered: true,
                PublicTransportClass: geometry.ComputationOnly ? body.UsesGpu ? "DeviceFamilyWork" : "CpuOrderedAbi1" : "CompleteCanonicalAbi1")
            { LocalCostSource = geometry.ComputationOnly ? "ComponentComposition" : "ReferenceDefault" };
            return geometry.ComputationOnly ? quote : FamilyGpuReadback.Reference(body, quote);
        }
        private static Func<FilterBodyDescription, FilterRuntimeServices, IFilterExecution> RuntimeFactory(
            NeowPartyProgram program, NeowPartyStage[] stages, bool fused, bool privateEdges,
            bool skipExact, IRuntimeProfile profile) => (body, services) => stages.Length == 0
                ? new CpuFilterExecution(body, profile, program.Matches, numericalRoots: true)
                : new NeowPartyFilterExecution(body, stages, fused, privateEdges, skipExact, services);
    }
}
