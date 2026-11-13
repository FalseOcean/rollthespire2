using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using Ordinary = RolltheSpire2.Core.Identity.BaseGameModelKeys.OrdinaryRelics;
using Relics = RolltheSpire2.Core.Identity.BaseGameModelKeys.Relics;

internal static class NeowCapsuleNonCombatChecks
{
    internal static int Run()
    {
        const string version = "0.113.0";
        var hero = BaseGameModelKeys.Characters.Ironclad;
        var fixture = Beta110FastSearchFixtureAuthority.CreateBeta111(hero, 10, 0, 1, version);
        var effects = fixture.EffectAuthority!;
        var oldCoin = new NeowEffectRelicSnapshot(Ordinary.OldCoin, 0, EffectRelicRarity.Rare,
            NestedRelicEffectKind.UnknownHook, SourceAssembly: "sts2");
        var authority = fixture.WithEffectAuthority(effects with { SharedRelicPoolSource = [..effects.SharedRelicPoolSource!, oldCoin] });
        var curse = new NeowStructuredEffectSearchCondition(Relics.NeowsBones, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.FinalCurse, NeowStructuredOutputKind.Curse, [BaseGameModelKeys.Cards.Guilty]);
        var coin = new NeowStructuredEffectSearchCondition(Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [Ordinary.OldCoin]);
        var query = SearchQuery.Empty with
        {
            OpeningRoute = new(Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(Relics.NeowsBones, [Relics.LeafyPoultice, Relics.SmallCapsule]),
            StructuredOpeningEffects = [curse, coin]
        };
        NeowReplayPlan Compile(SearchQuery request, RuntimeContextAuthoritySnapshot snapshot)
        {
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, hero, 10, snapshot,
                new(version, version, "beta", "Capsule fixture", true, ""), AncientOptionConditionProfile.BroadDefault);
            var prepared = SearchPreparationCompiler.Compile(SearchCompiler.Compile(request, context), new("000000000000", 64, 1, 1));
            if (!prepared.Success) throw new InvalidDataException(prepared.Issue);
            return NeowReplayPlan.Compile(prepared.Plan!.Model, false);
        }

        var complete = Compile(query, authority);
        var unspecified = Compile(query with { StructuredOpeningEffects = [curse] }, authority);
        var upgrade = Compile(query with { StructuredOpeningEffects = [curse, coin with { OutputKeys = [Ordinary.Whetstone] }] }, authority);
        var missingSources = authority.WithEffectAuthority(authority.EffectAuthority! with { RelicBagSourcePoolsExact = false });
        var incomplete = Compile(query, missingSources);
        Check(!HasRemainder(complete), "complete Old Coin binding");
        Check(HasRemainder(unspecified) && HasRemainder(upgrade) && HasRemainder(incomplete),
            "unbound contents, upgrade pickup and missing bag authority stay conservative");

        byte leafy = Beta110FastRelicCatalog.LeafyPoultice, small = Beta110FastRelicCatalog.SmallCapsule;
        int rejected = 0;
        for (ulong root = 0; root < 64; root++)
        {
            var rng = NeowFamilyReplay.NonCombatRng(root, complete);
            var pool = complete.Authority.EffectCatalog.GeneratedCurseIds;
            bool expected = pool[rng.NextInt(pool.Length)] == complete.RequiredFinalCurseIds.Single();
            Check(NeowFamilyReplay.Route(root, complete, leafy, small, default) == expected &&
                NeowFamilyReplay.Route(root, complete, small, leafy, default) == expected, "both acquisition orders");
            Check(NeowFamilyReplay.Route(root, unspecified, leafy, small, default), "unbound contents retain dependent candidates");
            if (!expected) rejected++;
        }
        Check(rejected is > 0 and < 64, "fixture distinguishes retained and rejected candidates");
        Console.WriteLine($"CAPSULE_NONCOMBAT_CONTINUATION_PASS rejected={rejected}/64");
        return 0;

        static bool HasRemainder(NeowReplayPlan plan) => plan.FilterRemainders.Contains("N.Neow:CapsuleNonCombatContinuation");
        static void Check(bool ok, string reason)
        {
            if (!ok) throw new InvalidDataException("CapsuleNonCombatContinuation: " + reason);
        }
    }
}
