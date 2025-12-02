using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor;
using Consolonia.Core.Infrastructure;

// ReSharper disable UnusedMember.Global //todo: how to disable it for public methods?
// ReSharper disable CheckNamespace
// ReSharper disable MemberCanBePrivate.Global

#nullable enable
namespace Consolonia
{
    public static class ApplicationStartup
    {
        public static void StartConsolonia<TApp>(bool supportBrightBackground, params string[] args) where TApp : Application, new()
        {
            StartConsolonia<TApp>(new DefaultNetConsole(), new EgaConsoleColorMode(supportBrightBackground), args);
        }

        public static void StartConsolonia<TApp>(IConsole console, IConsoleColorMode consoleColorMode,
            params string[] args) where TApp : Application, new()
        {
            ConsoloniaLifetime lifetime =
                BuildLifetime<TApp>(console, consoleColorMode, new ConsoloniaPlatformSettings(), args);

            lifetime.Start(args);
        }

        public static AppBuilder UseStandardConsole(this AppBuilder builder, bool supportBrightBackground)
        {
            return builder.UseConsole(new DefaultNetConsole()).UseConsoleColorMode(new EgaConsoleColorMode(supportBrightBackground));
        }

        public static AppBuilder UseConsole(this AppBuilder builder, IConsole console)
        {
            return builder.With(console)
                .With<IConsoleOutput>(console)
                .With<IConsoleCapabilities>(console);
        }

        /// <summary>
        ///     <seealso cref="ConsoloniaPlatformSettings" />
        /// </summary>
        public static AppBuilder ThrowOnErrors(this AppBuilder builder)
        {
            return builder.With<IPlatformSettings>(new ConsoloniaPlatformSettings
            {
                UnsafeRendering = false,
                UnsafeInput = false
            });
        }

        public static AppBuilder UseConsoleColorMode(this AppBuilder builder, IConsoleColorMode consoleColorMode)
        {
            return builder.With(consoleColorMode);
        }

        public static AppBuilder UseConsolonia(this AppBuilder builder)
        {
            Action? initialize = builder.RenderingSubsystemInitializer;

            return builder
                .UseStandardRuntimePlatformSubsystem()
                .UseWindowingSubsystem(() => new ConsoloniaPlatform().Initialize(), nameof(ConsoloniaPlatform))
                .UseRenderingSubsystem(() =>
                {
                    if (initialize != null)
                        initialize();

                    var fallback = AvaloniaLocator.Current.GetService<IPlatformRenderInterface>();
                    var consoloniaRenderInterface = new ConsoloniaRenderInterface(fallback);

                    AvaloniaLocator.CurrentMutable
                        .Bind<IPlatformRenderInterface>().ToConstant(consoloniaRenderInterface);
                }, nameof(ConsoloniaRenderInterface));
        }

        public static ConsoloniaLifetime BuildLifetime<TApp>(IConsole console,
            IConsoleColorMode consoleColorMode, ConsoloniaPlatformSettings settings, string[] args)
            where TApp : Application, new()
        {
            AppBuilder consoloniaAppBuilder = AppBuilder.Configure<TApp>()
                .UseConsole(console)
                .UseConsolonia()
                .UseConsoleColorMode(consoleColorMode)
                .With<IPlatformSettings>(settings)
                .LogToException();
            return CreateLifetime(consoloniaAppBuilder, args);
        }

        /// <summary>
        ///     Shuts down the application with the specified exit code.
        /// </summary>
        /// <param name="lifetime">The application lifetime.</param>
        /// <param name="exitCode">The exit code to use.</param>
        /// <exception cref="InvalidOperationException">Thrown when the lifetime does not support controlled shutdown.</exception>
        public static void Shutdown(this IApplicationLifetime lifetime, int exitCode = 0)
        {
            if (lifetime is IControlledApplicationLifetime controlledLifetime)
                controlledLifetime.Shutdown(exitCode);
            else
                throw new InvalidOperationException("The lifetime does not support controlled shutdown.");
        }

        /// <summary>
        ///     Shuts down the application with the specified exit code.
        /// </summary>
        /// <param name="lifetime">The application lifetime.</param>
        /// <param name="exitCode">The exit code to use.</param>
        /// <exception cref="InvalidOperationException">Thrown when the lifetime does not support controlled shutdown.</exception>
        public static void TryShutdown(this IApplicationLifetime lifetime, int exitCode = 0)
        {
            if (lifetime is IControlledApplicationLifetime controlledLifetime)
                controlledLifetime.TryShutdown(exitCode);
            else
                throw new InvalidOperationException("The lifetime does not support controlled shutdown.");
        }

        public static ConsoloniaLifetime CreateLifetime(AppBuilder builder, string[] args)
        {
            var lifetime = new ConsoloniaLifetime
            {
                Args = args
            };

            builder.SetupWithLifetime(lifetime);

            // Application has been instantiated here.
            // We need to initialize it

            // override AccessText to use ConsoloniaAccessText as default ContentPresenter for unknown data types (aka string)
            Application.Current!.DataTemplates.Add(new FuncDataTemplate<object>(
                (_, _) =>
                {
                    var result = new ConsoloniaAccessText();
                    // ReSharper disable AccessToStaticMemberViaDerivedType
                    result.Bind(TextBlock.TextProperty,
                        result.GetBindingObservable(Control.DataContextProperty, x => x?.ToString()));
                    return result;
                },
                true)
            );

            return lifetime;
        }

        public static int StartWithConsoleLifetime(
            this AppBuilder builder, string[] args)
        {
            ConsoloniaLifetime lifetime = CreateLifetime(builder, args);
            return lifetime.Start(args);
        }
    }
}