using System;
using Avalonia;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Fonts;
using Consolonia.NUnit;

namespace Consolonia.Benchmarks
{
    /// <summary>Starts a Consolonia application for the frame benchmarks.</summary>
    internal static class BenchmarkLifetime
    {
        /// <summary>
        ///     Starts an application on <paramref name="console" /> in a new Avalonia locator scope.
        ///     Dispose the lifetime, then <paramref name="scope" />, when the benchmark is done.
        /// </summary>
        public static ConsoloniaLifetime Start(UnitTestConsole console, out IDisposable scope)
        {
            scope = AvaloniaLocator.EnterScope();
            ConsoloniaLifetime lifetime = ApplicationStartup.CreateLifetime(AppBuilder.Configure<BenchmarkApp>()
                .UseConsole(console)
                .UseConsolonia()
                .UseConsoleColorMode(new RgbConsoleColorMode())
                .WithConsoleFonts()
                .With<IPlatformSettings>(new ConsoloniaPlatformSettings
                {
                    UnsafeInput = false,
                    UnsafeRendering = false
                }), []);
            console.SetupLifetime(lifetime);
            return lifetime;
        }

        private sealed class BenchmarkApp : Application;
    }
}
