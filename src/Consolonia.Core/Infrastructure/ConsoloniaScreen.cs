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
            var screen = new PlatformScreen(null);
            SetScreenProperty(screen, nameof(Screen.DisplayName), "Console");
            SetScreenProperty(screen, nameof(Screen.Scaling), 1d);
            SetScreenProperty(screen, nameof(Screen.Bounds), rect);
            SetScreenProperty(screen, nameof(Screen.WorkingArea), rect);
            SetScreenProperty(screen, nameof(Screen.IsPrimary), true);
            return screen;
        }

        private static void SetScreenProperty<T>(Screen screen, string propertyName, T value)
        {
            var property = typeof(Screen).GetProperty(propertyName) ??
                           throw new InvalidOperationException($"Screen property '{propertyName}' not found.");
            property.SetValue(screen, value);
        }
    }
}