namespace StudioUtil.Utils.Abstract;

#nullable enable

public interface IVariablesUtil
{
    void Set(string key, string value);

    string? Get(string key);
}