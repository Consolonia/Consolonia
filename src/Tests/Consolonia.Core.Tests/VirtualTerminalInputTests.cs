using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class VirtualTerminalInputTests
    {
        [Test]
        public void EnableAndDisposeNeverThrow()
        {
            // Under the test runner (redirected console) and on other operating systems this
            // returns a no-restore scope; either way enabling and disposing must be safe.
            Assert.DoesNotThrow(() =>
            {
                using (VirtualTerminalInput.Enable())
                {
                }
            });
        }

        [Test]
        public void DefaultScopeDisposeIsSafeAndIdempotent()
        {
            VirtualTerminalInput.Scope scope = default;

            Assert.DoesNotThrow(() =>
            {
                scope.Dispose();
                scope.Dispose();
            });
        }

        [Test]
        public void DisposingScopeTwiceIsSafe()
        {
            VirtualTerminalInput.Scope scope = VirtualTerminalInput.Enable();

            Assert.DoesNotThrow(() =>
            {
                scope.Dispose();
                scope.Dispose();
            });
        }
    }
}
