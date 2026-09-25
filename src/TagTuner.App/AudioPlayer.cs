using TagTuner.Core.Audio;
using TagTuner.Core.Model;
using Microsoft.UI.Dispatching;
using Windows.Media.Playback;

using TagTuner.Core.Settings;

namespace TagTuner.App;

/// <summary>
/// Die Vorschau-Wiedergabe.
///
/// Dünne Hülle um <see cref="MediaPlayer"/>, die zwei Dinge geraderückt:
/// Dessen Ereignisse kommen auf einem fremden Faden, und die Oberfläche darf
/// nur vom UI-Faden angefasst werden — darum geht alles durch die
/// DispatcherQueue. Und der Player hält die Datei offen, solange er sie
/// spielt; wer sie konvertieren will, muss ihn vorher loslassen.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly MediaPlayer _player = new();

    public AudioTrack? Current { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>Zustand, Titel oder Position haben sich geändert.</summary>
    public event Action? Changed;

    /// <summary>Wiedergabe nicht möglich — mit einer Begründung für die Statuszeile.</summary>
    public event Action<string>? Failed;

    public AudioPlayer(DispatcherQueue ui, double volume)
    {
        _ui = ui;
        _player.Volume = Math.Clamp(volume, 0, 1);
        _player.AudioCategory = MediaPlayerAudioCategory.Media;

        _player.PlaybackSession.PlaybackStateChanged += (s, _) => Post(() =>
        {
            IsPlaying = s.PlaybackState == MediaPlaybackState.Playing;
            Changed?.Invoke();
        });
        _player.PlaybackSession.PositionChanged += (_, _) => Post(() => Changed?.Invoke());

        _player.MediaEnded += (_, _) => Post(() =>
        {
            IsPlaying = false;
            Changed?.Invoke();
        });

        _player.MediaFailed += (_, args) => Post(() =>
        {
            var what = Current?.FileName ?? Strings.T("The file");
            Current = null;
            IsPlaying = false;
            Changed?.Invoke();

            // OGG und AIFF kann Windows von Haus aus oft nicht — das ist kein
            // Fehler der App, aber der Nutzer soll wissen, woran es liegt.
            Failed?.Invoke(Strings.T("{0} cannot be played ({1}).", what, args.Error));
        });
    }

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    public TimeSpan Position
    {
        get => _player.PlaybackSession.Position;
        set => _player.PlaybackSession.Position = value;
    }

    public TimeSpan Duration
    {
        get
        {
            var d = _player.PlaybackSession.NaturalDuration;
            return d > TimeSpan.Zero ? d : Current?.Duration ?? TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Spielt einen Track. Derselbe Track noch einmal heißt Pause/Weiter —
    /// so tut die Leertaste das, was man von ihr erwartet.
    /// </summary>
    public void Play(AudioTrack track)
    {
        if (Current is not null && Current.Path == track.Path)
        {
            Toggle();
            return;
        }

        try
        {
            Current = track;
            _player.Source = WithTags(track);
            _player.Play();
        }
        catch (Exception ex)
        {
            Current = null;
            IsPlaying = false;
            Changed?.Invoke();
            Failed?.Invoke($"{track.FileName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Die Datei samt dem, was Windows über sie anzeigen soll: Titel,
    /// Interpret, Album und Cover aus den Tags. Ohne das zeigt die
    /// Medienanzeige von Windows (Lautstärke-Overlay, Sperrbildschirm,
    /// Schnelleinstellungen) nur den Dateinamen oder gar nichts.
    /// </summary>
    private static MediaPlaybackItem WithTags(AudioTrack track)
    {
        var item = new MediaPlaybackItem(
            Windows.Media.Core.MediaSource.CreateFromUri(new Uri(track.Path)));

        try
        {
            var shown = item.GetDisplayProperties();
            shown.Type = Windows.Media.MediaPlaybackType.Music;

            var music = shown.MusicProperties;
            music.Title = string.IsNullOrWhiteSpace(track.Title)
                ? Path.GetFileNameWithoutExtension(track.Path)
                : track.Title;
            music.Artist = track.Artist;
            music.AlbumTitle = track.Album;
            music.AlbumArtist = track.AlbumArtist;
            music.TrackNumber = track.Track;

            if (AudioProbe.ReadCover(track.Path) is { Data.Length: > 0 } cover)
            {
                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(cover.Data);
                    writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                    writer.DetachStream();
                }
                shown.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream);
            }

            item.ApplyDisplayProperties(shown);
        }
        catch { /* Ohne Anzeige spielt es trotzdem */ }

        return item;
    }

    public void Toggle()
    {
        if (Current is null) return;
        if (IsPlaying) _player.Pause(); else _player.Play();
    }

    /// <summary>
    /// Hält an und gibt die Datei frei. Vor jedem Schreibvorgang nötig —
    /// ffmpeg und TagLib kommen sonst nicht an eine Datei heran, die der
    /// Player noch offen hat.
    /// </summary>
    public void Stop()
    {
        if (Current is null) return;
        _player.Pause();
        _player.Source = null;
        Current = null;
        IsPlaying = false;
        Changed?.Invoke();
    }

    /// <summary>Anhalten, falls einer der genannten Pfade gerade läuft.</summary>
    public void ReleaseIfPlaying(IEnumerable<string> paths)
    {
        if (Current is null) return;
        if (paths.Any(p => string.Equals(p, Current.Path, StringComparison.OrdinalIgnoreCase)))
            Stop();
    }

    private void Post(Action action) => _ui.TryEnqueue(() => action());

    public void Dispose()
    {
        try { _player.Dispose(); } catch { }
    }
}
