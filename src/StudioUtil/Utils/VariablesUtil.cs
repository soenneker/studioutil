using System.Collections.Generic;
using StudioUtil.Utils.Abstract;

namespace StudioUtil.Utils;

#nullable enable

public class VariablesUtil : IVariablesUtil
{
    private readonly Dictionary<string, string> _variables;

    public VariablesUtil()
    {
        _variables = new Dictionary<string, string>();
    }

    public void Set(string key, string value)
    {
        if (_variables.ContainsKey(key))
        {
            _variables[key] = value;
            return;
        }

        _variables.Add(key, value);
    }

    public string? Get(string key)
    {
       var success = _variables.TryGetValue(key, out var value);

       return success ? value : null;
    }
}