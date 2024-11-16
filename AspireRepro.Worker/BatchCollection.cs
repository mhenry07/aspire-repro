using System.Collections;
using Microsoft.Extensions.ObjectPool;

namespace AspireRepro.Worker;

public sealed class BatchCollection
    : IReadOnlyCollection<KeyValuePair<BatchCollection.Key, BatchCollection.Values>>, IResettable
{
    private readonly Dictionary<Key, Values> _groupLookup = [];

    public int Count => _groupLookup.Count;

    public void Add(long value)
    {
        var key = new Key(value);
        if (!_groupLookup.TryGetValue(key, out var group))
        {
            group = new Values();
            _groupLookup.Add(key, group);
        }

        group.Add(value);
    }

    public Dictionary<Key, Values>.Enumerator GetEnumerator()
        => _groupLookup.GetEnumerator();

    IEnumerator<KeyValuePair<Key, Values>> IEnumerable<KeyValuePair<Key, Values>>.GetEnumerator()
        => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool TryReset()
    {
        _groupLookup.Clear();

        return true;
    }

    public readonly record struct Key(long Row) : IEquatable<Key>
    {
        private const long Mod = 1000;

        public readonly bool Equals(Key other) => Row % Mod == other.Row % Mod;
        public override readonly int GetHashCode() => (Row % Mod).GetHashCode();
    }

    public sealed class Values
    {
        private const int DefaultCapacity = 16;
        private long[] _buffer = new long[DefaultCapacity];

        public int Length { get; private set; }

        public void Add(long value)
        {
            if (Length >= _buffer.Length)
            {
                var old = _buffer;
                _buffer = new long[old.Length * 2];
                old.AsSpan().CopyTo(_buffer);
            }

            _buffer[Length++] = value;
        }
    }
}
