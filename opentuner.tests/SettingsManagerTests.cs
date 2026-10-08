using System.IO;
using opentuner.Utilities;
using Xunit;

namespace OpenTuner.Tests
{
    public class SettingsManagerTests
    {
        public class SampleSettings
        {
            public string Name = "hello";
            public int Value = 42;
            public bool Flag = true;
            public string[] Items = { "a", "b" };
        }

        [Fact]
        public void Save_and_load_round_trips()
        {
            string group = "opentuner_test_" + System.Guid.NewGuid().ToString("N");
            var mgr = new SettingsManager<SampleSettings>(group);

            try
            {
                var original = new SampleSettings { Name = "roundtrip", Value = 7, Flag = false, Items = new[] { "x", "y", "z" } };
                Assert.True(mgr.SaveSettings(original));

                var loaded = mgr.LoadSettings(new SampleSettings());

                Assert.Equal("roundtrip", loaded.Name);
                Assert.Equal(7, loaded.Value);
                Assert.False(loaded.Flag);
                Assert.Equal(new[] { "x", "y", "z" }, loaded.Items);
            }
            finally
            {
                string path = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "settings", group + ".json");
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void Load_with_missing_file_returns_defaults()
        {
            string group = "opentuner_test_missing_" + System.Guid.NewGuid().ToString("N");
            var mgr = new SettingsManager<SampleSettings>(group);

            try
            {
                var defaults = new SampleSettings { Name = "default", Value = 1 };
                var loaded = mgr.LoadSettings(defaults);
                Assert.Equal("default", loaded.Name);
                Assert.Equal(1, loaded.Value);
            }
            finally
            {
                string path = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "settings", group + ".json");
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }
}
