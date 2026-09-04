using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Running;
using System.Reflection;

var config = ManualConfig.CreateMinimumViable()
    .AddDiagnoser(MemoryDiagnoser.Default)
    //.AddExporter(DefaultExporters.Plain)
    .AddExporter(MarkdownExporter.Default)
    .AddJob(Job.ShortRun);

//BenchmarkRunner.Run<Silesia_GZip>(config, args);
BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args, config);
