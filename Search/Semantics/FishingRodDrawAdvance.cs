using System.Numerics;
using RolltheSpire2.Core.Rng;
using State = (ulong S0, ulong S1, ulong S2, ulong S3);

namespace RolltheSpire2.Search.Semantics;

internal static class FishingRodDrawAdvance
{
    private const int StepLimit = 1024;
    private static readonly Lazy<State[][]> Powers = new(BuildPowers);

    internal static Xoshiro256StarStar Advance(Xoshiro256StarStar stream, int calls)
    {
        if (calls <= StepLimit)
        {
            stream.Advance(calls);
            return stream;
        }

        State state = stream.State;
        for (int remaining = calls; remaining != 0; remaining &= remaining - 1)
            state = Apply(Powers.Value[BitOperations.TrailingZeroCount(remaining)], state);
        return Xoshiro256StarStar.FromState(state.S0, state.S1, state.S2, state.S3, checked(stream.CallCount + calls));
    }

    private static State[][] BuildPowers()
    {
        // Xoshiro's state transition is linear over bits. Build it from the
        // existing RNG, then square it for jumps of 2, 4, 8, ... draws.
        var powers = new State[31][];
        var basis = new State[256];
        for (int column = 0; column < basis.Length; column++)
        {
            ulong bit = 1UL << (column % 64);
            State state = (column / 64) switch
            {
                0 => (bit, 0, 0, 0),
                1 => (0, bit, 0, 0),
                2 => (0, 0, bit, 0),
                _ => (0, 0, 0, bit)
            };
            var stream = Xoshiro256StarStar.FromState(state.S0, state.S1, state.S2, state.S3);
            stream.Advance(1);
            basis[column] = stream.State;
        }
        powers[0] = basis;
        for (int power = 1; power < powers.Length; power++)
        {
            State[] previous = powers[power - 1];
            var squared = new State[previous.Length];
            for (int column = 0; column < squared.Length; column++)
                squared[column] = Apply(previous, previous[column]);
            powers[power] = squared;
        }
        return powers;
    }

    private static State Apply(State[] columns, State input)
    {
        State result = default;
        XorColumns(columns, input.S0, 0, ref result);
        XorColumns(columns, input.S1, 64, ref result);
        XorColumns(columns, input.S2, 128, ref result);
        XorColumns(columns, input.S3, 192, ref result);
        return result;
    }

    private static void XorColumns(State[] columns, ulong bits, int offset, ref State output)
    {
        for (; bits != 0; bits &= bits - 1)
        {
            State column = columns[offset + BitOperations.TrailingZeroCount(bits)];
            output = (output.S0 ^ column.S0, output.S1 ^ column.S1, output.S2 ^ column.S2, output.S3 ^ column.S3);
        }
    }
}
