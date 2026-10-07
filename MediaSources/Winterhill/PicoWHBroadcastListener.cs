using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Serilog;

namespace opentuner.MediaSources.WinterHill
{
    public class PicoWHBroadcastListener
    {
        private Thread listener_thread;
        private volatile bool CloseThread = false;
        private UdpClient listener;

        private readonly int port = 9997;

        public delegate void OnBroadcastDelegate(string data);

        public event OnBroadcastDelegate OnBroadcast;

        public bool IsRunning => listener_thread != null && listener_thread.IsAlive;

        public PicoWHBroadcastListener()
        {
            // Bind in the constructor so that a second instance (or another owner of
            // the port) throws immediately and can be reported to the user.
            listener = new UdpClient(port);

            // Allow the receive call to time out periodically so the thread can notice
            // CloseThread instead of blocking forever.
            listener.Client.ReceiveTimeout = 1000;

            listener_thread = new Thread(ListenerThread)
            {
                IsBackground = true,
                Name = "PicoWHBroadcastListener"
            };
            listener_thread.Start();
        }

        public void Close()
        {
            if (CloseThread)
                return;

            CloseThread = true;

            // Closing the socket unblocks a pending Receive call.
            try { listener?.Close(); } catch { }

            // Give the worker a moment to exit cleanly. Never abort the thread.
            try { listener_thread?.Join(2000); } catch { }

            listener_thread = null;
        }

        public void ListenerThread()
        {
            while (!CloseThread)
            {
                try
                {
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, port);
                    byte[] data = listener.Receive(ref remoteEndPoint);

                    string receivedMessage = Encoding.ASCII.GetString(data);

                    try
                    {
                        OnBroadcast?.Invoke(receivedMessage);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error handling PicoTuner (WH) broadcast");
                    }
                }
                catch (SocketException sex) when (sex.SocketErrorCode == SocketError.TimedOut)
                {
                    // No broadcast within the timeout window - loop again so we can
                    // observe CloseThread.
                }
                catch (Exception)
                {
                    // Socket was closed (shutdown) or some transient error - stop if
                    // we are shutting down, otherwise back off briefly.
                    if (CloseThread || listener == null)
                        break;

                    Thread.Sleep(100);
                }
            }

            Log.Information("Broadcast Listener Thread Closed");
        }
    }
}
