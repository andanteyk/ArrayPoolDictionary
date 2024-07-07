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
    public class SwissTableDictionary<TKey, TValue> :
        ICollection<KeyValuePair<TKey, TValue>>,
        IDictionary<TKey, TValue>,
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
        IReadOnlyDictionary<TKey, TValue>,
        IDictionary,
        IDisposable
        where TKey : notnull
    {
        private const sbyte Empty = -128;
        private const sbyte Deleted = -2;

        private const int GroupWidth = 8;
        private const int GroupShift = 3;



        private sbyte[] m_ControlBytes;
        private KeyValuePair<TKey, TValue>[] m_Slots;
        private int m_Size;
        private int m_GrowthLeft;


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
        private static int LeadingZeroCount(ulong x)
        {
#if NETCOREAPP3_0_OR_GREATER
            return System.Numerics.BitOperations.LeadingZeroCount(x);
#else
            int zeroes = 64;
            if ((x >> 32) != 0)
            {
                zeroes -= 32;
                x >>= 32;
            }
            if ((x >> 16) != 0)
            {
                zeroes -= 16;
                x >>= 16;
            }
            if ((x >> 8) != 0)
            {
                zeroes -= 8;
                x >>= 8;
            }
            if ((x >> 4) != 0)
            {
                zeroes -= 4;
                x >>= 4;
            }
            if ((x >> 2) != 0)
            {
                zeroes -= 2;
                x >>= 2;
            }
            if ((x >> 1) != 0)
            {
                return zeroes - 2;
            }
            return zeroes - (int)x;
#endif
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEmpty(sbyte controlByte)
            => controlByte == Empty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFull(sbyte controlByte)
            => controlByte >= 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDeleted(sbyte controlByte)
            => controlByte == Deleted;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEmptyOrDeleted(sbyte controlByte)
            => controlByte < -1;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash1(int hash)
            => (int)((uint)hash >> 7);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static sbyte Hash2(int hash)
            => (sbyte)(hash & 0x7f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Match(ulong group, sbyte hash)
        {
            ulong msbs = 0x8080808080808080;
            ulong lsbs = 0x0101010101010101;
            ulong x = group ^ (lsbs * (byte)hash);
            return (x - lsbs) & ~x & msbs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MatchEmptyGroup(ulong group)
        {
            ulong msbs = 0x8080808080808080;
            return group & (~group << 6) & msbs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MatchEmptyOrDeletedGroup(ulong group)
        {
            ulong msbs = 0x8080808080808080;
            return group & (~group << 7) & msbs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ConvertSpecialToEmptyAndFullToDeleted(ulong group)
        {
            ulong msbs = 0x8080808080808080;
            ulong lsbs = 0x0101010101010101;
            ulong x = group & msbs;
            ulong res = (~x + (x >> 7)) & ~lsbs;
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetGrowthLeft()
        {
            m_GrowthLeft = m_ControlBytes.Length - m_ControlBytes.Length / 8 - m_Size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountLeadingEmptyOrDeleted(ulong group)
        {
            ulong gaps = 0x00fefefefefefefe;
            return (TrailingZeroCount(((~group & (group >> 7)) | gaps) + 1) + 7) >> 3;
        }




        private int GetEntryIndex(TKey key)
        {
            int mask = (m_ControlBytes.Length - 1) & ~0x7;
            int hashCode = key.GetHashCode();
            int hash1 = Hash1(hashCode);
            sbyte hash2 = Hash2(hashCode);

            for (int offset = 0; offset <= mask; offset += 8)
            {
                int current = (hash1 + offset) & mask;

                ulong group = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes)[current >> 3];
                ulong match = Match(group, hash2);
                for (int i = TrailingZeroCount(match) >> 3; match != 0; match &= match - 1, i = TrailingZeroCount(match) >> 3)
                {

                    if (EqualityComparer<TKey>.Default.Equals(m_Slots[current + i].Key, key))
                    {
                        return current + i;
                    }
                }

                if (MatchEmptyGroup(group) != 0)
                {
                    break;
                }
            }

            return -1;
        }

        private bool AddEntry(TKey key, TValue value, bool overwrite)
        {
            int mask = (m_ControlBytes.Length - 1) & ~0x7;
            int hashCode = key.GetHashCode();
            int hash1 = Hash1(hashCode);
            sbyte hash2 = Hash2(hashCode);

            for (int offset = 0; offset <= mask; offset += 8)
            {
                int current = (hash1 + offset) & mask;

                ulong group = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes)[current >> 3];
                ulong match = Match(group, hash2);
                for (int i = TrailingZeroCount(match) >> 3; match != 0; match &= match - 1, i = TrailingZeroCount(match) >> 3)
                {
                    if (EqualityComparer<TKey>.Default.Equals(m_Slots[current + i].Key, key))
                    {
                        if (overwrite)
                        {
                            m_Slots[current + i] = new KeyValuePair<TKey, TValue>(key, value);
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                if (MatchEmptyGroup(group) != 0)
                {
                    break;
                }
            }


            int target = FindFirstNonFull(hash1);
            if (m_GrowthLeft == 0 && !IsDeleted(m_ControlBytes[target]))
            {
                RehashAndGrowIfNecessary();
                target = FindFirstNonFull(hash1);
            }
            m_Size++;
            m_GrowthLeft -= IsEmpty(m_ControlBytes[target]) ? 1 : 0;
            m_ControlBytes[target] = Hash2(hashCode);
            m_Slots[target] = new KeyValuePair<TKey, TValue>(key, value);

            return true;
        }

        private bool RemoveEntry(TKey key)
        {
            int index = GetEntryIndex(key);
            if (index == -1)
            {
                return false;
            }

            m_Size--;
            int indexBefore = (index - 8) & (m_ControlBytes.Length - 1);
            var groups = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes);
            ulong emptyAfter = MatchEmptyGroup(groups[index >> 3]);
            ulong emptyBefore = MatchEmptyGroup(groups[indexBefore >> 3]);

            bool wasNeverFull = emptyBefore != 0 && emptyAfter != 0 &&
                (TrailingZeroCount(emptyAfter) >> 3) + (LeadingZeroCount(emptyBefore) >> 3) < 8;

            m_ControlBytes[index] = wasNeverFull ? Empty : Deleted;
            m_GrowthLeft += wasNeverFull ? 1 : 0;

            return true;
        }

        private int FindFirstNonFull(int hash1)
        {
            int mask = (m_ControlBytes.Length - 1) & ~0x7;
            var groups = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes);

            for (int offset = 0; offset <= mask; offset += 8)
            {
                int current = (hash1 + offset) & mask;

                ulong group = groups[current >> 3];
                ulong match = MatchEmptyOrDeletedGroup(group);

                if (match != 0)
                {
                    System.Diagnostics.Debug.Assert(IsEmptyOrDeleted(m_ControlBytes[current + (TrailingZeroCount(match) >> 3)]));
                    return current + (TrailingZeroCount(match) >> 3);
                }
            }

            return -1;
        }

        private void RehashAndGrowIfNecessary()
        {
            if (m_Size * 32L <= m_ControlBytes.Length * 25L)
            {
                // DropDeletesWithoutResize

                // Deleted to Empty, and Full to Deleted
                var groups = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes);
                for (int i = 0; i < groups.Length; i++)
                {
                    groups[i] = ConvertSpecialToEmptyAndFullToDeleted(groups[i]);
                }

                int mask = m_ControlBytes.Length - 1;
                for (int i = 0; i < m_ControlBytes.Length; i++)
                {
                    if (!IsDeleted(m_ControlBytes[i]))
                        continue;

                    int hashCode = m_Slots[i].Key.GetHashCode();
                    int hash1 = Hash1(hashCode);
                    int target = FindFirstNonFull(hash1);

                    // if is already in same group, dont move.
                    if (i >> 3 == target >> 3)
                    {
                        m_ControlBytes[i] = Hash2(hashCode);
                        continue;
                    }

                    if (IsEmpty(m_ControlBytes[target]))
                    {
                        m_ControlBytes[target] = Hash2(hashCode);
                        m_Slots[target] = m_Slots[i];
                        m_ControlBytes[i] = Empty;
                    }
                    else
                    {
                        // assumes m_ControlBytes[target] is deleted
                        m_ControlBytes[target] = Hash2(hashCode);
                        (m_Slots[i], m_Slots[target]) = (m_Slots[target], m_Slots[i]);
                        i--;
                    }
                }

                ResetGrowthLeft();
            }
            else
            {
                // Resize
                Resize(m_ControlBytes.Length << 1);
            }
        }

        private void Resize(int newCapacity)
        {
            var oldControlBytes = m_ControlBytes;
            var oldSlots = m_Slots;

            m_ControlBytes = ArrayPool<sbyte>.Shared.Rent(newCapacity);
            m_ControlBytes.AsSpan().Fill(Empty);
            m_Slots = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(newCapacity);
            m_Slots.AsSpan().Clear();

            ResetGrowthLeft();


            for (int i = 0; i < oldControlBytes.Length; i++)
            {
                if (IsFull(oldControlBytes[i]))
                {
                    int hashCode = oldSlots[i].Key.GetHashCode();
                    int target = FindFirstNonFull(Hash1(hashCode));
                    m_ControlBytes[target] = Hash2(hashCode);
                    m_Slots[target] = oldSlots[i];
                }
            }

            ArrayPool<sbyte>.Shared.Return(oldControlBytes);
            ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(oldSlots, true);
        }

        private void ClearTable()
        {
            // originally realloc m_ControlBytes/m_Slots when capacity >= 128
            m_Size = 0;
            m_ControlBytes.AsSpan().Fill(Empty);
            ResetGrowthLeft();
        }

        private int SkipEmptyOrDeleted(int from)
        {
            if (from >= m_ControlBytes.Length)
                return -1;

            var groups = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes);
            int i = from;

            if (!IsEmptyOrDeleted(m_ControlBytes[i]))
                return i;

            int firstShift = CountLeadingEmptyOrDeleted(groups[i >> 3] >> (i << 3));       // | (((1ul << ((i & 7) << 3)) - 1)
            i += firstShift;

            if (i >= m_ControlBytes.Length)
                return -1;

            while (IsEmptyOrDeleted(m_ControlBytes[i]))
            {
                int shift = CountLeadingEmptyOrDeleted(groups[i >> 3]);
                i += shift;

                if (shift == 0)
                    break;
            }

            return i;
        }



        public SwissTableDictionary(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            m_ControlBytes = ArrayPool<sbyte>.Shared.Rent(capacity);
            m_ControlBytes.AsSpan().Fill(Empty);
            m_Slots = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(capacity);
            m_Size = 0;
            ResetGrowthLeft();
        }
        public SwissTableDictionary() : this(16) { }

        public SwissTableDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary.Count)
        {
            if (dictionary is SwissTableDictionary<TKey, TValue> cloneSource)
            {
                for (int i = cloneSource.SkipEmptyOrDeleted(0); (uint)i < (uint)cloneSource.m_ControlBytes.Length; i = cloneSource.SkipEmptyOrDeleted(i + 1))
                {
                    var hashCode = cloneSource.m_Slots[i].Key.GetHashCode();
                    int index = FindFirstNonFull(Hash1(hashCode));
                    m_ControlBytes[index] = Hash2(hashCode);
                    m_Slots[index] = cloneSource.m_Slots[i];
                }

                m_Size = cloneSource.m_Size;
                ResetGrowthLeft();
                return;
            }

            foreach (var pair in dictionary)
            {
                AddEntry(pair.Key, pair.Value, false);
            }
        }

        public SwissTableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(source.Count())
        {
            foreach (var pair in source)
            {
                AddEntry(pair.Key, pair.Value, false);
            }
        }













        public TValue this[TKey key]
        {
            get => GetEntryIndex(key) is int index && index >= 0 ? m_Slots[index].Value : throw new KeyNotFoundException();
            set => AddEntry(key, value, true);
        }

        public int Count => m_Size;

        public bool IsReadOnly => false;

        public readonly struct KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly SwissTableDictionary<TKey, TValue> m_Parent;

            internal KeyCollection(SwissTableDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
            }

            public int Count => m_Parent.Count;

            public bool IsReadOnly => true;

            public bool IsSynchronized => false;

            public object SyncRoot => ((ICollection)m_Parent).SyncRoot;

            public void Add(TKey item) => throw new NotSupportedException();

            public void Clear() => throw new NotSupportedException();

            public bool Contains(TKey item)
            {
                return m_Parent.ContainsKey(item);
            }

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                if (array.Length - arrayIndex < m_Parent.m_Size)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Slots.Length; i++)
                {
                    if (IsEmptyOrDeleted(m_Parent.m_ControlBytes[i]))
                    {
                        skipped++;
                        continue;
                    }
                    array[arrayIndex + i - skipped] = m_Parent.m_Slots[i].Key;
                }
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();

            public bool Remove(TKey item) => throw new NotSupportedException();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public Enumerator GetEnumerator()
            {
                return new Enumerator(m_Parent);
            }

            public void CopyTo(Array array, int index)
            {
                if (array is not TKey[] tkey)
                    throw new InvalidCastException(nameof(array));
                if (array.Length - index < m_Parent.m_Size)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Slots.Length; i++)
                {
                    if (IsEmptyOrDeleted(m_Parent.m_ControlBytes[i]))
                    {
                        skipped++;
                        continue;
                    }
                    tkey[index + i - skipped] = m_Parent.m_Slots[i].Key;
                }
            }

            public struct Enumerator : IEnumerator<TKey>
            {
                private readonly SwissTableDictionary<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(SwissTableDictionary<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                }

                public TKey Current => m_Parent.m_Slots[m_Index].Key;

                object IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    while ((uint)++m_Index < (uint)m_Parent.m_Slots.Length)
                    {
                        if (!IsEmptyOrDeleted(m_Parent.m_ControlBytes[m_Index]))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                public void Reset() => throw new NotSupportedException();
            }
        }

        public readonly struct ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private readonly SwissTableDictionary<TKey, TValue> m_Parent;

            internal ValueCollection(SwissTableDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
            }

            public int Count => m_Parent.Count;

            public bool IsReadOnly => true;

            public bool IsSynchronized => false;

            public object SyncRoot => ((ICollection)m_Parent).SyncRoot;

            public void Add(TValue item) => throw new NotSupportedException();

            public void Clear() => throw new NotSupportedException();

            public bool Contains(TValue item)
            {
                return m_Parent.ContainsValue(item);
            }

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                if (array.Length - arrayIndex < m_Parent.m_Size)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Slots.Length; i++)
                {
                    if (IsEmptyOrDeleted(m_Parent.m_ControlBytes[i]))
                    {
                        skipped++;
                        continue;
                    }
                    array[i + arrayIndex - skipped] = m_Parent.m_Slots[i].Value;
                }
            }

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();

            public bool Remove(TValue item) => throw new NotSupportedException();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public Enumerator GetEnumerator()
            {
                return new Enumerator(m_Parent);
            }

            public void CopyTo(Array array, int index)
            {
                if (array is not TValue[] tvalue)
                    throw new InvalidCastException(nameof(array));
                if (array.Length - index < m_Parent.m_Size)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Slots.Length; i++)
                {
                    if (IsEmptyOrDeleted(m_Parent.m_ControlBytes[i]))
                    {
                        skipped++;
                        continue;
                    }
                    tvalue[i + index - skipped] = m_Parent.m_Slots[i].Value;
                }
            }

            public struct Enumerator : IEnumerator<TValue>
            {
                private readonly SwissTableDictionary<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(SwissTableDictionary<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                }

                public TValue Current => m_Parent.m_Slots[m_Index].Value;

                object? IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    while ((uint)++m_Index < (uint)m_Parent.m_Slots.Length)
                    {
                        if (!IsEmptyOrDeleted(m_Parent.m_ControlBytes[m_Index]))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                public void Reset() => throw new NotSupportedException();
            }
        }

        public KeyCollection Keys => new KeyCollection(this);

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;

        public ValueCollection Values => new ValueCollection(this);

        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public bool IsFixedSize => false;

        ICollection IDictionary.Keys => Keys;

        ICollection IDictionary.Values => Values;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        object? IDictionary.this[object key]
        {
            get => key is TKey tkey ? this[tkey] : throw new InvalidCastException(nameof(key));
            set
            {
                if (key is TKey tkey && value is TValue tvalue)
                    this[tkey] = tvalue;
                else
                    throw new InvalidCastException();
            }
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!AddEntry(item.Key, item.Value, false))
                throw new InvalidOperationException("key duplicated");
        }

        public void Add(TKey key, TValue value)
        {
            if (!AddEntry(key, value, false))
                throw new InvalidOperationException("key duplicated");
        }

        public void Clear()
        {
            ClearTable();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return GetEntryIndex(item.Key) is int index && index >= 0 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Slots[index].Value);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key) >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (!IsEmptyOrDeleted(m_ControlBytes[i]) &&
                    EqualityComparer<TValue>.Default.Equals(m_Slots[i].Value, value))
                {
                    return true;
                }
            }

            return false;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array.Length - arrayIndex < m_Size)
                throw new ArgumentException(nameof(array));

            int skipped = 0;
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (IsEmptyOrDeleted(m_ControlBytes[i]))
                {
                    skipped++;
                    continue;
                }
                array[i + arrayIndex - skipped] = new KeyValuePair<TKey, TValue>(m_Slots[i].Key, m_Slots[i].Value);
            }
        }

        public void Dispose()
        {
            if (m_Slots != null)
            {
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(m_Slots, true);
                ArrayPool<sbyte>.Shared.Return(m_ControlBytes);
                m_Slots = null!;
                m_ControlBytes = null!;
            }
            m_Size = 0;
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => GetEnumerator();

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (GetEntryIndex(item.Key) is int index && index >= 0 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Slots[index].Value))
            {
                return RemoveEntry(item.Key);
            }

            return false;
        }

        public bool Remove(TKey key)
        {
            return RemoveEntry(key);
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (GetEntryIndex(key) is int index && index >= 0)
            {
                value = m_Slots[index].Value;
                return true;
            }
            else
            {
                value = default!;
                return false;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();


        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        void IDictionary.Add(object key, object? value)
        {
            if (key is not TKey tkey)
                throw new InvalidCastException(nameof(key));
            if (value is not TValue tvalue)
                throw new InvalidCastException(nameof(value));

            Add(tkey, tvalue);
        }

        bool IDictionary.Contains(object key)
        {
            if (key is not TKey tkey)
                throw new InvalidCastException(nameof(key));

            return ContainsKey(tkey);
        }

        IDictionaryEnumerator IDictionary.GetEnumerator() => GetEnumerator();

        void IDictionary.Remove(object key)
        {
            if (key is not TKey tkey)
                throw new InvalidCastException(nameof(key));

            Remove(tkey);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array is not KeyValuePair<TKey, TValue>[] tarray)
                throw new InvalidCastException(nameof(array));

            CopyTo(tarray, index);
        }

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private readonly SwissTableDictionary<TKey, TValue> m_Parent;
            private int m_Index;

            internal Enumerator(SwissTableDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
                m_Index = -1;
            }

            public KeyValuePair<TKey, TValue> Current => m_Parent.m_Slots[m_Index];

            public DictionaryEntry Entry => new DictionaryEntry(Current.Key, Current.Value);

            public object Key => Current.Key;

            public object? Value => Current.Value;

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                while ((uint)++m_Index < (uint)m_Parent.m_Slots.Length)
                {
                    if (!IsEmptyOrDeleted(m_Parent.m_ControlBytes[m_Index]))
                    {
                        return true;
                    }
                }
                return false;
            }

            public void Reset() => throw new NotSupportedException();
        }





#if false
        public TValue this[TKey key]
        {
            get => GetEntryIndex(key) is int index && index >= 0 ? m_Slots[index].Value : throw new KeyNotFoundException();
            set => AddEntry(key, value, true);
        }

        public int Count => m_Size;

        public bool IsReadOnly => false;

        public ICollection<TKey> Keys => m_Slots.Select(s => s.Key).ToArray();          // TODO

        public ICollection<TValue> Values => m_Slots.Select(s => s.Value).ToArray();          // TODO

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!AddEntry(item.Key, item.Value, false))
                throw new InvalidOperationException("key duplicated");
        }

        public void Add(TKey key, TValue value)
        {
            if (!AddEntry(key, value, false))
                throw new InvalidOperationException("key duplicated");
        }

        public void Clear()
        {
            ClearTable();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return GetEntryIndex(item.Key) is int index && index >= 0 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Slots[index].Value);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key) >= 0;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
            ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(m_Slots, true);
            ArrayPool<sbyte>.Shared.Return(m_ControlBytes);
            m_Slots = null!;
            m_ControlBytes = null!;
            m_Size = 0;
            m_GrowthLeft = 0;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            throw new System.NotImplementedException();
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (GetEntryIndex(item.Key) is int index && index >= 0 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Slots[index].Value))
            {
                return RemoveEntry(item.Key);
            }

            return false;
        }

        public bool Remove(TKey key)
        {
            return RemoveEntry(key);
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (GetEntryIndex(key) is int index && index >= 0)
            {
                value = m_Slots[index].Value;
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
            throw new System.NotImplementedException();
        }

#endif


        public override string ToString()
        {
            return $"{m_Size} items";
        }
    }
}