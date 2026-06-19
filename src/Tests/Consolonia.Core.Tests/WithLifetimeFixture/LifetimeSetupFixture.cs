using System;
using Avalonia;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Dummy;
using Consolonia.Core.Infrastructure;
using Consolonia.Fonts;
using Consolonia.NUnit;
using NUnit.Framework;
using static System.GC;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [SetUpFixture]
    public class LifetimeSetupFixture : IDisposable
    {
        private bool _disposedValue;
        private ConsoloniaLifetime _lifetime;

        private IDisposable _scope;

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            SuppressFinalize(this);
        }

        [OneTimeSetUp]
        public void Setup()
        {
            var console = new UnitTestConsole(new PixelBufferSize(80, 25));
            _scope = AvaloniaLocator.EnterScope();
            _lifetime = ApplicationStartup.CreateLifetime(AppBuilder.Configure<ContextApp2>()
                .UseConsole(console)
                .UseConsolonia()
                .UseConsoleColorMode(new RgbConsoleColorMode())
                .WithConsoleFonts()
                .With<IPlatformSettings>(new ConsoloniaPlatformSettings
                {
                    UnsafeInput = false,
                    UnsafeRendering = false
                })
                .LogToException(), []);
            console.SetupLifetime(_lifetime);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            Dispose();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects)
                    _scope?.Dispose();
                    _lifetime?.Dispose();
                    _scope = null;
                    _lifetime = null;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }

        private class ContextApp2 : Application;
    }
}