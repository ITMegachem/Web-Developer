using System;
using System.Globalization;

namespace Mgt.Lit.WebFront.Helpers;

public static class DashboardNumberFormatter
{
    public static string FormatCompact(decimal value)
    {
        var absoluteValue = Math.Abs(value);

        if (absoluteValue >= 1_000_000_000m)
            return FormatScaled(value / 1_000_000_000m, "B");

        if (absoluteValue >= 1_000_000m)
            return FormatScaled(value / 1_000_000m, "M");

        if (absoluteValue >= 1_000m)
            return FormatScaled(value / 1_000m, "K");

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatScaled(decimal value, string suffix)
        => value.ToString("0.##", CultureInfo.InvariantCulture) + suffix;
}
