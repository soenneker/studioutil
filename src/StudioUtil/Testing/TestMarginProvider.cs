using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace StudioUtil.Testing;

[Export(typeof(IWpfTextViewMarginProvider))]
[Name(TestMargin.MarginName)]
[ContentType("CSharp")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[MarginContainer(PredefinedMarginNames.Left)]
[Order(Before = PredefinedMarginNames.LineNumber)]
internal sealed class TestMarginProvider : IWpfTextViewMarginProvider
{
    [Import]
    internal ITextDocumentFactoryService Documents = null!;

    public IWpfTextViewMargin? CreateMargin(IWpfTextViewHost textViewHost, IWpfTextViewMargin marginContainer) =>
        Documents.TryGetTextDocument(textViewHost.TextView.TextBuffer, out var document)
            ? new TestMargin(textViewHost.TextView, document) : null;
}
