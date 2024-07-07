using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ParallelDungeon.Rogue.Serialization
{

    public class SwissTableDictionaryTemp<TKey, TValue> :
            ICollection<KeyValuePair<TKey, TValue>>,
            IDictionary<TKey, TValue>,
            IEnumerable<KeyValuePair<TKey, TValue>>,
            IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
            IReadOnlyDictionary<TKey, TValue>,
            IDisposable
            where TKey : notnull
    {
        private const sbyte Empty = -128;
        private const sbyte Deleted = -2;
        private const sbyte Sentinel = -1;

        /*
        private ReadOnlySpan<byte> EmptyGroup = new byte[16]{
            Sentinel, Empty, Empty, Empty,
            Empty, Empty, Empty, Empty,
            Empty, Empty, Empty, Empty,
            Empty, Empty, Empty, Empty,
        };
        */

        private sbyte[] m_ControlBytes;
        private byte[] m_Slots;     // 🤔
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
            if (x & 0x00000000ffffffff) c -= 32;
            if (x & 0x0000ffff0000ffff) c -= 16;
            if (x & 0x00ff00ff00ff00ff) c -= 8;
            if (x & 0x0f0f0f0f0f0f0f0f) c -= 4;
            if (x & 0x3333333333333333) c -= 2;
            if (x & 0x5555555555555555) c -= 1;
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
            if ( (x >> 32 ) != 0){
                zeroes -= 32;
                x >>= 32;
            }
            if ((x >> 16) != 0) {
                zeroes -= 16;
                x >>= 16;
            }
            if ((x >> 8) != 0) {
                zeroes -= 8;
                x >>= 8;
            }
            if ((x >> 4) != 0) {
                zeroes -= 4;
                x >>= 4;
            }
            if ((x >> 2) != 0 ){
                zeroes -= 2;
                x >>= 2;
            }
            if ((x >> 1) != 0) {
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
            => controlByte < Sentinel;


        private const int GroupWidth = 8;
        private const int GroupShift = 3;

        private struct BitMask
        {
            public ulong Mask;
            public uint Width;
            public uint Shift;

            public BitMask(ulong mask)
            {
                Mask = mask;
                Width = GroupWidth;
                Shift = GroupShift;
            }

            public uint LowestBitSet()
            {
                return (uint)TrailingZeroCount(Mask) >> (int)Shift;
            }

            public uint HighestBitSet()
            {
                return (uint)(64 - 1) >> (int)Shift;
            }

            public uint TrailingZeros()
            {
                return (uint)TrailingZeroCount(Mask) >> (int)Shift;
            }

            public uint LeadingZeros()
            {
                uint totalSiginificantBits = Width << (int)Shift;
                uint extraBits = 64 - totalSiginificantBits;
                return (uint)LeadingZeroCount(Mask << (int)extraBits) >> (int)Shift;
            }

            public bool Next(Span<uint> bit)
            {
                if (Mask == 0)
                    return false;

                bit[0] = LowestBitSet();
                Mask &= Mask - 1;
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong CreateGroup(ReadOnlySpan<sbyte> controlBytes)
            => MemoryMarshal.Cast<sbyte, ulong>(controlBytes)[0];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BitMask MatchGroup(ReadOnlySpan<ulong> groups, byte hash)
        {
            ulong msbs = 0x8080808080808080;
            ulong lsbs = 0x0101010101010101;
            ulong x = groups[0] ^ (lsbs * hash);
            return new BitMask((x - lsbs) & ~x & msbs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BitMask MatchEmptyGroup(ReadOnlySpan<ulong> groups)
        {
            ulong msbs = 0x8080808080808080;
            return new BitMask(groups[0] & (~groups[0] << 6) & msbs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BitMask MatchEmptyOrDeletedGroup(ReadOnlySpan<ulong> groups)
        {
            ulong msbs = 0x8080808080808080;
            return new BitMask(groups[0] & (~groups[0] << 7) & msbs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountLeadingEmptyOrDeletedGroup(ReadOnlySpan<ulong> groups)
        {
            ulong gaps = 0x00fefefefefefefe;
            return (TrailingZeroCount(((~groups[0] & (groups[0] >> 7)) | gaps) + 1) + 7) >> 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ConvertSpecialToEmptyAndFullToDeleted(ReadOnlySpan<ulong> groups, Span<sbyte> dst)
        {
            ulong msbs = 0x8080808080808080;
            ulong lsbs = 0x0101010101010101;
            ulong x = groups[0] & msbs;
            ulong res = (~x + (x >> 7)) & ~lsbs;
            MemoryMarshal.Cast<sbyte, ulong>(dst)[0] = res;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NumClonedBytes()
        {
            return GroupWidth - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidCapacity(ulong n)
        {
            return ((n + 1) & n) == 0 && n > 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RandomSeed()
        {
            // originally, (ulong)ptr ^ ++ctr;
            return 0;
        }

        private static ulong H1(ulong hash, ReadOnlySpan<sbyte> ctrl)
        {
            ulong hashSeed = 0; // originally (ulong)(IntPtr)ctrl >> 12;
            return hash >> 7 ^ hashSeed;
        }

        private static sbyte H2(ulong hash)
        {
            return (sbyte)(hash & 0x7f);
        }

        private static bool ShouldInsertBackwards(ulong hash, ReadOnlySpan<sbyte> controlBytes)
        {
            return H1(hash, controlBytes) % 13 > 6;
        }

        private static void ConvertDeletedToEmptyAndFullToDeleted(Span<sbyte> controlBytes, int capacity)
        {
            for (int pos = 0; pos < (int)capacity; pos += GroupWidth)
            {
                var g = CreateGroup(controlBytes[pos..]);
                ConvertSpecialToEmptyAndFullToDeleted(MemoryMarshal.CreateSpan(ref g, 1), controlBytes[pos..]);
            }

            controlBytes.CopyTo(controlBytes[(capacity + 1)..(capacity + 1 + (int)NumClonedBytes())]);
            controlBytes[capacity] = Sentinel;
        }

        private static void ResetCtrl<T>(int capacity, Span<sbyte> ctrl, ReadOnlySpan<T> slots)
        {
            ctrl[..(capacity + 1 + (int)NumClonedBytes())].Fill(Empty);
            ctrl[capacity] = Sentinel;
            // PoisonMemory(slots[..capacity]). may mean nullify?
        }

        private static void SetCtrl<T>(int i, sbyte h, int capacity, Span<sbyte> ctrl, ReadOnlySpan<T> slots, int slotSize)
        {
            // Poison/Unpoison Memory.

            int mirrored_i = ((i - NumClonedBytes()) & capacity) + (NumClonedBytes() & capacity);
            ctrl[i] = h;
            ctrl[mirrored_i] = h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong NormalizeCapacity(int n)
        {
            return n != 0 ? ~0ul >> LeadingZeroCount((ulong)n) : 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong CapacityToGrowth(ulong capacity)
        {
            if (GroupWidth == 8 && capacity == 7)
                return 6;

            return capacity - capacity / 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GrowthToLowerBoundCapacity(ulong growth)
        {
            if (GroupWidth == 8 && growth == 7)
                return 8;

            return growth + (ulong)(((long)growth - 1) / 7);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SlotOffset(int capacity, int slotAlign)
        {
            int numControlBytes = capacity + 1 + NumClonedBytes();
            return (numControlBytes + slotAlign - 1) & (~slotAlign + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AllocSize(int capacity, int slotSize, int slotAlign)
        {
            return SlotOffset(capacity, slotAlign) + capacity * slotSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSmall(int capacity)
        {
            return capacity < GroupWidth - 1;
        }


        private struct ProbeSeq
        {
            // note: originally ulong
            public ulong Mask;
            public ulong Offset;
            public ulong Index;

            public ProbeSeq(ulong hash, ulong mask)
            {
                Mask = mask;
                Offset = hash & mask;
                Index = 0;
            }

            public ulong GetOffset(ulong i)
            {
                return (Offset + i) & Mask;
            }

            public void Next()
            {
                Index += GroupWidth;
                Offset = (Offset + Index) & Mask;
            }

            public static ProbeSeq Start(ReadOnlySpan<sbyte> ctrl, ulong hash, ulong capacity)
            {
                return new ProbeSeq(H1(hash, ctrl), capacity);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (ulong offset, ulong probeLength)
            FindFirstNonFull(ReadOnlySpan<sbyte> ctrl, ulong hash, ulong capacity)
        {
            var seq = ProbeSeq.Start(ctrl, hash, capacity);
            while (true)
            {
                var g = CreateGroup(ctrl[(int)seq.Offset..]);
                var mask = MatchEmptyOrDeletedGroup(MemoryMarshal.CreateSpan(ref g, 1));

                if (mask.Mask != 0)
                {
                    if (!IsSmall((int)capacity) && ShouldInsertBackwards(hash, ctrl))
                    {
                        return (seq.GetOffset(mask.HighestBitSet()), seq.Index);
                    }

                    return (seq.GetOffset(mask.TrailingZeros()), seq.Index);
                }

                seq.Next();
            }
        }


        private struct Iterator
        {
            public SwissTableDictionaryTemp<TKey, TValue> Set;
            public int ControlOffset;
            public int SlotOffset;

            public void SkipEmptyOrDeleted(int slotSize)
            {
                while (IsEmptyOrDeleted(Set.m_ControlBytes[ControlOffset]))
                {
                    var g = CreateGroup(Set.m_ControlBytes[ControlOffset..]);
                    int shift = CountLeadingEmptyOrDeletedGroup(MemoryMarshal.CreateSpan(ref g, 1));
                    ControlOffset += shift;
                    SlotOffset += shift * slotSize;
                }

                if (Set.m_ControlBytes[ControlOffset] == Sentinel)
                {
                    ControlOffset = -1;
                    SlotOffset = -1;
                }
            }

            public static Iterator At(SwissTableDictionaryTemp<TKey, TValue> self, int index, int slotSize)
            {
                var iter = new Iterator
                {
                    Set = self,
                    ControlOffset = index,
                    SlotOffset = index * slotSize,
                };
                iter.SkipEmptyOrDeleted(slotSize);

                iter.AssertIsValid();
                return iter;
            }

            public void AssertIsValid()
            {
                if (IsFull(Set.m_ControlBytes[ControlOffset]))
                    throw new InvalidOperationException("should be rehash");
            }

            public void AssertIsFull()
            {
                if (IsFull(Set.m_ControlBytes[ControlOffset]))
                    throw new InvalidOperationException("should be rehash");
            }

            public Span<T> Get<T>(Span<T> slot)
            {
                AssertIsValid();
                if (SlotOffset == -1)
                    return null;

                return slot[SlotOffset..];
            }

            public void Next(int slotSize)
            {
                AssertIsFull();

                ControlOffset++;
                SlotOffset += slotSize;

                SkipEmptyOrDeleted(slotSize);
            }
        }

        private static void EraseMetaOnly(Iterator iter, int slotSize)
        {
            iter.AssertIsFull();

            iter.Set.m_Size--;
            int index = iter.ControlOffset;
            int indexBefore = (index - GroupWidth) & iter.Set.m_Slots.Length;   // m_Slots.Length == capacity.

            var gAfter = CreateGroup(iter.Set.m_ControlBytes[iter.ControlOffset..]);
            var emptyAfter = MatchEmptyGroup(MemoryMarshal.CreateSpan(ref gAfter, 1));
            var gBefore = CreateGroup(iter.Set.m_ControlBytes[indexBefore..]);
            var emptyBefore = MatchEmptyGroup(MemoryMarshal.CreateSpan(ref gBefore, 1));

            bool wasNeverFull = emptyBefore.Mask != 0 && emptyAfter.Mask != 0 &&
                (emptyAfter.TrailingZeros() + emptyBefore.LeadingZeros()) < GroupWidth;

            // TODO: byte? maybe KeyValuePair<TKey, TValue> ?
            SetCtrl<byte>(index, wasNeverFull ? Empty : Deleted,
                iter.Set.m_Slots.Length, iter.Set.m_ControlBytes, iter.Set.m_Slots, slotSize);
            iter.Set.m_GrowthLeft += wasNeverFull ? 1 : 0;
        }

        private void ResetGrowthLeft()
        {
            m_GrowthLeft = (int)(CapacityToGrowth((ulong)m_Slots.Length) - (ulong)m_Size);
        }

        private void InitializeSlots(int slotSize, int slotAlign)
        {
            int capacity = m_Slots.Length;
            if (capacity == 0)
                throw new InvalidOperationException("capacity should be nonzero");

            // Allocate some memory with ;
            m_ControlBytes = new sbyte[AllocSize(capacity, slotSize, slotAlign)];
            // TODO: C++ tokuyuu no are?
            //m_Slots = m_ControlBytes[SlotOffset(capacity, slotAlign)..];
            ResetCtrl<byte>(capacity, m_ControlBytes, m_Slots);
            ResetGrowthLeft();
        }

        private void DestroySlots()
        {
            if (m_Slots.Length == 0)
                return;

            // TODO: delete hook event

            // TODO: free memory
            m_ControlBytes = new sbyte[16]{
                Sentinel, Empty, Empty, Empty,
                Empty, Empty, Empty, Empty,
                Empty, Empty, Empty, Empty,
                Empty, Empty, Empty, Empty,
            };
            m_Slots = null!;
            m_Size = 0;
            m_GrowthLeft = 0;
        }

        private void Resize(ulong newCapacity, int slotSize, int slotAlign)
        {
            if (!IsValidCapacity(newCapacity))
                throw new InvalidOperationException("invalid capacity");

            var oldControl = m_ControlBytes;
            var oldSlots = m_Slots;

            m_Slots = new byte[newCapacity];        // TODO
            InitializeSlots(slotSize, slotAlign);


            ulong totalProbeLength = 0;
            for (int i = 0; i != oldSlots.Length; i++)
            {
                if (IsFull(oldControl[i]))
                {
                    ulong hash = (ulong)oldSlots[i].GetHashCode();       // maybe KVP<>
                    var target = FindFirstNonFull(m_ControlBytes, (ulong)hash, (ulong)m_Slots.Length);
                    var newI = target.offset;
                    totalProbeLength += target.probeLength;
                    SetCtrl<byte>((int)newI, H2(hash), m_Slots.Length, m_ControlBytes, m_Slots, slotSize);
                    // TODO: transfer event
                }
            }

            // unpoison old memory(m_slots)
        }

        public void DropDeletesWithoutResize(int slotSize)
        {
            if (!IsValidCapacity((ulong)m_Slots.Length))
                throw new InvalidOperationException("invalid capacity");
            if (IsSmall(m_Slots.Length))
                throw new InvalidOperationException("capacity too small");

            ConvertDeletedToEmptyAndFullToDeleted(m_ControlBytes, m_Slots.Length);
            ulong totalProbeLength = 0;

            // alloc slot

            for (int i = 0; i != m_Slots.Length; i++)
            {
                if (!IsDeleted(m_ControlBytes[i]))
                    continue;

                var oldSlot = m_Slots[i];
                var hash = (ulong)oldSlot.GetHashCode();

                var target = FindFirstNonFull(m_ControlBytes, hash, (ulong)m_Slots.Length);
                int newI = (int)target.offset;
                totalProbeLength += target.probeLength;

                var newSlot = m_Slots[newI];

                int probeOffset = (int)ProbeSeq.Start(m_ControlBytes, hash, (ulong)m_Slots.Length).Offset;
                int ProbeIndex(int pos) => (((pos - probeOffset) & m_Slots.Length) / GroupWidth);

                if (ProbeIndex(newI) == ProbeIndex(i))
                {
                    SetCtrl<byte>(i, H2(hash), m_Slots.Length, m_ControlBytes, m_Slots, slotSize);
                    continue;
                }

                if (IsEmpty(m_ControlBytes[newI]))
                {
                    SetCtrl<byte>(newI, H2(hash), m_Slots.Length, m_ControlBytes, m_Slots, slotSize);
                    // invoke transfer event
                    SetCtrl<byte>(i, Empty, m_Slots.Length, m_ControlBytes, m_Slots, slotSize);
                }
                else
                {
                    if (!IsDeleted(m_ControlBytes[newI]))
                        throw new InvalidOperationException("bad ctrl value");

                    SetCtrl<byte>(newI, H2(hash), m_Slots.Length, m_ControlBytes, m_Slots, slotSize);

                    // invoke transfer event

                    i--;
                }

            }

            ResetGrowthLeft();
            // free slot
        }

        public void RehashAndGrowIfNecessary(int slotSize, int slotAlign)
        {
            if (m_Slots.Length == 0)
            {
                Resize(1, slotSize, slotAlign);
            }
            else if (m_Slots.Length > GroupWidth && m_Size * 32L <= m_Slots.Length * 25L)
            {
                DropDeletesWithoutResize(slotSize);
            }
            else
            {
                Resize((ulong)m_Slots.Length * 2 + 1, slotSize, slotAlign);
            }
        }

        public static void PrefetchHeapBlock()
        {
            // do nothing
        }

        public static void Prefetch()
        {
            // do nothing
        }

        public int PrepareInsert(ulong hash, int slotSize, int slotAlign)
        {
            var target = FindFirstNonFull(m_ControlBytes, hash, (ulong)m_Slots.Length);
            if (m_GrowthLeft == 0 && !IsDeleted(m_ControlBytes[target.offset]))
            {
                RehashAndGrowIfNecessary(slotSize, slotAlign);
                target = FindFirstNonFull(m_ControlBytes, hash, (ulong)m_Slots.Length);
            }
            m_Size++;
            m_GrowthLeft -= IsEmpty(m_ControlBytes[target.offset]) ? 1 : 0;
            SetCtrl<byte>((int)target.offset, H2(hash), m_Slots.Length, m_ControlBytes, m_Slots, slotSize);
            return (int)target.offset;
        }

        public (int index, bool inserted) FindOrPrepareInsert(ReadOnlySpan<byte> key, int slotSize, int slotAlign)
        {
            PrefetchHeapBlock();
            ulong hash = (ulong)key[0].GetHashCode();

            var seq = ProbeSeq.Start(m_ControlBytes, hash, (ulong)m_Slots.Length);
            while (true)
            {
                var g = CreateGroup(m_ControlBytes[(int)seq.Offset..]);
                var match = MatchGroup(MemoryMarshal.CreateSpan(ref g, 1), (byte)H2(hash));
                uint i = 0;

                while (match.Next(MemoryMarshal.CreateSpan(ref i, 1)))
                {
                    var idx = seq.GetOffset(i);
                    var slot = m_Slots[(int)idx..];

                    if (key == slot)    // note: it actually not work
                    {
                        return ((int)idx, false);
                    }
                }

                if (MatchEmptyGroup(MemoryMarshal.CreateSpan(ref g, 1)).Mask != 0)
                    break;

                seq.Next();
                if (seq.Index > (ulong)m_Slots.Length)
                    throw new InvalidOperationException("full table!");
            }

            return (PrepareInsert(hash, slotSize, slotAlign), true);
        }

        public Span<byte> PreInsert(int i)
        {
            var dst = m_Slots[i..];
            // Init(dst);
            return dst;
        }


        // actually ctor
        public static SwissTableDictionaryTemp<TKey, TValue> Create(int capacity)
        {
            var self = new SwissTableDictionaryTemp<TKey, TValue>()
            {
                m_ControlBytes = new sbyte[16]{
                    Sentinel, Empty, Empty, Empty,
                    Empty, Empty, Empty, Empty,
                    Empty, Empty, Empty, Empty,
                    Empty, Empty, Empty, Empty,
                }
            };

            if (capacity != 0)
            {
                self.m_Slots = new byte[NormalizeCapacity(capacity)];
                self.InitializeSlots(1, 0);
            }

            return self;
        }

        // actually ctor(capacity)
        public void Reserve(int n)
        {
            if ((int)n <= m_Size + m_GrowthLeft)
                return;

            n = (int)NormalizeCapacity((int)GrowthToLowerBoundCapacity((ulong)n));
            Resize((ulong)n, 1, 0);
        }

        // actually ctor(dict)
        public SwissTableDictionaryTemp<TKey, TValue> Duplicate()
        {
            var copy = Create(0);
            copy.Reserve(m_Size);

            for (var iter = Iterator.At(this, 0, 1);
                iter.Get<byte>(m_Slots) != null;
                iter.Next(1))
            {
                var v = iter.Get<byte>(m_Slots);
                ulong hash = (ulong)v[0].GetHashCode();

                var target = FindFirstNonFull(copy.m_ControlBytes, hash, (ulong)copy.m_Slots.Length);
                SetCtrl<byte>((int)target.offset, H2(hash), copy.m_Slots.Length, copy.m_ControlBytes, copy.m_Slots, 1);
                var slot = PreInsert((int)target.offset);
                v.CopyTo(slot);
            }

            copy.m_Size = m_Size;
            copy.m_GrowthLeft = m_GrowthLeft;
            return copy;
        }

        public void Destroy()
        {
            DestroySlots();
        }

        public bool IsEmpty() => m_Size == 0;

        public int Size() => m_Size;

        public int Capacity() => m_Slots.Length;

        public void ClearTable()
        {
            if (m_Slots.Length > 127)
            {
                DestroySlots();
            }
            else if (m_Slots.Length > 0)
            {
                m_Size = 0;
                ResetCtrl<byte>(m_Slots.Length, m_ControlBytes, m_Slots);
                ResetGrowthLeft();
            }

            if (m_Size != 0)
                throw new InvalidOperationException("size was still not zero");
        }

        private (Iterator iter, bool inserted) DeferredInsert(ReadOnlySpan<byte> key)
        {
            var res = FindOrPrepareInsert(key, 1, 0);

            if (res.inserted)
            {
                PreInsert(res.index);
            }

            return (Iterator.At(this, res.index, 1), res.inserted);
        }

        private (Iterator iter, bool inserted) Insert(ReadOnlySpan<byte> val)
        {
            var res = FindOrPrepareInsert(val, 1, 0);

            if (res.inserted)
            {
                var slot = PreInsert(res.index);
                val.CopyTo(slot);
            }

            return (Iterator.At(this, res.index, 1), res.inserted);
        }


        private Iterator FindHinted(ReadOnlySpan<byte> key, ulong hash)
        {
            var seq = ProbeSeq.Start(m_ControlBytes, hash, (ulong)m_Slots.Length);
            while (true)
            {
                var g = CreateGroup(m_ControlBytes[(int)seq.Offset..]);
                var match = MatchGroup(MemoryMarshal.CreateSpan(ref g, 1), (byte)H2(hash));
                uint i = 0;

                while (match.Next(MemoryMarshal.CreateSpan(ref i, 1)))
                {
                    var slot = m_Slots[(int)seq.GetOffset(i)..];
                    if (key == slot)
                    {
                        return Iterator.At(this, (int)seq.GetOffset(i), 1);
                    }
                }

                if (MatchEmptyGroup(MemoryMarshal.CreateSpan(ref g, 1)).Mask != 0)
                {
                    // empty iterator?
                    return new Iterator();
                }

                seq.Next();
                if ((int)seq.Index > m_Slots.Length)
                    throw new InvalidOperationException("full table");
            }
        }


        private Iterator Find(ReadOnlySpan<byte> key)
        {
            return FindHinted(key, (ulong)key[0].GetHashCode());
        }

        private void EraseAt(Iterator iter)
        {
            if (!(m_ControlBytes != null && IsFull(m_ControlBytes[0])))
                throw new InvalidOperationException();

            EraseMetaOnly(iter, 1);
        }

        private bool Erase(ReadOnlySpan<byte> key)
        {
            var iter = Find(key);
            if (iter.SlotOffset == -1)
                return false;

            EraseAt(iter);
            return true;
        }

        private void Rehash(int n)
        {
            if (n == 0 && m_Slots.Length == 0)
                return;

            if (n == 0 && m_Size == 0)
            {
                DestroySlots();
                return;
            }

            var m = NormalizeCapacity((int)(n | (int)GrowthToLowerBoundCapacity((ulong)m_Size)));

            if (n == 0 || (int)m > m_Slots.Length)
            {
                Resize(m, 1, 0);
            }
        }

        private bool Contains(ReadOnlySpan<byte> key)
        {
            return Find(key).SlotOffset != -1;
        }










        public TValue this[TKey key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public int Count => throw new NotImplementedException();

        public bool IsReadOnly => throw new NotImplementedException();

        public ICollection<TKey> Keys => throw new NotImplementedException();

        public ICollection<TValue> Values => throw new NotImplementedException();

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => throw new NotImplementedException();

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => throw new NotImplementedException();

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void Add(TKey key, TValue value)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public bool ContainsKey(TKey key)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public bool Remove(TKey key)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
