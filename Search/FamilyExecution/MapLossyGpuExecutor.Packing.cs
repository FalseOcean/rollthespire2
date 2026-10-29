using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class MapLossyGpuExecutor
{
    // Header: count, actCount, propertyCount, drawingCount.
    // Act: needed metric mask, first drawing, drawing count, reserved.
    // Property: local-act bit mask, metric, atLeast, signed threshold bits.
    // Drawing: transition word offset, state count, word count, accept state.
    // All tables are sized from the admitted plan, never truncated to a limit.
    private sealed record PackedPlan(MapLossyActPlan[] Acts, uint[] Config, uint[] Transitions, int WordCount);

    private static PackedPlan Pack(MapLossyPlan plan)
    {
        MapLossyActPlan[] acts = plan.Acts.ToArray();
        if (acts.Length is < 1 or > 3 || acts.Select(a => a.Act).Distinct().Count() != acts.Length ||
            acts.Any(a => a.Act is < 1 or > 3))
            throw new InvalidDataException("MapLossy.InvalidActPlan");
        int drawingCount = acts.Sum(a => a.Drawings.Count);
        var config = new uint[checked(4 + acts.Length * 4 + plan.Properties.Count * 4 + drawingCount * 4)];
        config[1] = (uint)acts.Length; config[2] = (uint)plan.Properties.Count; config[3] = (uint)drawingCount;
        var transitions = new List<uint>();
        int maxWords = 1, drawingIndex = 0;
        for (int a = 0; a < acts.Length; a++)
        {
            var act = acts[a];
            int descriptor = 4 + a * 4;
            foreach (int metric in act.PropertyMetrics)
            {
                if (metric is < 0 or > 8) throw new InvalidDataException("MapLossy.InvalidPropertyMetric");
                config[descriptor] |= 1u << metric;
            }
            config[descriptor + 1] = (uint)drawingIndex; config[descriptor + 2] = (uint)act.Drawings.Count;
            foreach (var drawing in act.Drawings)
            {
                var program = drawing.Program;
                uint[] table = program.ExportTransitions();
                ValidateProgram(program, table);
                int slot = 4 + acts.Length * 4 + plan.Properties.Count * 4 + drawingIndex * 4;
                config[slot] = (uint)transitions.Count; config[slot + 1] = (uint)program.StateCount;
                config[slot + 2] = (uint)program.WordCount; config[slot + 3] = (uint)program.AcceptState;
                transitions.AddRange(table); maxWords = Math.Max(maxWords, program.WordCount); drawingIndex++;
            }
        }
        for (int p = 0; p < plan.Properties.Count; p++)
        {
            var property = plan.Properties[p];
            if (property.Scope is < 0 or > 3 || property.Metric is < 0 or > 8 || !Enum.IsDefined(property.Comparison))
                throw new InvalidDataException("MapLossy.InvalidPropertyClause");
            uint mask = 0;
            for (int a = 0; a < acts.Length; a++)
            {
                if (property.Scope != 0 && property.Scope != acts[a].Act) continue;
                if ((config[4 + a * 4] & (1u << property.Metric)) == 0)
                    throw new InvalidDataException("MapLossy.PropertyMetricNotScheduled");
                mask |= 1u << a;
            }
            if (mask == 0 || property.Scope == 0 && acts.Length != 3)
                throw new InvalidDataException("MapLossy.PropertyActNotScheduled");
            int slot = 4 + acts.Length * 4 + p * 4;
            config[slot] = mask; config[slot + 1] = (uint)property.Metric;
            config[slot + 2] = property.Comparison == StandardMapComparison.AtLeast ? 1u : 0u;
            config[slot + 3] = unchecked((uint)property.Value);
        }
        return new(acts, config, transitions.ToArray(), maxWords);
    }

    private static void ValidateProgram(MapDrawingProgram program, uint[] table)
    {
        if (program.StateCount is < 1 or > MaxStates || program.WordCount != (program.StateCount + 31) / 32 ||
            program.AcceptState < 0 || program.AcceptState >= program.StateCount ||
            table.Length != program.StateCount * RoomTypes || table.Any(next => next >= program.StateCount))
            throw new InvalidDataException("MapLossy.InvalidDrawingProgramShape");
        for (int room = 0; room < RoomTypes; room++)
            if (table[program.AcceptState * RoomTypes + room] != program.AcceptState)
                throw new InvalidDataException("MapLossy.DrawingAcceptanceNotAbsorbing");
    }

    private static byte[] Prepare(SearchBatch batch, ReadOnlySpan<ulong> ordinals, MapLossyActPlan[] acts,
        int ascension, CancellationToken token)
    {
        var words = new ulong[checked(ordinals.Length * acts.Length * 5)]; Span<byte> seed = stackalloc byte[12];
        var contexts = acts.Select(a => StandardMapFamily.Context(a.Act, ascension)).ToArray();
        var offsets = acts.Select(a => Beta111Profile.Instance.DeriveNamedStreamSeed(0, $"act_{a.Act}_map")).ToArray();
        var heights = contexts.Select(c => Beta111StandardMapGenerator.GetNumberOfRooms(c.Act, false)).ToArray();
        for (int i = 0; i < ordinals.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            // Batch-local ABI1 ordinals identify true visible candidates; they
            // are never substituted for root hashes or compacted RNG identities.
            Beta110SeedCodec.WriteOrdinal(batch.GlobalCandidate(ordinals[i]), seed);
            ulong root = XxHash64.Hash(seed, 0);
            for (int a = 0; a < acts.Length; a++)
            {
                var context = contexts[a]; var rng = new BoundedMapRng(unchecked(root + offsets[a]));
                int rest = context.Act switch { ActKind.Overgrowth => rng.Gaussian(7, 6, 7),
                    ActKind.Hive => rng.Gaussian(6, 6, 7), _ => rng.Int(2) + 5 };
                int unknown = rng.Gaussian(12, 10, 14) - (context.ActIndex > 0 ? 1 : 0);
                var s = rng.State; int k = (i * acts.Length + a) * 5;
                words[k] = s.S0; words[k + 1] = s.S1; words[k + 2] = s.S2; words[k + 3] = s.S3;
                words[k + 4] = (ulong)(rest | unknown << 4 | (context.HasSwarmingElites ? 8 : 5) << 8 | heights[a] << 16);
            }
        }
        var bytes = new byte[checked(words.Length * 8)]; Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length); return bytes;
    }
}
