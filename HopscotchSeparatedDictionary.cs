
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ParallelDungeon.Rogue.Serialization
{

    public class HopscotchSeparatedDictionary<TKey, TValue> :
        ICollection<KeyValuePair<TKey, TValue>>,
        IDictionary<TKey, TValue>,
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
        IReadOnlyDictionary<TKey, TValue>,
        IDisposable
        where TKey : notnull
    {
        private TKey[] m_Keys;
        private TValue[] m_Values;
        private ulong[] m_Bitmaps;
        private int m_Count;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TrailingZeroCount(ulong x)
        {
#if NETCOREAPP3_0_OR_GREATER
            return System.Numerics.BitOperations.TrailingZeroCount(x);
#else
            uint lo = (uint)x;
            if (lo == 0)
            {
                return 32 + tzcnt((uint)(x >> 32));
            }
            return tzcnt(lo);

            static int tzcnt(uint x)
            {
                ReadOnlySpan<byte> TrailingZeroCountDeBruijn = new byte[]
                {
                    00, 01, 28, 02, 29, 14, 24, 03,
                    30, 22, 20, 15, 25, 17, 04, 08,
                    31, 27, 13, 23, 21, 19, 16, 07,
                    26, 12, 18, 06, 11, 05, 10, 09
                };
                return Unsafe.AddByteOffset(ref MemoryMarshal.GetReference(TrailingZeroCountDeBruijn),
                    (IntPtr)(int)(((x & (0 - x)) * 0x077cb531u) >> 27));
            }
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
            int bucketIndex = GetBucketIndex(hashCode, m_Bitmaps.Length);

            ulong bitmap = m_Bitmaps[bucketIndex];
            int mask = m_Bitmaps.Length - 1;
            while (TrailingZeroCount(bitmap) is int i && i < 64)
            {
                int next = (bucketIndex + i) & mask;
                if (EqualityComparer<TKey>.Default.Equals(m_Keys[next], key))
                {
                    return (hashCode, bucketIndex, next);
                }
                bitmap &= ~(1ul << i);
            }

            return (hashCode, bucketIndex, -1);
        }

        private void AddEntry(TKey key, TValue value)
        {
            if (key == null)
                ThrowArgumentNull(nameof(key));

            int hashCode = key.GetHashCode();

            if (m_Count == m_Bitmaps.Length)
            {
                Resize(m_Bitmaps.Length << 1);
            }

            int bucketIndex = GetBucketIndex(hashCode, m_Bitmaps.Length);
            int mask = m_Bitmaps.Length - 1;


            while (m_Bitmaps[bucketIndex] == ~0ul)
            {
                Resize(m_Bitmaps.Length << 1);
                bucketIndex = GetBucketIndex(hashCode, m_Bitmaps.Length);
                mask = m_Bitmaps.Length - 1;
            }

            for (int distance = 0; distance < m_Bitmaps.Length; distance++)
            {
                int nextIndex = (bucketIndex + distance) & mask;
                if ((m_Bitmaps[nextIndex] & 1) == 0)
                {
                    while (distance >= 64)
                    {
                        for (int shift = -63; shift < 0; shift++)
                        {
                            int shiftIndex = (bucketIndex + distance + shift) & mask;
                            ulong shiftBitmap = m_Bitmaps[shiftIndex];

                            if (shiftBitmap >= 1ul << shift)
                                continue;

                            int b = TrailingZeroCount(shiftBitmap);
                            shiftBitmap &= ~(1ul << b);
                            shiftBitmap |= 1ul << -shift;

                            m_Bitmaps[shiftIndex] = shiftBitmap;
                            (m_Keys[shiftIndex], m_Keys[nextIndex]) = (m_Keys[nextIndex], m_Keys[shiftIndex]);
                            (m_Values[shiftIndex], m_Values[nextIndex]) = (m_Values[nextIndex], m_Values[shiftIndex]);
                            distance += shift;
                            break;
                        }
                    }

                    m_Bitmaps[bucketIndex] |= 1ul << distance;
                    m_Bitmaps[nextIndex] |= 1ul;
                    m_Keys[nextIndex] = key;
                    m_Values[nextIndex] = value;
                    break;
                }
            }

            m_Count++;
        }

        private bool TryAdd(TKey key, TValue value, bool overwrite)
        {
            (_, _, int entryIndex) = GetEntryIndex(key);

            if (entryIndex != -1)
            {
                if (overwrite)
                {
                    m_Values[entryIndex] = value;
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
            var prevKeys = m_Keys;
            var prevValues = m_Values;
            var prevBitmaps = m_Bitmaps;

            m_Keys = ArrayPool<TKey>.Shared.Rent(size);
            m_Keys.AsSpan().Clear();
            m_Values = ArrayPool<TValue>.Shared.Rent(size);
            m_Values.AsSpan().Clear();
            m_Bitmaps = ArrayPool<ulong>.Shared.Rent(size);
            m_Bitmaps.AsSpan().Clear();
            m_Count = 0;

            for (int i = 0; i < prevBitmaps.Length; i++)
            {
                if (prevBitmaps[i] != 0)
                {
                    AddEntry(prevKeys[i], prevValues[i]);
                }
            }

            ArrayPool<TKey>.Shared.Return(prevKeys, true);
            ArrayPool<TValue>.Shared.Return(prevValues, true);
            ArrayPool<ulong>.Shared.Return(prevBitmaps);
        }

        private bool RemoveEntry(TKey key)
        {
            (_, int bucketIndex, int entryIndex) = GetEntryIndex(key);

            if (entryIndex == -1)
                return false;

            m_Keys[entryIndex] = default!;
            m_Values[entryIndex] = default!;
            m_Bitmaps[entryIndex] &= ~1ul;

            ulong bitmap = m_Bitmaps[bucketIndex];
            bitmap &= ~(1ul << (entryIndex - bucketIndex));
            m_Bitmaps[bucketIndex] = bitmap;

            m_Count--;
            return true;
        }

        // TODO
        private TKey[] GetKeys()
        {
            var array = new TKey[m_Count];
            int dst = 0;
            for (int src = 0; src < m_Bitmaps.Length; src++)
            {
                if (m_Bitmaps[src] != 0)
                {
                    array[dst++] = m_Keys[src];
                }
            }
            return array;
        }
        // TODO
        private TValue[] GetValues()
        {
            var array = new TValue[m_Count];
            int dst = 0;
            for (int src = 0; src < m_Bitmaps.Length; src++)
            {
                if (m_Bitmaps[src] != 0)
                {
                    array[dst++] = m_Values[src];
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




        public HopscotchSeparatedDictionary() : this(16) { }
        public HopscotchSeparatedDictionary(int capacity)
        {
            m_Keys = ArrayPool<TKey>.Shared.Rent(capacity);
            m_Keys.AsSpan().Clear();
            m_Values = ArrayPool<TValue>.Shared.Rent(capacity);
            m_Values.AsSpan().Clear();
            m_Bitmaps = ArrayPool<ulong>.Shared.Rent(capacity);
            m_Bitmaps.AsSpan().Clear();
            m_Count = 0;
        }
        public HopscotchSeparatedDictionary(IDictionary<TKey, TValue> source) : this(source.Count)
        {
            if (source is HopscotchSeparatedDictionary<TKey, TValue> cloneSource)
            {
                cloneSource.m_Keys.CopyTo(m_Keys.AsSpan());
                cloneSource.m_Values.CopyTo(m_Values.AsSpan());
                cloneSource.m_Bitmaps.CopyTo(m_Bitmaps.AsSpan());
                m_Count = cloneSource.m_Count;
                return;
            }

            foreach (var element in source)
            {
                Add(element);
            }
        }
        public HopscotchSeparatedDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(source.Count())
        {
            foreach (var element in source)
            {
                Add(element);
            }
        }






        public bool ContainsValue(TValue value)
        {
            for (int i = 0; i < m_Bitmaps.Length; i++)
            {
                if ((m_Bitmaps[i] & 1) == 0 &&
                    EqualityComparer<TValue>.Default.Equals(value, m_Values[i]))
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
                value = m_Values[entryIndex];
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
            private readonly HopscotchSeparatedDictionary<TKey, TValue> m_Parent;
            private int m_Index;

            internal Enumerator(HopscotchSeparatedDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
                m_Index = -1;
            }

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(m_Parent.m_Keys[m_Index], m_Parent.m_Values[m_Index]);

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                while (++m_Index < m_Parent.m_Count && (m_Parent.m_Bitmaps[m_Index] & 1) == 0) ;
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
            get => GetEntryIndex(key).entryIndex is int entryIndex && entryIndex >= 0 ? m_Values[entryIndex] : ThrowKeyNotFound();
            set => TryAdd(key, value, true);
        }



        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!TryAdd(item.Key, item.Value, false))
                ThrowKeyDuplicate();
        }

        public void Clear()
        {
            m_Keys.AsSpan().Clear();
            m_Values.AsSpan().Clear();
            m_Bitmaps.AsSpan().Clear();
            m_Count = 0;
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            (_, _, int entryIndex) = GetEntryIndex(item.Key);
            if (entryIndex == -1)
                return false;

            if (!EqualityComparer<TValue>.Default.Equals(item.Value, m_Values[entryIndex]))
                return false;

            return true;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (arrayIndex + m_Count < array.Length)
                throw new ArgumentException("array too short");

            int dst = arrayIndex;
            for (int src = 0; src < m_Bitmaps.Length; src++)
            {
                if (m_Bitmaps[src] != 0)
                {
                    array[dst] = new KeyValuePair<TKey, TValue>(m_Keys[src], m_Values[src]);
                    dst++;
                }
            }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            (_, _, int entryIndex) = GetEntryIndex(item.Key);

            if (entryIndex == -1)
                return false;

            if (!EqualityComparer<TValue>.Default.Equals(item.Value, m_Values[entryIndex]))
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
                value = m_Values[entryIndex];
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
            ArrayPool<TKey>.Shared.Return(m_Keys, true);
            ArrayPool<TValue>.Shared.Return(m_Values, true);
            ArrayPool<ulong>.Shared.Return(m_Bitmaps);
            m_Keys = null!;
            m_Values = null!;
            m_Bitmaps = null!;
            m_Count = 0;
        }

    }
}
