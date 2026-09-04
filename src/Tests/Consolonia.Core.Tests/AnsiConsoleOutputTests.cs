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
