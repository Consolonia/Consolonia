using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Input;
using Consolonia.Core.Helpers;
using Consolonia.Core.Helpers.InputProcessing;
using Consolonia.Core.InternalHelpers;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     IConsole implementation which purely uses Console API
    /// </summary>
    /// <remarks>
    ///     This implements uses standard Console.ReadKey to get input and
    ///     calls the base IConsoleOutput for output (or default of Console Output)
    /// </remarks>
    public class DefaultNetConsole : ConsoleBase
    {
        private static readonly Dictionary<ConsoleKey, Key> KeyMapping = new()
        {
            { ConsoleKey.Applications, Key.Apps },
            { ConsoleKey.Attention, Key.Attn },
            { ConsoleKey.LaunchApp1, Key.LaunchApplication1 },
            { ConsoleKey.LaunchApp2, Key.LaunchApplication2 },
            { ConsoleKey.MediaNext, Key.MediaNextTrack },
            { ConsoleKey.MediaPrevious, Key.MediaPreviousTrack },
            { ConsoleKey.MediaStop, Key.MediaStop },
            { ConsoleKey.MediaPlay, Key.MediaPlayPause },
            { ConsoleKey.LaunchMediaSelect, Key.SelectMedia },
            { ConsoleKey.EraseEndOfFile, Key.EraseEof },
            { ConsoleKey.LeftWindows, Key.LWin },
            { ConsoleKey.RightWindows, Key.RWin },
            { ConsoleKey.Spacebar, Key.Space },
            { ConsoleKey.RightArrow, Key.Right },
            { ConsoleKey.LeftArrow, Key.Left },
            { ConsoleKey.UpArrow, Key.Up },
            { ConsoleKey.DownArrow, Key.Down },
            { ConsoleKey.Backspace, Key.Back },
            { (ConsoleKey)18, Key.LeftAlt },
            { (ConsoleKey)16, Key.LeftShift },
            { (ConsoleKey)17, Key.LeftCtrl }
        };

        private static readonly FlagTranslator<ConsoleModifiers, RawInputModifiers> ModifiersFlagsTranslator = new([
            (ConsoleModifiers.Control, RawInputModifiers.Control),
            (ConsoleModifiers.Shift, RawInputModifiers.Shift), (ConsoleModifiers.Alt, RawInputModifiers.Alt)
        ]);

        public override bool SupportsAltSolo => false;

        public override bool SupportsMouse => false;

        public override bool SupportsMouseMove => false;

        // NOTE: we default to true because we determining GUI mouse 
        // requires platform specific code.
        public override bool ShowMouseCursor => true; 

        public DefaultNetConsole()
            : base(new DefaultNetConsoleOutput())
        {
            _inputBuffer = new FastBuffer<ConsoleKeyInfo>(ReadDataFunction);
            _inputProcessor = new InputProcessor<ConsoleKeyInfo>([
                new TextInputMatcher<ConsoleKeyInfo>(tuple =>
                {
                    bool processSeparateKeys = true;
                    if (tuple.Item1.Length >= 10) // todo: low: magic number here
                    {
                        CanBeHandledEventArgs canBeHandledEventArgs = new();
                        RaiseTextInput(tuple.Item1, (ulong)Environment.TickCount64, canBeHandledEventArgs);
                        processSeparateKeys = !canBeHandledEventArgs.Handled;
                    }

                    if (processSeparateKeys)
                        foreach (ConsoleKeyInfo consoleKeyInfo in tuple.Item2)
                            RaiseKeyInputInternal(consoleKeyInfo, false);
                }, ck => new Rune(ck.KeyChar)),
                new GenericMatcher<ConsoleKeyInfo>(consoleKeyInfo => RaiseKeyInputInternal(consoleKeyInfo))
            ]);
            // ReSharper disable VirtualMemberCallInConstructor
            PrepareConsole();

            StartSizeCheckTimerAsync();
            StartInputReading();
            _inputBuffer.StartReading();
        }

        private readonly FastBuffer<ConsoleKeyInfo> _inputBuffer;
        private readonly InputProcessor<ConsoleKeyInfo> _inputProcessor;

        private ConsoleKeyInfo[] ReadDataFunction()
        {
            while (true)
                try
                {
                    PauseTask?.Wait();
                    return [Console.ReadKey(true)];
                }
                catch (InvalidOperationException)
                {
                }
        }


        private void StartInputReading()
        {
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                await Helper.WaitDispatcherInitialized();

                while (!Disposed)
                {
                    PauseTask?.Wait();

                    ConsoleKeyInfo[] consoleKeyInfos = _inputBuffer.Dequeue();
                    if (!consoleKeyInfos.Any())
                        return;

                    await DispatchInputAsync(() => { _inputProcessor.ProcessChunk(consoleKeyInfos); });
                }
            });
        }

        private void RaiseKeyInputInternal(ConsoleKeyInfo consoleKeyInfo, bool tryAsTextInput = true)
        {
            Key key = ConvertToKey(consoleKeyInfo.Key);

            RawInputModifiers rawInputModifiers =
                ModifiersFlagsTranslator.Translate(consoleKeyInfo.Modifiers);

            RaiseKeyPress(key, consoleKeyInfo.KeyChar, rawInputModifiers, true,
                (ulong)Environment.TickCount64);
            Thread.Yield(); //todo: low is yielding necessary here?
            RaiseKeyPress(key, consoleKeyInfo.KeyChar, rawInputModifiers, false,
                (ulong)Environment.TickCount64, tryAsTextInput);
            Thread.Yield();
        }

        public static Key ConvertToKey(ConsoleKey consoleKey)
        {
            if (KeyMapping.TryGetValue(consoleKey, out Key key)) return key;

            if (!Enum.IsDefined(consoleKey) || !Enum.TryParse(consoleKey.ToString(), out key))
                return ConsoloniaPlatform.RaiseNotSupported<Key>(NotSupportedRequestCode.InputNotSupported, consoleKey);

            return key;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inputBuffer.Dispose();
            base.Dispose(disposing);
        }
    }
}