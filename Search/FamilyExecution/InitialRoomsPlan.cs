using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

// A filter for the explicitly uncommitted initial-room baseline. An offered
// Neow relic is never an acquisition; committed openings remain Exact duties.
internal sealed partial class InitialRoomsPlan
{
    internal const string Revision = "InitialRooms.Baseline.TrackedSharedFront.20261020.v1";
    internal const int Capacity = 1 << 20;
    internal GpuNumericalVariant NumericalVariant { get; private init; }
    internal string ActualRevision => Revision + (NumericalVariant == GpuNumericalVariant.NativeFloat64 ? "" : ".IntegerRne.20261025.v1");
    internal GpuNumericRequirements NumericRequirements => GpuIntegerNumerics.Requirements(NumericalVariant);
    private static readonly ulong UnknownHash = XxHash64.Hash("unknown_map_point"u8, 0);
    private static readonly ulong TreasureHash = XxHash64.Hash("treasure_room_relics"u8, 0);
    private static readonly ulong UpFrontHash = XxHash64.Hash("up_front"u8, 0);
    private readonly ExactSearchExecutionRequest _request;
    private readonly TreasureRelicSearchCondition? _treasure;
    private readonly int[] _lengths, _bucketLanes, _effectiveLengths, _targetLanes, _targetPositions, _removedPositions;
    private readonly bool[] _circlet;
    private readonly int _lastBucket;
    internal int UnknownPrefixCount { get; }
    internal bool CoversUnknownPrefix => UnknownPrefixCount > 0;
    internal bool CoversTreasure => _treasure is not null;
    internal string TreasureRemainder { get; }
    internal uint[][] Buffers { get; }

    private InitialRoomsPlan(ExactSearchExecutionRequest request, int unknown, TreasureRelicSearchCondition? treasure,
        int[] lengths, int[] lanes, int[] effectiveLengths, int[] targetLanes, int[] targetPositions,
        int[] removedPositions, bool[] circlet, int lastBucket, string remainder)
    {
        _request = request; UnknownPrefixCount = unknown; _treasure = treasure;
        _lengths = lengths; _bucketLanes = lanes; _effectiveLengths = effectiveLengths;
        _targetLanes = targetLanes; _targetPositions = targetPositions; _removedPositions = removedPositions;
        _circlet = circlet; _lastBucket = lastBucket; TreasureRemainder = remainder;
        var meta = new uint[32 + lengths.Length * 2];
        meta[0] = (uint)unknown; meta[1] = CoversTreasure ? 1u : 0u;
        meta[2] = (uint)(treasure?.StartOrdinal ?? 1); meta[3] = (uint)(treasure?.Count ?? 0);
        meta[4] = treasure?.OrderMode == TreasureRelicOrderMode.Unordered ? 1u : 0u;
        for (int slot = 0; slot < (treasure?.Count ?? 0); slot++)
            if (treasure!.Slots[slot].HasValue) meta[5] |= 1u << slot;
        meta[6] = (uint)lengths.Length; meta[7] = unchecked((uint)lastBucket);
        void Hash(int offset, ulong value) { meta[offset] = (uint)value; meta[offset + 1] = (uint)(value >> 32); }
        Hash(8, UnknownHash); Hash(10, TreasureHash); Hash(12, UpFrontHash);
        for (int i = 0; i < 3; i++) { meta[14 + i] = (uint)effectiveLengths[i]; meta[21 + i] = unchecked((uint)removedPositions[i]); }
        for (int i = 0; i < 2; i++)
        {
            meta[17 + i] = (uint)targetLanes[i]; meta[19 + i] = unchecked((uint)targetPositions[i]);
            if (circlet[i]) meta[25] |= 1u << i;
        }
        for (int i = 0; i < lengths.Length; i++) { meta[32 + i * 2] = (uint)lengths[i]; meta[33 + i * 2] = (uint)lanes[i]; }
        Buffers = [meta, []];
    }

    internal static InitialRoomsPlan? TryCompileTreasure(ExactSearchExecutionRequest request,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64) => TryCompile(request, true, false, numericalVariant);
    internal static InitialRoomsPlan? TryCompileUnknown(ExactSearchExecutionRequest request,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64) => TryCompile(request, false, true, numericalVariant);

    internal static InitialRoomsPlan? TryCompile(ExactSearchExecutionRequest request, bool includeTreasure = true, bool includeUnknown = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        if (request.CompiledSearch.Context.Party is not null)
        {
            if (!includeUnknown || !PartyInitialRooms.TryOrdinaryUnknown(request, out int prefix, out _)) return null;
            return new(request, prefix, null, [], [], new int[3], [4, 4], [-1, -1], [-1, -1, -1], new bool[2], -1,
                PartyInitialRooms.HasTreasure(request) ? "InitialRooms.SinglePlayerOnly" : "") { NumericalVariant = numericalVariant };
        }
        if (request.ProfileId != RuntimeProfileId.Beta111 || request.Authority.PlayersCount != 1 ||
            request.CompiledSearch.Context.Party is not null ||
            ProductionQueryValidator.HasOpeningInitialRoomConditions(request.Evaluation)) return null;
        int unknown = includeUnknown ? request.Evaluation.FirstActUnknownEventPrefixCount : 0;
        var treasure = includeTreasure && request.Evaluation.TreasureRelics is { IsEmpty: false } condition ? condition : null;
        if (unknown == 0 && treasure is null) return null;
        int[] lengths = [], lanes = [], effective = new int[3], targetLanes = [4, 4], targets = [-1, -1], removed = [-1, -1, -1];
        bool[] circlet = new bool[2]; int last = -1; string remainder = "";
        if (treasure is not null)
        {
            if (!TryTreasure(request, treasure, out lengths, out lanes, effective, targetLanes, targets, removed, circlet, out last, out remainder))
                treasure = null;
        }
        return unknown == 0 && treasure is null ? null : new(request, unknown, treasure,
            lengths, lanes, effective, targetLanes, targets, removed, circlet, last, remainder) { NumericalVariant = numericalVariant };
    }

    private static bool TryTreasure(ExactSearchExecutionRequest request, TreasureRelicSearchCondition condition,
        out int[] lengths, out int[] lanes, int[] effective, int[] targetLanes, int[] targets, int[] removed,
        bool[] circlet, out int last, out string issue)
    {
        lengths = []; lanes = []; last = -1;
        var world = request.Authority.WorldAuthority;
        if (world is null || !SourceAuthorityRules.SupportsExactIdentity(world.SourceAuthority) ||
            world.Completeness != SnapshotCompleteness.Complete || world.Beta109Generation is not { NoUnknownHooksOrModifiers: true })
        { issue = "InitialRooms.SharedBagExactAuthorityUnavailable"; return false; }
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out issue)) return false;
        var shared = Enumerable.Range(0, pool.BucketCount).Where(i => pool.BucketScopes[i] == 0).ToArray();
        lengths = shared.Select(i => pool.BucketLengths[i]).ToArray();
        lanes = shared.Select(i => pool.BucketKinds[i] is >= 1 and <= 3 ? pool.BucketKinds[i] - 1 : 3).ToArray();
        for (int lane = 0; lane < 3; lane++)
            if (lanes.Count(value => value == lane) != 1)
            { issue = "InitialRooms.SharedLaneMissingOrAmbiguous"; return false; }
        var seen = new HashSet<ushort>();
        dense.TryGetValue(new("RELIC", "MASSIVE_SCROLL"), out ushort massive);
        bool hasMassive = dense.ContainsKey(new("RELIC", "MASSIVE_SCROLL"));
        for (int index = 0; index < shared.Length; index++)
        {
            int bucket = shared[index], lane = lanes[index];
            if (lane == 3) continue;
            effective[lane] = lengths[index];
            for (int position = 0; position < lengths[index]; position++)
            {
                ushort id = pool.DenseRelicIds[pool.BucketOffsets[bucket] + position];
                // One tracked position represents one identity. Keep duplicate
                // source identities on Exact rather than silently collapsing them.
                if (!seen.Add(id)) { issue = "InitialRooms.SharedIdentityNotUnique"; return false; }
                if (hasMassive && id == massive) { removed[lane] = position; effective[lane]--; }
                for (int slot = 0; slot < condition.Count; slot++)
                    if (condition.Slots[slot] is { } target && target.Entry != "MASSIVE_SCROLL" &&
                        dense.TryGetValue(target, out ushort targetId) && id == targetId)
                    { targetLanes[slot] = lane; targets[slot] = position; last = Math.Max(last, index); }
            }
        }
        for (int slot = 0; slot < condition.Count; slot++) circlet[slot] = condition.Slots[slot]?.Entry == "CIRCLET";
        issue = ""; return true;
    }

    internal bool Matches(ulong root)
    {
        if (!MatchesUnknown(root, UnknownPrefixCount)) return false;
        if (_treasure is null) return true;
        var rarity = new Beta110FastRng(unchecked(root + TreasureHash));
        Span<int> encounterLanes = stackalloc int[2], ranks = stackalloc int[2], taken = stackalloc int[3];
        taken.Clear();
        int end = _treasure.StartOrdinal + _treasure.Count - 1;
        for (int i = 0; i < end; i++)
        {
            float roll = rarity.NextFloat(); int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
            while (lane < 3 && taken[lane] == _effectiveLengths[lane]) lane++;
            encounterLanes[i] = lane; ranks[i] = lane == 3 ? 0 : taken[lane]++;
        }
        // Rarity is independent of the shared shuffle. Impossible lanes need no UpFront replay.
        for (int slot = 0; slot < _treasure.Count; slot++)
        {
            if (_treasure.Slots[slot] is null) continue;
            bool possible = false;
            for (int i = _treasure.StartOrdinal - 1; i < end; i++)
                if ((_treasure.OrderMode == TreasureRelicOrderMode.Unordered || i == _treasure.StartOrdinal - 1 + slot) &&
                    (encounterLanes[i] == _targetLanes[slot] || encounterLanes[i] == 3 && _circlet[slot])) possible = true;
            if (!possible) return false;
        }
        Span<int> positions = stackalloc int[2]; _targetPositions.AsSpan().CopyTo(positions);
        var rng = new Beta110FastRng(unchecked(root + UpFrontHash));
        for (int bucket = 0; bucket <= _lastBucket; bucket++)
        {
            int lane = _bucketLanes[bucket], removal = lane < 3 ? _removedPositions[lane] : -1;
            bool tracks = _targetLanes.Contains(lane) && lane < 3;
            if (!tracks) { rng.ConsumeUnstableShuffle(_lengths[bucket]); continue; }
            for (int count = _lengths[bucket]; count > 1; count--)
            {
                int selected = rng.NextInt(count), tail = count - 1;
                for (int slot = 0; slot < 2; slot++)
                    if (_targetLanes[slot] == lane) positions[slot] = SwapPosition(positions[slot], selected, tail);
                removal = SwapPosition(removal, selected, tail);
            }
            for (int slot = 0; slot < 2; slot++)
                if (_targetLanes[slot] == lane && removal >= 0 && removal < positions[slot]) positions[slot]--;
        }
        int used = 0;
        for (int slot = 0; slot < _treasure.Count; slot++)
        {
            if (_treasure.Slots[slot] is null) continue;
            bool found = false;
            for (int i = _treasure.StartOrdinal - 1; i < end; i++)
                if ((used & (1 << i)) == 0 &&
                    (_treasure.OrderMode == TreasureRelicOrderMode.Unordered || i == _treasure.StartOrdinal - 1 + slot) &&
                    (encounterLanes[i] == 3 ? _circlet[slot] :
                        encounterLanes[i] == _targetLanes[slot] && ranks[i] == positions[slot]))
                { used |= 1 << i; found = true; break; }
            if (!found) return false;
        }
        return true;
    }
    private static int SwapPosition(int position, int selected, int tail) => position == selected ? tail : position == tail ? selected : position;

    internal static bool MatchesUnknown(ulong root, int count)
    {
        if (count == 0) return true;
        var rng = new Beta110FastRng(unchecked(root + UnknownHash));
        float monster = .1f, treasure = .02f, shop = .03f;
        for (int i = 0; i < count; i++)
        {
            float total = monster + treasure; total += shop;
            if (rng.NextFloat() <= total) return false;
            monster += .1f; treasure += .02f; shop += .03f;
        }
        return true;
    }

    internal bool Reference(ulong root)
    {
        if (CoversUnknownPrefix && !OrdinaryUnknownRoomPredictor.ForAct1Filter(root, UnknownPrefixCount, false)
                .Steps.All(s => s.Room == UnknownRoomCategory.Event)) return false;
        if (_treasure is null) return true;
        var profile = RuntimeProfileRegistry.Select(_request.Detection);
        var bags = RelicSequencePredictor.PredictFromRootHash(profile, root, "root-hash:" + root, _request.Authority.WorldAuthority);
        var prediction = TreasureRelicPredictor.Predict(profile, root, bags, [], _treasure.StartOrdinal + _treasure.Count - 1);
        if (prediction.Status != SeedDomainEvaluationStatus.Evaluated || prediction.Precision != PredictionPrecision.Exact)
            throw new InvalidDataException("InitialRooms.CompiledReferenceAuthorityChanged:" + prediction.IssueCode);
        int used = 0;
        for (int slot = 0; slot < _treasure.Count; slot++)
        {
            if (_treasure.Slots[slot] is not { } target) continue;
            var found = prediction.Entries.FirstOrDefault(e => e.RelicKey == target && e.EncounterOrdinal >= _treasure.StartOrdinal &&
                e.EncounterOrdinal < _treasure.StartOrdinal + _treasure.Count &&
                (_treasure.OrderMode == TreasureRelicOrderMode.Unordered || e.EncounterOrdinal == _treasure.StartOrdinal + slot) &&
                (used & (1 << e.EncounterOrdinal)) == 0);
            if (found is null) return false;
            used |= 1 << found.EncounterOrdinal;
        }
        return true;
    }

    internal string ShaderSource()
    {
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
        string numerical = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..donor.IndexOf("bool evaluate_event_results", StringComparison.Ordinal)];
        string source = FamilyGpuComputeUtility.LoadEmbeddedShader("InitialRoomsFamily.comp.glsl").Replace("/*__RNG_HASH__*/", numerical, StringComparison.Ordinal);
        return NumericalVariant == GpuNumericalVariant.NativeFloat64 ? source : InitialRoomsGpuNumericalSource.Adapt(source);
    }
}
