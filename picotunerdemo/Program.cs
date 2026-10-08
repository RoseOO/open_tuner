using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PicoTunerDemo
{
    /// <summary>
    /// Emulates a G4EWJ PicoTuner (WinterHill) Ethernet device and streams a test
    /// MPEG-TS (made from the supplied MP4) to OpenTuner's WinterHill source.
    ///
    /// Protocol (base IP port 9900 by default):
    ///   * device listens for commands on base+20 (form/broadcast), base+21 (RX1),
    ///     base+22 (RX2)
    ///   * device broadcasts its address to 255.255.255.255:9997
    ///   * device sends status "$" lines to &lt;client&gt;:base+1
    ///   * device sends the transport stream to &lt;client&gt;:base+41+RX
    /// </summary>
    internal static class Program
    {
        private static Options _opt;

        private static readonly object _lock = new object();

        private static UdpClient _sendSocket;
        private static readonly IPEndPoint _broadcast = new IPEndPoint(IPAddress.Broadcast, 9997);

        private static string _clientIp;          // learned from incoming commands
        private static volatile bool _running = true;

        // per receiver (index 1..2)
        private static readonly bool[] _active = new bool[3];
        private static readonly int[] _freq = new int[3];
        private static readonly int[] _sr = new int[3];
        private static readonly int[] _offset = new int[3];
        private static readonly string[] _plugs = new string[3];

        private static byte[] _ts;
        private static string _tsPath;
        private static int _bitrateKbps;
        private static double _streamSeconds = 10;

        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint ms);

        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint ms);

        private static int Main(string[] args)
        {
            Console.Title = "PicoTuner (WH) Demo Emulator";
            _opt = Options.Parse(args);

            if (_opt.ShowHelp)
            {
                Options.PrintHelp();
                return 0;
            }

            // 1ms scheduler resolution so the TS pacing sleeps are accurate
            try { timeBeginPeriod(1); } catch { }

            try
            {
                Console.WriteLine("PicoTuner (WH) demo emulator");
                Console.WriteLine("============================");

                PrepareTransportStream();

                _sendSocket = new UdpClient { EnableBroadcast = true };
                _sendSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                StartListener(_opt.BasePort + 20);   // form / broadcast / general commands
                StartListener(_opt.BasePort + 21);   // RX1 tuning
                StartListener(_opt.BasePort + 22);   // RX2 tuning (base+PORTLISTENBASE+RX)

                StartBroadcast();
                StartStatus();
                StartReceiver(1);
                StartReceiver(2);

                string ip = _opt.AdvertiseIp ?? DetectIp();
                Console.WriteLine();
                Console.WriteLine("Advertised IP   : " + ip);
                Console.WriteLine("Base IP port    : " + _opt.BasePort);
                Console.WriteLine("Command ports   : " + (_opt.BasePort + 21) + " (RX1), " + (_opt.BasePort + 22) + " (RX2)");
                Console.WriteLine("Status port     : " + (_opt.BasePort + 1) + "  (device -> OpenTuner)");
                Console.WriteLine("TS ports        : " + (_opt.BasePort + 41) + " (RX1), " + (_opt.BasePort + 42) + " (RX2)");
                Console.WriteLine("Transport stream: " + _tsPath + "  (" + _ts.Length + " bytes, " + _bitrateKbps + " kbit/s)");
                Console.WriteLine();
                Console.WriteLine("In OpenTuner: choose the WinterHill source, interface");
                Console.WriteLine("'PicoTuner (G4EWJ Ethernet WH)', and Connect. Tuning the");
                Console.WriteLine("source will start playback automatically.");
                Console.WriteLine();
                Console.WriteLine("Press Ctrl+C to stop.");

                while (_running)
                    Thread.Sleep(250);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal: " + ex.Message);
                try { timeEndPeriod(1); } catch { }
                return 1;
            }

            try { timeEndPeriod(1); } catch { }
            return 0;
        }

        // ------------------------------------------------------------------
        // transport stream preparation
        // ------------------------------------------------------------------
        private static void PrepareTransportStream()
        {
            if (!string.IsNullOrEmpty(_opt.TsFile))
            {
                _tsPath = _opt.TsFile;
                _streamSeconds = _opt.DurationSeconds > 0 ? _opt.DurationSeconds : 10.0;
            }
            else
            {
                if (string.IsNullOrEmpty(_opt.Mp4File) || !File.Exists(_opt.Mp4File))
                    throw new FileNotFoundException("MP4 not found: " + _opt.Mp4File + " (use --mp4 <path>)");

                _streamSeconds = _opt.Minutes > 0 ? _opt.Minutes * 60.0 : Math.Max(1, _opt.DurationSeconds);

                // cache key includes the length so changing --minutes re-encodes
                _tsPath = Path.Combine(Path.GetTempPath(),
                    "picotunerdemo_" + Path.GetFileNameWithoutExtension(_opt.Mp4File) +
                    "_" + (int)Math.Round(_streamSeconds) + "s_cbr.ts");

                bool needConvert = !File.Exists(_tsPath) ||
                                   File.GetLastWriteTimeUtc(_tsPath) < File.GetLastWriteTimeUtc(_opt.Mp4File);

                if (needConvert)
                    ConvertToTs(_opt.Mp4File, _tsPath, _streamSeconds);
                else
                    Console.WriteLine("Using cached TS: " + _tsPath);
            }

            if (!File.Exists(_tsPath))
                throw new FileNotFoundException("Transport stream not found: " + _tsPath);

            _ts = File.ReadAllBytes(_tsPath);

            _bitrateKbps = _opt.BitrateKbps > 0
                ? _opt.BitrateKbps
                : (int)Math.Round(_ts.Length * 8.0 / Math.Max(1, _streamSeconds) / 1000.0);

            if (_ts.Length == 0 || _ts[0] != 0x47)
                Console.WriteLine("WARNING: TS does not start with a sync byte (0x47) - playback may fail.");
        }

        private static void ConvertToTs(string mp4, string ts, double seconds)
        {
            string ffmpeg = ResolveFfmpeg();
            if (ffmpeg == null)
                throw new FileNotFoundException(
                    "ffmpeg.exe not found. Put ffmpeg.exe on PATH, beside this program, or pass --ffmpeg <path>.");

            bool hasAudio = HasAudioStream(ffmpeg, mp4);
            bool addTone = !hasAudio && !_opt.NoAudio;
            bool encodeAudio = hasAudio || addTone;

            const int VideoKbps = 1400;
            const int AudioKbps = 96;
            const int MuxrateKbps = 1700;

            Console.WriteLine("Encoding " + Path.GetFileName(mp4) + " -> " + ts +
                              " (" + (int)Math.Round(seconds) + "s, CBR " + MuxrateKbps + "k/s" +
                              (hasAudio ? ", source audio" : addTone ? ", + test tone" : ", no audio") +
                              ") with ffmpeg (first run only, may take a minute)...");

            // Re-encode to a CONSTANT-rate (muxrate) MPEG-TS with continuous
            // timestamps. A CBR stream lets the demo pace perfectly and stops
            // VLC's input from underrunning/stuttering. -stream_loop loops the
            // clip so the emulated signal runs continuously.
            var args = new StringBuilder();
            args.Append("-y -v warning -stream_loop -1 -i \"").Append(mp4).Append("\" ");
            if (addTone)
                args.Append("-f lavfi -i sine=frequency=440:sample_rate=48000 ");
            args.Append("-t ").Append(seconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(" ");
            args.Append("-map 0:v:0 ");
            if (hasAudio) args.Append("-map 0:a:0 ");
            else if (addTone) args.Append("-map 1:a:0 ");

            args.Append("-c:v libx264 -preset veryfast -pix_fmt yuv420p -profile:v main ");
            args.Append("-b:v ").Append(VideoKbps).Append("k -maxrate ").Append(VideoKbps).Append("k -bufsize ").Append(VideoKbps * 2).Append("k ");

            if (encodeAudio)
            {
                args.Append("-c:a aac -b:a ").Append(AudioKbps).Append("k -ar 48000 -ac 2 ");
                if (addTone) args.Append("-af \"volume=0.2\" ");
            }

            args.Append("-muxrate ").Append(MuxrateKbps).Append("k ");
            args.Append("-mpegts_flags +resend_headers -f mpegts \"").Append(ts).Append("\"");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0 || !File.Exists(ts))
                {
                    string tail = err.Length > 1500 ? err.Substring(err.Length - 1500) : err;
                    throw new Exception("ffmpeg failed (exit " + p.ExitCode + "):\n" + tail);
                }
            }

            Console.WriteLine("Encoding complete.");
        }

        private static bool HasAudioStream(string ffmpeg, string input)
        {
            try
            {
                string dir = Path.GetDirectoryName(ffmpeg);
                string ffprobe = Path.Combine(dir ?? "", "ffprobe.exe");
                if (!File.Exists(ffprobe))
                    return true;   // can't tell - assume the source has audio

                var psi = new ProcessStartInfo
                {
                    FileName = ffprobe,
                    Arguments = "-v error -select_streams a -show_entries stream=index -of csv=p=0 \"" + input + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    return outp.Length > 0;
                }
            }
            catch { return true; }
        }

        private static string ResolveFfmpeg()
        {
            if (!string.IsNullOrEmpty(_opt.FfmpegPath) && File.Exists(_opt.FfmpegPath))
                return _opt.FfmpegPath;

            // beside the program
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(local)) return local;
            local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(local)) return local;

            // on PATH
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            // a couple of common locations on this machine
            string[] guesses =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "open_tuner_0.B_20240714", "ffmpeg", "ffmpeg.exe"),
                @"C:\Program Files\BlueStacks_nxt\ffmpeg.exe"
            };
            foreach (string g in guesses)
                if (File.Exists(g)) return g;

            return null;
        }

        // ------------------------------------------------------------------
        // UDP command listeners
        // ------------------------------------------------------------------
        private static void StartListener(int port)
        {
            var client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            var thread = new Thread(() => ListenLoop(client, port)) { IsBackground = true, Name = "cmd" + port };
            thread.Start();
        }

        private static void ListenLoop(UdpClient client, int port)
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = client.Receive(ref remote);
                    string text = Encoding.ASCII.GetString(data).Trim();

                    if (_clientIp != remote.Address.ToString())
                    {
                        _clientIp = remote.Address.ToString();
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] client " + _clientIp + " : " + text);
                    }

                    HandleCommand(port, text);
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex)
                {
                    if (_running) Console.WriteLine("cmd " + port + " error: " + ex.Message);
                }
            }
        }

        private static void HandleCommand(int port, string text)
        {
            // strip the "[to@wh] " prefix and split the comma-separated key=value parts
            string body = text;
            int bracket = body.IndexOf(']');
            if (body.StartsWith("[to@wh]")) body = body.Substring(bracket + 1).Trim();

            var parts = body.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int rcv = port - _opt.BasePort - 20;   // 1 or 2 when from the tuning ports
            int freq = 0, sr = 0, offset = 0;
            string plug = "A";

            foreach (string part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim().ToLowerInvariant();
                string val = part.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "rcv": int.TryParse(val, out rcv); break;
                    case "freq": int.TryParse(val, out freq); break;
                    case "srate": int.TryParse(val, out sr); break;
                    case "offset": int.TryParse(val, out offset); break;
                    case "fplug": plug = val.ToUpperInvariant(); break;
                    case "vgx": Log("LNB-A " + val); break;
                    case "vgy": Log("LNB-B " + val); break;
                    case "callsign": _opt.Callsign = val; Log("callsign " + val); break;
                    case "bip": Log("base port change requested: " + val + " (ignored in demo)"); break;
                }
            }

            if (rcv < 1 || rcv > 2) rcv = 1;

            lock (_lock)
            {
                _active[rcv] = true;
                if (freq > 0) _freq[rcv] = freq;
                if (sr > 0) _sr[rcv] = sr;
                _offset[rcv] = offset;
                _plugs[rcv] = plug;
            }

            Log("tune RX" + rcv + " freq=" + freq + " srate=" + sr + " offset=" + offset + " fplug=" + plug);
        }

        // ------------------------------------------------------------------
        // broadcast + status + TS
        // ------------------------------------------------------------------
        private static void StartBroadcast()
        {
            string ip = _opt.AdvertiseIp ?? DetectIp();
            var thread = new Thread(() =>
            {
                int seq = 0;
                while (_running)
                {
                    try
                    {
                        var sb = new StringBuilder();
                        sb.Append("        PicoTuner Broadcast\r\n");
                        sb.Append("        Sequence   ").Append(++seq).Append("\r\n");
                        sb.Append("  Broadcast port   ").Append(9997).Append("\r\n");
                        sb.Append("    Base IP port   ").Append(_opt.BasePort).Append("\r\n");
                        sb.Append("      IP address   ").Append(ip).Append("\r\n");
                        byte[] buf = Encoding.ASCII.GetBytes(sb.ToString());
                        _sendSocket.Send(buf, buf.Length, _broadcast);
                    }
                    catch { }
                    Thread.Sleep(1000);
                }
            }) { IsBackground = true, Name = "broadcast" };
            thread.Start();
        }

        private static void StartStatus()
        {
            var thread = new Thread(() =>
            {
                while (_running)
                {
                    string ip = _clientIp;
                    if (!string.IsNullOrEmpty(ip))
                    {
                        for (int rx = 1; rx <= 2; rx++)
                        {
                            if (!_active[rx]) continue;
                            try
                            {
                                string msg = BuildStatus(rx);
                                byte[] buf = Encoding.ASCII.GetBytes(msg);
                                _sendSocket.Send(buf, buf.Length, new IPEndPoint(IPAddress.Parse(ip), _opt.BasePort + 1));
                            }
                            catch { }
                        }
                    }
                    Thread.Sleep(500);
                }
            }) { IsBackground = true, Name = "status" };
            thread.Start();
        }

        private static string BuildStatus(int rx)
        {
            string ip = _clientIp ?? "0.0.0.0";
            int tsPort = _opt.BasePort + 40 + rx;   // 9941 / 9942
            var sb = new StringBuilder();
            sb.Append("$0,").Append(rx).Append("\r\n");          // receiver id (base%100 == 0)
            sb.Append("$1,DVB-S2\r\n");                          // locked
            sb.Append("$6,").Append((_freq[rx] / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)).Append("\r\n");   // frequency (MHz)
            sb.Append("$9,").Append(_sr[rx]).Append("\r\n");     // symbol rate
            sb.Append("$12,13.5\r\n");                           // MER
            sb.Append("$11,0.00015\r\n");                        // BER
            sb.Append("$13,").Append(_opt.Callsign).Append("\r\n");  // service name
            sb.Append("$14,BATC DEMO\r\n");                      // service provider
            sb.Append("$15,0\r\n");                              // null %
            sb.Append("$18,QPSK 3/4\r\n");                       // modcod
            sb.Append("$30,3.0\r\n");                            // D number / margin
            sb.Append("$33,").Append(_plugs[rx] == "B" ? "BOT" : "TOP").Append("\r\n");
            sb.Append("$92,").Append(ip).Append(":").Append(tsPort).Append("\r\n");
            return sb.ToString();
        }

        private static void StartReceiver(int rx)
        {
            var thread = new Thread(() => ReceiverLoop(rx)) { IsBackground = true, Name = "ts" + rx };
            thread.Start();
        }

        private static void ReceiverLoop(int rx)
        {
            const int datagram = 7 * 188;   // PicoTuner sends 7 TS packets per UDP block

            while (_running)
            {
                string ip = _clientIp;
                if (string.IsNullOrEmpty(ip) || !_active[rx])
                {
                    Thread.Sleep(200);
                    continue;
                }

                var target = new IPEndPoint(IPAddress.Parse(ip), _opt.BasePort + 40 + rx);

                // pace 1% above the CBR muxrate so the receiver's buffer never
                // underruns (the small surplus sits in OpenTuner's 1 MB buffer)
                double bytesPerSecond = Math.Max(50000, _bitrateKbps * 1000.0 / 8.0 * 1.01);

                // send a 1s start cushion so VLC has something to buffer before
                // we drop into strict real-time pacing
                int offset = 0;
                long cushion = 0;
                long cushionTarget = (long)(bytesPerSecond * 1.0);
                while (_running && _active[rx] && ip == _clientIp && cushion < cushionTarget)
                {
                    int primed = SendChunk(rx, target, ref offset, datagram);
                    if (primed == 0) break;
                    cushion += primed;
                }

                var sw = Stopwatch.StartNew();
                long bytesSent = 0;

                while (_running && _active[rx] && ip == _clientIp)
                {
                    int count = SendChunk(rx, target, ref offset, datagram);
                    if (count == 0) continue;
                    bytesSent += count;

                    // accurate average-rate pacing (winmm timeBeginPeriod(1) gives ~1ms sleeps)
                    double targetMs = bytesSent * 1000.0 / bytesPerSecond;
                    double behind = targetMs - sw.Elapsed.TotalMilliseconds;
                    if (behind > 1.0)
                        Thread.Sleep((int)Math.Min(30, behind));
                }
            }
        }

        private static int SendChunk(int rx, IPEndPoint target, ref int offset, int maxBytes)
        {
            try
            {
                if (_ts == null || _ts.Length == 0)
                    return 0;

                if (offset >= _ts.Length)
                    offset = 0;

                int count = Math.Min(maxBytes, _ts.Length - offset);
                count -= count % 188;                 // whole TS packets only
                if (count <= 0) { offset = 0; return 0; }

                _sendSocket.Client.SendTo(_ts, offset, count, SocketFlags.None, target);

                offset += count;
                if (offset >= _ts.Length)
                    offset = 0;                       // loop

                return count;
            }
            catch (Exception ex)
            {
                if (_running) Log("TS" + rx + " error: " + ex.Message);
                Thread.Sleep(200);
                return 0;
            }
        }

        // ------------------------------------------------------------------
        private static void Log(string message)
        {
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
        }

        private static string DetectIp()
        {
            try
            {
                var candidates = new List<string>();
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        candidates.Add(ua.Address.ToString());
                    }
                }

                string preferred = candidates.FirstOrDefault(c => !c.StartsWith("169.254."));
                return preferred ?? candidates.FirstOrDefault() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        // ------------------------------------------------------------------
        private sealed class Options
        {
            public string Mp4File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                                 "Downloads", "720p.mp4");
            public string TsFile;
            public string FfmpegPath;
            public string AdvertiseIp;
            public int BasePort = 9900;
            public string Callsign = "M1RXO";
            public int BitrateKbps = 0;         // 0 = auto from size/duration
            public double DurationSeconds = 10;
            public double Minutes = 2;          // length of the remuxed looped TS
            public bool NoAudio = false;
            public bool ShowHelp;

            public static Options Parse(string[] args)
            {
                var o = new Options();
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    Func<string> next = () => i + 1 < args.Length ? args[++i] : null;

                    switch (a)
                    {
                        case "-h":
                        case "--help": o.ShowHelp = true; return o;
                        case "--mp4": o.Mp4File = next(); break;
                        case "--ts": o.TsFile = next(); break;
                        case "--ffmpeg": o.FfmpegPath = next(); break;
                        case "--ip": o.AdvertiseIp = next(); break;
                        case "--base": int.TryParse(next(), out o.BasePort); break;
                        case "--callsign": o.Callsign = next(); break;
                        case "--bitrate": int.TryParse(next(), out o.BitrateKbps); break;
                        case "--duration": double.TryParse(next(), NumberStyles.Float, CultureInfo.InvariantCulture, out o.DurationSeconds); break;
                        case "--minutes": double.TryParse(next(), NumberStyles.Float, CultureInfo.InvariantCulture, out o.Minutes); break;
                        case "--no-audio": o.NoAudio = true; break;
                        default:
                            if (!a.StartsWith("-") && File.Exists(a)) o.Mp4File = a;
                            break;
                    }
                }
                return o;
            }

            public static void PrintHelp()
            {
                Console.WriteLine(@"PicoTuner (WH) demo emulator

Usage: picotunerdemo [options]

  --mp4 <path>       Source MP4 to remux (default: %USERPROFILE%\Downloads\720p.mp4)
  --ts <path>        Use an existing .ts instead of remuxing
  --ffmpeg <path>    ffmpeg.exe location (otherwise found on PATH / beside the program)
  --ip <addr>        IP address to advertise (default: auto-detect)
  --base <port>      Base IP port (default 9900)
  --callsign <text>  Service name reported to OpenTuner (default M1RXO)
  --bitrate <kbps>   Output pacing bitrate (default: auto)
  --minutes <mins>   Length of the looped test stream (default 2)
  --no-audio         Do not add a test tone when the source has no audio
  --duration <secs>  Source duration used for auto bitrate (default 10)
  -h, --help         Show this help");
            }
        }
    }
}
