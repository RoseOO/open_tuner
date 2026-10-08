using System;
using System.Collections.Generic;
using opentuner.Qso;
using Xunit;

namespace OpenTuner.Tests
{
    public class AdifTests
    {
        [Fact]
        public void Builds_header_and_eoh()
        {
            var adif = AdifExport.Build(new List<QsoRecord>());
            Assert.Contains("<adif_ver:5>3.1.4", adif);
            Assert.Contains("<programid:9>Open Tuner", adif);
            Assert.Contains("<EOH>", adif);
        }

        [Fact]
        public void Qso_record_has_correct_fields_and_lengths()
        {
            var q = new QsoRecord
            {
                call = "G4KLB",
                my_call = "M1RXO",
                time_on_utc = new DateTime(2026, 10, 8, 12, 34, 56, DateTimeKind.Utc),
                time_off_utc = new DateTime(2026, 10, 8, 12, 40, 12, DateTimeKind.Utc),
                freq_mhz = 10489.75,
                mode = "DV",
                submode = "DATV",
                prop_mode = "SAT",
                sat_name = "QO-100",
                rst_sent = "59",
                rst_rcvd = "57",
                comment = "good signal"
            };

            var adif = AdifExport.Build(new[] { q });

            Assert.Contains("<CALL:5>G4KLB", adif);
            Assert.Contains("<QSO_DATE:8>20261008", adif);
            Assert.Contains("<TIME_ON:6>123456", adif);
            Assert.Contains("<TIME_OFF:6>124012", adif);
            Assert.Contains("<BAND:3>3cm", adif);
            Assert.Contains("<FREQ:8>10489.75", adif);
            Assert.Contains("<MODE:2>DV", adif);
            Assert.Contains("<SUBMODE:4>DATV", adif);
            Assert.Contains("<PROP_MODE:3>SAT", adif);
            Assert.Contains("<SAT_NAME:6>QO-100", adif);
            Assert.Contains("<STATION_CALLSIGN:5>M1RXO", adif);
            Assert.Contains("<COMMENT:11>good signal", adif);
            Assert.Contains("<EOR>", adif);
        }

        [Theory]
        [InlineData(10489.75, "3cm")]
        [InlineData(2400.0, "13cm")]
        [InlineData(1296.0, "23cm")]
        [InlineData(432.0, "70cm")]
        [InlineData(145.0, "2m")]
        public void Band_is_derived_from_frequency(double mhz, string band)
        {
            var q = new QsoRecord { freq_mhz = mhz };
            Assert.Equal(band, q.Band);
        }

        [Fact]
        public void Duration_is_time_off_minus_time_on()
        {
            var q = new QsoRecord
            {
                time_on_utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                time_off_utc = new DateTime(2026, 1, 1, 0, 5, 30, DateTimeKind.Utc)
            };
            Assert.Equal(TimeSpan.FromMinutes(5.5), q.Duration);
        }
    }
}
