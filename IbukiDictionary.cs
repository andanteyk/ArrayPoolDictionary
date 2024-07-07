using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if true

namespace ParallelDungeon.Rogue.Serialization
{

    public sealed class IbukiDictionary<TKey, TValue> :
            ICollection<KeyValuePair<TKey, TValue>>,
            IDictionary<TKey, TValue>,
            IEnumerable<KeyValuePair<TKey, TValue>>,
            IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
            IReadOnlyDictionary<TKey, TValue>,
            IDictionary,
            IDisposable
            where TKey : notnull
    {

        private readonly record struct Metadata(int ValueIndex)
        {
            public override string ToString()
            {
                return $"value=#{ValueIndex}";
            }
        }

        private KeyValuePair<TKey, TValue>[] m_KeyValues;
        private Metadata[] m_Metadata;
        private byte[] m_Fingerprints;

        private int m_Size;
        private int m_GrowthLeft;
        private IEqualityComparer<TKey>? m_Comparer;


        private const byte Empty = 0x80;
        private const byte Deleted = 0xfe;

        // load factor = num/den
        private const long LoadFactorNum = 25;
        private const long LoadFactorDen = 32;


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
        private static ref T At<T>(T[] array, int i)
        {
#if !DEBUG
            return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), i);
#else
            return ref array[i];
#endif
        }

        private int GetEntryIndex(TKey key)
        {
            int metadataIndex = FindMetadata(key);
            if (metadataIndex == -1)
            {
                return -1;
            }

            return At(m_Metadata, metadataIndex).ValueIndex;
        }

        private int FindMetadata(TKey key)
        {
            var fingerprints = m_Fingerprints;
            var metadata = m_Metadata;
            var keyValues = m_KeyValues;
            var comparer = m_Comparer;

            int hashCode = GetHashCode(key, comparer);
            int rootIndex = HashCodeToRootIndex(hashCode, fingerprints.Length);

            for (int rootOffset = 0; rootOffset < fingerprints.Length; rootOffset += 8)
            {
                int currentRoot = (rootIndex + rootOffset) & (fingerprints.Length - 1);

                ulong group = GetGroup(currentRoot, fingerprints);
                ulong match = MatchGroup(group, HashCodeToFingerprint(hashCode));

                while (match != 0)
                {
                    int trailingBitPosition = TrailingZeroCount(match) >> 3;
                    int index = (currentRoot + trailingBitPosition) & (fingerprints.Length - 1);
                    int kvpIndex = At(metadata, index).ValueIndex;

                    if (KeysAreEqual(key, At(keyValues, kvpIndex).Key, comparer))
                    {
                        return index;
                    }

                    match &= match - 1;
                }

                if (MatchEmptyGroup(group) != 0)
                {
                    return -1;
                }
            }

            Debug.Fail("never reach here");
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MatchGroup(ulong group, byte fingerprint)
        {
            ulong msbs = 0x8080808080808080;
            ulong lsbs = 0x0101010101010101;
            ulong x = group ^ (lsbs * fingerprint);
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
        private static int HashCodeToRootIndex(int hashCode, int length)
        {
            // TODO: 747796405 is 30 bits!
            return (int)((uint)(hashCode * 747796405) >> (32 - TrailingZeroCount((ulong)length)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte HashCodeToFingerprint(int hashCode)
        {
            return (byte)(hashCode & 0x7f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetHashCode(TKey key, IEqualityComparer<TKey>? comparer)
        {
            if (typeof(TKey).IsValueType)
            {
                if (comparer == null)
                {
                    return EqualityComparer<TKey>.Default.GetHashCode(key);
                }
                else
                {
                    return comparer.GetHashCode(key);
                }
            }
            else
            {
                return comparer!.GetHashCode(key);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool KeysAreEqual(TKey key1, TKey key2, IEqualityComparer<TKey>? comparer)
        {
            if (typeof(TKey).IsValueType)
            {
                if (comparer == null)
                {
                    return EqualityComparer<TKey>.Default.Equals(key1, key2);
                }
                else
                {
                    return comparer.Equals(key1, key2);
                }
            }
            else
            {
                return comparer!.Equals(key1, key2);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetGroup(int currentRoot, ReadOnlySpan<byte> fingerprints)
        {
            var groupSpan = MemoryMarshal.Cast<byte, ulong>(fingerprints[currentRoot..]);
            ulong group;

            if (groupSpan.Length > 0)
            {
                group = groupSpan[0];
            }
            else
            {
                groupSpan = MemoryMarshal.Cast<byte, ulong>(fingerprints);
                group = groupSpan[^1] >> ((currentRoot & 7) << 3) | groupSpan[0] << -((currentRoot & 7) << 3);
            }

            return group;
        }

        private bool AddEntry(TKey key, TValue value, bool overwrite)
        {
            var fingerprints = m_Fingerprints;
            var metadata = m_Metadata;
            var keyValues = m_KeyValues;
            var comparer = m_Comparer;
            int size = m_Size;

            int hashCode = GetHashCode(key, comparer);
            int rootIndex = HashCodeToRootIndex(hashCode, fingerprints.Length);

            for (int rootOffset = 0; rootOffset < fingerprints.Length; rootOffset += 8)
            {
                int currentRoot = (rootIndex + rootOffset) & (fingerprints.Length - 1);

                ulong group = GetGroup(currentRoot, fingerprints);
                var match = MatchGroup(group, HashCodeToFingerprint(hashCode));

                while (match != 0)
                {
                    int trailingBitPosition = TrailingZeroCount(match) >> 3;
                    int index = (currentRoot + trailingBitPosition) & (fingerprints.Length - 1);

                    if (KeysAreEqual(key, At(keyValues, At(metadata, index).ValueIndex).Key, comparer))
                    {
                        if (overwrite)
                        {
                            At(keyValues, At(metadata, index).ValueIndex) = new KeyValuePair<TKey, TValue>(key, value);
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    match &= match - 1;
                }

                match = MatchEmptyOrDeletedGroup(group);
                if (match != 0)
                {
                    int trailingBitPosition = TrailingZeroCount(match) >> 3;
                    int index = (currentRoot + trailingBitPosition) & (fingerprints.Length - 1);

                    m_GrowthLeft -= IsEmpty(At(fingerprints, index)) ? 1 : 0;
                    At(fingerprints, index) = HashCodeToFingerprint(hashCode);
                    At(metadata, index) = new Metadata(size);
                    At(keyValues, size) = new KeyValuePair<TKey, TValue>(key, value);
                    m_Size++;


                    if (m_GrowthLeft == 0)
                    {
                        RehashAndGrowIfNecessary();
                    }
                    return true;
                }
            }

            Debug.Fail("never reach here");
            return false;
        }

        private void RehashAndGrowIfNecessary()
        {
            var fingerprints = m_Fingerprints;

            if (m_Size * LoadFactorDen <= fingerprints.Length * LoadFactorNum)
            {
                DropDeletesWithoutResize();
            }
            else
            {
                Resize(fingerprints.Length << 1);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFull(byte fingerprint) => fingerprint < 0x80;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetGrowthLeft()
        {
            int capacity = m_Fingerprints.Length;
            m_GrowthLeft = (capacity - capacity / 8) - m_Size;
        }

        private void Resize(int newCapacity)
        {
            Debug.Assert((newCapacity & (newCapacity - 1)) == 0);

            var comparer = m_Comparer;

            var oldKeyValues = m_KeyValues;
            var oldMetadata = m_Metadata;
            var oldFingerprints = m_Fingerprints;

            var newKeyValues = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(newCapacity);
            var newMetadata = ArrayPool<Metadata>.Shared.Rent(newCapacity);
            newMetadata.AsSpan().Fill(new Metadata(-1));
            var newFingerprints = ArrayPool<byte>.Shared.Rent(newCapacity);
            newFingerprints.AsSpan().Fill(Empty);

            int size = 0;


            for (int i = 0; i < oldFingerprints.Length; i++)
            {
                if (IsFull(At(oldFingerprints, i)))
                {
                    var key = At(oldKeyValues, At(oldMetadata, i).ValueIndex).Key;

                    int hashCode = GetHashCode(key, comparer);
                    int rootIndex = HashCodeToRootIndex(hashCode, newFingerprints.Length);

                    for (int rootOffset = 0; rootOffset < newFingerprints.Length; rootOffset += 8)
                    {
                        int currentRoot = (rootIndex + rootOffset) & (newFingerprints.Length - 1);

                        ulong group = GetGroup(currentRoot, newFingerprints);
                        ulong match = MatchEmptyGroup(group);
                        if (match != 0)
                        {
                            int trailingBitPosition = TrailingZeroCount(match) >> 3;
                            int index = (currentRoot + trailingBitPosition) & (newFingerprints.Length - 1);

                            At(newFingerprints, index) = HashCodeToFingerprint(hashCode);
                            At(newMetadata, index) = new Metadata(size);
                            At(newKeyValues, size) = At(oldKeyValues, At(oldMetadata, i).ValueIndex);
                            size++;
                            break;
                        }
                    }
                }
            }


            m_KeyValues = newKeyValues;
            m_Metadata = newMetadata;
            m_Fingerprints = newFingerprints;
            ResetGrowthLeft();

            ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(oldKeyValues);
            ArrayPool<Metadata>.Shared.Return(oldMetadata);
            ArrayPool<byte>.Shared.Return(oldFingerprints);
        }

        private void DropDeletesWithoutResize()
        {
            var keyValues = m_KeyValues;
            var metadata = m_Metadata;
            var fingerprints = m_Fingerprints;
            var comparer = m_Comparer;

            var groups = MemoryMarshal.Cast<byte, ulong>(fingerprints);

            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = ConvertSpecialToEmptyAndFullToDeleted(groups[i]);
            }

            for (int i = 0; i < fingerprints.Length; i++)
            {
                if (!IsDeleted(At(fingerprints, i)))
                {
                    continue;
                }


                // TODO: understanding
                int hashCode = GetHashCode(At(keyValues, At(metadata, i).ValueIndex).Key, comparer);
                int rootIndex = HashCodeToRootIndex(hashCode, fingerprints.Length);

                for (int rootOffset = 0; rootOffset < fingerprints.Length; rootOffset += 8)
                {
                    int currentRoot = (rootIndex + rootOffset) & (fingerprints.Length - 1);

                    ulong group = GetGroup(currentRoot, fingerprints);
                    var match = MatchEmptyOrDeletedGroup(group);
                    if (match != 0)
                    {
                        int trailingBitPosition = TrailingZeroCount(match) >> 3;
                        int index = currentRoot + trailingBitPosition;

                        if (((index - rootIndex) & (fingerprints.Length - 1)) / 8 ==
                            ((i - rootIndex) & (fingerprints.Length - 1)))
                        {
                            At(fingerprints, i) = HashCodeToFingerprint(hashCode);
                        }
                        else if (IsEmpty(At(fingerprints, index)))
                        {
                            At(fingerprints, index) = HashCodeToFingerprint(hashCode);
                            At(metadata, index) = At(metadata, i);
                            At(fingerprints, i) = Empty;
                        }
                        else
                        {
                            At(fingerprints, index) = HashCodeToFingerprint(hashCode);
                            (At(metadata, index), At(metadata, i)) = (At(metadata, i), At(metadata, index));
                            i--;
                        }

                        break;
                    }
                }
            }

            ResetGrowthLeft();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEmpty(byte fingerprint) => fingerprint == Empty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDeleted(byte fingerprint) => fingerprint == Deleted;

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

        private bool RemoveEntry(TKey key)
        {
            int index = FindMetadata(key);
            if (index == -1)
            {
                return false;
            }

            var keyValues = m_KeyValues;
            var metadata = m_Metadata;
            var fingerprints = m_Fingerprints;
            int size = m_Size - 1;

            int indexBefore = (index - 8) & (fingerprints.Length - 1);

            ulong emptyAfter = MatchEmptyGroup(GetGroup(index, fingerprints));
            ulong emptyBefore = MatchEmptyGroup(GetGroup(indexBefore, fingerprints));

            bool wasNeverFull = emptyBefore != 0 && emptyAfter != 0 &&
                (TrailingZeroCount(emptyAfter) >> 3) + (LeadingZeroCount(emptyBefore) >> 3) < 8;

            At(fingerprints, index) = wasNeverFull ? Empty : Deleted;
            int prevKvpIndex = At(metadata, index).ValueIndex;
            At(metadata, index) = new Metadata(-1);

            int sizeIndex = FindMetadata(At(keyValues, size).Key);
            if (sizeIndex != -1)
            {
                At(metadata, sizeIndex) = new Metadata(prevKvpIndex);
                At(keyValues, prevKvpIndex) = At(keyValues, size);
            }
            m_Size = size;
            m_GrowthLeft += wasNeverFull ? 1 : 0;
            return true;
        }

        private void ClearTable()
        {
            m_Metadata.AsSpan().Fill(new Metadata(-1));
            m_Fingerprints.AsSpan().Fill(Empty);
            m_Size = 0;
            ResetGrowthLeft();
        }






        public IbukiDictionary() : this(8) { }
        public IbukiDictionary(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            capacity = Math.Max(capacity, 8);

            m_KeyValues = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(capacity);
            m_Metadata = ArrayPool<Metadata>.Shared.Rent(capacity);
            m_Metadata.AsSpan().Fill(new Metadata(-1));
            m_Fingerprints = ArrayPool<byte>.Shared.Rent(capacity);
            m_Fingerprints.AsSpan().Fill(Empty);

            if (typeof(TKey).IsValueType)
            {
                m_Comparer = null;
            }
            else
            {
                m_Comparer = EqualityComparer<TKey>.Default;
            }

            m_Size = 0;
            ResetGrowthLeft();
        }

        // TODO: opt.
        public IbukiDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary.Count)
        {
            foreach (var pair in dictionary)
            {
                AddEntry(pair.Key, pair.Value, false);
            }
        }
        public IbukiDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(source.Count())
        {
            foreach (var pair in source)
            {
                AddEntry(pair.Key, pair.Value, false);
            }
        }














        public TValue this[TKey key]
        {
            get => GetEntryIndex(key) is int index && index >= 0 ? At(m_KeyValues, index).Value : throw new KeyNotFoundException();
            set => AddEntry(key, value, true);
        }

        public int Count => m_Size;

        public bool IsReadOnly => false;

        public readonly struct KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly IbukiDictionary<TKey, TValue> m_Parent;

            internal KeyCollection(IbukiDictionary<TKey, TValue> parent)
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

                for (int i = 0; i < m_Parent.m_Size; i++)
                {
                    array[arrayIndex + i] = m_Parent.m_KeyValues[i].Key;
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

                for (int i = 0; i < m_Parent.m_Size; i++)
                {
                    tkey[i + index] = m_Parent.m_KeyValues[i].Key;
                }
            }

            public struct Enumerator : IEnumerator<TKey>
            {
                private readonly IbukiDictionary<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(IbukiDictionary<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                }

                public TKey Current => m_Parent.m_KeyValues[m_Index].Key;

                object IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    return ++m_Index < m_Parent.m_Size;
                }

                public void Reset() => throw new NotSupportedException();
            }
        }

        public readonly struct ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private readonly IbukiDictionary<TKey, TValue> m_Parent;

            internal ValueCollection(IbukiDictionary<TKey, TValue> parent)
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

                for (int i = 0; i < m_Parent.m_Size; i++)
                {
                    array[arrayIndex + i] = m_Parent.m_KeyValues[i].Value;
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

                for (int i = 0; i < m_Parent.m_Size; i++)
                {
                    tvalue[i + index] = m_Parent.m_KeyValues[i].Value;
                }
            }

            public struct Enumerator : IEnumerator<TValue>
            {
                private readonly IbukiDictionary<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(IbukiDictionary<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                }

                public TValue Current => m_Parent.m_KeyValues[m_Index].Value;

                object? IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    return ++m_Index < m_Parent.m_Size;
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
                EqualityComparer<TValue>.Default.Equals(item.Value, m_KeyValues[index].Value);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key) >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            foreach (var pair in m_KeyValues.AsSpan(..m_Size))
            {
                if (EqualityComparer<TValue>.Default.Equals(pair.Value, value))
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

            m_KeyValues.AsSpan(..m_Size).CopyTo(array.AsSpan(arrayIndex..));
        }

        public void Dispose()
        {
            if (m_KeyValues != null)
            {
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(m_KeyValues, true);
                m_KeyValues = null!;
            }
            if (m_Metadata != null)
            {
                ArrayPool<Metadata>.Shared.Return(m_Metadata);
                m_Metadata = null!;
            }
            m_Size = 0;
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => GetEnumerator();

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (GetEntryIndex(item.Key) is int index && index >= 0 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_KeyValues[index].Value))
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
                value = m_KeyValues[index].Value;
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
            private readonly IbukiDictionary<TKey, TValue> m_Parent;
            private int m_Index;

            internal Enumerator(IbukiDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
                m_Index = -1;
            }

            public KeyValuePair<TKey, TValue> Current => m_Parent.m_KeyValues[m_Index];

            public DictionaryEntry Entry => new DictionaryEntry(Current.Key, Current.Value);

            public object Key => Current.Key;

            public object? Value => Current.Value;

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                return ++m_Index < m_Parent.m_Size;
            }

            public void Reset() => throw new NotSupportedException();
        }

        public override string ToString()
        {
            return $"{m_Size} items";
        }
    }
}

#endif
