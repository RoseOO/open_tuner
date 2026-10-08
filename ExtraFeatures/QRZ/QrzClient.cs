using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Serilog;

namespace opentuner.ExtraFeatures.QRZ
{
    /// <summary>
    /// Minimal client for the QRZ.com XML data subscription API.
    ///   Login:  https://xmldata.qrz.com/xml/current/?username=..;password=..;agent=..
    ///   Lookup: https://xmldata.qrz.com/xml/current/?s=SESSION;callsign=..
    /// A QRZ XML subscription is required for callsign lookups to return data.
    /// </summary>
    public class QrzClient
    {
        private const string Endpoint = "https://xmldata.qrz.com/xml/current/";
        private const string Agent = "OpenTuner-1.0";

        private readonly QrzSettings _settings;
        private string _sessionKey = "";

        public QrzClient(QrzSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }
        }

        public bool HasCredentials => !string.IsNullOrWhiteSpace(_settings.username) && !string.IsNullOrWhiteSpace(_settings.password);

        private static string Fetch(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = Agent;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private bool Login(out string error)
        {
            error = "";

            if (!HasCredentials)
            {
                error = "No QRZ username/password configured";
                return false;
            }

            try
            {
                string url = Endpoint + "?username=" + Uri.EscapeDataString(_settings.username) +
                             ";password=" + Uri.EscapeDataString(_settings.password) +
                             ";agent=" + Uri.EscapeDataString(Agent);

                string raw = Fetch(url);
                XDocument doc = XDocument.Parse(raw);
                var session = Child(doc.Root, "Session");

                string err = Child(session, "Error")?.Value;
                string key = Child(session, "Key")?.Value;

                if (string.IsNullOrEmpty(err) && string.IsNullOrEmpty(key))
                    err = "Unexpected response: " + (raw != null && raw.Length > 300 ? raw.Substring(0, 300) : raw);

                if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(key))
                {
                    error = string.IsNullOrEmpty(err) ? "Login failed" : err;
                    _sessionKey = "";
                    return false;
                }

                _sessionKey = key;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _sessionKey = "";
                return false;
            }
        }

        public QrzResult Lookup(string callsign)
        {
            var result = new QrzResult { callsign = callsign };

            if (string.IsNullOrWhiteSpace(callsign))
            {
                result.error = "No callsign";
                return result;
            }

            try
            {
                if (string.IsNullOrEmpty(_sessionKey))
                {
                    if (!Login(out string loginError))
                    {
                        result.error = loginError;
                        return result;
                    }
                }

                XDocument doc = XDocument.Parse(Fetch(Endpoint + "?s=" + Uri.EscapeDataString(_sessionKey) + ";callsign=" + Uri.EscapeDataString(callsign)));
                var session = Child(doc.Root, "Session");

                string sessionError = Child(session, "Error")?.Value;
                // Session expired -> re-login once and retry inline.
                if (!string.IsNullOrEmpty(sessionError) && sessionError.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (Login(out string loginError))
                    {
                        doc = XDocument.Parse(Fetch(Endpoint + "?s=" + Uri.EscapeDataString(_sessionKey) + ";callsign=" + Uri.EscapeDataString(callsign)));
                        session = Child(doc.Root, "Session");
                        sessionError = Child(session, "Error")?.Value;
                    }
                    else
                    {
                        result.error = loginError;
                        return result;
                    }
                }

                var call = Child(doc.Root, "Callsign");
                if (call == null)
                {
                    result.error = string.IsNullOrEmpty(sessionError) ? "Not found" : sessionError;
                    return result;
                }

                result.success = true;
                result.callsign = Val(call, "call", callsign);
                result.name = Join(Val(call, "fname", ""), Val(call, "name", ""));
                result.country = Val(call, "country", "");
                result.grid = Val(call, "grid", "");
                result.city = Val(call, "addr2", "");
                result.state = Val(call, "state", "");
                result.lat = Val(call, "lat", "");
                result.lon = Val(call, "lon", "");
                result.image_url = Val(call, "image", "");
                result.licence_class = Val(call, "class", "");
                result.email = Val(call, "email", "");
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning("QRZ lookup failed for " + callsign + ": " + ex.Message);
                result.error = ex.Message;
                return result;
            }
        }

        public Task<QrzResult> LookupAsync(string callsign)
        {
            return Task.Run(() => Lookup(callsign));
        }

        private static string Val(XElement element, string name, string fallback)
        {
            string v = Child(element, name)?.Value;
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        /// <summary>
        /// Finds a child element by local name, ignoring any XML namespace.
        /// The QRZ.com API wraps its response in the default namespace
        /// "http://xmldata.qrz.com", so a plain Element("Session") returns null.
        /// </summary>
        private static XElement Child(XElement parent, string name)
        {
            if (parent == null)
                return null;

            foreach (var e in parent.Elements())
                if (e.Name.LocalName == name)
                    return e;

            return null;
        }

        private static string Join(string a, string b)
        {
            string result = ((a ?? "").Trim() + " " + (b ?? "").Trim()).Trim();
            return result;
        }
    }
}
