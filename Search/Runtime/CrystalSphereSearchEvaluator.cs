using System.Collections.Immutable;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Runtime;

/// <summary>Candidate-level same-path solving; no E batch filter claims this result.</summary>
internal static class CrystalSphereSearchEvaluator
{
    internal static SearchQueryEvaluation Evaluate(SearchModel plan, ulong rootHash,
        RuntimeContextAuthoritySnapshot authority, CancellationToken cancellationToken)
    {
        var rows = plan.Evaluation.EventResultConditions.Select((condition, index) => (condition, index))
            .Where(row => row.condition.Kind == EventResultConditionKind.CrystalSphereRewards).ToArray();
        if (rows.Length == 0) return SearchQueryEvaluation.Match();
        var semantics = authority.VersionResolution.SemanticProfileId;
        if (!EventOpportunitySemantics.SupportsProfile(EventResultConditionKind.CrystalSphereRewards,
            authority.ProfileId, semantics))
            return SearchQueryEvaluation.Unsupported(EventOpportunitySemantics.ProfileUnavailable(EventResultConditionKind.CrystalSphereRewards));

        var evidence = new List<SearchMatchEvidence>();
        SearchQueryEvaluation? unresolved = rows.Any(row => !row.condition.IsValid)
            ? SearchQueryEvaluation.Unsupported("CrystalSphere.InvalidCondition") : null;
        foreach (var branch in rows.Where(row => row.condition.IsValid).GroupBy(row =>
                     (Moves: row.condition.CrystalDivinations!.Value, Relics: row.condition.CrystalRelics.GetValueOrDefault())))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var categories = branch.Aggregate(CrystalSphereSeedCards.None, (v, row) => v | row.condition.CrystalCards.GetValueOrDefault());
            var rarities = branch.Where(row => row.condition.RelicRarity.HasValue)
                .Select(row => row.condition.RelicRarity!.Value).Distinct().Order().ToArray();
            // Separate conjunctive rows do not demand extra copies of an identical goal.
            // Repetition inside each authored list does; use maximum multiplicity.
            var cards = branch.SelectMany(row => (row.condition.CrystalTargetCards ?? []).GroupBy(key => key)
                    .Select(g => (Key: g.Key, Count: g.Count())))
                .GroupBy(item => item.Key).SelectMany(g => Enumerable.Repeat(g.Key, g.Max(item => item.Count)))
                .OrderBy(key => key.Serialized, StringComparer.Ordinal).ToImmutableArray();
            var targets = CrystalSphereSeedTargets.WithRelicRarities(categories, rarities);
            CrystalSphereSeedResult projection;
            if (cards.Length == 0)
                projection = CrystalSphereSeedSearch.Evaluate(rootHash, authority.PlayerSlotIndex, semantics, branch.Key.Moves,
                    targets, cancellationToken);
            else
            {
                var pool = CrystalSphereSearchSemantics.Pool(authority.EffectAuthority, authority.PlayersCount, branch.Key.Relics,
                    authority.Character.CharacterKey);
                if (pool is null)
                {
                    unresolved ??= SearchQueryEvaluation.Unknown("CrystalSphere.CardPoolUnavailable:" + branch.Key.Relics);
                    continue;
                }
                // Known identities outside the chosen conditional pool cannot be obtained.
                if (cards.Any(key => !pool.Any(card => card.CardKey == key)))
                    return SearchQueryEvaluation.NoMatch("CrystalSphere.TargetOutsidePremisePool");
                if (cards.Any(key => pool.Any(card => card.CardKey == key &&
                        card.Rarity is not (EffectCardRarity.Uncommon or EffectCardRarity.Rare))))
                {
                    unresolved ??= SearchQueryEvaluation.Unsupported("CrystalSphere.NamedTargetsRequireUncommonOrRare");
                    continue;
                }
                projection = CrystalSphereSeedSearch.EvaluateCards(rootHash, authority.PlayerSlotIndex, semantics,
                    branch.Key.Moves, pool, cards, cancellationToken, targets,
                    branch.Key.Relics.HasFlag(CrystalSphereSearchRelics.Driftwood));
            }
            if (projection.Verdict == CrystalSphereSeedVerdict.NoMatch)
                return SearchQueryEvaluation.NoMatch(projection.Reason ?? "CrystalSphere.TargetsRejected");
            if (projection.Verdict == CrystalSphereSeedVerdict.Unknown)
            {
                unresolved ??= SearchQueryEvaluation.Unknown(projection.Reason ?? "CrystalSphere.TargetsUnresolved");
                continue;
            }
            var witness = projection.Witness!;
            string path = string.Join(">", witness.Steps.Select(step => $"{step.X},{step.Y},{step.Tool}")) +
                ";rerolls=" + string.Join(">", witness.CardRerolls.Select(r => $"{r.ItemIndex},{r.SubscriptionIndex}")) +
                ";takes=" + string.Join(">", witness.CardTakes.Select(t => $"{t.ItemIndex},{t.SubscriptionIndex},{t.Card.Serialized}"));
            foreach (var row in branch)
                evidence.Add(new SearchMatchEvidence("EventResultConditionMatched", row.condition.TargetKey,
                    $"crystal-sphere:{branch.Key.Moves}:premises={branch.Key.Relics}:cards={string.Join(',', cards)}:tiers={categories}:rolled-rarity={string.Join(',', rarities)}",
                    EvidenceCode: new(EventOpportunitySemantics.EvidenceCode), OutcomeId: path, ProfileId: authority.ProfileId,
                    StreamDomain: "EventLocal", Authority: SourceAuthority.OfficialRuntimeExact,
                    AuthorityFingerprint: authority.EffectSnapshotFingerprint, ConditionId: "event-result-" + row.index));
        }
        return unresolved ?? SearchQueryEvaluation.Match(evidence);
    }
}
