using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using MemoryPack;

namespace ParallelDungeon.Rogue.Serialization
{
    [MemoryPackable(GenerateType.Collection)]
    public sealed partial class AnkerlDictionaryMod<TKey, TValue> :
            ICollection<KeyValuePair<TKey, TValue>>,
            IDictionary<TKey, TValue>,
            IEnumerable<KeyValuePair<TKey, TValue>>,
            IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
            IReadOnlyDictionary<TKey, TValue>,
            IDictionary,
            IDisposable
            where TKey : notnull
    {

        private record struct Metadata(uint Fingerprint, int ValueIndex)
        {
            public override string ToString()
            {
                return $"dist={Fingerprint >> 8} fingerprint={Fingerprint & 0xff:x2} value=#{ValueIndex}";
            }
        }

        private KeyValuePair<TKey, TValue>[] m_Values;
        private Metadata[] m_Metadata;

        private readonly IEqualityComparer<TKey>? m_Comparer;
        private int m_Size;
        private int m_Version;

        private const int DistanceUnit = 0x100;
        private const float MaxLoadFactor = 0.8f;



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
            if (x == 0)
                return 64;

            int n = 0;
            if (x <= 0x00000000ffffffff) { n += 32; x <<= 32; }
            if (x <= 0x0000ffffffffffff) { n += 16; x <<= 16; }
            if (x <= 0x00ffffffffffffff) { n += 8; x <<= 8; }
            if (x <= 0x0fffffffffffffff) { n += 4; x <<= 4; }
            if (x <= 0x3fffffffffffffff) { n += 2; x <<= 2; }
            if (x <= 0x7fffffffffffffff) { n += 1; }
            return n;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashCodeToFingerprint(int hashCode)
        {
            return DistanceUnit | ((uint)hashCode & 0xff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashCodeToMetadataIndex(int hashCode, int shift)
        {
            return (int)((uint)hashCode >> shift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref T At<T>(T[] array, int index)
        {
#if !DEBUG
            return ref Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(array), index);
#else
            return ref array[index];
#endif
        }

        private int GetEntryIndex(TKey key)
        {
            uint fingerprint;
            int metadataIndex;

            if (typeof(TKey).IsValueType && m_Comparer == null)
            {
                int hashCode = key.GetHashCode();
                fingerprint = HashCodeToFingerprint(hashCode);
                metadataIndex = HashCodeToMetadataIndex(hashCode, 32 - TrailingZeroCount((ulong)m_Metadata.Length));


                var metadata = At(m_Metadata, metadataIndex);


                // unrolled loop #1
                if (fingerprint == metadata.Fingerprint)
                {
                    if (EqualityComparer<TKey>.Default.Equals(At(m_Values, metadata.ValueIndex).Key, key))
                    {
                        return metadata.ValueIndex;
                    }
                }
                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);
                metadata = At(m_Metadata, metadataIndex);

                // unrolled loop #2
                if (fingerprint == metadata.Fingerprint)
                {
                    if (EqualityComparer<TKey>.Default.Equals(At(m_Values, metadata.ValueIndex).Key, key))
                    {
                        return metadata.ValueIndex;
                    }
                }
                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);

            }
            else
            {
                int hashCode = m_Comparer!.GetHashCode(key);
                fingerprint = HashCodeToFingerprint(hashCode);
                metadataIndex = HashCodeToMetadataIndex(hashCode, 32 - TrailingZeroCount((ulong)m_Metadata.Length));


                var metadata = At(m_Metadata, metadataIndex);


                // unrolled loop #1
                if (fingerprint == metadata.Fingerprint)
                {
                    if (m_Comparer!.Equals(At(m_Values, metadata.ValueIndex).Key, key))
                    {
                        return metadata.ValueIndex;
                    }
                }
                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);
                metadata = At(m_Metadata, metadataIndex);

                // unrolled loop #2
                if (fingerprint == metadata.Fingerprint)
                {
                    if (m_Comparer!.Equals(At(m_Values, metadata.ValueIndex).Key, key))
                    {
                        return metadata.ValueIndex;
                    }
                }
                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);

            }

            return GetEntryIndexFallback(key, fingerprint, metadataIndex);
        }

        private int GetEntryIndexFallback(TKey key, uint fingerprint, int metadataIndex)
        {
            var metadata = At(m_Metadata, metadataIndex);

            while (true)
            {
                if (fingerprint == metadata.Fingerprint)
                {
                    if ((typeof(TKey).IsValueType && m_Comparer == null) ?
                        EqualityComparer<TKey>.Default.Equals(At(m_Values, metadata.ValueIndex).Key, key) :
                        m_Comparer!.Equals(At(m_Values, metadata.ValueIndex).Key, key))
                    {
                        return metadata.ValueIndex;
                    }
                }
                else if (fingerprint > metadata.Fingerprint)
                {
                    return -1;
                }

                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);
                metadata = At(m_Metadata, metadataIndex);
            }
        }

        private bool AddEntry(TKey key, TValue value, bool overwrite)
        {
            int hashCode = typeof(TKey).IsValueType && m_Comparer == null ?
               key.GetHashCode() :
                m_Comparer!.GetHashCode(key);
            uint fingerprint = HashCodeToFingerprint(hashCode);
            int metadataIndex = HashCodeToMetadataIndex(hashCode, 32 - TrailingZeroCount((ulong)m_Metadata.Length));

            while (fingerprint <= At(m_Metadata, metadataIndex).Fingerprint)
            {
                if (fingerprint == At(m_Metadata, metadataIndex).Fingerprint)
                {
                    if (typeof(TKey).IsValueType && m_Comparer == null ?
                        EqualityComparer<TKey>.Default.Equals(key, At(m_Values, At(m_Metadata, metadataIndex).ValueIndex).Key) :
                        m_Comparer!.Equals(key, At(m_Values, At(m_Metadata, metadataIndex).ValueIndex).Key))
                    {
                        if (overwrite)
                        {
                            At(m_Values, At(m_Metadata, metadataIndex).ValueIndex) = new KeyValuePair<TKey, TValue>(key, value);
                            m_Version++;
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);
            }


            m_Size++;
            m_Version++;
            At(m_Values, m_Size - 1) = new KeyValuePair<TKey, TValue>(key, value);
            PlaceAndShiftUp(new Metadata(fingerprint, m_Size - 1), metadataIndex);


            if (m_Size >= m_Metadata.Length * MaxLoadFactor)
            {
                Resize(m_Metadata.Length << 1);
            }

            return true;
        }

        private void Resize(int newCapacity)
        {
            var oldValues = m_Values;
            var oldMetadata = m_Metadata;

            m_Values = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(newCapacity);
            oldValues.AsSpan(..m_Size).CopyTo(m_Values.AsSpan());
            m_Metadata = ArrayPool<Metadata>.Shared.Rent(newCapacity);
            m_Metadata.AsSpan().Clear();

            for (int i = 0; i < m_Size; i++)
            {
                (uint fingerprint, int metadataIndex) = NextWhileLess(At(m_Values, i).Key);
                PlaceAndShiftUp(new Metadata(fingerprint, i), metadataIndex);
            }

            ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(oldValues, true);
            ArrayPool<Metadata>.Shared.Return(oldMetadata);
        }

        private (uint fingerprint, int metadataIndex) NextWhileLess(TKey key)
        {
            int hashCode = typeof(TKey).IsValueType && m_Comparer == null ?
                key.GetHashCode() :
                m_Comparer!.GetHashCode(key);
            uint fingerprint = HashCodeToFingerprint(hashCode);
            int metadataIndex = HashCodeToMetadataIndex(hashCode, 32 - TrailingZeroCount((ulong)m_Metadata.Length));

            while (fingerprint < At(m_Metadata, metadataIndex).Fingerprint)
            {
                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);
            }

            return (fingerprint, metadataIndex);
        }

        private void PlaceAndShiftUp(Metadata metadata, int metadataIndex)
        {
            while (At(m_Metadata, metadataIndex).Fingerprint != 0)
            {
                (metadata, At(m_Metadata, metadataIndex)) = (At(m_Metadata, metadataIndex), metadata);
                metadata.Fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);
            }
            At(m_Metadata, metadataIndex) = metadata;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int IncrementMetadataIndex(int metadataIndex)
        {
            return ++metadataIndex & (m_Metadata.Length - 1);
        }

        private bool RemoveEntry(TKey key)
        {
            (uint fingerprint, int metadataIndex) = NextWhileLess(key);

            while (fingerprint == At(m_Metadata, metadataIndex).Fingerprint &&
                !EqualityComparer<TKey>.Default.Equals(At(m_Values, At(m_Metadata, metadataIndex).ValueIndex).Key, key))
            {
                fingerprint += DistanceUnit;
                metadataIndex = IncrementMetadataIndex(metadataIndex);
            }

            if (fingerprint != At(m_Metadata, metadataIndex).Fingerprint)
            {
                return false;
            }

            RemoveAt(metadataIndex);
            return true;
        }

        private void RemoveAt(int metadataIndex)
        {
            int valueIndex = At(m_Metadata, metadataIndex).ValueIndex;

            int nextMetadataIndex = IncrementMetadataIndex(metadataIndex);
            while (At(m_Metadata, nextMetadataIndex).Fingerprint >= DistanceUnit * 2)
            {
                At(m_Metadata, metadataIndex) = new Metadata(At(m_Metadata, nextMetadataIndex).Fingerprint - DistanceUnit, At(m_Metadata, nextMetadataIndex).ValueIndex);
                (metadataIndex, nextMetadataIndex) = (nextMetadataIndex, IncrementMetadataIndex(nextMetadataIndex));
            }

            At(m_Metadata, metadataIndex) = new Metadata();


            if (valueIndex != m_Size - 1)
            {
                At(m_Values, valueIndex) = At(m_Values, m_Size - 1);

                int movingHashCode = typeof(TKey).IsValueType && m_Comparer == null ?
                    At(m_Values, valueIndex).Key.GetHashCode() :
                    m_Comparer!.GetHashCode(At(m_Values, valueIndex).Key);

                int movingMetadataIndex = HashCodeToMetadataIndex(movingHashCode, 64 - TrailingZeroCount((ulong)m_Metadata.Length));

                int valueIndexBack = m_Size - 1;
                while (valueIndexBack != At(m_Metadata, movingMetadataIndex).ValueIndex)
                {
                    movingMetadataIndex = IncrementMetadataIndex(movingMetadataIndex);
                }
                At(m_Metadata, movingMetadataIndex).ValueIndex = valueIndex;
            }

            m_Size--;
            m_Version++;
        }

        private void ClearTable()
        {
            m_Metadata.AsSpan().Clear();
            m_Size = 0;
            m_Version++;
        }






        public AnkerlDictionaryMod() : this(4) { }
        public AnkerlDictionaryMod(IEqualityComparer<TKey> comparer) : this(4, comparer) { }
        public AnkerlDictionaryMod(int capacity) : this(capacity, typeof(TKey).IsValueType ? null! : EqualityComparer<TKey>.Default) { }
        public AnkerlDictionaryMod(int capacity, IEqualityComparer<TKey> comparer)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (!typeof(TKey).IsValueType && comparer == null)
                throw new ArgumentNullException(nameof(comparer));

            capacity = Math.Max(capacity, 4);

            m_Values = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(capacity);
            m_Metadata = ArrayPool<Metadata>.Shared.Rent(capacity);
            m_Metadata.AsSpan().Clear();

            m_Size = 0;
            m_Version = 0;
            m_Comparer = comparer;
        }

        public AnkerlDictionaryMod(IDictionary<TKey, TValue> dictionary) : this(dictionary, typeof(TKey).IsValueType ? null! : EqualityComparer<TKey>.Default) { }
        public AnkerlDictionaryMod(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
        {
            if (dictionary is AnkerlDictionaryMod<TKey, TValue> cloneSource &&
                cloneSource.m_Comparer == comparer)
            {
                m_Values = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(cloneSource.m_Values.Length);
                m_Metadata = ArrayPool<Metadata>.Shared.Rent(cloneSource.m_Metadata.Length);
                cloneSource.m_Values.AsSpan().CopyTo(m_Values);
                cloneSource.m_Metadata.AsSpan().CopyTo(m_Metadata);

                m_Size = cloneSource.m_Size;
                m_Comparer = comparer;
                return;
            }


            int capacity = Math.Max(dictionary.Count, 4);
            m_Values = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(capacity);
            m_Metadata = ArrayPool<Metadata>.Shared.Rent(capacity);
            m_Metadata.AsSpan().Clear();

            m_Size = 0;
            m_Version = 0;
            m_Comparer = comparer;

            foreach (var pair in dictionary)
            {
                AddEntry(pair.Key, pair.Value, false);
            }
        }
        public AnkerlDictionaryMod(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(source.Count(), typeof(TKey).IsValueType ? null! : EqualityComparer<TKey>.Default) { }
        public AnkerlDictionaryMod(IEnumerable<KeyValuePair<TKey, TValue>> source, IEqualityComparer<TKey> comparer) : this(source.Count(), comparer)
        {
            foreach (var pair in source)
            {
                AddEntry(pair.Key, pair.Value, false);
            }
        }














        public TValue this[TKey key]
        {
            get => GetEntryIndex(key) is int index && index >= 0 ? At(m_Values, index).Value : throw new KeyNotFoundException();
            set => AddEntry(key, value, true);
        }

        public int Count => m_Size;

        public bool IsReadOnly => false;

        public readonly struct KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly AnkerlDictionaryMod<TKey, TValue> m_Parent;

            internal KeyCollection(AnkerlDictionaryMod<TKey, TValue> parent)
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
                    array[arrayIndex + i] = m_Parent.m_Values[i].Key;
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
                    tkey[i + index] = m_Parent.m_Values[i].Key;
                }
            }

            public struct Enumerator : IEnumerator<TKey>
            {
                private readonly AnkerlDictionaryMod<TKey, TValue> m_Parent;
                private int m_Index;
                private int m_Version;

                internal Enumerator(AnkerlDictionaryMod<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                    m_Version = parent.m_Version;
                }

                public TKey Current => m_Parent.m_Values[m_Index].Key;

                object IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (m_Parent.m_Version != m_Version)
                        throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

                    return ++m_Index < m_Parent.m_Size;
                }

                public void Reset() => throw new NotSupportedException();
            }
        }

        public readonly struct ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private readonly AnkerlDictionaryMod<TKey, TValue> m_Parent;

            internal ValueCollection(AnkerlDictionaryMod<TKey, TValue> parent)
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
                    array[arrayIndex + i] = m_Parent.m_Values[i].Value;
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
                    tvalue[i + index] = m_Parent.m_Values[i].Value;
                }
            }

            public struct Enumerator : IEnumerator<TValue>
            {
                private readonly AnkerlDictionaryMod<TKey, TValue> m_Parent;
                private int m_Index;
                private int m_Version;

                internal Enumerator(AnkerlDictionaryMod<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                    m_Version = parent.m_Version;
                }

                public TValue Current => m_Parent.m_Values[m_Index].Value;

                object? IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (m_Parent.m_Version != m_Version)
                        throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

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
                EqualityComparer<TValue>.Default.Equals(item.Value, At(m_Values, index).Value);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key) >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            foreach (var pair in m_Values.AsSpan(..m_Size))
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

            m_Values.AsSpan(..m_Size).CopyTo(array.AsSpan(arrayIndex..));
        }

        public void Dispose()
        {
            if (m_Values != null)
            {
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(m_Values, true);
                m_Values = null!;
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
                EqualityComparer<TValue>.Default.Equals(item.Value, At(m_Values, index).Value))
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
                value = At(m_Values, index).Value;
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
            private readonly AnkerlDictionaryMod<TKey, TValue> m_Parent;
            private int m_Index;
            private int m_Version;

            internal Enumerator(AnkerlDictionaryMod<TKey, TValue> parent)
            {
                m_Parent = parent;
                m_Index = -1;
                m_Version = m_Parent.m_Version;
            }

            public KeyValuePair<TKey, TValue> Current => m_Parent.m_Values[m_Index];

            public DictionaryEntry Entry => new DictionaryEntry(Current.Key, Current.Value);

            public object Key => Current.Key;

            public object? Value => Current.Value;

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (m_Parent.m_Version != m_Version)
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

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
