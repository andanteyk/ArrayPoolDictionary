using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ParallelDungeon.Rogue.Serialization
{
    public class RobinHoodDictionary<TKey, TValue> :
        ICollection<KeyValuePair<TKey, TValue>>,
        IDictionary<TKey, TValue>,
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
        IReadOnlyDictionary<TKey, TValue>,
        IDictionary,
        IDisposable
        where TKey : notnull
    {

        private readonly struct Entry
        {
            public readonly TKey Key;
            public readonly TValue Value;
            public readonly int IdealIndex;

            public Entry(TKey key, TValue value, int idealIndex)
            {
                Key = key;
                Value = value;
                IdealIndex = idealIndex;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsEmpty() => IdealIndex == -1;

            public override string ToString()
            {
                return $"[{IdealIndex}] {Key}: {Value}";
            }
        }

        private Entry[] m_Entries;
        private int m_Count;

        private int GetEntryIndex(TKey key)
        {
            int mask = m_Entries.Length - 1;

            int hashCode = key.GetHashCode();

            for (int offset = 0; offset < m_Entries.Length; offset++)
            {
                int index = (hashCode + offset) & mask;

                if (m_Entries[index].IsEmpty())
                    return -1;

                if (offset > ((index - m_Entries[index].IdealIndex + m_Entries.Length) & mask))
                    return -1;

                if (EqualityComparer<TKey>.Default.Equals(m_Entries[index].Key, key))
                {
                    return index;
                }
            }

            return -1;
        }

        private bool AddEntry(TKey key, TValue value, bool overwrite)
        {
            if (m_Count >= m_Entries.Length * 0.5)
            {
                Resize(m_Entries.Length << 1);
            }

            int hashCode = key.GetHashCode();
            int mask = m_Entries.Length - 1;

            for (int offset = 0; offset < m_Entries.Length; offset++)
            {
                int index = (hashCode + offset) & mask;

                if (m_Entries[index].IsEmpty())
                {
                    m_Entries[index] = new Entry(key, value, hashCode & mask);
                    m_Count++;
                    return true;
                }

                if (EqualityComparer<TKey>.Default.Equals(m_Entries[index].Key, key))
                {
                    if (overwrite)
                    {
                        m_Entries[index] = new Entry(key, value, hashCode & mask);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (offset > ((index - m_Entries[index].IdealIndex) & mask))
                {
                    Entry temp = new Entry(key, value, hashCode & mask);
                    (m_Entries[index], temp) = (temp, m_Entries[index]);
                    key = temp.Key;
                    value = temp.Value;
                    hashCode = key.GetHashCode();
                    offset = 0;
                    continue;
                }
            }

            return false;
        }

        private bool RemoveEntry(TKey key)
        {
            int hashCode = key.GetHashCode();
            int mask = m_Entries.Length - 1;

            bool running = false;

            for (int offset = 0; offset < m_Entries.Length; offset++)
            {
                int index = (hashCode + offset) & mask;

                if (!running)
                {
                    if (m_Entries[index].IsEmpty())
                        return false;

                    if (offset > ((index - m_Entries[index].IdealIndex + m_Entries.Length) & mask))
                        return false;

                    if (EqualityComparer<TKey>.Default.Equals(m_Entries[index].Key, key))
                    {
                        running = true;
                    }
                }

                if (running)
                {
                    int next = (hashCode + offset + 1) & mask;
                    if (m_Entries[next].IsEmpty() || m_Entries[next].IdealIndex == next)
                    {
                        m_Entries[index] = new Entry(default!, default!, -1);
                        break;
                    }

                    m_Entries[index] = m_Entries[next];
                }
            }

            m_Count--;
            return true;
        }

        private void Resize(int newCapacity)
        {
            // System.Diagnostics.Debug.Write($"-> {newCapacity}...");

            var oldEntries = m_Entries;
            m_Entries = ArrayPool<Entry>.Shared.Rent(newCapacity);
            m_Entries.AsSpan().Fill(new Entry(default!, default!, -1));

            int mask = m_Entries.Length - 1;

            for (int i = 0; i < oldEntries.Length; i++)
            {
                if (!oldEntries[i].IsEmpty())
                {
                    var inserting = oldEntries[i];
                    int hashCode = inserting.Key.GetHashCode();

                    for (int offset = 0; offset < m_Entries.Length; offset++)
                    {
                        int index = (hashCode + offset) & mask;
                        if (m_Entries[index].IsEmpty())
                        {
                            m_Entries[index] = new Entry(inserting.Key, inserting.Value, hashCode & mask);
                            break;
                        }

                        if (offset > ((index - m_Entries[index].IdealIndex + m_Entries.Length) & mask))
                        {
                            var swap = m_Entries[index];
                            m_Entries[index] = new Entry(inserting.Key, inserting.Value, hashCode & mask);
                            inserting = swap;

                            hashCode = inserting.Key.GetHashCode();
                            offset = 0;
                            continue;
                        }
                    }
                }
            }

            ArrayPool<Entry>.Shared.Return(oldEntries, true);
            // System.Diagnostics.Debug.WriteLine($" done.");
        }

        private void ClearTable()
        {
            m_Entries.AsSpan().Fill(new Entry(default!, default!, -1));
            m_Count = 0;
        }




        public RobinHoodDictionary(int capacity)
        {
            m_Entries = ArrayPool<Entry>.Shared.Rent(capacity);
            m_Entries.AsSpan().Fill(new Entry(default!, default!, -1));
            m_Count = 0;
        }
        public RobinHoodDictionary() : this(16) { }

        public RobinHoodDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary.Count)
        {
            if (dictionary is RobinHoodDictionary<TKey, TValue> cloneSource)
            {
                cloneSource.m_Entries.CopyTo(m_Entries, 0);
                m_Count = cloneSource.m_Count;
                return;
            }

            foreach (var entry in dictionary)
            {
                AddEntry(entry.Key, entry.Value, false);
            }
        }
        public RobinHoodDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(source.Count())
        {
            foreach (var entry in source)
            {
                AddEntry(entry.Key, entry.Value, false);
            }
        }









        public TValue this[TKey key]
        {
            get => GetEntryIndex(key) is int index && index >= 0 ? m_Entries[index].Value : throw new KeyNotFoundException();
            set => AddEntry(key, value, true);
        }

        public int Count => m_Count;

        public bool IsReadOnly => false;

        public readonly struct KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly RobinHoodDictionary<TKey, TValue> m_Parent;

            internal KeyCollection(RobinHoodDictionary<TKey, TValue> parent)
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
                if (array.Length - arrayIndex < m_Parent.m_Count)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Entries.Length; i++)
                {
                    if (m_Parent.m_Entries[i].IsEmpty())
                    {
                        skipped++;
                        continue;
                    }
                    array[arrayIndex + i - skipped] = m_Parent.m_Entries[i].Key;
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
                if (tkey.Length - index < m_Parent.m_Count)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Entries.Length; i++)
                {
                    if (m_Parent.m_Entries[i].IsEmpty())
                    {
                        skipped++;
                        continue;
                    }
                    tkey[i + index - skipped] = m_Parent.m_Entries[i].Key;
                }
            }

            public struct Enumerator : IEnumerator<TKey>
            {
                private readonly RobinHoodDictionary<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(RobinHoodDictionary<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                }

                public TKey Current => m_Parent.m_Entries[m_Index].Key;

                object IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    while ((uint)++m_Index < (uint)m_Parent.m_Entries.Length)
                    {
                        if (!m_Parent.m_Entries[m_Index].IsEmpty())
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
            private readonly RobinHoodDictionary<TKey, TValue> m_Parent;

            internal ValueCollection(RobinHoodDictionary<TKey, TValue> parent)
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
                if (array.Length - arrayIndex < m_Parent.m_Count)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Entries.Length; i++)
                {
                    if (m_Parent.m_Entries[i].IsEmpty())
                    {
                        skipped++;
                        continue;
                    }
                    array[i + arrayIndex - skipped] = m_Parent.m_Entries[i].Value;
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
                if (array is not TValue[] tkey)
                    throw new InvalidCastException(nameof(array));
                if (tkey.Length - index < m_Parent.m_Count)
                    throw new ArgumentException(nameof(array));

                int skipped = 0;
                for (int i = 0; i < m_Parent.m_Entries.Length; i++)
                {
                    if (m_Parent.m_Entries[i].IsEmpty())
                    {
                        skipped++;
                        continue;
                    }
                    tkey[i + index - skipped] = m_Parent.m_Entries[i].Value;
                }
            }

            public struct Enumerator : IEnumerator<TValue>
            {
                private readonly RobinHoodDictionary<TKey, TValue> m_Parent;
                private int m_Index;

                internal Enumerator(RobinHoodDictionary<TKey, TValue> parent)
                {
                    m_Parent = parent;
                    m_Index = -1;
                }

                public TValue Current => m_Parent.m_Entries[m_Index].Value;

                object? IEnumerator.Current => Current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    while ((uint)++m_Index < (uint)m_Parent.m_Entries.Length)
                    {
                        if (!m_Parent.m_Entries[m_Index].IsEmpty())
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
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Entries[index].Value);
        }

        public bool ContainsKey(TKey key)
        {
            return GetEntryIndex(key) >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            foreach (var pair in m_Entries)
            {
                if (!pair.IsEmpty() && EqualityComparer<TValue>.Default.Equals(pair.Value, value))
                {
                    return true;
                }
            }

            return false;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array.Length - arrayIndex < m_Count)
                throw new ArgumentException(nameof(array));

            int skipped = 0;
            for (int i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].IsEmpty())
                {
                    skipped++;
                    continue;
                }
                array[i + arrayIndex - skipped] = new KeyValuePair<TKey, TValue>(m_Entries[i].Key, m_Entries[i].Value);
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

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => GetEnumerator();

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (GetEntryIndex(item.Key) is int index && index >= 0 &&
                EqualityComparer<TValue>.Default.Equals(item.Value, m_Entries[index].Value))
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
                value = m_Entries[index].Value;
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
            private readonly RobinHoodDictionary<TKey, TValue> m_Parent;
            private int m_Index;

            internal Enumerator(RobinHoodDictionary<TKey, TValue> parent)
            {
                m_Parent = parent;
                m_Index = -1;
            }

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(m_Parent.m_Entries[m_Index].Key, m_Parent.m_Entries[m_Index].Value);

            public DictionaryEntry Entry => new DictionaryEntry(Current.Key, Current.Value);

            public object Key => Current.Key;

            public object? Value => Current.Value;

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                while ((uint)++m_Index < (uint)m_Parent.m_Entries.Length)
                {
                    if (!m_Parent.m_Entries[m_Index].IsEmpty())
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
            return $"{m_Count} items";
        }
    }
}