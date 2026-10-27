using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StudioUtil.Testing;

internal static class OutputSourceLinks
{
    // Match source locations, never arbitrary URLs or commands from test output.
    private static readonly Regex Locations = new(
        @"(?:\bin |^\s*)(?<path>(?:[A-Za-z]:[\\/]|\\\\|/|\.?\.?[\\/])[^\r\n]+?)(?::line (?<line>[0-9]+)|\((?<line>[0-9]+)(?:,(?<column>[0-9]+))?\))",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<OutputSourceLink> Parse(string text)
    {
        if (text.IndexOf(":line ", StringComparison.Ordinal) < 0 && text.IndexOf('(') < 0)
            return Array.Empty<OutputSourceLink>();
        var links = new List<OutputSourceLink>();
        try
        {
            foreach (Match match in Locations.Matches(text))
            {
                if (!int.TryParse(match.Groups["line"].Value, out int line) || line < 1) continue;
                int column = 1;
                if (match.Groups["column"].Success && (!int.TryParse(match.Groups["column"].Value, out column) || column < 1)) continue;
                var path = match.Groups["path"];
                links.Add(new OutputSourceLink { Start = path.Index, Length = match.Index + match.Length - path.Index,
                    Path = path.Value, Line = line, Column = column });
            }
        }
        catch (RegexMatchTimeoutException) { } // Unusual output must remain readable even if it cannot be linked.
        return links;
    }
}
