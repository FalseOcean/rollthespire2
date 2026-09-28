using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class WorldFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            var p = Replay.Plan;
            // Preserve the donor's bounded worker geometries. These registrations
            // reuse the same numerical replay; no new cost evidence is inferred.
            bool eventGeometry = p.MaxRequiredAct == 3 && p.RequiresSecondBoss &&
                p.Acts.Count(a => a.HasBossPredicate) == 2 &&
                p.Acts.Sum(a => a.EventPredicates.Length) is >= 2 and <= 12 &&
                p.Acts.All(a => a.EventPredicates.All(e => e.RangeValue is >= 1 and <= 3)) &&
                p.ActSelectionGroups.Length <= 4 && p.ActSelectionGroups.All(g => g.EligibleActIds.Length <= 2) &&
                p.MaxEventCandidateCount <= 43 && p.MaxEncounterPoolCount <= 12 &&
                Replay.BucketLengths.SequenceEqual(new uint[] { 30, 25, 35, 25, 1, 2, 32, 26, 38, 26 });
            bool bounded = eventGeometry && p.Acts.All(a => !a.HasAncientPredicate);
            bool identities = p.MaxRequiredAct == 3 && !p.RequiresSecondBoss &&
                p.Acts.All(a => a.FamilyVariantAllowed && !a.HasBossPredicate && !a.HasEventPredicate) &&
                p.Acts.Count(a => a.HasAncientIdentityPredicate) == 2 &&
                p.Acts.All(a => a.AncientBranchIdentityPredicates.Length <= 1 && a.AncientIdentityPredicates.Length == 0) &&
                p.ActSelectionGroups.Length <= 4 && p.ActSelectionGroups.All(g => g.EligibleActIds.Length <= 2) &&
                p.MaxEncounterPoolCount <= 12 &&
                Replay.BucketLengths.SequenceEqual(new uint[] { 30, 25, 35, 25, 1, 2, 32, 26, 38, 26 });
            bool eventPriceGeometry = p.Acts.Select(a => a.EventCandidates.Length).SequenceEqual(new[] { 31, 28, 0, 25 }) &&
                p.Acts.Select(a => a.EventPredicates.Length).SequenceEqual(new[] { 1, 1, 0, 1 }) &&
                p.Acts.All(a => a.EventPredicates.All(e => e.RangeMode == 0 && e.RangeValue == 3 &&
                    e.SourceFilter == byte.MaxValue && e.Keys.Any.Length == 1 && e.Keys.All.Length == 0 && e.Keys.Ban.Length == 0)) &&
                p.Acts.All(a => a.FamilyBossBranches.Length <= 1 && a.FamilyBossBranches.All(b =>
                    b.First.Any.Length == 1 && b.First.All.Length == 0 && b.First.Ban.Length == 0 &&
                    b.Second.Any.Length <= 1 && b.Second.All.Length == 0 && b.Second.Ban.Length == 0));
            bool rareMixed = eventGeometry && eventPriceGeometry && p.SharedAncients.Length == 1 &&
                p.Acts.Select(a => a.Ancients.Length).SequenceEqual(new[] { 1, 1, 3, 3 }) &&
                p.Acts.Where(a => a.HasAncientIdentityPredicate).Select(a => a.Act).SequenceEqual(new[] { 2, 3 }) &&
                p.Acts.All(a => a.AncientIdentityPredicates.Length == 0 && a.AncientBranchIdentityPredicates.Length <= 1 &&
                    a.AncientBranchIdentityPredicates.All(c => c.Any.Length == 1 && c.All.Length == 0 && c.Ban.Length == 0));
            foreach (int workers in (bounded ? new[] { 1, 2, 4, 8 } : identities || rareMixed ? new[] { 1, 8 } : new[] { 1 })
                .Where(w => w <= _request.WorkerCount))
                yield return new FamilyCpuExecution(_request, this, "W.World.Cpu.Progression.20260907.v1", Replay.Matches, workers,
                    g => workers == 1 ? WorldPhysicalPricing.QuoteCpu(_request, Replay, _gpuPlan, g) : null);
        }
    }
}
