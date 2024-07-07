using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ParallelDungeon.Rogue.Serialization;


[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true)]
// [SimpleJob(RunStrategy.ColdStart, launchCount: 1, warmupCount: 5, iterationCount: 5, id: "FastAndDirtyJob")]
[GenericTypeArguments(typeof(ReadOnlyDictionary<ulong, ulong>), typeof(ReadOnlyDictionary<string, string>))]
[GenericTypeArguments(typeof(ImmutableDictionary<ulong, ulong>), typeof(ImmutableDictionary<string, string>))]
[GenericTypeArguments(typeof(FrozenDictionary<ulong, ulong>), typeof(FrozenDictionary<string, string>))]
public class WholeBenchmark4Readonly<TNumberDic, TStringDic>
    where TNumberDic : IReadOnlyDictionary<ulong, ulong>
    where TStringDic : IReadOnlyDictionary<string, string>
{
    private Dictionary<ulong, ulong> m_SourceRandomNumber = new();
    private Dictionary<ulong, ulong> m_SourceSequentialNumber = new();
    private Dictionary<string, string> m_SourceRandomString = new();
    private Dictionary<string, string> m_SourceSequentialString = new();

#pragma warning disable CS8618
    private IReadOnlyDictionary<ulong, ulong> m_ReadonlyRandomNumber;
    private IReadOnlyDictionary<ulong, ulong> m_ReadonlySequentialNumber;
    private IReadOnlyDictionary<string, string> m_ReadonlyRandomString;
    private IReadOnlyDictionary<string, string> m_ReadonlySequentialString;
#pragma warning restore

    private static int ElementCount => 1 << 10;
    private static int Base64Bytes => 12;


    public IEnumerable<object> FindNumberArgs()
    {
        yield return (ulong)ElementCount;
        yield return (ulong)ElementCount * 4 / 3;
        yield return (ulong)ElementCount * 2;
        yield return (ulong)ElementCount * 4;
        yield return ulong.MaxValue;
    }


    [GlobalSetup]
    public void GlobalSetup()
    {
        Console.WriteLine("// Begin GlobalSetup");

        for (int i = ElementCount; i > 0; i--)
        {
            m_SourceRandomNumber[Rng.Shared.Next()] = Rng.Shared.Next();
            m_SourceSequentialNumber[(ulong)i] = (ulong)i;
            m_SourceRandomString[Rng.Shared.NextBase64(Base64Bytes)] = Rng.Shared.NextBase64(Base64Bytes);
            m_SourceSequentialString[i.ToString()] = i.ToString();
        }

        m_ReadonlyRandomNumber = CreateNumber(m_SourceRandomNumber);
        m_ReadonlySequentialNumber = CreateNumber(m_SourceSequentialNumber);
        m_ReadonlyRandomString = CreateString(m_SourceRandomString);
        m_ReadonlySequentialString = CreateString(m_SourceSequentialString);

        Console.WriteLine("// End GlobalSetup");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (m_SourceRandomNumber is IDisposable idrn)
            idrn.Dispose();
        if (m_SourceSequentialNumber is IDisposable idsn)
            idsn.Dispose();
        if (m_SourceRandomString is IDisposable idrs)
            idrs.Dispose();
        if (m_SourceSequentialString is IDisposable idss)
            idss.Dispose();
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IReadOnlyDictionary<ulong, ulong> CreateNumber(IDictionary<ulong, ulong> dic)
    {
        return typeof(TNumberDic) switch
        {
            Type t when t == typeof(ReadOnlyDictionary<ulong, ulong>) => dic.AsReadOnly(),
            Type t when t == typeof(ImmutableDictionary<ulong, ulong>) => dic.ToImmutableDictionary(),
            Type t when t == typeof(FrozenDictionary<ulong, ulong>) => dic.ToFrozenDictionary(),
            _ => throw new InvalidOperationException(typeof(TNumberDic).ToString()),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IReadOnlyDictionary<string, string> CreateString(IDictionary<string, string> dic)
    {
        return typeof(TStringDic) switch
        {
            Type t when t == typeof(ReadOnlyDictionary<string, string>) => dic.AsReadOnly(),
            Type t when t == typeof(ImmutableDictionary<string, string>) => dic.ToImmutableDictionary(),
            Type t when t == typeof(FrozenDictionary<string, string>) => dic.ToFrozenDictionary(),
            _ => throw new InvalidOperationException(typeof(TStringDic).ToString()),
        };
    }



    [Benchmark, BenchmarkCategory("Number", "Copy")]
    public IReadOnlyDictionary<ulong, ulong> CopyNumber()
    {
        return CreateNumber(m_SourceRandomNumber);
    }

    [Benchmark, BenchmarkCategory("Number", "Iterate")]
    public ulong IterateNumber()
    {
        ulong result = 0;
        foreach (var pair in m_ReadonlyRandomNumber)
        {
            result += pair.Value;
        }
        return result;
    }

    [Benchmark, BenchmarkCategory("Number", "Find")]
    [ArgumentsSource(nameof(FindNumberArgs))]
    public ulong FindNumber(ulong max)
    {
        ulong checksum = 0;

        for (int i = 0; i < ElementCount; i++)
        {
            if (m_ReadonlySequentialNumber.TryGetValue(Rng.Shared.NextUlong(max), out var value))
            {
                checksum += value;
            }
        }

        return checksum;
    }


    [Benchmark, BenchmarkCategory("String", "Copy")]
    public IReadOnlyDictionary<string, string> CopyString()
    {
        return CreateString(m_SourceRandomString);
    }


    [Benchmark, BenchmarkCategory("String", "Iterate")]
    public int IterateString()
    {
        int result = 0;
        foreach (var pair in m_ReadonlyRandomString)
        {
            result += pair.Value.Length;
        }
        return result;
    }

    [Benchmark, BenchmarkCategory("String", "Find")]
    [ArgumentsSource(nameof(FindNumberArgs))]
    public int FindString(ulong max)
    {
        int checksum = 0;

        for (int i = 0; i < ElementCount; i++)
        {
            if (m_ReadonlySequentialString.TryGetValue(Rng.Shared.NextUlong(max).ToString(), out var value))
            {
                checksum += value.Length;
            }
        }

        return checksum;
    }
}
