//#define QUICK_TEST

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ParallelDungeon.Rogue.Serialization;


[MemoryDiagnoser]
[AnyCategoriesFilter("Dictionary", "Ankerl", "SeparateChaining", "Hopscotch", "RobinFood", "SwissTable")]
#if QUICK_TEST
// [ShortRunJob]
[StopOnFirstError]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5, invocationCount: 100, id: "QuickJob")]
#else
#endif
public class WholeBenchmark
{
#pragma warning disable CS8618
    private Dictionary<Type, IDictionary<ulong, ulong>> m_NumberDictionaries;
    private Dictionary<Type, IDictionary<string, string>> m_StringDictionaries;

    private ImmutableDictionary<ulong, ulong> m_ImmutableNumber;
    private ImmutableDictionary<string, string> m_ImmutableString;
    private Champ64Dictionary<ulong, ulong> m_Champ64Number;
    private Champ64Dictionary<string, string> m_Champ64String;

    private FrozenDictionary<ulong, ulong> m_FrozenNumber;
    private FrozenDictionary<string, string> m_FrozenString;
#pragma warning restore

    private Rng Seed = new Rng(1234567890, 9876543210);

#if QUICK_TEST
    private const int PreGeneratedEntryCount = 1 << 4;
    private const int AddAndRemoveEntryCount = 1 << 4;
    private const int AddAndGetEntryCount = 1 << 4;
    private const int RandomAddAndRemoveEntryCount = 1 << 4;
    private const int IterateEntryCount = 1 << 4;

    public IEnumerable<object[]> FindNumberArguments()
    {
        yield return new object[] { 64, 0, 1 << 4 };
        yield return new object[] { 64, 1, 1 << 4 };
        yield return new object[] { 64, 2, 1 << 4 };
        yield return new object[] { 64, 3, 1 << 4 };
        yield return new object[] { 64, 4, 1 << 4 };
    }
#else
    private const int PreGeneratedEntryCount = 1 << 20;
    private const int AddAndRemoveEntryCount = 1 << 20;
    private const int AddAndGetEntryCount = 1 << 20;
    private const int RandomAddAndRemoveEntryCount = 1 << 20;
    private const int IterateEntryCount = 1 << 20;

    public IEnumerable<object[]> FindNumberArguments()
    {
        yield return new object[] { 64, 0, 1 << 20 };
        yield return new object[] { 64, 1, 1 << 20 };
        yield return new object[] { 64, 2, 1 << 20 };
        yield return new object[] { 64, 3, 1 << 20 };
        yield return new object[] { 64, 4, 1 << 20 };

        yield return new object[] { 8192, 0, 1 << 20 };
        yield return new object[] { 8192, 1, 1 << 20 };
        yield return new object[] { 8192, 2, 1 << 20 };
        yield return new object[] { 8192, 3, 1 << 20 };
        yield return new object[] { 8192, 4, 1 << 20 };

        yield return new object[] { 1 << 20, 0, 1 << 10 };
        yield return new object[] { 1 << 20, 1, 1 << 10 };
        yield return new object[] { 1 << 20, 2, 1 << 10 };
        yield return new object[] { 1 << 20, 3, 1 << 10 };
        yield return new object[] { 1 << 20, 4, 1 << 10 };
    }
#endif

    public IEnumerable<ulong> AddAndGetEntryMax()
    {
        yield return AddAndGetEntryCount / 16;
        yield return AddAndGetEntryCount / 4;
        yield return AddAndGetEntryCount / 2;
        yield return ~0ul;
    }

    public IEnumerable<ulong> RandomAddAndRemoveEntryMask()
    {
        yield return 0b1001000000000000000000000000000000000000000100000000000000001000;
        yield return 0b1001000000000010001100000000000000000000000101000000000000001000;
        yield return 0b1001000000000110001100000000000000010000000101100000000000001001;
        yield return 0b1001000000000110001100000001000000010000000101110000000100101001;
        yield return 0b1101100000000110001100001001000000010000000101110001000100101001;
        yield return 0b1101100000001110001100001001001000010000100101110001000100101011;
    }



    [GlobalSetup]
    public void GlobalSetup()
    {
        if (m_NumberDictionaries != null)
            return;

        m_NumberDictionaries = new()
        {
            { typeof(Dictionary<ulong, ulong>), new Dictionary<ulong, ulong>() },
            { typeof(AnkerlDictionary<ulong, ulong>), new AnkerlDictionary<ulong, ulong>() },
            { typeof(ArrayPoolDictionary<ulong, ulong>), new ArrayPoolDictionary<ulong, ulong>() },
            { typeof(HopscotchOverflowDictionary<ulong, ulong>), new HopscotchOverflowDictionary<ulong, ulong>() },
        //    { typeof(OpenAddressingDictionary<ulong, ulong>), new OpenAddressingDictionary<ulong, ulong>() },
            { typeof(RobinHoodDictionary<ulong, ulong>), new RobinHoodDictionary<ulong, ulong>() },
            { typeof(SwissTableDictionary<ulong, ulong>), new SwissTableDictionary<ulong, ulong>() },
        };

        m_StringDictionaries = new()
        {
            { typeof(Dictionary<string, string>), new Dictionary<string, string>() },
            { typeof(AnkerlDictionary<string, string>), new AnkerlDictionary<string, string>() },
            { typeof(ArrayPoolDictionary<string, string>), new ArrayPoolDictionary<string, string>() },
            { typeof(HopscotchOverflowDictionary<string, string>), new HopscotchOverflowDictionary<string, string>() },
        //    { typeof(OpenAddressingDictionary<string, string>), new OpenAddressingDictionary<string, string>() },
            { typeof(RobinHoodDictionary<string, string>), new RobinHoodDictionary<string, string>() },
            { typeof(SwissTableDictionary<string, string>), new SwissTableDictionary<string, string>() },
        };


        var rng = new Rng(Seed);
        foreach (var numberDic in m_NumberDictionaries.Values)
        {
            for (int i = 0; i < PreGeneratedEntryCount; i++)
            {
                numberDic.Add(rng.Next(), rng.Next());
            }
        }

        foreach (var stringDic in m_StringDictionaries.Values)
        {
            for (int i = 0; i < PreGeneratedEntryCount; i++)
            {
                stringDic.Add(rng.NextBase64(rng.NextInt(9, 21)), rng.NextBase64(rng.NextInt(9, 21)));
            }
        }
    }


    public static void Run()
    {
        var summary = BenchmarkRunner.Run<WholeBenchmark>();
    }



    private static TDic AddNumber<TDic>(int count, ulong max)
        where TDic : IDictionary<ulong, ulong>, new()
    {
        var dic = new TDic();

        for (int i = 0; i < count; i++)
        {
            dic.Add(Rng.Shared.NextUlong(max), Rng.Shared.NextUlong(max));
        }

        return dic;
    }

    private TDic AddAndRemoveNumber<TDic>(int count)
            where TDic : IDictionary<ulong, ulong>, new()
    {
        var dic = new TDic();
        var rng = new Rng(Seed);

        for (int i = 0; i < count; i++)
        {
            dic[rng.Next()] = rng.Next();
        }

        dic.Clear();
        rng = new Rng(Seed);

        for (int i = 0; i < count; i++)
        {
            dic[rng.Next()] = rng.Next();
        }

        rng = new Rng(Seed);
        for (int i = 0; i < count; i++)
        {
            dic.Remove(rng.Next());
            rng.Next();
        }

        return dic;
    }

    private TDic AddAndGetNumber<TDic>(int count, ulong max)
        where TDic : IDictionary<ulong, ulong>, new()
    {
        var dic = new TDic();
        var rng = new Rng(Seed);
        ulong checksum = 0;

        for (int i = 0; i < count; i++)
        {
            ulong key = rng.NextUlong(max);
            if (!dic.TryGetValue(key, out var value))
                value = 0;
            checksum += dic[key] = ++value;
        }

        return dic;
    }

    private TDic RandomAddAndRemoveNumber<TDic>(int count, ulong mask)
        where TDic : IDictionary<ulong, ulong>, new()
    {
        var dic = new TDic();
        var rng = new Rng(Seed);

        for (int i = 0; i < count; i++)
        {
            dic[rng.Next() & mask] = (ulong)i;
            dic.Remove(rng.Next() & mask);
        }

        return dic;
    }

    private TDic IterateNumber<TDic>(int count)
        where TDic : IDictionary<ulong, ulong>, new()
    {
        var dic = new TDic();
        var rng = new Rng(Seed);
        ulong checksum = 0ul;

        for (int i = 0; i < count; i++)
        {
            dic[rng.Next()] = (ulong)i;

            foreach (var pair in dic)
            {
                checksum += pair.Value;
            }
        }

        rng = new Rng(Seed);

        for (int i = 0; i < count; i++)
        {
            dic.Remove(rng.Next());

            foreach (var pair in dic)
            {
                checksum += pair.Value;
            }
        }

        return dic;
    }

    private TDic FindNumber<TDic>(int count, int entryCount, int lookupCount)
        where TDic : IDictionary<ulong, ulong>, new()
    {
        var dic = new TDic();
        var rng = new Rng(Seed);
        var freeRng = new Rng(rng.Next(), rng.Next());
        ulong checksum = 0ul;

        for (int i = 0; i < count; i++)
        {
            rng.SetState(Seed);
            for (int k = 0; k < 4; k++)
            {
                if (k < entryCount)
                {
                    dic[rng.Next()] = rng.Next();
                }
                else
                {
                    dic[freeRng.Next()] = freeRng.Next();
                }
            }

            rng.SetState(Seed);
            for (int k = 0; k < lookupCount; k++)
            {
                if (dic.TryGetValue(rng.Next(), out var value))
                {
                    checksum += value;
                }
            }
        }
        return dic;
    }

    private TDic AddAndRemoveString<TDic>(int count, int byteLength, int lookupCount)
        where TDic : IDictionary<string, string>, new()
    {
        var dic = new TDic();
        var rng = new Rng(Seed);
        int checksum = 0;

        for (int i = 0; i < count; i++)
        {
            dic[rng.NextBase64(byteLength)] = i.ToString();

            if (dic.Remove(rng.NextBase64(byteLength)))
                checksum++;
        }

        return dic;
    }

    private TDic FindString<TDic>(int count, int byteLength, int entryCount, int lookupCount)
        where TDic : IDictionary<string, string>, new()
    {
        var dic = new TDic();
        var rng = new Rng(Seed);
        var freeRng = new Rng(rng.Next(), rng.Next());
        ulong checksum = 0ul;

        for (int i = 0; i < count; i++)
        {
            rng.SetState(Seed);
            for (int k = 0; k < 4; k++)
            {
                if (k < entryCount)
                {
                    dic[rng.NextBase64(byteLength)] = i.ToString();
                }
                else
                {
                    dic[freeRng.NextBase64(byteLength)] = i.ToString();
                }
            }

            rng.SetState(Seed);
            for (int k = 0; k < lookupCount; k++)
            {
                if (dic.TryGetValue(rng.NextBase64(byteLength), out var value))
                {
                    checksum++;
                }
            }
        }
        return dic;
    }

    private static TDic AddString<TDic>(int count, int characterCount)
        where TDic : IDictionary<string, string>, new()
    {
        var dic = new TDic();

        for (int i = 0; i < count; i++)
        {
            dic.Add(Rng.Shared.NextBase64(characterCount), Rng.Shared.NextBase64(characterCount));
        }

        return dic;
    }




    [Benchmark, BenchmarkCategory("Copy", "Dictionary", "Number")]
    public Dictionary<ulong, ulong> CopyDictionaryNumber() => new Dictionary<ulong, ulong>(m_NumberDictionaries[typeof(Dictionary<ulong, ulong>)]);

    [Benchmark, BenchmarkCategory("Copy", "Ankerl", "Number")]
    public AnkerlDictionary<ulong, ulong> CopyAnkerlNumber() => new AnkerlDictionary<ulong, ulong>(m_NumberDictionaries[typeof(AnkerlDictionary<ulong, ulong>)]);

    [Benchmark, BenchmarkCategory("Copy", "SeparateChaining", "Number")]
    public ArrayPoolDictionary<ulong, ulong> CopySeparateChainingNumber() => new ArrayPoolDictionary<ulong, ulong>(m_NumberDictionaries[typeof(ArrayPoolDictionary<ulong, ulong>)]);

    [Benchmark, BenchmarkCategory("Copy", "Hopscotch", "Number")]
    public HopscotchOverflowDictionary<ulong, ulong> CopyHopscotchNumber() => new HopscotchOverflowDictionary<ulong, ulong>(m_NumberDictionaries[typeof(HopscotchOverflowDictionary<ulong, ulong>)]);

    [Benchmark, BenchmarkCategory("Copy", "OpenAddressing", "Number")]
    public OpenAddressingDictionary<ulong, ulong> CopyOpenAddressingNumber() => new OpenAddressingDictionary<ulong, ulong>(m_NumberDictionaries[typeof(OpenAddressingDictionary<ulong, ulong>)]);

    [Benchmark, BenchmarkCategory("Copy", "RobinFood", "Number")]
    public RobinHoodDictionary<ulong, ulong> CopyRobinFoodNumber() => new RobinHoodDictionary<ulong, ulong>(m_NumberDictionaries[typeof(RobinHoodDictionary<ulong, ulong>)]);

    [Benchmark, BenchmarkCategory("Copy", "SwissTable", "Number")]
    public SwissTableDictionary<ulong, ulong> CopySwissTableNumber() => new SwissTableDictionary<ulong, ulong>(m_NumberDictionaries[typeof(SwissTableDictionary<ulong, ulong>)]);


    [Benchmark, BenchmarkCategory("Copy", "Dictionary", "String")]
    public Dictionary<string, string> CopyDictionaryString() => new Dictionary<string, string>(m_StringDictionaries[typeof(Dictionary<string, string>)]);

    [Benchmark, BenchmarkCategory("Copy", "Ankerl", "String")]
    public AnkerlDictionary<string, string> CopyAnkerlString() => new AnkerlDictionary<string, string>(m_StringDictionaries[typeof(AnkerlDictionary<string, string>)]);

    [Benchmark, BenchmarkCategory("Copy", "SeparateChaining", "String")]
    public ArrayPoolDictionary<string, string> CopySeparateChainingString() => new ArrayPoolDictionary<string, string>(m_StringDictionaries[typeof(ArrayPoolDictionary<string, string>)]);

    [Benchmark, BenchmarkCategory("Copy", "Hopscotch", "String")]
    public HopscotchOverflowDictionary<string, string> CopyHopscotchString() => new HopscotchOverflowDictionary<string, string>(m_StringDictionaries[typeof(HopscotchOverflowDictionary<string, string>)]);

    [Benchmark, BenchmarkCategory("Copy", "OpenAddressing", "String")]
    public OpenAddressingDictionary<string, string> CopyOpenAddressingString() => new OpenAddressingDictionary<string, string>(m_StringDictionaries[typeof(OpenAddressingDictionary<string, string>)]);

    [Benchmark, BenchmarkCategory("Copy", "RobinFood", "String")]
    public RobinHoodDictionary<string, string> CopyRobinFoodString() => new RobinHoodDictionary<string, string>(m_StringDictionaries[typeof(RobinHoodDictionary<string, string>)]);

    [Benchmark, BenchmarkCategory("Copy", "SwissTable", "String")]
    public SwissTableDictionary<string, string> CopySwissTableString() => new SwissTableDictionary<string, string>(m_StringDictionaries[typeof(SwissTableDictionary<string, string>)]);




    [Benchmark, BenchmarkCategory("AddAndRemove", "Dictionary", "Number")]
    public Dictionary<ulong, ulong> AddAndRemoveDictionaryNumber() => AddAndRemoveNumber<Dictionary<ulong, ulong>>(AddAndRemoveEntryCount);

    [Benchmark, BenchmarkCategory("AddAndRemove", "Ankerl", "Number")]
    public AnkerlDictionary<ulong, ulong> AddAndRemoveAnkerlNumber() => AddAndRemoveNumber<AnkerlDictionary<ulong, ulong>>(AddAndRemoveEntryCount);

    [Benchmark, BenchmarkCategory("AddAndRemove", "SeparateChaining", "Number")]
    public ArrayPoolDictionary<ulong, ulong> AddAndRemoveSeparateChainingNumber() => AddAndRemoveNumber<ArrayPoolDictionary<ulong, ulong>>(AddAndRemoveEntryCount);

    [Benchmark, BenchmarkCategory("AddAndRemove", "Hopscotch", "Number")]
    public HopscotchOverflowDictionary<ulong, ulong> AddAndRemoveHopscotchNumber() => AddAndRemoveNumber<HopscotchOverflowDictionary<ulong, ulong>>(AddAndRemoveEntryCount);

    [Benchmark, BenchmarkCategory("AddAndRemove", "OpenAddressing", "Number")]
    public OpenAddressingDictionary<ulong, ulong> AddAndRemoveOpenAddressingNumber() => AddAndRemoveNumber<OpenAddressingDictionary<ulong, ulong>>(AddAndRemoveEntryCount);

    [Benchmark, BenchmarkCategory("AddAndRemove", "RobinFood", "Number")]
    public RobinHoodDictionary<ulong, ulong> AddAndRemoveRobinFoodNumber() => AddAndRemoveNumber<RobinHoodDictionary<ulong, ulong>>(AddAndRemoveEntryCount);

    [Benchmark, BenchmarkCategory("AddAndRemove", "SwissTable", "Number")]
    public SwissTableDictionary<ulong, ulong> AddAndRemoveSwissTableNumber() => AddAndRemoveNumber<SwissTableDictionary<ulong, ulong>>(AddAndRemoveEntryCount);




    [Benchmark, BenchmarkCategory("AddAndGet", "Dictionary", "Number"), ArgumentsSource(nameof(AddAndGetEntryMax))]
    public Dictionary<ulong, ulong> AddAndGetDictionaryNumber(ulong max) => AddAndGetNumber<Dictionary<ulong, ulong>>(AddAndGetEntryCount, max);

    [Benchmark, BenchmarkCategory("AddAndGet", "Ankerl", "Number"), ArgumentsSource(nameof(AddAndGetEntryMax))]
    public AnkerlDictionary<ulong, ulong> AddAndGetAnkerlNumber(ulong max) => AddAndGetNumber<AnkerlDictionary<ulong, ulong>>(AddAndGetEntryCount, max);

    [Benchmark, BenchmarkCategory("AddAndGet", "SeparateChaining", "Number"), ArgumentsSource(nameof(AddAndGetEntryMax))]
    public ArrayPoolDictionary<ulong, ulong> AddAndGetSeparateChainingNumber(ulong max) => AddAndGetNumber<ArrayPoolDictionary<ulong, ulong>>(AddAndGetEntryCount, max);

    [Benchmark, BenchmarkCategory("AddAndGet", "Hopscotch", "Number"), ArgumentsSource(nameof(AddAndGetEntryMax))]
    public HopscotchOverflowDictionary<ulong, ulong> AddAndGetHopscotchNumber(ulong max) => AddAndGetNumber<HopscotchOverflowDictionary<ulong, ulong>>(AddAndGetEntryCount, max);

    [Benchmark, BenchmarkCategory("AddAndGet", "OpenAddressing", "Number"), ArgumentsSource(nameof(AddAndGetEntryMax))]
    public OpenAddressingDictionary<ulong, ulong> AddAndGetOpenAddressingNumber(ulong max) => AddAndGetNumber<OpenAddressingDictionary<ulong, ulong>>(AddAndGetEntryCount, max);

    [Benchmark, BenchmarkCategory("AddAndGet", "RobinFood", "Number"), ArgumentsSource(nameof(AddAndGetEntryMax))]
    public RobinHoodDictionary<ulong, ulong> AddAndGetRobinFoodNumber(ulong max) => AddAndGetNumber<RobinHoodDictionary<ulong, ulong>>(AddAndGetEntryCount, max);

    [Benchmark, BenchmarkCategory("AddAndGet", "SwissTable", "Number"), ArgumentsSource(nameof(AddAndGetEntryMax))]
    public SwissTableDictionary<ulong, ulong> AddAndGetSwissTableNumber(ulong max) => AddAndGetNumber<SwissTableDictionary<ulong, ulong>>(AddAndGetEntryCount, max);




    [Benchmark, BenchmarkCategory("RandomAddAndRemove", "Dictionary", "Number"), ArgumentsSource(nameof(RandomAddAndRemoveEntryMask))]
    public Dictionary<ulong, ulong> RandomAddAndRemoveDictionaryNumber(ulong mask) => RandomAddAndRemoveNumber<Dictionary<ulong, ulong>>(RandomAddAndRemoveEntryCount, mask);

    [Benchmark, BenchmarkCategory("RandomAddAndRemove", "Ankerl", "Number"), ArgumentsSource(nameof(RandomAddAndRemoveEntryMask))]
    public AnkerlDictionary<ulong, ulong> RandomAddAndRemoveAnkerlNumber(ulong mask) => RandomAddAndRemoveNumber<AnkerlDictionary<ulong, ulong>>(RandomAddAndRemoveEntryCount, mask);

    [Benchmark, BenchmarkCategory("RandomAddAndRemove", "SeparateChaining", "Number"), ArgumentsSource(nameof(RandomAddAndRemoveEntryMask))]
    public ArrayPoolDictionary<ulong, ulong> RandomAddAndRemoveSeparateChainingNumber(ulong mask) => RandomAddAndRemoveNumber<ArrayPoolDictionary<ulong, ulong>>(RandomAddAndRemoveEntryCount, mask);

    [Benchmark, BenchmarkCategory("RandomAddAndRemove", "Hopscotch", "Number"), ArgumentsSource(nameof(RandomAddAndRemoveEntryMask))]
    public HopscotchOverflowDictionary<ulong, ulong> RandomAddAndRemoveHopscotchNumber(ulong mask) => RandomAddAndRemoveNumber<HopscotchOverflowDictionary<ulong, ulong>>(RandomAddAndRemoveEntryCount, mask);

    [Benchmark, BenchmarkCategory("RandomAddAndRemove", "OpenAddressing", "Number"), ArgumentsSource(nameof(RandomAddAndRemoveEntryMask))]
    public OpenAddressingDictionary<ulong, ulong> RandomAddAndRemoveOpenAddressingNumber(ulong mask) => RandomAddAndRemoveNumber<OpenAddressingDictionary<ulong, ulong>>(RandomAddAndRemoveEntryCount, mask);

    [Benchmark, BenchmarkCategory("RandomAddAndRemove", "RobinFood", "Number"), ArgumentsSource(nameof(RandomAddAndRemoveEntryMask))]
    public RobinHoodDictionary<ulong, ulong> RandomAddAndRemoveRobinFoodNumber(ulong mask) => RandomAddAndRemoveNumber<RobinHoodDictionary<ulong, ulong>>(RandomAddAndRemoveEntryCount, mask);

    [Benchmark, BenchmarkCategory("RandomAddAndRemove", "SwissTable", "Number"), ArgumentsSource(nameof(RandomAddAndRemoveEntryMask))]
    public SwissTableDictionary<ulong, ulong> RandomAddAndRemoveSwissTableNumber(ulong mask) => RandomAddAndRemoveNumber<SwissTableDictionary<ulong, ulong>>(RandomAddAndRemoveEntryCount, mask);




    [Benchmark, BenchmarkCategory("Iterate", "Dictionary", "Number")]
    public Dictionary<ulong, ulong> IterateDictionaryNumber() => IterateNumber<Dictionary<ulong, ulong>>(IterateEntryCount);

    [Benchmark, BenchmarkCategory("Iterate", "Ankerl", "Number")]
    public AnkerlDictionary<ulong, ulong> IterateAnkerlNumber() => IterateNumber<AnkerlDictionary<ulong, ulong>>(IterateEntryCount);

    [Benchmark, BenchmarkCategory("Iterate", "SeparateChaining", "Number")]
    public ArrayPoolDictionary<ulong, ulong> IterateSeparateChainingNumber() => IterateNumber<ArrayPoolDictionary<ulong, ulong>>(IterateEntryCount);

    [Benchmark, BenchmarkCategory("Iterate", "Hopscotch", "Number")]
    public HopscotchOverflowDictionary<ulong, ulong> IterateHopscotchNumber() => IterateNumber<HopscotchOverflowDictionary<ulong, ulong>>(IterateEntryCount);

    [Benchmark, BenchmarkCategory("Iterate", "OpenAddressing", "Number")]
    public OpenAddressingDictionary<ulong, ulong> IterateOpenAddressingNumber() => IterateNumber<OpenAddressingDictionary<ulong, ulong>>(IterateEntryCount);

    [Benchmark, BenchmarkCategory("Iterate", "RobinFood", "Number")]
    public RobinHoodDictionary<ulong, ulong> IterateRobinFoodNumber() => IterateNumber<RobinHoodDictionary<ulong, ulong>>(IterateEntryCount);

    [Benchmark, BenchmarkCategory("Iterate", "SwissTable", "Number")]
    public SwissTableDictionary<ulong, ulong> IterateSwissTableNumber() => IterateNumber<SwissTableDictionary<ulong, ulong>>(IterateEntryCount);




    [Benchmark, BenchmarkCategory("Find", "Dictionary", "Number"), ArgumentsSource(nameof(FindNumberArguments))]
    public Dictionary<ulong, ulong> FindDictionaryNumber(int count, int entryCount, int lookupCount) => FindNumber<Dictionary<ulong, ulong>>(count, entryCount, lookupCount);

    [Benchmark, BenchmarkCategory("Find", "Ankerl", "Number"), ArgumentsSource(nameof(FindNumberArguments))]
    public AnkerlDictionary<ulong, ulong> FindAnkerlNumber(int count, int entryCount, int lookupCount) => FindNumber<AnkerlDictionary<ulong, ulong>>(count, entryCount, lookupCount);

    [Benchmark, BenchmarkCategory("Find", "SeparateChaining", "Number"), ArgumentsSource(nameof(FindNumberArguments))]
    public ArrayPoolDictionary<ulong, ulong> FindSeparateChainingNumber(int count, int entryCount, int lookupCount) => FindNumber<ArrayPoolDictionary<ulong, ulong>>(count, entryCount, lookupCount);

    [Benchmark, BenchmarkCategory("Find", "Hopscotch", "Number"), ArgumentsSource(nameof(FindNumberArguments))]
    public HopscotchOverflowDictionary<ulong, ulong> FindHopscotchNumber(int count, int entryCount, int lookupCount) => FindNumber<HopscotchOverflowDictionary<ulong, ulong>>(count, entryCount, lookupCount);

    [Benchmark, BenchmarkCategory("Find", "OpenAddressing", "Number"), ArgumentsSource(nameof(FindNumberArguments))]
    public OpenAddressingDictionary<ulong, ulong> FindOpenAddressingNumber(int count, int entryCount, int lookupCount) => FindNumber<OpenAddressingDictionary<ulong, ulong>>(count, entryCount, lookupCount);

    [Benchmark, BenchmarkCategory("Find", "RobinFood", "Number"), ArgumentsSource(nameof(FindNumberArguments))]
    public RobinHoodDictionary<ulong, ulong> FindRobinFoodNumber(int count, int entryCount, int lookupCount) => FindNumber<RobinHoodDictionary<ulong, ulong>>(count, entryCount, lookupCount);

    [Benchmark, BenchmarkCategory("Find", "SwissTable", "Number"), ArgumentsSource(nameof(FindNumberArguments))]
    public SwissTableDictionary<ulong, ulong> FindSwissTableNumber(int count, int entryCount, int lookupCount) => FindNumber<SwissTableDictionary<ulong, ulong>>(count, entryCount, lookupCount);


}
