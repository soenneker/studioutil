namespace StudioUtil.Utils.Abstract;

#nullable enable

/// <summary>
/// Defines the variables util contract.
/// </summary>
public interface IVariablesUtil
{
    /// <summary>
    /// Sets the value.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    void Set(string key, string value);

    /// <summary>
    /// Gets the value.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The result of the operation.</returns>
    string? Get(string key);
}