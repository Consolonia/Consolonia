using Avalonia;
using Consolonia;
using Consolonia.Fonts;

namespace Sandbox
{
    public static class Program
    {
        private static void Main(string[] args)
        {
            BuildAvaloniaApp()
                .StartWithConsoleLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseConsolonia()
                .UseAutoDetectedConsole()
                .UseMonochromeConsoleColorMode()
                .WithConsoleFonts()
                .WithDeveloperTools()
                .LogToException();
        }
    }
}