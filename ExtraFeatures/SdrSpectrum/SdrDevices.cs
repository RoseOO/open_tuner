using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace opentuner.ExtraFeatures.SdrSpectrum
{
    public delegate void SdrSamplesCallback(byte[] data, int count);

    public interface ISdrDevice : IDisposable
    {
        string Name { get; }

        bool IsOpen { get; }

        // IQ sample format delivered by this device.
        SdrSampleFormat SampleFormat { get; }

        string LastError { get; }

        bool Open();
        void Close();

        bool SetCenterFrequency(uint hz);
        bool SetSampleRate(uint hz);
        bool SetGain(int tenthsDb, bool agc);
        bool SetPpm(int ppm);

        void Start(SdrSamplesCallback callback);
        void Stop();
    }

    // ------------------------------------------------------------------
    // RTL-TCP (rtl_tcp) - works with a local or remote rtl_tcp server.
    // ------------------------------------------------------------------
    public class RtlTcpDevice : ISdrDevice
    {
        private const byte CMD_SET_FREQUENCY = 0x01;
        private const byte CMD_SET_SAMPLE_RATE = 0x02;
        private const byte CMD_SET_GAIN_MODE = 0x03;
        private const byte CMD_SET_GAIN = 0x04;
        private const byte CMD_SET_PPM = 0x05;

        private readonly string _host;
        private readonly int _port;

        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _readThread;
        private volatile bool _running;

        private SdrSamplesCallback _callback;

        public string Name => "RTL-TCP";
        public bool IsOpen { get; private set; }
        public SdrSampleFormat SampleFormat => SdrSampleFormat.Unsigned8;
        public string LastError { get; private set; } = "";

        public RtlTcpDevice(string host, int port)
        {
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            _port = port <= 0 ? 1234 : port;
        }

        public bool Open()
        {
            try
            {
                _client = new TcpClient();
                _client.NoDelay = true;

                // bounded connect so an unreachable host cannot freeze the UI
                var connectTask = _client.ConnectAsync(_host, _port);
                if (!connectTask.Wait(3000))
                {
                    throw new TimeoutException("connection to " + _host + ":" + _port + " timed out");
                }

                _stream = _client.GetStream();

                // rtl_tcp sends a 12 byte "RTL0" dongle info header before IQ data.
                try
                {
                    byte[] header = new byte[12];
                    _stream.ReadTimeout = 1500;
                    int read = 0;
                    while (read < 12)
                    {
                        int n = _stream.Read(header, read, 12 - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    _stream.ReadTimeout = Timeout.Infinite;
                }
                catch
                {
                    // some servers do not send the header - that is fine
                }

                IsOpen = true;
                LastError = "";
                Log.Information("SDR: connected to rtl_tcp at " + _host + ":" + _port);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning("SDR: rtl_tcp connect failed: " + ex.Message);
                Close();
                return false;
            }
        }

        public void Close()
        {
            _running = false;
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
            IsOpen = false;
        }

        private void SendCommand(byte cmd, uint param)
        {
            if (_stream == null)
                return;

            byte[] buffer = new byte[5];
            buffer[0] = cmd;
            BitConverter.GetBytes(param).CopyTo(buffer, 1);
            _stream.Write(buffer, 0, 5);
        }

        public bool SetCenterFrequency(uint hz) { SendCommand(CMD_SET_FREQUENCY, hz); return true; }
        public bool SetSampleRate(uint hz) { SendCommand(CMD_SET_SAMPLE_RATE, hz); return true; }
        public bool SetPpm(int ppm) { SendCommand(CMD_SET_PPM, (uint)ppm); return true; }

        public bool SetGain(int tenthsDb, bool agc)
        {
            SendCommand(CMD_SET_GAIN_MODE, agc ? 0u : 1u);
            if (!agc)
                SendCommand(CMD_SET_GAIN, (uint)Math.Max(0, tenthsDb));
            return true;
        }

        public void Start(SdrSamplesCallback callback)
        {
            _callback = callback;
            _running = true;

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "RtlTcpReader"
            };
            _readThread.Start();
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[65536];

            try
            {
                while (_running && _stream != null)
                {
                    int n = _stream.Read(buffer, 0, buffer.Length);
                    if (n <= 0)
                        break;

                    _callback?.Invoke(buffer, n);
                }
            }
            catch (Exception)
            {
                // socket closed / disconnected
            }
            finally
            {
                IsOpen = false;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _stream?.Close(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            Close();
        }
    }

    // ------------------------------------------------------------------
    // Local RTL-SDR via librtlsdr.
    // ------------------------------------------------------------------
    public class RtlSdrDevice : ISdrDevice
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ReadAsyncCallback(IntPtr buf, uint len, IntPtr ctx);

        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_open(out IntPtr dev, uint index);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_close(IntPtr dev);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_set_center_freq(IntPtr dev, uint freq);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_set_sample_rate(IntPtr dev, uint rate);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_set_tuner_gain_mode(IntPtr dev, int manual);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_set_tuner_gain(IntPtr dev, int gain);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_set_freq_correction(IntPtr dev, int ppm);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_reset_buffer(IntPtr dev);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_read_async(IntPtr dev, ReadAsyncCallback cb, IntPtr ctx, uint bufNum, uint bufLen);
        [DllImport("rtlsdr", CallingConvention = CallingConvention.Cdecl)]
        private static extern int rtlsdr_cancel_async(IntPtr dev);

        private IntPtr _dev = IntPtr.Zero;
        private readonly int _index;
        private SdrSamplesCallback _callback;
        private ReadAsyncCallback _nativeCallback;

        public string Name => "RTL-SDR (local)";
        public bool IsOpen { get; private set; }
        public SdrSampleFormat SampleFormat => SdrSampleFormat.Unsigned8;
        public string LastError { get; private set; } = "";

        public RtlSdrDevice(int index)
        {
            _index = index;
        }

        public bool Open()
        {
            try
            {
                int rc = rtlsdr_open(out _dev, (uint)_index);
                if (rc != 0 || _dev == IntPtr.Zero)
                {
                    LastError = "rtlsdr_open failed (" + rc + ")";
                    Log.Warning("SDR: " + LastError);
                    return false;
                }

                IsOpen = true;
                LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                LastError = "librtlsdr not available (" + ex.Message + ")";
                Log.Warning("SDR: " + LastError);
                return false;
            }
        }

        public void Close()
        {
            try { if (_dev != IntPtr.Zero) rtlsdr_close(_dev); } catch { }
            _dev = IntPtr.Zero;
            IsOpen = false;
        }

        public bool SetCenterFrequency(uint hz) => Invoke(() => rtlsdr_set_center_freq(_dev, hz));
        public bool SetSampleRate(uint hz) => Invoke(() => rtlsdr_set_sample_rate(_dev, hz));
        public bool SetPpm(int ppm) => Invoke(() => rtlsdr_set_freq_correction(_dev, ppm));

        public bool SetGain(int tenthsDb, bool agc)
        {
            if (!Invoke(() => rtlsdr_set_tuner_gain_mode(_dev, agc ? 0 : 1)))
                return false;
            if (!agc)
                Invoke(() => rtlsdr_set_tuner_gain(_dev, tenthsDb));
            return true;
        }

        private bool Invoke(Func<int> action)
        {
            try { return _dev != IntPtr.Zero && action() == 0; }
            catch { return false; }
        }

        public void Start(SdrSamplesCallback callback)
        {
            _callback = callback;
            _nativeCallback = NativeReadCallback;

            try
            {
                rtlsdr_reset_buffer(_dev);
                rtlsdr_read_async(_dev, _nativeCallback, IntPtr.Zero, 0, 0);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private void NativeReadCallback(IntPtr buf, uint len, IntPtr ctx)
        {
            if (len == 0 || _callback == null)
                return;

            byte[] data = new byte[len];
            Marshal.Copy(buf, data, 0, (int)len);
            _callback(data, (int)len);
        }

        public void Stop()
        {
            try { if (_dev != IntPtr.Zero) rtlsdr_cancel_async(_dev); } catch { }
        }

        public void Dispose()
        {
            Stop();
            Close();
        }
    }

    // ------------------------------------------------------------------
    // PlutoSDR / Pluto+ via libiio (USB or network).
    //
    // The URI selects the transport:
    //   "usb:"                 - USB (auto)
    //   "usb:1.1.5"            - specific USB device
    //   "ip:192.168.2.1"       - network / Ethernet
    //   "ip:pluto.local"       - mDNS hostname
    // ------------------------------------------------------------------
    public class PlutoSdrDevice : ISdrDevice
    {
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr iio_create_context_from_uri(string uri);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern void iio_context_destroy(IntPtr ctx);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr iio_context_find_device(IntPtr ctx, string name);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr iio_device_find_channel(IntPtr dev, string name, [MarshalAs(UnmanagedType.I1)] bool output);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern int iio_channel_attr_write(IntPtr ch, string attr, string val);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern int iio_channel_attr_write_longlong(IntPtr ch, string attr, long val);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern int iio_channel_enable(IntPtr ch);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr iio_device_create_buffer(IntPtr dev, uint sampleCount, [MarshalAs(UnmanagedType.I1)] bool cyclic);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern void iio_buffer_destroy(IntPtr buf);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern int iio_buffer_refill(IntPtr buf);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr iio_buffer_start(IntPtr buf);
        [DllImport("libiio", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr iio_buffer_end(IntPtr buf);

        private const int BufferSamples = 32768;

        private readonly string _uri;

        private IntPtr _ctx = IntPtr.Zero;
        private IntPtr _rxDev = IntPtr.Zero;
        private IntPtr _phyDev = IntPtr.Zero;
        private IntPtr _rxLo = IntPtr.Zero;     // ad9361-phy altvoltage0 (RX LO)
        private IntPtr _phyRx = IntPtr.Zero;    // ad9361-phy voltage0 (rate/gain)
        private IntPtr _buffer = IntPtr.Zero;

        private Thread _thread;
        private volatile bool _running;
        private SdrSamplesCallback _callback;

        public string Name => "PlutoSDR";
        public bool IsOpen { get; private set; }
        public SdrSampleFormat SampleFormat => SdrSampleFormat.Signed16;
        public string LastError { get; private set; } = "";

        public PlutoSdrDevice(string uri)
        {
            _uri = string.IsNullOrWhiteSpace(uri) ? "usb:" : uri.Trim();
        }

        public bool Open()
        {
            try
            {
                _ctx = iio_create_context_from_uri(_uri);
                if (_ctx == IntPtr.Zero)
                {
                    LastError = "libiio could not open context '" + _uri + "'";
                    Log.Warning("SDR: " + LastError);
                    return false;
                }

                _rxDev = iio_context_find_device(_ctx, "cf-ad9361-lpc");
                if (_rxDev == IntPtr.Zero)
                    _rxDev = iio_context_find_device(_ctx, "cf-ad9361A-lpc"); // Pluto+

                if (_rxDev == IntPtr.Zero)
                {
                    LastError = "AD9361 RX device not found on '" + _uri + "'";
                    Close();
                    return false;
                }

                _phyDev = iio_context_find_device(_ctx, "ad9361-phy");
                if (_phyDev != IntPtr.Zero)
                {
                    _rxLo = iio_device_find_channel(_phyDev, "altvoltage0", true);
                    _phyRx = iio_device_find_channel(_phyDev, "voltage0", false);
                }

                IntPtr chI = iio_device_find_channel(_rxDev, "voltage0", false);
                IntPtr chQ = iio_device_find_channel(_rxDev, "voltage1", false);

                if (chI != IntPtr.Zero) iio_channel_enable(chI);
                if (chQ != IntPtr.Zero) iio_channel_enable(chQ);

                IsOpen = true;
                LastError = "";
                Log.Information("SDR: PlutoSDR opened (" + _uri + ")");
                return true;
            }
            catch (Exception ex)
            {
                LastError = "libiio not available (" + ex.Message + ")";
                Log.Warning("SDR: " + LastError);
                Close();
                return false;
            }
        }

        public void Close()
        {
            _running = false;
            try { _thread?.Join(1000); } catch { }
            _thread = null;

            try { if (_buffer != IntPtr.Zero) iio_buffer_destroy(_buffer); } catch { }
            try { if (_ctx != IntPtr.Zero) iio_context_destroy(_ctx); } catch { }

            _buffer = IntPtr.Zero;
            _ctx = IntPtr.Zero;
            _rxDev = IntPtr.Zero;
            _phyDev = IntPtr.Zero;
            IsOpen = false;
        }

        public bool SetCenterFrequency(uint hz)
        {
            try { return _rxLo != IntPtr.Zero && iio_channel_attr_write_longlong(_rxLo, "frequency", hz) == 0; }
            catch { return false; }
        }

        public bool SetSampleRate(uint hz)
        {
            try
            {
                if (_phyRx == IntPtr.Zero)
                    return false;

                int rc = iio_channel_attr_write_longlong(_phyRx, "sampling_frequency", hz);
                iio_channel_attr_write_longlong(_phyRx, "rf_bandwidth", hz);
                return rc == 0;
            }
            catch { return false; }
        }

        public bool SetPpm(int ppm) => true;

        public bool SetGain(int tenthsDb, bool agc)
        {
            try
            {
                if (_phyRx == IntPtr.Zero)
                    return false;

                iio_channel_attr_write(_phyRx, "gain_control_mode", agc ? "slow_attack" : "manual");

                if (!agc)
                {
                    string db = (tenthsDb / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    iio_channel_attr_write(_phyRx, "hardwaregain", db);
                }

                return true;
            }
            catch { return false; }
        }

        public void Start(SdrSamplesCallback callback)
        {
            _callback = callback;

            try
            {
                _buffer = iio_device_create_buffer(_rxDev, BufferSamples, false);
                if (_buffer == IntPtr.Zero)
                {
                    LastError = "iio_device_create_buffer failed";
                    Log.Warning("SDR: " + LastError);
                    return;
                }

                _running = true;
                _thread = new Thread(ReadLoop)
                {
                    IsBackground = true,
                    Name = "PlutoReader"
                };
                _thread.Start();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning("SDR: Pluto start failed: " + ex.Message);
            }
        }

        private void ReadLoop()
        {
            byte[] copy = new byte[1];

            while (_running)
            {
                try
                {
                    if (iio_buffer_refill(_buffer) < 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    IntPtr start = iio_buffer_start(_buffer);
                    IntPtr end = iio_buffer_end(_buffer);
                    long length = (long)end - (long)start;

                    if (length <= 0)
                        continue;

                    if (copy.Length < length)
                        copy = new byte[(int)length];

                    Marshal.Copy(start, copy, 0, (int)length);
                    _callback?.Invoke(copy, (int)length);
                }
                catch (Exception)
                {
                    Thread.Sleep(10);
                }
            }
        }

        public void Stop()
        {
            _running = false;
        }

        public void Dispose()
        {
            Stop();
            Close();
        }
    }

    // ------------------------------------------------------------------
    // HackRF via libhackrf.
    // ------------------------------------------------------------------
    public class HackRfDevice : ISdrDevice
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct hackrf_transfer
        {
            public IntPtr device;
            public IntPtr buffer;
            public int buffer_length;
            public int valid_length;
            public IntPtr rx_ctx;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int HackRfCallback(IntPtr transfer);

        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_init();
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_open(out IntPtr dev);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_close(IntPtr dev);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_set_freq(IntPtr dev, ulong freq);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_set_sample_rate(IntPtr dev, double rate);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_set_amp_enable(IntPtr dev, byte enable);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_set_lna_gain(IntPtr dev, uint value);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_set_vga_gain(IntPtr dev, uint value);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_start_rx(IntPtr dev, HackRfCallback cb, IntPtr ctx);
        [DllImport("hackrf", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hackrf_stop_rx(IntPtr dev);

        private IntPtr _dev = IntPtr.Zero;
        private SdrSamplesCallback _callback;
        private HackRfCallback _nativeCallback;
        private bool _initialised;

        public string Name => "HackRF (local)";
        public bool IsOpen { get; private set; }
        public SdrSampleFormat SampleFormat => SdrSampleFormat.Signed8;
        public string LastError { get; private set; } = "";

        public bool Open()
        {
            try
            {
                if (!_initialised)
                {
                    hackrf_init();
                    _initialised = true;
                }

                int rc = hackrf_open(out _dev);
                if (rc != 0 || _dev == IntPtr.Zero)
                {
                    LastError = "hackrf_open failed (" + rc + ")";
                    Log.Warning("SDR: " + LastError);
                    return false;
                }

                IsOpen = true;
                LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                LastError = "libhackrf not available (" + ex.Message + ")";
                Log.Warning("SDR: " + LastError);
                return false;
            }
        }

        public void Close()
        {
            try { if (_dev != IntPtr.Zero) hackrf_close(_dev); } catch { }
            _dev = IntPtr.Zero;
            IsOpen = false;
        }

        public bool SetCenterFrequency(uint hz) => Invoke(() => hackrf_set_freq(_dev, hz));
        public bool SetSampleRate(uint hz) => Invoke(() => hackrf_set_sample_rate(_dev, hz));
        public bool SetPpm(int ppm) => true; // HackRF has no ppm correction

        public bool SetGain(int tenthsDb, bool agc)
        {
            // map to LNA (0..40 dB in 8 dB steps) / VGA (0..62 dB in 2 dB steps)
            int dB = Math.Max(0, tenthsDb / 10);
            uint lna = (uint)Math.Min(40, (dB / 8) * 8);
            uint vga = (uint)Math.Min(62, ((dB - lna) / 2) * 2);
            Invoke(() => hackrf_set_lna_gain(_dev, lna));
            Invoke(() => hackrf_set_vga_gain(_dev, vga));
            return true;
        }

        private bool Invoke(Func<int> action)
        {
            try { return _dev != IntPtr.Zero && action() == 0; }
            catch { return false; }
        }

        public void Start(SdrSamplesCallback callback)
        {
            _callback = callback;
            _nativeCallback = NativeCallback;

            try
            {
                hackrf_set_amp_enable(_dev, 0);
                int rc = hackrf_start_rx(_dev, _nativeCallback, IntPtr.Zero);
                if (rc != 0)
                    LastError = "hackrf_start_rx failed (" + rc + ")";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private int NativeCallback(IntPtr transferPtr)
        {
            try
            {
                hackrf_transfer transfer = (hackrf_transfer)Marshal.PtrToStructure(transferPtr, typeof(hackrf_transfer));
                if (transfer.valid_length > 0 && _callback != null)
                {
                    byte[] data = new byte[transfer.valid_length];
                    Marshal.Copy(transfer.buffer, data, 0, transfer.valid_length);
                    _callback(data, transfer.valid_length);
                }
            }
            catch { }

            return 0;
        }

        public void Stop()
        {
            try { if (_dev != IntPtr.Zero) hackrf_stop_rx(_dev); } catch { }
        }

        public void Dispose()
        {
            Stop();
            Close();
        }
    }
}
