using opentuner;
using Xunit;

namespace OpenTuner.Tests
{
    public class TSRecorderTests
    {
        [Theory]
        [InlineData("G4KLB", "G4KLB")]
        [InlineData("M0DNY John", "M0DNY_John")]
        [InlineData("", "unknown")]
        [InlineData("   ", "unknown")]
        [InlineData("a/b\\c:d", "a_b_c_d")]
        public void Sanitize_makes_valid_filename(string input, string expected)
        {
            Assert.Equal(expected, TSRecorder.Sanitize(input));
        }
    }
}
