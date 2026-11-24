using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class ModernCapsuleRelicFamily
{
    private const string Neow = "N.Neow";
    private const string IdentityCompositeRevision = ".NeowIdentityComposite.20261114.v1";

    // The modern R body already replays identity. An optional same-dispatch
    // N+R body can own it only when the separately registered N promises
    // nothing beyond this R body's verified prerequisites.
    internal static bool TryCreateIdentityComposite(SearchModel model, NeowReplayPlan n, FilterBodyDescription neow,
        FilterBodyDescription relic, out FilterBodyDescription? composite)
    {
        composite = null;
        if (model.Authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            model.Authority.PlayersCount != 1 || !n.AuditedModern113IdentityReplay ||
            (n.TopAny | n.TopAll | n.TopBan | n.BonesAny | n.BonesBan) != 0 ||
            n.StructuredConditions.Length != 0 || n.HasFinalCurseFastProjection || n.ExactOnly.Length != 0 ||
            neow.FamilyId != Neow || !neow.Coverage.SequenceEqual([Neow]) || neow.Preparation.Remainders.Count != 0 ||
            !relic.UsesGpu || relic.FamilyId != RelicFamily.Id || !relic.Coverage.Contains(Obligation) ||
            !relic.Coverage.All(domain => domain is RelicFamily.Id or Obligation) ||
            !relic.PhysicalRevision.StartsWith("R.Relic.Gpu.ModernCapsuleHeads.", StringComparison.Ordinal) ||
            relic.Preparation.Remainders.Count != 0) return false;

        var r = NeowReplayPlan.Compile(model, true);
        if (r.Selected != n.Selected || r.Bones != n.Bones || r.First != n.First || r.Second != n.Second ||
            (r.TopAny | r.TopAll | r.TopBan | r.BonesAny | r.BonesBan) != 0) return false;

        // Source-bound rows already require their source. Compare R's actual
        // pair necessity, including the separately required Capsule sources.
        ulong required = r.BonesAll;
        if (r.Bones)
        {
            foreach (var row in r.Filter.StructuredNeowEffects)
                if (!NeowReplayPlan.IsGroupedCapsule(row) &&
                    Beta110FastRelicCatalog.TryGetId(row.SourceRelicKey, out byte source))
                    required |= Beta110FastRelicCatalog.Bit(source);
            if (r.First != Beta110FastRelicCatalog.InvalidId && r.Second != Beta110FastRelicCatalog.InvalidId)
                required |= Beta110FastRelicCatalog.Bit(r.First) | Beta110FastRelicCatalog.Bit(r.Second);
        }
        if ((n.BonesAll & ~required) != 0 || n.RequireBones && !r.Bones) return false;
        // A source-bound Small→Rare predicate passing the actual R draw implies
        // the optional N rarity gate: that gate already proves lower lanes nonempty.
        var preparation = relic.Preparation with
        {
            FamilyId = Neow,
            Coverage = [Neow, ..relic.Coverage],
            Obligations = [..neow.Preparation.EffectiveObligations, ..relic.Preparation.EffectiveObligations],
            AnalyticalCost = new(Neow, relic.WindowCapacity, [], "ModernCapsuleIdentityCompositeWorkUnpriced"),
            Survival = FamilySurvivalProjection.Unresolved(Neow, "ModernCapsuleIdentityCompositeProbabilityUnpriced")
        };
        composite = relic with
        {
            Preparation = preparation,
            PhysicalRevision = relic.PhysicalRevision + IdentityCompositeRevision,
            PerformanceProjection = compact =>
            {
                var condition = relic.ResolveConditionPerformance(compact);
                return new(Neow, condition.PhysicalImplementationRevision + IdentityCompositeRevision,
                    condition.AlgorithmRevision, condition.Factor, condition.Evidence, condition.UsesGpu);
            },
            BindPrivate = null,
            BindPrivateShared = null,
            CpuAlternatives = [],
            PhysicalBodies = [relic]
        };
        return true;
    }
}
