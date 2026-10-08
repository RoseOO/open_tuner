using System;

namespace opentuner.Qso
{
    /// <summary>
    /// A single logged contact. Times are UTC. Designed to carry everything needed
    /// for an ADIF record, including the satellite specifics (QO-100 etc).
    /// </summary>
    public class QsoRecord
    {
        public string call = "";                 // worked station
        public string my_call = "";              // operator / station callsign
        public DateTime time_on_utc = DateTime.UtcNow;
        public DateTime time_off_utc = DateTime.UtcNow;
        public double freq_mhz = 0;              // QSO frequency (satellite downlink by default)
        public string mode = "DV";               // ADIF MODE
        public string submode = "DATV";          // ADIF SUBMODE (e.g. DATV)
        public string prop_mode = "SAT";         // ADIF PROP_MODE
        public string sat_name = "QO-100";       // ADIF SAT_NAME
        public string rst_sent = "59";
        public string rst_rcvd = "59";
        public string comment = "";

        /// <summary>ADIF BAND derived from the frequency (e.g. 3cm, 13cm).</summary>
        public string Band
        {
            get
            {
                double mhz = freq_mhz;
                if (mhz <= 0) return "";
                if (mhz >= 10000 && mhz <= 10500) return "3cm";
                if (mhz >= 5650 && mhz <= 5925) return "6cm";
                if (mhz >= 2400 && mhz <= 2500) return "13cm";
                if (mhz >= 1240 && mhz <= 1300) return "23cm";
                if (mhz >= 420 && mhz <= 450) return "70cm";
                if (mhz >= 144 && mhz <= 148) return "2m";
                return "";
            }
        }

        /// <summary>Total on-air duration of the contact.</summary>
        public TimeSpan Duration => time_off_utc >= time_on_utc ? time_off_utc - time_on_utc : TimeSpan.Zero;
    }
}
