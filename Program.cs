using System.Collections.Generic;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ParallelDungeon.Rogue.Serialization;
using System.Collections.Immutable;
using System;
using System.Diagnostics;
using BenchmarkDotNet.Configs;
using System.Linq;
using MemoryPack;
using System.Diagnostics.CodeAnalysis;
using System.Collections;


#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif


Test.RunTest();



//var summary = BenchmarkRunner.Run<WholeBenchmark3>();
var summary = BenchmarkSwitcher.FromTypes(new Type[] { typeof(WholeBenchmark4<,>) }).Run();
return;













/*
var summary = BenchmarkRunner.Run<CompareClass>();
//*/


[AnyCategoriesFilter("Champ", "Immutable", "Champ64")]
[MemoryDiagnoser, StopOnFirstError]
public class CompareClass
{
#pragma warning disable CS8618
    public static Dictionary<Vector3, Vector3> m_dict;
    public static ImmutableDictionary<Vector3, Vector3> m_immutable;
    public static ArrayPoolDictionary<Vector3, Vector3> m_pool;
    public static MergedArrayPoolDictionary<Vector3, Vector3> m_merged;
    public static HopscotchDictionary<Vector3, Vector3> m_hopscotch;
    public static HopscotchSeparatedDictionary<Vector3, Vector3> m_separated;
    public static HopscotchOverflowDictionary<Vector3, Vector3> m_overflow;
    public static OpenAddressingDictionary<Vector3, Vector3> m_openAddressing;
    public static SwissTableDictionary<Vector3, Vector3> m_swissTable;
    public static RobinHoodDictionary<Vector3, Vector3> m_robinFood;
    public static AnkerlDictionary<Vector3, Vector3> m_ankerl;
    public static ChampDictionary<Vector3, Vector3> m_champ;
    public static Champ64Dictionary<Vector3, Vector3> m_champ64;

#if NET8_0_OR_GREATER
    public static FrozenDictionary<Vector3, Vector3> m_frozen;
#endif
#pragma warning restore

    public class WorstHasher
    {
        public int Value { get; set; }
        public override int GetHashCode()
        {
            return 0;
        }

        public override bool Equals(object? obj)
        {
            return obj is WorstHasher other && Value == other.Value;
        }
    }



    static CompareClass()
    {
        m_dict = new Dictionary<Vector3, Vector3>();
        m_pool = new ArrayPoolDictionary<Vector3, Vector3>();
        m_merged = new MergedArrayPoolDictionary<Vector3, Vector3>();
        m_hopscotch = new HopscotchDictionary<Vector3, Vector3>();
        m_separated = new HopscotchSeparatedDictionary<Vector3, Vector3>();
        m_overflow = new HopscotchOverflowDictionary<Vector3, Vector3>();
        m_openAddressing = new OpenAddressingDictionary<Vector3, Vector3>();
        m_swissTable = new SwissTableDictionary<Vector3, Vector3>();
        m_robinFood = new RobinHoodDictionary<Vector3, Vector3>();
        m_ankerl = new AnkerlDictionary<Vector3, Vector3>();

        for (int i = 0; i < 50; i++)
        {
            m_dict.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_pool.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_merged.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_hopscotch.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_separated.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_overflow.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_openAddressing.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_swissTable.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_robinFood.Add(new Vector3(i, i, i), new Vector3(i, i, i));
            m_ankerl.Add(new Vector3(i, i, i), new Vector3(i, i, i));
        }

        m_immutable = m_dict.ToImmutableDictionary();
        m_champ = ChampDictionary<Vector3, Vector3>.Create(m_dict);
        m_champ64 = Champ64Dictionary<Vector3, Vector3>.Create(m_dict);

#if NET8_0_OR_GREATER
        m_frozen = m_dict.ToFrozenDictionary();
#endif
    }


    [Benchmark, BenchmarkCategory("Original", "new")]
    public Dictionary<Vector3, Vector3> DictionaryNew()
    {
        var dict = new Dictionary<Vector3, Vector3>(m_dict);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Immutable", "new")]
    public ImmutableDictionary<Vector3, Vector3> ImmutableNew()
    {
        var dict = m_dict.ToImmutableDictionary();

        return dict;
    }


#if NET8_0_OR_GREATER
    [Benchmark, BenchmarkCategory("Frozen", "new")]
    public FrozenDictionary<Vector3, Vector3> FrozenNew()
    {
        var dict = m_dict.ToFrozenDictionary();

        return dict;
    }
#endif

    [Benchmark, BenchmarkCategory("ArrayPool", "new")]
    public ArrayPoolDictionary<Vector3, Vector3> ArrayPoolNew()
    {
        using var dict = new ArrayPoolDictionary<Vector3, Vector3>(m_pool);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Merged", "new")]
    public MergedArrayPoolDictionary<Vector3, Vector3> MergedNew()
    {
        using var dict = new MergedArrayPoolDictionary<Vector3, Vector3>(m_merged);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Hopscotch", "new")]
    public HopscotchDictionary<Vector3, Vector3> HopscotchNew()
    {
        using var dict = new HopscotchDictionary<Vector3, Vector3>(m_hopscotch);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Separated", "new")]
    public HopscotchSeparatedDictionary<Vector3, Vector3> SeparatedNew()
    {
        using var dict = new HopscotchSeparatedDictionary<Vector3, Vector3>(m_separated);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Overflow", "new")]
    public HopscotchOverflowDictionary<Vector3, Vector3> OverflowNew()
    {
        using var dict = new HopscotchOverflowDictionary<Vector3, Vector3>(m_overflow);

        return dict;
    }

    [Benchmark, BenchmarkCategory("OpenAddressing", "new")]
    public OpenAddressingDictionary<Vector3, Vector3> OpenAddressingNew()
    {
        using var dict = new OpenAddressingDictionary<Vector3, Vector3>(m_openAddressing);

        return dict;
    }

    [Benchmark, BenchmarkCategory("SwissTable", "new")]
    public SwissTableDictionary<Vector3, Vector3> SwissTableNew()
    {
        using var dict = new SwissTableDictionary<Vector3, Vector3>(m_swissTable);

        return dict;
    }

    [Benchmark, BenchmarkCategory("RobinFood", "new")]
    public RobinHoodDictionary<Vector3, Vector3> RobinFoodNew()
    {
        using var dict = new RobinHoodDictionary<Vector3, Vector3>(m_robinFood);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Ankerl", "new")]
    public AnkerlDictionary<Vector3, Vector3> AnkerlNew()
    {
        using var dict = new AnkerlDictionary<Vector3, Vector3>(m_ankerl);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Champ", "new")]
    public ChampDictionary<Vector3, Vector3> ChampNew()
    {
        var dict = ChampDictionary<Vector3, Vector3>.Create(m_dict);

        return dict;
    }

    [Benchmark, BenchmarkCategory("Champ64", "new")]
    public Champ64Dictionary<Vector3, Vector3> Champ64New()
    {
        var dict = Champ64Dictionary<Vector3, Vector3>.Create(m_dict);

        return dict;
    }


    [Benchmark, BenchmarkCategory("Original", "add")]
    public Dictionary<Vector3, Vector3> DictionaryAdd()
    {
        var dict = new Dictionary<Vector3, Vector3>(m_dict);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Immutable", "add")]
    public ImmutableDictionary<Vector3, Vector3> ImmutableAdd()
    {
        var dict = m_immutable;

        dict = dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }


    [Benchmark, BenchmarkCategory("ArrayPool", "add")]
    public ArrayPoolDictionary<Vector3, Vector3> ArrayPoolAdd()
    {
        using var dict = new ArrayPoolDictionary<Vector3, Vector3>(m_pool);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Merged", "add")]
    public MergedArrayPoolDictionary<Vector3, Vector3> MergedAdd()
    {
        using var dict = new MergedArrayPoolDictionary<Vector3, Vector3>(m_merged);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Hopscotch", "add")]
    public HopscotchDictionary<Vector3, Vector3> HopscotchAdd()
    {
        using var dict = new HopscotchDictionary<Vector3, Vector3>(m_hopscotch);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Separated", "add")]
    public HopscotchSeparatedDictionary<Vector3, Vector3> SeparatedAdd()
    {
        using var dict = new HopscotchSeparatedDictionary<Vector3, Vector3>(m_separated);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Overflow", "add")]
    public HopscotchOverflowDictionary<Vector3, Vector3> OverflowAdd()
    {
        using var dict = new HopscotchOverflowDictionary<Vector3, Vector3>(m_overflow);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("OpenAddressing", "add")]
    public OpenAddressingDictionary<Vector3, Vector3> OpenAddressingAdd()
    {
        using var dict = new OpenAddressingDictionary<Vector3, Vector3>(m_openAddressing);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("SwissTable", "add")]
    public SwissTableDictionary<Vector3, Vector3> SwissTableAdd()
    {
        using var dict = new SwissTableDictionary<Vector3, Vector3>(m_swissTable);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("RobinFood", "add")]
    public RobinHoodDictionary<Vector3, Vector3> RobinFoodAdd()
    {
        using var dict = new RobinHoodDictionary<Vector3, Vector3>(m_robinFood);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Ankerl", "add")]
    public AnkerlDictionary<Vector3, Vector3> AnkerlAdd()
    {
        using var dict = new AnkerlDictionary<Vector3, Vector3>(m_ankerl);

        dict.Add(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Champ", "add")]
    public ChampDictionary<Vector3, Vector3> ChampAdd()
    {
        var dict = m_champ;

        dict = dict.AddEntry(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Champ64", "add")]
    public Champ64Dictionary<Vector3, Vector3> Champ64Add()
    {
        var dict = m_champ64;

        dict = dict.AddEntry(new Vector3(97, 97, 97), new Vector3(97, 97, 97));

        return dict;
    }



    [Benchmark, BenchmarkCategory("Original", "remove")]
    public Dictionary<Vector3, Vector3> DictionaryRemove()
    {
        var dict = new Dictionary<Vector3, Vector3>(m_dict);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Immutable", "remove")]
    public ImmutableDictionary<Vector3, Vector3> ImmutableRemove()
    {
        var dict = m_immutable;

        dict = dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("ArrayPool", "remove")]
    public ArrayPoolDictionary<Vector3, Vector3> ArrayPoolRemove()
    {
        using var dict = new ArrayPoolDictionary<Vector3, Vector3>(m_pool);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Merged", "remove")]
    public MergedArrayPoolDictionary<Vector3, Vector3> MergedRemove()
    {
        using var dict = new MergedArrayPoolDictionary<Vector3, Vector3>(m_merged);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Hopscotch", "remove")]
    public HopscotchDictionary<Vector3, Vector3> HopscotchRemove()
    {
        using var dict = new HopscotchDictionary<Vector3, Vector3>(m_hopscotch);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Separated", "remove")]
    public HopscotchSeparatedDictionary<Vector3, Vector3> SeparatedRemove()
    {
        using var dict = new HopscotchSeparatedDictionary<Vector3, Vector3>(m_separated);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Overflow", "remove")]
    public HopscotchOverflowDictionary<Vector3, Vector3> OverflowRemove()
    {
        using var dict = new HopscotchOverflowDictionary<Vector3, Vector3>(m_overflow);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("OpenAddressing", "remove")]
    public OpenAddressingDictionary<Vector3, Vector3> OpenAddressingRemove()
    {
        using var dict = new OpenAddressingDictionary<Vector3, Vector3>(m_openAddressing);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("SwissTable", "remove")]
    public SwissTableDictionary<Vector3, Vector3> SwissTableRemove()
    {
        using var dict = new SwissTableDictionary<Vector3, Vector3>(m_swissTable);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("RobinFood", "remove")]
    public RobinHoodDictionary<Vector3, Vector3> RobinFoodRemove()
    {
        using var dict = new RobinHoodDictionary<Vector3, Vector3>(m_robinFood);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Ankerl", "remove")]
    public AnkerlDictionary<Vector3, Vector3> AnkerlRemove()
    {
        using var dict = new AnkerlDictionary<Vector3, Vector3>(m_ankerl);

        dict.Remove(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Champ", "remove")]
    public ChampDictionary<Vector3, Vector3> ChampRemove()
    {
        var dict = m_champ;

        dict = dict.RemoveEntry(new Vector3(27, 27, 27));

        return dict;
    }

    [Benchmark, BenchmarkCategory("Champ64", "remove")]
    public Champ64Dictionary<Vector3, Vector3> Champ64Remove()
    {
        var dict = m_champ64;

        dict = dict.RemoveEntry(new Vector3(27, 27, 27));

        return dict;
    }



    [Benchmark, BenchmarkCategory("Original", "search")]
    public Vector3 DictionarySearch()
    {
        return m_dict[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Immutable", "search")]
    public Vector3 ImmutableSearch()
    {
        return m_immutable[new Vector3(27, 27, 27)];
    }

#if NET8_0_OR_GREATER
    [Benchmark, BenchmarkCategory("Frozen", "search")]
    public Vector3 FrozenSearch()
    {
        return m_frozen[new Vector3(27, 27, 27)];
    }
#endif

    [Benchmark, BenchmarkCategory("ArrayPool", "search")]
    public Vector3 ArrayPoolSearch()
    {
        return m_pool[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Merged", "search")]
    public Vector3 MergedSearch()
    {
        return m_merged[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Hopscotch", "search")]
    public Vector3 HopscotchSearch()
    {
        return m_hopscotch[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Separated", "search")]
    public Vector3 SeparatedSearch()
    {
        return m_separated[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Overflow", "search")]
    public Vector3 OverflowSearch()
    {
        return m_overflow[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("OpenAddressing", "search")]
    public Vector3 OpenAddressingSearch()
    {
        return m_openAddressing[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("SwissTable", "search")]
    public Vector3 SwissTableSearch()
    {
        return m_swissTable[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("RobinFood", "search")]
    public Vector3 RobinFoodSearch()
    {
        return m_robinFood[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Ankerl", "search")]
    public Vector3 AnkerlSearch()
    {
        return m_ankerl[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Champ", "search")]
    public Vector3 ChampSearch()
    {
        return m_champ[new Vector3(27, 27, 27)];
    }

    [Benchmark, BenchmarkCategory("Champ64", "search")]
    public Vector3 Champ64Search()
    {
        return m_champ64[new Vector3(27, 27, 27)];
    }


    [Benchmark, BenchmarkCategory("Original", "worst")]
    public Dictionary<WorstHasher, Vector3> DictionaryWorst()
    {
        var dict = new Dictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }

    [Benchmark, BenchmarkCategory("ArrayPool", "worst")]
    public ArrayPoolDictionary<WorstHasher, Vector3> ArrayPoolWorst()
    {
        using var dict = new ArrayPoolDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }


    [Benchmark, BenchmarkCategory("Merged", "worst")]
    public MergedArrayPoolDictionary<WorstHasher, Vector3> MergedWorst()
    {
        using var dict = new MergedArrayPoolDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }

    /*
    [Benchmark, BenchmarkCategory("Hopscotch", "worst")]
    public HopscotchDictionary<WorstHasher, Vector3> HopscotchWorst()
    {
        var dict = new HopscotchDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }

    [Benchmark, BenchmarkCategory("Separated", "worst")]
    public HopscotchSeparatedDictionary<WorstHasher, Vector3> SeparatedWorst()
    {
        var dict = new HopscotchSeparatedDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }
    */

    [Benchmark, BenchmarkCategory("Overflow", "worst")]
    public HopscotchOverflowDictionary<WorstHasher, Vector3> OverflowWorst()
    {
        var dict = new HopscotchOverflowDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }

    [Benchmark, BenchmarkCategory("OpenAddressing", "worst")]
    public OpenAddressingDictionary<WorstHasher, Vector3> OpenAddressingWorst()
    {
        var dict = new OpenAddressingDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }

    [Benchmark, BenchmarkCategory("SwissTable", "worst")]
    public SwissTableDictionary<WorstHasher, Vector3> SwissTableWorst()
    {
        var dict = new SwissTableDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }

    [Benchmark, BenchmarkCategory("RobinFood", "worst")]
    public RobinHoodDictionary<WorstHasher, Vector3> RobinFoodWorst()
    {
        var dict = new RobinHoodDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }

    [Benchmark, BenchmarkCategory("Ankerl", "worst")]
    public AnkerlDictionary<WorstHasher, Vector3> AnkerlWorst()
    {
        var dict = new AnkerlDictionary<WorstHasher, Vector3>();

        for (int i = 0; i < 256; i++)
        {
            dict.Add(new WorstHasher { Value = i }, new Vector3(i, i, i));
        }

        for (int i = 128; i < 256; i++)
        {
            dict.Remove(new WorstHasher { Value = i });
        }

        return dict;
    }
}

public class SomeClass<TKey, TValue> : IDictionary<TKey, TValue>
{
    public TValue this[TKey key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public ICollection<TKey> Keys => throw new NotImplementedException();

    public ICollection<TValue> Values => throw new NotImplementedException();

    public int Count => throw new NotImplementedException();

    public bool IsReadOnly => throw new NotImplementedException();

    public void Add(TKey key, TValue value)
    {
        throw new NotImplementedException();
    }

    public void Add(KeyValuePair<TKey, TValue> item)
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

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    public bool Remove(TKey key)
    {
        throw new NotImplementedException();
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
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