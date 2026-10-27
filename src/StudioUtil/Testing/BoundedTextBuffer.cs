using System;
using System.Collections.Generic;
using System.Text;

namespace StudioUtil.Testing;

internal sealed class BoundedTextBuffer
{
    private readonly LinkedList<string> _chunks = new();
    private readonly StringBuilder _tail = new();
    private readonly int _capacity;
    private string? _snapshot = "";
    public BoundedTextBuffer(int capacity = 1_000_000) => _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public int Length { get; private set; }
    public long Version { get; private set; }
    public long Generation { get; private set; }
    public long Removed { get; private set; }
    public void Append(string text)
    {
        if (text.Length == 0) return;
        _snapshot = null;
        Version++;
        if (text.Length >= _capacity)
        {
            Removed += Length + text.Length - _capacity;
            _chunks.Clear();
            _tail.Clear();
            _chunks.AddLast(text.Substring(text.Length - _capacity));
            Length = _capacity;
            return;
        }
        int position = 0;
        while (position < text.Length)
        {
            int count = Math.Min(4096 - _tail.Length, text.Length - position);
            _tail.Append(text, position, count);
            position += count;
            if (_tail.Length == 4096) { _chunks.AddLast(_tail.ToString()); _tail.Clear(); }
        }
        Length += text.Length;
        int target = Length > _capacity ? _capacity * 3 / 4 : _capacity;
        while (Length > target)
        {
            var first = _chunks.First;
            int remove = Math.Min(first?.Value.Length ?? _tail.Length, Length - target);
            if (first == null) _tail.Remove(0, remove);
            else if (remove == first.Value.Length) _chunks.RemoveFirst();
            else first.Value = first.Value.Substring(remove);
            Length -= remove;
            Removed += remove;
        }
    }
    public void Clear()
    {
        _chunks.Clear(); _tail.Clear(); Length = 0; Removed = 0; _snapshot = ""; Version++; Generation++;
    }
    public bool EndsWith(string suffix)
    {
        if (suffix.Length > Length) return false;
        int remaining = suffix.Length;
        int tailCount = Math.Min(remaining, _tail.Length);
        for (int i = 1; i <= tailCount; i++) if (_tail[_tail.Length - i] != suffix[remaining - i]) return false;
        remaining -= tailCount;
        for (var chunk = _chunks.Last; chunk != null && remaining > 0; chunk = chunk.Previous)
        {
            int count = Math.Min(remaining, chunk.Value.Length);
            if (string.CompareOrdinal(chunk.Value, chunk.Value.Length - count, suffix, remaining - count, count) != 0) return false;
            remaining -= count;
        }
        return remaining == 0;
    }
    public string GetText(int start = 0)
    {
        if (start < 0 || start > Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (start == 0 && _snapshot != null) return _snapshot;
        var text = new StringBuilder(Length - start);
        int skip = start;
        foreach (string chunk in _chunks)
        {
            if (skip >= chunk.Length) { skip -= chunk.Length; continue; }
            text.Append(chunk, skip, chunk.Length - skip); skip = 0;
        }
        for (int i = skip; i < _tail.Length; i++) text.Append(_tail[i]);
        string result = text.ToString();
        if (start == 0) _snapshot = result;
        return result;
    }
    public override string ToString() => GetText();
}
