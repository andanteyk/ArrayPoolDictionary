using System;
using System.Collections.Generic;
using System.Diagnostics;
using ParallelDungeon.Rogue.Serialization;

public class Test
{
    public static void RunTest()
    {
        var dic = new IbukiDictionary<Guid, Guid>();

        var addedKeys = new List<Guid>();

        for (int i = 0; i < 1024; i++)
        {
            var key = Guid.NewGuid();

            dic.Add(key, key);
            addedKeys.Add(key);


            foreach (var k in addedKeys)
            {
                Debug.Assert(dic.ContainsKey(k));
            }
            Debug.Assert(dic.Count == i + 1);
        }


        for (int i = 0; i < 1024; i++)
        {
            Debug.Assert(dic[addedKeys[i]] == addedKeys[i]);
        }
        for (int i = 0; i < 1024; i++)
        {
            Debug.Assert(!dic.ContainsKey(Guid.NewGuid()));
        }


        for (int i = 0; i < 1024; i++)
        {
            var key = addedKeys[i];

            Debug.Assert(dic.Remove(key));
            Debug.Assert(!dic.ContainsKey(key));
        }

        Debug.Assert(dic.Count == 0);



        addedKeys.Clear();

        for (int i = 0; i < 1024; i++)
        {
            var key = Guid.NewGuid();

            dic.Add(key, key);
            addedKeys.Add(key);


            foreach (var k in addedKeys)
            {
                Debug.Assert(dic.ContainsKey(k));
            }
            Debug.Assert(dic.Count == i + 1);
        }


    }
}
