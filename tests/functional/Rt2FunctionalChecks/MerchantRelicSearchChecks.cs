using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Persistence;

internal static class MerchantRelicSearchChecks
{
    internal static int Run()
    {
        const string version = "0.113.1";
        const string seed = "6Q8LL102Y4CF";
        var profile = Beta111Profile.Instance;
        var world = RelicSequenceFixtureFactory.CreateModern();
        var source = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, 10, 0, 1, version);
        var authority = source.WithEffectAuthority(source.EffectAuthority! with { MerchantInitialInventoryShapeExact = true })
            .WithWorldAuthority(world with { CapturedProfileId = profile.ProfileId, Beta109Generation = world.Beta109Generation! with
                { Profile = profile.ProfileId, RngSemantics = RngSemanticProfileId.Modern113 } });
        SearchContext Context(RuntimeContextAuthoritySnapshot input) => SearchContextFactory.From(input.ProfileId,
            input.Character.CharacterKey, input.Ascension, input,
            new(version, version, "beta", "fixture", true, ""), AncientOptionConditionProfile.BroadDefault);
        var context = Context(authority);
        var root = TrustedRootHashInput.FromCanonicalSeed(profile, seed);
        var bags = RelicSequencePredictor.PredictFromRootHash(profile, root.RootHash, seed, authority.WorldAuthority);
        var offers = NormalMerchantRelicProjector.Project(root.RootHash, authority, bags, merchantCount: 2);
        if (offers.Merchants.Count < 2 || offers.Merchants[0].Left.RelicKey is not { } left ||
            offers.Merchants[0].Middle.RelicKey is not { } middle || offers.Merchants[1].Middle.RelicKey is not { } later)
            throw new InvalidDataException("Missing test offers: " + offers.IssueCode);
        var query = SearchQuery.Empty with { MerchantRelicConditions = [new(1, 1, left), new(1, 2, later)] };
        PreparedSearch Prepare(SearchQuery q, bool skip = false, SearchContext? ctx = null) =>
            SearchPreparationCompiler.Compile(SearchCompiler.Compile(q, ctx ?? context),
                new(seed, 1, 1, 1, SkipExactValidation: skip)).Plan ?? throw new InvalidDataException("Missing prepared plan");
        SearchQueryEvaluation Evaluate(SearchQuery q, SearchContext? ctx = null) =>
            ProductionExactSearchEvaluator.Evaluate(Prepare(q, ctx:ctx).Model, root).Evaluation;
        void Check(bool ok, string name) { if (!ok) throw new InvalidDataException("MerchantRelic: " + name); }
        var positive = Evaluate(query);
        Check(positive.Disposition == SearchDisposition.Match &&
            positive.Evidence.Any(e => e.RelatedKey == left && e.Ordinal == 1 && e.OutcomeId == "Left") &&
            positive.Evidence.Any(e => e.RelatedKey == later && e.Ordinal == 2 && e.OutcomeId == "Middle"), "either slot and window evidence");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, middle)] }).Disposition == SearchDisposition.Match,
            "right boundary inclusive");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, later)] }).Disposition == SearchDisposition.NoMatch,
            "outside window rejected");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, middle), new(1, 1, left)] }).Disposition == SearchDisposition.Match,
            "shelf accepts both targets at one shop in either slot order");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 2, later), new(1, 2, left)] }).Disposition == SearchDisposition.Match,
            "shelf accepts targets from separate shops in either visit order");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, left), new(1, 1, later)] }).Disposition == SearchDisposition.NoMatch,
            "shelf requires all targets inside the shared window");
        var missing = Context(authority.WithWorldAuthority(null));
        Check(Evaluate(query, missing).Disposition == SearchDisposition.Unknown, "incomplete projection cannot confirm");
        Check(Evaluate(query with { FishingRodDrawResearch = true }).Disposition == SearchDisposition.Match, "combined exact");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, later)], FishingRodDrawResearch = true }).Disposition ==
            SearchDisposition.NoMatch, "another matching domain cannot bypass missing target");
        var partial = authority.WithEffectAuthority(authority.EffectAuthority! with { MerchantInitialInventoryShapeExact = false });
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 2, left)] }, Context(partial)).Disposition == SearchDisposition.Match &&
            Evaluate(query with { MerchantRelicConditions = [new(1, 2, later)] }, Context(partial)).Disposition == SearchDisposition.Unknown &&
            Evaluate(query with { MerchantRelicConditions = [new(1, 2, later), new(1, 1, later)] }, Context(partial)).Disposition == SearchDisposition.NoMatch,
            "partial window retains witnesses and independent rejections");
        var options = SearchPresetStore.CreateJsonOptions();
        var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(query, options), options)!;
        Check(Prepare(restored).Model.CompiledSearch.SemanticFingerprint == Prepare(query).Model.CompiledSearch.SemanticFingerprint &&
            SearchContinuationIdentity.For(Prepare(query)) != SearchContinuationIdentity.For(Prepare(SearchQuery.Empty)), "preset and continuation identity");
        try
        {
            _ = Prepare(query with { MerchantRelicConditions = [new(4, 3, left)] });
            throw new InvalidDataException("Invalid window accepted");
        }
        catch (ArgumentException ex) when (ex.Message == "MerchantRelic:InvalidWindow") { }
        foreach (bool skip in new[] { false, true })
        {
            var plan = Prepare(query, skip);
            var physical = FamilyExecutionCoordinator.Plan(plan, gpuAvailable: false);
            Check(physical.FilterRemainders.Contains("MerchantRelic:ProductionExactOnly"), "coverage identifies exact-only duty");
            Scan().GetAwaiter().GetResult();
            async Task Scan()
            {
                await using var session = FamilyExecutionCoordinator.Start(plan, gpuAvailable:false);
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
                Check(session.TryReadCandidate(out var hit) && hit!.Seed == seed && hit.IsUnfiltered == skip &&
                    hit.ValidationDisposition == (skip ? SearchDisposition.NotEvaluatedByPolicy : SearchDisposition.Match), "production candidate validation");
            }
        }
        Console.WriteLine($"MERCHANT_RELIC_SEARCH=PASS;seed={seed};left={left};middle={middle};laterMiddle={later};partialWindowsAndSkipVerified=true");
        return 0;
    }
}
