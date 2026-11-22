using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class AppThreadSynchronizationContextTests : ConsoloniaAppTestBase<App>
    {
        public AppThreadSynchronizationContextTests() : base(new PixelBufferSize(80, 40))
        {
            Args = Array.Empty<string>();
        }

        // The UI thread is shared by every app this base starts, so the second case runs on a
        // thread that has already been through a shutdown. Normal priority is the one the
        // dispatcher leaves the thread's own context in place for.
        [TestCase(1)]
        [TestCase(2)]
        public async Task AwaitOnUiThreadResumes(int appCycle)
        {
            Task resumed = Dispatcher.UIThread.InvokeAsync(async () => await Task.Yield(),
                DispatcherPriority.Normal);

            Task finished = await Task.WhenAny(resumed, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.That(finished, Is.SameAs(resumed),
                $"await on the UI thread did not resume in app cycle {appCycle}");
        }
    }
}