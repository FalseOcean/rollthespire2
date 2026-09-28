namespace RolltheSpire2.Core.Prediction.Maps;

internal sealed partial class BoundedActMap
{
    private readonly ulong[] _finalSignatures = new ulong[Slots];

    // Research-private full-Final specialization of MapBasisScalars. PostProcess
    // moves columns, not raw rows or identities. Ignore dead ends exactly as the
    // independent graph evaluator does; neither Start nor Boss contributes a room.
    internal MapOutcomeSignature EvaluateFinalScalars()
    {
        if (!_complete) throw new InvalidOperationException("Final scalars require a complete replay.");
        UInt128 reachesBoss = Bit(_boss);
        _finalSignatures[_boss] = 0;
        Span<int> minima = stackalloc int[6];
        Span<int> maxima = stackalloc int[5];
        for (int node = _boss - 1; node >= Start; node--)
        {
            if (!Active(node)) continue;
            minima.Fill(16); maxima.Clear(); bool any = false;
            for (int lane = 0; lane < _degree[node]; lane++)
            {
                int child = _children[node * 7 + lane];
                if (child / 7 <= node / 7) throw new InvalidOperationException("Expected increasing-row Final DAG.");
                if ((reachesBoss & Bit(child)) == 0) continue;
                any = true;
                var next = new MapOutcomeSignature(_finalSignatures[child]);
                for (int d = 0; d < 6; d++) minima[d] = Math.Min(minima[d], next.Guarantee(d));
                for (int d = 0; d < 5; d++) maxima[d] = Math.Max(maxima[d], next.Maximum(d));
            }
            if (!any) continue;
            if (node != Start)
            {
                int dimension = MapBasisScalars.Index((MapPointType)_type[node]);
                if (dimension >= 0) { minima[dimension]++; maxima[dimension]++; }
                minima[5] = _type[node] == (byte)MapPointType.Monster ? minima[5] + 1 : 0;
            }
            _finalSignatures[node] = MapOutcomeSignature.Create(minima, maxima).Bits;
            reachesBoss |= Bit(node);
        }
        if ((reachesBoss & Bit(Start)) == 0) throw new InvalidOperationException("No legal Start to Boss path.");
        return new(_finalSignatures[Start]);
    }
}
