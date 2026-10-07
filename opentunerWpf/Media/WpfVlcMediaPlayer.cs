using System;
using LibVLCSharp.Shared;
using opentuner;
using opentuner.MediaPlayers;
using opentuner.MediaPlayers.VLC;

namespace OpenTuner.Wpf.Players
{
    /// <summary>
    /// Native WPF VLC media player (LibVLCSharp.WPF). Mirrors the WinForms
    /// VLCMediaPlayer but hosts into a WPF VideoView.
    /// </summary>
    public class WpfVlcMediaPlayer : OTMediaPlayer
    {
        private static LibVLC _libVlc;

        private readonly LibVLCSharp.WPF.VideoView _videoView;

        private Media _media;
        private TSStreamMediaInput _mediaInput;
        private MediaPlayer _mediaPlayer;

        private int _volume;
        private int _id;

        private CircularBuffer _queue;

        public override event EventHandler<MediaStatus> onVideoOut;

        public WpfVlcMediaPlayer(LibVLCSharp.WPF.VideoView videoView)
        {
            if (_libVlc == null)
                _libVlc = new LibVLC("--aout=directsound", "--no-video-title-show");

            _videoView = videoView;
        }

        public override int getID() => _id;
        public override string GetName() => "VLC";

        public override void Initialize(CircularBuffer TSDataQueue, int ID)
        {
            _id = ID;
            Initialize(TSDataQueue);
        }

        public override void Initialize(CircularBuffer TSDataQueue)
        {
            _queue = TSDataQueue;
        }

        private void UpdatePlayer(MediaPlayer player, bool play)
        {
            if (_videoView.Dispatcher.CheckAccess())
            {
                _videoView.MediaPlayer = player;
                if (play)
                    _videoView.MediaPlayer?.Play(_media);
            }
            else
            {
                _videoView.Dispatcher.Invoke(() => UpdatePlayer(player, play));
            }
        }

        public override void Play()
        {
            _queue?.Clear();

            if (_mediaInput != null)
            {
                _mediaInput.ts_sync = false;
                _mediaInput.end = false;
            }

            Stop();

            _mediaInput = new TSStreamMediaInput(_queue);
            _media = new Media(_libVlc, _mediaInput);

            var cfg = new MediaConfiguration { EnableHardwareDecoding = false };
            _media.AddOption(cfg);

            _mediaPlayer = new MediaPlayer(_libVlc)
            {
                EnableMouseInput = false,
                EnableKeyInput = false
            };

            _mediaPlayer.Vout += (s, e) =>
            {
                if (_mediaPlayer != null)
                    _mediaPlayer.Volume = _volume;

                MediaStatus status = new MediaStatus();
                foreach (var track in _media.Tracks)
                {
                    switch (track.TrackType)
                    {
                        case TrackType.Audio:
                            status.AudioChannels = track.Data.Audio.Channels;
                            status.AudioCodec = _media.CodecDescription(TrackType.Audio, track.Codec);
                            status.AudioRate = track.Data.Audio.Rate;
                            break;
                        case TrackType.Video:
                            status.VideoCodec = _media.CodecDescription(TrackType.Video, track.Codec);
                            status.VideoWidth = track.Data.Video.Width;
                            status.VideoHeight = track.Data.Video.Height;
                            break;
                    }
                }

                onVideoOut?.Invoke(this, status);
            };

            UpdatePlayer(_mediaPlayer, true);
        }

        public override void Stop()
        {
            if (_mediaInput != null)
                _mediaInput.end = true;

            try { _mediaPlayer?.Dispose(); } catch { }
            _mediaPlayer = null;

            UpdatePlayer(null, false);
        }

        public override void Close()
        {
            try { _mediaInput?.Dispose(); } catch { }
            try { _media?.Dispose(); } catch { }
        }

        public override void SnapShot(string FileName)
        {
            try { _mediaPlayer?.TakeSnapshot(0, FileName, 0, 0); } catch { }
        }

        public override void SetVolume(int Volume)
        {
            _volume = Volume;
            try { if (_mediaPlayer != null) _mediaPlayer.Volume = Volume; } catch { }
        }

        public override int GetVolume() => _volume;
    }
}
