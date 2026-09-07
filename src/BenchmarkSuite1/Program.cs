using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using System.IO;

namespace BenchmarkSuite1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var config = ManualConfig.Create(DefaultConfig.Instance)
                .WithArtifactsPath(Path.Combine(Path.GetTempPath(), "BenchmarkSuite1"));
            var _ = BenchmarkRunner.Run(typeof(Program).Assembly, config);
        }
    }
}
