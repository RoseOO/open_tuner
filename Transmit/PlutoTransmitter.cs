using System;
using opentuner.ExtraFeatures.MqttClient;
using opentuner.Utilities;
using Serilog;

namespace opentuner.Transmit
{
    /// <summary>Snapshot of the Pluto (F5OEO firmware) transmit state.</summary>
    public class PlutoState
    {
        public bool detected = false;
        public string callsign = "NOCALL";
        public string version = "";
        public double frequency = 0;
        public bool transmitting = false;
        public double gain = 0;
        public string ts_source_mode = "";
        public string ts_source_address = "";
        public string mode = "";
        public string fec = "";
        public string constel = "";
        public string frame = "";
        public bool pilots = false;
        public string symbol_rate = "";
        public string fec_mode = "";
        public string temperature = "";
    }

    /// <summary>
    /// UI-agnostic Pluto transmit controller. Talks to F5OEO Pluto firmware over
    /// MQTT (dt/pluto/# for state, cmd/pluto/&lt;CALL&gt;/... for commands) and can
    /// route the local TS UDP stream to the Pluto for on-air transmission.
    /// </summary>
    public class PlutoTransmitter
    {
        public event Action<PlutoState> StateChanged;

        public PlutoState State { get; } = new PlutoState();

        private readonly MqttManager _mqtt;
        private string _cmdPath = "";

        public PlutoTransmitter(MqttManager mqtt)
        {
            _mqtt = mqtt;
            if (_mqtt != null)
                _mqtt.OnMqttMessageReceived += OnMqttMessage;
        }

        public void Close()
        {
            if (_mqtt != null)
                _mqtt.OnMqttMessageReceived -= OnMqttMessage;
        }

        private void OnMqttMessage(MqttMessage message)
        {
            try
            {
                if (message == null || string.IsNullOrEmpty(message.Topic) || !message.Topic.Contains("dt/pluto"))
                    return;

                string[] parts = message.Topic.Split('/');
                if (parts.Length < 3)
                    return;

                if (!State.detected)
                {
                    if (parts[2].ToUpper() == "NOCALL")
                        return;

                    State.detected = true;
                    State.callsign = parts[2].ToUpper();
                    _cmdPath = "cmd/pluto/" + parts[2];
                }

                string prefix = "dt/pluto/" + parts[2];
                if (!message.Topic.StartsWith(prefix))
                    return;

                string topic = message.Topic.Substring(prefix.Length);

                switch (topic)
                {
                    case "/system/version": State.version = message.Message; break;
                    case "/tx/frequency": double.TryParse(message.Message, out State.frequency); break;
                    case "/tx/mute": State.transmitting = message.Message == "1"; break;
                    case "/tx/gain": double.TryParse(message.Message, out State.gain); break;
                    case "/tx/dvbs2/tssourcemode":
                        State.ts_source_mode = message.Message == "0" ? "UDP" : message.Message == "1" ? "File" : message.Message == "2" ? "Pattern" : "Unknown";
                        break;
                    case "/tx/dvbs2/tssourceaddress": State.ts_source_address = message.Message; break;
                    case "/tx/stream/mode":
                        switch (message.Message)
                        {
                            case "pass": State.mode = "Passthrough"; break;
                            case "dvbs2-ts": State.mode = "DVBS2 TS"; break;
                            case "dvbs2-gse": State.mode = "DVBS2 GSE"; break;
                            case "test": State.mode = "Test Tone"; break;
                        }
                        break;
                    case "/tx/dvbs2/fec": State.fec = message.Message; break;
                    case "/tx/dvbs2/constel": State.constel = message.Message.ToUpper(); break;
                    case "/tx/dvbs2/frame": State.frame = message.Message.ToUpper(); break;
                    case "/tx/dvbs2/pilots": State.pilots = message.Message == "1"; break;
                    case "/tx/dvbs2/sr": State.symbol_rate = message.Message; break;
                    case "/tx/dvbs2/fecmode": State.fec_mode = message.Message.ToUpper(); break;
                    case "/temperature_ad": State.temperature = message.Message; break;
                    default: return;
                }

                StateChanged?.Invoke(State);
            }
            catch (Exception ex)
            {
                Log.Warning("Pluto message parse failed: " + ex.Message);
            }
        }

        private void Send(string topic, string value)
        {
            if (_mqtt == null || string.IsNullOrEmpty(_cmdPath) || !_mqtt.IsConnected)
                return;

            try { _ = _mqtt.SendMqttCommand(_cmdPath + topic, value); }
            catch (Exception ex) { Log.Warning("Pluto command failed: " + ex.Message); }
        }

        public void ConfigureHardwareMode(string mode) => Send("/tx/stream/mode", mode);
        public void SetHardwareMode(int option)
        {
            switch (option)
            {
                case 0: ConfigureHardwareMode("pass"); break;
                case 1: ConfigureHardwareMode("dvbs2-ts"); break;
                case 2: ConfigureHardwareMode("dvbs2-gse"); break;
                case 3: ConfigureHardwareMode("test"); break;
            }
        }

        public void SetFrequency(int frequencyHz) => Send("/tx/frequency", frequencyHz.ToString());
        public void SetGain(double gain) => Send("/tx/gain", gain.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void SetSymbolRate(int sr) => Send("/tx/dvbs2/sr", sr.ToString());
        public void SetFec(string fec) => Send("/tx/dvbs2/fec", fec);
        public void SetConstellation(string constel) => Send("/tx/dvbs2/constel", constel);
        public void SetFrame(string frame) => Send("/tx/dvbs2/frame", frame);
        public void SetFecMode(string mode) => Send("/tx/dvbs2/fecmode", mode);
        public void SetPilots(bool on) => Send("/tx/dvbs2/pilots", on ? "1" : "0");
        public void SetTsSourceMode(int mode) => Send("/tx/dvbs2/tssourcemode", mode.ToString());
        public void SetTsSourceAddress(string address) => Send("/tx/dvbs2/tssourceaddress", address);

        /// <summary>
        /// Points the Pluto at a UDP TS source (our TSUdpStreamer) and selects UDP mode.
        /// </summary>
        public void RouteTsFromUdp(string host, int port)
        {
            SetTsSourceMode(0);
            SetTsSourceAddress("udp://" + host + ":" + port);
        }

        // requires the call to be set; the firmware reboots automatically
        public void ConfigureCallsignAndReboot(string callsign)
        {
            try { if (_mqtt != null) _ = _mqtt.SendMqttCommand("cmd/pluto/call", callsign); } catch { }
        }

        public void Reboot()
        {
            try { if (_mqtt != null) _ = _mqtt.SendMqttCommand("system/reboot", "reboot"); } catch { }
        }
    }
}
