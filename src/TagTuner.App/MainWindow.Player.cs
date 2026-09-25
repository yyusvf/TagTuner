using TagTuner.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace TagTuner.App;

// Die Wiedergabe unten links: Abspielen, die Stelle im Lied und die
// Lautstärke. Alles bewusst zurückhaltend; das Abspielen ist zum
// Reinhören da, nicht der Zweck der App.

public sealed partial class MainWindow
{
    /// <summary>Die Lautstärke vor dem Stummschalten, für das Zurück.</summary>
    private double _unmutedVolume = 20;

    /// <summary>Solange gezogen wird, folgt die Leiste dem Zeiger, nicht dem Lied.</summary>
    private bool _seeking;

    private DispatcherTimer? _volumeClose;

    private void UpdatePlayerBar()
    {
        var track = _player.Current;

        PlayBtn.Content = _player.IsPlaying ? "" : "";
        // Die Leiste wird schon im Konstruktor gefüllt, bevor der erste Tab
        // existiert — ActiveTab wäre dort ein Zugriff ins Leere.
        var haveTracks = _tabs.Count > 0 && ActiveTab.Tracks.Count > 0;

        PlayBtn.IsEnabled = track is not null
            || haveTracks
            || ActivePane.Selected().Count > 0;

        UpdateVolumeIcon();

        // Die Lautstärke erst, wenn etwas läuft: Vorher gibt es nichts, dessen
        // Lautstärke man einstellen könnte.
        VolumeBtn.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        if (track is null) VolumePopup.IsOpen = false;

        NowPanel.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        if (track is null) return;

        NowPlaying.Text = string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title;
        NowTime.Text = $"{Clock(_player.Position)} / {Clock(_player.Duration)}";

        if (!_seeking)
        {
            var total = _player.Duration.TotalSeconds;
            SeekFill.Width = total > 0
                ? SeekTrack.ActualWidth * Math.Clamp(_player.Position.TotalSeconds / total, 0, 1)
                : 0;
        }

        static string Clock(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // ── Stelle im Lied ───────────────────────────────────────────

    private void OnSeekPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_player.Current is null) return;
        _seeking = true;
        SeekArea.CapturePointer(e.Pointer);
        SeekTo(e);
    }

    private void OnSeekMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_seeking) SeekTo(e);
    }

    private void OnSeekReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        SeekArea.ReleasePointerCaptures();
    }

    private void SeekTo(PointerRoutedEventArgs e)
    {
        var width = SeekTrack.ActualWidth;
        if (width <= 0) return;

        var share = Math.Clamp(e.GetCurrentPoint(SeekTrack).Position.X / width, 0, 1);
        SeekFill.Width = width * share;
        _player.Position = TimeSpan.FromSeconds(_player.Duration.TotalSeconds * share);
    }

    /// <summary>Unter dem Zeiger wird die Leiste hell: Hier kann man klicken.</summary>
    private void OnSeekEnter(object sender, PointerRoutedEventArgs e) =>
        SeekFill.Background = Res("AccentBrush");

    private void OnSeekExit(object sender, PointerRoutedEventArgs e)
    {
        if (!_seeking) SeekFill.Background = Res("TextFillColorTertiaryBrush");
    }

    // ── Lautstärke ───────────────────────────────────────────────

    private void OnVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_player is null) return;
        _player.Volume = e.NewValue / 100.0;
        UpdateVolumeIcon();
    }

    /// <summary>Ein Klick aufs Symbol schaltet stumm und wieder zurück.</summary>
    private void OnToggleMute(object sender, RoutedEventArgs e)
    {
        if (VolumeSlider.Value > 0)
        {
            _unmutedVolume = VolumeSlider.Value;
            VolumeSlider.Value = 0;
        }
        else VolumeSlider.Value = Math.Max(5, _unmutedVolume);
    }

    private void UpdateVolumeIcon()
    {
        var v = VolumeSlider.Value;
        VolumeBtn.Content = v <= 0 ? ""
                          : v < 34 ? ""
                          : v < 67 ? ""
                          : "";
        VolumeText.Text = $"{Math.Round(v)}";
    }

    private void OnVolumeEnter(object sender, PointerRoutedEventArgs e)
    {
        _volumeClose?.Stop();
        if (VolumePopup.IsOpen) return;

        VolumePopup.PlacementTarget = VolumeBtn;
        VolumePopup.IsOpen = true;
    }

    /// <summary>
    /// Kurz warten statt sofort zu schließen: Auf dem Weg vom Symbol zum
    /// Regler verlässt der Zeiger das eine, bevor er das andere erreicht.
    /// Solange am Regler gezogen wird, bleibt er ohnehin offen.
    /// </summary>
    private void OnVolumeLeave(object sender, PointerRoutedEventArgs e)
    {
        _volumeClose ??= NewVolumeTimer();
        _volumeClose.Start();
    }

    private DispatcherTimer NewVolumeTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            if (VolumeSlider.PointerCaptures?.Count > 0) return;
            timer.Stop();
            VolumePopup.IsOpen = false;
        };
        return timer;
    }
}
