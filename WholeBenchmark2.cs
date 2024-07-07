using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using ParallelDungeon.Rogue.Serialization;

[MemoryDiagnoser]
public class WholeBenchmark2
{
    private Rng seed = new();
    private Dictionary<(Type, int), IDictionary<ulong, ulong>> m_NumberDicitionaries = new();
    private Dictionary<(Type, int), IDictionary<string, string>> m_StringDictionaries = new();


    public const int LoopCount = 1 << 8;



    public WholeBenchmark2()
    {
        GlobalSetup();
    }


    //[GlobalSetup]
    public void GlobalSetup()
    {
        void Setup<TDic, TKey, TValue>(Func<IDictionary<TKey, TValue>, TDic> ctor, Func<Rng, KeyValuePair<TKey, TValue>> generator)
            where TDic : IDictionary<TKey, TValue>
            where TKey : notnull
        {
            IDictionary<TKey, TValue> source = new Dictionary<TKey, TValue>();
            var rng = new Rng(seed);

            for (int i = 4; i <= 20; i++)
            {
                int length = 1 << i;

                var dic = ctor(source);

                while (dic.Count < length)
                {
                    var elem = generator(rng);
                    dic[elem.Key] = elem.Value;
                }


                if (dic is IDictionary<ulong, ulong> numberDic)
                {
                    m_NumberDicitionaries.Add((typeof(TDic), length), numberDic);
                }
                else if (dic is IDictionary<string, string> stringDic)
                {
                    m_StringDictionaries.Add((typeof(TDic), length), stringDic);
                }
                source = dic;
            }
        }

        KeyValuePair<ulong, ulong> numberGenerator(Rng rng) => new KeyValuePair<ulong, ulong>(rng.Next(), Rng.Shared.Next());



        Setup(source => new AnkerlDictionary<ulong, ulong>(source), numberGenerator);
        Console.WriteLine("setup done. source=" + AnkerlNumberSource().Count());
    }


    public IEnumerable<IDictionary<ulong, ulong>> NumberSource()
    {
        foreach (var dic in m_NumberDicitionaries.Values)
        {
            yield return dic;
        }
    }

    public IEnumerable<IDictionary<string, string>> StringSource()
    {
        foreach (var dic in m_StringDictionaries.Values)
        {
            yield return dic;
        }
    }

    public IEnumerable<object> AnkerlNumberSource() => m_NumberDicitionaries.Values.OfType<AnkerlDictionary<ulong, ulong>>();





    [Benchmark, ArgumentsSource(nameof(AnkerlNumberSource)), BenchmarkCategory("Copy", "Number", "Ankerl")]
    public AnkerlDictionary<ulong, ulong> CopyNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
        => new AnkerlDictionary<ulong, ulong>(source);

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberSource)), BenchmarkCategory("Add", "Number", "Ankerl")]
    public AnkerlDictionary<ulong, ulong> AddNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        var dict = new AnkerlDictionary<ulong, ulong>(source);
        for (int i = 0; i < LoopCount; i++)
        {
            dict[(ulong)i] = Rng.Shared.Next();
        }
        return dict;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberSource)), BenchmarkCategory("Overwrite", "Number", "Ankerl")]
    public AnkerlDictionary<ulong, ulong> OverwriteNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        var dict = new AnkerlDictionary<ulong, ulong>(source);
        var rng = new Rng(seed);
        for (int i = 0; i < LoopCount; i++)
        {
            dict[rng.Next()] = Rng.Shared.Next();
        }
        return dict;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberSource)), BenchmarkCategory("RemoveFail", "Number", "Ankerl")]
    public AnkerlDictionary<ulong, ulong> RemoveFailNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        var dict = new AnkerlDictionary<ulong, ulong>(source);
        for (int i = 0; i < LoopCount; i++)
        {
            dict.Remove((ulong)i);
        }
        return dict;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberSource)), BenchmarkCategory("Remove", "Number", "Ankerl")]
    public AnkerlDictionary<ulong, ulong> RemoveNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        var dict = new AnkerlDictionary<ulong, ulong>(source);
        var rng = new Rng(seed);
        for (int i = 0; i < LoopCount; i++)
        {
            dict.Remove(rng.Next());
        }
        return dict;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberSource)), BenchmarkCategory("Search", "Number", "Ankerl")]
    public AnkerlDictionary<ulong, ulong> SearchNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        var dict = new AnkerlDictionary<ulong, ulong>(source);
        ulong checksum = 0;

        var rng = new Rng(seed);
        for (int i = 0; i < LoopCount; i++)
        {
            if (dict.TryGetValue(rng.Next(), out var value))
                checksum += value;
        }
        return dict;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberSource)), BenchmarkCategory("SearchFail", "Number", "Ankerl")]
    public AnkerlDictionary<ulong, ulong> SearchFailNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        var dict = new AnkerlDictionary<ulong, ulong>(source);
        ulong checksum = 0;

        for (int i = 0; i < LoopCount; i++)
        {
            if (dict.TryGetValue((ulong)i, out var value))
                checksum += value;
        }
        return dict;
    }
}


