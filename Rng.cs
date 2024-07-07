using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

public sealed class Rng
{
    private ulong m_State0, m_State1;

    [ThreadStatic]
    private static Rng? m_Shared;
    public static Rng Shared => m_Shared ??= new Rng();



    public Rng()
    {
        var span = (stackalloc ulong[2]);
        do
        {
            RandomNumberGenerator.Fill(MemoryMarshal.AsBytes(span));
        } while (span[0] == 0 && span[1] == 0);

        m_State0 = span[0];
        m_State1 = span[1];
    }

    public Rng(ulong s0, ulong s1)
    {
        if (s0 == 0 && s1 == 0)
            throw new ArgumentException();

        m_State0 = s0;
        m_State1 = s1;
    }

    public Rng(Rng source)
    {
        m_State0 = source.m_State0;
        m_State1 = source.m_State1;
    }

    public void SetState(Rng source)
    {
        m_State0 = source.m_State0;
        m_State1 = source.m_State1;
    }

    public ulong Next()
    {
        ulong s0 = m_State0, s1 = m_State1;
        ulong result = BitOperations.RotateLeft((s0 + s1) * 9, 29) + s0;

        m_State0 = s0 ^ BitOperations.RotateLeft(s1, 29);
        m_State1 = s0 ^ s1 << 9;

        return result;
    }

    public uint NextUint(uint maxExclusive)
    {
        var hi = Math.BigMul(Next(), maxExclusive, out var lo);
        if (lo < maxExclusive)
        {
            ulong lowerBound = (0ul - maxExclusive) % maxExclusive;

            while (lo < lowerBound)
            {
                hi = Math.BigMul(Next(), maxExclusive, out lo);
            }
        }

        return (uint)hi;
    }

    public uint NextUint(uint minInclusive, uint maxExclusive)
        => minInclusive + NextUint(maxExclusive - minInclusive);

    public int NextInt(int maxExclusive) => (int)NextUint((uint)maxExclusive);
    public int NextInt(int minInclusive, int maxExclusive)
        => minInclusive + (int)NextUint((uint)(maxExclusive - minInclusive));

    public ulong NextUlong(ulong maxExclusive)
    {
        var hi = Math.BigMul(Next(), maxExclusive, out var lo);
        if (lo < maxExclusive)
        {
            ulong lowerBound = (0ul - maxExclusive) % maxExclusive;

            while (lo < lowerBound)
            {
                hi = Math.BigMul(Next(), maxExclusive, out lo);
            }
        }

        return hi;
    }

    public string NextBase64(int byteLength)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(byteLength);
        var span = buffer.AsSpan(..byteLength);
        var ulongs = MemoryMarshal.Cast<byte, ulong>(span);

        for (int i = 0; i < ulongs.Length; i++)
        {
            ulongs[i] = Next();
        }

        var remains = span[(ulongs.Length * 8)..];
        if (remains.Length > 0)
        {
            ulong r = Next();
            for (int i = 0; i < remains.Length; i++)
            {
                remains[i] = (byte)r;
                r >>= 8;
            }
        }

        var str = Convert.ToBase64String(span);
        ArrayPool<byte>.Shared.Return(buffer);

        return str;
    }
}
