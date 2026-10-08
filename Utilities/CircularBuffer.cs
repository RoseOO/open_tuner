using Serilog;
using System;

namespace opentuner
{
    public class CircularBuffer
    {
        private byte[] buffer;
        private int head;
        private int tail;
        private readonly int capacity;
        private readonly object syncRoot = new object();

        public int Count { get; private set; }

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentException("Buffer capacity must be positive.", nameof(capacity));
            }

            this.capacity = capacity;
            buffer = new byte[capacity];
        }

        public void Enqueue(byte item)
        {
            lock (syncRoot)
            {
                if (Count == capacity)
                {
                    Dequeue();
                }

                buffer[head] = item;
                head = (head + 1) % capacity;
                Count++;
            }
        }

        public void Enqueue(byte[] items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            lock (syncRoot)
            {
                int len = items.Length;

                // if the incoming block is bigger than the whole buffer, keep the tail
                if (len >= capacity)
                {
                    Array.Copy(items, len - capacity, buffer, 0, capacity);
                    head = 0;
                    tail = 0;
                    Count = capacity;
                    return;
                }

                // make room by dropping the oldest bytes (never blocks / never loops)
                int free = capacity - Count;
                if (len > free)
                {
                    int drop = len - free;
                    tail = (tail + drop) % capacity;
                    Count -= drop;
                }

                int first = Math.Min(len, capacity - head);
                Array.Copy(items, 0, buffer, head, first);
                if (len > first)
                    Array.Copy(items, first, buffer, 0, len - first);

                head = (head + len) % capacity;
                Count += len;
            }
        }

        public byte Dequeue()
        {
            lock (syncRoot)
            {
                if (Count == 0)
                {
                    // throw new InvalidOperationException("Buffer is empty.");
                    /* remark DL1RF: I observed this exception a few times.
                     * There seem to be a glitch between threads.
                     * Log an Error instead and return 0 as fake data here
                     * This happend while stop playing or leaving the program at all
                     * Occured only a very times.
                     */
                    Log.Warning("CircularBuffer.Dequeue: Buffer is empty.");
                    return 0;
                }

                byte item = buffer[tail];
                tail = (tail + 1) % capacity;
                Count--;

                return item;
            }
        }

        public byte Peek()
        {
            lock (syncRoot)
            {
                if (Count == 0)
                {
                    // throw new InvalidOperationException("Buffer is empty.");
                    /* remark DL1RF: I observed this exception a few times.
                     * There seem to be a glitch between threads.
                     * Log an Error instead and return 0 as fake data here
                     * This happend while stop playing or leaving the program at all
                     * Occured only a very few times.
                     */
                    Log.Warning("CircularBuffer.Peek: Buffer is empty.");
                    return 0;
                }

                return buffer[tail];
            }
        }

        public byte TryPeek()
        {
            lock (syncRoot)
            {
                if (Count == 0)
                {
                    return 0;
                }

                return buffer[tail];
            }
        }

        public byte[] DequeueBytes(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            lock (syncRoot)
            {
                if (Count == 0)
                {
                    throw new InvalidOperationException("Buffer is empty.");
                }

                int bytesToRead = Math.Min(count, Count);
                byte[] result = new byte[bytesToRead];
                int sourceIndex = tail;
                int destIndex = 0;

                while (bytesToRead > 0)
                {
                    int bytesToCopy = Math.Min(bytesToRead, capacity - sourceIndex);
                    Array.Copy(buffer, sourceIndex, result, destIndex, bytesToCopy);
                    sourceIndex = (sourceIndex + bytesToCopy) % capacity;
                    destIndex += bytesToCopy;
                    bytesToRead -= bytesToCopy;
                }

                Count -= result.Length;
                tail = sourceIndex;

                return result;
            }
        }

        public void Clear()
        {
            lock (syncRoot)
            {
                head = tail = Count = 0;
            }
        }
    }
}
