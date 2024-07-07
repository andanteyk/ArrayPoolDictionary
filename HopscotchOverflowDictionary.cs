
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ParallelDungeon.Rogue.Serialization
{

    public class HopscotchOverflowDictionary<TKey, TValue> :
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
            public ulong Bitmap;

            public override string ToString()
            {
                return $"{Bitmap:x16} | {Key}: {Value}";
            }
        }

        private Entry[] m_Entries;
        private int m_Count;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TrailingZeroCount(ulong x)
        {
#if NETCOREAPP3_0_OR_GREATER
            return System.Numerics.BitOperations.TrailingZeroCount(x);
#else
            int c = 63;
            x &= ~x + 1;
            if ((x & 0x00000000ffffffff) != 0) c -= 32;
            if ((x & 0x0000ffff0000ffff) != 0) c -= 16;
            if ((x & 0x00ff00ff00ff00ff) != 0) c -= 8;
            if ((x & 0x0f0f0f0f0f0f0f0f) != 0) c -= 4;
            if ((x & 0x3333333333333333) != 0) c -= 2;
            if ((x & 0x5555555555555555) != 0) c -= 1;
            return c;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBucketIndex(int hashCode, int bucketLength)
        {
            return hashCode & (bucketLength - 1);
        }

        private (int hashCode, int bucketIndex, int entryIndex) GetEntryIndex(TKey key)
        {
            if (key == null)
                ThrowArgumentNull(nameof(key));

            int hashCode = key.GetHashCode();
            int bucketIndex = GetBucketIndex(hashCode, m_Entries.Length);

            do
            {
                ulong bitmap = m_Entries[bucketIndex].Bitmap;
                int mask = m_Entries.Length - 1;

                for (int i = 0; i < 63; i++)
                {
                    if ((bitmap & (1ul << i)) != 0)
                    {
                        int next = (bucketIndex + i) & mask;
                        if (EqualityComparer<TKey>.Default.Equals(m_Entries[next].Key, key))
                        {
                            return (hashCode, bucketIndex, next);
                        }
                    }
                }

                if ((bitmap & (1ul << 63)) != 0)
                {
                    bucketIndex = (bucketIndex + 63) & mask;
                }
                else
                {
                    break;
                }
            } while (true);

            return (hashCode, bucketIndex, -1);
        }

        private void AddEntry(TKey key, TValue value)
        {
            if (key == null)
                ThrowArgumentNull(nameof(key));

            int hashCode = key.GetHashCode();

            if (m_Count >= m_Entries.Length * 0.5)
            {
                Resize(m_Entries.Length << 1);
            }

            int bucketIndex = GetBucketIndex(hashCode, m_Entries.Length);
            int mask = m_Entries.Length - 1;


            while (m_Entries[bucketIndex].Bitmap == ~0ul)
            {
                bucketIndex = (bucketIndex + 63) & mask;
            }

            int distance;
            for (distance = 0; distance < m_Entries.Length; distance++)
            {
                int nextIndex = (bucketIndex + distance) & mask;
                if ((m_Entries[nextIndex].Bitmap & 1) == 0)
                {
                    while (distance >= 64)
                    {
                        for (int shift = -63; shift < 0; shift++)
                        {
                            int shiftIndex = (bucketIndex + distance + shift) & mask;
                            ulong shiftBitmap = m_Entries[shiftIndex].Bitmap;

                            if (shiftBitmap >= 1ul << -shift)
                                continue;

                            int b = TrailingZeroCount(shiftBitmap);
                            shiftBitmap &= ~(1ul << b);
                            shiftBitmap |= 1ul << -shift;

                            m_Entries[shiftIndex].Bitmap = shiftBitmap;
                            (m_Entries[shiftIndex].Key, m_Entries[nextIndex].Key) = (m_Entries[nextIndex].Key, m_Entries[shiftIndex].Key);
                            (m_Entries[shiftIndex].Value, m_Entries[nextIndex].Value) = (m_Entries[nextIndex].Value, m_Entries[shiftIndex].Value);
                            distance += shift;
                            break;
                        }
                    }

                    m_Entries[bucketIndex].Bitmap |= 1ul << distance;
                    m_Entries[nextIndex].Bitmap |= 1ul;
                    m_Entries[nextIndex].Key = key;
                    m_Entries[nextIndex].Value = value;
                    break;
                }
            }

            System.Diagnostics.Debug.Assert(distance < m_Entries.Length);

            m_Count++;
        }

        private bool TryAdd(TKey key, TValue value, bool overwrite)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);

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

            AddEntry(key, value);
            return true;
        }

        private void Resize(int size)
        {
            var prevEntries = m_Entries;

            System.Diagnostics.Debug.Assert(m_Count == prevEntries.Count(e => (e.Bitmap & 1) != 0));

            m_Entries = ArrayPool<Entry>.Shared.Rent(size);
            m_Entries.AsSpan().Clear();
            m_Count = 0;

            for (int i = 0; i < prevEntries.Length; i++)
            {
                if ((prevEntries[i].Bitmap & 1) != 0)
                {
                    AddEntry(prevEntries[i].Key, prevEntries[i].Value);
                }
            }

            System.Diagnostics.Debug.Assert(m_Count == m_Entries.Count(e => (e.Bitmap & 1) != 0));

            ArrayPool<Entry>.Shared.Return(prevEntries, true);
        }

        private bool RemoveEntry(TKey key)
        {
            (_, int bucketIndex, int entryIndex) = GetEntryIndex(key);

            if (entryIndex == -1)
                return false;

            m_Entries[entryIndex].Key = default!;
            m_Entries[entryIndex].Value = default!;
            m_Entries[entryIndex].Bitmap &= ~1ul;

            ulong bitmap = m_Entries[bucketIndex].Bitmap;
            bitmap &= ~(1ul << (entryIndex - bucketIndex));
            m_Entries[bucketIndex].Bitmap = bitmap;

            m_Count--;
            return true;
        }

        // TODO
        private TKey[] GetKeys()
        {
            var array = new TKey[m_Count];
            int dst = 0;
            for (int src = 0; src < m_Entries.Length; src++)
            {
                if (m_Entries[src].Bitmap != 0)
                {
                    array[dst++] = m_Entries[src].Key;
                }
            }
            return array;
        }
        // TODO
        private TValue[] GetValues()
        {
            var array = new TValue[m_Count];
            int dst = 0;
            for (int src = 0; src < m_Entries.Length; src++)
            {
                if (m_Entries[src].Bitmap != 0)
                {
                    array[dst++] = m_Entries[src].Value;
                }
            }
            return array;
        }


        [DoesNotReturn]
        private static void ThrowKeyDuplicate()
            => throw new ArgumentException("duplicated key");

        [DoesNotReturn]
        private static void ThrowArgumentNull(string name)
            => throw new ArgumentNullException(name);

        [DoesNotReturn]
        private static void ThrowInvalidOperation(string name)
            => throw new InvalidOperationException(name);

        [DoesNotReturn]
        private static TValue ThrowKeyNotFound()
            => throw new KeyNotFoundException();




        public HopscotchOverflowDictionary() : this(16) { }
        public HopscotchOverflowDictionary(int capacity)
        {
            m_Entries = ArrayPool<Entry>.Shared.Rent(capacity);
            m_Entries.AsSpan().Clear();
            m_Count = 0;
        }
        public HopscotchOverflowDictionary(IDictionary<TKey, TValue> source) : this(source.Count)
        {
            if (source is HopscotchOverflowDictionary<TKey, TValue> cloneSource)
            {
                cloneSource.m_Entries.CopyTo(m_Entries.AsSpan());
                m_Count = cloneSource.m_Count;
                return;
            }

            foreach (var element in source)
            {
                Add(element);
            }
        }
        public HopscotchOverflowDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(source.Count())
        {
            foreach (var element in source)
            {
                Add(element);
            }
        }






        public bool ContainsValue(TValue value)
        {
            for (int i = 0; i < m_Entries.Length; i++)
            {
                if ((m_Entries[i].Bitmap & 1) == 0 &&
                    EqualityComparer<TValue>.Default.Equals(value, m_Entries[i].Value))
                    return true;
            }
            return false;
        }

        public bool Remove(TKey key)
        {
            return RemoveEntry(key);
        }

        public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);

            if (entryIndex != -1)
            {
                value = m_Entries[entryIndex].Value;
                return RemoveEntry(key);
            }
            else
            {
                value = default!;
                return false;
            }
        }

        public override string ToString()
        {
            return $"{m_Count} items";
        }

        public bool TryAdd(TKey key, TValue value)
        {
            return TryAdd(key, value, false);
        }

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly HopscotchOverflowDictionary<TKey, TValue> m_Parent;
            private int m_Index;

            internal Enumerator(HopscotchOverflowDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
                m_Index = -1;
            }

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(m_Parent.m_Entries[m_Index].Key, m_Parent.m_Entries[m_Index].Value);

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                while (++m_Index < m_Parent.m_Count && (m_Parent.m_Entries[m_Index].Bitmap & 1) == 0) ;
                return m_Index < m_Parent.m_Count;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }







        public int Count => m_Count;

        public bool IsReadOnly => false;

        public ICollection<TKey> Keys => GetKeys();

        public ICollection<TValue> Values => GetValues();

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => GetKeys();

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => GetValues();

        public TValue this[TKey key]
        {
            get => GetEntryIndex(key).entryIndex is int entryIndex && entryIndex >= 0 ? m_Entries[entryIndex].Value : ThrowKeyNotFound();
            set => TryAdd(key, value, true);
        }



        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!TryAdd(item.Key, item.Value, false))
                ThrowKeyDuplicate();
        }

        public void Clear()
        {
            m_Entries.AsSpan().Clear();
            m_Count = 0;
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            (_, _, int entryIndex) = GetEntryIndex(item.Key);
            if (entryIndex == -1)
                return false;

            if (!EqualityComparer<TValue>.Default.Equals(item.Value, m_Entries[entryIndex].Value))
                return false;

            return true;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (arrayIndex + m_Count < array.Length)
                throw new ArgumentException("array too short");

            int dst = arrayIndex;
            for (int src = 0; src < m_Entries.Length; src++)
            {
                if (m_Entries[src].Bitmap != 0)
                {
                    array[dst] = new KeyValuePair<TKey, TValue>(m_Entries[src].Key, m_Entries[src].Value);
                    dst++;
                }
            }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            (_, _, int entryIndex) = GetEntryIndex(item.Key);

            if (entryIndex == -1)
                return false;

            if (!EqualityComparer<TValue>.Default.Equals(item.Value, m_Entries[entryIndex].Value))
                return false;

            return RemoveEntry(item.Key);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(TKey key, TValue value)
        {
            if (!TryAdd(key, value, false))
                ThrowKeyDuplicate();
        }

        public bool ContainsKey(TKey key)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);

            return entryIndex != -1;
        }

        bool IDictionary<TKey, TValue>.Remove(TKey key)
        {
            return RemoveEntry(key);
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);

            if (entryIndex != -1)
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

        public void Dispose()
        {
            if (m_Entries != null)
            {
                ArrayPool<Entry>.Shared.Return(m_Entries, true);
                m_Entries = null!;
            }
            m_Count = 0;
        }

    }

}