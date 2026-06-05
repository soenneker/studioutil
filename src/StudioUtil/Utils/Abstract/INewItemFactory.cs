using StudioUtil.Dtos;
using EnvDTE80;
using System.Threading.Tasks;

namespace StudioUtil.Utils.Abstract;

#nullable enable
/// <summary>
/// Defines the new item factory contract.
/// </summary>
public interface INewItemFactory
{
    /// <summary>
    /// Executes the create operation.
    /// </summary>
    /// <param name="dte">The dte.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<NewItem?> Create(DTE2 dte);
}