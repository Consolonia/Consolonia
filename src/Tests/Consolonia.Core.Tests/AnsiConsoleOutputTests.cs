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
    }
}
