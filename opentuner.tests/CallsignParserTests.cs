using opentuner.Utilities;
using Xunit;

namespace OpenTuner.Tests
{
    public class CallsignParserTests
    {
        [Theory]
        [InlineData("G4KLB", "G4KLB")]
        [InlineData("g4klb", "G4KLB")]
        [InlineData("M0DNY John", "M0DNY")]
        [InlineData("F5OEO/P test", "F5OEO")]
        [InlineData("GB3IT", "GB3IT")]
        [InlineData("QO-100 WB beacon", null)]
        [InlineData("1080p test pattern", null)]
        [InlineData("", null)]
        [InlineData("   ", null)]
        [InlineData("1000/1500/333", null)]
        [InlineData("VK3ABC - Melbourne", "VK3ABC")]
        [InlineData("ZS6ABC test", "ZS6ABC")]
        public void Extract_finds_callsign(string input, string expected)
        {
            Assert.Equal(expected, CallsignParser.Extract(input));
        }

        [Theory]
        [InlineData("F5OEO/P", "F5OEO")]
        [InlineData("G4KLB/MM", "G4KLB")]
        [InlineData("G4KLB", "G4KLB")]
        public void StripSuffix_removes_portable_suffix(string input, string expected)
        {
            Assert.Equal(expected, CallsignParser.StripSuffix(input));
        }
    }
}
