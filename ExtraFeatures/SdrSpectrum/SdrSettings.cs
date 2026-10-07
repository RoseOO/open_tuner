namespace opentuner.ExtraFeatures.SdrSpectrum
{
    public enum SdrSourceType
    {
        RtlTcp = 0,
        RtlSdr = 1,
        HackRf = 2,
        Pluto = 3
    }

    public enum SdrSampleFormat
    {
        Unsigned8 = 0,
        Signed8 = 1,
        Signed16 = 2
    }

    public enum SdrDemodMode
    {
        AM = 0,
        USB = 1,
        LSB = 2
    }

    public class SdrSettings
    {
        public int Version = 1;

        // which SDR backend to use
        public SdrSourceType SourceType = SdrSourceType.RtlTcp;

        // RTL-TCP connection
        public string RtlTcpHost = "127.0.0.1";
        public int RtlTcpPort = 1234;

        // PlutoSDR / Pluto+ libiio URI: "usb:", "usb:x.y.z", "ip:192.168.2.1"
        public string PlutoUri = "usb:";

        // local device selection (RTL-SDR index / HackRF index)
        public int DeviceIndex = 0;

        // radio
        public uint SampleRateHz = 2400000;
        public uint CenterFrequencyHz = 741500000;  // default IF (741.5 MHz, horizontal WB)
        public bool Agc = true;
        public int GainDb = 200;                    // tenths of dB when Agc == false
        public int PpmCorrection = 0;

        // display
        public int FftSize = 2048;
        public int MinDb = -105;
        public int MaxDb = -15;

        // sweep mode - for narrow-bandwidth receivers (RTL) this retunes across a
        // wider span and stitches the FFTs together into one view (slower refresh).
        public bool SweepEnabled = false;
        public uint SweepSpanHz = 9000000;        // total span to cover (9 MHz)
        public uint SweepCenterHz = 741500000;    // centre of the swept region
        public int SweepDwellMs = 250;            // time spent per step

        // tuning
        public uint BroadbandSymbolRate = 1500;   // ksym/s used when tuning DATV from the spectrum
        public uint NarrowbandSymbolRate = 30;    // ksym/s used for narrowband/SSB

        // estimate the symbol rate from the measured width of the signal peak
        // (occupied bandwidth = symbol rate * (1 + rolloff))
        public bool EstimateSymbolRate = true;
        public double Rolloff = 0.35;
        public bool SnapToPeak = true;   // snap a click to the nearest detected peak

        // narrowband listening (line-up)
        public bool AudioEnabled = false;
        public SdrDemodMode DemodMode = SdrDemodMode.USB;
        public int AudioVolume = 70;              // 0..100
        public int AudioOffsetHz = 0;             // BFO offset within the tuned passband

        // convenience presets (Hz)
        public uint BroadbandCenterHz = 741500000;      // 741.5 MHz
        public uint NarrowbandCenterHz = 739500000;     // 739.5 MHz

        // LNB local oscillator (kHz). The SDR sees the IF; the tuner expects the
        // satellite frequency, so this is added when tuning: sat = IF + LO.
        public uint LnbLoKHz = 9750000;

        // Track the tuner's LNB power selection: horizontal = wideband (741.5 MHz),
        // vertical = narrowband (739.5 MHz).
        public bool FollowLnbPolarization = true;
    }
}
