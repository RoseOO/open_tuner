using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Serilog;

namespace opentuner
{
    public class TSStreamMediaInput : LibVLCSharp.Shared.MediaInput
    {

        CircularBuffer ts_data_queue;
        public bool ts_sync = false;
        public bool end = false;

        public TSStreamMediaInput(CircularBuffer _ts_data_queue )
        {
            // we can't seek live data
            CanSeek = false;
            ts_data_queue = _ts_data_queue;
        }

        public override bool Open(out ulong size)
        {
            Log.Information("Open Media Input");

            bool success = true;
            size = ulong.MaxValue;

            return success;
        }

        public override void Close()
        {
            Log.Information("Close Media Input");
        }


        public override int Read(IntPtr buf, uint len)
        {
            int timeout = 0;

            // wait for a minimum amount of data (or end)
            while (ts_data_queue.Count < 1010)
            {
                if (end)
                    return 0;

                if (timeout > 500000)
                {
                    Log.Information("TSStreamMediaInput : Read Timeout");
                    return 0;
                }

                Thread.Sleep(5);
                timeout += 50;
            }

            int available = ts_data_queue.Count;
            int want = (int)Math.Min((long)len, (long)available);
            if (want <= 0)
                return 0;

            // dequeue the whole block in one locked copy (fast path)
            byte[] chunk = ts_data_queue.DequeueBytes(want);

            int start = 0;
            if (!ts_sync)
            {
                // drop leading bytes until the first 0x47 sync byte
                while (start < chunk.Length && chunk[start] != 0x47)
                    start++;

                if (start >= chunk.Length)
                    return 0;   // no sync byte in this block

                ts_sync = true;
            }

            int count = chunk.Length - start;
            if (count <= 0)
                return 0;

            Marshal.Copy(chunk, start, buf, count);
            return count;
        }

        public override bool Seek(ulong offset)
        {
            // seeking is not allowed/possible
            Log.Information("VLC Trying to Seek" + offset.ToString());
            return false;
        }

    }
}
