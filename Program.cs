using BenchmarkDotNet.Running;
using System;


Test.RunTest();


//var summary = BenchmarkRunner.Run<WholeBenchmark3>();
var summary = BenchmarkSwitcher.FromTypes(new Type[] { typeof(WholeBenchmark4<,>) }).Run();
return;

