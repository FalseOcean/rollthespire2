using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Events;

/// <summary>
/// Pure DTO projection shared by Stable107 and Beta109 world replays. The caller
/// shuffles Candidates with the same UpFront RNG instance used by production world
/// replay. Complete then derives a static effective ordinary-event sequence without
/// consuming RNG or reading live game state.
/// </summary>
public static class EventPoolSequenceProjector
{
    // Product-scoped Normal Neow assumption used by Search/Probability consumers
    // that explicitly model the opening Ancient. It is not a global Beta111 rule.
    public const int NormalNeowOpeningAncientEventCursorOffset = 1;

    public sealed record Candidate(
        ModelKey EventKey,
        EventPoolSourceKind Source,
        int SourceOrdinal,
        IReadOnlyList<string> EpochIds);

    public sealed record PreparedAct(
        int Act,
        ModelKey ActKey,
        List<Candidate> Candidates,
        int RawActLocalCount,
        int RawSharedCount,
        int FilteredOutCount,
        IReadOnlyList<EventEpochFilterEvidence> EpochFilters);

    public static bool TryPrepare(
        int act,
        ModelKey actKey,
        bool actSequenceAuthorityExact,
        IReadOnlyList<ModelKey> orderedActLocalRaw,
        IReadOnlyList<ModelKey> orderedSharedRaw,
        Beta109EventCatalogAuthoritySnapshot authority,
        IReadOnlyList<ModelKey> expectedEligibleOrder,
        out PreparedAct? prepared,
        out string issueCode)
    {
        prepared = null;
        issueCode = string.Empty;
        if (!actKey.IsValid || !actSequenceAuthorityExact)
        {
            issueCode = "MissingActEventSequenceAuthority";
            return false;
        }
        if (!authority.HasExactFilteringAuthority)
        {
            issueCode = "MissingEventEpochAuthority";
            return false;
        }
        if (orderedActLocalRaw is null || orderedSharedRaw is null || expectedEligibleOrder is null)
        {
            issueCode = "MissingEventCatalog";
            return false;
        }
        if (!SequenceEqual(orderedSharedRaw, authority.OrderedSharedEventsRaw))
        {
            issueCode = "SharedEventCatalogAlignmentMismatch";
            return false;
        }

        var candidates = new List<Candidate>(orderedActLocalRaw.Count + orderedSharedRaw.Count);
        candidates.AddRange(orderedActLocalRaw.Select((key, index) =>
            new Candidate(key, EventPoolSourceKind.ActLocal, index + 1, EpochIds(key, authority))));
        candidates.AddRange(orderedSharedRaw.Select((key, index) =>
            new Candidate(key, EventPoolSourceKind.Shared, index + 1, EpochIds(key, authority))));

        var filterEvidence = new List<EventEpochFilterEvidence>(authority.EpochsInFilterOrder.Count);
        int removedTotal = 0;
        foreach (Beta109EventEpochSnapshot epoch in authority.EpochsInFilterOrder)
        {
            int removed = 0;
            if (!epoch.IsRevealed)
            {
                var members = new HashSet<ModelKey>(epoch.OrderedMemberKeys, ModelKeyComparer.Instance);
                removed = candidates.RemoveAll(candidate => members.Contains(candidate.EventKey));
                removedTotal += removed;
            }
            filterEvidence.Add(new EventEpochFilterEvidence(
                epoch.EpochId,
                epoch.IsRevealed,
                epoch.OrderedMemberKeys.Count,
                removed,
                PredictionPrecision.Exact,
                "event-pool-sequence.epoch-filter." + epoch.EpochId.ToLowerInvariant()));
        }

        if (!SequenceEqual(candidates.Select(candidate => candidate.EventKey), expectedEligibleOrder))
        {
            issueCode = "EventEligibleOrderAlignmentMismatch";
            return false;
        }

        prepared = new PreparedAct(
            act,
            actKey,
            candidates,
            orderedActLocalRaw.Count,
            orderedSharedRaw.Count,
            removedTotal,
            filterEvidence);
        return true;
    }

    public static EventPoolActSequenceResult Complete(
        RuntimeProfileId profileId,
        PreparedAct prepared,
        EventEffectiveCandidateContext candidateContext,
        SourceAuthority authority,
        SnapshotCompleteness completeness,
        int rngCallCountBefore,
        int rngCallCountAfter,
        string evidenceCode)
    {
        PredictionPrecision projectionPrecision = EventStaticEligibilityCatalog.ProjectionPrecision(profileId);
        EventPoolRawSequenceEntryResult[] rawEntries = prepared.Candidates
            .Select((candidate, index) => new EventPoolRawSequenceEntryResult(
                index + 1,
                candidate.SourceOrdinal,
                candidate.EventKey,
                candidate.Source,
                candidate.EpochIds,
                PredictionPrecision.Exact,
                evidenceCode + ".raw-entry"))
            .ToArray();

        if (!candidateContext.OpeningAncientCursorAuthorityExact)
        {
            throw new InvalidOperationException("MissingOpeningAncientCursorAuthority");
        }

        int cursorOffset = Math.Clamp(candidateContext.OpeningAncientCursorOffset, 0, prepared.Candidates.Count);
        var effectiveEntries = new List<EventPoolSequenceEntryResult>();
        var exclusions = new List<EventStaticExclusionEvidence>();
        int openingSkipped = cursorOffset;
        int staticFiltered = 0;

        for (int rawIndex = 0; rawIndex < cursorOffset; rawIndex++)
        {
            Candidate skipped = prepared.Candidates[rawIndex];
            exclusions.Add(new EventStaticExclusionEvidence(
                rawIndex + 1,
                skipped.EventKey,
                skipped.Source,
                EventStaticExclusionKind.OpeningAncientCursorOffset,
                candidateContext.OpeningAncientCursorAuthorityCode,
                Array.Empty<int>(),
                projectionPrecision,
                "event-pool-sequence.opening-ancient-cursor-offset." +
                EventStaticEligibilityCatalog.ProjectionAuthorityCode(profileId)));
        }

        for (int rawIndex = cursorOffset; rawIndex < prepared.Candidates.Count; rawIndex++)
        {
            Candidate candidate = prepared.Candidates[rawIndex];
            EventStaticEligibilityResult rule = EventStaticEligibilityCatalog.Evaluate(
                candidate.EventKey,
                prepared.Act,
                profileId,
                candidate.Source,
                candidateContext.ImmutableEligibility);
            if (rule.ShouldReject)
            {
                staticFiltered++;
                exclusions.Add(new EventStaticExclusionEvidence(
                    rawIndex + 1,
                    candidate.EventKey,
                    candidate.Source,
                    ExclusionKind(rule.Decision),
                    rule.RuleId,
                    rule.AllowedActs,
                    rule.Precision,
                    rule.EvidenceCode));
                continue;
            }

            effectiveEntries.Add(new EventPoolSequenceEntryResult(
                effectiveEntries.Count + 1,
                rawIndex + 1,
                candidate.SourceOrdinal,
                candidate.EventKey,
                candidate.Source,
                candidate.EpochIds,
                projectionPrecision,
                evidenceCode + ".effective-candidate-entry." +
                EventStaticEligibilityCatalog.ProjectionAuthorityCode(profileId))
            {
                EligibilityKind = rule.EligibilityKind,
                EligibilityReasonCode = rule.RuleId
            });
        }

        return new EventPoolActSequenceResult(
            prepared.Act,
            prepared.ActKey,
            rawEntries,
            effectiveEntries,
            prepared.RawActLocalCount,
            prepared.RawSharedCount,
            rawEntries.Length,
            effectiveEntries.Count,
            prepared.FilteredOutCount,
            cursorOffset,
            openingSkipped,
            staticFiltered,
            0,
            prepared.EpochFilters,
            exclusions,
            projectionPrecision,
            authority,
            completeness,
            "up_front",
            rngCallCountBefore,
            rngCallCountAfter,
            evidenceCode);
    }

    public static EventPoolSequencePredictionResult Build(
        RuntimeProfileId profileId,
        IReadOnlyList<EventPoolActSequenceResult> acts,
        SourceAuthority authority,
        SnapshotCompleteness completeness,
        string authorityFingerprint,
        string evidenceCode)
    {
        if (acts.Count == 0)
            return EventPoolSequencePredictionResult.Unknown(profileId, "EventPoolSequenceEmpty", authorityFingerprint);
        string catalogFingerprint = Fingerprint(
            $"event-pool-sequence-catalog-v4:{profileId}:{EventStaticEligibilityCatalog.CatalogVersion}",
            acts.SelectMany(Describe));
        PredictionPrecision precision = acts.All(act => act.Precision == PredictionPrecision.Exact)
            ? PredictionPrecision.Exact
            : acts.Any(act => act.Precision == PredictionPrecision.Partial)
                ? PredictionPrecision.Partial
                : PredictionPrecision.Unknown;
        PredictionPrecision rawPrecision = acts.All(act =>
                act.RawEntries.All(entry => entry.Precision == PredictionPrecision.Exact))
            ? PredictionPrecision.Exact
            : acts.Any(act => act.RawEntries.Any(entry => entry.Precision == PredictionPrecision.Partial))
                ? PredictionPrecision.Partial
                : PredictionPrecision.Unknown;
        return new EventPoolSequencePredictionResult(
            profileId,
            SeedDomainEvaluationStatus.Evaluated,
            acts,
            precision,
            authority,
            completeness,
            string.Empty,
            authorityFingerprint,
            catalogFingerprint,
            evidenceCode,
            new[]
            {
                new PredictionDiagnostic("event-pool-sequence-profile", profileId.ToString()),
                new PredictionDiagnostic("event-pool-sequence-authority-fingerprint", authorityFingerprint),
                new PredictionDiagnostic("event-pool-sequence-catalog-fingerprint", catalogFingerprint),
                new PredictionDiagnostic("event-pool-sequence-act-count", acts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new PredictionDiagnostic("event-pool-sequence-scope", "InitialEffectiveCandidateQueuesAfterAuthoritativeOpeningCursorAndImmutableEligibility"),
                new PredictionDiagnostic("event-pool-sequence-static-rule-catalog", EventStaticEligibilityCatalog.CatalogVersion),
                new PredictionDiagnostic("event-pool-sequence-static-projection-authority", EventStaticEligibilityCatalog.ProjectionAuthorityCode(profileId)),
                new PredictionDiagnostic("event-pool-sequence-opening-cursor-contract", "PerActAuthoritativeContext;NormalNeowUsesOneSlot"),
                new PredictionDiagnostic("event-pool-sequence-runtime-dependent-count", acts.Sum(act => act.Entries.Count(entry => entry.EligibilityKind == EventCandidateEligibilityKind.RuntimeDependent)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new PredictionDiagnostic("event-pool-sequence-route-disclaimer", "RunStartCandidateOrderOnly;ActualFutureVisitedSequenceUnsupported")
            })
        {
            RawEventOrderPrecision = rawPrecision,
            StaticActCleaningPrecision = precision,
            StaticCandidateOrdinalPrecision = precision,
            RuntimeEligibility = EventRuntimeEligibilityProjectionStatus.ClassifiedAtRunStart,
            ActualVisitedEventSequence = ActualVisitedEventSequenceStatus.Unsupported
        };
    }

    private static EventStaticExclusionKind ExclusionKind(EventStaticEligibilityDecision decision) => decision switch
    {
        EventStaticEligibilityDecision.RejectAlwaysDisallowed => EventStaticExclusionKind.AlwaysDisallowed,
        EventStaticEligibilityDecision.RejectByPlayerMode => EventStaticExclusionKind.PlayerMode,
        EventStaticEligibilityDecision.RejectByUnlockState => EventStaticExclusionKind.UnlockState,
        _ => EventStaticExclusionKind.ActIndex
    };

    private static IReadOnlyList<string> EpochIds(
        ModelKey key,
        Beta109EventCatalogAuthoritySnapshot authority) =>
        authority.EpochsInFilterOrder
            .Where(epoch => epoch.OrderedMemberKeys.Contains(key, ModelKeyComparer.Instance))
            .Select(epoch => epoch.EpochId)
            .ToArray();

    private static bool SequenceEqual(IEnumerable<ModelKey> left, IEnumerable<ModelKey> right) =>
        left.SequenceEqual(right, ModelKeyComparer.Instance);

    private static IEnumerable<string> Describe(EventPoolActSequenceResult act)
    {
        yield return $"act:{act.Act}:{act.ActKey.Serialized}:{act.RawActLocalCount}:{act.RawSharedCount}:{act.FilteredOutCount}:{act.OpeningAncientCursorOffset}:{act.StaticFilteredOutCount}:{act.DuplicateFilteredOutCount}";
        foreach (EventPoolRawSequenceEntryResult entry in act.RawEntries)
            yield return $"raw:{entry.RawOrdinal}:{entry.Source}:{entry.SourceOrdinal}:{entry.EventKey.Serialized}:{string.Join("+", entry.EpochIds)}";
        foreach (EventPoolSequenceEntryResult entry in act.Entries)
            yield return $"effective:{entry.Ordinal}:{entry.RawOrdinal}:{entry.Source}:{entry.SourceOrdinal}:{entry.EventKey.Serialized}:{string.Join("+", entry.EpochIds)}";
        foreach (EventStaticExclusionEvidence exclusion in act.StaticExclusions)
            yield return $"excluded:{exclusion.RawOrdinal}:{exclusion.Kind}:{exclusion.EventKey.Serialized}:{exclusion.RuleId}:{string.Join("+", exclusion.AllowedActs)}";
    }

    private static string Fingerprint(string prefix, IEnumerable<string> values)
    {
        string payload = prefix + "\n" + string.Join("\n", values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
