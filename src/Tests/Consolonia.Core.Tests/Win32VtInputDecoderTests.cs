using System.Collections.Generic;
using System.Linq;
using Consolonia.PlatformSupport;
using NUnit.Framework;
using static Vanara.PInvoke.Kernel32;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class Win32VtInputDecoderTests
    {
        /// <summary>Terminal input as the console hands it over in virtual terminal input mode.</summary>
        private static INPUT_RECORD[] Typed(string text)
        {
            return text.Select(c => new INPUT_RECORD
            {
                EventType = EVENT_TYPE.KEY_EVENT,
                Event = { KeyEvent = new KEY_EVENT_RECORD { bKeyDown = true, wRepeatCount = 1, uChar = c } }
            }).ToArray();
        }

        private static List<KEY_EVENT_RECORD> Keys(IEnumerable<INPUT_RECORD> records)
        {
            return records.Where(r => r.EventType == EVENT_TYPE.KEY_EVENT).Select(r => r.Event.KeyEvent).ToList();
        }

        private static List<MOUSE_EVENT_RECORD> Mice(IEnumerable<INPUT_RECORD> records)
        {
            return records.Where(r => r.EventType == EVENT_TYPE.MOUSE_EVENT).Select(r => r.Event.MouseEvent)
                .ToList();
        }

        [Test]
        public void Win32InputModeKeysBecomeTheRecordsTheyDescribe()
        {
            // Up pressed and released, then Shift+A pressed
            INPUT_RECORD[] decoded = new Win32VtInputDecoder()
                .Decode(Typed("\u001b[38;72;0;1;0;1_\u001b[38;72;0;0;0;1_\u001b[65;30;65;1;16;1_"));

            List<KEY_EVENT_RECORD> keys = Keys(decoded);
            Assert.That(keys, Has.Count.EqualTo(3));
            Assert.That((keys[0].wVirtualKeyCode, keys[0].wVirtualScanCode, keys[0].bKeyDown), Is.EqualTo(((ushort)38, (ushort)72, true)));
            Assert.That(keys[1].bKeyDown, Is.False);
            Assert.That((keys[2].uChar, keys[2].dwControlKeyState), Is.EqualTo(('A', CONTROL_KEY_STATE.SHIFT_PRESSED)));
        }

        [Test]
        public void SgrPixelsReportsBecomeMouseRecordsInZeroBasedPixels()
        {
            INPUT_RECORD[] decoded = new Win32VtInputDecoder()
                .Decode(Typed("\u001b[<0;123;456M\u001b[<32;130;460M\u001b[<0;131;461m"));

            List<MOUSE_EVENT_RECORD> mice = Mice(decoded);
            Assert.That(mice, Has.Count.EqualTo(3));

            Assert.That((mice[0].dwMousePosition.X, mice[0].dwMousePosition.Y), Is.EqualTo(((short)122, (short)455)));
            Assert.That(mice[0].dwEventFlags, Is.EqualTo(MOUSE_EVENT_FLAG.NONE));
            Assert.That(mice[0].dwButtonState, Is.EqualTo(MOUSE_BUTTON_STATE.FROM_LEFT_1ST_BUTTON_PRESSED));

            Assert.That(mice[1].dwEventFlags, Is.EqualTo(MOUSE_EVENT_FLAG.MOUSE_MOVED));
            Assert.That(mice[1].dwButtonState, Is.EqualTo(MOUSE_BUTTON_STATE.FROM_LEFT_1ST_BUTTON_PRESSED),
                "a drag still has the button down");

            Assert.That(mice[2].dwButtonState, Is.EqualTo(MOUSE_BUTTON_STATE.NONE));
        }

        [Test]
        public void RightButtonAndModifiersAreKept()
        {
            // 2 = right button, +4 Shift, +16 Ctrl
            MOUSE_EVENT_RECORD mouse = Mice(new Win32VtInputDecoder().Decode(Typed("\u001b[<22;5;6M"))).Single();

            Assert.That(mouse.dwButtonState, Is.EqualTo(MOUSE_BUTTON_STATE.RIGHTMOST_BUTTON_PRESSED));
            Assert.That(mouse.dwControlKeyState,
                Is.EqualTo(CONTROL_KEY_STATE.SHIFT_PRESSED | CONTROL_KEY_STATE.LEFT_CTRL_PRESSED));
        }

        [TestCase("\u001b[<64;5;6M", true, TestName = "WheelUpIsAPositiveDelta")]
        [TestCase("\u001b[<65;5;6M", false, TestName = "WheelDownIsANegativeDelta")]
        public void WheelCarriesItsDirectionInTheHighWord(string report, bool up)
        {
            MOUSE_EVENT_RECORD mouse = Mice(new Win32VtInputDecoder().Decode(Typed(report))).Single();

            Assert.That(mouse.dwEventFlags, Is.EqualTo(MOUSE_EVENT_FLAG.MOUSE_WHEELED));
            Assert.That(unchecked((int)mouse.dwButtonState) > 0, Is.EqualTo(up));
        }

        [Test]
        public void SequenceSplitAcrossReadsIsHeldUntilItEnds()
        {
            var decoder = new Win32VtInputDecoder();

            Assert.That(decoder.Decode(Typed("\u001b[<0;12")), Is.Empty);
            Assert.That(Mice(decoder.Decode(Typed("3;456M"))).Single().dwMousePosition.X, Is.EqualTo(122));
        }

        [Test]
        public void QueryRepliesAreNotTakenForKeys()
        {
            INPUT_RECORD[] decoded = new Win32VtInputDecoder()
                .Decode(Typed("\u001b[?1016;1$y\u001b[?62;4;22c\u001b[14;24;12t"));

            Assert.That(Keys(decoded), Is.Empty);
        }

        [Test]
        public void PlainVtKeysAreMappedForTerminalsWithoutWin32InputMode()
        {
            List<KEY_EVENT_RECORD> keys = Keys(new Win32VtInputDecoder().Decode(Typed("z\u001b[1;5C\u001b[3~\r")))
                .Where(k => k.bKeyDown).ToList();

            Assert.That(keys.Select(k => k.wVirtualKeyCode), Is.EqualTo(new ushort[] { 'Z', 0x27, 0x2E, 0x0D }));
            Assert.That(keys[0].uChar, Is.EqualTo('z'));
            Assert.That(keys[1].dwControlKeyState, Is.EqualTo(CONTROL_KEY_STATE.LEFT_CTRL_PRESSED));
        }

        [Test]
        public void LoneEscapeAtTheEndOfAReadIsTheEscKey()
        {
            KEY_EVENT_RECORD key = Keys(new Win32VtInputDecoder().Decode(Typed("\u001b"))).First();

            Assert.That((key.wVirtualKeyCode, key.bKeyDown), Is.EqualTo(((ushort)0x1B, true)));
        }

        [Test]
        public void RecordsThatAreNotCharactersPassThrough()
        {
            var resize = new INPUT_RECORD { EventType = EVENT_TYPE.WINDOW_BUFFER_SIZE_EVENT };
            var win32Key = new INPUT_RECORD
            {
                EventType = EVENT_TYPE.KEY_EVENT,
                Event = { KeyEvent = new KEY_EVENT_RECORD { bKeyDown = true, wVirtualKeyCode = 0x70 } }
            };

            INPUT_RECORD[] decoded = new Win32VtInputDecoder().Decode([resize, win32Key]);

            Assert.That(decoded.Select(r => r.EventType),
                Is.EqualTo(new[] { EVENT_TYPE.WINDOW_BUFFER_SIZE_EVENT, EVENT_TYPE.KEY_EVENT }));
            Assert.That(decoded[1].Event.KeyEvent.wVirtualKeyCode, Is.EqualTo(0x70));
        }
    }
}
