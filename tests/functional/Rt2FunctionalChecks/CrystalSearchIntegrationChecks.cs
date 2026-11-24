using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

internal static class CrystalSearchIntegrationChecks
{
    private const string Version = "0.113.1";
    private const CrystalSphereSearchRelics Premises = CrystalSphereSearchRelics.PrismaticGem |
        CrystalSphereSearchRelics.DingyRug | CrystalSphereSearchRelics.Driftwood;
    private static readonly ModelKey Hero = BaseGameModelKeys.Characters.Ironclad;
    private static readonly ModelKey CrystalSphere = new(BaseGameModelKeys.Categories.Event, "CRYSTAL_SPHERE");

    internal static int Run()
    {
        int checks = 0;
        void Check(bool result, string reason)
        {
            checks++;
            if (!result) throw new InvalidOperationException("CrystalSearchIntegrationChecks: " + reason);
        }

        var authority = CreateAuthority();
        var effects = authority.EffectAuthority!;
        var own = effects.CharacterRewardPool!;
        var other = effects.OtherCharacterPools!;
        var regent = other.Single(pool => pool.CharacterKey == BaseGameModelKeys.Characters.Regent).Cards;
        var silent = other.Single(pool => pool.CharacterKey == BaseGameModelKeys.Characters.Silent).Cards;
        var colorless = effects.ColorlessRewardPool!;
        var expectedPool = regent.Concat(own).Concat(silent).Concat(colorless)
            .Where(card => !card.IsMultiplayerOnly && card.IsUnlockedInCapturedPool && card.EligibleForPostCombatRewardByPoolMembership)
            .DistinctBy(card => card.CardKey).ToArray();

        Check(CrystalSphereSearchSemantics.Pool(effects, 1, CrystalSphereSearchRelics.None, Hero)!.SequenceEqual(own),
            "Base premise supplies only the event owner's character cards in source order");
        Check(CrystalSphereSearchSemantics.Pool(effects, 1, Premises, Hero)!.SequenceEqual(expectedPool),
            "Prismatic takes native character pool order ahead of owner union and Dingy appends colorless; early identity wins");
        var identityOnly = effects with { OtherCharacterPoolsExact = false, ColorlessRewardPoolExact = false,
            OtherCharacterIdentityPoolsExact = true, ColorlessRewardIdentityPoolExact = true };
        Check(CrystalSphereSearchSemantics.Pool(identityOnly, 1, Premises, Hero)!.SequenceEqual(expectedPool),
            "Dingy and Prismatic keep card order with captured identities and incomplete behavior metadata");
        var party = CrystalSphereSearchSemantics.Pool(effects, 2, Premises, Hero)!;
        Check(party.Any(card => card.IsMultiplayerOnly) && !party.Any(card => !card.IsUnlockedInCapturedPool ||
            !card.EligibleForPostCombatRewardByPoolMembership), "Mode alters only multiplayer membership and keeps reward/unlock requirements");
        Check(CrystalSphereSearchSemantics.Pool(effects with { NativeCharacterPoolOrder = null }, 1, Premises, Hero) is null &&
            CrystalSphereSearchSemantics.Pool(effects with { ColorlessRewardPoolExact = false }, 1, Premises, Hero) is null,
            "Unknown native Prismatic order or Dingy pool leaves named-card availability unresolved");

        var uncommon = regent.First(card => card.Rarity == EffectCardRarity.Uncommon).CardKey;
        var rare = own.First(card => card.Rarity == EffectCardRarity.Rare).CardKey;
        var named = new EventResultSearchCondition(EventResultConditionKind.CrystalSphereRewards, CrystalSphere)
        {
            CrystalDivinations = 3,
            CrystalTargetCards = [uncommon],
            CrystalRelics = Premises
        };
        var authored = named with { CrystalTargetCards = [rare, uncommon, rare] };
        var json = SearchPresetStore.CreateJsonOptions();
        var restored = JsonSerializer.Deserialize<EventResultSearchCondition>(JsonSerializer.Serialize(authored, json), json)!;
        Check(restored.IsValid && restored.CrystalRelics == Premises &&
            restored.CrystalTargetCards!.SequenceEqual(authored.CrystalTargetCards!),
            "JSON retains premise flags and authored card order including repeated identities");
        var state = new EventEditorStateSnapshot([], -1, -1, -1, null, [CrystalSphere.Entry]) { OpportunityResults = [authored] };
        var restoredState = JsonSerializer.Deserialize<EventEditorStateSnapshot>(JsonSerializer.Serialize(state, json), json)!;
        Check(restoredState.OpportunityResults.Single().CrystalTargetCards!.SequenceEqual(authored.CrystalTargetCards!),
            "Editor snapshot retains every named card target on reload");
        var compiled = Compile(authority, authored);
        var same = Compile(authority, restored);
        Check(compiled.SemanticFingerprint == same.SemanticFingerprint &&
            SearchContinuationIdentity.For(compiled) == SearchContinuationIdentity.For(same),
            "A round-tripped named-card query retains semantic and resume identity");
        foreach (var changed in new[]
        {
            authored with { CrystalTargetCards = [rare, uncommon] },
            authored with { CrystalRelics = Premises & ~CrystalSphereSearchRelics.Driftwood },
            authored with { CrystalRelics = Premises & ~CrystalSphereSearchRelics.PrismaticGem },
            authored with { CrystalRelics = Premises & ~CrystalSphereSearchRelics.DingyRug }
        })
        {
            var edited = Compile(authority, changed);
            Check(edited.SemanticFingerprint != compiled.SemanticFingerprint &&
                SearchContinuationIdentity.For(edited) != SearchContinuationIdentity.For(compiled),
                "A different copy count or relic premise cannot resume the prior query");
        }
        var reordered = authority.WithEffectAuthority(effects with
        {
            NativeCharacterPoolOrder = effects.NativeCharacterPoolOrder!.Reverse().ToArray()
        });
        Check(SearchContinuationIdentity.For(Compile(reordered, authored)) != SearchContinuationIdentity.For(compiled),
            "A different captured native pool order changes continuation identity");

        var namedPlan = Prepare(authority, named);
        Check(!EventResultFamily.TryCreate(namedPlan.Model, FilterExecutionPreferences.From(namedPlan.RunOptions), out _) &&
            EventOpportunitySemantics.FilterRemainder(named.Kind, authority.ProfileId, authority.VersionResolution.SemanticProfileId) ==
                EventOpportunitySemantics.CrystalFilterRemainder,
            "Named crystal solving remains a final-model obligation with no empty E filter participation");
        var trial = new EventResultSearchCondition(EventResultConditionKind.TrialCase, new(BaseGameModelKeys.Categories.Event, "TRIAL"))
            { TrialCase = TrialCaseTarget.Merchant };
        var mixedPlan = Prepare(authority, named, trial);
        var mixed = new EventResultFamilyPlan(mixedPlan.Model);
        Check(mixed.Conditions.SequenceEqual([trial]) && mixed.GpuSupported,
            "A named crystal target does not displace the independent legacy Trial batch predicate");
        CheckFilterWarnings(namedPlan, mixedPlan, Check);
        var outside = Exact(Prepare(authority, named with { CrystalRelics = null }), 0);
        Check(outside.Disposition == SearchDisposition.NoMatch && outside.FailureCode == "CrystalSphere.TargetOutsidePremisePool",
            "A card known to be absent under the selected premise cannot match");
        var missingAuthority = authority.WithEffectAuthority(effects with { NativeCharacterPoolOrder = null });
        var missing = Exact(Prepare(missingAuthority, named), 0);
        Check(missing.Disposition == SearchDisposition.Unknown && missing.FailureCode.StartsWith("CrystalSphere.CardPoolUnavailable", StringComparison.Ordinal),
            "A missing Prismatic pool is delivered as unresolved instead of rejecting the authored target");
        var impossibleRelic = named with { RelicRarity = EffectRelicRarity.Rare };
        Check(SearchFeasibilityAnalyzer.Analyze(Compile(missingAuthority, impossibleRelic, trial)).Proof?.ReasonCode ==
            SearchImpossibilityReasonCode.CrystalRelicRequiresFourMoves,
            "the known relic contradiction remains decisive with a missing card pool and an independent event condition");
        CheckPartyRelicImpossibility(impossibleRelic, Check);

        var category = named with { CrystalTargetCards = null, CrystalCards = CrystalSphereSeedCards.Uncommon, CrystalRelics = null };
        var ordinary = Prepare(missingAuthority, category);
        var pair = Prepare(authority, named, category with { CrystalRelics = Premises });
        var repeated = Prepare(authority, named with { CrystalTargetCards = [uncommon, uncommon] }, named);
        bool witnessed = false;
        for (ulong root = 0; root < 12; root++)
        {
            var old = CrystalSphereSeedSearch.Evaluate(root, 0, RngSemanticProfileId.Modern113, 3,
                new(CrystalSphereSeedCards.Uncommon, null));
            Check(Exact(ordinary, root).Disposition == Disposition(old.Verdict),
                "Rarity-category queries retain prior behavior without named-card pool authority");
            if (witnessed) continue;
            var expected = CrystalSphereSeedSearch.EvaluateCards(root, 0, RngSemanticProfileId.Modern113, 3,
                expectedPool, [uncommon], rarityTargets: new(CrystalSphereSeedCards.Uncommon, null), driftwood: true);
            var actual = Exact(pair, root);
            Check(actual.Disposition == Disposition(expected.Verdict), "Named acquisition and category share the same board witness search");
            if (expected.Witness is null) continue;
            var proof = actual.Evidence.Where(row => row.OutcomeId is not null).ToArray();
            Check(proof.Length == 2 && proof[0].OutcomeId == proof[1].OutcomeId &&
                proof[0].OutcomeId!.Contains(uncommon.Serialized, StringComparison.Ordinal),
                "Both conditions use the witness that acquired the authored card");
            var two = CrystalSphereSeedSearch.EvaluateCards(root, 0, RngSemanticProfileId.Modern113, 3,
                expectedPool, ImmutableArray.Create(uncommon, uncommon), driftwood: true);
            var repeatedExact = Exact(repeated, root);
            Check(repeatedExact.Disposition == Disposition(two.Verdict),
                "Duplicate targets compel two takes, while a second row for the same key does not require a third");
            witnessed = true;
        }
        Check(witnessed, "Fixture supplies at least one confirmed same-board named-card acquisition");

        Console.WriteLine($"CrystalSearchIntegrationChecks passed: {checks}");
        return 0;
    }

    private static void CheckPartyRelicImpossibility(EventResultSearchCondition condition, Action<bool, string> check)
    {
        var captured = Beta109WorldFixtureFactory.Create();
        var world = captured with
        {
            CapturedProfileId = RuntimeProfileId.Beta111,
            Beta109Generation = captured.Beta109Generation! with
            {
                Profile = RuntimeProfileId.Beta111,
                Ascension = 10,
                GameMode = WorldGameMode.Multiplayer,
                IsMultiplayer = true,
                PlayerCount = 2,
                LobbyPlayers = [new(0, Hero, false, true), new(1, Hero, false, true)]
            }
        };
        var players = Enumerable.Range(0, 2).Select(slot =>
            Beta110FastSearchFixtureAuthority.CreateBeta111(Hero, 10, slot, 2, Version).WithWorldAuthority(world)).ToArray();
        var context = Context(players[0]) with { Party = new OrderedPartyAuthority(players, ["FixtureP1", "FixtureP2"], world) };
        var table = SearchQuery.Empty with
        {
            Players = [new(0, ModelKeySetFilter.Empty), new(1, ModelKeySetFilter.Empty)
                { Conditions = SearchQuery.Empty with { EventResultConditions = [condition] } }]
        };
        var compiled = SearchCompiler.Compile(table, context);
        check(SearchFeasibilityAnalyzer.Analyze(compiled).Proof?.ReasonCode == SearchImpossibilityReasonCode.CrystalRelicRequiresFourMoves &&
            compiled.NormalizedQuery.Players[1].Conditions.EventResultConditions.SequenceEqual([condition]),
            "a party member's impossible goal rejects the conjunction while retaining the authored condition");
        var rejected = SearchPreparationCompiler.Compile(compiled, new("000000000000", 16, 1, 1));
        check(!rejected.Success && rejected.Plan is null && rejected.Disposition == SearchDisposition.NoMatch &&
            rejected.Issue.StartsWith("SearchImpossible:CrystalRelicRequiresFourMoves:", StringComparison.Ordinal),
            "direct party preparation applies the same proof before starting a scan");
    }

    private static void CheckFilterWarnings(PreparedSearch crystal, PreparedSearch mixed, Action<bool, string> check)
    {
        const string finalOnly = EventResultFamily.Id + ":" + EventOpportunitySemantics.CrystalFilterRemainder;
        const string missingFilter = EventResultFamily.Id + ":NoSelectedFilter";
        var exact = FamilyExecutionCoordinator.Plan(crystal, gpuAvailable: false);
        check(exact.FilterRemainders.SequenceEqual([finalOnly]) &&
            FamilyExecutionCoordinator.FilterWarningRemainders(exact).Count == 0,
            "Supported Crystal-only plan retains the final obligation without warning about missing E");
        var skipRequest = crystal with { RunOptions = crystal.RunOptions with { SkipExactValidation = true } };
        var skip = FamilyExecutionCoordinator.Plan(skipRequest, gpuAvailable: false);
        check(skip.AllConditionsUnfiltered && FamilyExecutionCoordinator.FilterWarningRemainders(skip).SequenceEqual([finalOnly]),
            "Skipping final validation discloses the actual unchecked Crystal obligation");

        var mixedExact = FamilyExecutionCoordinator.Plan(mixed, gpuAvailable: false);
        check(mixedExact.OrderedFamilies.Any(body => body.FamilyId == EventResultFamily.Id) &&
            mixedExact.FilterRemainders.SequenceEqual([finalOnly]) &&
            FamilyExecutionCoordinator.FilterWarningRemainders(mixedExact).Count == 0,
            "Applying legacy E to a mixed query leaves its expected Crystal responsibility for final validation");
        var unselected = mixedExact with { Stages = [] };
        unselected = unselected with
        {
            FilterRemainders = FamilyExecutionCoordinator.DescribeFilterRemainders(mixed, unselected, mixedExact.OrderedFamilies)
        };
        check(FamilyExecutionCoordinator.FilterWarningRemainders(unselected).SequenceEqual([missingFilter]) &&
            unselected.FilterRemainders.Contains(finalOnly),
            "A missing ordinary E stage still warns even beside a supported final-only Crystal condition");

        const string unsupported = EventResultFamily.Id + ":EventResult.SourceModelUnavailableForProfile:CrystalSphereRewards";
        var olderCause = EventOpportunitySemantics.FilterRemainder(EventResultConditionKind.CrystalSphereRewards,
            RuntimeProfileId.Stable107, RngSemanticProfileId.Stable107Legacy);
        var unavailable = exact with { FilterRemainders = [EventResultFamily.Id + ":" + olderCause] };
        check(FamilyExecutionCoordinator.FilterWarningRemainders(unavailable).SequenceEqual([unsupported]),
            "An unsupported Crystal model remains a warning when Exact is enabled");
    }

    private static RuntimeContextAuthoritySnapshot CreateAuthority()
    {
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(Hero, 10, 0, 1, Version);
        var effects = authority.EffectAuthority!;
        var own = effects.CharacterRewardPool!;
        var other = effects.OtherCharacterPools!.ToArray();
        int regent = Array.FindIndex(other, pool => pool.CharacterKey == BaseGameModelKeys.Characters.Regent);
        var regentPool = other[regent].Cards.ToArray();
        regentPool[0] = regentPool[0] with { IsMultiplayerOnly = true };
        regentPool[1] = regentPool[1] with { IsUnlockedInCapturedPool = false };
        regentPool[2] = regentPool[2] with { EligibleForPostCombatRewardByPoolMembership = false };
        other[regent] = other[regent] with { Cards = [own[0] with { InstanceId = "native-first" }, .. regentPool] };
        return authority.WithEffectAuthority(effects with
        {
            CharacterRewardIdentityPoolExact = true,
            OtherCharacterPools = other,
            NativeCharacterPoolOrder = [BaseGameModelKeys.Characters.Regent, Hero, BaseGameModelKeys.Characters.Silent]
        });
    }

    private static SearchContext Context(RuntimeContextAuthoritySnapshot authority) =>
        new(RuntimeProfileId.Beta111, Hero, 10, authority,
            new(Version, Version, "beta", "crystal-search-integration", true, ""), SearchEvaluationAssumptions.BroadDefault);

    private static CompiledSearch Compile(RuntimeContextAuthoritySnapshot authority, params EventResultSearchCondition[] conditions) =>
        SearchCompiler.Compile(SearchQuery.Empty with { EventResultConditions = conditions }, Context(authority));

    private static PreparedSearch Prepare(RuntimeContextAuthoritySnapshot authority, params EventResultSearchCondition[] conditions)
    {
        var result = SearchPreparationCompiler.Compile(Compile(authority, conditions), new("000000000000", 16, 1, 1));
        return result.Plan ?? throw new InvalidOperationException("Crystal search fixture preparation failed: " + result.Issue);
    }

    private static SearchQueryEvaluation Exact(PreparedSearch plan, ulong root) =>
        ProductionExactSearchEvaluator.Evaluate(plan.Model, TrustedRootHashInput.RootOnly(root)).Evaluation;

    private static SearchDisposition Disposition(CrystalSphereSeedVerdict verdict) => verdict switch
    {
        CrystalSphereSeedVerdict.Match => SearchDisposition.Match,
        CrystalSphereSeedVerdict.NoMatch => SearchDisposition.NoMatch,
        _ => SearchDisposition.Unknown
    };
}
