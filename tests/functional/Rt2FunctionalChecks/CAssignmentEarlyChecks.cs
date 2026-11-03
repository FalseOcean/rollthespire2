using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static class CAssignmentEarlyChecks
{
    internal static int Run(int count = 256)
    {
        int checks = 0, earlyRejections = 0, positives = 0, faults = 0;
        void Check(bool condition, string label)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("C CPU assignment early: " + label);
        }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var fixtures = CCompleteMatchingChecks.Cases(context).ToList();
        var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
        var neutral = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null);
        var drops = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.DropAny, null);
        fixtures.Add(new("TwoNoDrop", CCompleteMatchingChecks.Request(SearchQuery.Empty with
        {
            CombatPotionRewards = new(2, CombatRewardSequenceOrderMode.Unordered, [noDrop, noDrop])
        }, context)));
        fixtures.Add(new("TwoDrop", CCompleteMatchingChecks.Request(SearchQuery.Empty with
        {
            CombatPotionRewards = new(3, CombatRewardSequenceOrderMode.Unordered, [drops, drops, neutral])
        }, context)));
        var twoRoutes = fixtures.First(f => f.Name == "Repeated").Request.Model.CompiledSearch.NormalizedQuery with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                [BaseGameModelKeys.Relics.ScrollBoxes, BaseGameModelKeys.Relics.ArcaneScroll], BonesRouteOrderMode.AnyOrder)
        };
        fixtures.Add(new("TwoRoutes", CCompleteMatchingChecks.Request(twoRoutes, context)));

        foreach (var fixture in fixtures)
        {
            var original = new CombatRewardReplay(fixture.Request.Model, cpuAssignmentEarly: false);
            var candidate = new CombatRewardReplay(fixture.Request.Model, catalog: original.Catalog, plan: original.Plan,
                cpuAssignmentEarly: true);
            Check(!original.ConservativelyKeeps && !candidate.ConservativelyKeeps, "fixture executes: " + fixture.Name);
            var prepared = Beta110CombatRewardFastStage.PrepareAssignmentEarly(original.Plan, original.Catalog);
            Check(candidate.UsesCpuAssignmentEarly == (prepared is not null), "captured structural eligibility");
            Check(original.RouteCount == candidate.RouteCount, "authored routes unchanged");
            if (fixture.Name == "TwoRoutes") Check(candidate.RouteCount == 2, "both Bones orders retained");
            var selection = new CombatRewardCardSelection(original.Catalog);
            for (ulong root = 0; root < (ulong)Math.Max(1, count); root++)
            {
                for (int route = 0; route < original.RouteCount; route++)
                {
                    var opening = original.Opening(root, route, out var niche);
                    Check(opening.EquivalentTo(candidate.Opening(root, route, out var candidateNiche)) && niche == candidateNiche,
                        "unchanged opening checkpoint/state");
                    var before = Beta110CombatRewardFastStage.EvaluateForFamily(opening, original.Plan, original.Catalog,
                        fixture.Request.Model.Ascension, selection);
                    var after = Beta110CombatRewardFastStage.EvaluateForFamily(opening, original.Plan, original.Catalog,
                        fixture.Request.Model.Ascension, selection, prepared);
                    Check(before.Status == after.Status, "full/streaming disposition: " + fixture.Name);
                    Check(after.Battle1Evaluated <= before.Battle1Evaluated && after.Battle2Evaluated <= before.Battle2Evaluated &&
                        after.Battle3Evaluated <= before.Battle3Evaluated, "only a suffix can be omitted");
                    if (after.Status == Beta110CombatRewardRouteProjectionStatus.ExactProjection)
                    {
                        positives++;
                        Check(before == after, "success completes the same complete matcher");
                    }
                    else if (after.Battle1Evaluated + after.Battle2Evaluated + after.Battle3Evaluated <
                             before.Battle1Evaluated + before.Battle2Evaluated + before.Battle3Evaluated) earlyRejections++;
                }
                Check(original.Matches(root) == candidate.Matches(root), "complete route union unchanged");
                Check(original.Matches(root) == candidate.MatchesReference(root), "GPU oracle retains original complete generation");
            }
        }
        Check(positives > 0, "population includes positive matching");
        Check(earlyRejections > 0, "population exercises an actual skipped suffix");

        var baseRequest = fixtures.First(f => f.Name == "Repeated").Request;
        var basis = new CombatRewardReplay(baseRequest.Model, cpuAssignmentEarly: false);
        var plan = basis.Plan;
        var catalog = basis.Catalog;
        var proof = Beta110CombatRewardFastStage.PrepareAssignmentEarly(plan, catalog);
        Check(proof is not null, "basis has a valid suffix proof");
        // Known potion/gold and deterministic presentation modifiers leave the
        // proven three-card generator intact; compare their actual replay too.
        foreach (var flags in new[] { Beta110CombatRewardInfluenceFlags.ForcePotionReward,
            Beta110CombatRewardInfluenceFlags.AmethystAubergineFixedGold,
            Beta110CombatRewardInfluenceFlags.DeterministicUpgradeOnly | Beta110CombatRewardInfluenceFlags.DeterministicEnchantmentOnly })
        {
            var modifiedPlan = plan with { ExplicitContext = plan.ExplicitContext with { InfluenceFlags = flags, FixedGoldAmount = 20 } };
            var modifiedProof = Beta110CombatRewardFastStage.PrepareAssignmentEarly(modifiedPlan, catalog);
            Check(modifiedProof is not null, "known three-card modifier is proven");
            var original = new CombatRewardReplay(baseRequest.Model, catalog: catalog, plan: modifiedPlan, cpuAssignmentEarly: false);
            var candidate = new CombatRewardReplay(baseRequest.Model, catalog: catalog, plan: modifiedPlan, cpuAssignmentEarly: true);
            for (ulong root = 0; root < 64; root++) Check(original.Matches(root) == candidate.Matches(root), "known modifier disposition");
        }
        void Disabled(string label, Beta110CombatRewardFastPlan changedPlan, Beta110FastEffectCatalog changedCatalog)
        {
            Check(Beta110CombatRewardFastStage.PrepareAssignmentEarly(changedPlan, changedCatalog) is null, "guard: " + label);
        }
        Disabled("card authority", plan with { CardPoolAuthorityExact = false }, catalog);
        Disabled("potion authority", plan with { PotionPoolAuthorityExact = false }, catalog);
        Disabled("oversized target count", plan with { CardAssignmentTargets = Enumerable.Repeat(plan.CardAssignmentTargets[0], 7).ToArray() }, catalog);
        Disabled("oversized window", plan with { CardAssignmentWindow = 7 }, catalog);
        Disabled("mismatched potion arrays", plan with { PotionAssignmentRequirements = [0] }, catalog);
        Disabled("Candy", plan with { ExplicitContext = plan.ExplicitContext with {
            InfluenceFlags = Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard } }, catalog);
        Disabled("extra reward", plan with { ExplicitContext = plan.ExplicitContext with { AdditionalCardRewardCount = 1 } }, catalog);
        var cards = catalog.CombatRewardCardPool;
        ushort id = cards.Common[0];
        Disabled("cross-rarity identity", plan, catalog with { CombatRewardCardPool = cards with { Rare = [.. cards.Rare, id] } });
        Disabled("short lane", plan, catalog with { CombatRewardCardPool = cards with { Common = cards.Common[..2] } });
        Disabled("invalid potion", plan, catalog with { CombatRewardPotionPool = catalog.CombatRewardPotionPool with {
            Common = [Beta110FastDenseId.Invalid] } });
        Disabled("missing potion lane", plan, catalog with { CombatRewardPotionPool = catalog.CombatRewardPotionPool with { Rare = [] } });

        foreach (var faultCatalog in new[]
        {
            catalog with { CombatRewardCardPool = new([id, id, id], [], []) },
            catalog with { CombatRewardCardPool = cards with { Rare = [Beta110FastDenseId.Invalid] } },
            catalog with { CombatRewardPotionPool = catalog.CombatRewardPotionPool with { Common = [], Uncommon = [], Rare = [] } }
        })
        {
            Disabled("numeric fault pool", plan, faultCatalog);
            var original = new CombatRewardReplay(baseRequest.Model, catalog: faultCatalog, plan: plan, cpuAssignmentEarly: false);
            var candidate = new CombatRewardReplay(baseRequest.Model, catalog: faultCatalog, plan: plan, cpuAssignmentEarly: true);
            Check(!candidate.UsesCpuAssignmentEarly, "faulting body remains original");
            for (ulong root = 0; root < (ulong)Math.Max(64, count); root++)
            {
                var before = Capture(original, root);
                var after = Capture(candidate, root);
                Check(before == after, "original numeric failure and disposition retained");
                if (before.Error is not null) faults++;
            }
        }
        Check(faults > 0, "fault preservation has actual failures");
        // Runtime state can invalidate an otherwise prepared request proof.
        // In that case the original complete body must execute, including faults.
        foreach (int variant in new[] { 0, 1, 2 })
        for (ulong root = 0; root < 16; root++)
        {
            var opening = basis.Opening(root, 0);
            if (variant == 0) opening.RewardsContinuationAuthorityExact = false;
            if (variant == 1) opening.InfluenceFlags |= Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard;
            if (variant == 2) opening.AdditionalCardRewardCount = 4;
            var before = Stage(opening, null);
            var after = Stage(opening, proof);
            Check(before == after, "runtime proof bypass preserves full result/failure");
        }
        string? previous = Environment.GetEnvironmentVariable("RT2_C_CPU_ASSIGNMENT");
        try
        {
            Environment.SetEnvironmentVariable("RT2_C_CPU_ASSIGNMENT", "off");
            var original = new CombatRewardReplay(baseRequest.Model);
            Environment.SetEnvironmentVariable("RT2_C_CPU_ASSIGNMENT", "on");
            var candidate = new CombatRewardReplay(baseRequest.Model);
            Environment.SetEnvironmentVariable("RT2_C_CPU_ASSIGNMENT", "off");
            Check(!original.UsesCpuAssignmentEarly && candidate.UsesCpuAssignmentEarly, "request-captured switch");
            Check(original.CpuRevision != candidate.CpuRevision, "separate physical revision");
            Check(original.Matches(123) == candidate.Matches(123), "capture survives environment change");
            foreach (string? mode in new string?[] { null, "", "default" })
            {
                Environment.SetEnvironmentVariable("RT2_C_CPU_ASSIGNMENT", mode);
                Check(new CombatRewardReplay(baseRequest.Model).UsesCpuAssignmentEarly, "eligible production default: " + mode);
            }
        }
        finally { Environment.SetEnvironmentVariable("RT2_C_CPU_ASSIGNMENT", previous); }

        Console.WriteLine($"C_CPU_ASSIGNMENT_EARLY_PASS checks={checks};earlyRejections={earlyRejections};positives={positives};faults={faults};originalOracle=true");
        return 0;

        (Beta110CombatRewardRouteEvaluation? Result, string? Error) Stage(Beta110OpeningRewardState opening, CombatRewardCpuAssignmentEarlyPlan? prepared)
        {
            try { return (Beta110CombatRewardFastStage.EvaluateForFamily(opening, plan, catalog, baseRequest.Model.Ascension, assignmentEarly: prepared), null); }
            catch (InvalidOperationException error) { return (null, error.Message); }
        }
    }

    private static (bool Match, string? Error) Capture(CombatRewardReplay replay, ulong root)
    {
        try { return (replay.Matches(root), null); }
        catch (InvalidOperationException error) { return (false, error.Message); }
    }
}
