using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner.ExtraFeatures.QRZ
{
    public class QrzSettings
    {
        public bool enabled = false;
        public bool auto_lookup = true;
        public string username = "";
        public string password = "";
    }

    public class QrzResult
    {
        public bool success = false;
        public string callsign = "";
        public string name = "";
        public string country = "";
        public string grid = "";
        public string city = "";
        public string state = "";
        public string lat = "";
        public string lon = "";
        public string image_url = "";
        public string licence_class = "";
        public string email = "";
        public string error = "";

        public string DisplayName
        {
            get
            {
                string n = (name ?? "").Trim();
                return string.IsNullOrEmpty(n) ? callsign : n;
            }
        }

        public override string ToString()
        {
            if (!success)
                return callsign + ": " + (string.IsNullOrEmpty(error) ? "no data" : error);

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(name)) parts.Add(name);
            if (!string.IsNullOrEmpty(city)) parts.Add(city);
            else if (!string.IsNullOrEmpty(state)) parts.Add(state);
            if (!string.IsNullOrEmpty(country)) parts.Add(country);
            if (!string.IsNullOrEmpty(grid)) parts.Add("[" + grid + "]");
            return callsign + " - " + string.Join(", ", parts);
        }
    }
}
