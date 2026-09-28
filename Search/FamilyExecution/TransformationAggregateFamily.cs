using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>One semantic Coverage atom. All card identities/counters are local;
/// only ordered logical ordinals leave this CPU realization.</summary>
internal sealed partial class TransformationAggregateNumericalPlan
{
    private readonly TransformationAggregateCondition _condition;
    private readonly NeowReplayPlan? _neow;
    private readonly int[] _neowIds = [];
    private readonly List<bool> _rare = [];
    private readonly int[] _targets;
    private readonly (ulong Hash, int Prefix, int[][] Pools)[] _events;
    private readonly bool _closedNeow;
    private static readonly ulong TransformationsHash = XxHash64.Hash("transformations"u8, 0);
    private static readonly ulong NicheHash = XxHash64.Hash("niche"u8, 0);

    internal TransformationAggregateNumericalPlan(ExactSearchExecutionRequest request)
    {
        _condition = request.Evaluation.TransformationAggregate ?? throw new ArgumentException("MissingAggregate");
        var keys = new Dictionary<ModelKey, int>();
        int Key(ModelKey key, bool rare)
        {
            if (keys.TryGetValue(key, out int id))
            {
                if (_rare[id] != rare) throw new InvalidOperationException("TransformationAggregate.InconsistentCardRarity");
                return id;
            }
            id = keys.Count; keys.Add(key, id); _rare.Add(rare); return id;
        }
        if (_condition.UsesNeow)
        {
            ModelKey selected = _condition.Opening switch {
                TransformationOpening.LeafyPoultice => BaseGameModelKeys.Relics.LeafyPoultice,
                TransformationOpening.NewLeaf => BaseGameModelKeys.Relics.NewLeaf, _ => BaseGameModelKeys.Relics.NeowsBones };
            // Donor compilation only: these are not hidden Query/Family obligations.
            var e = ExactSearchEvaluationProjection.Empty with { NeowRoute = new(selected),
                RequiredBonesCombination = _condition.IsBones
                    ? [BaseGameModelKeys.Relics.LeafyPoultice, _condition.BonesCompanion] : [] };
            _neow = NeowReplayPlan.Compile(request with { Evaluation = e }, false);
            var catalog = _neow.Authority.EffectCatalog;
            _neowIds = Enumerable.Repeat(-1, catalog.DenseKeys.Length).ToArray();
            foreach (ushort id in catalog.LeafyStrikeTransformPool.Concat(catalog.LeafyDefendTransformPool).Concat(catalog.NewLeafTransformPool).Distinct())
                _neowIds[id] = Key(catalog.DenseKeys[id], catalog.CardRarityByDenseId[id] == (byte)EffectCardRarity.Rare);
            bool leafy = _condition.Opening != TransformationOpening.NewLeaf, leaf = _condition.Opening is TransformationOpening.NewLeaf or TransformationOpening.BonesLeafyNewLeaf;
            _closedNeow = (!leafy || catalog.LeafyTransformAuthorityExact && catalog.LeafyStrikeTransformPool.Length > 0 && catalog.LeafyDefendTransformPool.Length > 0) &&
                (!leaf || catalog.NewLeafTransformAuthorityExact && catalog.NewLeafTransformPool.Length > 0);
            if (_condition.Opening == TransformationOpening.BonesLeafyNewLeaf)
            {
                var deck = request.Authority.EffectAuthority?.OrderedDeck;
                var strikes = deck?.Where(c => c.IsBasic && c.IsStrike).ToArray() ?? [];
                // Both routes consume distinct Basics. Equality of the surviving
                // source pools is a captured fact, not a name-based assumption.
                _closedNeow &= strikes.Length >= 2 && strikes.All(c => c.CardKey == strikes[0].CardKey && c.PoolId == strikes[0].PoolId);
            }
        }
        if (_condition.Opening == TransformationOpening.BonesLeafyOther)
        {
            // Only certify companions whose immediate projection cannot consume
            // Transformations or remove the two source Basics before Leafy.
            _closedNeow &= _condition.LeafyFirst || _condition.BonesCompanion.Entry is
                "ARCANE_SCROLL" or "HEFTY_TABLET" or "LEAD_PAPERWEIGHT" or "LOST_COFFER" or "KALEIDOSCOPE" or
                "SCROLL_BOXES" or "PHIAL_HOLSTER" or "NEOWS_TALISMAN" or "POMANDER" or
                "BOOMING_CONCH" or "FISHING_ROD" or "GOLDEN_PEARL" or "NEOWS_TORMENT" or
                "LAVA_ROCK" or "NUTRITIOUS_OYSTER" or "STONE_HUMIDIFIER";
        }
        var events = new List<(ulong, int, int[][])>();
        if (_condition.EventScenario is { } scenario)
        {
            int[][] Pool(int count) => Enumerable.Range(0, count).Select(i =>
                scenario.Premises.VanillaLocalDependenciesBound && scenario.Targets[i].OrderedSourceCandidates is { } pool
                    ? pool.Select(c => Key(c.CardKey, c.Rarity == "Rare")).ToArray() : []).ToArray();
            if (_condition.MorphicGrove) events.Add((XxHash64.Hash("MORPHIC_GROVE"u8, 0), 0, Pool(2)));
            if (_condition.AromaOfChaos) events.Add((XxHash64.Hash("AROMA_OF_CHAOS"u8, 0), 0, Pool(1)));
            if (_condition.WhisperingHollow) events.Add((XxHash64.Hash("WHISPERING_HOLLOW"u8, 0), 1, Pool(1)));
            if (_condition.Symbiote) events.Add((XxHash64.Hash("SYMBIOTE"u8, 0), 0, Pool(1)));
            if (_condition.TrialNondescript) events.Add((XxHash64.Hash("TRIAL"u8, 0), 2, Pool(2)));
        }
        _events = events.ToArray();
        _targets = _condition.TargetMultiset.Select(k => keys.TryGetValue(k, out int id) ? id : -2).ToArray();
    }

    internal bool Matches(ulong root)
    {
        Span<int> outputs = stackalloc int[10]; int count = 0;
        if (_neow is { } n)
        {
            if (!NeowFamilyReplay.Observe(root, n).IdentityPass) return false;
            int nc = _condition.Opening == TransformationOpening.BonesLeafyNewLeaf ? 3 : _condition.Opening is TransformationOpening.LeafyPoultice or TransformationOpening.BonesLeafyOther ? 2 : 1;
            if (!_closedNeow) { outputs[..nc].Fill(-1); count = nc; }
            else
            {
                var catalog = n.Authority.EffectCatalog;
                // The closed Leafy/NewLeaf pair has separate streams and equal
                // remaining Basic pools in either order; no route union of cards.
                if (_condition.Opening != TransformationOpening.NewLeaf)
                {
                    var rng = new Beta110FastRng(unchecked(root + (ulong)n.Authority.PlayerSlotIndex + TransformationsHash));
                    Span<ushort> pair = stackalloc ushort[2]; NeowLocalOperators.DrawLeafyTransforms(catalog, ref rng, pair);
                    outputs[count++] = _neowIds[pair[0]]; outputs[count++] = _neowIds[pair[1]];
                }
                if (_condition.Opening is TransformationOpening.NewLeaf or TransformationOpening.BonesLeafyNewLeaf)
                {
                    var rng = new Beta110FastRng(unchecked(root + NicheHash));
                    outputs[count++] = _neowIds[NeowLocalOperators.DrawNewLeafTransform(catalog, ref rng)];
                }
            }
        }
        foreach (var e in _events)
        {
            var rng = new Beta110FastRng(unchecked(root + e.Hash));
            if (e.Prefix == 1) rng.NextInt(19);
            if (e.Prefix == 2 && rng.NextInt(3) != 2) return false;
            foreach (int[] pool in e.Pools)
            {
                int draw = rng.NextInt(pool.Length == 0 ? 1 : pool.Length);
                outputs[count++] = pool.Length == 0 ? -1 : pool[draw];
            }
        }
        if (count != _condition.OpportunityCount) throw new InvalidOperationException("TransformationAggregate.NumericalOutputCount");
        int unknown = 0, rare = 0;
        foreach (int value in outputs[..count]) { if (value == -1) unknown++; else if (_rare[value]) rare++; }
        if (_condition.Predicate == TransformationAggregatePredicate.RareCountAtLeast) return rare + unknown >= _condition.MinimumRareCount;
        Span<bool> used = stackalloc bool[10]; used.Clear(); int missing = 0;
        foreach (int target in _targets)
        {
            int found = -1;
            for (int i = 0; i < count; i++) if (!used[i] && outputs[i] == target) { found = i; break; }
            if (found < 0) missing++; else used[found] = true;
        }
        return missing <= unknown;
    }
}
