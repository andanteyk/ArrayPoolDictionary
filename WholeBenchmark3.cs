using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using ParallelDungeon.Rogue.Serialization;

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true)]
[AnyCategoriesFilter("Ankerl", "AnkerlMod2")]
//[SimpleJob(RunStrategy.ColdStart, launchCount: 1, warmupCount: 5, iterationCount: 5, id: "FastAndDirtyJob")]
public class WholeBenchmark3
{

    private static List<IDictionary<ulong, ulong>> m_NumberDictionaries;
    private static List<IDictionary<string, string>> m_StringDictionaries;

    private static List<IReadOnlyDictionary<ulong, ulong>> m_NumberReadonlyDictionaries;
    private static List<IReadOnlyDictionary<string, string>> m_StringReadonlyDictionaries;

    const int StringByteLength = 12;

    static WholeBenchmark3()
    {
        m_NumberDictionaries = new();
        m_StringDictionaries = new();
        m_NumberReadonlyDictionaries = new();
        m_StringReadonlyDictionaries = new();

        void AddNumber(Func<IDictionary<ulong, ulong>> ctor)
        {
            // m_NumberDictionaries.Add(ctor());

            for (int i = 1 << 20; i <= 1 << 20; i <<= 1)
            {
                var dic = ctor();
                dic[~0ul] = ~0ul;
                while (dic.Count < i)
                {
                    dic[Rng.Shared.Next()] = Rng.Shared.Next();
                }
                m_NumberDictionaries.Add(dic);
            }

            /*
            for (int i = 100000; i <= 1000000; i += 100000)
            {
                var dic = ctor();
                dic[~0ul] = ~0ul;
                while (dic.Count < i)
                {
                    dic[Rng.Shared.Next()] = Rng.Shared.Next();
                }
                m_NumberDictionaries.Add(dic);
            }
            //*/
        }

        void AddString(Func<IDictionary<string, string>> ctor)
        {
            // m_StringDictionaries.Add(ctor());

            for (int i = 1 << 20; i <= 1 << 20; i <<= 1)
            {
                var dic = ctor();
                dic["aaaaaaaaaaaaaaaa"] = "aaaaaaaaaaaaaaaa";
                while (dic.Count < i)
                {
                    dic[Rng.Shared.NextBase64(StringByteLength)] = Rng.Shared.NextBase64(StringByteLength);
                }
                m_StringDictionaries.Add(dic);
            }

            /*
            for (int i = 100000; i <= 1000000; i += 100000)
            {
                var dic = ctor();
                dic["aaaaaaaaaaaaaaaa"] = "aaaaaaaaaaaaaaaa";
                while (dic.Count < i)
                {
                    dic[Rng.Shared.NextBase64(StringByteLength)] = Rng.Shared.NextBase64(StringByteLength);
                }
                m_StringDictionaries.Add(dic);
            }
            //*/
        }

        AddNumber(() => new Dictionary<ulong, ulong>());
        AddNumber(() => new AnkerlDictionary<ulong, ulong>());
        AddNumber(() => new AnkerlDictionaryMod<ulong, ulong>());
        AddNumber(() => new AnkerlDictionaryMod2<ulong, ulong>());
        AddNumber(() => new ArrayPoolDictionary<ulong, ulong>());
        AddNumber(() => new HopscotchOverflowDictionary<ulong, ulong>());
        AddNumber(() => new OpenAddressingDictionary<ulong, ulong>());
        AddNumber(() => new RobinHoodDictionary<ulong, ulong>());
        AddNumber(() => new SwissTableDictionary<ulong, ulong>());
        AddNumber(() => new SwissTableDictionaryRevisited<ulong, ulong>());


        foreach (var dic in m_NumberDictionaries.OfType<Dictionary<ulong, ulong>>())
        {
            m_NumberReadonlyDictionaries.Add(dic.AsReadOnly());
            m_NumberReadonlyDictionaries.Add(dic.ToImmutableDictionary());
            m_NumberReadonlyDictionaries.Add(dic.ToFrozenDictionary());
            m_NumberReadonlyDictionaries.Add(Champ64Dictionary<ulong, ulong>.Create(dic));
        }

        Console.WriteLine("number ready");

        AddString(() => new Dictionary<string, string>());
        AddString(() => new AnkerlDictionary<string, string>());
        AddString(() => new AnkerlDictionaryMod<string, string>());
        AddString(() => new AnkerlDictionaryMod2<string, string>());
        AddString(() => new ArrayPoolDictionary<string, string>());
        AddString(() => new HopscotchOverflowDictionary<string, string>());
        AddString(() => new OpenAddressingDictionary<string, string>());
        AddString(() => new RobinHoodDictionary<string, string>());
        AddString(() => new SwissTableDictionary<string, string>());
        AddString(() => new SwissTableDictionaryRevisited<string, string>());


        foreach (var dic in m_StringDictionaries.OfType<Dictionary<string, string>>())
        {
            m_StringReadonlyDictionaries.Add(dic.AsReadOnly());
            m_StringReadonlyDictionaries.Add(dic.ToImmutableDictionary());
            m_StringReadonlyDictionaries.Add(dic.ToFrozenDictionary());
            m_StringReadonlyDictionaries.Add(Champ64Dictionary<string, string>.Create(dic));
        }

        Console.WriteLine("string ready");
    }


    public IEnumerable<object> DictionaryNumberArguments() => m_NumberDictionaries.OfType<Dictionary<ulong, ulong>>();
    public IEnumerable<object> AnkerlNumberArguments() => m_NumberDictionaries.OfType<AnkerlDictionary<ulong, ulong>>();
    public IEnumerable<object> AnkerlModNumberArguments() => m_NumberDictionaries.OfType<AnkerlDictionaryMod<ulong, ulong>>();
    public IEnumerable<object> AnkerlMod2NumberArguments() => m_NumberDictionaries.OfType<AnkerlDictionaryMod2<ulong, ulong>>();
    public IEnumerable<object> ArrayPoolNumberArguments() => m_NumberDictionaries.OfType<ArrayPoolDictionary<ulong, ulong>>();
    public IEnumerable<object> HopscotchNumberArguments() => m_NumberDictionaries.OfType<HopscotchOverflowDictionary<ulong, ulong>>();
    public IEnumerable<object> OpenAddressingNumberArguments() => m_NumberDictionaries.OfType<OpenAddressingDictionary<ulong, ulong>>();
    public IEnumerable<object> RobinHoodNumberArguments() => m_NumberDictionaries.OfType<RobinHoodDictionary<ulong, ulong>>();
    public IEnumerable<object> SwissTableNumberArguments() => m_NumberDictionaries.OfType<SwissTableDictionary<ulong, ulong>>();
    public IEnumerable<object> SwissTableRevisitedNumberArguments() => m_NumberDictionaries.OfType<SwissTableDictionaryRevisited<ulong, ulong>>();

    public IEnumerable<object> ReadOnlyNumberArguments() => m_NumberReadonlyDictionaries.OfType<ReadOnlyDictionary<ulong, ulong>>();
    public IEnumerable<object> ImmutableNumberArguments() => m_NumberReadonlyDictionaries.OfType<ImmutableDictionary<ulong, ulong>>();
    public IEnumerable<object> FrozenNumberArguments() => m_NumberReadonlyDictionaries.OfType<FrozenDictionary<ulong, ulong>>();
    public IEnumerable<object> Champ64NumberArguments() => m_NumberReadonlyDictionaries.OfType<Champ64Dictionary<ulong, ulong>>();

    public IEnumerable<object> DictionaryStringArguments() => m_StringDictionaries.OfType<Dictionary<string, string>>();
    public IEnumerable<object> AnkerlStringArguments() => m_StringDictionaries.OfType<AnkerlDictionary<string, string>>();
    public IEnumerable<object> AnkerlModStringArguments() => m_StringDictionaries.OfType<AnkerlDictionaryMod<string, string>>();
    public IEnumerable<object> AnkerlMod2StringArguments() => m_StringDictionaries.OfType<AnkerlDictionaryMod2<string, string>>();
    public IEnumerable<object> ArrayPoolStringArguments() => m_StringDictionaries.OfType<ArrayPoolDictionary<string, string>>();
    public IEnumerable<object> HopscotchStringArguments() => m_StringDictionaries.OfType<HopscotchOverflowDictionary<string, string>>();
    public IEnumerable<object> OpenAddressingStringArguments() => m_StringDictionaries.OfType<OpenAddressingDictionary<string, string>>();
    public IEnumerable<object> RobinHoodStringArguments() => m_StringDictionaries.OfType<RobinHoodDictionary<string, string>>();
    public IEnumerable<object> SwissTableStringArguments() => m_StringDictionaries.OfType<SwissTableDictionary<string, string>>();
    public IEnumerable<object> SwissTableRevisitedStringArguments() => m_StringDictionaries.OfType<SwissTableDictionaryRevisited<string, string>>();

    public IEnumerable<object> ReadOnlyStringArguments() => m_StringReadonlyDictionaries.OfType<ReadOnlyDictionary<string, string>>();
    public IEnumerable<object> ImmutableStringArguments() => m_StringReadonlyDictionaries.OfType<ImmutableDictionary<string, string>>();
    public IEnumerable<object> FrozenStringArguments() => m_StringReadonlyDictionaries.OfType<FrozenDictionary<string, string>>();
    public IEnumerable<object> Champ64StringArguments() => m_StringReadonlyDictionaries.OfType<Champ64Dictionary<string, string>>();




    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TDic AddRemoveNumber<TDic>(TDic source)
        where TDic : IDictionary<ulong, ulong>
    {
        ulong key = Rng.Shared.Next();
        source[key] = Rng.Shared.Next();
        source.Remove(key);

        return source;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TDic AddRemoveString<TDic>(TDic source)
        where TDic : IDictionary<string, string>
    {
        string key = Rng.Shared.NextBase64(StringByteLength);
        source[key] = Rng.Shared.NextBase64(StringByteLength);
        source.Remove(key);

        return source;
    }




    [Benchmark, ArgumentsSource(nameof(AnkerlNumberArguments)), BenchmarkCategory("Ankerl", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberArguments)), BenchmarkCategory("Ankerl", "Number")]
    public ulong SearchRandomNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlNumberArguments)), BenchmarkCategory("Ankerl", "Number")]
    public ulong SearchExistNumberAnkerl(AnkerlDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlStringArguments)), BenchmarkCategory("Ankerl", "String")]
    public IDictionary<string, string> AddRemoveStringAnkerl(AnkerlDictionary<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlStringArguments)), BenchmarkCategory("Ankerl", "String")]
    public string SearchRandomStringAnkerl(AnkerlDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlStringArguments)), BenchmarkCategory("Ankerl", "String")]
    public string SearchExistStringAnkerl(AnkerlDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }





    [Benchmark, ArgumentsSource(nameof(AnkerlModNumberArguments)), BenchmarkCategory("AnkerlMod", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberAnkerlMod(AnkerlDictionaryMod<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlModNumberArguments)), BenchmarkCategory("AnkerlMod", "Number")]
    public ulong SearchRandomNumberAnkerlMod(AnkerlDictionaryMod<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlModNumberArguments)), BenchmarkCategory("AnkerlMod", "Number")]
    public ulong SearchExistNumberAnkerlMod(AnkerlDictionaryMod<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlModStringArguments)), BenchmarkCategory("AnkerlMod", "String")]
    public IDictionary<string, string> AddRemoveStringAnkerlMod(AnkerlDictionaryMod<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlModStringArguments)), BenchmarkCategory("AnkerlMod", "String")]
    public string SearchRandomStringAnkerlMod(AnkerlDictionaryMod<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlModStringArguments)), BenchmarkCategory("AnkerlMod", "String")]
    public string SearchExistStringAnkerlMod(AnkerlDictionaryMod<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }




    [Benchmark, ArgumentsSource(nameof(AnkerlMod2NumberArguments)), BenchmarkCategory("AnkerlMod2", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberAnkerlMod2(AnkerlDictionaryMod2<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlMod2NumberArguments)), BenchmarkCategory("AnkerlMod2", "Number")]
    public ulong SearchRandomNumberAnkerlMod2(AnkerlDictionaryMod2<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlMod2NumberArguments)), BenchmarkCategory("AnkerlMod2", "Number")]
    public ulong SearchExistNumberAnkerlMod2(AnkerlDictionaryMod2<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlMod2StringArguments)), BenchmarkCategory("AnkerlMod2", "String")]
    public IDictionary<string, string> AddRemoveStringAnkerlMod2(AnkerlDictionaryMod2<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlMod2StringArguments)), BenchmarkCategory("AnkerlMod2", "String")]
    public string SearchRandomStringAnkerlMod2(AnkerlDictionaryMod2<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(AnkerlMod2StringArguments)), BenchmarkCategory("AnkerlMod2", "String")]
    public string SearchExistStringAnkerlMod2(AnkerlDictionaryMod2<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }






    [Benchmark, ArgumentsSource(nameof(ArrayPoolNumberArguments)), BenchmarkCategory("ArrayPool", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberArrayPool(ArrayPoolDictionary<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(ArrayPoolNumberArguments)), BenchmarkCategory("ArrayPool", "Number")]
    public ulong SearchRandomNumberArrayPool(ArrayPoolDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(ArrayPoolNumberArguments)), BenchmarkCategory("ArrayPool", "Number")]
    public ulong SearchExistNumberArrayPool(ArrayPoolDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(ArrayPoolStringArguments)), BenchmarkCategory("ArrayPool", "String")]
    public IDictionary<string, string> AddRemoveStringArrayPool(ArrayPoolDictionary<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(ArrayPoolStringArguments)), BenchmarkCategory("ArrayPool", "String")]
    public string SearchRandomStringArrayPool(ArrayPoolDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(ArrayPoolStringArguments)), BenchmarkCategory("ArrayPool", "String")]
    public string SearchExistStringArrayPool(ArrayPoolDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }





    [Benchmark, ArgumentsSource(nameof(HopscotchNumberArguments)), BenchmarkCategory("Hopscotch", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberHopscotch(HopscotchOverflowDictionary<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(HopscotchNumberArguments)), BenchmarkCategory("Hopscotch", "Number")]
    public ulong SearchRandomNumberHopscotch(HopscotchOverflowDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(HopscotchNumberArguments)), BenchmarkCategory("Hopscotch", "Number")]
    public ulong SearchExistNumberHopscotch(HopscotchOverflowDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(HopscotchStringArguments)), BenchmarkCategory("Hopscotch", "String")]
    public IDictionary<string, string> AddRemoveStringHopscotch(HopscotchOverflowDictionary<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(HopscotchStringArguments)), BenchmarkCategory("Hopscotch", "String")]
    public string SearchRandomStringHopscotch(HopscotchOverflowDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(HopscotchStringArguments)), BenchmarkCategory("Hopscotch", "String")]
    public string SearchExistStringHopscotch(HopscotchOverflowDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }




    [Benchmark, ArgumentsSource(nameof(OpenAddressingNumberArguments)), BenchmarkCategory("OpenAddressing", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberOpenAddressing(OpenAddressingDictionary<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(OpenAddressingNumberArguments)), BenchmarkCategory("OpenAddressing", "Number")]
    public ulong SearchRandomNumberOpenAddressing(OpenAddressingDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(OpenAddressingNumberArguments)), BenchmarkCategory("OpenAddressing", "Number")]
    public ulong SearchExistNumberOpenAddressing(OpenAddressingDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(OpenAddressingStringArguments)), BenchmarkCategory("OpenAddressing", "String")]
    public IDictionary<string, string> AddRemoveStringOpenAddressing(OpenAddressingDictionary<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(OpenAddressingStringArguments)), BenchmarkCategory("OpenAddressing", "String")]
    public string SearchRandomStringOpenAddressing(OpenAddressingDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(OpenAddressingStringArguments)), BenchmarkCategory("OpenAddressing", "String")]
    public string SearchExistStringOpenAddressing(OpenAddressingDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }



    [Benchmark, ArgumentsSource(nameof(RobinHoodNumberArguments)), BenchmarkCategory("RobinHood", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberRobinHood(RobinHoodDictionary<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(RobinHoodNumberArguments)), BenchmarkCategory("RobinHood", "Number")]
    public ulong SearchRandomNumberRobinHood(RobinHoodDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(RobinHoodNumberArguments)), BenchmarkCategory("RobinHood", "Number")]
    public ulong SearchExistNumberRobinHood(RobinHoodDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(RobinHoodStringArguments)), BenchmarkCategory("RobinHood", "String")]
    public IDictionary<string, string> AddRemoveStringRobinHood(RobinHoodDictionary<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(RobinHoodStringArguments)), BenchmarkCategory("RobinHood", "String")]
    public string SearchRandomStringRobinHood(RobinHoodDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(RobinHoodStringArguments)), BenchmarkCategory("RobinHood", "String")]
    public string SearchExistStringRobinHood(RobinHoodDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }




    [Benchmark, ArgumentsSource(nameof(SwissTableNumberArguments)), BenchmarkCategory("SwissTable", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberSwissTable(SwissTableDictionary<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableNumberArguments)), BenchmarkCategory("SwissTable", "Number")]
    public ulong SearchRandomNumberSwissTable(SwissTableDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableNumberArguments)), BenchmarkCategory("SwissTable", "Number")]
    public ulong SearchExistNumberSwissTable(SwissTableDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableStringArguments)), BenchmarkCategory("SwissTable", "String")]
    public IDictionary<string, string> AddRemoveStringSwissTable(SwissTableDictionary<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableStringArguments)), BenchmarkCategory("SwissTable", "String")]
    public string SearchRandomStringSwissTable(SwissTableDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableStringArguments)), BenchmarkCategory("SwissTable", "String")]
    public string SearchExistStringSwissTable(SwissTableDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }



    [Benchmark, ArgumentsSource(nameof(SwissTableRevisitedNumberArguments)), BenchmarkCategory("SwissTableRevisited", "Number")]
    public IDictionary<ulong, ulong> AddRemoveNumberSwissTableRevisited(SwissTableDictionaryRevisited<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableRevisitedNumberArguments)), BenchmarkCategory("SwissTableRevisited", "Number")]
    public ulong SearchRandomNumberSwissTableRevisited(SwissTableDictionaryRevisited<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableRevisitedNumberArguments)), BenchmarkCategory("SwissTableRevisited", "Number")]
    public ulong SearchExistNumberSwissTableRevisited(SwissTableDictionaryRevisited<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableRevisitedStringArguments)), BenchmarkCategory("SwissTableRevisited", "String")]
    public IDictionary<string, string> AddRemoveStringSwissTableRevisited(SwissTableDictionaryRevisited<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableRevisitedStringArguments)), BenchmarkCategory("SwissTableRevisited", "String")]
    public string SearchRandomStringSwissTableRevisited(SwissTableDictionaryRevisited<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(SwissTableRevisitedStringArguments)), BenchmarkCategory("SwissTableRevisited", "String")]
    public string SearchExistStringSwissTableRevisited(SwissTableDictionaryRevisited<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }



    [Benchmark, ArgumentsSource(nameof(DictionaryNumberArguments)), BenchmarkCategory("Dictionary", "Number")]
    public IDictionary<ulong, ulong> DictionaryAddRemoveNumber(Dictionary<ulong, ulong> source)
    {
        return AddRemoveNumber(source);
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryNumberArguments)), BenchmarkCategory("Dictionary", "Number")]
    public ulong DictionarySearchRandomNumber(Dictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryNumberArguments)), BenchmarkCategory("Dictionary", "Number")]
    public ulong DictionarySearchExistNumber(Dictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryStringArguments)), BenchmarkCategory("Dictionary", "String")]
    public IDictionary<string, string> DictionaryAddRemoveString(Dictionary<string, string> source)
    {
        return AddRemoveString(source);
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryStringArguments)), BenchmarkCategory("Dictionary", "String")]
    public string DictionarySearchRandomString(Dictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryStringArguments)), BenchmarkCategory("Dictionary", "String")]
    public string DictionarySearchExistString(Dictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }








    [Benchmark, ArgumentsSource(nameof(DictionaryNumberArguments)), BenchmarkCategory("ReadOnly", "Number")]
    public IDictionary<ulong, ulong> ReadOnlyNumberCreate(Dictionary<ulong, ulong> source)
    {
        return source.AsReadOnly();
    }

    [Benchmark, ArgumentsSource(nameof(ReadOnlyNumberArguments)), BenchmarkCategory("ReadOnly", "Number")]
    public ulong ReadOnlySearchRandomNumber(ReadOnlyDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(ReadOnlyNumberArguments)), BenchmarkCategory("ReadOnly", "Number")]
    public ulong ReadOnlySearchExistNumber(ReadOnlyDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryStringArguments)), BenchmarkCategory("ReadOnly", "String")]
    public IDictionary<string, string> ReadOnlyStringCreate(Dictionary<string, string> source)
    {
        return source.AsReadOnly();
    }

    [Benchmark, ArgumentsSource(nameof(ReadOnlyStringArguments)), BenchmarkCategory("ReadOnly", "String")]
    public string ReadOnlySearchRandomString(ReadOnlyDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(ReadOnlyStringArguments)), BenchmarkCategory("ReadOnly", "String")]
    public string ReadOnlySearchExistString(ReadOnlyDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }




    [Benchmark, ArgumentsSource(nameof(DictionaryNumberArguments)), BenchmarkCategory("Immutable", "Number")]
    public IDictionary<ulong, ulong> ImmutableNumberCreate(Dictionary<ulong, ulong> source)
    {
        return source.ToImmutableDictionary();
    }

    [Benchmark, ArgumentsSource(nameof(ImmutableNumberArguments)), BenchmarkCategory("Immutable", "Number")]
    public ulong ImmutableSearchRandomNumber(ImmutableDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(ImmutableNumberArguments)), BenchmarkCategory("Immutable", "Number")]
    public ulong ImmutableSearchExistNumber(ImmutableDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryStringArguments)), BenchmarkCategory("Immutable", "String")]
    public IDictionary<string, string> ImmutableStringCreate(Dictionary<string, string> source)
    {
        return source.ToImmutableDictionary();
    }

    [Benchmark, ArgumentsSource(nameof(ImmutableStringArguments)), BenchmarkCategory("Immutable", "String")]
    public string ImmutableSearchRandomString(ImmutableDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(ImmutableStringArguments)), BenchmarkCategory("Immutable", "String")]
    public string ImmutableSearchExistString(ImmutableDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }




    [Benchmark, ArgumentsSource(nameof(DictionaryNumberArguments)), BenchmarkCategory("Frozen", "Number")]
    public IDictionary<ulong, ulong> FrozenNumberCreate(Dictionary<ulong, ulong> source)
    {
        return source.ToFrozenDictionary();
    }

    [Benchmark, ArgumentsSource(nameof(FrozenNumberArguments)), BenchmarkCategory("Frozen", "Number")]
    public ulong FrozenSearchRandomNumber(FrozenDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(FrozenNumberArguments)), BenchmarkCategory("Frozen", "Number")]
    public ulong FrozenSearchExistNumber(FrozenDictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryStringArguments)), BenchmarkCategory("Frozen", "String")]
    public IDictionary<string, string> FrozenStringCreate(Dictionary<string, string> source)
    {
        return source.ToFrozenDictionary();
    }

    [Benchmark, ArgumentsSource(nameof(FrozenStringArguments)), BenchmarkCategory("Frozen", "String")]
    public string FrozenSearchRandomString(FrozenDictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(FrozenStringArguments)), BenchmarkCategory("Frozen", "String")]
    public string FrozenSearchExistString(FrozenDictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }





    [Benchmark, ArgumentsSource(nameof(DictionaryNumberArguments)), BenchmarkCategory("Champ64", "Number")]
    public Champ64Dictionary<ulong, ulong> Champ64NumberCreate(Dictionary<ulong, ulong> source)
    {
        return Champ64Dictionary<ulong, ulong>.Create(source);
    }

    [Benchmark, ArgumentsSource(nameof(Champ64NumberArguments)), BenchmarkCategory("Champ64", "Number")]
    public ulong Champ64SearchRandomNumber(Champ64Dictionary<ulong, ulong> source)
    {
        return source.TryGetValue(Rng.Shared.Next(), out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(Champ64NumberArguments)), BenchmarkCategory("Champ64", "Number")]
    public ulong Champ64SearchExistNumber(Champ64Dictionary<ulong, ulong> source)
    {
        return source.TryGetValue(~0ul, out var value) ? value : 0;
    }

    [Benchmark, ArgumentsSource(nameof(DictionaryStringArguments)), BenchmarkCategory("Champ64", "String")]
    public Champ64Dictionary<string, string> Champ64StringCreate(Dictionary<string, string> source)
    {
        return Champ64Dictionary<string, string>.Create(source);
    }

    [Benchmark, ArgumentsSource(nameof(Champ64StringArguments)), BenchmarkCategory("Champ64", "String")]
    public string Champ64SearchRandomString(Champ64Dictionary<string, string> source)
    {
        return source.TryGetValue(Rng.Shared.NextBase64(StringByteLength), out var value) ? value : string.Empty;
    }

    [Benchmark, ArgumentsSource(nameof(Champ64StringArguments)), BenchmarkCategory("Champ64", "String")]
    public string Champ64SearchExistString(Champ64Dictionary<string, string> source)
    {
        return source.TryGetValue("aaaaaaaaaaaaaaaa", out var value) ? value : string.Empty;
    }


}
