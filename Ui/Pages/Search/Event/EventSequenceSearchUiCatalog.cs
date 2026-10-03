using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal sealed record EventSearchUiCandidate(
    ModelKey EventKey,
    bool HasKnownCondition,
    IReadOnlyList<int> KnownAllowedActs,
    bool IsShared,
    IReadOnlyList<ModelKey> VariantActKeys,
    string EvidenceCode);

internal sealed record EventSequenceSearchUiCatalog(
    RuntimeProfileId ProfileId,
    IReadOnlyDictionary<int, IReadOnlyList<EventSearchUiCandidate>> CandidatesByAct,
    bool CatalogAvailable,
    string EvidenceCode)
{
    public RolltheSpire2.Core.Prediction.MorphicGroveScenario? MorphicGroveScenario { get; init; }
    public static EventSequenceSearchUiCatalog Empty(RuntimeProfileId profileId, string evidenceCode) => new(
        profileId,
        new Dictionary<int, IReadOnlyList<EventSearchUiCandidate>>
        {
            [1] = Array.Empty<EventSearchUiCandidate>(),
            [2] = Array.Empty<EventSearchUiCandidate>(),
            [3] = Array.Empty<EventSearchUiCandidate>()
        },
        false,
        evidenceCode);

    public static EventSequenceSearchUiCatalog FromAuthority(
        RuntimeProfileId profileId,
        WorldAuthoritySnapshot? world)
    {
        if (world is null)
        {
            return Empty(profileId, "event-sequence-ui-world-authority-missing");
        }

        var candidates = new Dictionary<int, IReadOnlyList<EventSearchUiCandidate>>();
        bool exact = true;
        HashSet<ModelKey> shared = ResolveSharedEvents(world);

        for (int act = 1; act <= 3; act++)
        {
            IReadOnlyList<ModelKey> keys;
            IReadOnlyList<(ModelKey ActKey, IReadOnlyList<ModelKey> RawEvents)> variantSources;
            bool actExact;

            if (RuntimeProfilePolicies.IsModernCore(profileId))
            {
                Beta109ActGenerationSnapshot[] acts =
                    (world.Beta109Generation?.OrderedActCatalog ?? Array.Empty<Beta109ActGenerationSnapshot>())
                    .Where(item => item.Act == act)
                    .ToArray();
                keys = DistinctInOrder(acts.SelectMany(item => item.OrderedEligibleEvents));
                variantSources = acts
                    .Select(item => (item.ActKey, (IReadOnlyList<ModelKey>)item.OrderedRawEvents))
                    .ToArray();
                actExact = acts.Length > 0 && acts.All(item => item.EligibleEventOrderExact);
            }
            else if (profileId == RuntimeProfileId.Stable107)
            {
                WorldActSnapshot[] acts =
                    (world.ActGroups ?? Array.Empty<WorldActGroupSnapshot>())
                    .Where(group => group.Act == act)
                    .SelectMany(group => group.Acts)
                    .ToArray();
                keys = DistinctInOrder(acts.SelectMany(item => item.OrderedEligibleEvents));
                variantSources = acts
                    .Select(item => (item.ActKey, (IReadOnlyList<ModelKey>)item.OrderedRawEvents))
                    .ToArray();
                actExact = acts.Length > 0 && acts.All(item => item.EventSequenceAuthorityExact);
            }
            else
            {
                return Empty(profileId, "event-sequence-ui-profile-unsupported");
            }

            exact &= actExact;
            candidates[act] = keys
                .Where(key => key.IsValid)
                .Select(key => (Key: key, Rule: EventStaticEligibilityCatalog.Evaluate(key, act, profileId)))
                .Where(item => !item.Rule.ShouldReject)
                .Select(item => BuildCandidate(
                    item.Key,
                    item.Rule,
                    shared.Contains(item.Key),
                    VariantMembership(item.Key, variantSources)))
                .ToArray();
        }

        bool available = candidates.Values.Any(list => list.Count > 0);
        return new EventSequenceSearchUiCatalog(
            profileId,
            candidates,
            available,
            available
                ? "event-sequence-ui-runtime-catalog:" + (exact ? "exact" : "partial")
                : "event-sequence-ui-runtime-catalog-empty");
    }

    public IReadOnlyList<EventSearchUiCandidate> CandidatesForAct(int act) =>
        CandidatesByAct.TryGetValue(act, out IReadOnlyList<EventSearchUiCandidate>? candidates)
            ? candidates
            : Array.Empty<EventSearchUiCandidate>();

    public EventSearchUiCandidate? Find(int act, ModelKey key) =>
        CandidatesForAct(act).FirstOrDefault(candidate => candidate.EventKey == key);

    private static EventSearchUiCandidate BuildCandidate(
        ModelKey key,
        EventStaticEligibilityResult result,
        bool isShared,
        IReadOnlyList<ModelKey> variantActKeys)
    {
        bool known = !string.Equals(result.RuleId, "NoAuditedStaticExclusion", StringComparison.Ordinal) &&
                     result.AllowedActs.Count > 0;
        return new EventSearchUiCandidate(
            key,
            known,
            known ? result.AllowedActs : Array.Empty<int>(),
            isShared,
            variantActKeys,
            result.EvidenceCode.Value);
    }

    private static HashSet<ModelKey> ResolveSharedEvents(WorldAuthoritySnapshot world)
    {
        IEnumerable<ModelKey> source = world.EventAuthority.OrderedSharedEventsRaw.Count > 0
            ? world.EventAuthority.OrderedSharedEventsRaw
            : world.SharedEvents ?? Array.Empty<ModelKey>();
        return source.Where(key => key.IsValid).ToHashSet(ModelKeyComparer.Instance);
    }

    private static IReadOnlyList<ModelKey> VariantMembership(
        ModelKey eventKey,
        IReadOnlyList<(ModelKey ActKey, IReadOnlyList<ModelKey> RawEvents)> variantSources)
    {
        var output = new List<ModelKey>();
        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach ((ModelKey actKey, IReadOnlyList<ModelKey> rawEvents) in variantSources)
        {
            if (!actKey.IsValid || !rawEvents.Contains(eventKey, ModelKeyComparer.Instance) || !seen.Add(actKey))
            {
                continue;
            }
            output.Add(actKey);
        }
        return output;
    }

    private static IReadOnlyList<ModelKey> DistinctInOrder(IEnumerable<ModelKey> source)
    {
        var output = new List<ModelKey>();
        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (ModelKey key in source)
        {
            if (key.IsValid && seen.Add(key)) output.Add(key);
        }
        return output;
    }
}
