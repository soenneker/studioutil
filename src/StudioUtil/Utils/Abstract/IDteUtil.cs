using System.Threading.Tasks;
using EnvDTE80;

namespace StudioUtil.Utils.Abstract;

/// <summary>
/// Defines the dte util contract.
/// </summary>
public interface IDteUtil
{
    /// <summary>
    /// Gets dte.
    /// </summary>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<DTE2> GetDte();
}