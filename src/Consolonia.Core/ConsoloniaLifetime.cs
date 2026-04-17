using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Window = Avalonia.Controls.Window;

// ReSharper disable CheckNamespace
// ReSharper disable NotNullOrRequiredMemberIsNotInitialized
// ReSharper disable ConstantConditionalAccessQualifier
namespace Consolonia
{
    [Consolonia]
    public class ConsoloniaLifetime : IClassicDesktopStyleApplicationLifetime,
        IDisposable
    {
        private CancellationTokenSource _cts = new();
        private bool _disposedValue;
        private int _exitCode;
        private bool _isShuttingDown;

        public static IConsole Console => AvaloniaLocator.Current.GetRequiredService<IConsole>();

        /// <summary>
        ///     Gets the arguments passed to the AppBuilder Start method.
        /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
        public string[] Args { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        public event EventHandler<ControlledApplicationLifetimeStartupEventArgs> Startup;

        public event EventHandler<ControlledApplicationLifetimeExitEventArgs> Exit;

        public void Shutdown(int exitCode = 0)
        {
            DoShutdown(new ShutdownRequestedEventArgs(), true, true, exitCode);
        }

        public ShutdownMode ShutdownMode { get; set; } = ShutdownMode.OnExplicitShutdown;

        public Window MainWindow { get; set; }

        public IReadOnlyList<Window> Windows => new List<Window> { MainWindow };

        public event EventHandler<ShutdownRequestedEventArgs> ShutdownRequested;

        public bool TryShutdown(int exitCode = 0)
        {
            return DoShutdown(new ShutdownRequestedEventArgs(), true, false, exitCode);
        }

        internal void SetupCore(string[] args)
        {
            Startup?.Invoke(this, new ControlledApplicationLifetimeStartupEventArgs(args));

            var lifetimeEvents = AvaloniaLocator.Current.GetService<IPlatformLifetimeEventsImpl>();

            if (lifetimeEvents != null)
                lifetimeEvents.ShutdownRequested += OnShutdownRequested;

            MainWindow.Closed += (_, _) => TryShutdown();
        }

        public int Start(string[] args)
        {
            return StartCore(args);
        }

        /// <summary>
        ///     Since the lifetime must be set up/prepared with 'args' before executing Start(), an overload with no parameters
        ///     seems more suitable for integrating with some lifetime manager providers, such as MS HostApplicationBuilder.
        /// </summary>
        /// <returns>exit code</returns>
        public int Start()
        {
            return StartCore(Args ?? Array.Empty<string>());
        }

        internal int StartCore(string[] args)
        {
            SetupCore(args);

            MainWindow.Show();

            try
            {
                Dispatcher.UIThread.MainLoop(_cts.Token);
                Environment.ExitCode = _exitCode;
                return _exitCode;
            }
            finally
            {
                Dispose();
            }
        } // ReSharper disable UnusedParameter.Local
        // ReSharper disable UnusedMember.Local
#pragma warning disable IDE0060 // Remove unused parameter
        private bool DoShutdown(
            ShutdownRequestedEventArgs e,
            bool isProgrammatic,
            bool force = false,
            int exitCode = 0)
        {
            if (!force)
            {
                ShutdownRequested?.Invoke(this, e);

                if (e.Cancel)
                    return false;

                if (_isShuttingDown)
                    throw new InvalidOperationException("Application is already shutting down.");
            }

            _exitCode = exitCode;
            _isShuttingDown = true;

            try
            {
                var args = new ControlledApplicationLifetimeExitEventArgs(exitCode);
                Exit?.Invoke(this, args);
                _exitCode = args.ApplicationExitCode;
            }
            finally
            {
                _isShuttingDown = false;

                _cts?.Cancel();
                _cts = null;
                Dispatcher.UIThread.InvokeShutdown();
            }

            return true;
        }
#pragma warning restore IDE0060 // Remove unused parameter

        // ReSharper disable once UnusedMember.Local
        private void OnShutdownRequested(object sender, ShutdownRequestedEventArgs e)
        {
            DoShutdown(e, false);
        }

        /// <summary>
        ///     The returned task indicates when the console is successfully paused
        /// </summary>
        public Task DisconnectFromConsoleAsync(CancellationToken cancellationToken)
        {
            var taskToWaitFor = new TaskCompletionSource();
            cancellationToken.Register(taskToWaitFor.SetResult);

            var consoleWindow = (ConsoleWindowImpl)MainWindow.PlatformImpl;
            IConsole console = consoleWindow!.Console;

            Task pauseTask = taskToWaitFor.Task;

            console.PauseIO(pauseTask);

            pauseTask.ContinueWith(_ =>
            {
                consoleWindow.ClearScreen();
                Dispatcher.UIThread.Post(() => { MainWindow.InvalidateVisual(); });
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            return Dispatcher.UIThread.InvokeAsync(() => { }).GetTask();
        }

#pragma warning disable CA1822
        // ReSharper disable once MemberCanBeMadeStatic.Global It can be static only because we rely on static locator currently. I bet it will change in the future
        public bool IsRgbColorMode()
#pragma warning restore CA1822
        {
            IConsoleColorMode consoleColorMode = AvaloniaLocator.Current.GetService<IConsoleColorMode>()
                                                 ?? throw new ConsoloniaException(
                                                     "Console color mode has not been initialized");

            return consoleColorMode is RgbConsoleColorMode;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _cts?.Dispose();
                    _cts = null;
                    if (MainWindow != null && MainWindow.PlatformImpl != null)
                    {
                        var consoleTopLevelImpl = (ConsoleWindowImpl)MainWindow.PlatformImpl;
                        ArgumentNullException.ThrowIfNull(consoleTopLevelImpl, nameof(consoleTopLevelImpl));
                        consoleTopLevelImpl.Dispose();
                    }
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}