using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Tests.FastSearch;

internal static class TransformationAncientChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool pass, string description)
        {
            if (!pass) throw new InvalidOperationException(description);
            checks++;
        }

        var source = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context.Authority;
        var captured = source.EffectAuthority!;
        var initial = OpeningTransformationContinuation.Initial(captured);
        Check(initial is { StreamOffset: 0, BasicCount: 10, BasicStrikeDefendCount: 9 },
            "Ironclad can submit Bash to Astrolabe while Pandora uses nine eligible Strike/Defend instances");
        Check(OpeningTransformationContinuation.Initial(captured with { OrderedDeck = null }) is
            { StreamOffset: 0, BasicCount: null, BasicStrikeDefendCount: null }, "Missing deck leaves the clean cursor available");
        Check(OpeningTransformationContinuation.FromDeck(null, captured.OrderedDeck) is
            { StreamOffset: null, BasicStrikeDefendCount: 9 }, "Missing cursor leaves the observed materials available");

        var strike = captured.OrderedDeck!.First(card => card.IsStrike);
        var candidates = new[]
        {
            strike with { CardKey = new ModelKey("CARD", "CHECK_RARE"), Rarity = EffectCardRarity.Rare, PoolOrder = 4, CanUpgrade = false },
            strike with { CardKey = new ModelKey("CARD", "CHECK_COMMON"), Rarity = EffectCardRarity.Common, PoolOrder = 2 },
            strike with { CardKey = new ModelKey("CARD", "CHECK_UNCOMMON"), Rarity = EffectCardRarity.Uncommon, PoolOrder = 3 }
        };
        var invalid = candidates[0] with { CardKey = new ModelKey("CARD", "CHECK_MULTIPLAYER"), IsMultiplayerOnly = true, PoolOrder = 1 };
        var authority = captured with { TransformPool = [candidates[0], invalid, strike, candidates[1], candidates[2]] };
        var projector = TransformationRelicProjector.Capture(authority);
        Check(projector is not null, "Captured Standard character pool is usable");
        Check(TransformationRelicProjector.Capture(authority with { TransformPool = [invalid, strike] }) is null,
            "Missing eligible replacements stay Unknown");
        Check(TransformationRelicProjector.Capture(authority with
            { TransformPool = [candidates[0] with { PoolId = "other" }, candidates[1]] }) is null,
            "A different candidate pool cannot stand in for the initial Basic target pool");

        const ulong root = 0x35c5eedUL;
        IRuntimeProfile profile = Beta111Profile.Instance;
        var ordered = candidates.OrderBy(card => card.PoolOrder).ToArray();
        foreach (var (semantics, slot, streamRoot, stream) in new[]
                 {
                     (RngSemanticProfileId.Modern113, 0, root, "transformations"),
                     (RngSemanticProfileId.Modern113, 2, root + 2, "transformations"),
                     (RngSemanticProfileId.Modern110, 2, root, "niche")
                 })
        {
            var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(streamRoot, stream));
            var expected = Enumerable.Range(0, 15).Select(_ => ordered[rng.NextInt(ordered.Length)]).ToArray();
            var pantry = projector!.Project(profile, root, slot, semantics, 0, 15, upgrade: false);
            Check(pantry.Select(card => card.CardKey).SequenceEqual(expected.Select(card => card.CardKey)) &&
                pantry.All(card => !card.Upgraded), $"Pandora follows ordered replacements with repeat draws for {semantics}/{slot}");
            var selected = projector.Project(profile, root, slot, semantics, 2, 3, upgrade: true);
            Check(selected.SequenceEqual(expected.Skip(2).Take(3).Select(card =>
                new TransformationRelicCard(card.CardKey, card.Rarity, card.CanUpgrade))),
                $"Astrolabe moves two cursor steps and upgrades each legal replacement for {semantics}/{slot}");
        }

        var single = TransformationRelicProjector.Capture(authority with { TransformPool = [candidates[0]] })!;
        Check(single.Project(profile, root, 0, RngSemanticProfileId.Modern113, 0, 1, upgrade: true).Single() ==
            new TransformationRelicCard(candidates[0].CardKey, EffectCardRarity.Rare, false),
            "Astrolabe preserves a non-upgradable generated Rare");
        Check(single.Project(profile, root, 0, RngSemanticProfileId.Modern113, 6, 0, upgrade: false).Length == 0,
            "A zero-material Pandora box generates no replacements");

        var modern = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 10, 0, 1, "0.113.1")
            .WithEffectAuthority(captured).WithWorldAuthority(source.WorldAuthority);
        var box = new ModelKey("RELIC", "PANDORAS_BOX");
        var astro = new ModelKey("RELIC", "ASTROLABE");
        foreach (var selected in new[] { BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NeowsBones })
        {
            string seed = Enumerable.Range(0, 250).Select(i => i.ToString("D12")).First(s =>
                ModernNeowIdentityPredictor.PredictModernCore(s, modern, profile, true).RelicKeys.Contains(selected));
            ulong hash = profile.ComputeRootSeed(seed);
            if (!SeedPredictionRequest.TryCreate(seed, modern.Character, 10, 1, 0, modern, null, SeedPredictionDomainSelection.Neow,
                    false, out var request, out var error)) throw new InvalidOperationException(error.ToString());
            var facts = RuntimeProfileRegistry.Observe(new("0.113.1", "0.113.1", "beta", "fixture", true, ""), request!);
            var choice = facts.Neow!.Choices.Single(c => c.RelicKey == selected);
            SeedAncientTransformResult Read(ModelKey relic, int? slot = null, string route = "", int? count = null) =>
                SeedAncientTransformPredictor.Project(hash, request!, facts, relic, slot, route, count);
            var initialBox = Read(box);
            Check(initialBox.Status == SeedAncientTransformStatus.Ready && initialBox.Cards.Count == 10,
                "Pandora previews ten draws regardless of the nine starting Strikes and Defends");
            Check(Read(astro).Cards.Select(c => c.CardKey).SequenceEqual(initialBox.Cards.Take(3).Select(c => c.CardKey))
                && Read(astro).Cards.All(c => c.Upgraded), "Astrolabe uses the same initial prefix with upgrades");
            Check(Read(box, choice.SlotIndex, "absent route").Status == SeedAncientTransformStatus.OpeningUnavailable,
                "A stale selected route cannot silently use the initial sequence");
            if (selected == BaseGameModelKeys.Relics.NeowsBones)
            {
                Check(choice.BonesOutcome!.OriginalRoutes.Count > 0 && choice.BonesOutcome.OriginalRoutes.All(route =>
                    route.TransformationContinuation is not null), "Seed information retains each original Bones branch");
                Check(choice.OpeningRewardContinuations!.Routes.All(route =>
                    Read(box, choice.SlotIndex, route.Route.RouteId).Status != SeedAncientTransformStatus.OpeningUnavailable),
                    "Each route ID presented by the opening strip selects its matching Bones continuation");
                Check(Read(box, choice.SlotIndex).Status == SeedAncientTransformStatus.OpeningUnavailable,
                    "Selecting Bones alone leaves the acquisition branch unresolved");
            }
            else
            {
                string route = choice.OpeningRewardContinuations!.Routes.Single().Route.RouteId;
                var afterLeafy = Read(box, choice.SlotIndex, route);
                var expected = Read(box, count: 12).Cards.Skip(2).ToArray();
                Check(afterLeafy.PreviewCount == 10 && afterLeafy.Cards.SequenceEqual(expected),
                    "Selecting Leafy skips its two draws and still shows the full preview");
                foreach (int? remaining in new int?[] { null, 0, 2 })
                {
                    var depleted = choice with { TransformationContinuation = new(2, remaining, remaining) };
                    var partial = facts with { Neow = facts.Neow with { Choices = [depleted] } };
                    var pandora = SeedAncientTransformPredictor.Project(hash, request!, partial, box, choice.SlotIndex, route);
                    var astrolabe = SeedAncientTransformPredictor.Project(hash, request!, partial, astro, choice.SlotIndex, route);
                    Check(pandora.Cards.SequenceEqual(expected) && astrolabe.Cards.Count == 3 &&
                        astrolabe.Cards.Select(c => c.CardKey).SequenceEqual(expected.Take(3).Select(c => c.CardKey)),
                        "Unknown or depleted opening materials never suppress the next draws from a known cursor");
                }
                Check(Read(box, choice.SlotIndex, route, 2).Cards.SequenceEqual(initialBox.Cards.Skip(2).Take(2)) &&
                    Read(box, choice.SlotIndex, route, 0).Status == SeedAncientTransformStatus.Ready,
                    "Manual Pandora material count preserves the selected cursor, including zero");
            }
        }
        foreach (var oldEffects in new[] { captured, captured with { OrderedDeck = [] } })
        {
            const string seed = "6Q6B0U2XA308";
            var oldAuthority = source.WithEffectAuthority(oldEffects);
            if (!SeedPredictionRequest.TryCreate(seed, source.Character, 10, 1, 0, oldAuthority, null,
                    SeedPredictionDomainSelection.Neow, false, out var oldRequest, out var error)) throw new InvalidOperationException(error.ToString());
            var oldFacts = RuntimeProfileRegistry.Observe(new("0.111.0", "0.111.0", "beta", "fixture", true, ""), oldRequest!);
            var result = SeedAncientTransformPredictor.Project(profile.ComputeRootSeed(seed), oldRequest!, oldFacts, box, null, "");
            Check(result.Status == (oldEffects.OrderedDeck!.Count == 0 ? SeedAncientTransformStatus.Ready :
                SeedAncientTransformStatus.UnsupportedVersion) && result.Cards.Count == 0,
                "On 0.111 only zero eligible materials settles Pandora independently of the shared Niche stream");
        }
        Console.WriteLine($"Transformation ancient checks passed ({checks}).");
        return TransformationAncientPrefilterChecks.Run();
    }
}
