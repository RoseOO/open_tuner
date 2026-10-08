using opentuner.MediaSources;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace opentuner
{
    // Records the incoming (raw) transport stream for a single tuner to a .ts file.
    // The recorder taps the source's raw TS consumer queue, so what is written to
    // disk is the unmodified 188 byte packet stream as received from the device.
    public class TSRecorder
    {

        public CircularBuffer ts_data_queue = new CircularBuffer(GlobalDefines.CircularBufferStartingCapacity);

        object locker = new object();

        public event EventHandler<bool> onRecordStatusChange;

        protected bool _record;
        public bool record
        {
            get
            {
                lock (locker)
                    return _record;
            }
            set
            {
                lock (locker)
                    _record = value;
            }
        }

        public int ID { get { return _id; } }
        private int _id = 0;

        private volatile bool recording = false;
        public bool IsRecording { get { return recording; } }

        // Full path of the file currently being written (empty when not recording).
        public string CurrentFilename { get; private set; } = "";

        string media_path = "";

        // ----- options (set by the UI before/while recording) -----
        public string FilenameTemplate { get; set; } = "{callsign}_{service}_{freq}_{date}_{time}";
        public bool WriteSidecar { get; set; } = true;
        public long MaxSizeBytes { get; set; } = 0;        // 0 = unlimited
        public TimeSpan MaxDuration { get; set; } = TimeSpan.Zero;   // Zero = unlimited

        // Last signal metadata seen for this tuner (used for the filename + sidecar).
        public OTSourceData LatestData { get; set; }

        private long _bytesWritten = 0;
        private DateTime _fileStartedUtc = DateTime.MinValue;
        private int _splitIndex = 0;

        private volatile bool _running = false;
        private Thread _recorderThread = null;
        private readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);

        public TSRecorder(string _media_path, int id, OTSource TSSource)
        {
            media_path = _media_path;
            this._id = id;

            // register for TS Stream
            TSSource.RegisterTSConsumer(id, ts_data_queue);

            recording = false;

            _recorderThread = new Thread(worker_thread)
            {
                IsBackground = true,
                Name = "TSRecorder" + id.ToString()
            };
            _recorderThread.Start();
        }

        public void Close()
        {
            // Request a stop and wait for the worker to finalise the file. Never
            // abort the thread so the last packets are flushed to disk.
            _running = false;
            record = false;
            _wake.Set();

            try { _recorderThread?.Join(2000); } catch { }

            _recorderThread = null;
        }

        public static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ' ' ? '_' : c);

            string result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "unknown" : result;
        }

        private string BuildFilename()
        {
            var d = LatestData;
            string service = d?.service_name ?? "";
            string callsign = opentuner.Utilities.CallsignParser.Extract(service) ?? "unknown";
            string freq = d != null && d.frequency > 0
                ? (d.frequency / 1000.0).ToString("F3", CultureInfo.InvariantCulture)
                : "0";

            string name = FilenameTemplate ?? "{callsign}_{service}_{freq}_{date}_{time}";
            name = name
                .Replace("{callsign}", Sanitize(callsign))
                .Replace("{service}", Sanitize(service))
                .Replace("{freq}", freq)
                .Replace("{sr}", (d?.symbol_rate ?? 0).ToString())
                .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
                .Replace("{time}", DateTime.Now.ToString("HH-mm-ss"))
                .Replace("{tuner}", (_id + 1).ToString());

            if (_splitIndex > 0)
                name += "_" + _splitIndex.ToString("D3");

            return Sanitize(name) + ".ts";
        }

        private void StopRecording(ref BinaryWriter binWriter)
        {
            try
            {
                if (binWriter != null)
                {
                    binWriter.Flush();
                    binWriter.Close();
                    binWriter.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error closing raw TS file");
            }
            finally
            {
                binWriter = null;
            }

            WriteSidecarFile();

            recording = false;
            CurrentFilename = "";
            _bytesWritten = 0;
            onRecordStatusChange?.Invoke(this, false);
        }

        private void WriteSidecarFile()
        {
            if (!WriteSidecar || string.IsNullOrEmpty(CurrentFilename))
                return;

            try
            {
                var d = LatestData;
                var meta = new
                {
                    file = Path.GetFileName(CurrentFilename),
                    recorded_utc = _fileStartedUtc.ToString("o"),
                    tuner = _id + 1,
                    callsign = opentuner.Utilities.CallsignParser.Extract(d?.service_name ?? ""),
                    service_name = d?.service_name,
                    service_provider = "",
                    frequency_khz = d?.frequency ?? 0,
                    frequency_mhz = d != null ? Math.Round(d.frequency / 1000.0, 4) : 0,
                    symbol_rate = d?.symbol_rate ?? 0,
                    mer = d?.mer ?? 0,
                    db_margin = d?.db_margin ?? 0,
                    device = ""
                };

                File.WriteAllText(CurrentFilename + ".json", JsonConvert.SerializeObject(meta, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Warning("Unable to write record sidecar: " + ex.Message);
            }
        }

        private bool StartRecording(ref BinaryWriter binWriter)
        {
            try
            {
                string filename = BuildFilename();

                if (!string.IsNullOrEmpty(media_path) && !Directory.Exists(media_path))
                    Directory.CreateDirectory(media_path);

                string fullPath = Path.Combine(media_path, filename);

                binWriter = new BinaryWriter(File.Open(fullPath, FileMode.Create));

                recording = true;
                CurrentFilename = fullPath;
                _bytesWritten = 0;
                _fileStartedUtc = DateTime.UtcNow;

                Log.Information("Recording raw TS to " + fullPath);

                onRecordStatusChange?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unable to start raw TS recording");
                binWriter = null;
                recording = false;
                CurrentFilename = "";
                record = false;
                onRecordStatusChange?.Invoke(this, false);
                return false;
            }
        }

        private bool ShouldSplit()
        {
            if (MaxSizeBytes > 0 && _bytesWritten >= MaxSizeBytes)
                return true;

            if (MaxDuration > TimeSpan.Zero && _fileStartedUtc != DateTime.MinValue &&
                DateTime.UtcNow - _fileStartedUtc >= MaxDuration)
                return true;

            return false;
        }

        public void worker_thread()
        {
            _running = true;
            BinaryWriter binWriter = null;
            bool ts_sync = true;

            try
            {
                while (_running)
                {
                    if (recording == false && record == true)
                    {
                        _splitIndex = 0;
                        StartRecording(ref binWriter);
                        ts_data_queue.Clear();
                        ts_sync = true;
                    }
                    else if (recording == true && record == false)
                    {
                        StopRecording(ref binWriter);
                    }
                    else if (recording == true && ShouldSplit())
                    {
                        // close the current file and roll over to a new one
                        StopRecording(ref binWriter);
                        _splitIndex++;
                        StartRecording(ref binWriter);
                        ts_data_queue.Clear();
                        ts_sync = true;
                    }

                    int ts_data_count = ts_data_queue.Count;

                    if (ts_data_count > 0)
                    {
                        try
                        {
                            byte data = ts_data_queue.Dequeue();

                            if (record == true && binWriter != null)
                            {
                                if (ts_sync == true && data == 0x47)
                                {
                                    Log.Information("TS Header Sync");
                                    ts_sync = false;
                                }

                                if (ts_sync == false)
                                {
                                    binWriter.Write(data);
                                    _bytesWritten++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Raw TS recorder read error");
                        }
                    }
                    else
                    {
                        // Wake immediately if closing/record state changes, otherwise
                        // idle briefly to avoid spinning.
                        _wake.Wait(50);
                        _wake.Reset();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Raw TS recorder thread terminated unexpectedly");
            }
            finally
            {
                StopRecording(ref binWriter);
                Log.Information("Raw TS recorder thread stopped");
            }
        }
    }
}
