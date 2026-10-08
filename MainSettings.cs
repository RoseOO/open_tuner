using opentuner.SettingsManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner
{
    public class MainSettings : GenericSettings
    {
        [Group("Settings 1")]
        [FriendlyName("Media Path")]
        public string media_path = "";
        public string media_video_path = "";

        // When enabled, incoming transport streams are recorded to raw .ts files as
        // soon as streaming starts (the per-tuner "R" button can still toggle it).
        public bool auto_record = false;

        [Group("Settings 2")]
        public bool enable_spectrum_checkbox = true;
        public bool enable_sdr_spectrum_checkbox = false;
        public bool enable_chatform_checkbox = true;
        public bool enable_mqtt_checkbox = false;
        public bool enable_quicktune_checkbox = false;
        public bool enable_datvreporter_checkbox = false;

        // future
        public bool enable_plutoctrl_checkbox = false;

        // ----- recording -----
        // {callsign} {service} {freq} {sr} {date} {time} {tuner} are replaced at record start.
        public string record_filename_template = "{callsign}_{service}_{freq}_{date}_{time}";
        public bool record_sidecar = true;      // write a .json sidecar with signal metadata
        public int record_max_mb = 0;           // 0 = unlimited, auto-split size
        public int record_max_minutes = 0;      // 0 = unlimited, auto-split duration

        // ----- snapshots -----
        public int snapshot_interval_seconds = 0;   // 0 = disabled

        // ----- layout -----
        public string layout_preset = "2-side";     // "1", "2-side", "2-stack", "4-quad"

        public int default_source = 0;
        public bool mute_at_startup = true;

        public bool auto_connect = false;

        public bool hide_properties = false; // can also be toggled with CTRL-P
        public bool hide_ExtraTool = false;  // can also be toggled with CTRL-E
        public bool[] show_video_info = { true, true, true, true };

        public int[] mediaplayer_preferences = { 0, 1, 1, 1 };
        public bool[] mediaplayer_windowed = { false, false, false, false };
        public string[] streamer_udp_hosts = { "127.0.0.1", "127.0.0.1", "127.0.0.1", "127.0.0.1" };
        public int[] streamer_udp_ports = { 5000, 5001, 5002, 5003 };

        // loaded on startup and updated on exit
        public int gui_window_width = -1;
        public int gui_window_height = -1;
        public int gui_window_x = -1;
        public int gui_window_y = -1;
        public int gui_window_state = 0;
        public int gui_main_splitter_position = 436;

        // WPF UI language (see opentunerWpf LocalizationManager.Languages)
        public string language = "en";

        // QSO logging defaults
        public string station_callsign = "M1RXO";
        public string qso_satellite = "QO-100";
        public string qso_prop_mode = "SAT";
    }
}
