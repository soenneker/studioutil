using StudioUtil.Dtos;
using EnvDTE80;
using System.Threading.Tasks;

namespace StudioUtil.Utils.Abstract;

#nullable enable
public interface INewItemFactory
{
    ValueTask<NewItem?> Create(DTE2 dte);
}