using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace opentuner.Qso
{
    /// <summary>Writes QSO records as an ADIF 3.1.4 document.</summary>
    public static class AdifExport
    {
        public static string Build(IEnumerable<QsoRecord> records, string programVersion = "OpenTuner")
        {
            var sb = new StringBuilder();

            sb.Append("Open Tuner ADIF export\r\n");
            sb.Append("<adif_ver:5>3.1.4 ").Append('\n');
            sb.Append("<programid:9>Open Tuner ").Append('\n');
            sb.Append("<created_timestamp:15>")
              .Append(DateTime.UtcNow.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture))
              .Append(' ').Append('\n');
            sb.Append("<EOH>\r\n\r\n");

            if (records != null)
            {
                foreach (QsoRecord q in records)
                    AppendQso(sb, q);
            }

            return sb.ToString();
        }

        private static void AppendQso(StringBuilder sb, QsoRecord q)
        {
            Field(sb, "CALL", q.call);
            Field(sb, "QSO_DATE", q.time_on_utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Field(sb, "TIME_ON", q.time_on_utc.ToString("HHmmss", CultureInfo.InvariantCulture));
            Field(sb, "QSO_DATE_OFF", q.time_off_utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Field(sb, "TIME_OFF", q.time_off_utc.ToString("HHmmss", CultureInfo.InvariantCulture));
            Field(sb, "BAND", q.Band);
            Field(sb, "FREQ", q.freq_mhz > 0 ? q.freq_mhz.ToString("0.######", CultureInfo.InvariantCulture) : "");
            Field(sb, "MODE", q.mode);
            Field(sb, "SUBMODE", q.submode);
            Field(sb, "PROP_MODE", q.prop_mode);
            Field(sb, "SAT_NAME", q.sat_name);
            Field(sb, "RST_SENT", q.rst_sent);
            Field(sb, "RST_RCVD", q.rst_rcvd);
            Field(sb, "STATION_CALLSIGN", q.my_call);
            Field(sb, "OPERATOR", q.my_call);
            Field(sb, "COMMENT", q.comment);
            sb.Append("<EOR>\r\n\r\n");
        }

        private static void Field(StringBuilder sb, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            sb.Append('<').Append(name).Append(':').Append(value.Length).Append('>');
            sb.Append(value);
            sb.Append('\n');
        }
    }
}
