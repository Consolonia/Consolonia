using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.NUnit
{
    [NonParallelizable /*todo: switch to semaphore like https://stackoverflow.com/a/6427425/2362847 to allow other tests to execute in parallel*/]
    public abstract class ConsoloniaAppTestBase<TApp>
        where TApp : Application, new()
    {
        // Claude:
        // ReSharper disable StaticMemberInGenericType
        // A single dedicated (background) thread is reused for every test of a given TApp, and only the
        // Application/Dispatcher C# objects running on top of it are recreated per test. Avalonia's
        // Dispatcher.UIThread is a process wide singleton bound to whichever physical thread first touches
        // it, and once shut down it cannot be reused on a *different* thread, so we can't just spin up a
        // brand-new ThreadPool thread for every test (see https://github.com/Consolonia/Consolonia/issues/679).
        // This mirrors how Avalonia's own HeadlessUnitTestSession keeps one persistent dispatcher thread
        // for the whole test session.
        private static readonly BlockingCollection<Action> AppThreadQueue = new();

        static ConsoloniaAppTestBase()
        {
            var thread = new Thread(() =>
            {
                foreach (Action action in AppThreadQueue.GetConsumingEnumerable()) action();
            })
            {
                IsBackground = true,
                Name = $"{nameof(ConsoloniaAppTestBase<TApp>)}<{typeof(TApp).Name}> UI Thread"
            };
            thread.Start();
        }
        // ReSharper restore StaticMemberInGenericType

        private readonly PixelBufferSize _size;
        private IDisposable _scope;
        private TaskCompletionSource _disposeTaskCompletionSource;
        private ConsoloniaLifetime _lifetime;

        protected ConsoloniaAppTestBase(PixelBufferSize size)
        {
            _size = size;
        }

#pragma warning disable CA1819 // todo: provide a solution
        protected string[] Args { get; init; }
#pragma warning restore CA1819

        protected UnitTestConsole UITest { get; private set; }

        private static void ResetDispatcherForUnitTests()
        {
            MethodInfo method = typeof(Dispatcher).GetMethod("ResetForUnitTests",
                BindingFlags.Static | BindingFlags.NonPublic);
            method!.Invoke(null, null);
        }

        protected virtual AppBuilder CreateAppBuilder()
        {
            return AppBuilder.Configure<TApp>()
                .UseConsole(UITest)
                .UseConsolonia()
                .UseConsoleColorMode(new RgbConsoleColorMode())
                .With<IPlatformSettings>(new ConsoloniaPlatformSettings
                {
                    UnsafeInput = false,
                    UnsafeRendering = false
                })
                .LogToException();
        }

        [SetUp]
        public async Task SetUpApp()
        {
            UITest = new UnitTestConsole(_size);
            var setupTaskSource = new TaskCompletionSource();

            AppThreadQueue.Add(() =>
            {
                _disposeTaskCompletionSource = new TaskCompletionSource();
                
                ResetDispatcherForUnitTests();

                _scope = AvaloniaLocator.EnterScope();
                _lifetime = ApplicationStartup.CreateLifetime(CreateAppBuilder(), Args);
                UITest.SetupLifetime(_lifetime);
                setupTaskSource.SetResult();
                _lifetime.Start(Args);

                // Resetting static of AppBuilderBase so AppBuilder.Configure<TApp>() can be called again
                // by the next test.
                typeof(AppBuilder).GetField("s_setupWasAlreadyCalled",
                        BindingFlags.Static | BindingFlags.NonPublic)!
                    .SetValue(null, false);
                _disposeTaskCompletionSource.SetResult();
            });

            await setupTaskSource.Task.ConfigureAwait(true);

            // Waiting Main Window To appear
            CancellationToken cancellationToken = new CancellationTokenSource(60000 /*todo: magic number*/).Token;
            await Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool windowFound = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Window mainWindow = _lifetime?.MainWindow;
                        return mainWindow != null;
                    });
                    if (windowFound)
                        return;
                }
            }, cancellationToken).ConfigureAwait(true);

            // Waiting all jobs to finish
            await UITest.WaitRendered().ConfigureAwait(true);
        }

        [TearDown]
        public async Task TearDownApp()
        {
            ConsoloniaLifetime lifetime = _lifetime;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lifetime.Shutdown();
                lifetime.Dispose();
            }).GetTask().ConfigureAwait(true);

            _lifetime = null;
            
            await _disposeTaskCompletionSource.Task.ConfigureAwait(true);
            _disposeTaskCompletionSource = null;

            _scope.Dispose();
            _scope = null;

            UITest.Dispose();
            UITest = null;
        }
    }
}
