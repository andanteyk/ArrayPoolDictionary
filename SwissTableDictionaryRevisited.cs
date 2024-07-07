using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ParallelDungeon.Rogue.Serialization
{
    public class SwissTableDictionaryRevisited<TKey, TValue> :
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
        private const sbyte Sentinel = -1;

        private const int GroupWidth = 8;
        private const int GroupShift = 3;



        private sbyte[] m_ControlBytes;
        private KeyValuePair<TKey, TValue>[] m_Slots;
        private int m_Size;
        private int m_Capacity;
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
            int capacityToGrowth = (GroupWidth == 8 && m_Capacity == 7) ? 6 : (m_Capacity - m_Capacity / 8);

            m_GrowthLeft = capacityToGrowth - m_Size;
        }




        private int GetEntryIndex(TKey key)
        {
            return Find(key);
        }

        private bool AddEntry(TKey key, TValue value, bool overwrite)
        {
            var result = FindOrPrepareInsert(key);
            if (result.inserted || overwrite)
            {
                m_Slots[result.index] = new KeyValuePair<TKey, TValue>(key, value);
                return true;
            }

            return false;
        }

        private bool RemoveEntry(TKey key)
        {
            return Erase(key);
        }



        // CWISS_RawTable_EraseMetaOnly
        private void RemoveAt(int index)
        {
            Debug.Assert(IsFull(m_ControlBytes[index]));

            m_Size--;

            int indexBefore = (index - GroupWidth) & m_Capacity;

            var emptyAfter = MatchEmptyGroup(MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes.AsSpan(index..))[0]);
            var emptyBefore = MatchEmptyGroup(MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes.AsSpan(indexBefore..))[0]);

            bool wasNeverFull = emptyBefore != 0 && emptyAfter != 0 &&
                (TrailingZeroCount(emptyAfter) >> GroupShift) + (LeadingZeroCount(emptyBefore) >> GroupShift) < GroupWidth;

            SetControlByte(index, wasNeverFull ? Empty : Deleted, m_Capacity);
            m_GrowthLeft += wasNeverFull ? 1 : 0;
        }


        // CWISS_NumClonedBytes
        private const int SizeOfClonedBytes = GroupWidth - 1;

        // CWISS_RawTable_InitializeSlots
        [MemberNotNull(nameof(m_ControlBytes), nameof(m_Slots))]
        private void InitializeSlots(int capacity)
        {
            Debug.Assert(capacity > 0);

            int sizeOfControlBytes = capacity + 1 + SizeOfClonedBytes;

            m_ControlBytes = ArrayPool<sbyte>.Shared.Rent(sizeOfControlBytes);
            m_ControlBytes.AsSpan(..sizeOfControlBytes).Fill(Empty);
            m_ControlBytes[capacity] = Sentinel;

            m_Slots = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(capacity);

            ResetGrowthLeft();
        }


        // CWISS_RawTable_rehash_and_grow_if_necessary
        private void RehashAndGrowIfNecessary()
        {
            if (m_Capacity == 0)
            {
                Resize(1);
            }
            else if (m_Capacity > GroupWidth && m_Size * 32L <= m_Capacity * 25L)
            {
                DropDeletesWithoutResize();
            }
            else
            {
                Resize(m_Capacity * 2 + 1);
            }
        }

        // CWISS_RawTable_Resize
        private void Resize(int newCapacity)
        {
            Debug.Assert(((newCapacity + 1) & newCapacity) == 0 && newCapacity > 0);

            int oldCapacity = m_Capacity;
            m_Capacity = newCapacity;
            var oldControlBytes = m_ControlBytes;
            var oldSlots = m_Slots;
            InitializeSlots(newCapacity);

            for (int i = 0; i < oldCapacity; i++)
            {
                if (IsFull(oldControlBytes[i]))
                {
                    int hash = EqualityComparer<TKey>.Default.GetHashCode(oldSlots[i].Key);
                    int target = FindFirstNonFull(hash);

                    SetControlByte(target, Hash2(hash), m_Capacity);
                    m_Slots[target] = oldSlots[i];
                }
            }

            if (oldCapacity != 0)
            {
                ArrayPool<sbyte>.Shared.Return(oldControlBytes);
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(oldSlots);
            }
        }

        // CWISS_IsSmall
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSmall(int capacity)
        {
            return capacity < GroupWidth - 1;
        }

        // CWISS_RawTable_DropDeletesWithoutResize
        private void DropDeletesWithoutResize()
        {
            Debug.Assert(((m_Capacity + 1) & m_Capacity) == 0 && m_Capacity > 0);
            Debug.Assert(!IsSmall(m_Capacity));

            ConvertDeletedToEmptyAndFullToDeleted(m_Capacity);

            for (int i = 0; i < m_Capacity; i++)
            {
                if (!IsDeleted(m_ControlBytes[i]))
                {
                    continue;
                }

                int hash = EqualityComparer<TKey>.Default.GetHashCode(m_Slots[i].Key);
                int target = FindFirstNonFull(hash);

                int probeOffset = Hash1(hash) & m_Capacity;
                if (((target - probeOffset) & m_Capacity) / GroupWidth ==
                    ((i - probeOffset) & m_Capacity) / GroupWidth)
                {
                    SetControlByte(i, Hash2(hash), m_Capacity);
                    continue;
                }

                if (IsEmpty(m_ControlBytes[target]))
                {
                    SetControlByte(target, Hash2(hash), m_Capacity);
                    m_Slots[i] = m_Slots[target];
                    SetControlByte(i, Empty, m_Capacity);
                }
                else
                {
                    Debug.Assert(IsDeleted(m_ControlBytes[target]));
                    SetControlByte(target, Hash2(hash), m_Capacity);

                    (m_Slots[i], m_Slots[target]) = (m_Slots[target], m_Slots[i]);
                    i--;
                }
            }

            ResetGrowthLeft();
        }

        // CWISS_ConvertDeletedToEmptyAndFullToDeleted
        private void ConvertDeletedToEmptyAndFullToDeleted(int capacity)
        {
            Debug.Assert(m_ControlBytes[capacity] == Sentinel);
            Debug.Assert(((capacity + 1) & capacity) == 0 && capacity > 0);

            var ulongs = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes);
            for (int i = 0; i < capacity; i += GroupWidth)
            {
                ulongs[i >> 3] = ConvertSpecialToEmptyAndFullToDeleted(ulongs[i >> 3]);
            }

            m_ControlBytes.AsSpan(..SizeOfClonedBytes).CopyTo(m_ControlBytes.AsSpan((capacity + 1)..));
            m_ControlBytes[capacity] = Sentinel;
        }

        // CWISS_SetCtrl
        private void SetControlByte(int index, sbyte controlByte, int capacity)
        {
            Debug.Assert(index < capacity);

            int mirrored = ((index - SizeOfClonedBytes) & capacity) +
                (SizeOfClonedBytes & capacity);

            m_ControlBytes[index] = controlByte;
            m_ControlBytes[mirrored] = controlByte;
        }

        // CWISS_FindFirstNonFull
        private int FindFirstNonFull(int hash)
        {
            int probeSeqMask = m_Capacity;
            int probeSeqOffset = Hash1(hash) & probeSeqMask;
            int probeSeqIndex = 0;

            while (true)
            {
                var group = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes.AsSpan(probeSeqOffset..))[0];
                var match = MatchEmptyOrDeletedGroup(group);
                if (match != 0)
                {
                    // TODO: debug build shuffing?
                    int result = (probeSeqOffset + (TrailingZeroCount(match) >> GroupShift)) & probeSeqMask;

                    Debug.Assert(IsEmptyOrDeleted(m_ControlBytes[result]));
                    return result;
                }

                // CWISS_ProbeSeq_next
                probeSeqIndex += GroupWidth;
                probeSeqOffset += probeSeqIndex;
                probeSeqOffset &= probeSeqMask;

                Debug.Assert(probeSeqIndex <= m_Capacity);
            }
        }

        // CWISS_RawTable_PrepareInsert
        private int PrepareInsert(int hash)
        {
            int target = FindFirstNonFull(hash);

            if (m_GrowthLeft == 0 && !IsDeleted(m_ControlBytes[target]))
            {
                RehashAndGrowIfNecessary();
                target = FindFirstNonFull(hash);
            }

            m_Size++;
            m_GrowthLeft -= IsEmpty(m_ControlBytes[target]) ? 1 : 0;
            SetControlByte(target, Hash2(hash), m_Capacity);

            return target;
        }

        // CWISS_Group_Match
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GroupMatch(ulong group, sbyte hash2)
        {
            ulong msbs = 0x8080808080808080;
            ulong lsbs = 0x0101010101010101;
            ulong x = group ^ (lsbs * (byte)hash2);
            return (x - lsbs) & ~x & msbs;
        }

        // CWISS_BitMask_next
        private static bool BitMaskNext(ref ulong bitmask, out int bit)
        {
            if (bitmask == 0)
            {
                bit = 0;
                return false;
            }

            bit = TrailingZeroCount(bitmask) >> GroupShift;
            bitmask &= bitmask - 1;
            return true;
        }

        // CWISS_RawTable_FindOrPrepareInsert
        private (int index, bool inserted) FindOrPrepareInsert(TKey key)
        {
            int hash = EqualityComparer<TKey>.Default.GetHashCode(key);

            int probeSeqMask = m_Capacity;
            int probeSeqOffset = Hash1(hash) & probeSeqMask;
            int probeSeqIndex = 0;

            while (true)
            {
                var group = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes.AsSpan(probeSeqOffset..))[0];
                var match = GroupMatch(group, Hash2(hash));

                while (BitMaskNext(ref match, out int i))
                {
                    // CWISS_ProbeSeq_offset
                    int index = (probeSeqOffset + i) & probeSeqMask;
                    if (EqualityComparer<TKey>.Default.Equals(key, m_Slots[index].Key))
                    {
                        return (index, false);
                    }
                }

                if (GroupMatch(group, Empty) != 0)
                {
                    break;
                }

                // CWISS_ProbeSeq_next
                probeSeqIndex += GroupWidth;
                probeSeqOffset += probeSeqIndex;
                probeSeqOffset &= probeSeqMask;

                Debug.Assert(probeSeqIndex <= m_Capacity);
            }

            return (PrepareInsert(hash), true);
        }

        // CWISS_RawTable_new
        [MemberNotNull(nameof(m_ControlBytes), nameof(m_Slots))]
        private void CreateTable(int capacity)
        {
            m_Capacity = capacity != 0 ? (int)(~0u >> (LeadingZeroCount((ulong)capacity) - 32)) : 1;
            InitializeSlots(m_Capacity);
        }

        // CWISS_RawTable_DestroySlots
        private void DestroySlots()
        {
            if (m_Capacity == 0)
            {
                return;
            }

            ArrayPool<sbyte>.Shared.Return(m_ControlBytes);
            m_ControlBytes = ArrayPool<sbyte>.Shared.Rent(16);

            m_ControlBytes[0] = Sentinel;
            m_ControlBytes[1] = Empty;
            m_ControlBytes[2] = Empty;
            m_ControlBytes[3] = Empty;
            m_ControlBytes[4] = Empty;
            m_ControlBytes[5] = Empty;
            m_ControlBytes[6] = Empty;
            m_ControlBytes[7] = Empty;
            m_ControlBytes[8] = Empty;
            m_ControlBytes[9] = Empty;
            m_ControlBytes[10] = Empty;
            m_ControlBytes[11] = Empty;
            m_ControlBytes[12] = Empty;
            m_ControlBytes[13] = Empty;
            m_ControlBytes[14] = Empty;
            m_ControlBytes[15] = Empty;

            ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(m_Slots);
            m_Slots = null!;     // ?
            m_Size = 0;
            m_Capacity = 0;
            m_GrowthLeft = 0;
        }

        // CWISS_RawTable_clear
        private void ClearTable()
        {
            if (m_Capacity > 127)
            {
                DestroySlots();
            }
            else if (m_Capacity > 0)
            {
                m_Size = 0;
                m_ControlBytes.AsSpan().Fill(Empty);
                m_ControlBytes[m_Capacity] = Sentinel;
                ResetGrowthLeft();
            }
        }

        // CWISS_RawTable_find
        // CWISS_RawTable_find_hinted
        private int Find(TKey key)
        {
            int hash = EqualityComparer<TKey>.Default.GetHashCode(key);

            int probeSeqMask = m_Capacity;
            int probeSeqOffset = Hash1(hash) & probeSeqMask;
            int probeSeqIndex = 0;

            while (true)
            {
                var group = MemoryMarshal.Cast<sbyte, ulong>(m_ControlBytes.AsSpan(probeSeqOffset..))[0];
                var match = GroupMatch(group, Hash2(hash));

                while (BitMaskNext(ref match, out int i))
                {
                    int slotOffset = (probeSeqOffset + i) & probeSeqMask;

                    if (EqualityComparer<TKey>.Default.Equals(key, m_Slots[slotOffset].Key))
                    {
                        return slotOffset;
                    }
                }

                if (MatchEmptyGroup(group) != 0)
                {
                    return -1;
                }

                probeSeqIndex += GroupWidth;
                probeSeqOffset += probeSeqIndex;
                probeSeqOffset &= probeSeqMask;

                Debug.Assert(probeSeqIndex <= m_Capacity);
            }
        }

        // CWISS_RawTable_erase
        // CWISS_RawTable_erase_at
        private bool Erase(TKey key)
        {
            int index = Find(key);
            if (index == -1)
                return false;

            Debug.Assert(IsFull(m_ControlBytes[index]));
            RemoveAt(index);

            return true;
        }







        public SwissTableDictionaryRevisited(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            CreateTable(capacity);
        }
        public SwissTableDictionaryRevisited() : this(16) { }

        public SwissTableDictionaryRevisited(IDictionary<TKey, TValue> dictionary) : this(dictionary.Count)
        {
            if (dictionary is SwissTableDictionaryRevisited<TKey, TValue> cloneSource)
            {
                /*// TODO: optimize
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
                */
            }

            foreach (var pair in dictionary)
            {
                AddEntry(pair.Key, pair.Value, false);
            }
        }

        public SwissTableDictionaryRevisited(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(source.Count())
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
            private readonly SwissTableDictionaryRevisited<TKey, TValue> m_Parent;

            internal KeyCollection(SwissTableDictionaryRevisited<TKey, TValue> parent)
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
                    if (!IsFull(m_Parent.m_ControlBytes[i]))
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
                    if (!IsFull(m_Parent.m_ControlBytes[i]))
                    {
                        skipped++;
                        continue;
                    }
                    tkey[index + i - skipped] = m_Parent.m_Slots[i].Key;
                }
            }

            public struct Enumerator : IEnumerator<TKey>
            {
                private readonly SwissTableDictionaryRevisited<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(SwissTableDictionaryRevisited<TKey, TValue> parent)
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
                        if (IsFull(m_Parent.m_ControlBytes[m_Index]))
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
            private readonly SwissTableDictionaryRevisited<TKey, TValue> m_Parent;

            internal ValueCollection(SwissTableDictionaryRevisited<TKey, TValue> parent)
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
                    if (!IsFull(m_Parent.m_ControlBytes[i]))
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
                    if (!IsFull(m_Parent.m_ControlBytes[i]))
                    {
                        skipped++;
                        continue;
                    }
                    tvalue[i + index - skipped] = m_Parent.m_Slots[i].Value;
                }
            }

            public struct Enumerator : IEnumerator<TValue>
            {
                private readonly SwissTableDictionaryRevisited<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(SwissTableDictionaryRevisited<TKey, TValue> parent)
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
                        if (IsFull(m_Parent.m_ControlBytes[m_Index]))
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
                if (!IsFull(m_ControlBytes[i]))
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
            private readonly SwissTableDictionaryRevisited<TKey, TValue> m_Parent;
            private int m_Index;

            internal Enumerator(SwissTableDictionaryRevisited<TKey, TValue> parent)
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
                    if (IsFull(m_Parent.m_ControlBytes[m_Index]))
                    {
                        return true;
                    }
                }
                return false;
            }

            public void Reset() => throw new NotSupportedException();
        }




        public override string ToString()
        {
            return $"{m_Size} items";
        }
    }
}