using opentuner.MediaPlayers;
using opentuner.Utilities;
using Serilog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace opentuner.MediaSources.WinterHill
{
    public partial class WinterHillSource : OTSource
    {
        private WinterHillSettings _settings;
        private SettingsManager<WinterHillSettings> _settingsManager;

        public override event SourceDataChange OnSourceData;

        private bool _connected = false;
        public override bool DeviceConnected => _connected;

        private VideoChangeCallback VideoChangeCB;

        Thread[] ts_thread_t = null;
        TSThread[] ts_threads;

        // todo: fix double buffer read
        private CircularBuffer[] udp_buffer; // = new CircularBuffer(GlobalDefines.CircularBufferStartingCapacity);
        public CircularBuffer[] ts_data_queue; // = new CircularBuffer(GlobalDefines.CircularBufferStartingCapacity);

        private OTMediaPlayer[] _media_player = new OTMediaPlayer[4];
        private TSRecorder[] _recorders = new TSRecorder[4];
        private TSUdpStreamer[] _streamer = new TSUdpStreamer[4];
        private List<TunerControlForm> _tuner_forms;

        private int ts_devices = 4;

        UDPClient[] udp_clients;

        private string _MediaPath;
        private string _LocalIp;

        private int[] _current_frequency = new int[4] {0, 0, 0, 0};
        private int[] _current_offset = new int[4] { 0, 0, 0, 0 };
        private int[] _current_sr = new int[4] { 0, 0, 0, 0 };

        private string[] last_service_name = new string[4] { "", "", "", "" };
        private string[] last_service_provider = new string[4] { "", "", "", "" };
        private string[] last_dbm = new string[4] { "", "", "", "" };
        private string[] last_mer = new string[4] { "", "", "", "" };
        
        private int[] demodstate = new int[4] {0, 0, 0, 0};

        private bool[] playing = new bool[4] {false, false, false, false};

        bool _videoPlayersReady = false;

        int hw_device = 1;

        // connection watchdog / auto reconnect (PicoTuner WH over Ethernet)
        private volatile bool _shutdownWatchdog = false;
        private Thread reconnect_thread_t = null;
        private DateTime _lastStatusUtc = DateTime.UtcNow;
        private bool _whOffline = false;
        private DateTime _lastWhRetry = DateTime.MinValue;
        private DateTime _whStartUtc = DateTime.MinValue;

        public WinterHillSource()
        {
            // settings
            _settings = new WinterHillSettings();
            _settingsManager = new SettingsManager<WinterHillSettings>("winterhill_settings");
            _settings = _settingsManager.LoadSettings(_settings);
        }


        public override int Initialize(VideoChangeCallback VideoChangeCB, Control Parent)
        {
            _parent = Parent;

            int udp_port = _settings.WinterHillWSUdpBasePort;

            int defaultInterface = _settings.DefaultInterface;

            // Headless (WPF) mode: skip the WinForms choose-interface dialog.
            if (Parent == null && defaultInterface == 0)
            {
                defaultInterface = 2;   // PicoTuner Ethernet
                Log.Warning("No default WinterHill interface set - defaulting to PicoTuner Ethernet (set it in source settings)");
            }

            if (defaultInterface == 0)
            {
                ChooseWinterHillHardwareInterfaceForm chooseInterfaceForm = new ChooseWinterHillHardwareInterfaceForm();

                if (chooseInterfaceForm.ShowDialog() == DialogResult.OK)
                {
                    defaultInterface = chooseInterfaceForm.comboHardwareInterface.SelectedIndex + 1;
                }
                else
                {
                    return -1;
                }
            }

            for (int c = 0; c < 4; c++)
                _current_offset[c] = (int)_settings.DefaultOffset[c];

            // connect interface
            switch (defaultInterface)
            {
                case 1: // websockets
                    connectWebsockets();
                    udp_port = _settings.WinterHillWSUdpBasePort;
                    ts_devices = 4;
                    hw_device = 1;

                    break;
                case 2: // udp pico wh
                    // Optionally discover the device's IP address and base port from
                    // its status broadcast before connecting.
                    if (_settings.AutoFindUdp)
                    {
                        Cursor previousCursor = Cursor.Current;
                        Cursor.Current = Cursors.WaitCursor;

                        try
                        {
                            if (TryAutoFindWinterHill(out string foundIp, out int foundBasePort))
                            {
                                _settings.WinterHillUdpHost = foundIp;
                                _settings.WinterHillUdpBasePort = foundBasePort;
                                _settingsManager.SaveSettings(_settings);
                            }
                        }
                        finally
                        {
                            Cursor.Current = previousCursor;
                        }
                    }

                    udp_port = _settings.WinterHillUdpBasePort;
                    ConnectWinterHillUDP(udp_port + 1);
                    _whStartUtc = DateTime.UtcNow;

                    UDPSetVoltage(0, _settings.LNBVoltage[0]);
                    UDPSetVoltage(1, _settings.LNBVoltage[1]);


                    ts_devices = 2;
                    hw_device = 2;
                    break;
            }

            for (int c = 0; c < ts_devices; c++)
                SetFrequency(c, _settings.DefaultFrequency[c], _settings.DefaultSR[c], true);

            // open udp ts ports
            udp_clients = new UDPClient[4];
            ts_threads = new TSThread[4];
            ts_thread_t = new Thread[4];
            ts_data_queue = new CircularBuffer[4];
            udp_buffer = new CircularBuffer[4];

            for (int c = 0; c < ts_devices; c++)
            {
                int port = udp_port + 41 + c;

                Log.Information("TS UDP Ports: " + port.ToString());

                udp_buffer[c] = new CircularBuffer(GlobalDefines.CircularBufferStartingCapacity);
                ts_data_queue[c] = new CircularBuffer(GlobalDefines.CircularBufferStartingCapacity);

                udp_clients[c] = new UDPClient(port, c);
                udp_clients[c].ConnectionStatusChanged += WinterHillSource_ConnectionStatusChanged;
                udp_clients[c].DataReceived += WinterHillSource_DataReceived;
                udp_clients[c].Connect();

                FlushTS flush_ts = null;
                ReadTS read_ts = null;

                switch(c)
                {
                    case 0: flush_ts = FlushTS0; read_ts = ReadTS0; break;
                    case 1: flush_ts = FlushTS1; read_ts = ReadTS1; break;
                    case 2: flush_ts = FlushTS2; read_ts = ReadTS2; break;
                    case 3: flush_ts = FlushTS3; read_ts = ReadTS3; break;
                }


                ts_threads[c] = new TSThread(ts_data_queue[c], flush_ts, read_ts, "WH TS" + c.ToString());
                ts_thread_t[c] = new Thread(ts_threads[c].worker_thread) { IsBackground = true, Name = "WH TS" + c.ToString() };
                ts_thread_t[c].Start();

            }

            BuildSourceProperties();

            this.VideoChangeCB = VideoChangeCB;

            // get local ip
            List<string> detected_ips = CommonFunctions.determineIP();

            if (detected_ips.Count > 0)
                _LocalIp = detected_ips[0];

            if (detected_ips.Count > 1)
            {
                for (int c = 0; c < detected_ips.Count; c++)
                {
                    Log.Warning(detected_ips[c]);
                }
                Log.Warning("Multiple IP's detected, using " + _LocalIp);
            }

            // Watch the status feed and transparently re-send tuning settings if the
            // PicoTuner (WH) over Ethernet goes away (e.g. power cycle / network drop).
            _shutdownWatchdog = false;
            reconnect_thread_t = new Thread(reconnect_watchdog)
            {
                IsBackground = true,
                Name = "WinterHillReconnect"
            };
            reconnect_thread_t.Start();

            return ts_devices;
        }

        private void reconnect_watchdog()
        {
            while (!_shutdownWatchdog)
            {
                Thread.Sleep(1000);

                if (_shutdownWatchdog)
                    return;

                // Only applies to the PicoTuner (WH) Ethernet interface.
                if (hw_device != 2 || (!_settings.AutoReconnect && !_settings.AutoFindUdp))
                    continue;

                // grace period after (re)starting so we don't declare "offline" before
                // the first status packet has had a chance to arrive
                if ((DateTime.UtcNow - _whStartUtc).TotalSeconds < 10 && _lastStatusUtc <= _whStartUtc)
                    continue;

                bool online = (DateTime.UtcNow - _lastStatusUtc).TotalSeconds < 6;

                if (online)
                {
                    if (_whOffline)
                    {
                        _whOffline = false;
                        Log.Information("PicoTuner (WH) connection restored - restoring tuning settings");
                        ResendWinterHillSettings();
                    }
                    continue;
                }

                if (!_whOffline)
                {
                    _whOffline = true;
                    if (_settings.AutoReconnect)
                        Log.Warning("PicoTuner (WH) connection lost - will keep looking and re-sending tuning settings");
                    else
                        Log.Warning("PicoTuner (WH) connection lost - will keep looking for the device");
                }

                // Auto-find: keep listening for the device broadcast and switch to
                // the advertised IP / base port once found (repeat until connected).
                if (_settings.AutoFindUdp)
                {
                    try
                    {
                        if (TryAutoFindWinterHill(out string foundIp, out int foundPort))
                        {
                            if (foundIp != _settings.WinterHillUdpHost || foundPort != _settings.WinterHillUdpBasePort)
                            {
                                Log.Information("PicoTuner (WH): auto-find updated address to " + foundIp + ":" + foundPort);
                                _settings.WinterHillUdpHost = foundIp;
                                _settings.WinterHillUdpBasePort = foundPort;
                                _settingsManager.SaveSettings(_settings);
                            }
                        }
                    }
                    catch { }
                }

                // Periodically re-send tuning so the device recovers even if it
                // rebooted and forgot its settings.
                if (_settings.AutoReconnect && (DateTime.UtcNow - _lastWhRetry).TotalSeconds >= 5)
                {
                    _lastWhRetry = DateTime.UtcNow;
                    ResendWinterHillSettings();
                }
            }
        }

        private void ResendWinterHillSettings()
        {
            try
            {
                UDPSetVoltage(0, _settings.LNBVoltage[0]);
                UDPSetVoltage(1, _settings.LNBVoltage[1]);

                for (int c = 0; c < ts_devices; c++)
                {
                    UDPSetFrequency(c, _current_frequency[c], _current_sr[c]);
                }

                Log.Information("PicoTuner (WH): re-sent tuning settings");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to re-send WinterHill tuning settings");
            }
        }

        public void FlushTS0()
        {
            udp_buffer[0].Clear();
        }
        public void FlushTS1()
        {
            udp_buffer[1].Clear();
        }
        public void FlushTS2()
        {
            udp_buffer[2].Clear();
        }
        public void FlushTS3()
        {
            udp_buffer[3].Clear();
        }

        byte ReadTSGeneric(int id, ref byte[] data, ref uint dataRead)
        {
            dataRead = 0;

            if (data == null || data.Length == 0)
                return 1;

            int read = Math.Min(udp_buffer[id].Count, data.Length);
            if (read <= 0)
                return 0;

            byte[] chunk = udp_buffer[id].DequeueBytes(read);
            Array.Copy(chunk, data, chunk.Length);
            dataRead = (uint)chunk.Length;

            return 0;
        }

        byte ReadTS0(ref byte[] data, ref uint dataRead)
        {
            return ReadTSGeneric(0, ref data, ref dataRead);
        }

        byte ReadTS1(ref byte[] data, ref uint dataRead)
        {
            return ReadTSGeneric(1, ref data, ref dataRead);
        }

        byte ReadTS2(ref byte[] data, ref uint dataRead)
        {
            return ReadTSGeneric(2, ref data, ref dataRead);
        }

        byte ReadTS3(ref byte[] data, ref uint dataRead)
        {
            return ReadTSGeneric(3, ref data, ref dataRead);
        }

        private void WinterHillSource_DataReceived(object sender, byte[] e)
        {
            int device = ((UDPClient)sender).getID();

            if (!playing[device])
                return;

            // enqueue the whole datagram in one locked copy (much faster than per-byte)
            if (e != null && e.Length > 0)
                udp_buffer[device].Enqueue(e);
            ts_threads[device].NewDataPresent();
        }

        private void WinterHillSource_ConnectionStatusChanged(object sender, bool connection_status)
        {
            Log.Information("Connection Status " + ((UDPClient)sender).getID() + " : " + (connection_status ? "Connected" : "Disconnected"));
        }

        public override void Close()
        {
            Log.Information("Closing WinterHill Source");

            _shutdownWatchdog = true;
            try { reconnect_thread_t?.Join(1500); } catch { }

            int defaultInterface = _settings.DefaultInterface;
            _settingsManager.SaveSettings(_settings);

            switch (defaultInterface)
            {
                case 1: // websockets
                    DisconnectWebsockets();
                    break;
                case 2: // udp pico wh
                    DisconnectWinterHillUDP();
                    break;
            }
            if (ts_threads != null)
            {
                for (int c = 0; c < ts_threads.Length; c++)
                {
                    if (ts_threads[c] != null)
                    {
                        bool stopped = false;
                        ts_threads[c].Stop(ref stopped);
                    }
                }
            }

            if (udp_clients != null)
            {
                for (int c = 0; c < udp_clients.Length; c++)
                {
                    udp_clients[c]?.Disconnect();
                }

            }
            //UDPClient[] udp_clients;
        }

        public override void ConfigureMediaPath(string MediaPath)
        {
            _MediaPath = MediaPath;
        }

        public override void ConfigureTSRecorders(List<TSRecorder> TSRecorders)
        {
            for (int c = 0; c  < TSRecorders.Count; c++)
            {
                _recorders[c] = TSRecorders[c];
            }
        }

        public override void ConfigureTSStreamers(List<TSUdpStreamer> TSStreamers)
        {
            for (int c = 0; c < TSStreamers.Count; c++)
            {
                _streamer[c] = TSStreamers[c];
            }
        }

        public override void ConfigureVideoPlayers(List<OTMediaPlayer> MediaPlayers)
        {
            for (int c = 0; c < ts_devices; c++)
            {
                _media_player[c] = MediaPlayers[c];
                _media_player[c].onVideoOut += WinterHillSource_onVideoOut;
                if (_settings.DefaultMuted[c])
                {
                    _media_player[c].SetVolume(0);
                }
                else
                {
                    _media_player[c].SetVolume((int)_settings.DefaultVolume[c]);
                }
            }

            _videoPlayersReady = true;
        }

        private void WinterHillSource_onVideoOut(object sender, MediaStatus e)
        {
            int video_id = ((OTMediaPlayer)sender).getID();

            preMute[video_id] = (int)_settings.DefaultVolume[video_id];
            muted[video_id] = _settings.DefaultMuted[video_id];
            if (muted[video_id] == true)
            {
                _media_player[video_id].SetVolume(0);
            }
            else
            {
                _media_player[video_id].SetVolume(preMute[video_id]);
            }

            UpdateMediaProperties(video_id, e);
        }

        public override string GetDescription()
        {
            return "WinterHill Client, Compatible with:" +
            Environment.NewLine + Environment.NewLine +
            "ZR6TG - WH Variant (websocket)" + Environment.NewLine +
            "G4EWJ - PicoTuner WH (Ethernet)" + Environment.NewLine;

        }

        public override string GetDeviceName()
        {
            switch (hw_device)
            {
                case 1: return "WinterHill (ZR6TG Variant)";
                case 2: return "PicoTuner (G4EWJ Ethernet WH)";
            }

            return "Unknown";
        }

        public override long GetFrequency(int device, bool offset_included)
        {
            return _current_frequency[device] + (offset_included ? _current_offset[device] : 0);
        }

        public override string GetName()
        {
            return "WinterHill Variant";
        }

        public override void OverrideDefaultMuted(bool Override)
        {
            if (Override)
            {
//                for (int i = 0; i < _settings.DefaultMuted.Count(); i++)
                for (int i = 0; i < hw_device; i++)
                {
                    preMute[i] = (int)_settings.DefaultVolume[i];                           // save DefaultVolume in preMute
                    _tuner_properties[i].UpdateValue("volume_slider_" + i.ToString(), "0"); // side effect: will set DefaultVolume to 0
                    _tuner_properties[i].UpdateMuteButtonColor("media_controls_" + i.ToString(), Color.PaleVioletRed);
                    muted[i] = _settings.DefaultMuted[i] = true;
                    _settings.DefaultVolume[i] = (uint)preMute[i];                          // restore DefaultVolume
                }
            }
        }

        public override CircularBuffer GetVideoDataQueue(int device)
        {
            return ts_data_queue[device];
        }

        public override int GetVideoSourceCount()
        {
            return ts_devices;
        }

        public override void RegisterTSConsumer(int device, CircularBuffer ts_buffer_queue)
        {
            ts_threads[device].RegisterTSConsumer(ts_buffer_queue);
        }

        public void SetRFPort(int device, int port)
        {
            Console.WriteLine("Set Device: " + device.ToString() + "," + port.ToString());
            _settings.RFPort[device] = (uint)port;

            SetFrequency(device, (uint)_current_frequency[device], (uint)_current_sr[device], false);
        }


        public override void SetFrequency(int device, uint frequency, uint symbol_rate, bool offset_included)
        {
            Log.Information("SetFrequency: " + device.ToString() + "," + frequency.ToString() + "," + symbol_rate.ToString() + "," + offset_included.ToString());

            if (device >= 0 && device < _current_frequency.Length)
            {
                _current_frequency[device] = (int)frequency;
                _current_sr[device] = (int)symbol_rate;
            }

            if (offset_included)
            {
                switch (hw_device)
                {
                    case 1: WSSetFrequency(device, (int)frequency, (int)symbol_rate);
                        break;
                    case 2: UDPSetFrequency(device, (int)frequency, (int)symbol_rate);
                        break;
                }

            }
            else
            {
                switch (hw_device)
                {
                    case 1:  WSSetFrequency(device, (int)frequency + (int)_current_offset[device], (int)symbol_rate);
                        break;
                    case 2: UDPSetFrequency(device, (int)frequency + (int)_current_offset[device], (int)symbol_rate);
                        break;
                }
            }

        }

        public override object GetSettingsObject() => _settings;

        public override void PersistSettings()
        {
            try { _settingsManager.SaveSettings(_settings); } catch { }
        }

        public override void ShowSettings()
        {
            WinterHillSettingsForm settingsForm = new WinterHillSettingsForm(_settings);

            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                _settingsManager.SaveSettings(_settings);
            }

        }

        public override void StartStreaming(int device)
        {
            if (ts_threads != null)
            {
                if (ts_threads[device] != null)
                {
                    ts_threads[device].start_ts();
                }
            }
        }

        public override void StopStreaming(int device)
        {
            if (ts_threads != null)
            {
                if (ts_threads[device] != null)
                {
                    ts_threads[device].stop_ts();
                }
            }
        }

        public override string GetMoreInfoLink()
        {
            return "https://www.zr6tg.co.za/opentuner-winterhill-source/";
        }
    }
}
