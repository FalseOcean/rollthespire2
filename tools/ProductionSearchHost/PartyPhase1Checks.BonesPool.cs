using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    private static async Task CheckBonesPool(GameVersionDetection detection)
    {
        // Owner's actual two-Ironclad A10 run: compare against the game's own
        // eligibility method and RNG, not another RT2 implementation of the pool.
        const string seed = "R93FVP7EQ817";
        var profile = Beta111Profile.Instance;
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var root = profile.ComputeRootSeed(seed);
        int checks = 0;
        void Check(bool value, string why)
        { checks++; if (!value) throw new Exception("Bones pool regression: " + why); }
        var getValid = typeof(NeowsBones).GetMethod("GetValidRelics", BindingFlags.NonPublic | BindingFlags.Static)!;
        ModelKey[][] observed = [
            [BaseGameModelKeys.Relics.LostCoffer, BaseGameModelKeys.Relics.StoneHumidifier],
            [BaseGameModelKeys.Relics.SilkenTress, BaseGameModelKeys.Relics.NewLeaf]];
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        foreach (var (count, full) in new[] { (1, true), (2, true), (4, true), (2, false) })
        {
            var unlockData = (full ? UnlockState.all : UnlockState.none).ToSerializable();
            if (!full) unlockData.NumberOfRuns = 1;
            var unlocks = UnlockState.FromSerializable(unlockData);
            var nativePlayers = Enumerable.Range(0, count).Select(slot => Player.CreateForNewRun(
                ModelDb.AllCharacters.Single(c => c.Id.Entry == "IRONCLAD"), unlocks, (ulong)(slot + 1))).ToArray();
            _ = RunState.CreateForTest(nativePlayers, ascensionLevel: 10, seed: seed);
            for (int slot = 0; slot < count; slot++)
            {
                var effects = ReflectionNeowEffectSnapshotAdapter.Capture(profile, seed,
                    CharacterIdentity.FromKey(BaseGameModelKeys.Characters.Ironclad), 10, count, slot,
                    profile.ProfileId, "0.111.0", new(true, true), unlocks).EffectAuthority!;
                var nativePool = ((IEnumerable<RelicModel>)getValid.Invoke(null, [nativePlayers[slot]])!).ToList();
                var pool = effects.BonesEligibleRelics!.ToArray();
                Check(effects.BonesEligibilityExact, "captured eligibility is exact");
                Check(pool.Select(k => k.Entry).SequenceEqual(nativePool.Select(r => r.Id.Entry)),
                    $"ordered native pool: players={count}, full={full}, slot={slot}");
                if (full) Check(pool.Length == (count == 1 ? 28 : 27), "solo/party pool size");
                nativePlayers[slot].PlayerRng.Rewards.Shuffle(nativePool);
                var rng = NeowEffectRngContext.CreateFromRootHash(profile, root, slot);
                rng.Rewards.UnstableShuffle(pool);
                Check(pool.Take(2).Select(k => k.Entry).SequenceEqual(nativePool.Take(2).Select(r => r.Id.Entry)), "native pair");
                Check(rng.Rewards.CallCount == nativePlayers[slot].PlayerRng.Rewards.ToSerializable().counter,
                    "post-Bones Rewards consumption");
                if (count == 2 && full)
                    Check(pool.Take(2).SequenceEqual(observed[slot]), "Owner actual run pair");
            }
        }
        var party = PartyRuntimeAuthorityCapture.Capture(profile, seed,
            [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Ironclad],
            [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
        var context = SearchContextFactory.From(profile.ProfileId, BaseGameModelKeys.Characters.Ironclad, 10,
            party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        var shared = NeowEffectRngContext.CreateFromRootHash(profile, root, 0);
        for (int slot = 0; slot < 2; slot++)
        {
            var owner = party.Players[slot];
            Check(SeedPredictionRequest.TryCreate(seed, owner.Character, 10, 2, slot, owner,
                AncientOptionConditionProfile.BroadDefault, SeedPredictionDomainSelection.Neow, 10, false,
                out var request, out _), "information request");
            var choices = PartyNeowProjection.ProjectChoices(request!, root, shared.Niche, shared.CombatPotionGeneration);
            var choice = choices.Single(c => c.RelicKey == bones);
            Check(choice.BonesOutcome!.OfferedRelics.SequenceEqual(observed[slot]), "information projection matches actual pair");
            var continuation = choice.BonesOutcome.OriginalRoutes.First().OpeningRewardContinuation!;
            shared = shared.WithShared(continuation.NicheState!.Restore(), continuation.CombatPotionGenerationState!.Restore());
        }
        SearchQuery Table(bool actual) => SearchQuery.Empty with
        {
            Players = Enumerable.Range(0, 2).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
            {
                Conditions = SearchQuery.Empty with
                {
                    OpeningRoute = new(bones),
                    OpeningRouteRelicRequirement = new(bones, actual ? observed[slot] :
                        [BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.SilkenTress], BonesRouteOrderMode.AnyOrder),
                    StructuredOpeningEffects = actual ? [] : [new(BaseGameModelKeys.Relics.ArcaneScroll,
                        NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.ProductRelevantEffects,
                        NeowStructuredOutputKind.Card, [new ModelKey("CARD", "BARRICADE")])]
                }
            }).ToArray()
        };
        Check(VisibleSeedCandidateCodec.TryParseOrdinal(profile, seed, out var ordinal, out _, out _), "seed ordinal");
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        foreach (bool actual in new[] { false, true })
        {
            var compiled = SearchCompiler.Compile(Table(actual), context);
            var request = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 33, 1, 1)).Plan!;
            var result = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(profile, seed));
            Check(result.Evaluation.Disposition == (actual ? SearchDisposition.Match : SearchDisposition.NoMatch),
                "Exact accepts actual pair and rejects reported false positive");
            if (actual)
            {
                double onePlayer = 1d / 9 * 2 / (27 * 26);
                var probability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled)).Probability;
                Check(probability is { } p && Math.Abs(p - onePlayer * onePlayer) < 1e-15,
                    "party probability uses 27-item pool for both players");
            }
            var cpu = new NeowFamily.PartyInitial(request);
            var gpu = new NeowFamily.PartyInitial(request, preferGpu: true);
            try
            {
                Check(gpu.ConditionPerformance.UsesGpu, "GPU admission");
                var batch = new SearchBatch(ordinal, 33);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch),
                    FamilyCandidateSet.FromSortedAbi1(batch, [0UL, 2UL, 10UL, 32UL]) })
                {
                    var a = await cpu.InvokeAsync(execution, new(batch, request), input, default);
                    var b = await gpu.InvokeAsync(execution, new(batch, request), input, default);
                    Check(a.EnumerateLogicalOrdinals().SequenceEqual(b.EnumerateLogicalOrdinals()), "CPU/GPU dense/compact parity");
                    Check(a.EnumerateLogicalOrdinals().Contains(0UL) == actual, "fast keeps actual and rejects stale pair");
                }
            }
            finally { await cpu.DisposeAsync(execution); await gpu.DisposeAsync(execution); }
        }
        GD.Print($"PARTY_BONES_POOL_PASS checks={checks};seed={seed};nativePool=true;soloUnchanged=true;partialUnlock=true;information=true;exact=true;cpuGpu=true;probability=true");
    }
}
