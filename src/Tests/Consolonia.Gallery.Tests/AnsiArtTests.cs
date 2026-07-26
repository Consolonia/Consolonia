using System.Threading.Tasks;
using Avalonia.Input;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class AnsiArtTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            //Claude generated 

            // Image 1: NI-BLND0123.ANS - row with "Nachos!" text, validates alignment of speech bubble
            await UITest.AssertHasText("     ■ ▄ ▀█ ■ ▄ ▓█▓█▓█▓█▓█▓█▓█▓▀▄▄▀/▓Nachos!▓▒░");

            // Image 2: 12.ANS
            await ClickNext();
            await UITest.AssertHasText(
                " ▄▀█████████████▄▀██▄▄  ███▀▄▓▒▒▒░            ░     ▐ ");

            // Image 3: ak-evoke.ans
            await ClickNext();
            await UITest.AssertHasText(
                " $$$$$$$$b┐.`'└*/$$$s┐,. ┌$$j$P'.d$' I$$7 `*' \".,┌⌐s\\ ");

            // Image 4: BLUES.ANS - row with "C A F E" text, validates alignment of cafe sign
            await ClickNext();
            await UITest.AssertHasText(
                "   C A F E  ██               ▐▌          ▐▌           ");
            // Verify PCBoard/BBS directives are not rendered
            await UITest.AssertHasNoText("@NOPAUSE@");
            await UITest.AssertHasNoText("@PAUSE@");

            // Image 5: CHECS-GODZILLA 6.ANS - row with "HAVE CASTLED" text
            await ClickNext();
            await UITest.AssertHasText("  ▌  ░░▒▒▒░ ░ ▐▌ ░░░░ HAVE CASTLED   ▄ ▄▄█▀▀     ,ƒ▄▄ ");

            // Image 6: E_JOHN.ANS
            await ClickNext();
            await UITest.AssertHasText("│ ███████████▌█▌███████████████████ ████▌▐    E L T O  ");

            // Image 7: MERMAID3.ANS
            await ClickNext();
            await UITest.AssertHasText("           o▐▐(_)██▄▄ ▄ ▄████     it is wetter - Unde");

            // Image 8: N-CRPNTR.ANS
            await ClickNext();
            await UITest.AssertHasText("        ░░ ▀▀▓▓▄▄▄▄▄ █▀█▄░▄▀▀██▓████░▓██░▓████▓▐▐███░ ");

            // Image 9: N-HLKHGN.ANS
            await ClickNext();
            await UITest.AssertHasText("│ ██▓▓▓▓░ ▌▐█▌▐ ░▄▌▀ ▌ ▀ ▐ ▌▐ ▐ █ █▀ ▐██░░▀▄█░░░▄█████ ");

            // Image 10: SFMSG11.ANS
            await ClickNext();
            await UITest.AssertHasText("  ███│█│██│             ╙ ─┌─ │            ███  │ │   ");

            // Image 11: US-MOTHR.ANS - eye row with 'o' characters at correct positions validates alignment
            await ClickNext();
            await UITest.AssertHasText(
                " ▒ ▄▀     ░▒▓▀▄▄  ░▓░ o  ▄▓▄  o ▒▓░  ▄░▓▒░  ░   ▀▄ ▓▒ ");

            // Image 12: wa-goat.ans
            await ClickNext();
            await UITest.AssertHasText(
                " ▄ ▄■▄▄▄████▓▒▒░ ▓▒░ ▐▀▄▀▓▓▒░█▓▄▄░▒▓▓▓▓▓▓■▀▓▀▌▐▌▐█▄▄▀ ");
        }

        private static async Task ClickNext()
        {
            await UITest.KeyInput(Key.Space);
            await Task.Delay(500); //todo: slow test
        }
    }
}