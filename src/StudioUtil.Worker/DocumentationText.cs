using System.Text.RegularExpressions;

namespace StudioUtil.Testing;

internal static class DocumentationText
{
    public static string Create(string declaration)
    {
        var match = Regex.Match(declaration, @"(:|,) (I[a-zA-Z]+)");
        return match.Success ? $"///<inheritdoc cref=\"{match.Groups[2].Value}\"/>" : "///<inheritdoc/>";
    }
}
