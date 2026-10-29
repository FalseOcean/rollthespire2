using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Immutable numerical inputs for V. The original request and its full branch
// relationships remain with W/Exact; this body checks necessary conditions only.
internal sealed class VariantOpeningPlan
{
    private static readonly ulong SelectionHash = XxHash64.Hash("act_selection"u8, 0);
    internal sealed record Group(int Act, bool DeterministicFirst, bool[] Allowed, bool? OverrideAllowed);
    internal Group[] Groups { get; }
    internal VariantOpeningPreparation Preparation { get; }
    internal VariantOpeningPreparation CheckedPreparation => Preparation;
    internal int UnknownPrefixCount { get; }
    internal bool CoversVariants => Groups.Length > 0;
    internal string VariantUnavailableReason { get; }
    internal int SelectionDraws => Groups.Count(group => !group.DeterministicFirst);
    internal int VariantComparisons => Groups.Length;
    // Independent streams permit a local short circuit. Compare the declared
    // number of primitive draws; this does not impose a global Family order.
    internal bool UnknownFirst => UnknownPrefixCount > 0 && CoversVariants && UnknownPrefixCount < SelectionDraws;

    private VariantOpeningPlan(VariantOpeningPreparation preparation, Group[] groups, int unknown, string unavailable)
    {
        Preparation = preparation; Groups = groups; UnknownPrefixCount = unknown;
        VariantUnavailableReason = unavailable;
    }

    internal static VariantOpeningPlan? TryCompile(ExactSearchExecutionRequest request,
        VariantOpeningPreparation preparation, GpuNumericalVariant numericalVariant)
    {
        // Reuse the existing ordinary-room admission, including Party's shared
        // maximum prefix and the authored-opening intervention exclusions.
        int unknown = InitialRoomsPlan.TryCompileUnknown(request, numericalVariant)?.UnknownPrefixCount ?? 0;
        Group[] groups = [];
        string unavailable = "";
        if (preparation.HasVariantRestrictions)
        {
            var generation = request.Authority.WorldAuthority?.Beta109Generation;
            if (preparation.AuthorityFingerprint != request.CompiledSearch.Context.WorldSnapshotFingerprint)
                unavailable = "V.PreparedAuthorityMismatch";
            else if (generation is null || generation.Profile != request.ProfileId || !generation.CanReconstructSelectedActs)
                unavailable = "V.VariantAuthorityIncomplete";
            else
            {
                var ordered = generation.ActSelectionGroups.OrderBy(group => group.Act).ToArray();
                if (preparation.Acts.Any(row => !ordered.Any(group => group.Act == row.Act)))
                    unavailable = "V.VariantSelectionGroupMissing";
                else
                {
                    int lastAct = preparation.Acts.Max(row => row.Act);
                    groups = ordered.TakeWhile(group => group.Act <= lastAct).Select((group, index) => new Group(
                        group.Act, group.SelectionMode == Beta109ActSelectionMode.DeterministicFirst,
                        group.EligibleActsInSourceOrder.Select(key => preparation.Allows(group.Act, key)).ToArray(),
                        index == 0 && generation.Act1OverrideResolvedKey is { } key
                            ? preparation.Allows(group.Act, key) : null)).ToArray();
                }
            }
        }
        if (groups.Length == 0 && unknown == 0) return null;
        // Only actually checked variant facts can participate in conditional s.
        var actual = groups.Length > 0 ? preparation : preparation with { Acts = [] };
        return new(actual, groups, unknown, unavailable);
    }

    internal bool Matches(ulong root)
    {
        if (UnknownFirst && !InitialRoomsPlan.MatchesUnknown(root, UnknownPrefixCount)) return false;
        if (CoversVariants)
        {
            var selection = new Beta110FastRng(unchecked(root + SelectionHash));
            foreach (var group in Groups)
            {
                // Even an Act1 override retains the original selection draw.
                int selected = group.DeterministicFirst ? 0 : selection.NextInt(group.Allowed.Length);
                if (!(group.OverrideAllowed ?? group.Allowed[selected])) return false;
            }
        }
        return UnknownFirst || InitialRoomsPlan.MatchesUnknown(root, UnknownPrefixCount);
    }
}
