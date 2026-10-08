using System;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using opentuner;
using opentuner.MediaPlayers;
using opentuner.MediaPlayers.FFMPEG;

namespace OpenTuner.Wpf.Players
{
    /// <summary>
    /// Native WPF FFMPEG (FlyleafLib) media player. Mirrors the WinForms
    /// FFMPEGMediaPlayer but hosts into a WPF FlyleafHost so the "FFMPEG"
    /// player setting works in the WPF app.
    /// </summary>
    public class WpfFlyleafMediaPlayer : OTMediaPlayer
    {
        private int player_volume = 0;

        public override event EventHandler<MediaStatus> onVideoOut;

        public Player player { get; set; }
        public Config config { get; set; }

        private readonly FlyleafLib.Controls.WPF.FlyleafHost media_player;

        private MediaStream media_stream;

        private CircularBuffer ts_data_queue;

        private int _id = 0;

        public WpfFlyleafMediaPlayer(FlyleafLib.Controls.WPF.FlyleafHost host)
        {
            media_player = host;

            config = new Config();
            config.Video.BackgroundColor = System.Windows.Media.Colors.Black;
            config.Demuxer.AllowTimeouts = false;
            config.Player.MinBufferDuration = TimeSpan.FromSeconds(1.5).Ticks;
            config.Decoder.MaxAudioFrames = 40;
            config.Demuxer.BufferDuration = TimeSpan.FromSeconds(10).Ticks;

            player = new Player(config);

            media_player.Player = player;

            player.OpenCompleted += Player_OpenCompleted;
            player.PlaybackStopped += (s, e) => { };
        }

        private void Player_OpenCompleted(object sender, OpenCompletedArgs e)
        {
            try { player.Audio.Volume = player_volume; } catch { }

            var media_status = new MediaStatus
            {
                VideoCodec = player.Video.Codec,
                VideoWidth = Convert.ToUInt32(player.Video.Width),
                VideoHeight = Convert.ToUInt32(player.Video.Height),
                AudioCodec = player.Audio.Codec,
                AudioChannels = Convert.ToUInt32(player.Audio.Channels),
                AudioRate = Convert.ToUInt32(player.Audio.SampleRate)
            };

            onVideoOut?.Invoke(this, media_status);
        }

        public override void Initialize(CircularBuffer TSDataQueue)
        {
            ts_data_queue = TSDataQueue;
            media_stream = new MediaStream(TSDataQueue);
        }

        public override void Initialize(CircularBuffer TSDataQueue, int ID)
        {
            _id = ID;
            Initialize(TSDataQueue);
        }

        public override void Play()
        {
            try
            {
                ts_data_queue?.Clear();
                media_stream.ts_sync = false;
                media_stream.end = false;
                player.OpenAsync(media_stream);
                player.Play();
            }
            catch { }
        }

        public override void Stop()
        {
            try
            {
                if (media_stream != null)
                    media_stream.end = true;
                if (player.IsPlaying)
                    player.Stop();
            }
            catch { }
        }

        public override void Close()
        {
            try { player?.Dispose(); } catch { }
            try { media_stream?.Dispose(); } catch { }
        }

        public override void SnapShot(string FileName)
        {
            try { player?.TakeSnapshotToFile(FileName); } catch { }
        }

        public override void SetVolume(int Volume)
        {
            player_volume = Volume;
            try { if (player != null) player.Audio.Volume = Volume; } catch { }
        }

        public override int GetVolume() => player_volume;

        public override string GetName() => "FFMPEG";

        public override int getID() => _id;
    }
}
