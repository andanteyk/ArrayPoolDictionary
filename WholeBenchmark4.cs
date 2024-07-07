using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using ParallelDungeon.Rogue.Serialization;

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true)]
// [SimpleJob(RunStrategy.ColdStart, launchCount: 1, warmupCount: 5, iterationCount: 5, id: "FastAndDirtyJob")]
[GenericTypeArguments(typeof(AnkerlDictionary<ulong, ulong>), typeof(AnkerlDictionary<string, string>))]
[GenericTypeArguments(typeof(AnkerlDictionaryMod<ulong, ulong>), typeof(AnkerlDictionaryMod<string, string>))]
[GenericTypeArguments(typeof(AnkerlDictionaryMod2<ulong, ulong>), typeof(AnkerlDictionaryMod2<string, string>))]
[GenericTypeArguments(typeof(ArrayPoolDictionary<ulong, ulong>), typeof(ArrayPoolDictionary<string, string>))]
[GenericTypeArguments(typeof(HopscotchDictionary<ulong, ulong>), typeof(HopscotchDictionary<string, string>))]
[GenericTypeArguments(typeof(OpenAddressingDictionary<ulong, ulong>), typeof(OpenAddressingDictionary<string, string>))]
[GenericTypeArguments(typeof(RobinHoodDictionary<ulong, ulong>), typeof(RobinHoodDictionary<string, string>))]
[GenericTypeArguments(typeof(SwissTableDictionaryRevisited<ulong, ulong>), typeof(SwissTableDictionaryRevisited<string, string>))]
[GenericTypeArguments(typeof(IbukiDictionary<ulong, ulong>), typeof(IbukiDictionary<string, string>))]
[GenericTypeArguments(typeof(Dictionary<ulong, ulong>), typeof(Dictionary<string, string>))]
public class WholeBenchmark4<TNumberDic, TStringDic>
    where TNumberDic : IDictionary<ulong, ulong>, new()
    where TStringDic : IDictionary<string, string>, new()
{
    private TNumberDic m_SourceRandomNumber = new();
    private TNumberDic m_SourceSequentialNumber = new();
    private TStringDic m_SourceRandomString = new();
    private TStringDic m_SourceSequentialString = new();

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
            // note: * 6364136223846793005: To disperse hash values
            m_SourceSequentialNumber[(ulong)i * 6364136223846793005] = (ulong)i * 6364136223846793005;
            m_SourceRandomString[Rng.Shared.NextBase64(Base64Bytes)] = Rng.Shared.NextBase64(Base64Bytes);
            m_SourceSequentialString[i.ToString()] = i.ToString();
        }

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



    [Benchmark, BenchmarkCategory("Number", "Copy")]
    public TNumberDic CopyNumber()
    {
        var dic = new TNumberDic();

        foreach (var pair in m_SourceRandomNumber)
        {
            dic.Add(pair.Key, pair.Value);
        }

        return dic;
    }

    [Benchmark, BenchmarkCategory("Number", "AddAndRemove")]
    public TNumberDic AddAndRemoveNumber()
    {
        var dic = new TNumberDic();

        foreach (var pair in m_SourceRandomNumber)
        {
            dic.Add(pair.Key, pair.Value);
        }

        foreach (var pair in m_SourceRandomNumber)
        {
            dic.Remove(pair.Key);
        }

        return dic;
    }

    [Benchmark, BenchmarkCategory("Number", "Iterate")]
    public ulong IterateNumber()
    {
        ulong result = 0;
        foreach (var pair in m_SourceRandomNumber)
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
            if (m_SourceSequentialNumber.TryGetValue(Rng.Shared.NextUlong(max) * 6364136223846793005, out var value))
            {
                checksum += value;
            }
        }

        return checksum;
    }

    [Benchmark, BenchmarkCategory("Number", "Alloc")]
    public void AllocNumber()
    {
        var dic = new TNumberDic();

        foreach (var pair in m_SourceRandomNumber)
        {
            dic.Add(pair.Key, pair.Value);
        }
    }


    [Benchmark, BenchmarkCategory("String", "Copy")]
    public TStringDic CopyString()
    {
        var dic = new TStringDic();

        foreach (var pair in m_SourceRandomString)
        {
            dic.Add(pair.Key, pair.Value);
        }

        return dic;
    }

    [Benchmark, BenchmarkCategory("String", "AddAndRemove")]
    public TStringDic AddAndRemoveString()
    {
        var dic = new TStringDic();

        foreach (var pair in m_SourceRandomString)
        {
            dic.Add(pair.Key, pair.Value);
        }

        foreach (var pair in m_SourceRandomString)
        {
            dic.Remove(pair.Key);
        }

        return dic;
    }

    [Benchmark, BenchmarkCategory("String", "Iterate")]
    public int IterateString()
    {
        int result = 0;
        foreach (var pair in m_SourceRandomString)
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
            if (m_SourceSequentialString.TryGetValue(Rng.Shared.NextUlong(max).ToString(), out var value))
            {
                checksum += value.Length;
            }
        }

        return checksum;
    }

    [Benchmark, BenchmarkCategory("String", "Alloc")]
    public void AllocString()
    {
        var dic = new TStringDic();

        foreach (var pair in m_SourceRandomString)
        {
            dic.Add(pair.Key, pair.Value);
        }
    }
}
