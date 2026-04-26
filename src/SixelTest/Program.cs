using System;
using Avalonia;
using Consolonia;

namespace SixelTest
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            BuildAvaloniaApp()
                .StartWithConsoleLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseSkia()
                .UseSixelFramebuffer()
                .UseAutoDetectedConsole();
        }
    }
}
