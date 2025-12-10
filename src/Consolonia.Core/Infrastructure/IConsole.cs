using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedMemberInSuper.Global

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     IConsole is made up of IConsoleInput and IConsoleOutput
    /// </summary>
    public interface IConsole : IConsoleOutput
    {
        event Action Resized;

        event Action<Key, char, RawInputModifiers, bool, ulong, bool> KeyEvent;
        event Action<string, ulong, CanBeHandledEventArgs> TextInputEvent;
        event Action<RawPointerEventType, Point, Vector?, RawInputModifiers> MouseEvent;

        event Action<bool> FocusEvent;

        void PauseIO(Task task);
    }
}