using System.Numerics;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// N-only necessary observations. Each route chooses ONE pair of advances and
// evaluates all of its predicates together. These alternatives are not claimed
// bag/deck outcomes; R and Production Exact retain their own truth ownership.
internal sealed record NeowCapsuleNicheContinuation(int[][] DrawSets, string[] Remainders)
{
    internal const string Revision = ".SoloCapsuleNicheAlternatives.20261101.v1";
    private const int Priors = 33;
    internal static readonly int[] Zero = [0];
    internal int[] Advances(byte source, byte prior) => source is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule
        ? DrawSets[(source == Beta110FastRelicCatalog.SmallCapsule ? 0 : Priors) + (prior == 255 ? 32 : prior)] : Zero;
    internal static bool ReadsNiche(byte source) => source is Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.NewLeaf;
    internal static bool ObservedAfterFirst(NeowReplayPlan n, byte second) => n.HasFinalCurseFastProjection ||
        ReadsNiche(second) && n.StructuredConditions.Any(c => c.SourceRelicId == second);

    internal static bool TryInitialCounts(SearchModel request, out int attacks, out int skills)
    {
        var a = request.Authority.EffectAuthority;
        attacks = skills = 0;
        if (!request.Authority.CanUseCurrentModel || request.Authority.NoRunModifiers != true ||
            !request.Authority.IsAuditedBeta111SourceContext || a?.HasExactDeck != true) return false;
        attacks = a.OrderedDeck!.Count(c => c.CardType == EffectCardType.Attack && c.CanUpgrade);
        skills = a.OrderedDeck!.Count(c => c.CardType == EffectCardType.Skill && c.CanUpgrade);
        return true;
    }

    internal static NeowCapsuleNicheContinuation? Compile(SearchModel request, Beta110FastNeowAuthority authority,
        bool bones, byte selected, byte first, byte second, ulong required, ulong banned,
        IReadOnlyList<Beta110FastStructuredCondition> conditions, bool finalCurse)
    {
        if (authority.PlayersCount != 1 || !finalCurse && !conditions.Any(c => ReadsNiche(c.SourceRelicId))) return null;
        byte[] sources = !bones ? [selected] : first != 255 && second != 255 ? [first, second] :
            authority.BonesEligibleRelicIds.Where(id => (banned & Beta110FastRelicCatalog.Bit(id)) == 0 &&
                (BitOperations.PopCount(required) != 2 || (required & Beta110FastRelicCatalog.Bit(id)) != 0)).ToArray();
        if (!sources.Any(id => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule) ||
            BonesHiddenUpgradeContinuation.CaptureMode() == 0) return null; // Explicit frozen comparison only.
        bool ready = TryInitialCounts(request, out int attacks, out int skills);
        bool missingRequiredDeck = false;
        var sets = new int[2 * Priors][];
        for (int sourceIndex = 0; sourceIndex < 2; sourceIndex++)
        {
            byte source = sourceIndex == 0 ? Beta110FastRelicCatalog.SmallCapsule : Beta110FastRelicCatalog.LargeCapsule;
            int capacity = sourceIndex == 0 ? 1 : 2;
            var rows = request.Evaluation.StructuredNeowEffects.Where(c => !c.IsEmpty &&
                Beta110FastRelicCatalog.TryGetId(c.SourceRelicKey, out byte id) && id == source && NeowReplayPlan.IsCapsule(c)).ToArray();
            uint authored = 0;
            int occupied = 0;
            if (rows.Length == 1 && (rows[0].Kind is NeowStructuredConditionKind.ExactUnorderedPair or NeowStructuredConditionKind.ExactSingle) &&
                rows[0].OutputKeys.Count <= capacity && rows[0].OutputKeys.Distinct().Count() == rows[0].OutputKeys.Count && !rows[0].AllowDuplicateOutputs)
            {
                var keys = rows[0].OutputKeys;
                authored = (keys.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone) ? 1u : 0u) |
                    (keys.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint) ? 2u : 0u);
                occupied = keys.Count;
            }
            bool knownZero = authored==0 && occupied==capacity;
            missingRequiredDeck |= !ready && !knownZero && sources.Contains(source);
            for (int priorIndex = 0; priorIndex < Priors; priorIndex++)
            {
                byte prior = priorIndex == 32 ? (byte)255 : (byte)priorIndex;
                int at = sourceIndex * Priors + priorIndex;
                if(knownZero) { sets[at]=[0]; continue; }
                if (!ready || !TryBounds(prior, attacks, skills, out var a, out var s)) { sets[at] = [-1]; continue; }
                var draws = new SortedSet<int>();
                for (uint mask = 0; mask < 4; mask++)
                {
                    if ((mask & authored) != authored || BitOperations.PopCount(mask & ~authored) > capacity - occupied) continue;
                    int low = ((mask & 1) == 0 ? 0 : Draws(a.Min)) + ((mask & 2) == 0 ? 0 : Draws(s.Min));
                    int high = ((mask & 1) == 0 ? 0 : Draws(a.Max)) + ((mask & 2) == 0 ? 0 : Draws(s.Max));
                    for (int value = low; value <= high; value++) draws.Add(value);
                }
                sets[at] = draws.ToArray();
            }
        }
        return new(sets, missingRequiredDeck ? ["CapsuleNicheUpgradeDeckAuthorityUnavailable"] : []);
    }

    // Bounds are over eligible counts, not assumed card identities or upgrade
    // outcomes. Default-odds card grants may already be upgraded, including the
    // game's zero-roll boundary. Removals/transforms/upgrades need not pick the
    // same type, hence each type retains its complete possible interval.
    private static bool TryBounds(byte prior, int attacks, int skills,
        out (int Min, int Max) a, out (int Min, int Max) s)
    {
        a = (attacks, attacks); s = (skills, skills);
        if (prior == 255) return true;
        int remove = 0, add = 0;
        switch (prior)
        {
            case Beta110FastRelicCatalog.DowsingRod:
            case Beta110FastRelicCatalog.ArcaneScroll:
            case Beta110FastRelicCatalog.HeftyTablet:
            case Beta110FastRelicCatalog.LeadPaperweight:
            case Beta110FastRelicCatalog.LostCoffer:
            case Beta110FastRelicCatalog.MassiveScroll:
            case Beta110FastRelicCatalog.NeowsTorment: add = 1; break;
            case Beta110FastRelicCatalog.Kaleidoscope: add = 2; break;
            case Beta110FastRelicCatalog.ScrollBoxes: add = 3; break;
            case Beta110FastRelicCatalog.LeafyPoultice: remove = add = 2; break;
            case Beta110FastRelicCatalog.NewLeaf: remove = add = 1; break;
            case Beta110FastRelicCatalog.PreciseScissors:
            case Beta110FastRelicCatalog.Pomander: remove = 1; break;
            case Beta110FastRelicCatalog.PrecariousShears:
            case Beta110FastRelicCatalog.NeowsTalisman:
            case Beta110FastRelicCatalog.SmallCapsule: remove = 2; break;
            case Beta110FastRelicCatalog.LargeCapsule:
                // Its W/WP upgrades precede its new Strike/Defend. Eggs can
                // upgrade either addition, so neither is presumed eligible.
                remove = 2; add = 1; break;
            default: if (prior > Beta110FastRelicCatalog.StoneHumidifier) return false; break;
        }
        a = (Math.Max(0, attacks - remove), attacks + add);
        s = (Math.Max(0, skills - remove), skills + add);
        return true;
    }
    private static int Draws(int eligible) => Math.Max(eligible - 1, 0);

    internal uint[] Pack()
    {
        var data = new List<uint>(DrawSets.Length * 2);
        for (int i = 0; i < DrawSets.Length * 2; i++) data.Add(0);
        for (int i = 0; i < DrawSets.Length; i++)
        {
            data[i * 2] = (uint)data.Count; data[i * 2 + 1] = (uint)DrawSets[i].Length;
            data.AddRange(DrawSets[i].Select(value => unchecked((uint)value)));
        }
        return data.ToArray();
    }
}
