using System.IO;
using opentuner.Utilities;
using Xunit;

namespace OpenTuner.Tests
{
    public class BandplanTests
    {
        private const string Sample = @"<?xml version=""1.0""?>
<channels>
  <channel>
    <x-freq>10491.5</x-freq>
    <s-freq>2402</s-freq>
    <sr>1500</sr>
    <name>BEACON</name>
    <block>A</block>
  </channel>
  <channel>
    <x-freq>10493.25</x-freq>
    <s-freq>2403.75</s-freq>
    <sr>1000</sr>
    <name>1000</name>
    <block>A</block>
  </channel>
</channels>";

        [Fact]
        public void Load_parses_channels()
        {
            string file = Path.Combine(Path.GetTempPath(), "bandplan_test_" + System.Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(file, Sample);

            try
            {
                var bp = Bandplan.Load(file);

                Assert.Equal(2, bp.Channels.Count);
                Assert.Equal(10491.5, bp.Channels[0].RxFreqMHz, 3);
                Assert.Equal(2402, bp.Channels[0].TxFreqMHz, 3);
                Assert.Equal(1500u, bp.Channels[0].SymbolRate);
                Assert.True(bp.Channels[0].IsBeacon);
                Assert.False(bp.Channels[1].IsBeacon);
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        [Fact]
        public void Load_missing_file_returns_empty()
        {
            var bp = Bandplan.Load(Path.Combine(Path.GetTempPath(), "does_not_exist_" + System.Guid.NewGuid().ToString("N") + ".xml"));
            Assert.Empty(bp.Channels);
        }
    }
}
