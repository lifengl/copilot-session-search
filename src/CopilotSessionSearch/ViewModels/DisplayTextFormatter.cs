#nullable enable

using System.Globalization;

namespace CopilotSessionSearch.ViewModels;

internal static class DisplayTextFormatter
{
    public static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    public static string FormatSessionSpan(TimeSpan value)
    {
        if (value.TotalDays >= 1)
        {
            return $"{(int)value.TotalDays}d {value.Hours}h";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes}m";
        }

        return $"{Math.Max(0, (int)value.TotalSeconds)}s";
    }

    public static string FormatCount(int value, string singular, string plural)
    {
        return value == 1
            ? $"1 {singular}"
            : $"{value:N0} {plural}";
    }
}
