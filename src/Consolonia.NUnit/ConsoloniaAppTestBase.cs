using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
        private sealed class WorkItem
        {
            public WorkItem(Action action)
            {
                Action = action;
                Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Action Action { get; }
            public TaskCompletionSource Completion { get; }
        }

        // Avalonia dispatchers and cached render resources keep their creating thread affinity.
        private static readonly BlockingCollection<WorkItem> WorkItems = new();

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "The worker boundary must report every action failure without terminating the shared thread.")]
        static ConsoloniaAppTestThread()
        {
            var thread = new Thread(() =>
            {
                foreach (WorkItem workItem in WorkItems.GetConsumingEnumerable())
                {
                    try
                    {
                        workItem.Action();
                        workItem.Completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        workItem.Completion.TrySetException(exception);
                    }
                }
            })
            {
                IsBackground = true,
                Name = nameof(ConsoloniaAppTestThread)
            };
            thread.Start();
        }

        public static Task Queue(Action action)
        {
            var workItem = new WorkItem(action);
            WorkItems.Add(workItem);
            return workItem.Completion.Task;
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
            var uiTest = new UnitTestConsole(_size);
            UITest = uiTest;
            var setupTaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposeTaskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTaskCompletionSource = disposeTaskCompletionSource;

            Task workerTask = ConsoloniaAppTestThread.Queue(() =>
            {
                try
                {
                    ResetDispatcher("ResetBeforeUnitTests");
                    _ = Dispatcher.CurrentDispatcher;
                    _scope = AvaloniaLocator.EnterScope();
                    _lifetime = ApplicationStartup.CreateLifetime(CreateAppBuilder(), Args);
                    uiTest.SetupLifetime(_lifetime);
                    setupTaskSource.TrySetResult();
                    _lifetime.Start(Args);
                }
                finally
                {
                    CleanupTestApplication();
                }
            });

            _ = workerTask.ContinueWith(task =>
            {
                if (task.Exception is { } exception)
                {
                    var failures = exception.Flatten().InnerExceptions;
                    setupTaskSource.TrySetException(failures);
                    disposeTaskCompletionSource.TrySetException(failures);
                }
                else
                {
                    disposeTaskCompletionSource.TrySetResult();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            await setupTaskSource.Task.ConfigureAwait(true);

            // Waiting Main Window To appear
            CancellationToken cancellationToken = new CancellationTokenSource(60000 /*todo: magic number*/).Token;
            Task mainWindowTask = Task.Run(async () =>
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
            }, cancellationToken);
            await AwaitWhileWorkerIsRunning(mainWindowTask, workerTask).ConfigureAwait(true);

            // Waiting all jobs to finish
            await AwaitWhileWorkerIsRunning(uiTest.WaitRendered(), workerTask).ConfigureAwait(true);
        }

        [TearDown]
        public async Task GlobalTearDown()
        {
            ConsoloniaLifetime lifetime = _lifetime;
            if (lifetime is not null)
                await Dispatcher.UIThread.InvokeAsync(() => { lifetime.Shutdown(); }).GetTask().ConfigureAwait(true);

            await _disposeTaskCompletionSource.Task.ConfigureAwait(true);
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Cleanup must attempt every resource after any individual cleanup failure.")]
        private void CleanupTestApplication()
        {
            Exception cleanupException = null;

            void Cleanup(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    cleanupException ??= exception;
                }
            }

            // Resetting static of AppBuilderBase
            Cleanup(() => typeof(AppBuilder).GetField("s_setupWasAlreadyCalled",
                    BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, false));
            Cleanup(() => _lifetime?.Dispose());
            _lifetime = null;
            Cleanup(() => UITest?.Dispose());
            UITest = null;
            Cleanup(() => ResetDispatcher("ResetForUnitTests"));
            Cleanup(() => _scope?.Dispose());
            _scope = null;
            Cleanup(() => ResetDispatcher("ResetBeforeUnitTests"));

            if (cleanupException is not null)
                ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }

        private static async Task AwaitWhileWorkerIsRunning(Task operation, Task workerTask)
        {
            Task completedTask = await Task.WhenAny(operation, workerTask).ConfigureAwait(true);
            if (completedTask == workerTask)
            {
                await workerTask.ConfigureAwait(true);
                throw new InvalidOperationException("Application lifetime ended during test setup.");
            }

            await operation.ConfigureAwait(true);
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
