using AVFoundation;
using MediaPlayer;
using TagTuner.Core.Model;

namespace TagTuner.Mac;

/// <summary>
/// Spielt ein Lied zum Reinhören. AVAudioPlayer reicht dafür: FLAC, MP3,
/// AAC, WAV und AIFF kann macOS selbst. Die Medientasten und die Anzeige im
/// Kontrollzentrum laufen über MPNowPlayingInfoCenter.
/// </summary>
internal sealed class Player
{
    public event Action? Changed;

    /// <summary>Am Ende eines Lieds: das nächste wählt der Besitzer.</summary>
    public event Action? Finished;

    private AVAudioPlayer? _player;
    private readonly FinishDelegate _finish;
    private float _volume;

    public AudioTrack? Track { get; private set; }

    /// <summary>
    /// Das Fenster, aus dem gerade gespielt wird. Es gibt einen Player für
    /// alle Tabs; weiter zum nächsten Lied geht es in der Liste dieses Fensters.
    /// </summary>
    public object? Owner { get; private set; }
    public bool IsPlaying => _player?.Playing == true;
    public double Position => _player?.CurrentTime ?? 0;
    public double Duration => _player?.Duration ?? 0;

    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_player is not null) _player.Volume = _volume; }
    }

    public Player(double volume)
    {
        _volume = (float)volume;
        _finish = new FinishDelegate(this);

        var cc = MPRemoteCommandCenter.Shared;
        cc.PlayCommand.AddTarget(_ => { Resume(); return MPRemoteCommandHandlerStatus.Success; });
        cc.PauseCommand.AddTarget(_ => { Pause(); return MPRemoteCommandHandlerStatus.Success; });
        cc.TogglePlayPauseCommand.AddTarget(_ => { Toggle(); return MPRemoteCommandHandlerStatus.Success; });
        cc.NextTrackCommand.AddTarget(_ => { Finished?.Invoke(); return MPRemoteCommandHandlerStatus.Success; });
        cc.ChangePlaybackPositionCommand.AddTarget(e =>
        {
            if (e is MPChangePlaybackPositionCommandEvent p) Seek(p.PositionTime);
            return MPRemoteCommandHandlerStatus.Success;
        });
    }

    /// <returns>Eine Fehlermeldung, wenn es nicht abspielbar ist.</returns>
    public string? Play(AudioTrack track, object? owner = null)
    {
        owner ??= Owner;
        Stop();
        Owner = owner;
        var p = AVAudioPlayer.FromUrl(NSUrl.FromFilename(track.Path), out var error);
        if (p is null) return error?.LocalizedDescription ?? "?";
        p.Volume = _volume;
        p.Delegate = _finish;
        p.PrepareToPlay();
        p.Play();
        _player = p;
        Track = track;
        UpdateNowPlaying();
        Changed?.Invoke();
        return null;
    }

    public void Toggle()
    {
        if (_player is null) return;
        if (_player.Playing) Pause(); else Resume();
    }

    public void Pause()
    {
        _player?.Pause();
        UpdateNowPlaying();
        Changed?.Invoke();
    }

    public void Resume()
    {
        _player?.Play();
        UpdateNowPlaying();
        Changed?.Invoke();
    }

    public void Seek(double seconds)
    {
        if (_player is null) return;
        _player.CurrentTime = Math.Clamp(seconds, 0, _player.Duration);
        UpdateNowPlaying();
    }

    public void Stop()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        Track = null;
        Owner = null;
        MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo();
        MPNowPlayingInfoCenter.DefaultCenter.PlaybackState = MPNowPlayingPlaybackState.Stopped;
        Changed?.Invoke();
    }

    /// <summary>
    /// Vor dem Schreiben loslassen: AVAudioPlayer hält die Datei offen. Danach
    /// an derselben Stelle weiter, nur wenn es vorher lief.
    /// </summary>
    public Action? Release(IEnumerable<string> paths)
    {
        if (Track is not { } t || !paths.Contains(t.Path)) return null;
        var at = Position;
        var was = IsPlaying;
        var owner = Owner;
        Stop();
        return () =>
        {
            if (!File.Exists(t.Path)) return;
            if (Play(t, owner) is null)
            {
                Seek(at);
                if (!was) Pause();
            }
        };
    }

    private void UpdateNowPlaying()
    {
        if (Track is not { } t || _player is null) return;
        MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo
        {
            Title = string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(t.FileName) : t.Title,
            Artist = t.Artist,
            AlbumTitle = t.Album,
            PlaybackDuration = _player.Duration,
            ElapsedPlaybackTime = _player.CurrentTime,
            PlaybackRate = _player.Playing ? 1 : 0,
        };
        MPNowPlayingInfoCenter.DefaultCenter.PlaybackState =
            _player.Playing ? MPNowPlayingPlaybackState.Playing : MPNowPlayingPlaybackState.Paused;
    }

    private sealed class FinishDelegate(Player owner) : AVAudioPlayerDelegate
    {
        public override void FinishedPlaying(AVAudioPlayer player, bool flag)
        {
            owner.Changed?.Invoke();
            owner.Finished?.Invoke();
        }
    }
}
