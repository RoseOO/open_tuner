using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace opentuner.Utilities
{
    public class BandplanChannel
    {
        public string Name = "";
        public string Block = "";
        public double RxFreqMHz;    // x-freq (downlink)
        public double TxFreqMHz;    // s-freq (uplink) - may be 0 if unknown
        public uint SymbolRate;     // kSym/s

        public bool IsBeacon =>
            !string.IsNullOrEmpty(Name) && Name.IndexOf("BEACON", StringComparison.OrdinalIgnoreCase) >= 0;

        public override string ToString() =>
            (string.IsNullOrEmpty(Block) ? "" : Block + "  ") +
            (RxFreqMHz > 0 ? RxFreqMHz.ToString("0.####", CultureInfo.InvariantCulture) + " MHz" : "") +
            (SymbolRate > 0 ? "  " + SymbolRate + "k" : "") +
            (string.IsNullOrEmpty(Name) ? "" : "  " + Name);
    }

    /// <summary>Loads the QO-100 bandplan (extra/bandplan.xml).</summary>
    public class Bandplan
    {
        public readonly List<BandplanChannel> Channels = new List<BandplanChannel>();

        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra", "bandplan.xml");

        public static Bandplan Load(string path)
        {
            var bp = new Bandplan();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return bp;

                var doc = XDocument.Load(path);
                if (doc.Root == null)
                    return bp;

                foreach (var ch in doc.Root.Elements("channel"))
                {
                    double.TryParse(ch.Element("x-freq")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rx);
                    double.TryParse(ch.Element("s-freq")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double tx);
                    uint.TryParse(ch.Element("sr")?.Value, out uint sr);

                    bp.Channels.Add(new BandplanChannel
                    {
                        Name = ch.Element("name")?.Value ?? "",
                        Block = ch.Element("block")?.Value ?? "",
                        RxFreqMHz = rx,
                        TxFreqMHz = tx,
                        SymbolRate = sr
                    });
                }
            }
            catch { }

            return bp;
        }

        public IEnumerable<BandplanChannel> Beacons()
        {
            foreach (var c in Channels)
                if (c.IsBeacon)
                    yield return c;
        }
    }
}
