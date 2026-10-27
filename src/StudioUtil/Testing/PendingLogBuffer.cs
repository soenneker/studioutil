namespace StudioUtil.Testing;

internal sealed class PendingLogBuffer
{
    private readonly object _gate = new();
    private readonly BoundedTextBuffer _text = new(256 * 1024);
    public bool IsEmpty { get { lock (_gate) return _text.Length == 0; } }
    public void Append(string text) { lock (_gate) _text.Append(text); }
    public string Drain()
    {
        lock (_gate)
        {
            string text = (_text.Removed > 0 ? "[Earlier queued output omitted]\n" : "") + _text.GetText();
            _text.Clear();
            return text;
        }
    }
}
