using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Consolonia.PlatformSupport;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class DispatcherExceptionTests
    {
        private const string LegacyMessage =
            "Avalonia's routed-event dispatcher exception behavior has changed. " +
            "The TargetInvocationException catch in CursesConsole is probably legacy code and needs to be reconsidered.";

        [Test]
        [RequiresThread]
        public void PostedRoutedEventStillThrowsMarkedTargetInvocationException()
        {
            Dispatcher dispatcher = CreateDispatcher();
            MenuItem menuItem = CreateThrowingMenuItem();
            Exception filteredException = null;

            dispatcher.UnhandledExceptionFilter += (_, args) =>
            {
                filteredException = args.Exception;
                args.RequestCatch = false;
            };

            dispatcher.Post(() => menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
            Exception exception = Assert.Catch(() => dispatcher.RunJobs(), LegacyMessage);

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.TypeOf<TargetInvocationException>(), LegacyMessage);
                Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>(), LegacyMessage);
                Assert.That(filteredException, Is.SameAs(exception), LegacyMessage);
                Assert.That(CursesConsole.IsDispatcherException(exception), Is.True,
                    "The thrown exception does not carry Dispatcher.ExceptionDataKey. " + LegacyMessage);
            });
        }

        private static Dispatcher CreateDispatcher()
        {
            return (Dispatcher)typeof(Dispatcher)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                    [typeof(IDispatcherImpl)], null)
                .Invoke([new ManagedDispatcherImpl(null)]);
        }

        private static MenuItem CreateThrowingMenuItem()
        {
            var menuItem = new MenuItem();
            menuItem.AddHandler((RoutedEvent)MenuItem.ClickEvent,
                new EventHandler<RoutedEventArgs>((_, _) => ThrowSync()));
            return menuItem;
        }

        private static void ThrowSync()
        {
            throw new InvalidOperationException("Exception from a synchronous routed-event handler.");
        }
    }
}