using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

#nullable enable

namespace ParallelDungeon.Rogue.Serialization
{
    public class MergedArrayPoolDictionary<TKey, TValue> :
        ICollection<KeyValuePair<TKey, TValue>>,
        IDictionary<TKey, TValue>,
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
        IReadOnlyDictionary<TKey, TValue>,
        IDisposable
        where TKey : notnull
    {

        private struct Entry
        {
            public TKey Key;
            public TValue Value;
            public int Next;

            public override string ToString()
            {
                return $"{Key}: {Value} (-> {Next - 1})";
            }
        }

        private int[] m_Buckets;
        private Entry[] m_Entries;
        private int m_Count;
        private int m_FreeIndex;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBucketIndex(int hashCode, int bucketLength)
        {
            // note: should use prime mod?
            return hashCode & (bucketLength - 1);
        }

        private (int hashCode, int bucketIndex, int entryIndex) GetEntryIndex(TKey key)
        {
            if (key == null)
                ThrowArgumentNull(nameof(key));

            int hashCode = key.GetHashCode();
            int bucketIndex = GetBucketIndex(hashCode, m_Entries.Length);

            int entryIndex;
            int safetyCount;
            for (entryIndex = m_Buckets[bucketIndex] - 1, safetyCount = 0;
                (uint)entryIndex < (uint)m_Entries.Length && safetyCount <= m_Entries.Length;
                entryIndex = m_Entries[entryIndex].Next - 1, safetyCount++)
            {
                if (EqualityComparer<TKey>.Default.Equals(m_Entries[entryIndex].Key, key))
                {
                    return (hashCode, bucketIndex, entryIndex);
                }
            }

            if (safetyCount > m_Entries.Length)
                ThrowInvalidOperation("detect infinite loop");

            return (hashCode, bucketIndex, -1);
        }

        private bool TryAdd(TKey key, TValue value, bool overwrite)
        {
            if (key == null)
                ThrowArgumentNull(nameof(key));

            (int hashCode, int bucketIndex, int entryIndex) = GetEntryIndex(key);
            if (entryIndex != -1)
            {
                if (overwrite)
                {
                    m_Entries[entryIndex].Value = value;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (m_Count == m_Entries.Length)
            {
                Resize(m_Count << 1);
                bucketIndex = GetBucketIndex(hashCode, m_Entries.Length);
            }


            int newEntryIndex;
            if (m_FreeIndex == 0)
            {
                newEntryIndex = m_Count;
            }
            else
            {
                newEntryIndex = m_FreeIndex - 1;
                m_FreeIndex = m_Entries[m_FreeIndex - 1].Next;
            }

            if (m_Buckets[bucketIndex] == 0)
            {
                m_Buckets[bucketIndex] = newEntryIndex + 1;
            }
            else
            {
                int safetyCount;
                for (entryIndex = m_Buckets[bucketIndex] - 1, safetyCount = 0;
                    (uint)entryIndex < (uint)m_Entries.Length && safetyCount < m_Entries.Length;
                    entryIndex = m_Entries[entryIndex].Next - 1, safetyCount++)
                {
                    if (m_Entries[entryIndex].Next == 0)
                    {
                        m_Entries[entryIndex].Next = newEntryIndex + 1;
                        break;
                    }
                }
            }

            m_Entries[newEntryIndex].Key = key;
            m_Entries[newEntryIndex].Value = value;
            m_Entries[newEntryIndex].Next = 0;
            m_Count++;
            return true;
        }


        private void Resize(int size)
        {
            var newBuckets = ArrayPool<int>.Shared.Rent(size);
            var newEntries = ArrayPool<Entry>.Shared.Rent(size);
            newBuckets.AsSpan().Clear();
            newEntries.AsSpan().Clear();

            var oldBuckets = m_Buckets;
            var oldEntries = m_Entries;
            m_Buckets = newBuckets;
            m_Entries = newEntries;
            m_Count = 0;
            m_FreeIndex = -1;

            for (int i = 0; i < oldEntries.Length; i++)
            {
                TryAdd(oldEntries[i].Key, oldEntries[i].Value, false);
            }

            ArrayPool<int>.Shared.Return(oldBuckets);
            ArrayPool<Entry>.Shared.Return(oldEntries, true);
        }

        private bool TryRemove(TKey key)
        {
            (_, int bucketIndex, int entryIndex) = GetEntryIndex(key);

            if (entryIndex < 0)
            {
                return false;
            }

            if (m_Buckets[bucketIndex] - 1 == entryIndex)
            {
                m_Buckets[bucketIndex] = m_Entries[entryIndex].Next;
            }
            else
            {
                int prev = m_Buckets[bucketIndex] - 1;
                int next = m_Entries[prev].Next - 1;
                int safetyCount;
                for (safetyCount = 0; safetyCount < m_Entries.Length; safetyCount++)
                {
                    if (next == entryIndex)
                    {
                        m_Entries[prev].Next = m_Entries[next].Next;
                        break;
                    }
                    (prev, next) = (next, m_Entries[next].Next - 1);
                }

                if (safetyCount >= m_Entries.Length)
                    ThrowInvalidOperation("detect infinite loop");
            }

            m_Entries[entryIndex].Key = default!;
            m_Entries[entryIndex].Value = default!;
            m_Entries[entryIndex].Next = m_FreeIndex;
            m_FreeIndex = entryIndex + 1;
            m_Count--;
            return true;
        }



        [DoesNotReturn]
        private static void ThrowArgumentNull(string name) => throw new ArgumentNullException(name);

        [DoesNotReturn]
        private static void ThrowInvalidOperation(string message) => throw new InvalidOperationException(message);

        [DoesNotReturn]
        private static void ThrowKeyDuplicated() => throw new ArgumentException("given key is already exists");








        public MergedArrayPoolDictionary() : this(16) { }
        public MergedArrayPoolDictionary(int capacity)
        {
            m_Buckets = ArrayPool<int>.Shared.Rent(capacity);
            m_Entries = ArrayPool<Entry>.Shared.Rent(capacity);
            m_Buckets.AsSpan().Clear();
            m_Entries.AsSpan().Clear();
            m_Count = 0;
            m_FreeIndex = 0;
        }
        public MergedArrayPoolDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary.Count)
        {
            if (dictionary is MergedArrayPoolDictionary<TKey, TValue> cloneDictionary)
            {
                cloneDictionary.m_Buckets.CopyTo(m_Buckets.AsSpan());
                cloneDictionary.m_Entries.CopyTo(m_Entries.AsSpan());
                m_Count = cloneDictionary.m_Count;
                m_FreeIndex = cloneDictionary.m_FreeIndex;
                return;
            }

            foreach (var pair in dictionary)
            {
                if (!TryAdd(pair.Key, pair.Value, false))
                    ThrowKeyDuplicated();
            }
        }
        public MergedArrayPoolDictionary(IEnumerable<KeyValuePair<TKey, TValue>> dictionary) : this(dictionary.Count())
        {
            // TODO: optimization.
            foreach (var pair in dictionary)
            {
                if (!TryAdd(pair.Key, pair.Value, false))
                    ThrowKeyDuplicated();
            }
        }


        public TValue this[TKey key]
        {
            get => GetEntryIndex(key).entryIndex is int index && index != -1 ? m_Entries[index].Value : throw new KeyNotFoundException();
            set => TryAdd(key, value, true);
        }

        public int Count => m_Count;

        public bool IsReadOnly => false;


        public readonly struct KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
        {
            private readonly MergedArrayPoolDictionary<TKey, TValue> m_Parent;

            internal KeyCollection(MergedArrayPoolDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
            }

            public int Count => m_Parent.Count;

            public bool IsReadOnly => true;

            public void Add(TKey item) => throw new NotSupportedException();

            public void Clear() => throw new NotSupportedException();

            public bool Contains(TKey item) => m_Parent.ContainsKey(item);

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                throw new NotImplementedException();
            }

            public IEnumerator<TKey> GetEnumerator()
            {
                throw new NotImplementedException();
            }

            public bool Remove(TKey item) => throw new NotSupportedException();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }


        public readonly struct ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
        {
            private readonly MergedArrayPoolDictionary<TKey, TValue> m_Parent;

            internal ValueCollection(MergedArrayPoolDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
            }

            public int Count => m_Parent.Count;

            public bool IsReadOnly => true;

            public void Add(TValue item) => throw new NotSupportedException();

            public void Clear() => throw new NotSupportedException();

            public bool Contains(TValue item) => m_Parent.ContainsValue(item);

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                throw new NotImplementedException();
            }

            public IEnumerator<TValue> GetEnumerator()
            {
                throw new NotImplementedException();
            }

            public bool Remove(TValue item) => throw new NotSupportedException();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }


        public KeyCollection Keys => new KeyCollection(this);
        public ValueCollection Values => new ValueCollection(this);

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;

        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!TryAdd(item.Key, item.Value, false))
                ThrowKeyDuplicated();
        }

        public void Add(TKey key, TValue value)
        {
            if (!TryAdd(key, value, false))
                ThrowKeyDuplicated();
        }

        public void Clear()
        {
            m_Count = 0;
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return GetEntryIndex(item.Key).entryIndex is int index && index >= 0 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Entries[index].Value);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key).entryIndex is int index && index >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array.Length < arrayIndex + m_Count)
                ThrowInvalidOperation("array is too short");

            for (int i = 0; i < m_Count; i++)
            {
                array[arrayIndex + i] = new KeyValuePair<TKey, TValue>(m_Entries[i].Key, m_Entries[i].Value);
            }
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly MergedArrayPoolDictionary<TKey, TValue> parent;
            private int index;

            internal Enumerator(MergedArrayPoolDictionary<TKey, TValue> parent)
            {
                this.parent = parent;
                index = -1;
            }

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(parent.m_Entries[index].Key, parent.m_Entries[index].Value);

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                return ++index < parent.m_Count;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            (_, int bucketIndex, int entryIndex) = GetEntryIndex(item.Key);

            if (entryIndex < 0)
            {
                return false;
            }
            if (!EqualityComparer<TValue>.Default.Equals(m_Entries[entryIndex].Value, item.Value))
            {
                return false;
            }

            return TryRemove(item.Key);
        }

        public bool Remove(TKey key)
        {
            return TryRemove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);
            if (entryIndex >= 0)
            {
                value = m_Entries[entryIndex].Value;
                return true;
            }
            else
            {
                value = default!;
                return false;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return $"{m_Count} items";
        }

        public void Dispose()
        {
            ArrayPool<int>.Shared.Return(m_Buckets);
            ArrayPool<Entry>.Shared.Return(m_Entries, true);

            m_Buckets = null!;
            m_Entries = null!;
            m_Count = 0;
            m_FreeIndex = -1;
        }
    }
}
