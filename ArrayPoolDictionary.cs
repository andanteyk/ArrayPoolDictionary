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
    // Separate Chaining
    public class ArrayPoolDictionary<TKey, TValue> :
        ICollection<KeyValuePair<TKey, TValue>>,
        IDictionary<TKey, TValue>,
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
        IReadOnlyDictionary<TKey, TValue>,
        IDisposable
        where TKey : notnull
    {

        private int[] m_Buckets;
        private TKey[] m_Keys;
        private TValue[] m_Values;
        private int[] m_Nexts;
        private int m_Count;
        private int m_FreeList;


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
            int bucketIndex = GetBucketIndex(hashCode, m_Buckets.Length);
            int entryIndex = m_Buckets[bucketIndex];

            while ((uint)entryIndex < (uint)m_Keys.Length)
            {
                if (EqualityComparer<TKey>.Default.Equals(m_Keys[entryIndex], key))
                {
                    return (hashCode, bucketIndex, entryIndex);
                }

                entryIndex = m_Nexts[entryIndex];
            }

            return (hashCode, bucketIndex, -1);
        }

        private bool TryAdd(TKey key, TValue value, bool overwrite)
        {
            if (key == null)
                ThrowArgumentNull(nameof(key));

            (int hashCode, int bucketIndex, int oldEntryIndex) = GetEntryIndex(key);
            if (oldEntryIndex != -1)
            {
                if (overwrite)
                {
                    m_Values[oldEntryIndex] = value;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (m_Count == m_Buckets.Length)
            {
                Resize(m_Count << 1);
                bucketIndex = GetBucketIndex(hashCode, m_Buckets.Length);
            }

            int newIndex;
            if (m_FreeList != -1)
            {
                newIndex = m_FreeList;
                m_FreeList = m_Nexts[m_FreeList];
                m_Nexts[newIndex] = -1;
            }
            else
            {
                newIndex = m_Count;
            }


            if (m_Buckets[bucketIndex] == -1)
            {
                m_Buckets[bucketIndex] = newIndex;
            }
            else
            {
                int entryIndex, safetyCounter;
                for (entryIndex = m_Buckets[bucketIndex], safetyCounter = 0;
                    m_Nexts[entryIndex] != -1 && safetyCounter < m_Count;
                    entryIndex = m_Nexts[entryIndex], safetyCounter++) ;

                if (safetyCounter >= m_Count)
                    ThrowInvalidOperation($"content may alter. {safetyCounter} >= {m_Count}");

                m_Nexts[entryIndex] = newIndex;
            }

            m_Keys[newIndex] = key;
            m_Values[newIndex] = value;
            m_Count++;
            return true;
        }

        private void Resize(int size)
        {
            var newBucket = ArrayPool<int>.Shared.Rent(size);
            var newKeys = ArrayPool<TKey>.Shared.Rent(size);
            var newValues = ArrayPool<TValue>.Shared.Rent(size);
            var newNexts = ArrayPool<int>.Shared.Rent(size);

            newBucket.AsSpan().Fill(-1);
            m_Keys.CopyTo(newKeys, 0);
            m_Values.CopyTo(newValues, 0);
            newNexts.AsSpan().Fill(-1);

            for (int i = 0; i < m_Count; i++)
            {
                var hashCode = m_Keys[i]!.GetHashCode();
                int bucketIndex = GetBucketIndex(hashCode, size);
                if (newBucket[bucketIndex] == -1)
                {
                    newBucket[bucketIndex] = i;
                }
                else
                {
                    int entryIndex, safetyCounter;
                    for (entryIndex = newBucket[bucketIndex], safetyCounter = 0;
                        newNexts[entryIndex] != -1 && safetyCounter < m_Count;
                        entryIndex = newNexts[entryIndex], safetyCounter++) ;

                    if (safetyCounter >= m_Count)
                        ThrowInvalidOperation("content may alter");

                    newNexts[entryIndex] = i;
                }
            }

            ArrayPool<int>.Shared.Return(m_Buckets);
            ArrayPool<TKey>.Shared.Return(m_Keys, true);
            ArrayPool<TValue>.Shared.Return(m_Values, true);
            ArrayPool<int>.Shared.Return(m_Nexts);

            m_Buckets = newBucket;
            m_Keys = newKeys;
            m_Values = newValues;
            m_Nexts = newNexts;
        }

        private bool TryRemove(TKey key)
        {
            (_, int bucketIndex, int entryIndex) = GetEntryIndex(key);

            if (entryIndex < 0)
            {
                return false;
            }

            int prev = m_Buckets[bucketIndex];
            int next = m_Nexts[prev];
            if (prev == entryIndex)
            {
                m_Buckets[bucketIndex] = next;
            }
            else
            {
                int safetyCount;
                for (safetyCount = 0; safetyCount < m_Count; safetyCount++)
                {
                    if (next == entryIndex)
                    {
                        m_Nexts[prev] = m_Nexts[next];
                        break;
                    }
                    (prev, next) = (next, m_Nexts[next]);
                }
                if (safetyCount >= m_Count)
                    ThrowInvalidOperation("content may alter");
            }


            m_Nexts[entryIndex] = m_FreeList;
            m_FreeList = entryIndex;

            m_Count--;
            return true;
        }



        [DoesNotReturn]
        private static void ThrowArgumentNull(string name) => throw new ArgumentNullException(name);

        [DoesNotReturn]
        private static void ThrowInvalidOperation(string message) => throw new InvalidOperationException(message);

        [DoesNotReturn]
        private static void ThrowKeyDuplicated() => throw new ArgumentException("given key is already exists");








        public ArrayPoolDictionary() : this(16) { }
        public ArrayPoolDictionary(int capacity)
        {
            m_Buckets = ArrayPool<int>.Shared.Rent(capacity);
            Array.Fill(m_Buckets, -1);
            m_Keys = ArrayPool<TKey>.Shared.Rent(capacity);
            m_Values = ArrayPool<TValue>.Shared.Rent(capacity);
            m_Nexts = ArrayPool<int>.Shared.Rent(capacity);
            Array.Fill(m_Nexts, -1);
            m_Count = 0;
        }
        public ArrayPoolDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary.Count)
        {
            if (dictionary is ArrayPoolDictionary<TKey, TValue> cloneDictionary)
            {
                cloneDictionary.m_Buckets.CopyTo(m_Buckets.AsSpan());
                cloneDictionary.m_Keys.CopyTo(m_Keys.AsSpan());
                cloneDictionary.m_Values.CopyTo(m_Values.AsSpan());
                cloneDictionary.m_Nexts.CopyTo(m_Nexts.AsSpan());
                m_Count = cloneDictionary.m_Count;
                m_FreeList = cloneDictionary.m_FreeList;
                return;
            }

            foreach (var pair in dictionary)
            {
                if (!TryAdd(pair.Key, pair.Value, false))
                    ThrowKeyDuplicated();
            }
        }
        public ArrayPoolDictionary(IEnumerable<KeyValuePair<TKey, TValue>> dictionary) : this(dictionary.Count())
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
            get => GetEntryIndex(key).entryIndex is int index && index != -1 ? m_Values[index] : throw new KeyNotFoundException();
            set => TryAdd(key, value, true);
        }

        public int Count => m_Count;

        public bool IsReadOnly => false;

        public ICollection<TKey> Keys => m_Keys[0..m_Count];

        public ICollection<TValue> Values => m_Values[0..m_Count];

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => m_Keys[0..m_Count];

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => m_Values[0..m_Count];

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
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Values[index]);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key).entryIndex is int index && index >= 0;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array.Length < arrayIndex + m_Count)
                ThrowInvalidOperation("array is too short");

            for (int i = 0; i < m_Count; i++)
            {
                array[arrayIndex + i] = new KeyValuePair<TKey, TValue>(m_Keys[i], m_Values[i]);
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
            private readonly ArrayPoolDictionary<TKey, TValue> parent;
            private int index;

            internal Enumerator(ArrayPoolDictionary<TKey, TValue> parent)
            {
                this.parent = parent;
                index = -1;
            }

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(parent.m_Keys[index], parent.m_Values[index]);

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
            if (!EqualityComparer<TValue>.Default.Equals(m_Values[entryIndex], item.Value))
            {
                return false;
            }

            return TryRemove(item.Key);
        }

        // TODO: broken.
        public bool Remove(TKey key)
        {
            return TryRemove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);
            if (entryIndex >= 0)
            {
                value = m_Values[entryIndex];
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
            if (m_Buckets != null)
            {
                ArrayPool<int>.Shared.Return(m_Buckets);
                ArrayPool<TKey>.Shared.Return(m_Keys, true);
                ArrayPool<TValue>.Shared.Return(m_Values, true);
                ArrayPool<int>.Shared.Return(m_Nexts);

                m_Buckets = null!;
                m_Keys = null!;
                m_Values = null!;
                m_Nexts = null!;
            }
            m_Count = 0;
        }
    }
}
