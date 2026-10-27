using System;

namespace StudioUtil.Testing;

internal static class OutputDisplayWindow
{
    internal const int MaxCharacters = 32 * 1024;
    internal const int MaxLines = 400;

    internal static int Start(string text)
    {
        int start = Math.Max(0, text.Length - MaxCharacters);
        int lines = 0;
        for (int i = text.Length - 2; i >= start; i--)
            if (text[i] == '\n' && ++lines >= MaxLines) return i + 1;
        return start;
    }
}
