using Consolonia.Controls;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class AnsiConsoleOutputTests
    {
        [TestCase("[?62;4;22c", ExpectedResult = true,
            TestName = "SixelFeatureIsDetected")]
        [TestCase("[?62;4c", ExpectedResult = true,
            TestName = "SixelFeatureAsLastParameterIsDetected")]
        [TestCase("[?61;6;7;14;21;22;23;24;28;32;42;4c", ExpectedResult = true,
            TestName = "SixelFeatureInLongResponseIsDetected")]
        [TestCase("[?62;22c", ExpectedResult = false,
            TestName = "ResponseWithoutSixelFeatureIsRejected")]
        [TestCase("[?62;44;14c", ExpectedResult = false,
            TestName = "FeatureContainingDigitFourIsNotMistakenForSixel")]
        [TestCase("[?4c", ExpectedResult = false,
            TestName = "DeviceClassFourWithoutFeaturesIsRejected")]
        [TestCase("[?62c", ExpectedResult = false,
            TestName = "ResponseWithoutFeaturesIsRejected")]
        [TestCase("", ExpectedResult = false,
            TestName = "EmptyResponseIsRejected")]
        [TestCase("garbage", ExpectedResult = false,
            TestName = "GarbageResponseIsRejected")]
        [TestCase("[?2026;2$y[?62;4;22c", ExpectedResult = true,
            TestName = "SixelFeatureAfterSynchronizedOutputReplyIsDetected")]
        [TestCase("[?2026;2$y[?62;22c", ExpectedResult = false,
            TestName = "SynchronizedOutputReplyIsNotReadAsDeviceAttributes")]
        public bool DetectsSixelSupportFromDeviceAttributes(string deviceAttributesResponse)
        {
            return AnsiConsoleOutput.DeviceAttributesIndicateSixelSupport(deviceAttributesResponse);
        }

        [TestCase("_Gi=31;OK", ExpectedResult = true,
            TestName = "KittyGraphicsReplyIsDetected")]
        [TestCase("_Gi=31;OK[?62;4;22c", ExpectedResult = true,
            TestName = "KittyGraphicsReplyCombinedWithDeviceAttributesIsDetected")]
        [TestCase("_Gi=31;EBADF:something went wrong", ExpectedResult = false,
            TestName = "KittyGraphicsErrorReplyIsRejected")]
        [TestCase("[?62;4;22c", ExpectedResult = false,
            TestName = "DeviceAttributesAloneAreNotKittyGraphics")]
        [TestCase("", ExpectedResult = false,
            TestName = "EmptyKittyResponseIsRejected")]
        public bool DetectsKittyGraphicsSupportFromProbeResponse(string response)
        {
            return AnsiConsoleOutput.ResponseIndicatesKittyGraphicsSupport(response);
        }

        [TestCase("[?2026;1$y", ExpectedResult = true,
            TestName = "SynchronizedOutputSetStateIsDetected")]
        [TestCase("[?2026;2$y", ExpectedResult = true,
            TestName = "SynchronizedOutputResetStateIsDetected")]
        [TestCase("[?2026;3$y", ExpectedResult = true,
            TestName = "SynchronizedOutputPermanentlySetStateIsDetected")]
        [TestCase("[?2026;0$y", ExpectedResult = false,
            TestName = "SynchronizedOutputUnrecognizedModeIsRejected")]
        [TestCase("[?2026;4$y", ExpectedResult = false,
            TestName = "SynchronizedOutputPermanentlyResetStateIsRejected")]
        [TestCase("_Gi=31;OK[?2026;2$y[?62;4;22c", ExpectedResult = true,
            TestName = "SynchronizedOutputReplyCombinedWithOtherProbeRepliesIsDetected")]
        [TestCase("[?62;4;22c", ExpectedResult = false,
            TestName = "DeviceAttributesAloneAreNotSynchronizedOutput")]
        [TestCase("", ExpectedResult = false,
            TestName = "EmptySynchronizedOutputResponseIsRejected")]
        public bool DetectsSynchronizedOutputSupportFromProbeResponse(string response)
        {
            return AnsiConsoleOutput.ResponseIndicatesSynchronizedOutputSupport(response);
        }

        [TestCase("[?1016;1$y", ExpectedResult = true,
            TestName = "SgrPixelsMouseSetStateIsDetected")]
        [TestCase("[?1016;2$y", ExpectedResult = true,
            TestName = "SgrPixelsMouseResetStateIsDetected")]
        [TestCase("[?1016;0$y", ExpectedResult = false,
            TestName = "SgrPixelsMouseUnrecognizedModeIsRejected")]
        [TestCase("_Gi=31;OK[?2026;2$y[?1016;2$y[?62;4;22c", ExpectedResult = true,
            TestName = "SgrPixelsMouseReplyCombinedWithOtherProbeRepliesIsDetected")]
        [TestCase("[?2026;2$y[?1016;0$y[?62;4;22c", ExpectedResult = false,
            TestName = "SynchronizedOutputSupportIsNotMistakenForSgrPixelsMouse")]
        [TestCase("[?10160;1$y", ExpectedResult = false,
            TestName = "LongerModeNumberIsNotMistakenForSgrPixelsMouse")]
        [TestCase("", ExpectedResult = false,
            TestName = "EmptySgrPixelsMouseResponseIsRejected")]
        public bool DetectsSgrPixelsMouseSupportFromProbeResponse(string response)
        {
            return AnsiConsoleOutput.ResponseIndicatesSgrPixelsMouseSupport(response);
        }

        [TestCase("kitty", ConsoleCapabilities.None,
            ExpectedResult = ConsoleCapabilities.SupportsKittyGraphics,
            TestName = "KittyOverrideForcesKittyGraphicsOn")]
        [TestCase("KITTY", ConsoleCapabilities.SupportsSixel,
            ExpectedResult = ConsoleCapabilities.SupportsSixel | ConsoleCapabilities.SupportsKittyGraphics,
            TestName = "KittyOverrideIsCaseInsensitiveAndKeepsSixel")]
        [TestCase("sixel", ConsoleCapabilities.SupportsKittyGraphics,
            ExpectedResult = ConsoleCapabilities.SupportsSixel,
            TestName = "SixelOverrideForcesSixelAndDisablesKitty")]
        [TestCase("quad", ConsoleCapabilities.SupportsKittyGraphics | ConsoleCapabilities.SupportsSixel,
            ExpectedResult = ConsoleCapabilities.None,
            TestName = "QuadOverrideDisablesGraphicsProtocols")]
        [TestCase(null, ConsoleCapabilities.SupportsSixel,
            ExpectedResult = ConsoleCapabilities.SupportsSixel,
            TestName = "MissingOverrideLeavesDetectionUntouched")]
        [TestCase("garbage", ConsoleCapabilities.SupportsSixel,
            ExpectedResult = ConsoleCapabilities.SupportsSixel,
            TestName = "UnknownOverrideLeavesDetectionUntouched")]
        public ConsoleCapabilities AppliesGraphicsProtocolOverride(string overrideValue,
            ConsoleCapabilities detected)
        {
            return AnsiConsoleOutput.ApplyGraphicsProtocolOverride(detected, overrideValue);
        }
    }
}