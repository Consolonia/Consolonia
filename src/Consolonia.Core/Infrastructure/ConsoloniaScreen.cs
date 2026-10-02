using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;

namespace Consolonia.Core.Infrastructure
{
    public class ConsoloniaScreen : IScreenImpl
    {
        private readonly Screen[] _screens;

        public ConsoloniaScreen(PixelRect rect)
        {
            _screens = [CreateScreen(rect)];
        }

        public int ScreenCount => 1;

        public IReadOnlyList<Screen> AllScreens => _screens.AsReadOnly();

        public Action Changed { get; set; }

        public Task<bool> RequestScreenDetails()
        {
            return Task.FromResult(true);
        }

        public Screen ScreenFromPoint(PixelPoint point)
        {
            return _screens[0];
        }

        public Screen ScreenFromRect(PixelRect rect)
        {
            return _screens[0];
        }

        public Screen ScreenFromTopLevel(ITopLevelImpl topLevel)
        {
            return _screens[0];
        }

        public Screen ScreenFromWindow(IWindowBaseImpl window)
        {
            return _screens[0];
        }

        private static Screen CreateScreen(PixelRect rect)
        {
            return new ConsolePlatformScreen(rect);
        }

        private sealed class ConsolePlatformScreen : PlatformScreen
        {
            public ConsolePlatformScreen(PixelRect rect) : base(ConsolePlatformHandle.Instance)
            {
                DisplayName = "Console";
                Scaling = 1;
                Bounds = rect;
                WorkingArea = rect;
                IsPrimary = true;
            }
        }

        private sealed class ConsolePlatformHandle : IPlatformHandle
        {
            public static readonly ConsolePlatformHandle Instance = new();

            public nint Handle => 0;

            public string HandleDescriptor => "Consolonia";
        }
    }
}