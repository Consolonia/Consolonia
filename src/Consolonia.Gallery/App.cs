using System;
using System.Globalization;
using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Consolonia.Core.Infrastructure;
using Consolonia.Gallery.View;
using Consolonia.Themes.TurboVision.Templates;

namespace Consolonia.Gallery
{
    internal class App : ConsoloniaApplication
    {
        static App()
        {
            // we want tests and UI to be executed with same culture
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        }

        public App()
        {
            Styles.Add(new TurboVisionTheme(new Uri("avares://Consolonia.Gallery")));
            Styles.Add(new Consolonia.Modal.ModalTheme(new Uri("avares://Consolonia.Gallery")));
        }

        public override void OnFrameworkInitializationCompleted()
        {
            ((IClassicDesktopStyleApplicationLifetime)ApplicationLifetime)!.MainWindow = new ControlsListView();
            base.OnFrameworkInitializationCompleted();
        }
    }
}