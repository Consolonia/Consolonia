using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Consolonia.Themes;

namespace Consolonia.Gallery
{
    internal class App : Application
    {
        internal const string TurboVisionProgramParameter = "-turbovision";

        static App()
        {
            // we want tests and UI to be executed with same culture
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (((ConsoloniaLifetime)ApplicationLifetime).IsRgbColorMode()
                && !((ConsoloniaLifetime)ApplicationLifetime).Args.Any(argument => argument != null &&
                    argument.EndsWith(TurboVisionProgramParameter, StringComparison.OrdinalIgnoreCase)))
                Styles.Add(new ModernTheme());
            else
                Styles.Add(new TurboVisionTheme());

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow(); // designer runs as classic desktop

            base.OnFrameworkInitializationCompleted();
        }
    }
}