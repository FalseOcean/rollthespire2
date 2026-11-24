using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using Relics = RolltheSpire2.Core.Identity.BaseGameModelKeys.Relics;

internal static class BonesOrderReuseChecks
{
    internal static int Run()
    {
        const string version = "0.113.1";
        var hero = BaseGameModelKeys.Characters.Ironclad;
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(hero, 10, 0, 1, version);
        var effects = authority.EffectAuthority!;
        var potions = effects.PotionPool!;
        authority = authority.WithEffectAuthority(effects with {
            PotionPool = [..potions, ..potions.Select(p => p with {
                PotionKey = new("POTION", p.PotionKey.Entry + "_2"), PoolOrder = p.PoolOrder + potions.Count })]
        });
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, hero, 10, authority,
            new(version, version, "beta", "Bones order fixture", true, ""), AncientOptionConditionProfile.BroadDefault);
        var card = new ModelKey("CARD", "BENCH_CHARACTER_0");
        var coffer = new NeowStructuredEffectSearchCondition(Relics.LostCoffer, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [card]);
        var holster = new NeowStructuredEffectSearchCondition(Relics.PhialHolster, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.GeneratedPotions, NeowStructuredOutputKind.Potion, [new("POTION", "BENCH_UNCOMMON_POTION")]);
        var curse = new NeowStructuredEffectSearchCondition(Relics.NeowsBones, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.FinalCurse, NeowStructuredOutputKind.Curse, [BaseGameModelKeys.Cards.Guilty]);
        var newLeaf = coffer with { SourceRelicKey = Relics.NewLeaf, Scope = NeowStructuredEffectScope.TransformResults };
        bool disabled = Environment.GetEnvironmentVariable("RT2_DISABLE_NEOW_BONES_ORDER_REUSE") == "1";
        int cases = 0;
        Check(Relics.LostCoffer, Relics.PhialHolster, [coffer, holster], reuse: true, equal: true);
        Check(Relics.LostCoffer, Relics.GoldenPearl, [coffer], reuse: true, equal: true);
        Check(Relics.ArcaneScroll, Relics.LostCoffer, [coffer], reuse: false, equal: false);
        Check(Relics.Kaleidoscope, Relics.PhialHolster, [holster], reuse: false, equal: false);
        Check(Relics.Kaleidoscope, Relics.PhialHolster, [curse], reuse: true, equal: true);
        Check(Relics.LeafyPoultice, Relics.NewLeaf, [newLeaf], reuse: false, equal: false);
        Check(Relics.PrecariousShears, Relics.NewLeaf, [newLeaf], reuse: true, equal: true);
        Check(Relics.LostCoffer, Relics.PhialHolster, [coffer], reuse: false, equal: true, pinned: true);
        Console.WriteLine($"BONES_ORDER_REUSE=PASS cases={cases} rootsPerCase=2048 disabled={disabled}");
        return 0;

        void Check(ModelKey a, ModelKey b, NeowStructuredEffectSearchCondition[] conditions, bool reuse, bool equal,
            bool pinned = false)
        {
            cases++;
            var query = SearchQuery.Empty with {
                OpeningRoute = new(Relics.NeowsBones),
                OpeningRouteRelicRequirement = pinned ? new(Relics.NeowsBones, [a, b], BonesRouteOrderMode.ExactOrder)
                    : new(Relics.NeowsBones, [a, b]),
                StructuredOpeningEffects = conditions
            };
            var prepared = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context), new("000000000000", 64, 1, 1));
            var plan = NeowReplayPlan.Compile((prepared.Plan ?? throw new InvalidDataException(prepared.Issue)).Model, false);
            string label = $"{a.Entry}+{b.Entry}/{string.Join(",", conditions.Select(c => c.SourceRelicKey.Entry))}/pinned={pinned}";
            if (!NeowFamilyGpuPlan.TryCreate(plan, out var gpu, out var issue) || gpu is null ||
                gpu.ReuseBonesOrder != (reuse && !disabled) || gpu.UsesIndependentBonesOrder(3) != (reuse && !disabled))
                throw new InvalidDataException($"{label} GPU reuse={gpu?.ReuseBonesOrder}, issue={issue}");
            if (!Beta110FastRelicCatalog.TryGetId(a, out var first) || !Beta110FastRelicCatalog.TryGetId(b, out var second))
                throw new InvalidDataException(label);
            if (reuse && (NeowBonesOrderReuse.CanShare(plan with { ExactOnly = ["UnprojectedGoal"] }, [first, second]) ||
                NeowBonesOrderReuse.CanShare(plan with { VanillaBonesOrderAuthority = false }, [first, second])))
                throw new InvalidDataException(label + " accepted an additional goal or an unverified source");
            if (a == Relics.PrecariousShears &&
                NeowBonesOrderReuse.CanShare(plan with { RemovableBasicStrikes = 1 }, [first, second]))
                throw new InvalidDataException(label + " accepted insufficient duplicate Strikes");
            int differences = 0, passed = 0;
            for (ulong root = 0; root < 2048; root++)
            {
                var rewards = new Beta110FastRng(unchecked(root + NeowSingleplayerReplay.RewardsHash));
                bool forward = NeowSingleplayerReplay.Route(root, plan, first, second, rewards);
                bool reverse = NeowSingleplayerReplay.Route(root, plan, second, first, rewards);
                if (forward != reverse) differences++;
                if (forward) passed++;
            }
            if ((differences == 0) != equal || passed is 0 or 2048)
                throw new InvalidDataException($"{label} route differences={differences} first order passed={passed}");
        }
    }
}
