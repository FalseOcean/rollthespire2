using System.Collections.Immutable;
using System.Numerics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorOperationCombination(ImmutableArray<long> Targets, PredictorOperationResult Result);
internal sealed record PredictorOperationCombinationSpace(ImmutableArray<long> Candidates, int Minimum, int Maximum,
    bool Ordered, BigInteger Count);

internal static class PredictorOperationCombinations
{
    internal static PredictorOperationCombinationSpace Describe(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorInput> prefix, bool ordered, CancellationToken token = default)
    {
        var preview = PredictorOperation.Preview(snapshot, prefix, token);
        if (preview.Disposition != PredictorInputResult.Accepted)
            throw new ArgumentException("OperationCombinationPrefixInvalid");
        if (preview.Waiting.Phase is not (PredictorPhase.EventCards or PredictorPhase.ObtainDeckCards or PredictorPhase.TransformCards))
            throw new ArgumentException("OperationCombinationTargetChoiceRequired");
        var selection = preview.Waiting.Selection;
        int minimum = selection.MinimumCardCount, maximum = selection.MaximumCardCount;
        if (minimum < 0 || maximum < minimum || maximum > selection.CandidateCardIds.Length)
            throw new InvalidDataException("OperationCombinationBoundsInvalid");
        BigInteger total = 0;
        for (int count = minimum; count <= maximum; count++)
        {
            BigInteger ways = 1;
            for (int i = 0; i < count; i++) ways *= selection.CandidateCardIds.Length - i;
            if (!ordered) for (int i = 2; i <= count; i++) ways /= i;
            total += ways;
        }
        return new(selection.CandidateCardIds, minimum, maximum, ordered, total);
    }

    // Lazy enumeration: callers can choose an appropriate display batch without
    // truncating the declared space or allocating all permutations up front.
    internal static IEnumerable<PredictorOperationCombination> Enumerate(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorInput> prefix, bool ordered, CancellationToken token = default)
    {
        var space = Describe(snapshot, prefix, ordered, token);
        var phase = PredictorOperation.Preview(snapshot, prefix, token).Waiting.Phase;
        for (int count = space.Minimum; count <= space.Maximum; count++)
            foreach (var ids in Targets(space.Candidates, count, ordered, token))
            {
                token.ThrowIfCancellationRequested();
                PredictorInput choice = phase switch
                {
                    PredictorPhase.EventCards => new SelectEventCards(ids),
                    PredictorPhase.ObtainDeckCards => new ChooseObtainDeckCards(ids),
                    PredictorPhase.TransformCards when ids.Length == 1 => new SelectTransformationCard(ids[0]),
                    _ => throw new InvalidOperationException("OperationCombinationPhaseChanged")
                };
                yield return new(ids, PredictorOperation.Preview(snapshot, prefix.Add(choice), token));
            }
    }

    private static IEnumerable<ImmutableArray<long>> Targets(ImmutableArray<long> candidates, int count,
        bool ordered, CancellationToken token)
    {
        var chosen = new long[count];
        var used = new bool[candidates.Length];
        return Walk(0, 0);
        IEnumerable<ImmutableArray<long>> Walk(int depth, int start)
        {
            token.ThrowIfCancellationRequested();
            if (depth == count) { yield return chosen.ToImmutableArray(); yield break; }
            for (int i = ordered ? 0 : start; i < candidates.Length; i++)
            {
                if (used[i]) continue;
                chosen[depth] = candidates[i]; used[i] = true;
                foreach (var result in Walk(depth + 1, i + 1)) yield return result;
                used[i] = false;
            }
        }
    }
}
