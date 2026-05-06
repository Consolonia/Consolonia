using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Fonts;
using Consolonia.Gallery.View;
using Consolonia.ManagedWindows;
using Consolonia.NUnit;
using Iciclecreek.Avalonia.WindowManager;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests.Base
{
    internal class GalleryTestsBaseBase : ConsoloniaAppTestBase<App>
    {
        protected GalleryTestsBaseBase(PixelBufferSize size = default) : base(size.IsEmpty
            ? new PixelBufferSize(80, 40)
            : size)
        {
            Args = new string[2];
            Args[1] = GetType().Name[..^5];
        }

        protected override AppBuilder CreateAppBuilder()
        {
            return base.CreateAppBuilder()
                .UseManagedWindows()
                .WithConsoleFonts();
        }

        [OneTimeSetUp]
        public async Task Setup()
        {
            // Close any ManagedWindows left open by a previous test
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var mainWindow =
                    ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime)!.MainWindow!;
                var windowsPanel = mainWindow.FindDescendantOfType<WindowsPanel>();
                if (windowsPanel != null)
                {
                    foreach (var child in windowsPanel.Windows.OfType<ManagedWindow>().ToList())
                        child.Close();
                }
            });

            await UITest.WaitRendered();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ControlsListView controlsListView = GetControlsListAndMainWindow();
                controlsListView!.ChangeTo(Args);
            });

            await UITest.WaitRendered();

            await UITest.KeyInput(Key.Tab); // focusing first element within gallery item (if present)

            bool focusStillOnTheList = false;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ControlsListView controlsListView = GetControlsListAndMainWindow();
                focusStillOnTheList = controlsListView.GalleryGrid.IsKeyboardFocusWithin;
            });

            if (focusStillOnTheList) // todo: F393122D-9623-4535-A87A-F031C2769386 Avalonia bug, ListBox does not receive focus at first time as supposed (but sometimes does)
                await UITest.KeyInput(Key.Tab);
            return;

            ControlsListView GetControlsListAndMainWindow()
            {
                var mainWindow =
                    (MainWindow)
                    ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime)!
                    .MainWindow!;
                var controlsListView = mainWindow.FindDescendantOfType<ControlsListView>();
                return controlsListView;
            }
        }
    }
}