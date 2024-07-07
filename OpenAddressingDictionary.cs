using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ParallelDungeon.Rogue.Serialization
{

    public class OpenAddressingDictionary<TKey, TValue> :
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
            public UsedFlags Used;

            public override string ToString()
            {
                return $"{Used} | {Key}: {Value}";
            }
        }
        private enum UsedFlags : byte
        {
            Empty = 0,
            Used = 1,
            Tombstone = 2,
        }
        private Entry[] m_Entries;
        private int m_Count;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBucketIndex(int hashCode, int bucketLength)
        {
            return hashCode & (bucketLength - 1);
        }

        private (int hashCode, int bucketIndex, int entryIndex) GetEntryIndex(TKey key)
        {
            int hashCode = key.GetHashCode();

            int bucketIndex = GetBucketIndex(hashCode, m_Entries.Length);
            int mask = m_Entries.Length - 1;

            for (int offset = 0; offset < m_Entries.Length; offset++)
            {
                int index = (bucketIndex + offset) & mask;
                if (m_Entries[index].Used == UsedFlags.Used &&
                    EqualityComparer<TKey>.Default.Equals(m_Entries[index].Key, key))
                {
                    return (hashCode, bucketIndex, index);
                }

                if (m_Entries[index].Used == UsedFlags.Empty)
                    break;
            }

            return (hashCode, bucketIndex, -1);
        }

        private bool TryAdd(TKey key, TValue value, bool overwrite)
        {
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

            if (m_Count >= m_Entries.Length * 0.5)
            {
                Resize(m_Entries.Length << 1);
                bucketIndex = GetBucketIndex(hashCode, m_Entries.Length);
            }

            for (int offset = 0; offset < m_Entries.Length; offset++)
            {
                int index = (bucketIndex + offset) & (m_Entries.Length - 1);
                if (m_Entries[index].Used != UsedFlags.Used)
                {
                    m_Entries[index].Key = key;
                    m_Entries[index].Value = value;
                    m_Entries[index].Used = UsedFlags.Used;
                    m_Count++;
                    return true;
                }
            }

            throw new InvalidOperationException("never reach here");
        }

        private void Resize(int size)
        {
            var newEntries = ArrayPool<Entry>.Shared.Rent(size);
            newEntries.AsSpan().Clear();
            // System.Diagnostics.Debug.WriteLine($"-> {size}");

            for (int i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].Used == UsedFlags.Used)
                {
                    int bucketIndex = GetBucketIndex(m_Entries[i].Key.GetHashCode(), newEntries.Length);
                    for (int offset = 0; offset < newEntries.Length; offset++)
                    {
                        int entryIndex = (bucketIndex + offset) & (newEntries.Length - 1);
                        if (newEntries[entryIndex].Used != UsedFlags.Used)
                        {
                            newEntries[entryIndex] = m_Entries[i];
                            break;
                        }
                    }
                }
            }

            ArrayPool<Entry>.Shared.Return(m_Entries, true);
            m_Entries = newEntries;
        }

        private bool TryRemove(TKey key)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);

            if (entryIndex == -1)
            {
                return false;
            }


            m_Entries[entryIndex].Key = default!;
            m_Entries[entryIndex].Value = default!;
            //m_Entries[entryIndex].Used = UsedFlags.Tombstone;
            m_Entries[entryIndex].Used = UsedFlags.Empty;


            int currentIndex = entryIndex;
            int nextIndex = entryIndex;
            for (int offset = 1; offset < m_Entries.Length; offset++)
            {
                nextIndex = (nextIndex + 1) & (m_Entries.Length - 1);

                if (m_Entries[nextIndex].Used != UsedFlags.Used)
                {
                    break;
                }

                var nextOrigin = GetBucketIndex(m_Entries[nextIndex].Key.GetHashCode(), m_Entries.Length);
                if (currentIndex <= nextIndex)
                {
                    if (currentIndex < nextOrigin && nextOrigin <= nextIndex)
                    {
                        continue;
                    }
                }
                else
                {
                    if (nextOrigin <= nextIndex && currentIndex < nextOrigin)
                    {
                        continue;
                    }
                }

                m_Entries[currentIndex] = m_Entries[nextIndex];
                m_Entries[nextIndex].Used = UsedFlags.Empty;
                currentIndex = nextIndex;
            }

            m_Count--;
            return true;
        }






        public OpenAddressingDictionary() : this(16) { }
        public OpenAddressingDictionary(int capacity)
        {
            m_Entries = ArrayPool<Entry>.Shared.Rent(capacity);
            m_Entries.AsSpan().Clear();
            m_Count = 0;
        }
        public OpenAddressingDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary.Count)
        {
            if (dictionary is OpenAddressingDictionary<TKey, TValue> cloneSource)
            {
                cloneSource.m_Entries.CopyTo(m_Entries.AsSpan());
                m_Count = cloneSource.m_Count;
                return;
            }

            foreach (var pair in dictionary)
            {
                TryAdd(pair.Key, pair.Value, false);
            }
        }
        public OpenAddressingDictionary(IEnumerable<KeyValuePair<TKey, TValue>> dictionary) : this(dictionary.Count())
        {
            foreach (var pair in dictionary)
            {
                TryAdd(pair.Key, pair.Value, false);
            }
        }








        public TValue this[TKey key]
        {
            get => GetEntryIndex(key).entryIndex is int index && index >= 0 ? m_Entries[index].Value : throw new KeyNotFoundException();
            set => TryAdd(key, value, true);
        }

        public ICollection<TKey> Keys => m_Entries.Where(e => e.Used == UsedFlags.Used).Select(e => e.Key).ToArray();

        public ICollection<TValue> Values => m_Entries.Where(e => e.Used == UsedFlags.Used).Select(e => e.Value).ToArray();

        public int Count => m_Count;

        public bool IsReadOnly => false;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => throw new System.NotImplementedException();

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => throw new System.NotImplementedException();

        public void Add(TKey key, TValue value)
        {
            if (!TryAdd(key, value, false))
                throw new ArgumentException("key already exists");
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!TryAdd(item.Key, item.Value, false))
                throw new ArgumentException("key is already exists");
        }

        public void Clear()
        {
            m_Entries.AsSpan().Clear();
            m_Count = 0;
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return GetEntryIndex(item.Key).entryIndex is int entryIndex && entryIndex != -1 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Entries[entryIndex].Value);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key).entryIndex != -1;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array.Length + arrayIndex < m_Count)
                throw new ArgumentException("array too short");

            int dst = 0;
            for (int src = 0; src < m_Entries.Length; src++)
            {
                if (m_Entries[src].Used == UsedFlags.Used)
                {
                    array[arrayIndex + dst++] = new KeyValuePair<TKey, TValue>(m_Entries[src].Key, m_Entries[src].Value);
                }
            }
        }

        public void Dispose()
        {
            if (m_Entries != null)
            {
                ArrayPool<Entry>.Shared.Return(m_Entries);
                m_Entries = null!;
            }
            m_Count = 0;
        }


        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly OpenAddressingDictionary<TKey, TValue> parent;
            private int index;

            internal Enumerator(OpenAddressingDictionary<TKey, TValue> parent)
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
                while (++index < parent.m_Count)
                {
                    if (parent.m_Entries[index].Used == UsedFlags.Used)
                    {
                        return true;
                    }
                }
                return false;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => GetEnumerator();

        public bool Remove(TKey key)
        {
            return TryRemove(key);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            (_, _, int entryIndex) = GetEntryIndex(item.Key);
            if (entryIndex == -1)
                return false;

            if (EqualityComparer<TValue>.Default.Equals(item.Value, m_Entries[entryIndex].Value))
            {
                return TryRemove(item.Key);
            }

            return false;
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);

            if (entryIndex == -1)
            {
                value = default!;
                return false;
            }
            else
            {
                value = m_Entries[entryIndex].Value;
                return true;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();


        public override string ToString()
        {
            return $"{m_Count} items";
        }
    }
}