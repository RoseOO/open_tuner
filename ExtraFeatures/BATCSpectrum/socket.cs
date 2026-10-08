// From https://github.com/m0dts/QO-100-WB-Live-Tune - Rob Swinbank

using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;

namespace opentuner
{
    public class socket
    {
        public Action<ushort[]> callback;

        private WebSocket ws;       //websocket client

        private ushort[] fft_data;

        public bool connected;

        public DateTime lastdata;

        public event EventHandler<bool> ConnectionStatusChanged;

        private volatile bool _stopping = false;
        private int _attempt = 0;
        private System.Timers.Timer _reconnectTimer;
        private readonly object _lock = new object();

        public socket()
        {
            connected = false;
        }

        public void start()
        {
            _stopping = false;
            Connect();
        }

        private void Connect()
        {
            if (_stopping)
                return;

            if (connected)
                return;

            try
            {
                Log.Information("Websocket: QO_Spectrum: Try connect..");

                lock (_lock)
                {
                    try { ws?.Close(); } catch { }

                    ws = new WebSocket("wss://eshail.batc.org.uk/wb/fft", "fft_m0dtslivetune");

                    ws.OnMessage += (ss, ee) => NewData(ee.RawData);
                    ws.OnOpen += Ws_OnOpen;
                    ws.OnClose += Ws_OnClose;
                    ws.OnError += Ws_OnError;

                    ws.ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Websocket: QO_Spectrum: connect failed: " + ex.Message);
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (_stopping)
                return;

            lock (_lock)
            {
                // don't stack reconnect timers
                if (_reconnectTimer != null && _reconnectTimer.Enabled)
                    return;

                int delay = Math.Min(30, (int)Math.Pow(2, Math.Min(_attempt, 5)));
                _attempt++;

                Log.Information("Websocket: QO_Spectrum: reconnecting in " + delay + "s");

                _reconnectTimer = new System.Timers.Timer(delay * 1000) { AutoReset = false };
                _reconnectTimer.Elapsed += (s, e) =>
                {
                    try { _reconnectTimer?.Dispose(); } catch { }
                    _reconnectTimer = null;
                    if (!_stopping) Connect();
                };
                _reconnectTimer.Start();
            }
        }

        private void Ws_OnClose(object sender, CloseEventArgs e)
        {
            connected = false;
            Log.Information("Websocket: QO_Spectrum: Connection Closed");
            ConnectionStatusChanged?.Invoke(this, connected);
            ScheduleReconnect();
        }

        private void Ws_OnOpen(object sender, EventArgs e)
        {
            connected = true;
            _attempt = 0;
            Log.Information("Websocket: QO_Spectrum: Connected.");
            ConnectionStatusChanged?.Invoke(this, connected);
            lastdata = DateTime.Now;
        }

        private void Ws_OnError(object sender, ErrorEventArgs e)
        {
            Log.Information("Websocket: QO_Spectrum: Error " + e.Message);
            // OnError is usually followed by OnClose, but schedule here too in
            // case it isn't, so we always re-establish.
            ScheduleReconnect();
        }

        public void stop()
        {
            _stopping = true;

            lock (_lock)
            {
                try { _reconnectTimer?.Stop(); } catch { }
                try { _reconnectTimer?.Dispose(); } catch { }
                _reconnectTimer = null;

                try { ws?.Close(); } catch { }
                connected = false;
            }
        }

        private void NewData(byte[] data)
        {
            lastdata = DateTime.Now;

            fft_data = new UInt16[data.Length / 2];

            //unpack bytes to unsigned short int values
            int n = 0;
            byte[] buf = new byte[2];

            for (int i = 0; i < data.Length; i += 2)
            {
                buf[0] = data[i];
                buf[1] = data[i + 1];
                fft_data[n] = BitConverter.ToUInt16(buf, 0);
                n++;
            }
            callback?.Invoke(fft_data);
        }
    }
}
