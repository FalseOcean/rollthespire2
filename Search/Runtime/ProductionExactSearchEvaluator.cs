using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Prediction.Profiles;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

internal sealed record ProductionExactSearchResult(
    SeedPredictionRequest? Request,
    SeedPredictionDocument? Document,
    RuntimeContextAuthoritySnapshot Authority,
    SearchQueryEvaluation Evaluation,
    string FailureCode)
{
    public bool IsMatch => Evaluation.Disposition == SearchDisposition.Match && Request is not null && Document is not null;
}

/// <summary>
/// Production validation stage: call the version-bound Predictor for the required
/// domains and validate its result against Query. Filter predicates cannot emit
/// a final Match; SearchQueryEvaluation carries the validated route evidence.
/// </summary>
internal static class ProductionExactSearchEvaluator
{
    public static ProductionExactSearchResult Evaluate(
        ExactSearchExecutionRequest plan,
        TrustedRootHashInput input) => Evaluate(plan, input, CancellationToken.None);

    internal static ProductionExactSearchResult Evaluate(
        ExactSearchExecutionRequest plan,
        TrustedRootHashInput input, CancellationToken cancellationToken) =>
        Evaluate(plan, input, cancellationToken, null, plan.ExactOptions);

    internal static ProductionExactSearchResult Evaluate(
        ExactSearchExecutionRequest plan, TrustedRootHashInput input,
        CancellationToken cancellationToken, ShionExactTrialOptions options) =>
        Evaluate(plan, input, cancellationToken, null, options);

    private static ProductionExactSearchResult Evaluate(
        ExactSearchExecutionRequest plan,
        TrustedRootHashInput input, CancellationToken cancellationToken,
        Lazy<IReadOnlyList<NeowEffectRelicSnapshot>>? preparedRelicBag, ShionExactTrialOptions options = default)
    {
        if (plan.CompiledSearch.Context.Party is not null)
        {
            bool initialRooms = PartyInitialRooms.HasConditions(plan);
            // The execution-only projection excludes these separately owned
            // observations; the caller keeps the original request and sources.
            var domains = initialRooms ? PartyInitialRooms.IndependentDomains(plan) : plan;
            var partyResult = EvaluatePartyInitial(domains, input, cancellationToken);
            if (!initialRooms) return partyResult;
            var partyEvaluation = CombineIndependentEvaluations(partyResult.Evaluation,
                PartyInitialRooms.Evaluate(plan, input.RootHash));
            return partyResult with { Authority = plan.Authority,
                Evaluation = partyEvaluation, FailureCode = partyEvaluation.FailureCode };
        }
        if ((plan.CompiledSearch.NormalizedQuery.TransformationAggregate is null) != (plan.Evaluation.TransformationAggregate is null))
            throw new InvalidOperationException("TransformationAggregate.MissingExactObligation");
        var query = plan.CompiledSearch.NormalizedQuery;
        bool unresolvedRewards = Semantics.TransformationAggregateCondition.HasUnresolvedRewardContinuation(query);
        bool unresolvedRelics = Semantics.TransformationAggregateCondition.HasUnresolvedRelicContinuation(query);
        SearchQueryEvaluation continuationEvaluation = unresolvedRewards || unresolvedRelics
            ? SearchQueryEvaluation.Unknown(unresolvedRewards && unresolvedRelics
                ? "TransformationAggregate.NeowDependentRewardRelicContinuationNotSupportedInV1"
                : unresolvedRewards ? "TransformationAggregate.NeowDependentCombatRewardContinuationNotSupported"
                    : "TransformationAggregate.NeowDependentRelicContinuationNotSupported")
            : SearchQueryEvaluation.Match();
        if (unresolvedRewards || unresolvedRelics)
        {
            // These obligations require the same actual continuation as T. The
            // current model cannot supply it: retain an explicit residual, never
            // evaluate C/R against an invented independent initial state.
            plan = plan with { Evaluation = plan.Evaluation with
            {
                NormalCombatRewardConditions = unresolvedRewards ? [] : plan.Evaluation.NormalCombatRewardConditions,
                CombatCardRewardSequence = unresolvedRewards ? null : plan.Evaluation.CombatCardRewardSequence,
                CombatCardExclusion = unresolvedRewards ? null : plan.Evaluation.CombatCardExclusion,
                CombatPotionRewardSequence = unresolvedRewards ? null : plan.Evaluation.CombatPotionRewardSequence,
                RelicSequenceConditions = unresolvedRelics ? [] : plan.Evaluation.RelicSequenceConditions,
                RelicShopSequenceConditions = unresolvedRelics ? [] : plan.Evaluation.RelicShopSequenceConditions
            }};
        }
        RuntimeContextAuthoritySnapshot authority;
        if (plan.Evaluation.RequiresEffectColdPath || plan.Evaluation.RequiresWorldAuthority)
        {
            if (!SearchAuthorityProjector.TryProjectForRootHash(
                    plan,
                    input.RootHash,
                    input.SeedIdentity,
                    out authority,
                    out SearchDisposition projectionDisposition,
                    out string projectionIssue,
                    preparedRelicBag))
            {
                SearchQueryEvaluation projectionFailure = projectionDisposition switch
                {
                    SearchDisposition.Unsupported => SearchQueryEvaluation.Unsupported(projectionIssue),
                    SearchDisposition.NotEvaluatedByPolicy => SearchQueryEvaluation.Unknown(projectionIssue),
                    _ => SearchQueryEvaluation.Unknown(projectionIssue)
                };
                return new ProductionExactSearchResult(null, null, plan.Authority, projectionFailure, projectionIssue);
            }
        }
        else
        {
            authority = SearchAuthorityProjector.PreserveSeedIndependentEffectFoundation(plan.Authority);
        }

        // Canonical Event Result and Merchant Colorless are global/root-local facts.
        // Prove them directly before document/route evaluation so neither Event
        // occurrence nor OpeningRoute precision can reject an otherwise exact result.
        SearchQueryEvaluation canonicalEvaluation = Beta111CanonicalEventShopSearchEvaluator.Evaluate(
            plan,
            input.RootHash,
            authority);
        if (canonicalEvaluation.Disposition == SearchDisposition.NoMatch)
        {
            return new ProductionExactSearchResult(
                null,
                null,
                authority,
                canonicalEvaluation,
                canonicalEvaluation.FailureCode);
        }

        var mapEvaluation = StandardMapExactEvaluator.Evaluate(plan, input.SeedIdentity);
        if (mapEvaluation.Disposition == SearchDisposition.NoMatch)
            return new(null,null,authority,mapEvaluation,mapEvaluation.FailureCode);
        canonicalEvaluation = CombineIndependentEvaluations(canonicalEvaluation, mapEvaluation, continuationEvaluation);

        CharacterIdentity character = CharacterIdentity.FromKey(plan.CharacterKey);
        int relicSequencePreviewCount = plan.Evaluation.RelicSequenceConditions.Count == 0
            ? SeedPredictionInputLimits.DefaultRelicSequencePreviewCount
            : Math.Max(
                SeedPredictionInputLimits.DefaultRelicSequencePreviewCount,
                plan.Evaluation.RelicSequenceConditions.Max(condition => condition.RangeValue));

        if (!SeedPredictionRequest.TryCreateFromRootHash(
                input,
                character,
                plan.Ascension,
                authority.PlayersCount,
                authority.PlayerSlotIndex,
                authority,
                plan.Evaluation.AncientOptionConditions,
                plan.Evaluation.CheapPredictionDomains,
                relicSequencePreviewCount,
                plan.IncludeDiagnostics,
                out SeedPredictionRequest? cheapRequest,
                out SeedPredictionRequestError requestError))
        {
            string failure = "CheapPathRequestRejected:" + requestError;
            return new ProductionExactSearchResult(
                null,
                null,
                authority,
                SearchQueryEvaluation.Unknown(failure),
                failure);
        }

        // Canonical-only Event/Shop queries do not run the general Predictor at all.
        // Production truth is already proven by the root-local evaluator above; invoking
        // unrelated Neow/World analysis here would let non-observable state or invariant
        // failures reacquire rejection authority. A minimal document is retained only
        // because SearchCandidate currently carries the shared analysis payload contract.
        if ((plan.Evaluation.RequiresCanonicalRootLocalDomain || plan.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0) &&
            plan.Evaluation.CheapPredictionDomains == SeedPredictionDomainSelection.None &&
            !plan.Evaluation.RequiresNormalCombatRewardDomain)
        {
            canonicalEvaluation = CombineIndependentEvaluations(canonicalEvaluation,
                ProductionQueryValidator.EvaluateInitialRoomBaseline(plan, null, input.RootHash));
            SeedPredictionDocument canonicalDocument = CreateCanonicalOnlyDocument(
                cheapRequest!,
                plan,
                input);
            SearchQueryEvaluation canonicalOnly = AttachCanonicalGlobalWitness(
                canonicalEvaluation,
                canonicalDocument.CanonicalSeed);
            return new ProductionExactSearchResult(
                cheapRequest,
                canonicalDocument,
                authority,
                canonicalOnly,
                canonicalOnly.FailureCode);
        }

        cheapRequest = cheapRequest!.WithComplexBonesDeckInteractions(
            plan.CompiledSearch.RequiresComplexBonesDeckInteractionEvaluation);
        cheapRequest.PartyOpeningChoices = plan.PartyOpeningChoices;
        cheapRequest.RewardPoolPreparation = plan.RewardPoolPreparation;
        SelectRequiredNeowEffects(cheapRequest, plan);
        Beta111NeowIdentityReuse? identityReuse = options.OpeningReuse && plan.ProfileId == RuntimeProfileId.Beta111 &&
            plan.Authority.PlayersCount == 1 && plan.PartyOpeningChoices is null && plan.Evaluation.RequiresNormalCombatRewardDomain
                ? new() : null;
        if (identityReuse is not null) ShionExactTrialMetrics.Candidate();
        SeedPredictionDocument cheapDocument = RuntimeProfileRegistry.PredictFromRootHash(plan.Detection, cheapRequest, input,
            identityReuse: identityReuse);
        ThrowIfPredictionFailed(cheapDocument);
        SearchQueryEvaluation cheapEvaluation = ProductionQueryValidator.EvaluateCheap(plan, cheapDocument, authority,
            out SearchQueryEvaluation openingEvaluation, input.RootHash);
        cheapEvaluation = canonicalEvaluation.Disposition == SearchDisposition.Match
            ? MergeCanonicalGlobalEvidence(cheapEvaluation, canonicalEvaluation)
            : cheapEvaluation;
        SearchQueryEvaluation rewardBase = cheapEvaluation.Disposition == SearchDisposition.Match ? cheapEvaluation : openingEvaluation;
        if (cheapEvaluation.Disposition != SearchDisposition.Match && canonicalEvaluation.Disposition == SearchDisposition.Match &&
            rewardBase.Disposition == SearchDisposition.Match)
            rewardBase = MergeCanonicalGlobalEvidence(rewardBase, canonicalEvaluation);
        if (cheapEvaluation.Disposition == SearchDisposition.NoMatch || !plan.Evaluation.RequiresNormalCombatRewardDomain ||
            rewardBase.Disposition != SearchDisposition.Match ||
            !rewardBase.Witnesses.Any(w => !string.IsNullOrWhiteSpace(w.OpeningRouteId)))
        {
            cheapEvaluation = CombineIndependentEvaluations(cheapEvaluation, canonicalEvaluation, rewardBase);
            return new ProductionExactSearchResult(
                cheapRequest,
                cheapDocument,
                authority,
                cheapEvaluation,
                cheapEvaluation.FailureCode);
        }

        SeedPredictionDomainSelection rewardDomains =
            SeedPredictionDomainSelection.Neow |
            SeedPredictionDomainSelection.NormalCombatRewards;
        NormalCombatRewardProjectionRequest rewardProjectionRequest = BuildRewardProjectionRequest(plan) with
        { BattleCount = Math.Max(plan.Evaluation.CombatCardExclusion?.BattleCount ?? 0, Math.Max(3, Math.Max(plan.Evaluation.CombatCardRewardSequence?.Count ?? 0, plan.Evaluation.CombatPotionRewardSequence?.Count ?? 0))) };
        if (!SeedPredictionRequest.TryCreateFromRootHash(
                input,
                character,
                plan.Ascension,
                authority.PlayersCount,
                authority.PlayerSlotIndex,
                authority,
                plan.Evaluation.AncientOptionConditions,
                rewardDomains,
                relicSequencePreviewCount,
                plan.IncludeDiagnostics,
                rewardProjectionRequest,
                out SeedPredictionRequest? rewardRequest,
                out requestError))
        {
            string failure = "RewardPathRequestRejected:" + requestError;
            return new ProductionExactSearchResult(
                cheapRequest,
                cheapDocument,
                authority,
                SearchQueryEvaluation.Unknown(failure),
                failure);
        }

        rewardRequest = rewardRequest!.WithComplexBonesDeckInteractions(
            plan.CompiledSearch.RequiresComplexBonesDeckInteractionEvaluation);
        rewardRequest.PartyOpeningChoices = plan.PartyOpeningChoices;
        rewardRequest.RewardPoolPreparation = plan.RewardPoolPreparation;
        SelectRequiredNeowEffects(rewardRequest, plan);
        // Both requests use this candidate's same authority, effect roots,
        // Bones mode and party choices. Reuse the immutable opening projection;
        // the profile still validates identity authority and the full reward
        // sequence. The candidate-local proof avoids only duplicate pure identity work.
        SeedPredictionDocument rewardDocument = RuntimeProfileRegistry.PredictFromRootHash(plan.Detection, rewardRequest, input,
            cheapDocument.Sections.SelectMany(section => section.NeowChoices).ToArray(), identityReuse);
        if (identityReuse?.WasReused == true) ShionExactTrialMetrics.IdentityReused();
        ThrowIfPredictionFailed(rewardDocument);
        SearchQueryEvaluation evaluation = ProductionQueryValidator.EvaluateRewards(plan, rewardDocument, rewardBase);
        evaluation = CombineIndependentEvaluations(evaluation, cheapEvaluation, canonicalEvaluation);
        return new ProductionExactSearchResult(
            rewardRequest,
            rewardDocument,
            authority,
            evaluation,
            evaluation.FailureCode);
    }

    internal static SearchQueryEvaluation CombineIndependentEvaluations(params SearchQueryEvaluation[] evaluations)
    {
        // NoMatch proves a conjunction false even if another independent
        // observation is unresolved. Only all-Match may carry proof witnesses.
        SearchQueryEvaluation? mismatch = evaluations.FirstOrDefault(e => e.Disposition == SearchDisposition.NoMatch);
        if (mismatch is not null) return mismatch;
        SearchQueryEvaluation? unresolved = evaluations.FirstOrDefault(e => e.Disposition != SearchDisposition.Match);
        if (unresolved is not null) return unresolved;
        return evaluations[0] with { Evidence = evaluations.SelectMany(e => e.Evidence).Distinct().ToArray() };
    }

    private static void SelectRequiredNeowEffects(SeedPredictionRequest request, ExactSearchExecutionRequest plan)
    {
        request.NeowEffectRoots = !plan.Evaluation.RequiresEffectColdPath
            ? new HashSet<ModelKey>()
            : plan.Evaluation.NeowRoute is { IsValid: true } pinned
                ? new HashSet<ModelKey> { pinned.RouteRelicKey }
                : null;
    }

    // Error is not a semantic Unknown/NoMatch. Do not let a failed computation
    // mark its candidate complete and advance the session's committed prefix.
    internal static void ThrowIfPredictionFailed(SeedPredictionDocument document)
    {
        if (!document.Warnings.Any(w => w.Code == PredictionWarningCode.AnalysisFailed)) return;
        string evidence = string.Join(" | ", document.Diagnostics
            .Where(d => d.Code == PredictionDiagnosticCodes.Exception).Select(d => d.Value));
        throw new InvalidOperationException($"ProductionExact.AnalysisFailed:{document.PredictorId}:{document.CanonicalSeed}:{evidence}");
    }

    private static ProductionExactSearchResult EvaluatePartyInitial(ExactSearchExecutionRequest plan, TrustedRootHashInput input, CancellationToken cancellationToken)
    {
        if (plan.CompiledSearch.UsesPartyFamilyProjection)
        {
            var result = EvaluatePartyFamilies(plan, input, cancellationToken);
            if (result.Evaluation.Disposition is not (SearchDisposition.Match or SearchDisposition.NoMatch))
            {
                // An unresolved shared observation or earlier seat cannot hide a
                // contradiction in a later root/slot-local Event or pristine Shop
                // condition. These canonical facts consume no opening continuation
                // and prove no route/occurrence. Do not resume dependent N/C children
                // with an invented shared cursor, or use a local Match as a witness.
                foreach (var personal in plan.CompiledSearch.PlayerSearches.Take(plan.CompiledSearch.Context.Party!.Players.Count))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var child = Compilation.ExactSearchExecutionRequestFactory.ForPlayer(plan, personal);
                    if (!child.Evaluation.RequiresEventResultDomain && !child.Evaluation.RequiresMerchantColorlessDomain) continue;
                    var authority = SearchAuthorityProjector.PreserveSeedIndependentEffectFoundation(child.Authority);
                    var independent = Beta111CanonicalEventShopSearchEvaluator.Evaluate(child, input.RootHash, authority);
                    if (independent.Disposition != SearchDisposition.NoMatch) continue;
                    result = new(null, null, authority, independent, independent.FailureCode);
                    break;
                }
            }
            if (result.Evaluation.Disposition == SearchDisposition.Match || ReferenceEquals(result.Authority, plan.Authority)) return result;
            // A child owns its observation and unresolved cause. The whole-table
            // result still belongs to the parent request's delivery identity.
            // Retain the child document/request for diagnostics without relabeling
            // their observed payload or manufacturing a whole-table witness.
            var source = result.Authority;
            var scope = new SearchMatchEvidence("Party.ChildEvaluationScope", RelatedKey: source.Character.CharacterKey,
                EvidenceCode: new("Party.NonMatch.SourceContext"), ProfileId: source.ProfileId,
                Authority: source.SourceAuthority, AuthorityFingerprint: string.Join("|", source.CatalogFingerprint,
                    source.UnlockSnapshotFingerprint, source.EffectSnapshotFingerprint, source.WorldSnapshotFingerprint),
                Ordinal: source.PlayerSlotIndex + 1);
            return result with { Authority = plan.Authority,
                Evaluation = result.Evaluation with { Evidence = [..result.Evaluation.Evidence, scope] } };
        }
        var party = plan.CompiledSearch.Context.Party!;
        var query = plan.CompiledSearch.NormalizedQuery;
        var initial = ProjectPartyObservations(input, party, query, includeWorld: true);
        SearchQueryEvaluation initialStatus = EvaluatePartyObservations(query, initial);
        if (initialStatus.Disposition != SearchDisposition.Match)
            return PartyObservationResult(plan, input, initial, initialStatus);
        if (Semantics.PartyNeowQuery.HasTransactions(plan.CompiledSearch.NormalizedQuery) &&
            party.World.Beta109Generation is not { HasExactPartyRelicBags: true })
            return PartyObservationResult(plan, input, initial, SearchQueryEvaluation.Unknown("Party.N.OrderedRelicBagsIncomplete"));
        var observation = Semantics.PartyNeowQuery.Complete(initial, plan.CompiledSearch, cancellationToken);
        if (observation is null) return new(null, null, plan.Authority, SearchQueryEvaluation.NoMatch("Party.N.NoJointWitness"), "Party.N.NoJointWitness");
        if (observation.Root != input.RootHash) throw new InvalidOperationException("Party.WrongRoot");
        if (!Semantics.PartyInitialQuery.MatchesWorld(query, observation) || !Semantics.PartyInitialQuery.MatchesOffers(query, observation))
            return new(null, null, plan.Authority, SearchQueryEvaluation.NoMatch("Party.InitialPredicate"), "Party.InitialPredicate");
        var request = SeedPredictionRequest.ForPartyInitialInformation(input, party, plan.IncludeDiagnostics);
        var document = CreateCanonicalOnlyDocument(request, plan, input, observation);
        var evidence = new List<SearchMatchEvidence>();
        if (Semantics.PartyInitialQuery.HasWorld(query)) evidence.Add(new("W.World.Shared.Initial", AuthorityFingerprint: party.Fingerprint));
        foreach (var p in query.Players.Where(p => !p.Offers.IsEmpty))
            evidence.Add(new($"N.Neow.P{p.Slot + 1}.InitialOffers", AuthorityFingerprint: party.Fingerprint,
                OrderedOptionKeys: observation.Players[p.Slot].Offers));
        foreach (var transaction in observation.Transactions)
            evidence.Add(new($"N.Neow.P{transaction.Slot + 1}.SelectedResult.{transaction.Intent}", AuthorityFingerprint: party.Fingerprint,
                OrderedOptionKeys: [transaction.Option]));
        return new(request, document, plan.Authority, SearchQueryEvaluation.Match(evidence), "");
    }

    private static ProductionExactSearchResult EvaluatePartyFamilies(ExactSearchExecutionRequest plan,
        TrustedRootHashInput input, CancellationToken token)
    {
        var party = plan.CompiledSearch.Context.Party!;
        // The shared child owns requested World facts. This envelope only needs
        // the players whose initial N observations participate in the query.
        var initial = ProjectPartyObservations(input, party, plan.CompiledSearch.NormalizedQuery,
            includeWorld: false) with { IncludesFamilyInformation = true };
        SearchQueryEvaluation initialStatus = EvaluatePartyObservations(plan.CompiledSearch.NormalizedQuery, initial, includeWorld: false);
        if (initialStatus.Disposition != SearchDisposition.Match)
            return PartyObservationResult(plan, input, initial, initialStatus);
        if (!Semantics.PartyInitialQuery.MatchesOffers(plan.CompiledSearch.NormalizedQuery, initial))
            return new(null, null, plan.Authority, SearchQueryEvaluation.NoMatch("Party.InitialOffers"), "Party.InitialOffers");
        var shared = Compilation.ExactSearchExecutionRequestFactory.ForPlayer(plan, plan.CompiledSearch.PlayerSearches[^1]);
        var sharedResult = HasPartyChildObservations(shared) ? Evaluate(shared, input, token) : null;
        if (sharedResult is { IsMatch: false }) return sharedResult;
        var streams = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, input.RootHash, 0);
        var evidence = new List<SearchMatchEvidence>(sharedResult?.Evaluation.Evidence ?? []) { new(Semantics.PartyInitialQuery.OpeningPremiseId) };
        var transactions = new List<PartyNeowResult>();
        // Root and original owner are fixed for this synchronous candidate. The
        // bag is independent of shared opening cursors; each effect clones it.
        var preparedBags = new Lazy<IReadOnlyList<NeowEffectRelicSnapshot>>?[party.Players.Count];
        Lazy<IReadOnlyList<NeowEffectRelicSnapshot>> BagForSlot(int slot)
        {
            if (preparedBags[slot] is { } prepared) return prepared;
            var owner = party.Players[slot];
            ulong root = input.RootHash;
            return preparedBags[slot] = new(
                () => NeowRewardGenerator.BuildOrderedRelicBag(Beta111Profile.Instance, root, owner),
                LazyThreadSafetyMode.None);
        }
        if (!plan.CompiledSearch.NormalizedQuery.Players.Any(p => p.Conditions.OpeningRoute is not null || p.Conditions.HasCombatRewardConstraints))
        {
            foreach (var personal in plan.CompiledSearch.PlayerSearches.Take(party.Players.Count))
            {
                var result = EvaluateUnselected(personal);
                if (result is null) continue;
                if (!result.IsMatch) return result;
                evidence.AddRange(result.Evaluation.Evidence.Select(e => e with { Code = $"P{personal.Context.Authority.PlayerSlotIndex + 1}." + e.Code }));
            }
            var envelope = SeedPredictionRequest.ForPartyInitialInformation(input, party, plan.IncludeDiagnostics);
            return new(envelope, CreateCanonicalOnlyDocument(envelope, plan, input, initial), plan.Authority,
                SearchQueryEvaluation.Match(evidence), "");
        }
        ProductionExactSearchResult? unresolved = null;
        var matched = Visit(0, streams.Niche, streams.CombatPotionGeneration, true);
        return matched ?? unresolved ?? new(null, null, plan.Authority,
            SearchQueryEvaluation.NoMatch("Party.NoJointOpeningWitness"), "Party.NoJointOpeningWitness");

        ProductionExactSearchResult? EvaluateUnselected(Semantics.CompiledSearch compiled)
        {
            var child = Compilation.ExactSearchExecutionRequestFactory.ForPlayer(plan, compiled);
            // A None-domain general request defaults to N. Do not manufacture
            // that unrelated prediction for a seat with no evaluation obligation.
            if (!HasPartyChildObservations(child)) return null;
            if (!child.Evaluation.HasNeowConstraints) return Evaluate(child, input, token);
            var owner = party.Players[compiled.Context.Authority.PlayerSlotIndex];
            if (!SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, owner.Ascension,
                owner.PlayersCount, owner.PlayerSlotIndex, owner, compiled.Context.EvaluationAssumptions.AncientEligibilityAssumptions,
                SeedPredictionDomainSelection.Neow, 10, false, out var request, out var error))
                throw new InvalidOperationException("Party.UnselectedPlayerRequest:" + error);
            // Observation-only N starts at its initial shared cursor and uses the
            // same explicit Capsule obtain-effect premise as selected openings.
            SelectRequiredNeowEffects(request!, child);
            var preparedBag = BagForSlot(owner.PlayerSlotIndex);
            var choices = PartyNeowProjection.ProjectChoices(request!, input.RootHash,
                streams.Niche, streams.CombatPotionGeneration, true, Semantics.PartyInitialQuery.CapsuleEffectPremise(compiled.Query), preparedBag);
            return Evaluate(child with { PartyOpeningChoices = choices }, input, token, preparedBag);
        }

        ProductionExactSearchResult? Visit(int slot, Core.Rng.Xoshiro256StarStar niche, Core.Rng.Xoshiro256StarStar potions, bool sharedRngKnown)
        {
            token.ThrowIfCancellationRequested();
            if (slot == party.Players.Count)
            {
                var envelope = SeedPredictionRequest.ForPartyInitialInformation(input, party, plan.IncludeDiagnostics);
                return new(envelope, CreateCanonicalOnlyDocument(envelope, plan, input, initial with { Transactions = transactions.ToArray() }), plan.Authority,
                    SearchQueryEvaluation.Match(evidence.ToArray()), "");
            }
            var compiled = plan.CompiledSearch.PlayerSearches[slot];
            if (compiled.Query.OpeningRoute is null && !compiled.Query.HasCombatRewardConstraints)
            {
                // Explicit product premise: an unselected predecessor consumes no shared RNG.
                var independent = EvaluateUnselected(compiled);
                if (independent is { IsMatch: false }) return independent;
                int count = evidence.Count;
                if (independent is not null)
                    evidence.AddRange(independent.Evaluation.Evidence.Select(e => e with { Code = $"P{slot + 1}." + e.Code }));
                var found = Visit(slot + 1, niche, potions, sharedRngKnown);
                evidence.RemoveRange(count, evidence.Count - count);
                return found;
            }

            var owner = party.Players[slot];
            if (!SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, owner.Ascension,
                owner.PlayersCount, slot, owner, compiled.Context.EvaluationAssumptions.AncientEligibilityAssumptions,
                SeedPredictionDomainSelection.Neow, 10, false, out var request, out var error))
                throw new InvalidOperationException("Party.PlayerRequest:" + error);
            SelectRequiredNeowEffects(request!, Compilation.ExactSearchExecutionRequestFactory.ForPlayer(plan, compiled));
            var preparedBag = BagForSlot(slot);
            var choices = PartyNeowProjection.ProjectChoices(request!, input.RootHash, niche, potions, sharedRngKnown,
                Semantics.PartyInitialQuery.CapsuleEffectPremise(compiled.Query), preparedBag);
            // Unselected N keeps the established neutral C policy. Do not pin an
            // arbitrary perturbing offer merely because that route can satisfy C.
            // Selected openings still share one concrete whole-table witness.
            var selected = compiled.Query.OpeningRoute?.RouteRelicKey;
            foreach (var choice in choices.Where(c => selected is null || c.RelicKey == selected).SelectMany(PartyNeowProjection.ConcreteRoutes))
            {
                var local = selected is not null
                    ? compiled.Query with { OpeningRoute = new(choice.RelicKey) } : compiled.Query;
                var pinned = local == compiled.Query ? compiled : Semantics.SearchCompiler.CompilePlayer(local, compiled.Context);
                var child = Compilation.ExactSearchExecutionRequestFactory.ForPlayer(plan, pinned) with { PartyOpeningChoices = [choice] };
                var result = Evaluate(child, input, token, preparedBag);
                if (!result.IsMatch)
                {
                    if (result.Evaluation.Disposition != SearchDisposition.NoMatch) unresolved ??= result;
                    continue;
                }
                var continuation = choice.OpeningRewardContinuations?.Routes.SingleOrDefault();
                bool neutralPredecessor = selected is null;
                bool nextSharedKnown = sharedRngKnown && (neutralPredecessor || continuation?.NicheState is not null && continuation.CombatPotionGenerationState is not null);
                int before = evidence.Count;
                evidence.AddRange(result.Evaluation.Evidence.Select(e => e with { Code = $"P{slot + 1}." + e.Code }));
                transactions.Add(new(slot, choice.RelicKey, PartyNeowIntent.ResultTarget, [], choice.EffectGroups, choice.EffectGroups)
                { OpeningRouteId = continuation?.Route.RouteId ?? "" });
                // Unknown shared state taints only a later consumer; independent family facts remain usable.
                var found = Visit(slot + 1, !neutralPredecessor && nextSharedKnown ? continuation!.NicheState!.Restore() : niche,
                    !neutralPredecessor && nextSharedKnown ? continuation!.CombatPotionGenerationState!.Restore() : potions, nextSharedKnown);
                transactions.RemoveAt(transactions.Count - 1);
                evidence.RemoveRange(before, evidence.Count - before);
                if (found is not null) return found;
            }
            return null;
        }
    }

    private static bool HasPartyChildObservations(ExactSearchExecutionRequest child) =>
        child.Evaluation.HasNeowConstraints || child.Evaluation.RequiresWorldDomain ||
        child.Evaluation.RequiresRelicSequenceDomain || child.Evaluation.RequiresNormalCombatRewardDomain ||
        child.Evaluation.RequiresCanonicalRootLocalDomain || child.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0;

    private static bool RequiresPartyInitialOffers(Semantics.PlayerOfferQuery player)
    {
        var n = player.Conditions.LegacyNeow;
        return !player.Offers.IsEmpty || player.SelectedOption is not null ||
            player.Conditions.HasCombatRewardConstraints || player.Conditions.OpeningRoute is not null ||
            player.Conditions.StructuredOpeningEffects.Count > 0 || !n.NeowRelics.IsEmpty || n.RequireNeowsBones ||
            !n.BonesRelics.IsEmpty || n.RequiredBonesCombination.Count > 0 || n.RequiredBonesAcquisitionOrder.Count > 0 ||
            n.RequireSmallCapsule || n.RequireLargeCapsule || !n.CapsuleContainedRelics.IsEmpty || n.RequireWhetstone ||
            n.RequireWarPaint || n.RequiredFinalCurse.HasValue || n.BannedFinalCurses.Count > 0 ||
            n.EffectOutputConditions.Any(c => !c.IsEmpty) || n.Preset != NeowSearchPreset.None;
    }

    private static bool RequiresPartyBosses(Semantics.SearchQuery query) =>
        query.LegacyWorld.BossFilters.Count > 0 || query.LegacyWorld.BossOrdinalFilters.Count > 0 ||
        query.VariantBossBranches.Any(b => !b.FirstBoss.IsEmpty || b.IncludesSecondBoss);

    private static PartySeedInformation ProjectPartyObservations(TrustedRootHashInput input,
        OrderedPartyAuthority party, Semantics.SearchQuery query, bool includeWorld) =>
        PartySeedInformation.Project(input.CanonicalSeed, party,
            includeActs: includeWorld && query.VariantBossBranches.Count > 0,
            includeBosses: includeWorld && RequiresPartyBosses(query),
            query.Players.Where(RequiresPartyInitialOffers).Select(p => p.Slot).ToHashSet());

    private static SeedPredictionDocument CreateCanonicalOnlyDocument(
        SeedPredictionRequest request,
        ExactSearchExecutionRequest plan,
        TrustedRootHashInput input, PartySeedInformation? party = null) => new()
    {
        Party = party,
        Context = SeedPredictionDocument.CreateContext(request, plan.Detection.DisplayVersion, plan.ProfileId),
        PredictorId = party is null ? "Beta111CanonicalEventShopSearchEvaluator" : "Beta111PartyInitialInformation",
        OriginalSeed = request.OriginalSeed,
        CanonicalSeed = input.CanonicalSeed,
        Sections = Array.Empty<PredictionSection>(),
        Warnings = Array.Empty<PredictionWarning>(),
        Diagnostics = Array.Empty<PredictionDiagnostic>(),
        OverallStatus = SeedPredictionOverallStatus.Completed,
        ProductRelevantProjectionPrecision = PredictionPrecision.Exact,
        ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
        FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.NotEvaluated
    };

    internal static SearchQueryEvaluation EvaluatePartyObservations(Semantics.SearchQuery query, PartySeedInformation initial, bool includeWorld = true)
    {
        SearchQueryEvaluation? unresolved = null;
        foreach (var player in query.Players)
        {
            if (!RequiresPartyInitialOffers(player)) continue;
            var offers = initial.Players.Single(p => p.Slot == player.Slot);
            if (offers.IdentityPrecision != PredictionPrecision.Exact)
                unresolved ??= offers.IdentityPrecision == PredictionPrecision.Unsupported
                    ? SearchQueryEvaluation.Unsupported(offers.IssueCode) : SearchQueryEvaluation.Unknown(offers.IssueCode);
            else if (!Semantics.PartyInitialQuery.Matches(player.Offers, offers.Offers))
                return SearchQueryEvaluation.NoMatch("Party.InitialOffers:P" + (player.Slot + 1));
        }
        if (includeWorld && Semantics.PartyInitialQuery.HasWorld(query))
        {
            bool needsActs = query.VariantBossBranches.Count > 0;
            bool needsBosses = RequiresPartyBosses(query);
            if (needsActs && initial.ActIdentityPrecision != PredictionPrecision.Exact)
                unresolved ??= SearchQueryEvaluation.Unknown("Party.SelectedActsNotExact");
            else if (needsActs && query.VariantBossBranches.Any(b => initial.Acts.Count < b.Act))
                unresolved ??= SearchQueryEvaluation.Unknown("Party.SelectedActMissing");
            else if (needsActs && query.VariantBossBranches.GroupBy(b => b.Act).Any(g =>
                initial.Acts.Count >= g.Key && !g.Any(b => b.VariantKey == initial.Acts[g.Key - 1])))
                return SearchQueryEvaluation.NoMatch("Party.VariantParentRejected");
            if (needsBosses && initial.BossStatus != Core.World.SeedDomainEvaluationStatus.Evaluated)
                unresolved ??= initial.BossStatus == Core.World.SeedDomainEvaluationStatus.Unsupported
                    ? SearchQueryEvaluation.Unsupported("Party.WorldProjectionIncomplete:" + initial.BossIssueCode)
                    : SearchQueryEvaluation.Unknown("Party.WorldProjectionIncomplete:" + initial.BossIssueCode);
            else if ((!needsActs || initial.ActIdentityPrecision == PredictionPrecision.Exact) &&
                !Semantics.PartyInitialQuery.MatchesWorld(query, initial))
                return SearchQueryEvaluation.NoMatch("Party.InitialWorldPredicate");
        }
        return unresolved ?? SearchQueryEvaluation.Match();
    }

    private static ProductionExactSearchResult PartyObservationResult(ExactSearchExecutionRequest plan,
        TrustedRootHashInput input, PartySeedInformation information, SearchQueryEvaluation evaluation)
    {
        var request = SeedPredictionRequest.ForPartyInitialInformation(input, plan.CompiledSearch.Context.Party!, plan.IncludeDiagnostics);
        return new(request, CreateCanonicalOnlyDocument(request, plan, input, information), plan.Authority, evaluation, evaluation.FailureCode);
    }

    private static NormalCombatRewardProjectionRequest BuildRewardProjectionRequest(ExactSearchExecutionRequest plan) =>
        plan.CombatRewardRoutePolicy.ExactPolicy switch
        {
            CombatRewardExactRoutePolicy.PinnedRealRoute when plan.Evaluation.NeowRoute is { IsValid: true } pinned =>
                new NormalCombatRewardProjectionRequest(
                    NormalCombatRewardProjectionScope.SearchExact,
                    NormalCombatRewardRouteSelectionMode.PinnedRealRoute,
                    pinned.RouteRelicKey,
                    plan.Evaluation.RequiredBonesAcquisitionOrder),
            CombatRewardExactRoutePolicy.UnpinnedVerifyNeutralRealRoute =>
                new NormalCombatRewardProjectionRequest(
                    NormalCombatRewardProjectionScope.SearchExact,
                    NormalCombatRewardRouteSelectionMode.ProvablyNeutralRealRoutes),
            // SearchExact + AllRealRoutes is deliberately rejected by the
            // predictor in P10-RP1, so a missing/unknown policy fails closed.
            _ => new NormalCombatRewardProjectionRequest(
                NormalCombatRewardProjectionScope.SearchExact,
                NormalCombatRewardRouteSelectionMode.AllRealRoutes)
        };

    private static SearchQueryEvaluation AttachCanonicalGlobalWitness(
        SearchQueryEvaluation canonicalEvaluation,
        string canonicalSeed)
    {
        if (canonicalEvaluation.Disposition != SearchDisposition.Match)
            return canonicalEvaluation;

        string[] conditionIds = canonicalEvaluation.Evidence
            .Select(item => item.ConditionId ?? item.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        EvidenceCode[] evidenceCodes = canonicalEvaluation.Evidence
            .Select(item => item.EvidenceCode)
            .Where(code => !string.IsNullOrWhiteSpace(code.Value))
            .Distinct()
            .ToArray();
        var witness = new SearchMatchWitness(
            canonicalSeed,
            string.Empty,
            Array.Empty<ModelKey>(),
            conditionIds,
            string.Empty,
            null,
            evidenceCodes);
        return SearchQueryEvaluation.Match(
            canonicalEvaluation.Evidence,
            Array.Empty<string>(),
            new[] { witness });
    }

    internal static SearchQueryEvaluation MergeCanonicalGlobalEvidence(
        SearchQueryEvaluation documentEvaluation,
        SearchQueryEvaluation canonicalEvaluation)
    {
        if (documentEvaluation.Disposition != SearchDisposition.Match ||
            canonicalEvaluation.Disposition != SearchDisposition.Match ||
            canonicalEvaluation.Evidence.Count == 0)
        {
            return documentEvaluation;
        }

        // An aggregate's Bones results are route facts, not global facts. Retain
        // every matching route, then intersect with the N witness carrying R's
        // proven bag facts before C validates its copied Rewards continuation.
        // Never combine two existential routes or report discarded T alternatives.
        var aggregateRoutes=canonicalEvaluation.Evidence.Where(e=>e.Code=="TransformationAggregateMatched" && e.AcquisitionOrder is {Count:2}).ToArray();
        if(aggregateRoutes.Length>0)
        {
            var compatible=documentEvaluation.Witnesses.Where(w=>aggregateRoutes.Any(e=>
                w.AcquisitionOrder.SequenceEqual(e.AcquisitionOrder!) &&
                e.RouteId is { } route && w.OpeningRouteId.EndsWith("."+route["transformation-aggregate:".Length..],StringComparison.Ordinal))).ToArray();
            if(compatible.Length==0) return SearchQueryEvaluation.NoMatch("TransformationAggregate.NoSharedOpeningRoute");
            documentEvaluation=SearchQueryEvaluation.Match(documentEvaluation.Evidence,
                compatible.Select(w=>w.OpeningRouteId).Distinct(StringComparer.Ordinal).ToArray(),compatible);
            canonicalEvaluation=SearchQueryEvaluation.Match(canonicalEvaluation.Evidence.Where(e=>
                e.Code!="TransformationAggregateMatched" || compatible.Any(w=>
                    w.AcquisitionOrder.SequenceEqual(e.AcquisitionOrder ?? Array.Empty<ModelKey>()) &&
                    e.RouteId is { } route && w.OpeningRouteId.EndsWith("."+route["transformation-aggregate:".Length..],StringComparison.Ordinal))).ToArray());
        }

        SearchMatchEvidence[] evidence = documentEvaluation.Evidence
            .Concat(canonicalEvaluation.Evidence)
            .Distinct()
            .ToArray();
        string[] conditionIds = canonicalEvaluation.Evidence
            .Select(item => item.ConditionId ?? item.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        EvidenceCode[] evidenceCodes = canonicalEvaluation.Evidence
            .Select(item => item.EvidenceCode)
            .Where(code => !string.IsNullOrWhiteSpace(code.Value))
            .Distinct()
            .ToArray();
        SearchMatchWitness[] witnesses = documentEvaluation.Witnesses
            .Select(witness => witness with
            {
                MatchedConditions = witness.MatchedConditions
                    .Concat(conditionIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                EvidenceCodes = witness.EvidenceCodes
                    .Concat(evidenceCodes)
                    .Distinct()
                    .ToArray()
            })
            .ToArray();

        return SearchQueryEvaluation.Match(evidence, documentEvaluation.MatchedRouteIds, witnesses);
    }
}
