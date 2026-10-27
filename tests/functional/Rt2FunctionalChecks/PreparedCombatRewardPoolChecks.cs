using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class PreparedCombatRewardPoolChecks
{
    internal static int Run(int count = 32)
    {
        int checks = 0, projectedRoutes = 0;
        void Check(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidOperationException("PreparedRewardPools:" + reason); }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var source = context.Authority.EffectAuthority!;
        var world = context.Authority.WorldAuthority;
        var profile = RuntimeProfileRegistry.Select(context.Detection);
        string? old = Environment.GetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS");
        try
        {
            var card = source.CharacterRewardPool!.First(c => c.IsUnlockedInCapturedPool && !c.IsMultiplayerOnly &&
                c.Rarity == EffectCardRarity.Common).CardKey;
            var query = SearchQuery.Empty with { CombatCardRewards = new(1,
                CombatRewardSequenceOrderMode.Ordered, [card]) };
            ExactSearchExecutionRequest Compile(string? mode)
            {
                Environment.SetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS", mode);
                return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context),
                    new("000000000000", 65536, 30, 1)).Plan
                    ?? throw new InvalidOperationException("PreparedRewardPools.Compile");
            }
            var off = Compile("off");
            var on = Compile("on");
            var defaultPlan = Compile(null);
            var offOwner = off.RewardPoolPreparation!;
            var onOwner = on.RewardPoolPreparation!;
            var pools = onOwner.Pools ?? throw new InvalidOperationException("PreparedRewardPools.SourceNotApplicable");
            Check(!offOwner.Enabled && offOwner.Pools is null && offOwner.Capture().PreparationAttempts == 0,
                "off captures the switch without fixed-pool preparation");
            Check(onOwner.Enabled && onOwner.Capture().PreparationAttempts == 1 && onOwner.Capture().PreparedCount == 1,
                "on prepares the compiled request exactly once");
            Check(defaultPlan.RewardPoolPreparation is { Enabled: true, Pools: not null } &&
                defaultPlan.RewardPoolPreparation.Capture().PreparationAttempts == 1,
                "the production default prepares eligible fixed pools without an environment switch");
            Environment.SetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS", "off");
            Check(onOwner.Enabled && !offOwner.Enabled, "already compiled requests do not reread the comparison environment");
            var fp64Free = FamilyExecutionCoordinator.CreateRegisteredFamilies(on, true, false);
            Check(fp64Free.Any(f => f.FamilyId == "C.CombatReward") &&
                fp64Free.All(f => (f.GpuNumericRequirements & GpuNumericRequirements.Float64) == 0) &&
                ReferenceEquals(on.RewardPoolPreparation?.Pools, pools),
                "Core fixed-pool preparation is independent of NativeFloat64 and retains the existing FP64-free C body");
            Check(on.SnapshotFingerprint == off.SnapshotFingerprint &&
                on.Authority.EffectSnapshotFingerprint == off.Authority.EffectSnapshotFingerprint,
                "fixed preparation does not rename the semantic or captured authority identity");
            Check(!ReferenceEquals(pools.OwnedAuthority.CharacterRewardPool, source.CharacterRewardPool) &&
                !ReferenceEquals(pools.OwnedAuthority.PotionPool, source.PotionPool), "raw captured pools are detached");
            bool readOnly = false;
            try { ((IList<NeowEffectCardSnapshot>)pools.Cards)[0] = pools.Cards[0]; }
            catch (NotSupportedException) { readOnly = true; }
            Check(readOnly, "prepared card pool rejects mutation");
            readOnly = false;
            try { ((IList<NeowEffectPotionSnapshot>)pools.OwnedAuthority.PotionPool!)[0] = pools.Potions[0]; }
            catch (NotSupportedException) { readOnly = true; }
            Check(readOnly, "owned source potion pool rejects mutation");
            Check(pools.IsCompatible(profile.ProfileId, pools.OwnedAuthority with { SnapshotFingerprint = "root-local-bag" }, world),
                "root-local authority projection may change unrelated bag identity while sharing owned pool references");
            Check(!pools.IsCompatible(profile.ProfileId, source, world) &&
                !pools.IsCompatible(RuntimeProfileId.Unsupported, pools.OwnedAuthority, world) &&
                !pools.IsCompatible(profile.ProfileId, pools.OwnedAuthority with {
                    CharacterRewardPool = pools.OwnedAuthority.CharacterRewardPool!.ToArray() }, world),
                "a fingerprint or equal contents alone cannot certify different pool ownership/profile");

            var offBefore = offOwner.Capture();
            var onBefore = onOwner.Capture();
            for (ulong i = 0; i < (ulong)Math.Max(1, count); i++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(profile, i + 1);
                var input = TrustedRootHashInput.FromCanonicalSeed(profile, seed);
                SeedPredictionRequest Prediction(ExactSearchExecutionRequest plan)
                {
                    Check(SeedPredictionRequest.TryCreateFromRootHash(input, plan.Authority.Character, plan.Ascension,
                        1, 0, plan.Authority, plan.Evaluation.AncientOptionConditions,
                        SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards,
                        10, false, out var request, out _), "real copied-data prediction request is valid");
                    request!.RewardPoolPreparation = plan.RewardPoolPreparation;
                    var copy = request.WithComplexBonesDeckInteractions(false);
                    Check(ReferenceEquals(copy.RewardPoolPreparation, plan.RewardPoolPreparation),
                        "request copies preserve the immutable compiled preparation owner");
                    return copy;
                }
                var a = RuntimeProfileRegistry.PredictFromRootHash(off.Detection, Prediction(off), input);
                var b = RuntimeProfileRegistry.PredictFromRootHash(on.Detection, Prediction(on), input);
                var baselineRewards = a.Sections.Select(s => s.NormalCombatRewardSequencePrediction).Where(r => r is not null).ToArray();
                var preparedRewards = b.Sections.Select(s => s.NormalCombatRewardSequencePrediction).Where(r => r is not null).ToArray();
                Check(baselineRewards.Length > 0 && JsonSerializer.Serialize(baselineRewards) == JsonSerializer.Serialize(preparedRewards),
                    "complete Core reward outputs, continuation, RNG counts and diagnostics remain identical");
                projectedRoutes += preparedRewards.Sum(r => r!.Routes.Count(route => route.Battles.Count > 0));
                var exactA = ProductionExactSearchEvaluator.Evaluate(off, input);
                var exactB = ProductionExactSearchEvaluator.Evaluate(on, input);
                Check(exactA.FailureCode == exactB.FailureCode &&
                    JsonSerializer.Serialize(exactA.Evaluation) == JsonSerializer.Serialize(exactB.Evaluation),
                    "actual Production Exact disposition and full match/route evidence remain identical");
            }
            var offAfter = offOwner.Capture(); var onAfter = onOwner.Capture();
            Check(projectedRoutes > 0 && onAfter.PreparedHits > onBefore.PreparedHits &&
                onAfter.UnpreparedBuilds == 0 && onAfter.CompatibilityMisses == 0,
                "nonempty real reward routes actually use the prepared Core path");
            Check(offAfter.PredictionCalls - offBefore.PredictionCalls == onAfter.PredictionCalls - onBefore.PredictionCalls &&
                offAfter.UnpreparedBuilds - offBefore.UnpreparedBuilds == onAfter.PreparedHits - onBefore.PreparedHits &&
                offAfter.PreparedHits == 0 && onAfter.PreparationAttempts == 1,
                "same call boundary counts off repeated builds versus on fixed preparation/hits");

            var input0 = TrustedRootHashInput.FromCanonicalSeed(profile, VisibleSeedCandidateCodec.FormatOrdinal(profile, 1));
            NormalCombatRewardSequencePredictionResult Predict(NeowEffectAuthoritySnapshot? authority,
                CombatRewardPoolPreparation? preparation) => NormalCombatRewardSequencePredictor.PredictFromRootHash(
                    profile, input0.RootHash, input0.SeedIdentity, 0, context.Ascension, Array.Empty<NeowChoiceResult>(),
                    authority, world, NormalCombatRewardProjectionRequest.RichAnalysis, preparation);
            foreach (var invalid in new NeowEffectAuthoritySnapshot?[] { null,
                source with { CapturedProfileId = RuntimeProfileId.Unsupported },
                source with { CharacterRewardPool = null }, source with { PotionPool = null },
                source with { CharacterRewardPoolExact = false },
                source with { CharacterRewardPool = source.CharacterRewardPool!.Concat([source.CharacterRewardPool![0]]).ToArray() },
                source with { PotionPool = [] } })
            {
                var rejectedPreparation = new CombatRewardPoolPreparation(true);
                rejectedPreparation.Prepare(profile.ProfileId, invalid, world);
                Check(rejectedPreparation.Pools is null &&
                    JsonSerializer.Serialize(Predict(invalid, null)) == JsonSerializer.Serialize(Predict(invalid, rejectedPreparation)),
                    "missing/invalid source preserves the original failure result and precedence");
            }
            var mismatched = new CombatRewardPoolPreparation(true);
            mismatched.Prepare(profile.ProfileId, source, world);
            Check(JsonSerializer.Serialize(Predict(source, null)) == JsonSerializer.Serialize(Predict(source, mismatched)) &&
                mismatched.Capture().CompatibilityMisses == 1 && mismatched.Capture().UnpreparedBuilds == 1,
                "unowned authority retains the original pool construction instead of consuming cached data");
            var fresh = Compile("on");
            Check(!ReferenceEquals(fresh.RewardPoolPreparation, onOwner) &&
                fresh.RewardPoolPreparation!.Capture().PredictionCalls == 0 &&
                fresh.RewardPoolPreparation.Capture().PreparationAttempts == 1,
                "a new compiled request has its own bounded preparation lifetime");

            // Existing controlled slot fixture: this proves unchanged copied-
            // data Party execution, not a captured multiplayer speed claim.
            var party = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, [context, 2, null, null])!;
            var tableQuery = SearchQuery.Empty with { Players = party.Party!.Players.Select(owner =>
                new PlayerOfferQuery(owner.PlayerSlotIndex, ModelKeySetFilter.Empty) { Conditions = query }).ToArray() };
            ExactSearchExecutionRequest CompileParty(string mode)
            {
                Environment.SetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS", mode);
                return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(tableQuery, party),
                    new("000000000000", 65536, 30, 1)).Plan
                    ?? throw new InvalidOperationException("PreparedRewardPools.PartyCompile");
            }
            var partyOff = CompileParty("off"); var partyOn = CompileParty("on");
            Check(partyOff.RewardPoolPreparation is null && partyOn.RewardPoolPreparation is null &&
                ReferenceEquals(partyOff.Authority, partyOn.Authority),
                "Party is outside preparation reuse while retaining its original captured authority");
            for (int slot = 0; slot < party.Party.Players.Count; slot++)
            {
                var childOff = ExactSearchExecutionRequestFactory.ForPlayer(partyOff, partyOff.CompiledSearch.PlayerSearches[slot]);
                var childOn = ExactSearchExecutionRequestFactory.ForPlayer(partyOn, partyOn.CompiledSearch.PlayerSearches[slot]);
                Check(childOn.Evaluation.RequiresNormalCombatRewardDomain && childOn.RewardPoolPreparation is null &&
                    childOff.RewardPoolPreparation is null && ReferenceEquals(childOn.Authority, childOff.Authority) &&
                    !childOn.ExactOptions.OpeningReuse && !childOff.ExactOptions.OpeningReuse &&
                    ReferenceEquals(childOn.Authority.EffectAuthority!.CharacterRewardPool,
                        party.Party.Players[slot].EffectAuthority!.CharacterRewardPool),
                    "ForPlayer keeps the original Party C pool/prediction path without per-candidate preparation");
            }
            foreach (bool float64 in new[] { false, true })
                Check(FamilyExecutionCoordinator.CreateRegisteredFamilies(partyOn, true, float64)
                    .Any(f => f.FamilyId == "C.CombatReward"),
                    "preparation's solo scope does not prohibit Party C registration with or without FP64");
            for (ulong i = 0; i < (ulong)Math.Clamp(count, 1, 4); i++)
            {
                var input = TrustedRootHashInput.FromCanonicalSeed(profile, VisibleSeedCandidateCodec.FormatOrdinal(profile, i + 1));
                var a = ProductionExactSearchEvaluator.Evaluate(partyOff, input);
                var b = ProductionExactSearchEvaluator.Evaluate(partyOn, input);
                Check(a.FailureCode == b.FailureCode && JsonSerializer.Serialize(a.Evaluation) == JsonSerializer.Serialize(b.Evaluation),
                    "Party original Exact predictions retain complete dispositions/evidence across the preparation switch");
            }
        }
        finally { Environment.SetEnvironmentVariable("RT2_C_PREPARED_REWARD_POOLS", old); }
        Console.WriteLine($"PreparedCombatRewardPool checks={checks};projectedRoutes={projectedRoutes};PASS");
        return 0;
    }
}
