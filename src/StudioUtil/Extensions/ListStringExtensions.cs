using System.Collections.Generic;

namespace StudioUtil.Extensions;

/// <summary>
/// Represents the list string extension.
/// </summary>
public static class ListStringExtension
{
    /// <summary>
    /// Executes the replace operation.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="replacements">The replacements.</param>
    public static void Replace(this List<string> list, Dictionary<string, string> replacements)
    {
        for (var i = 0; i < list.Count; i++)
        {
            foreach (var kvp in replacements)
            {
                list[i] = list[i].Replace(kvp.Key, kvp.Value);
            }
        }
    }
}