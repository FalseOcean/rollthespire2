using System.Globalization;

namespace RolltheSpire2.Presentation.Ui1;

/// <summary>
/// Width-stable player-facing numeric formatting for narrow status surfaces.
/// Full exact values remain available through UI tooltips.
/// </summary>
internal static class CompactNumberFormatter
{
    private static readonly (double Divisor, string Suffix)[] Units =
    {
        (1_000_000_000_000d, "T"),
        (1_000_000_000d, "B"),
        (1_000_000d, "M"),
        (1_000d, "K")
    };

    public static string FormatCount(long value) => Format(value);

    public static string FormatRate(double value) => Format(value) + "/s";

    public static string FormatDuration(double milliseconds)
    {
        if (!(milliseconds >= 0d) || !double.IsFinite(milliseconds))
            return string.Empty;
        if (milliseconds < 1000d)
            return milliseconds.ToString(milliseconds < 10d ? "0.##" : milliseconds < 100d ? "0.#" : "0", CultureInfo.InvariantCulture) + " ms";

        double seconds = milliseconds / 1000d;
        if (seconds < 60d)
            return seconds.ToString(seconds < 10d ? "0.##" : "0.#", CultureInfo.InvariantCulture) + " s";

        double minutes = seconds / 60d;
        if (minutes < 60d)
            return minutes.ToString(minutes < 10d ? "0.##" : "0.#", CultureInfo.InvariantCulture) + " min";

        double hours = minutes / 60d;
        if (hours < 24d)
            return hours.ToString(hours < 10d ? "0.##" : "0.#", CultureInfo.InvariantCulture) + " h";

        double days = hours / 24d;
        return days.ToString(days < 10d ? "0.##" : "0.#", CultureInfo.InvariantCulture) + " d";
    }

    public static string Format(double value)
    {
        double magnitude = Math.Abs(value);
        foreach ((double divisor, string suffix) in Units)
        {
            if (magnitude < divisor)
                continue;

            double scaled = value / divisor;
            string format = Math.Abs(scaled) < 10d ? "0.##" : Math.Abs(scaled) < 100d ? "0.#" : "0";
            return scaled.ToString(format, CultureInfo.InvariantCulture) + suffix;
        }

        return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
    }

    public static string FormatRarity(double probability)
    {
        if (double.IsNaN(probability) || double.IsInfinity(probability) || probability <= 0d)
            return string.Empty;

        double reciprocal = 1d / probability;
        return "1 / " + FormatRarityDenominator(reciprocal);
    }

    private static string FormatRarityDenominator(double value)
    {
        if (value < 1_000d)
            return value.ToString(value < 10d ? "0.##" : value < 100d ? "0.#" : "0", CultureInfo.InvariantCulture);

        return Format(value);
    }
}
