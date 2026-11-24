namespace RolltheSpire2.Search.Predictability;

internal static class SearchPredictabilityMath
{
    public static double ExpectedRootsToFirst(double probability) => probability > 0d ? 1d / probability : double.PositiveInfinity;

    public static double ExpectedRootsToTarget(double probability, int targetMatches) =>
        probability > 0d ? Math.Max(1, targetMatches) / probability : double.PositiveInfinity;

    public static double ProbabilityAtLeastOne(double probability, double roots)
    {
        if (!(roots > 0d)) return 0d;
        if (probability <= 0d) return 0d;
        if (probability >= 1d) return 1d;
        return OneMinusExp(roots * LogMissProbability(probability));
    }

    // Independent-root approximation only. This is never an Exact probability authority.
    public static double ProbabilityAtLeastTarget(double probability, long roots, int targetMatches)
    {
        int target = Math.Max(1, targetMatches);
        if (roots <= 0) return 0d;
        if (target > roots) return 0d;
        if (probability <= 0d) return 0d;
        if (probability >= 1d) return 1d;
        if (double.IsNaN(probability)) return double.NaN;
        if (target == 1) return ProbabilityAtLeastOne(probability, roots);

        // Start at the target boundary and sum away from the mean. In particular,
        // underflow of P[X=0] says nothing about the tail near a large mean.
        bool upper = target > roots * probability;
        long boundary = upper ? target : target - 1;
        double logMass = LogBinomialMass(probability, roots, boundary);
        double term = 1d, sum = 1d, compensation = 0d;
        long index = boundary;
        double miss = 1d - probability;

        // For the upper tail r_i <= k/(k+i+1); for the lower tail
        // r_i <= (k-i)/k. With k <= int.MaxValue, after m=500000 terms
        // the remaining scaled sum is bounded by
        // exp(-m*(m-1)/(2*(k+m))) * k/m < 2.4e-22.
        // This fixed ceiling is independent of roots; no distribution approximation
        // or array proportional to the population is needed. Usually the geometric
        // remainder bound stops much earlier. These bounds exclude roundoff.
        for (int step = 0; step < 500_000; step++)
        {
            if (upper ? index == roots : index == 0) break;
            double ratio = upper
                ? (roots - index) * probability / ((index + 1d) * miss)
                : index * miss / (((roots - index) + 1d) * probability);
            if (ratio < 1d && term * ratio <= 1e-16 * sum * (1d - ratio)) break;
            term *= ratio;
            double adjusted = term - compensation;
            double next = sum + adjusted;
            compensation = (next - sum) - adjusted;
            sum = next;
            index += upper ? 1 : -1;
        }
        double logTail = Math.Min(0d, logMass + Math.Log(sum));
        return upper ? Math.Exp(logTail) : OneMinusExp(logTail);
    }

    // net9's LogP1/ExpM1 do not preserve all small arguments on our supported
    // runtime. Compensate the rounded 1-p explicitly, including 1-p == 1.
    // Also shared by ETA's inverse at-least-one calculation.
    internal static double LogMissProbability(double probability)
    {
        double miss = 1d - probability;
        return miss == 1d ? -probability : Math.Log(miss) * (-probability / (miss - 1d));
    }

    private static double OneMinusExp(double exponent)
    {
        if (Math.Abs(exponent) >= .5d) return 1d - Math.Exp(exponent);
        double term = exponent, sum = exponent;
        // |x| < 1/2: the omitted exponential series is < 2e-23 absolute.
        for (int k = 2; k <= 18; k++)
        {
            term *= exponent / k;
            sum += term;
        }
        return -sum;
    }

    private static double LogBinomialMass(double probability, long roots, long successes)
    {
        if (successes == 0) return roots * LogMissProbability(probability);
        if (successes == roots) return roots * Math.Log(probability);
        long failures = roots - successes;
        // The deviance form avoids subtracting enormous log-factorials. Compute
        // the two opposite differences once: (n-k)-n*(1-p) loses low bits for
        // large n and small p, even when the desired tail is near its mean.
        double difference = Math.FusedMultiplyAdd(-(double)roots, probability, successes);
        return StirlingError(roots) - StirlingError(successes) - StirlingError(failures)
            - Deviance(successes, roots * probability, difference)
            - Deviance(failures, roots * (1d - probability), -difference)
            - .5d * Math.Log(2d * Math.PI * successes * (failures / (double)roots));
    }

    // x*log(x/mean) + mean-x, using its convergent series near the mean.
    private static double Deviance(double x, double mean, double difference)
    {
        if (Math.Abs(difference) >= .1d * (x + mean))
        {
            double ratio = x / mean;
            return x * (double.IsFinite(ratio) ? Math.Log(ratio) : Math.Log(x) - Math.Log(mean)) - difference;
        }
        double v = difference / (x + mean);
        double sum = difference * v;
        double term = 2d * x * v;
        // |v| < .1 and x <= long.MaxValue: the omitted series is < 5.4e-18.
        for (int j = 1; j <= 16; j++)
        {
            term *= v * v;
            sum += term / (2 * j + 1);
        }
        return sum;
    }

    // log(n!) - (n+1/2)*log(n) + n - log(2*pi)/2 for positive integers.
    // At n>=16 the first omitted Stirling term is < 1.1e-16.
    private static double StirlingError(long n)
    {
        if (n < 16) return n switch
        {
            1 => 0.081061466795327261d, 2 => 0.041340695955409297d,
            3 => 0.027677925684998338d, 4 => 0.020790672103765093d,
            5 => 0.016644691189821193d, 6 => 0.013876128823070748d,
            7 => 0.01189670994589177d, 8 => 0.010411265261972096d,
            9 => 0.0092554621827127329d, 10 => 0.0083305634333628708d,
            11 => 0.0075736754879518406d, 12 => 0.0069428401072095299d,
            13 => 0.0064089941880042071d, 14 => 0.0059513701127588475d,
            _ => 0.0055547335519628011d
        };
        double inverse = 1d / n;
        double square = inverse * inverse;
        return inverse * (1d / 12d + square * (-1d / 360d + square * (1d / 1260d
            + square * (-1d / 1680d + square / 1188d))));
    }
}
