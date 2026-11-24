using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorChoiceIntChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static void Run()
    {
        MutuallyExclusiveUpgradePopulations();
        SkipAndIndependentSources();
        ClampAndExactWitnesses();
        SourceContractsAndDetachedValues();
        ManyChoicesWithoutCombinationExpansion();
        Console.WriteLine("PREDICTOR_CHOICE_INT_PASS correlated populations; skip; independent choices; exact clamp witnesses; schema conflicts; 2048-choice affine composition");
    }

    private static PredictorChoiceInt Choice(string id, params long[] values) =>
        PredictorChoiceInt.Alternatives(id, "Attack|Skill", values);

    private static void MutuallyExclusiveUpgradePopulations()
    {
        var attacks = 6 + Choice("reward:1", 1, 0);
        var skills = 4 + Choice("reward:1", 0, 1);
        var advance = attacks.SubtractAndClamp(1) + skills.SubtractAndClamp(1);
        Check(advance.KnownValue == 9 && advance.Bounds == new PredictorChoiceIntBounds(9, 9),
            "Mutually exclusive Attack/Skill picks must yield exactly nine shuffle draws, never independent maxima");
        Check(advance.Evaluate(new Dictionary<string, int>()) == 9 && advance.Sources.Length == 1,
            "Cancelled numeric uncertainty must retain its source contract without requiring a witness assignment");
        var reordered = skills.SubtractAndClamp(1) + attacks.SubtractAndClamp(1);
        Check(advance.Equals(reordered) && advance.GetHashCode() == reordered.GetHashCode(),
            "Equivalent composition order must preserve canonical equality");
        Check((attacks - attacks).KnownValue == 0 && (3 * attacks - attacks * 3).KnownValue == 0,
            "Subtraction and constant scaling must cancel the same source");
    }

    private static void SkipAndIndependentSources()
    {
        var attacks = 6 + PredictorChoiceInt.Alternatives("reward:skip", "Attack|Skill|Skip", 1, 0, 0);
        var skills = 4 + PredictorChoiceInt.Alternatives("reward:skip", "Attack|Skill|Skip", 0, 1, 0);
        var advance = attacks.SubtractAndClamp(1) + skills.SubtractAndClamp(1);
        Check(advance.KnownValue == null && advance.Bounds == new PredictorChoiceIntBounds(8, 9),
            "A skip makes total advancement eight or nine, never ten");
        for (int option = 0; option < 3; option++)
            Check(advance.Evaluate(new Dictionary<string, int> { ["reward:skip"] = option }) == (option == 2 ? 8 : 9),
                "Both populations must use the same original reward alternative");

        var independent = (6 + Choice("reward:a", 1, 0)).SubtractAndClamp(1) +
            (4 + Choice("reward:b", 0, 1)).SubtractAndClamp(1);
        Check(independent.Bounds == new PredictorChoiceIntBounds(8, 10) && independent.KnownValue == null,
            "Distinct original rewards must remain independent, even with identical alternative contracts");
        Check(independent.Evaluate(new Dictionary<string, int> { ["reward:a"] = 0, ["reward:b"] = 1 }) == 10,
            "The two independent rewards may legitimately both increase their own population");
    }

    private static void ClampAndExactWitnesses()
    {
        Check(PredictorChoiceInt.Constant(-3).ClampNonNegative().KnownValue == 0 &&
            PredictorChoiceInt.Constant(0).SubtractAndClamp(1).KnownValue == 0,
            "Negative and zero populations must clamp without invented draws");
        var boundary = PredictorChoiceInt.Alternatives("boundary", "Negative|Zero|Positive", -2, 0, 3).ClampNonNegative();
        var complement = PredictorChoiceInt.Alternatives("boundary", "Negative|Zero|Positive", 3, 3, 0);
        Check((boundary + complement).KnownValue == 3,
            "A single-source clamp must retain alternative correlation and permit later cancellation");
        Check((-2 * boundary).Bounds == new PredictorChoiceIntBounds(-6, 0),
            "Negative scaling must reverse the proof bounds");

        var x = Choice("clamp:a", 0, 1) + Choice("clamp:b", 0, 1) - 1;
        var absolute = x.ClampNonNegative() + (0 - x).ClampNonNegative();
        Check(!absolute.IsAffine && absolute.KnownValue == null &&
            absolute.Bounds == new PredictorChoiceIntBounds(0, 2),
            "A correlated multi-source clamp may have loose bounds; those endpoints are not an exact solution");
        for (int a = 0; a < 2; a++)
            for (int b = 0; b < 2; b++)
                Check(absolute.Evaluate(new Dictionary<string, int> { ["clamp:a"] = a, ["clamp:b"] = b }) == Math.Abs(a + b - 1),
                    "The retained nonlinear expression must evaluate exact witnesses, including unattainable bound endpoint two");
        var reorderedX = Choice("clamp:b", 0, 1) + Choice("clamp:a", 0, 1) - 1;
        Check((x.ClampNonNegative() - reorderedX.ClampNonNegative()).KnownValue == 0,
            "Structurally equivalent nonlinear atoms must cancel without cross-source enumeration");
        Check(x.SubtractAndClamp(x).KnownValue == 0,
            "Subtract-before-clamp must preserve same-source cancellation");
    }

    private static void SourceContractsAndDetachedValues()
    {
        var values = new long[] { 1, 0 };
        var original = Choice("contract", values);
        values[0] = 100;
        Check(original.Evaluate(new Dictionary<string, int> { ["contract"] = 0 }) == 1,
            "Alternative vectors must be detached from mutable caller arrays");
        Throws<ArgumentException>(() => original + PredictorChoiceInt.Alternatives("contract", "Skill|Attack", 0, 1),
            "Different ordered alternative meaning must not silently merge");
        Throws<ArgumentException>(() => original + PredictorChoiceInt.Alternatives("contract", "Attack|Skill", 0, 1, 0),
            "Different option counts must not silently merge");
        Throws<ArgumentException>(() => (original - original) + PredictorChoiceInt.Alternatives("contract", "Other", 0, 0),
            "Numeric cancellation must not erase conflicting source schemas");
        Throws<ArgumentException>(() => original.Scale(0) + PredictorChoiceInt.Alternatives("contract", "Other", 0, 0),
            "Scaling to zero must retain source schemas");
        Throws<ArgumentException>(() => original.Evaluate(new Dictionary<string, int>()),
            "An unresolved source must require its witness assignment");
        Throws<ArgumentOutOfRangeException>(() => original.Evaluate(new Dictionary<string, int> { ["contract"] = 2 }),
            "An out-of-contract alternative must fail explicitly");
        Throws<ArgumentException>(() => PredictorChoiceInt.Alternatives("empty", "None", []),
            "A source must contain at least one alternative");
        Throws<OverflowException>(() => PredictorChoiceInt.Constant(long.MaxValue) + 1,
            "Arithmetic overflow must fail rather than weaken the integer expression");
    }

    private static void ManyChoicesWithoutCombinationExpansion()
    {
        const int count = 2048;
        var sum = PredictorChoiceInt.Constant(0);
        var onePerChoice = PredictorChoiceInt.Constant(0);
        var assignment = new Dictionary<string, int>();
        long expected = 0;
        for (int i = 0; i < count; i++)
        {
            string id = "reward:" + i;
            sum += PredictorChoiceInt.Alternatives(id, "A|B|C|D", 0, 1, 2, 3);
            onePerChoice += PredictorChoiceInt.Alternatives(id, "A|B|C|D", 1, 1, 0, 0) +
                PredictorChoiceInt.Alternatives(id, "A|B|C|D", 0, 0, 1, 1);
            assignment[id] = i % 4;
            expected += i % 4;
        }
        Check(sum.IsAffine && sum.Sources.Length == count && sum.KnownValue == null &&
            sum.Bounds == new PredictorChoiceIntBounds(0, 3 * count) && sum.Evaluate(assignment) == expected,
            "Thousands of four-way choices must compose as source vectors without enumerating 4^N combinations");
        Check(onePerChoice.KnownValue == count && onePerChoice.Sources.Length == count &&
            onePerChoice.Evaluate(new Dictionary<string, int>()) == count,
            "Per-choice correlated cancellation must remain constant across thousands of independent rewards");
    }

    private static void Throws<T>(Func<object> operation, string message) where T : Exception
    {
        try { _ = operation(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }
}
