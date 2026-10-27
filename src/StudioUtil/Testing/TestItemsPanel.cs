using System.Windows.Controls;

namespace StudioUtil.Testing;

public sealed class TestItemsPanel : VirtualizingStackPanel
{
    internal void ShowIndex(int index) => BringIndexIntoView(index);
}
