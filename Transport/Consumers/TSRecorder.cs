using opentuner.MediaSources;
using Serilog;
using System;
using System.IO;
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

            recording = false;
            CurrentFilename = "";
            onRecordStatusChange?.Invoke(this, false);
        }

        private bool StartRecording(ref BinaryWriter binWriter)
        {
            try
            {
                string filename = DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss") + "_" + _id + ".ts";

                // if path doesn't exist then save in same folder
                if (!string.IsNullOrEmpty(media_path) && Directory.Exists(media_path))
                {
                    filename = Path.Combine(media_path, DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss") + "_" + _id + ".ts");
                }

                binWriter = new BinaryWriter(File.Open(filename, FileMode.Create));

                recording = true;
                CurrentFilename = filename;

                Log.Information("Recording raw TS to " + filename);

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
                        StartRecording(ref binWriter);
                        ts_data_queue.Clear();
                        ts_sync = true;
                    }
                    else if (recording == true && record == false)
                    {
                        StopRecording(ref binWriter);
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
