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
    internal static class ConsoloniaAppTestThread
    {
        // Avalonia dispatchers and cached render resources keep their creating thread affinity.
        private static readonly BlockingCollection<Action> WorkItems = new();

        static ConsoloniaAppTestThread()
        {
            var thread = new Thread(() =>
            {
                foreach (Action action in WorkItems.GetConsumingEnumerable())
                    action();
            })
            {
                IsBackground = true,
                Name = nameof(ConsoloniaAppTestThread)
            };
            thread.Start();
        }

        public static void Queue(Action action)
        {
            WorkItems.Add(action);
        }
    }

    [NonParallelizable /*todo: switch to semaphore like https://stackoverflow.com/a/6427425/2362847 to allow other tests to execute in parallel*/]
#pragma warning disable CA1001 // we are relying on TearDown by NUnit
    public abstract class ConsoloniaAppTestBase<TApp>
        where TApp : Application, new()
#pragma warning restore CA1001
    {
        private readonly PixelBufferSize _size;
        private IDisposable _scope;

        protected ConsoloniaAppTestBase(PixelBufferSize size)
        {
            _size = size;
        }

#pragma warning disable CA1819 // todo: provide a solution
        protected string[] Args { get; init; }
#pragma warning restore CA1819

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
        public async Task GlobalSetup()
        {
            UITest = new UnitTestConsole(_size);
            var setupTaskSource = new TaskCompletionSource();

            ConsoloniaAppTestThread.Queue(() =>
            {
                _disposeTaskCompletionSource = new TaskCompletionSource();
                ResetDispatcher("ResetBeforeUnitTests");
                _ = Dispatcher.CurrentDispatcher;
                _scope = AvaloniaLocator.EnterScope();
                _lifetime = ApplicationStartup.CreateLifetime(CreateAppBuilder(), Args);
                UITest.SetupLifetime(_lifetime);
                setupTaskSource.SetResult();
                _lifetime.Start(Args);

                // Resetting static of AppBuilderBase
                typeof(AppBuilder).GetField("s_setupWasAlreadyCalled",
                        BindingFlags.Static | BindingFlags.NonPublic)!
                    .SetValue(null, false);
                _lifetime.Dispose();
                _lifetime = null;
                UITest.Dispose();
                UITest = null;
                ResetDispatcher("ResetForUnitTests");
                _scope.Dispose();
                _scope = null;
                ResetDispatcher("ResetBeforeUnitTests");
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
        public async Task GlobalTearDown()
        {
            ConsoloniaLifetime lifetime = _lifetime;
            await Dispatcher.UIThread.InvokeAsync(() => { lifetime.Shutdown(); }).GetTask().ConfigureAwait(true);

            await _disposeTaskCompletionSource.Task.ConfigureAwait(true);
        }

        private static void ResetDispatcher(string methodName)
        {
            // Avalonia uses these internal hooks for its own per-test application isolation.
            typeof(Dispatcher).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, null);
        }

        // ReSharper disable StaticMemberInGenericType
        private static TaskCompletionSource _disposeTaskCompletionSource; // todo: tests now rely on static
        private static ConsoloniaLifetime _lifetime;

        protected static UnitTestConsole UITest { get; private set; }
        // ReSharper restore StaticMemberInGenericType
    }
}
