using System;
using System.Collections;
using System.Collections.Generic;

namespace ActiveStateMachine.Generators
{
    /// <summary>
    /// A small immutable array wrapper that implements structural equality, so it can be
    /// safely used inside incremental generator models for correct pipeline caching.
    /// </summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
        where T : IEquatable<T>
    {
        private readonly T[]? _items;

        public EquatableArray(T[] items) => _items = items;

        public int Count => _items?.Length ?? 0;

        public T this[int index] => _items![index];

        public bool Equals(EquatableArray<T> other)
        {
            if (Count != other.Count)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(this[i], other[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < Count; i++)
                {
                    hash = (hash * 31) + (this[i]?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            var items = _items ?? Array.Empty<T>();
            foreach (var item in items)
            {
                yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
