using System.IO;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace BenchmarkSuite1
{
    internal class Program
    {
        private static void Main()
        {
            ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
                .WithArtifactsPath(Path.Combine(Path.GetTempPath(), "BenchmarkSuite1"));
            Summary[] _ = BenchmarkRunner.Run(typeof(Program).Assembly, config);
        }
    }
}