using System;

namespace StudioUtil.Testing;

internal static class TestDuration
{
    public static string Format(double? milliseconds)
    {
        if (!milliseconds.HasValue || double.IsNaN(milliseconds.Value) || double.IsInfinity(milliseconds.Value)) return "";
        double value = Math.Round(Math.Max(0, milliseconds.Value), 2);
        if (value < 1000) return $"{value:0.##} ms";
        double seconds = Math.Round(value / 1000, 2);
        if (seconds < 60) return $"{seconds:0.##} s";
        double wholeSeconds = Math.Floor(seconds);
        double minutes = Math.Floor(wholeSeconds / 60);
        if (minutes < 60) return $"{minutes:0}m {wholeSeconds % 60:00}s";
        double hours = Math.Floor(minutes / 60);
        if (hours < 24) return $"{hours:0}h {minutes % 60:00}m {wholeSeconds % 60:00}s";
        return $"{Math.Floor(hours / 24):0}d {hours % 24:00}h {minutes % 60:00}m {wholeSeconds % 60:00}s";
    }
}
