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
        ///     Runs the benchmarks selected by the arguments, for example <c>--filter *BitmapFrame*</c>.
        ///     With no arguments, BenchmarkDotNet asks which benchmarks to run.
        ///     <c>--digest</c> and <c>--alloc</c> run the <see cref="ConsoleOutputBenchmarks" /> checks instead.
        /// </summary>
        private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--digest")
            {
                ConsoleOutputBenchmarks.PrintDigests();
                return;
            }

            if (args.Length == 1 && args[0] == "--alloc")
            {
                ConsoleOutputBenchmarks.PrintAllocations();
                return;
            }

            ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
                .WithArtifactsPath(Path.Combine(Path.GetTempPath(), "Consolonia.Benchmarks"));
            IEnumerable<Summary> _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}