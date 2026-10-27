using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    // Called only by the native CPU-corpus export, outside timed search runs.
    private static void ExportCpuCombatCorpus(string directory, SearchContext context,
        OrderedPartyAuthority party, GameVersionDetection detection)
    {
        var evidence = new List<object>();
        foreach (int slot in new[] { 0, party.Players.Count - 1 })
        {
            var owner = party.Players[slot];
            bool exported = false;
            string lastOutcome = "NoNeutralSixBattleRoute";
            for (ulong ordinal = 0; ordinal < 4096 && !exported; ordinal++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
                if (!SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, owner.Ascension,
                    owner.PlayersCount, slot, owner, AncientOptionConditionProfile.BroadDefault,
                    SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards,
                    10, false, out var sourceRequest, out var requestError))
                    throw new InvalidDataException("PartyCpuCombatCorpus.SourceRequest:" + requestError);
                var streams = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, input.RootHash, 0);
                var choices = PartyNeowProjection.ProjectChoices(sourceRequest!, input.RootHash,
                    streams.Niche, streams.CombatPotionGeneration, true,
                    PartyInitialQuery.CapsuleEffectPremise(SearchQuery.Empty));
                var prediction = NormalCombatRewardSequencePredictor.PredictFromRootHash(
                    Beta111Profile.Instance, input.RootHash, seed, slot, owner.Ascension, choices,
                    owner.EffectAuthority, owner.WorldAuthority,
                    new(NormalCombatRewardProjectionScope.SearchExact,
                        NormalCombatRewardRouteSelectionMode.ProvablyNeutralRealRoutes) { BattleCount = 6 });
                foreach (var route in prediction.Routes.Where(route =>
                    route.Status == SeedDomainEvaluationStatus.Evaluated && route.Battles.Count == 6 &&
                    OpeningRewardNeutralityClassifier.Classify(route.RepresentativeContinuation) == OpeningRewardNeutrality.ProvablyNeutral))
                {
                    // This selects an authored request from a real positive
                    // projection. No pool membership or party prefix is rewritten.
                    var targets = new List<(int Battle, NormalCombatRewardCardResult Card)>();
                    var usedBattles = new HashSet<int>();
                    var usedIdentities = new HashSet<ModelKey>();
                    // The current C predictor deliberately labels model card
                    // projections Partial. Full Production Exact below owns
                    // acceptance; do not invent an Exact-only source gate.
                    var candidates = route.Battles.SelectMany(battle => battle.Cards
                        .Select(card => new { Battle = battle.BattleOrdinal, Card = card }))
                        .OrderBy(item => item.Card.Rarity == EffectCardRarity.Uncommon ? 0 :
                            item.Card.Rarity == EffectCardRarity.Rare ? 1 : 2)
                        .ThenBy(item => item.Battle).ThenBy(item => item.Card.Ordinal);
                    foreach (var item in candidates)
                    {
                        if (usedBattles.Contains(item.Battle) || usedIdentities.Contains(item.Card.CardKey)) continue;
                        targets.Add((item.Battle, item.Card));
                        usedBattles.Add(item.Battle);
                        usedIdentities.Add(item.Card.CardKey);
                        if (targets.Count == 3) break;
                    }
                    if (targets.Count != 3) { lastOutcome = "ThreeDistinctBattleIdentitiesUnavailable"; continue; }
                    ModelKey?[] slots = targets.OrderBy(target => target.Battle)
                        .Select(target => (ModelKey?)target.Card.CardKey).Concat(new ModelKey?[] { null, null, null }).ToArray();
                    var query = SearchQuery.Empty with { Players = party.Players.Select((_, index) =>
                        new PlayerOfferQuery(index, ModelKeySetFilter.Empty) { Conditions = index == slot
                            ? SearchQuery.Empty with { CombatCardRewards = new(6, CombatRewardSequenceOrderMode.Unordered, slots) }
                            : SearchQuery.Empty }).ToArray() };
                    var compiled = SearchCompiler.Compile(query, context);
                    var result = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 16));
                    if (!result.Success) throw new InvalidDataException("PartyCpuCombatCorpus.Compile:" + result.Issue);
                    var exact = ProductionExactSearchEvaluator.Evaluate(result.Plan!, input);
                    if (!exact.IsMatch)
                    {
                        lastOutcome = exact.Evaluation.Disposition + ":" + exact.FailureCode;
                        continue;
                    }
                    string workload = "C-slot" + slot + ".json";
                    ProductionSearchReplay.Write(Path.Combine(directory, workload),
                        new ProductionSearchReplayWorkload(1, query, new("000000000000", 1L << 34, int.MaxValue, 16, false)));
                    evidence.Add(new {
                        Players = party.Players.Count, ActiveSlot = slot, Character = owner.Character.CharacterKey,
                        Workload = workload, Positive = seed, SeedCandidatesExamined = ordinal + 1,
                        Context = party.Fingerprint, GameVersion = detection.NormalizedVersion,
                        Source = "NativeRuntimePartyAuthorityCapture + existing pure NormalCombatRewardSequencePredictor; not native battle execution",
                        PersonalArrival = "Real offered neutral route; personal Rewards root+slot+rewards, zero opening consumption",
                        SharedArrival = "Initial shared cursors; other owners unselected and empty under PartyInitialQuery premise; C has no shared-stream dependency",
                        OpeningPremise = PartyInitialQuery.OpeningPremiseId,
                        SelectedOpeningAuthored = false,
                        SourceRoute = route.RepresentativeContinuation.Route,
                        SourceContinuation = route.RepresentativeContinuation.ContinuationFingerprint,
                        SourceRewardsCheckpoint = route.RepresentativeContinuation.RewardsRngState,
                        PredictionAuthority = prediction.AuthorityFingerprint,
                        CapturedEffectAuthority = owner.EffectAuthority!.SnapshotFingerprint,
                        CapturedCardPoolCount = owner.EffectAuthority.CharacterRewardPool!.Count,
                        CapturedMultiplayerOnlyCards = owner.EffectAuthority.CharacterRewardPool.Count(card => card.IsMultiplayerOnly),
                        CapturedPotionPoolCount = owner.EffectAuthority.PotionPool!.Count,
                        CapturedMultiplayerOnlyPotions = owner.EffectAuthority.PotionPool.Count(potion => potion.IsMultiplayerOnly),
                        Targets = targets.OrderBy(target => target.Battle).Select(target => new {
                            SourceBattle = target.Battle, target.Card.CardKey, target.Card.Rarity, target.Card.Precision }).ToArray(),
                        FullPartyExactDisposition = exact.Evaluation.Disposition,
                        FullPartyExactEvidence = exact.Evaluation.Evidence,
                        FullPartyExactWitnesses = exact.Evaluation.Witnesses
                    });
                    exported = true;
                    break;
                }
            }
            if (!exported) throw new InvalidDataException("PartyCpuCombatCorpus.NoPositiveWithin4096:" +
                party.Players.Count + ":" + slot + ":" + lastOutcome);
        }
        File.WriteAllText(Path.Combine(directory, "C-evidence.json"),
            System.Text.Json.JsonSerializer.Serialize(evidence, ProductionSearchReplay.JsonOptions));
        Godot.GD.Print($"PARTY_CPU_COMBAT_CORPUS_PASS players={party.Players.Count};earlyLateSlots=true;window=6;distinctTargets=3;positiveExact=true");
    }
}
