using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    internal class ConsoloniaAppTestBaseTests : ConsoloniaAppTestBase<LifecycleTestApp>
    {
        private static Application _firstApplication;

        public ConsoloniaAppTestBaseTests() : base(new PixelBufferSize(20, 10))
        {
        }

        [Test]
        [Order(1)]
        public void StartsApplicationForFirstTest()
        {
            _firstApplication = Application.Current;

            Assert.That(_firstApplication, Is.Not.Null);
        }

        [Test]
        [Order(2)]
        public void StartsFreshApplicationForNextTest()
        {
            Assert.That(Application.Current, Is.Not.Null);
            Assert.That(Application.Current, Is.Not.SameAs(_firstApplication));
        }
    }

    internal sealed class LifecycleTestApp : Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new Window();

            base.OnFrameworkInitializationCompleted();
        }
    }

    [NonParallelizable]
    internal class ConsoloniaAppTestThreadTests
    {
        [Test]
        [Timeout(5000)]
        public async Task ContinuesProcessingAfterWorkItemFailure()
        {
            var expectedException = new InvalidOperationException("Expected test failure");
            Task failedTask = ConsoloniaAppTestThread.Queue(() => throw expectedException);

            InvalidOperationException actualException =
                Assert.ThrowsAsync<InvalidOperationException>(async () => await failedTask)!;
            Assert.That(actualException, Is.SameAs(expectedException));

            bool nextWorkItemRan = false;
            Task successfulTask = ConsoloniaAppTestThread.Queue(() => nextWorkItemRan = true);

            await successfulTask;
            Assert.That(nextWorkItemRan, Is.True);
        }
    }
}
