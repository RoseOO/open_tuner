using System;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Serilog;
using opentuner.Utilities;

namespace opentuner.MediaSources.WinterHill
{
    public partial class WinterHillSource
    {
        UdpClient WH_Client = new UdpClient();

        UDPClient longmynd_status;

        public const int WH_BROADCAST_PORT = 9997;

        /// <summary>
        /// Listens for the PicoTuner (WH) status broadcast on UDP 9997 and extracts
        /// the advertised IP address and base port. Returns false (leaving the
        /// configured values untouched) if nothing is heard before the timeout.
        /// </summary>
        public bool TryAutoFindWinterHill(out string ip, out int basePort)
        {
            ip = null;
            basePort = 0;

            UdpClient listener = null;

            try
            {
                listener = new UdpClient(WH_BROADCAST_PORT);
                listener.Client.ReceiveTimeout = 1000;

                Log.Information("PicoTuner (WH): auto-finding device (listening on UDP " + WH_BROADCAST_PORT +
                                " for up to " + _settings.AutoFindTimeoutMs + " ms)");

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(_settings.AutoFindTimeoutMs);

                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = listener.Receive(ref remote);
                        string message = Encoding.ASCII.GetString(data);

                        if (TryParseBroadcast(message, out string foundIp, out int foundPort))
                        {
                            ip = foundIp;
                            basePort = foundPort;

                            Log.Information("PicoTuner (WH): auto-found at " + ip + ":" + basePort.ToString());
                            return true;
                        }
                    }
                    catch (SocketException sex) when (sex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // no broadcast yet - keep waiting until the deadline
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("PicoTuner (WH): auto-find failed (" + ex.Message + "), using configured address");
            }
            finally
            {
                try { listener?.Close(); } catch { }
            }

            Log.Warning("PicoTuner (WH): no broadcast received, using configured address " +
                        _settings.WinterHillUdpHost + ":" + _settings.WinterHillUdpBasePort.ToString());
            return false;
        }

        private static bool TryParseBroadcast(string message, out string ip, out int basePort)
        {
            ip = null;
            basePort = 0;

            string foundIp = null;
            int foundPort = 0;

            foreach (string rawLine in message.Split('\n'))
            {
                string line = rawLine.Trim();

                if (foundIp == null && line.IndexOf("IP address", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Match m = Regex.Match(line, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                    if (m.Success)
                        foundIp = m.Groups[1].Value;
                }

                if (foundPort == 0 && line.IndexOf("Base IP port", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Match m = Regex.Match(line, @"\b(\d{2,5})\b");
                    if (m.Success)
                        foundPort = int.Parse(m.Groups[1].Value);
                }
            }

            if (foundIp != null && foundPort > 0)
            {
                ip = foundIp;
                basePort = foundPort;
                return true;
            }

            return false;
        }

        public void ConnectWinterHillUDP(int port)
        {

            longmynd_status = new UDPClient(port);

            longmynd_status.DataReceived += Longmynd_status_DataReceived;
            longmynd_status.ConnectionStatusChanged += Longmynd_status_ConnectionStatusChanged;
            longmynd_status.Connect();
        }

        private void Longmynd_status_ConnectionStatusChanged(object sender, bool connection_status)
        {
            if (connection_status)
            {
                Log.Information("UDP Status Port Connected");
            }
            else
                Log.Warning("UDP Status Port Disconnected");
        }

        public void DisconnectWinterHillUDP()
        {
            longmynd_status?.Close();
        }

        private void Longmynd_status_DataReceived(object sender, byte[] e)
        {
            // Log.Information("Status Data Received");

            // Heartbeat for the connection watchdog.
            _lastStatusUtc = DateTime.UtcNow;

            var data = Encoding.ASCII.GetString(e);

            int udp_base_port = _settings.WinterHillUdpBasePort;

            string[] status_strings = data.Split('\n');

            monitorMessage mm = new monitorMessage();
            mm.type = "RX";
            mm.timestamp = 0;
            mm.rx = new ReceiverMessage[3];
            mm.rx[0] = new ReceiverMessage();
            mm.rx[1] = new ReceiverMessage();
            mm.rx[2] = new ReceiverMessage();

            mm.rx[0].rx = 99;
            mm.rx[1].rx = 99;
            mm.rx[2].rx = 99;

            int receiver = 1;

            for (int c = 0; c <  status_strings.Length; c++)
            {
                string[] dt = status_strings[c].Trim().Split(',');


                switch(dt[0])
                {
                    case "$0":

                        int receiver_num = udp_base_port % 100;

                        receiver = Convert.ToInt32(dt[1]) - receiver_num;

                        if (receiver < 1 || receiver > 2 )
                        {
                            //Log.Information( "receiver: " + receiver.ToString() + " : " + status_strings[c]);
                            return;
                        }
                        
                        break;
                    case "$1":
                        switch(dt[1].Trim())
                        {
                            case "DVB-S2":
                                mm.rx[receiver].scanstate = 2;
                                break;
                            case "DVB-S1":
                                mm.rx[receiver].scanstate = 3;
                                break;
                            case "header":
                                mm.rx[receiver].scanstate = 1;
                                break;
                            case "search":
                                mm.rx[receiver].scanstate = 0;
                                break;
                            case "lost":
                                mm.rx[receiver].scanstate = 0;
                                break;
                            default:
                                Log.Warning("WH: Don't know how to decode: " + dt[1]);
                                mm.rx[receiver].scanstate = 1;
                                break;
                        }
                        break;
                    case "$6":
                        mm.rx[receiver].frequency = dt[1];
                        break;
                    case "$9":
                        mm.rx[receiver].symbol_rate = dt[1];
                        break;
                    case "$12":
                        mm.rx[receiver].mer = dt[1];
                        break;
                    case "$11":
                        mm.rx[receiver].ber = dt[1];
                        break;
                    case "$13":
                        mm.rx[receiver].service_name = dt[1]; 
                        break;
                    case "$14":
                        mm.rx[receiver].service_provider_name = dt[1]; 
                        break;
                    case "$15":
                        mm.rx[receiver].null_percentage = dt[1]; 
                        break;
                    case "$18":
                        mm.rx[receiver].modcod = dt[1];
                        break;
                    case "$30":
                        mm.rx[receiver].dbmargin = dt[1];
                        break;

                    case "$33":
                        //Log.Information(dt[1]);

                        if (dt[1].Trim() == "TOP")
                        {
                            mm.rx[receiver].rfport = 0;
                        }
                        else
                        {
                            mm.rx[receiver].rfport = 1;
                        }
                        break;

                    case "$92":
                        string[] ts_data = dt[1].Split(':');

                        if (ts_data.Length > 1)
                        {
                            mm.rx[receiver].ts_addr = ts_data[0];
                            mm.rx[receiver].ts_port = ts_data[1];
                        }
                        break;

                    default:
                        //Log.Information("Unknown status string: " + status_strings[c]);
                        break;
                }
            }

            if (mm.rx[receiver].service_name == null)
                mm.rx[receiver].service_name = "";

            if (mm.rx[receiver].service_provider_name == null)
                mm.rx[receiver].service_provider_name = "";

            if (mm.rx[receiver].mer == null) 
                mm.rx[receiver].mer = "";

            if (mm.rx[receiver].modcod == null)
                mm.rx[receiver].modcod = "";

            if (mm.rx[receiver].dbmargin == null)
                mm.rx[receiver].dbmargin = "";

            if (mm.rx[receiver].ts_port == null) 
                mm.rx[receiver].ts_port = "";

            if (mm.rx[receiver].ts_addr == null)
                mm.rx[receiver].ts_addr = "";


            mm.rx[receiver].rx = receiver;

            UpdateInfo(mm);
        }


        public void UDPSetVoltage(int plug, uint voltage)
        {
            int base_port = _settings.WinterHillUdpBasePort;

            IPEndPoint WinterHill_end_point = new IPEndPoint(IPAddress.Parse(_settings.WinterHillUdpHost), base_port + 21);

            string command2 = "";
            string vg = "vgx";

            if (plug == 0)
            {
                vg = "vgx";
            }
            else
            {
                vg = "vgy";
            }
            switch (voltage)
            {
                case 0: command2 =  ("[to@wh] " + vg + "=OFF"); break;
                case 13: command2 = ("[to@wh] " + vg + "=LO"); break;
                case 18: command2 = ("[to@wh] " + vg + "=HI"); break;
            }

            _settings.LNBVoltage[plug] = voltage;

            // let the SDR spectrum follow the polarisation (18V = horizontal/WB, 13V = vertical/NB)
            if (plug == 0)
                NotifyPolarizationChanged(voltage == 18 ? 2 : (voltage == 13 ? 1 : 0));

            Log.Information(command2);

            byte[] outStream = Encoding.ASCII.GetBytes(command2);
            try
            {
                WH_Client.Client.SendTo(outStream, WinterHill_end_point);
            }
            catch
            {
                Console.WriteLine("Error sending UDP Command");
            }

        }

        public void UDPSetFrequency(int device, int freq, int sr)
        {
            int base_port = _settings.WinterHillUdpBasePort + ( device == 0 ? 21 :  22);

            int receiver_num = (_settings.WinterHillUdpBasePort % 100) + 1 + device;

            IPEndPoint WinterHill_end_point = new IPEndPoint(IPAddress.Parse(_settings.WinterHillUdpHost), base_port );

            Log.Information("UDP Set Frequency Device: " + device + " : " + _settings.WinterHillUdpHost + " : " + base_port.ToString() );
            
            try
            {
                VideoChangeCB?.Invoke(device + 1, false);
                playing[device] = false;
                demodstate[device] = -1;
            }
            catch (Exception ex)
            {
            }

            string command = "[to@wh] rcv=" + receiver_num.ToString() + ",freq=" + freq.ToString() + ",offset=" + _current_offset[device].ToString() + ",srate=" + sr.ToString()  + ",fplug=" + (_settings.RFPort[device] == 0 ? "A" : "B") + "\n";

            Log.Information(command);

            byte[] outStream = Encoding.ASCII.GetBytes(command);
            try
            {
                Log.Information("Setting WH UDP Frequency: " + freq.ToString());
                WH_Client.Client.SendTo(outStream, WinterHill_end_point);
            }
            catch
            {
                Console.WriteLine("Error sending UDP Command");
            }
        }

    }
}
