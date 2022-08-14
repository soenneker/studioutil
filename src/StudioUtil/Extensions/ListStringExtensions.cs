using System.Collections.Generic;

namespace StudioUtil.Extensions;

public static class ListStringExtension
{
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