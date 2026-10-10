using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Consolonia.Benchmarks
{
    internal class Program
    {
        /// <summary>
        ///     Runs the benchmarks the arguments select, e.g. <c>--filter *SixelFrame*</c>; with none,
        ///     BenchmarkDotNet asks which to run.
        /// </summary>
        private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--digest")
            {
                ConsoleOutputBenchmarks.PrintDigests();
                return;
            }

            ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
                .WithArtifactsPath(Path.Combine(Path.GetTempPath(), "Consolonia.Benchmarks"));
            IEnumerable<Summary> _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
