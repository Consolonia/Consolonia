using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Consolonia.Controls;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class SliderTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            await UITest.KeyInput(Key.Tab);
            await UITest.AssertHasText(" 🠷\ufe0E─────");
            await UITest.KeyInput(Key.Right);
            await UITest.KeyInput(Key.Right);
            await UITest.AssertHasText(" ─🠷\ufe0E────");
        }

        [TestCase(Orientation.Horizontal)]
        [TestCase(Orientation.Vertical)]
        public async Task CaretTracksSliderFocusWithNonControlDataContext(Orientation orientation)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = (MainWindow)((IClassicDesktopStyleApplicationLifetime)
                    Application.Current.ApplicationLifetime).MainWindow;
                Slider slider = window.GetVisualDescendants().OfType<Slider>()
                    .First(control => control.Orientation == orientation);
                slider.DataContext = new object();
                Thumb thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
                CaretControl caret = thumb.GetVisualDescendants().OfType<CaretControl>().Single();

                Assert.IsTrue(slider.Focus());
                Assert.IsTrue(caret.IsCaretShown);

                Slider other = window.GetVisualDescendants().OfType<Slider>()
                    .First(control => control != slider);
                Assert.IsTrue(other.Focus());
                Assert.IsFalse(slider.IsFocused);
                Assert.IsFalse(caret.IsCaretShown);
            });
        }
    }
}