using System;
using NAudio.Wave;

namespace OpenTuner.Wpf.Sdr
{
    /// <summary>Small NAudio sink for narrowband SDR listening (48 kHz mono).</summary>
    public class SdrAudioOutput : IDisposable
    {
        public const int AudioRateHz = 48000;

        private WaveOutEvent _waveOut;
        private BufferedWaveProvider _buffer;
        private int _volume = 70;

        public bool IsOpen => _waveOut != null;

        public void Open()
        {
            if (_waveOut != null)
                return;

            var format = new WaveFormat(AudioRateHz, 16, 1);
            _buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(400),
                DiscardOnBufferOverflow = true
            };

            _waveOut = new WaveOutEvent { DesiredLatency = 120 };
            _waveOut.Init(_buffer);
            _waveOut.Volume = _volume / 100f;
            _waveOut.Play();
        }

        public void SetVolume(int volume)
        {
            _volume = Math.Max(0, Math.Min(100, volume));
            if (_waveOut != null)
                _waveOut.Volume = _volume / 100f;
        }

        public void Write(float sample)
        {
            if (_buffer == null)
                return;

            if (sample > 1f) sample = 1f;
            else if (sample < -1f) sample = -1f;

            short s = (short)(sample * 32767f);
            var bytes = new byte[2];
            bytes[0] = (byte)(s & 0xFF);
            bytes[1] = (byte)((s >> 8) & 0xFF);

            try { _buffer.AddSamples(bytes, 0, 2); } catch { }
        }

        public void Close()
        {
            try { _waveOut?.Stop(); } catch { }
            try { _waveOut?.Dispose(); } catch { }
            _waveOut = null;
            _buffer = null;
        }

        public void Dispose() => Close();
    }
}
