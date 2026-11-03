using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Immutable C-owned input. R supplies its audited initial-pool compiler, not
// execution state; every root/route below owns its RNG and bounded local bag.
internal sealed class CombatRewardObservedOpening
{
    internal const string Revision = ".ObservedOpening.20261102.v1";
    private const int Prefix = 4, SeenCapacity = 6;
    private readonly RelicFamilyPool? _pool;
    private readonly int[] _entries;
    private readonly Beta110FastRewardRelicCapability[] _caps, _top;
    private readonly bool[] _invalidatesNiche, _topInvalidatesNiche;
    private readonly int[] _topIdentity, _advances;
    private readonly int _circlet;
    internal byte[] BonesIds { get; }
    internal bool DynamicBones { get; }
    internal string Issue { get; }
    internal bool Available => Issue.Length == 0;
    internal bool HasUnresolvedEffects { get; }
    internal int BagCapacity { get; }
    internal int ShuffleDraws => _pool is null ? 0 : _pool.BucketLengths.Sum(n => Math.Max(0, n - 1));
    internal uint[] GpuData { get; }

    internal static bool Needed(Beta110CombatRewardOpeningConsumptionProjection opening) =>
        opening.ReplayBonesOffer && opening.OrderedRelicIds.Length < 2 ||
        opening.OrderedRelicIds.Any(IsCapsule);

    internal CombatRewardObservedOpening(SearchModel request, NeowAuthoredUpgradeContinuation? upgrades)
    {
        var opening = Beta110CombatRewardOpeningConsumptionProjector.Project(request.CompiledSearch);
        DynamicBones = opening.ReplayBonesOffer && opening.OrderedRelicIds.Length < 2;
        BonesIds = (request.Authority.EffectAuthority?.BonesEligibleRelics ?? [])
            .Where(k => k != BaseGameModelKeys.Relics.NeowsBones)
            .Select(k => Beta110FastRelicCatalog.TryGetId(k, out byte id) ? id : (byte)255).ToArray();
        bool capsules = opening.OrderedRelicIds.Any(IsCapsule) || DynamicBones && BonesIds.Any(IsCapsule);
        var guaranteed = ExplicitOpeningRelicFacts.EnumerateGuaranteed(request.CompiledSearch.NormalizedQuery).ToHashSet();
        var identities = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        var caps = new List<Beta110FastRewardRelicCapability>();
        var invalidates = new List<bool>();
        bool party = request.Authority.PlayersCount > 1;
        int Identity(ModelKey key)
        {
            if (identities.TryGetValue(key, out int known)) return known;
            int index = identities.Count; identities.Add(key, index);
            var capability = Beta110FastEffectCatalogCompiler.BuildRewardRelicCapability(request.ProfileId, key, true);
            bool niche = false;
            if (party)
                capability = capability with { ContinuationSupported = true,
                    InfluenceFlags = capability.InfluenceFlags & ~Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation };
            else if (VanillaRelicRewardEffects.TryGet(request.ProfileId, key, out var facts))
                niche = (facts.OnObtainCapabilities & RelicOnObtainRewardEffects.ConsumesNicheRng) != 0 &&
                    !(guaranteed.Contains(key) && key == BaseGameModelKeys.OrdinaryRelics.Whetstone ||
                      guaranteed.Contains(key) && key == BaseGameModelKeys.OrdinaryRelics.WarPaint);
            caps.Add(capability); invalidates.Add(niche);
            return index;
        }
        var dense = new Dictionary<ModelKey, ushort>(ModelKeyComparer.Instance);
        string issue = "";
        RelicFamilyPool? pool = null;
        if (capsules && !RelicFamilyPlanCompiler.TryCompilePool(request, out pool, out dense, out _, out issue))
            issue = "ObservedOpeningBag:" + issue;
        _pool = pool;
        if (pool is not null)
        {
            var keys = new ModelKey[dense.Count];
            foreach (var pair in dense) keys[pair.Value] = pair.Key;
            _entries = pool.DenseRelicIds.Select(id => Identity(keys[id])).ToArray();
        }
        else _entries = [];
        _circlet = Identity(BaseGameModelKeys.OrdinaryRelics.Circlet);
        _top = new Beta110FastRewardRelicCapability[30];
        _topIdentity = new int[30]; _topInvalidatesNiche = new bool[30];
        for (byte id = 0; id < 30; id++)
        {
            var key = Beta110FastRelicCatalog.KeyOf(id);
            _topIdentity[id] = Identity(key);
            var top = Beta110FastEffectCatalogCompiler.BuildRewardRelicCapability(request.ProfileId, key, false);
            if (id is 2 or 3 or 10 or 14 or 15 or 16 or 17 or 22 or 28)
                top = top with { ContinuationSupported = true,
                    InfluenceFlags = top.InfluenceFlags & ~Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation };
            _top[id] = top;
            _topInvalidatesNiche[id] = id is not (14 or 19) &&
                VanillaRelicRewardEffects.TryGet(request.ProfileId, key, out var facts) &&
                (facts.OnObtainCapabilities & RelicOnObtainRewardEffects.ConsumesNicheRng) != 0;
        }
        _caps = caps.ToArray(); _invalidatesNiche = invalidates.ToArray();
        _advances = new int[31 * 31];
        for (byte current = 0; current < 30; current++)
        for (int previous = 0; previous < 31; previous++)
            _advances[current * 31 + previous] = upgrades?.Advance(current, previous == 30 ? (byte)255 : (byte)previous) ?? 0;
        BagCapacity = pool is null ? 0 : Enumerable.Range(0, pool.BucketCount)
            .Where(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] <= 3)
            .Select(i => pool.BucketLengths[i]).DefaultIfEmpty(0).Max();
        Issue = issue;
        HasUnresolvedEffects = pool is not null && Enumerable.Range(0, pool.BucketCount)
            .Where(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] <= 3)
            .SelectMany(i => _entries.Skip(pool.BucketOffsets[i]).Take(pool.BucketLengths[i]))
            .Any(i => !_caps[i].Exact) || DynamicBones && BonesIds.Any(id => id >= 30 || !_top[id].Exact);
        GpuData = Pack();
    }

    private static bool IsCapsule(byte id) => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule;

    private static uint PackCapability(Beta110FastRewardRelicCapability c, bool niche) =>
        (c.ContinuationSupported ? 1u : 0u) | (c.InfluenceSupported ? 2u : 0u) | (niche ? 4u : 0u) |
        ((uint)c.InfluenceFlags << 8) | ((uint)c.AdditionalCardRewardCount << 24);

    private uint[] Pack()
    {
        int buckets = _pool?.BucketCount ?? 0;
        int table = 16, entries = table + buckets * 4, cap = entries + _entries.Length;
        int top = cap + _caps.Length * 2, advance = top + 30 * 3;
        var values = new uint[advance + _advances.Length];
        values[0] = 1; values[1] = DynamicBones ? 1u : 0u; values[2] = (uint)buckets;
        values[3] = (uint)table; values[5] = (uint)cap; values[6] = (uint)top;
        values[7] = (uint)advance; values[8] = (uint)BagCapacity; values[9] = (uint)_circlet;
        for (int i = 0; i < buckets; i++)
        {
            values[table + i * 4] = _pool!.BucketScopes[i];
            values[table + i * 4 + 1] = _pool.BucketKinds[i];
            values[table + i * 4 + 2] = (uint)(entries + _pool.BucketOffsets[i]);
            values[table + i * 4 + 3] = (uint)_pool.BucketLengths[i];
        }
        for (int i = 0; i < _entries.Length; i++) values[entries + i] = (uint)_entries[i];
        for (int i = 0; i < _caps.Length; i++)
        { values[cap + i * 2] = PackCapability(_caps[i], _invalidatesNiche[i]); values[cap + i * 2 + 1] = (uint)_caps[i].FixedGoldAmount; }
        for (int i = 0; i < 30; i++)
        { values[top + i * 3] = PackCapability(_top[i], _topInvalidatesNiche[i]); values[top + i * 3 + 1] = (uint)_top[i].FixedGoldAmount; values[top + i * 3 + 2] = (uint)_topIdentity[i]; }
        for (int i = 0; i < _advances.Length; i++) values[advance + i] = unchecked((uint)_advances[i]);
        return values;
    }

    internal Beta110OpeningRewardState Replay(ulong root, int route, CombatRewardReplay owner,
        int playerSlot, int ascension, bool defect, int? precedingNicheDraws, out UpFrontRngCheckpoint? nicheState)
    {
        var rewards = new Beta110FastRng(unchecked(root + (ulong)playerSlot + XxHash64.Hash("rewards"u8, 0)));
        var niche = new Beta110FastRng(unchecked(root + XxHash64.Hash("niche"u8, 0)));
        bool nicheKnown = precedingNicheDraws.HasValue;
        for (int i = 0; i < precedingNicheDraws.GetValueOrDefault(); i++) _ = niche.NextDouble();
        byte first = owner.Plan.OpeningConsumption.OrderedRelicIds.FirstOrDefault((byte)255);
        byte second = owner.Plan.OpeningConsumption.OrderedRelicIds.Skip(1).FirstOrDefault((byte)255);
        int count = owner.Plan.OpeningConsumption.OrderedRelicIds.Length;
        if (owner.Plan.OpeningConsumption.ReplayBonesOffer)
        {
            if (DynamicBones)
            {
                Span<byte> bones = stackalloc byte[BonesIds.Length]; BonesIds.CopyTo(bones);
                rewards.UnstableShuffle(bones); first = bones[0]; second = bones[1]; count = 2;
            }
            else rewards.ConsumeUnstableShuffle(BonesIds.Length);
        }
        var state = new Beta110OpeningRewardState { PotionOdds = .4f, CardRarityOffset = -.05f,
            RewardsContinuationAuthorityExact = true, RewardInfluenceAuthorityExact = true };
        Span<int> prefixes = stackalloc int[12], lengths = stackalloc int[3], consumed = stackalloc int[3], seen = stackalloc int[SeenCapacity];
        consumed.Clear(); int seenCount = 0; bool bagReady = false;
        byte previous = 255;
        for (int index = 0; index < count; index++)
        {
            int at = route == 0 ? index : count - 1 - index;
            byte relic = at == 0 ? first : second;
            if (relic >= 30) { state.RewardInfluenceAuthorityExact = false; break; }
            if (!Apply(_topIdentity[relic], _top[relic], _topInvalidatesNiche[relic], seen, ref seenCount, ref nicheKnown, ref state)) break;
            if (IsCapsule(relic))
            {
                if (!bagReady) { InitializeBag(root, prefixes, lengths); bagReady = true; }
                for (int draw = 0; draw < (relic == Beta110FastRelicCatalog.LargeCapsule ? 2 : 1); draw++)
                {
                    float roll = rewards.NextFloat(); int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
                    while (lane < 3 && consumed[lane] >= lengths[lane]) lane++;
                    int identity = lane < 3 ? prefixes[lane * Prefix + consumed[lane]++] : _circlet;
                    if (!Apply(identity, _caps[identity], _invalidatesNiche[identity], seen, ref seenCount, ref nicheKnown, ref state)) break;
                }
                if (!state.ExactProjectionAuthority) break;
            }
            else
            {
                bool unlisted = DynamicBones && !owner.Plan.OpeningConsumption.OrderedRelicIds.Contains(relic);
                if (unlisted && !HasPoolFor(relic, owner.Catalog, playerSlot >= 0 && owner.IsMultiplayer))
                { state.RewardsContinuationAuthorityExact = false; break; }
                if (relic == Beta110FastRelicCatalog.Kaleidoscope && !nicheKnown && !owner.KaleidoscopeCountOnly)
                { state.RewardsContinuationAuthorityExact = false; break; }
                if (!CombatRewardOpeningReplay.TryReplayQueryLiteralRelicRewardsConsumption(relic, owner.Catalog,
                    ascension, defect, ref rewards, ref niche, false, owner.IsMultiplayer, nicheKnown))
                    throw new InvalidOperationException("C.CombatReward.OpeningConsumptionFailed:" + relic);
            }
            int advance = _advances[relic * 31 + (previous == 255 ? 30 : previous)];
            if (advance < 0) nicheKnown = false;
            else if (nicheKnown) for (int draw = 0; draw < advance; draw++) _ = niche.NextDouble();
            previous = relic;
        }
        if (owner.Plan.OpeningConsumption.ReplayBonesOffer && nicheKnown) _ = niche.NextDouble();
        state.Rewards = rewards; nicheState = nicheKnown ? niche.CaptureCheckpoint() : null;
        return state;
    }

    private static bool Apply(int identity, Beta110FastRewardRelicCapability capability, bool invalidates,
        Span<int> seen, ref int count, ref bool nicheKnown, ref Beta110OpeningRewardState state)
    {
        if (invalidates) nicheKnown = false;
        if (!capability.Exact)
        { state.RewardsContinuationAuthorityExact &= capability.ContinuationSupported; state.RewardInfluenceAuthorityExact &= capability.InfluenceSupported; return false; }
        if (seen[..count].Contains(identity)) return true;
        seen[count++] = identity;
        state.InfluenceFlags |= capability.InfluenceFlags;
        state.AdditionalCardRewardCount = checked((byte)(state.AdditionalCardRewardCount + capability.AdditionalCardRewardCount));
        state.FixedGoldAmount = checked((short)(state.FixedGoldAmount + capability.FixedGoldAmount));
        return true;
    }

    private void InitializeBag(ulong root, Span<int> prefixes, Span<int> lengths)
    {
        var pool = _pool ?? throw new InvalidOperationException("C.ObservedOpeningBagMissing");
        var rng = new Beta110FastRng(unchecked(root + XxHash64.Hash("up_front"u8, 0)));
        prefixes.Fill(-1); lengths.Clear();
        Span<int> permutation = stackalloc int[Math.Max(1, BagCapacity)];
        for (int bucket = 0; bucket < pool.BucketCount; bucket++)
        {
            int length = pool.BucketLengths[bucket];
            if (pool.BucketScopes[bucket] == 0 || pool.BucketKinds[bucket] == 4)
            { rng.ConsumeUnstableShuffle(length); continue; }
            int lane = pool.BucketKinds[bucket] - 1;
            _entries.AsSpan(pool.BucketOffsets[bucket], length).CopyTo(permutation);
            rng.UnstableShuffle(permutation[..length]); lengths[lane] = length;
            permutation[..Math.Min(Prefix, length)].CopyTo(prefixes.Slice(lane * Prefix));
        }
    }

    private static bool HasPoolFor(byte source, Beta110FastEffectCatalog c, bool party) => source switch
    {
        10 => c.CharacterRewardPool.Rare.Length > 0,
        2 => c.CharacterRewardPool.Rare.Length > 0,
        15 => c.ColorlessRewardPool.TotalCount > 0,
        16 => c.CharacterRewardPool.TotalCount > 0 && c.PotionPool.TotalCount > 0,
        17 when party => c.MultiplayerRewardPool.TotalCount > 0,
        14 => c.OtherCharacterPools.Length >= 3 && c.OtherCharacterPools.All(p => p.TotalCount > 0),
        22 => c.CharacterRewardPool.Common.Length > 0 && c.CharacterRewardPool.Uncommon.Length > 0,
        _ => true
    };
}
