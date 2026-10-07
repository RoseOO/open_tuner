using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using opentuner.MediaPlayers;
using opentuner.Utilities;

namespace opentuner.MediaSources
{
    public abstract class OTSource
    {
        public delegate void VideoChangeCallback(int video_number, bool start);
        public delegate void SourceDataChange(int video_nr, OTSourceData properties, string description);
        public abstract event SourceDataChange OnSourceData;

        // Raised when the LNB power supply / polarisation changes.
        // 0 = off, 1 = vertical, 2 = horizontal.
        public event Action<int> OnPolarizationChanged;

        protected void NotifyPolarizationChanged(int supply)
        {
            OnPolarizationChanged?.Invoke(supply);
        }


        // Request the Source Name (eg. Minitiouner)
        public abstract string GetName();

        // Request Device Name (eg. FTDI, Picotuner, etc)
        public abstract string GetDeviceName();

        // Request Source Description (can also include some info regarding its current settings)
        public abstract string GetDescription();

        public abstract string GetMoreInfoLink();

        // Shows a Source specific setting screen. Called when user clicks "Settings" in source selection screen.
        public abstract void ShowSettings();

        public abstract void SetFrequency(int device, uint frequency, uint symbol_rate, bool offset_included);
        public abstract long GetFrequency(int device, bool offset_included);

        public abstract int GetVolume(int device);
        public abstract void UpdateVolume(int device, int volume_delta);
        public abstract void ToggleMute(int device);

        public abstract Dictionary<string, string> GetSignalData(int device);

        public abstract void StartStreaming(int device);
        public abstract void StopStreaming(int device);
        public abstract int GetVideoSourceCount();
        public abstract CircularBuffer GetVideoDataQueue(int device);
        public abstract void RegisterTSConsumer(int device, CircularBuffer ts_buffer_queue);

        public abstract void Close();

        // initialize returns how many video players it need
        public abstract int Initialize(VideoChangeCallback VideoChangeCB, Control Parent);

        // WPF / headless initialise: no WinForms parent, no property UI is built.
        public int InitializeHeadless(VideoChangeCallback VideoChangeCB)
        {
            return Initialize(VideoChangeCB, null);
        }

        public abstract void ConfigureVideoPlayers(List<OTMediaPlayer> MediaPlayers);

        public abstract void ConfigureTSRecorders(List<TSRecorder> TSRecorders);

        public abstract void ConfigureTSStreamers(List<TSUdpStreamer> TSStreamers);

        public abstract void ConfigureMediaPath(string MediaPath);
        public abstract bool DeviceConnected { get; }

        //public abstract byte SelectHardwareInterface(int hardware_interface);

        public abstract void OverrideDefaultMuted(bool Override);

        public abstract void UpdateFrequencyPresets(List<StoredFrequency> FrequencyPresets);

        // ---- UI-agnostic property model (used by the WPF host) ----
        public virtual List<PropertyGroupDescriptor> GetPropertyGroups()
        {
            return new List<PropertyGroupDescriptor>();
        }

        public virtual string GetPropertyValue(int groupId, string key)
        {
            return "";
        }

        public virtual void SetPropertySlider(int groupId, string key, int value)
        {
        }

        public virtual void SetPropertyMediaButton(int groupId, string key, int function)
        {
        }

        // Right-click menu for a property (tuner control, RF input, symbol rate, LNB, presets...)
        public virtual List<PropertyMenuOption> GetPropertyMenu(int groupId, string key)
        {
            return new List<PropertyMenuOption>();
        }

        public virtual void InvokePropertyCommand(int groupId, string key, int command, int[] options)
        {
        }

        // Media button state bitmask: 1 = muted, 2 = recording, 4 = udp streaming.
        public virtual int GetMediaButtonState(int groupId)
        {
            return 0;
        }

        // Raised when "Tuner Control" is chosen for a property. The WPF host
        // subscribes and shows its own tune dialog; the WinForms host falls back
        // to the per-tuner TunerControlForm.
        public event Action<int> TunerControlRequested;

        protected bool HasTunerControlSubscribers => TunerControlRequested != null;

        protected void RaiseTunerControlRequested(int tuner)
        {
            TunerControlRequested?.Invoke(tuner);
        }

        // ---- Settings access for the WPF host ----
        public virtual object GetSettingsObject()
        {
            return null;
        }

        public virtual void PersistSettings()
        {
        }

    }
}
