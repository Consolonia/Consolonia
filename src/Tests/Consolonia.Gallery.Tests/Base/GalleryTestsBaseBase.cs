using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Fonts;
using Consolonia.Gallery.View;
using Consolonia.NUnit;
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
                .WithConsoleFonts();
        }

        [OneTimeSetUp]
        public async Task Setup()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ControlsListView controlsListView = GetControlsListAndMainWindow();
                controlsListView!.ChangeTo(Args);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ControlsListView controlsListView = GetControlsListAndMainWindow();
                FocusFirstGalleryControl(controlsListView);
            });

            await UITest.WaitRendered();
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

            static void FocusFirstGalleryControl(ControlsListView controlsListView)
            {
                Control focusTarget = controlsListView.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(IsGalleryContentFocusTarget);

                focusTarget?.Focus(NavigationMethod.Tab);

                static bool IsGalleryContentFocusTarget(Control control)
                {
                    return control.Focusable &&
                           control.IsEffectivelyVisible &&
                           control is not ListBox { Name: nameof(ControlsListView.GalleryGrid) } &&
                           !control.GetVisualAncestors().OfType<ListBox>()
                               .Any(listBox => listBox.Name == nameof(ControlsListView.GalleryGrid));
                }
            }
        }
    }
}
