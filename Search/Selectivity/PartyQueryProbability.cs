using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using System.Text.Json;

namespace RolltheSpire2.Search.Selectivity;

// Query probability only. No physical survival, cost, RNG state or search admission.
internal static partial class PartyQueryProbability
{
    internal static readonly string[] Assumptions =
    [
        "SharedFactsPaidOnce=true",
        "PlayerOrder=P1ToP4;NoPickupOrderMultiplicityBonus",
        "CrossPlayerPersonalConditions=AssumedIndependent;NotSourceProven",
        "SharedNicheDrawOffsetsUseStationaryMarginalApproximation;NotJointRngEnumeration",
        "SamePlayerDependenciesRetainExistingConditionalModels",
        "AncientIdentityAndActVariantConditionedOnceForAllPlayers"
    ];

    internal static JointSelectivityResult Estimate(CompiledSearch compiled)
    {
        if (compiled.Status == QueryNormalizationStatus.Impossible)
            return JointSelectivityResult.Exact(0, JointSelectivityCombinationMethod.ExactImpossible,
                "Probability.Party.NormalizationImpossible", "Canonical query is impossible.", "P=0");
        var known = new List<JointSelectivityComponent>();
        var unknown = new List<string>();
        var assumptions = new HashSet<string>(Assumptions);
        bool impossible = false;
        double product = 1;
        var confidence = SearchSelectivityConfidence.Medium;
        void Add(string id, string label, JointSelectivityResult result)
        {
            assumptions.UnionWith(result.Assumptions);
            if (result.ExactlyImpossible) impossible = true;
            if (result.Probability is { } p)
            {
                if (result.Confidence < confidence) confidence = result.Confidence;
                product *= p;
                known.Add(new(id, label, "Party", p, result.EvidenceCode, result.Method,
                    SearchSelectivityDependencyClass.AssumedIndependent, true, p > 0, result.Notes, result.Assumptions));
            }
            else unknown.AddRange(result.UnknownComponents.DefaultIfEmpty(result.EvidenceCode).Select(s => id + ":" + s));
            // Presentation of the conditional C factor; it is already included
            // in the player's block and must never be multiplied a second time.
            if (id.StartsWith("party:player:", StringComparison.Ordinal) &&
                result.KnownComponents.FirstOrDefault(c => c.Condition == nameof(SearchSelectivityDomain.CombatReward)) is { } combat)
                known.Add(combat with { Id = id + "/C", Parent = id, Condition = label + " · C" });
        }

        var query = compiled.NormalizedQuery; // Compiler has already bound shared facts once.
        Add("party:shared", "Shared facts and player A", Shared(compiled));
        if (query.StandardMaps.Count > 0)
            Add("party:map", "Shared map", StandardMapProbabilityEstimator.EstimateParty(SearchSelectivityInput.From(compiled)));
        var morphic = query.Players.SelectMany(p => p.Conditions.EventResultConditions)
            .Where(c => c.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains).ToArray();
        if (morphic.Length > 0)
        {
            var estimate = EventResultProbabilityEstimator.EstimateTransformJoint(morphic);
            Add("party:shared-morphic", "Shared Morphic draws with personal pools", estimate.Probability is { } p
                ? JointSelectivityResult.Exact(p, JointSelectivityCombinationMethod.SingleAuthorityTerm, estimate.EvidenceCode,
                    estimate.Notes, "One common event draw sequence; all personal predicates conjoined.", assumptions: estimate.Assumptions)
                : Unknown(estimate.EvidenceCode));
        }
        foreach (var player in query.Players)
        {
            var q = player.Conditions;
            if (player.SelectedOption is not null || player.Results.Count > 0)
            {
                // Historical concrete transaction queries are outside the
                // current editor contract; do not discard their choice meaning.
                unknown.Add($"P{player.Slot + 1}:LegacyTransactionProbabilityUnmodeled");
                continue;
            }
            q = q with
            {
                AncientBranches = [],
                EventResultConditions = q.EventResultConditions.Where(c => c.Kind is not (EventResultConditionKind.FakeMerchantOfferedFakeRelic or
                    EventResultConditionKind.MorphicGroveGroupInitialBasicsContains)).ToArray(),
                LegacyWorld = q.LegacyWorld with
                {
                    AncientOptionFilters = [],
                    AncientSeaGlassTargetFilters = []
                }
            };
            Add($"party:player:{player.Slot}", $"P{player.Slot + 1}", EstimatePersonalOffers(compiled, player, q));
        }
        if (impossible)
            return JointSelectivityResult.Exact(0, JointSelectivityCombinationMethod.ExactImpossible,
                "Probability.Party.ImpossibleFactor", "An authoritative factor is impossible.", "P=0", components: known);
        if (unknown.Count > 0)
            return JointSelectivityResult.Partial("Probability.Party.Partial", "Some multiplayer factors lack a model.",
                "Shared block once × each player's conditional personal block", known, unknown,
                assumptions: assumptions.ToArray());
        return JointSelectivityResult.Exact(product, JointSelectivityCombinationMethod.IndependentProduct,
            "Probability.Party.AssumedIndependent", "Shared facts counted once; personal blocks use an explicit independence approximation.",
            "P(shared W/E, all A) × product P(personal N/R/S/E/C)", components: known, assumptions: assumptions.ToArray(),
            dependencyCoverage: "SharedParentsConditioned;CrossPlayerIndependenceAssumed")
            with { Confidence = confidence, ExactlyImpossible = false };
    }

    private static JointSelectivityResult Shared(CompiledSearch compiled)
    {
        var q = compiled.NormalizedQuery;
        var neow = new ModelKey(BaseGameModelKeys.Categories.Event, "NEOW");
        bool NeowIdentity(ModelKeySetFilter keys)
        {
            bool IsNeow(ModelKey key) => key.Entry == neow.Entry &&
                key.Category is BaseGameModelKeys.Categories.Event or BaseGameModelKeys.Categories.Ancient;
            return (keys.Any.Count == 0 || keys.Any.Any(IsNeow)) && keys.All.All(IsNeow) && !keys.Ban.Any(IsNeow);
        }
        if (q.LegacyWorld.AncientIdentityFilters.Any(f => f.Act == 1 && !NeowIdentity(f.Keys)))
            return JointSelectivityResult.Exact(0, JointSelectivityCombinationMethod.ExactImpossible,
                "Probability.Party.Act1AncientConflict", "The standard Act 1 Ancient is Neow.", "P=0");
        var shared = q with { Players = [], StandardMaps = [], EventResultConditions = q.Players
            .SelectMany(p => p.Conditions.EventResultConditions).Where(c => c.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic)
            .DistinctBy(c => JsonSerializer.Serialize(c)).ToArray() };
        var generation = compiled.Context.Authority.WorldAuthority?.Beta109Generation;
        bool hasOptions = q.Players.Any(p => p.Conditions.AncientBranches.Count > 0 ||
            p.Conditions.LegacyWorld.AncientOptionFilters.Any(f => f.Act > 1) ||
            p.Conditions.LegacyWorld.AncientSeaGlassTargetFilters.Any(f => f.Act > 1));
        bool hasWorld = PartyInitialQuery.HasWorld(shared) || shared.EventSequenceConstraints.Count > 0 ||
            shared.LegacyWorld.AncientIdentityFilters.Any(f => f.Act > 1);
        if (!hasOptions && !hasWorld && shared.EventResultConditions.Count == 0) return Unit();
        if (generation is null || !generation.ActSelectionAuthorityExact) return Unknown("SharedWorldAuthorityMissing");
        var groups = generation.ActSelectionGroups.OrderBy(g => g.Act).ToArray();
        var priors = groups.Select(g => WorldProbabilityEstimator.ResolveVariantPriors(generation, g)).ToArray();
        if (groups.Length == 0 || groups.Any(g => !g.EligibilityAndOrderExact) ||
            priors.Any(p => p.Count == 0 || Math.Abs(p.Sum(x => x.Prior) - 1) > 1e-10)) return Unknown("SharedVariantPriorMissing");
        var selected = new ModelKey[groups.Length];
        var traces = new List<JointSelectivityBranchTrace>();
        var missing = new List<string>();
        double total = 0;
        void Visit(int index, double weight)
        {
            if (index < groups.Length)
            { foreach (var (key, p) in priors[index]) { selected[index] = key; Visit(index + 1, weight * p); } return; }
            var rows = shared.LegacyWorld.BossOrdinalFilters.ToList();
            foreach (var act in shared.VariantBossBranches.GroupBy(b => b.Act))
            {
                int at = Array.FindIndex(groups, g => g.Act == act.Key);
                var branches = at < 0 ? [] : act.Where(b => b.VariantKey == selected[at]).ToArray();
                if (branches.Length == 0) return;
                foreach (var b in branches)
                {
                    if (!b.FirstBoss.IsEmpty) rows.Add(new(b.Act, 1, b.FirstBoss));
                    if (b.IncludesSecondBoss && !b.SecondBoss.IsEmpty) rows.Add(new(b.Act, 2, b.SecondBoss));
                }
            }
            var conditioned = generation with { SelectedActs = selected.ToArray(), SelectedActsExact = true,
                ActSelectionGroups = groups.Select((g, i) => g with
                { EligibleActsInSourceOrder = [selected[i]], SelectionMode = Beta109ActSelectionMode.DeterministicFirst }).ToArray() };
            var authority = compiled.Context.Authority.WithWorldAuthority(compiled.Context.Authority.WorldAuthority! with { Beta109Generation = conditioned });
            var context = compiled.Context with { Party = null, Authority = authority };
            var worldQuery = shared with { VariantBossBranches = [], LegacyWorld = shared.LegacyWorld with
                { BossOrdinalFilters = rows, AncientIdentityFilters = [] } };
            var world = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(SearchCompiler.CompilePlayer(worldQuery, context)));
            var ancient = world.Probability == 0 ? Unit() : Ancients(compiled, context, conditioned);
            double? probability = world.Probability == 0 ? 0 : world.Probability is { } wp && ancient.Probability is { } ap ? wp * ap : null;
            string label = string.Join(",", selected.Select(k => k.Entry));
            if (probability is { } branchProbability) total += weight * branchProbability;
            else missing.Add(label + ":" + (world.Probability is null ? world.EvidenceCode : ancient.EvidenceCode));
            traces.Add(new(label, "One shared Act selection", weight, true, probability, probability == 0,
                ancient.EvidenceCode, ancient.KnownComponents, world.Assumptions.Concat(ancient.Assumptions).ToArray()));
        }
        Visit(0, 1);
        if (missing.Count > 0) return JointSelectivityResult.Partial("Probability.Party.SharedPartial",
            "A shared conditional branch lacks a model.", "Sum shared variant mass × W × joint Ancient options",
            [], missing, branches: traces, assumptions: Assumptions);
        return JointSelectivityResult.Exact(total, JointSelectivityCombinationMethod.FiniteMixture,
            "Probability.Party.SharedOnce", "One shared world with each player's parent-conditioned Ancient options.",
            "Sum P(variant) P(W|variant) Sum P(Ancient identities|variant) product P(player options|identities)",
            branches: traces, assumptions: Assumptions) with { Confidence = SearchSelectivityConfidence.Medium };
    }

    private static JointSelectivityResult Ancients(CompiledSearch compiled, SearchContext sharedContext, Beta109WorldGenerationSnapshot generation)
    {
        var q = compiled.NormalizedQuery;
        // Act 1 identity was discharged by Shared; its options belong to N.
        // Sea Glass target filters refine only an actually visible Sea Glass.
        var acts = q.LegacyWorld.AncientIdentityFilters.Where(f => f.Act > 1).Select(f => f.Act)
            .Concat(q.Players.SelectMany(p => p.Conditions.AncientBranches.Select(b => b.Act)))
            .Concat(q.Players.SelectMany(p => p.Conditions.LegacyWorld.AncientOptionFilters.Where(f => f.Act > 1).Select(f => f.Act)))
            .Concat(q.Players.SelectMany(p => p.Conditions.LegacyWorld.AncientSeaGlassTargetFilters.Where(f => f.Act > 1).Select(f => f.Act)))
            .Distinct().Order().ToArray();
        if (acts.Length == 0) return Unit();
        if (acts.Any(a => a is not (2 or 3))) return Unknown("AncientActUnmodeled");
        if (!generation.SharedAncientCatalogExact || !generation.AllSharedAncientCatalogExact ||
            !generation.UnlockFactsExact || !generation.DirectSourceAudited ||
            !generation.NoUnknownHooksOrModifiers && !sharedContext.Authority.UsesBestEffortModel ||
            acts.Any(act => !generation.OrderedActCatalog.Any(a => a.Act == act && generation.SelectedActs.Contains(a.ActKey) &&
                a.HasExactGenerationInputs))) return Unknown("AncientPoolAuthorityMissing");
        var pools = acts.Select(act => generation.OrderedActCatalog.Where(a => a.Act == act && generation.SelectedActs.Contains(a.ActKey))
            .SelectMany(a => a.OrderedAncients).Concat(generation.SharedAncients).Distinct()
            .Where(k => q.LegacyWorld.AncientIdentityFilters.Where(f => f.Act == act).All(f => PartyInitialQuery.Matches(f.Keys, [k]))).ToArray()).ToArray();
        if (pools.Any(p => p.Length == 0)) return JointSelectivityResult.Exact(0, JointSelectivityCombinationMethod.ExactImpossible,
            "Probability.Party.SharedAncientConflict", "No common Ancient identity satisfies the shared conjunction.", "P=0");
        var chosen = new ModelKey[acts.Length];
        double total = 0; bool missing = false;
        var cache = new Dictionary<(int Slot, int Act, ModelKey Key), double?>();
        double? Option(int slot, int act, ModelKey key)
        {
            if (cache.TryGetValue((slot, act, key), out var cached)) return cached;
            var player = q.Players[slot]; var local = player.Conditions;
            var allRows = local.AncientBranches.Where(b => b.Act == act).ToArray();
            var rows = allRows.Where(b => b.AncientKey == key).ToArray();
            if (allRows.Length > 0 && rows.Length == 0) return 0;
            var filters = local.LegacyWorld.AncientOptionFilters.Where(f => f.Act == act).ToArray();
            var input = SearchSelectivityInput.From(compiled.PlayerSearches[slot]);
            var result = AncientOptionProbabilityEstimator.EstimateConditionalConjunction(input, act, key, allRows,
                filters.Select(f => f.Keys).ToArray(),
                local.LegacyWorld.AncientSeaGlassTargetFilters.Where(f => f.Act == act).Select(f => f.Keys).ToArray());
            cache[(slot, act, key)] = result.Probability;
            return result.Probability;
        }
        void Visit(int index)
        {
            if (index < acts.Length) { foreach (var key in pools[index]) { chosen[index] = key; Visit(index + 1); } return; }
            var identityQuery = SearchQuery.Empty with { LegacyWorld = SearchQuery.Empty.LegacyWorld with
                { AncientIdentityFilters = acts.Select((act, i) => new ActModelKeySetFilter(act, new([], [chosen[i]], []))).ToArray() } };
            var identity = AncientIdentityProbabilityEstimator.Estimate(SearchSelectivityInput.From(SearchCompiler.CompilePlayer(identityQuery, sharedContext)));
            if (identity.Probability is not { } mass) { missing = true; return; }
            if (mass == 0) return;
            bool branchMissing = false;
            for (int i = 0; i < acts.Length; i++) foreach (var player in q.Players)
            {
                var p = Option(player.Slot, acts[i], chosen[i]);
                if (p == 0) return;
                if (p is null) branchMissing = true; else mass *= p.Value;
            }
            if (branchMissing) missing = true; else total += mass;
        }
        Visit(0);
        return missing ? Unknown("AncientConditionalOptionMissing") : JointSelectivityResult.Exact(total,
            JointSelectivityCombinationMethod.FiniteMixture, "Probability.Party.AncientSharedParent",
            "Shared identity mass paid once, personal option probabilities multiplied conditionally.", "Sum identity mass × product conditional options",
            assumptions: Assumptions);
    }

    private static JointSelectivityResult Unit() => JointSelectivityResult.Exact(1, JointSelectivityCombinationMethod.SingleAuthorityTerm,
        "Probability.Party.Empty", "No predicate.", "1");
    private static JointSelectivityResult Unknown(string issue) => JointSelectivityResult.Unpriced("Probability.Party." + issue,
        "Multiplayer probability authority or conditional model is missing.", "No missing factor replaced with one.", [issue]);
}
