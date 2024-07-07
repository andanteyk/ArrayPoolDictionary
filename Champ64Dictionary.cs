using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ParallelDungeon.Rogue.Serialization
{

    public class Champ64Dictionary<TKey, TValue> :
            IEnumerable<KeyValuePair<TKey, TValue>>,
            IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
            IReadOnlyDictionary<TKey, TValue>
            where TKey : notnull
    {

        private readonly ICompactMapNode m_RootNode;
        private readonly int m_HashCode;
        private readonly int m_CachedSize;


        private Champ64Dictionary(ICompactMapNode rootNode, int hashCode, int cachedSize)
        {
            m_RootNode = rootNode;
            m_HashCode = hashCode;
            m_CachedSize = cachedSize;
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopCount(uint x)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.PopCount(x);
#else
            x = (x & 0x55555555) + ((x >> 1) & 0x55555555);
            x = (x & 0x33333333) + ((x >> 2) & 0x33333333);
            x = (x & 0x0f0f0f0f) + ((x >> 4) & 0x0f0f0f0f);
            x = (x & 0x00ff00ff) + ((x >> 8) & 0x00ff00ff);
            x = (x & 0x0000ffff) + ((x >> 16) & 0x0000ffff);
            return (int)x;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopCount(ulong x)
        {
#if NETCOREAPP3_0_OR_GREATER
            return BitOperations.PopCount(x);
#else
            x = (x & 0x5555555555555555) + ((x >> 1) & 0x5555555555555555);
            x = (x & 0x3333333333333333) + ((x >> 2) & 0x3333333333333333);
            x = (x & 0x0f0f0f0f0f0f0f0f) + ((x >> 4) & 0x0f0f0f0f0f0f0f0f);
            x = (x & 0x00ff00ff00ff00ff) + ((x >> 8) & 0x00ff00ff00ff00ff);
            x = (x & 0x0000ffff0000ffff) + ((x >> 16) & 0x0000ffff0000ffff);
            x = (x & 0x00000000ffffffff) + ((x >> 32) & 0x00000000ffffffff);
            return (int)x;
#endif
        }


        private sealed class MapResult
        {
            public TValue? ReplacedValue { get; private set; }
            public bool IsModified { get; private set; }
            public bool IsReplaced { get; private set; }

            public void Modify() => IsModified = true;
            public void Update(TValue replacedValue)
            {
                ReplacedValue = replacedValue;
                IsModified = true;
                IsReplaced = true;
            }
        }


        private interface ICompactMapNode
        {
            private protected const int BitPartitionSize = 6;

            private protected const int HashCodeLength = 1 << BitPartitionSize;
            private protected const int BitPartitionMask = HashCodeLength - 1;


            protected long NodeMap { get; }
            protected long DataMap { get; }

            protected const byte SizeEmpty = 0b00;
            protected const byte SizeOne = 0b01;
            protected const byte SizeMoreThanOne = 0b10;

            byte PredicateSize();

            ICompactMapNode GetNode(int index);
            KeyValuePair<TKey, TValue> GetPair(int index);

            bool ContainsKey(TKey key, int keyHash, int shift);
            bool TryFindKey(TKey key, int keyHash, int shift, [MaybeNullWhen(false)] out TValue value);
            ICompactMapNode Update(TKey key, TValue value, int keyHash, int shift, MapResult details);
            ICompactMapNode Remove(TKey key, int keyHash, int shift, MapResult details);


            ICompactMapNode CopyAndSetValue(long bitPosition, TValue value);
            ICompactMapNode CopyAndInsertValue(long bitPosition, TKey key, TValue value);
            ICompactMapNode CopyAndRemoveValue(long bitPosition);
            ICompactMapNode CopyAndSetNode(long bitPosition, ICompactMapNode node);
            ICompactMapNode CopyAndMigrateFromInlineToNode(long bitPosition, ICompactMapNode node);
            ICompactMapNode CopyAndMigrateFromNodeToInline(long bitPosition, ICompactMapNode node);

            private protected static ICompactMapNode MergeTwoKeyValuePairs(TKey key1, TValue value1, int keyHash1, TKey key2, TValue value2, int keyHash2, int shift)
            {
                if (shift >= HashCodeLength)
                {
                    var pairs = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(2);
                    pairs[0] = new KeyValuePair<TKey, TValue>(key1, value1);
                    pairs[1] = new KeyValuePair<TKey, TValue>(key2, value2);
                    var newNode = new HashCollisionMapNode(pairs.AsSpan(..2), keyHash1);
                    ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(pairs);
                    return newNode;
                }

                int mask1 = (keyHash1 >> shift) & BitPartitionMask;
                int mask2 = (keyHash2 >> shift) & BitPartitionMask;

                if (mask1 != mask2)
                {
                    long dataMap = (1L << mask1) | (1L << mask2);

                    if (mask1 < mask2)
                    {
                        return new KeyValuePairNode(0, dataMap, new KeyValuePair<TKey, TValue>(key1, value1), new KeyValuePair<TKey, TValue>(key2, value2));
                    }
                    else
                    {
                        return new KeyValuePairNode(0, dataMap, new KeyValuePair<TKey, TValue>(key2, value2), new KeyValuePair<TKey, TValue>(key1, value1));
                    }
                }
                else
                {
                    var node = MergeTwoKeyValuePairs(key1, value1, keyHash1, key2, value2, keyHash2, shift + BitPartitionSize);

                    long nodeMap = 1L << mask1;
                    return new KeyValuePairNode(nodeMap, 0, node);
                }
            }
        }


        private sealed class KeyValuePairNode : ICompactMapNode
        {
            private long NodeMap { get; init; }
            private long DataMap { get; init; }
            private KeyValuePair<TKey, TValue>[] Pairs { get; init; }
            private ICompactMapNode[] Nodes { get; init; }

            long ICompactMapNode.NodeMap => NodeMap;
            long ICompactMapNode.DataMap => DataMap;

            public KeyValuePairNode(long nodeMap, long dataMap, ReadOnlySpan<KeyValuePair<TKey, TValue>> pairs, ReadOnlySpan<ICompactMapNode> nodes)
            {
                NodeMap = nodeMap;
                DataMap = dataMap;
                Pairs = new KeyValuePair<TKey, TValue>[pairs.Length];
                pairs.CopyTo(Pairs.AsSpan());
                Nodes = new ICompactMapNode[nodes.Length];
                nodes.CopyTo(Nodes.AsSpan());

                System.Diagnostics.Debug.Assert(PopCount((ulong)DataMap) == Pairs.Length);
                System.Diagnostics.Debug.Assert(PopCount((ulong)NodeMap) == Nodes.Length);
            }
            public KeyValuePairNode(long nodeMap, long dataMap, KeyValuePair<TKey, TValue> pair1, KeyValuePair<TKey, TValue> pair2)
            {
                NodeMap = nodeMap;
                DataMap = dataMap;
                Pairs = new KeyValuePair<TKey, TValue>[2] { pair1, pair2 };
                Nodes = Array.Empty<ICompactMapNode>();

                System.Diagnostics.Debug.Assert(PopCount((ulong)DataMap) == Pairs.Length);
                System.Diagnostics.Debug.Assert(PopCount((ulong)NodeMap) == Nodes.Length);
            }
            public KeyValuePairNode(long nodeMap, long dataMap, ICompactMapNode node)
            {
                NodeMap = nodeMap;
                DataMap = dataMap;
                Pairs = Array.Empty<KeyValuePair<TKey, TValue>>();
                Nodes = new[] { node };

                System.Diagnostics.Debug.Assert(PopCount((ulong)DataMap) == Pairs.Length);
                System.Diagnostics.Debug.Assert(PopCount((ulong)NodeMap) == Nodes.Length);
            }


            public static KeyValuePairNode Empty { get; } = new KeyValuePairNode(0, 0, ReadOnlySpan<KeyValuePair<TKey, TValue>>.Empty, ReadOnlySpan<ICompactMapNode>.Empty);


            byte ICompactMapNode.PredicateSize()
            {
                if (PopCount((ulong)NodeMap) > 0)
                    return ICompactMapNode.SizeMoreThanOne;

                return PopCount((ulong)DataMap) switch
                {
                    0 => ICompactMapNode.SizeEmpty,
                    1 => ICompactMapNode.SizeOne,
                    _ => ICompactMapNode.SizeMoreThanOne,
                };
            }

            ICompactMapNode ICompactMapNode.GetNode(int index)
            {
                return Nodes[index];
            }

            KeyValuePair<TKey, TValue> ICompactMapNode.GetPair(int index)
            {
                return Pairs[index];
            }

            ICompactMapNode ICompactMapNode.CopyAndSetValue(long bitPosition, TValue value)
            {
                int index = BitPositionToIndex(DataMap, bitPosition);

                Pairs[index] = new KeyValuePair<TKey, TValue>(Pairs[index].Key, value);
                return this;
            }

            ICompactMapNode ICompactMapNode.CopyAndInsertValue(long bitPosition, TKey key, TValue value)
            {
                int index = BitPositionToIndex(DataMap, bitPosition);

                var newPairs = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(Pairs.Length + 1);
                Pairs.AsSpan(..index).CopyTo(newPairs);
                newPairs[index] = new KeyValuePair<TKey, TValue>(key, value);
                Pairs.AsSpan(index..).CopyTo(newPairs.AsSpan((index + 1)..));

                var newNode = new KeyValuePairNode(NodeMap, DataMap | bitPosition, newPairs.AsSpan(..(Pairs.Length + 1)), Nodes);
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(newPairs);

                return newNode;
            }

            ICompactMapNode ICompactMapNode.CopyAndRemoveValue(long bitPosition)
            {
                int index = BitPositionToIndex(DataMap, bitPosition);

                var newPairs = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(Pairs.Length - 1);
                Pairs.AsSpan(..index).CopyTo(newPairs);
                Pairs.AsSpan((index + 1)..).CopyTo(newPairs.AsSpan(index..));

                var newNode = new KeyValuePairNode(NodeMap, DataMap ^ bitPosition, newPairs.AsSpan(..(Pairs.Length - 1)), Nodes);
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(newPairs);

                return newNode;
            }

            ICompactMapNode ICompactMapNode.CopyAndSetNode(long bitPosition, ICompactMapNode node)
            {
                int index = BitPositionToIndex(NodeMap, bitPosition);

                Nodes[index] = node;
                return this;
            }

            ICompactMapNode ICompactMapNode.CopyAndMigrateFromInlineToNode(long bitPosition, ICompactMapNode node)
            {
                int dataIndex = BitPositionToIndex(DataMap, bitPosition);
                int nodeIndex = BitPositionToIndex(NodeMap, bitPosition);

                var newPairs = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(Pairs.Length - 1);
                var newNodes = ArrayPool<ICompactMapNode>.Shared.Rent(Nodes.Length + 1);
                Pairs.AsSpan(..dataIndex).CopyTo(newPairs);
                Pairs.AsSpan((dataIndex + 1)..).CopyTo(newPairs.AsSpan(dataIndex..));
                Nodes.AsSpan(..nodeIndex).CopyTo(newNodes);
                newNodes[nodeIndex] = node;
                Nodes.AsSpan(nodeIndex..).CopyTo(newNodes.AsSpan((nodeIndex + 1)..));

                var newNode = new KeyValuePairNode(NodeMap | bitPosition, DataMap ^ bitPosition, newPairs.AsSpan(..(Pairs.Length - 1)), newNodes.AsSpan(..(Nodes.Length + 1)));
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(newPairs);
                ArrayPool<ICompactMapNode>.Shared.Return(newNodes);

                return newNode;
            }

            ICompactMapNode ICompactMapNode.CopyAndMigrateFromNodeToInline(long bitPosition, ICompactMapNode node)
            {
                int dataIndex = BitPositionToIndex(DataMap, bitPosition);
                int nodeIndex = BitPositionToIndex(NodeMap, bitPosition);

                var newPairs = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(Pairs.Length + 1);
                var newNodes = ArrayPool<ICompactMapNode>.Shared.Rent(Nodes.Length - 1);
                Pairs.AsSpan(..dataIndex).CopyTo(newPairs);
                newPairs[dataIndex] = node.GetPair(0);      // TODO
                Pairs.AsSpan(dataIndex..).CopyTo(newPairs.AsSpan((dataIndex + 1)..));
                Nodes.AsSpan(..nodeIndex).CopyTo(newNodes);
                Nodes.AsSpan((nodeIndex + 1)..).CopyTo(newNodes.AsSpan(nodeIndex..));

                var newNode = new KeyValuePairNode(NodeMap ^ bitPosition, DataMap | bitPosition, newPairs.AsSpan(..(Pairs.Length + 1)), newNodes.AsSpan(..(Nodes.Length - 1)));
                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(newPairs);
                ArrayPool<ICompactMapNode>.Shared.Return(newNodes);

                return newNode;
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static long HashCodeToBitPosition(int keyHash, int shift)
            {
                return 1L << (int)(((uint)keyHash >> shift) & ICompactMapNode.BitPartitionMask);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int BitPositionToIndex(long map, long bitPosition)
            {
                return PopCount((ulong)(map & (bitPosition - 1)));
            }


            public bool ContainsKey(TKey key, int keyHash, int shift)
            {
                long bitPos = HashCodeToBitPosition(keyHash, shift);

                if ((DataMap & bitPos) != 0)
                {
                    int index = BitPositionToIndex(DataMap, bitPos);
                    return EqualityComparer<TKey>.Default.Equals(Pairs[index].Key, key);
                }

                if ((NodeMap & bitPos) != 0)
                {
                    int index = BitPositionToIndex(NodeMap, bitPos);
                    return Nodes[index].ContainsKey(key, keyHash, shift + ICompactMapNode.BitPartitionSize);
                }

                return false;
            }

            public bool TryFindKey(TKey key, int keyHash, int shift, [MaybeNullWhen(false)] out TValue value)
            {
                long bitPos = HashCodeToBitPosition(keyHash, shift);

                if ((DataMap & bitPos) != 0)
                {
                    int index = BitPositionToIndex(DataMap, bitPos);
                    if (EqualityComparer<TKey>.Default.Equals(Pairs[index].Key, key))
                    {
                        value = Pairs[index].Value;
                        return true;
                    }

                    value = default!;
                    return false;
                }

                if ((NodeMap & bitPos) != 0)
                {
                    int index = BitPositionToIndex(NodeMap, bitPos);
                    return Nodes[index].TryFindKey(key, keyHash, shift + ICompactMapNode.BitPartitionSize, out value);
                }

                value = default!;
                return false;
            }

            public ICompactMapNode Update(TKey key, TValue value, int keyHash, int shift, MapResult details)
            {
                long bitPos = HashCodeToBitPosition(keyHash, shift);

                if ((DataMap & bitPos) != 0)
                {
                    int index = BitPositionToIndex(DataMap, bitPos);
                    var currentKey = Pairs[index].Key;

                    if (EqualityComparer<TKey>.Default.Equals(currentKey, key))
                    {
                        details.Update(Pairs[index].Value);
                        return ((ICompactMapNode)this).CopyAndSetValue(bitPos, value);
                    }
                    else
                    {
                        var currentValue = Pairs[index].Value;
                        var subNode = ICompactMapNode.MergeTwoKeyValuePairs(currentKey, currentValue, currentKey.GetHashCode(), key, value, keyHash, shift + ICompactMapNode.BitPartitionSize);
                        details.Modify();
                        return ((ICompactMapNode)this).CopyAndMigrateFromInlineToNode(bitPos, subNode);
                    }
                }
                else if ((NodeMap & bitPos) != 0)
                {
                    var subNode = Nodes[BitPositionToIndex(NodeMap, bitPos)];
                    var newSubNode = subNode.Update(key, value, keyHash, shift + ICompactMapNode.BitPartitionSize, details);

                    if (details.IsModified)
                    {
                        return ((ICompactMapNode)this).CopyAndSetNode(bitPos, newSubNode);
                    }
                    else
                    {
                        return this;
                    }
                }
                else
                {
                    details.Modify();
                    return ((ICompactMapNode)this).CopyAndInsertValue(bitPos, key, value);
                }
            }

            public ICompactMapNode Remove(TKey key, int keyHash, int shift, MapResult details)
            {
                long bitPos = HashCodeToBitPosition(keyHash, shift);

                if ((DataMap & bitPos) != 0)
                {
                    int index = BitPositionToIndex(DataMap, bitPos);
                    if (EqualityComparer<TKey>.Default.Equals(Pairs[index].Key, key))
                    {
                        var currentValue = Pairs[index].Value;
                        details.Update(currentValue);

                        if (PopCount((ulong)DataMap) == 2 && PopCount((ulong)NodeMap) == 0)
                        {
                            long newDataMap = (shift == 0) ? DataMap ^ bitPos : HashCodeToBitPosition(keyHash, 0);

                            var newPair = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(1);
                            newPair[0] = Pairs[index == 0 ? 1 : 0];
                            var newNode = new KeyValuePairNode(0, newDataMap, newPair.AsSpan(..1), Array.Empty<ICompactMapNode>());
                            ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(newPair);
                            return newNode;
                        }
                        else
                        {
                            return ((ICompactMapNode)this).CopyAndRemoveValue(bitPos);
                        }
                    }
                    else
                    {
                        return this;
                    }
                }
                else if ((NodeMap & bitPos) != 0)
                {
                    var subNode = Nodes[BitPositionToIndex(NodeMap, bitPos)];
                    var newSubNode = subNode.Remove(key, keyHash, shift + ICompactMapNode.BitPartitionSize, details);

                    if (!details.IsModified)
                    {
                        return this;
                    }

                    switch (newSubNode.PredicateSize())
                    {
                        case 0:
                            throw new InvalidOperationException();
                        case 1:
                            {
                                if (PopCount((ulong)DataMap) == 0 && PopCount((ulong)NodeMap) == 1)
                                {
                                    return newSubNode;
                                }
                                else
                                {
                                    return ((ICompactMapNode)this).CopyAndMigrateFromNodeToInline(bitPos, newSubNode);
                                }
                            }
                        default:
                            return ((ICompactMapNode)this).CopyAndSetNode(bitPos, newSubNode);
                    }
                }

                return this;
            }

        }

        private sealed class HashCollisionMapNode : ICompactMapNode
        {
            private KeyValuePair<TKey, TValue>[] Pairs;
            private int Hash;

            public HashCollisionMapNode(ReadOnlySpan<KeyValuePair<TKey, TValue>> pairs, int hash)
            {
                Pairs = new KeyValuePair<TKey, TValue>[pairs.Length];
                pairs.CopyTo(Pairs);
                Hash = hash;
            }


            long ICompactMapNode.NodeMap => throw new NotSupportedException();

            long ICompactMapNode.DataMap => throw new NotSupportedException();

            bool ICompactMapNode.ContainsKey(TKey key, int keyHash, int shift)
            {
                if (keyHash != Hash)
                    return false;

                foreach (var pair in Pairs)
                {
                    if (EqualityComparer<TKey>.Default.Equals(pair.Key, key))
                        return true;
                }

                return false;
            }

            ICompactMapNode ICompactMapNode.CopyAndInsertValue(long bitPosition, TKey key, TValue value)
            {
                throw new NotSupportedException();
            }

            ICompactMapNode ICompactMapNode.CopyAndMigrateFromInlineToNode(long bitPosition, ICompactMapNode node)
            {
                throw new NotSupportedException();
            }

            ICompactMapNode ICompactMapNode.CopyAndMigrateFromNodeToInline(long bitPosition, ICompactMapNode node)
            {
                throw new NotSupportedException();
            }

            ICompactMapNode ICompactMapNode.CopyAndRemoveValue(long bitPosition)
            {
                throw new NotSupportedException();
            }

            ICompactMapNode ICompactMapNode.CopyAndSetNode(long bitPosition, ICompactMapNode node)
            {
                throw new NotSupportedException();
            }

            ICompactMapNode ICompactMapNode.CopyAndSetValue(long bitPosition, TValue value)
            {
                throw new NotSupportedException();
            }

            ICompactMapNode ICompactMapNode.GetNode(int index)
            {
                throw new NotSupportedException();
            }

            KeyValuePair<TKey, TValue> ICompactMapNode.GetPair(int index)
            {
                return Pairs[index];
            }

            byte ICompactMapNode.PredicateSize()
            {
                return ICompactMapNode.SizeMoreThanOne;
            }

            ICompactMapNode ICompactMapNode.Remove(TKey key, int keyHash, int shift, MapResult details)
            {
                for (int i = 0; i < Pairs.Length; i++)
                {
                    if (!EqualityComparer<TKey>.Default.Equals(Pairs[i].Key, key))
                        continue;

                    var currentValue = Pairs[i].Value;
                    details.Update(currentValue);

                    switch (Pairs.Length)
                    {
                        case 1:
                            return KeyValuePairNode.Empty;
                        case 2:
                            {
                                var otherKey = i == 0 ? Pairs[1].Key : Pairs[0].Key;
                                var otherValue = i == 0 ? Pairs[1].Value : Pairs[0].Value;
                                var newNode = KeyValuePairNode.Empty;
                                return newNode.Update(otherKey, otherValue, keyHash, 0, details);
                            }
                        default:
                            {
                                var removingPair = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(Pairs.Length - 1);
                                Pairs.AsSpan(..i).CopyTo(removingPair);
                                Pairs.AsSpan((i + 1)..).CopyTo(removingPair.AsSpan(i..));

                                var newNode = new HashCollisionMapNode(removingPair.AsSpan(..(Pairs.Length - 1)), Hash);
                                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(removingPair);
                                return newNode;
                            }
                    }
                }

                return this;
            }

            bool ICompactMapNode.TryFindKey(TKey key, int keyHash, int shift, out TValue value)
            {
                foreach (var pair in Pairs)
                {
                    if (EqualityComparer<TKey>.Default.Equals(pair.Key, key))
                    {
                        value = pair.Value;
                        return true;
                    }
                }
                value = default!;
                return false;
            }

            ICompactMapNode ICompactMapNode.Update(TKey key, TValue value, int keyHash, int shift, MapResult details)
            {
                System.Diagnostics.Debug.Assert(keyHash == Hash);

                for (int i = 0; i < Pairs.Length; i++)
                {
                    if (!EqualityComparer<TKey>.Default.Equals(Pairs[i].Key, key))
                        continue;

                    var currentValue = Pairs[i].Value;
                    if (EqualityComparer<TValue>.Default.Equals(currentValue, value))
                    {
                        return this;
                    }
                    else
                    {
                        var copy = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(Pairs.Length);
                        Pairs.AsSpan().CopyTo(copy);
                        copy[i] = new KeyValuePair<TKey, TValue>(Pairs[i].Key, value);

                        var newNode = new HashCollisionMapNode(copy.AsSpan(..Pairs.Length), Hash);
                        details.Update(currentValue);

                        ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(copy);
                        return newNode;
                    }
                }

                var newPairs = ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(Pairs.Length + 1);
                Pairs.AsSpan().CopyTo(newPairs);
                newPairs[Pairs.Length] = new KeyValuePair<TKey, TValue>(key, value);

                var newPairNode = new HashCollisionMapNode(newPairs.AsSpan(..(Pairs.Length + 1)), Hash);
                details.Modify();

                ArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(newPairs);
                return newPairNode;
            }
        }









        private Champ64Dictionary()
        {
            m_RootNode = KeyValuePairNode.Empty;
            m_HashCode = 0;
            m_CachedSize = 0;
        }

        public static Champ64Dictionary<TKey, TValue> Create()
        {
            return new Champ64Dictionary<TKey, TValue>();
        }
        public static Champ64Dictionary<TKey, TValue> Create(IDictionary<TKey, TValue> dictionary)
        {
            if (dictionary is Champ64Dictionary<TKey, TValue> cloneSource)
            {
                return cloneSource;
            }

            var result = new Champ64Dictionary<TKey, TValue>();

            foreach (var pair in dictionary)
            {
                result = result.AddEntry(pair.Key, pair.Value);
            }

            return result;
        }
        public static Champ64Dictionary<TKey, TValue> Create(IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            var result = new Champ64Dictionary<TKey, TValue>();

            foreach (var pair in source)
            {
                result = result.AddEntry(pair.Key, pair.Value);
            }

            return result;
        }



        public Champ64Dictionary<TKey, TValue> AddEntry(TKey key, TValue value)
        {
            int keyHash = key.GetHashCode();
            var details = new MapResult();

            var newRootNode = m_RootNode.Update(key, value, keyHash, 0, details);

            if (!details.IsModified)
                return this;

            if (details.IsReplaced)
            {
                int oldHash = details.ReplacedValue?.GetHashCode() ?? 0;
                int newHash = value?.GetHashCode() ?? 0;

                return new Champ64Dictionary<TKey, TValue>(newRootNode, m_HashCode + (keyHash ^ newHash) - (keyHash ^ oldHash), m_CachedSize);
            }

            int valueHash = value?.GetHashCode() ?? 0;
            return new Champ64Dictionary<TKey, TValue>(newRootNode, m_HashCode + (keyHash ^ valueHash), m_CachedSize + 1);
        }

        public Champ64Dictionary<TKey, TValue> RemoveEntry(TKey key)
        {
            int keyHash = key.GetHashCode();
            var details = new MapResult();

            var newRootNode = m_RootNode.Remove(key, keyHash, 0, details);

            if (!details.IsModified)
                return this;

            System.Diagnostics.Debug.Assert(details.IsReplaced);
            int valueHash = details.ReplacedValue?.GetHashCode() ?? 0;
            return new Champ64Dictionary<TKey, TValue>(newRootNode, m_HashCode - (keyHash ^ valueHash), m_CachedSize - 1);
        }

        public bool TryGetEntry(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            return m_RootNode.TryFindKey(key, key.GetHashCode(), 0, out value);
        }











        public TValue this[TKey key]
        {
            get => TryGetEntry(key, out var value) ? value : throw new KeyNotFoundException();
            set => AddEntry(key, value);
        }

        public int Count => m_CachedSize;

        public bool IsReadOnly => true;

        public ICollection<TKey> Keys => throw new NotSupportedException();

        public ICollection<TValue> Values => throw new NotSupportedException();

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;



        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return TryGetEntry(item.Key, out var value) &&
                EqualityComparer<TValue>.Default.Equals(item.Value, value);
        }

        public bool ContainsKey(TKey key)
        {
            return TryGetEntry(key, out _);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            throw new System.NotImplementedException();
        }


        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (TryGetEntry(key, out var entry))
            {
                value = entry;
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

        public override string ToString()
        {
            return $"{m_CachedSize} items";
        }
    }
}
