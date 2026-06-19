using System;
using Avalonia;
using Avalonia.Platform;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public class MiscTests
    {
        [Test]
        public void TestExceptionRequest()
        {
            const int supportedRequestCode = -1;
            const NotSupportedRequestCode notSupportedRequestCode = (NotSupportedRequestCode)supportedRequestCode;
            Assert.IsFalse(Enum.IsDefined(notSupportedRequestCode),
                $"Code {supportedRequestCode} is reserved for internal use: tests");

            var request = new NotSupportedRequest(notSupportedRequestCode, Array.Empty<object>());
            try
            {
                throw new ConsoloniaNotSupportedException(request, typeof(object));
            }
            catch (ConsoloniaNotSupportedException ex)
            {
                Assert.AreEqual(request.ErrorCode, ex.Request.ErrorCode);
                Assert.IsFalse(request.Handled);
            }
        }

        [Test]
        public void ConsoloniaScreenUsesStablePlatformHandle()
        {
            var bounds = new PixelRect(0, 0, 80, 25);
            var screenImpl = new ConsoloniaScreen(bounds);

            Screen screen = screenImpl.AllScreens[0];

            Assert.AreEqual(1, screenImpl.ScreenCount);
            Assert.AreEqual("Console", screen.DisplayName);
            Assert.AreEqual(bounds, screen.Bounds);
            Assert.AreEqual(bounds, screen.WorkingArea);
            Assert.DoesNotThrow(() => screen.GetHashCode());
            Assert.DoesNotThrow(() => screen.Equals(screen));
        }
    }
}
