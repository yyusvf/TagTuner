using TagTuner.Core.Model;
using TagTuner.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TagTuner.App;

// „Ordner angleichen": eine Box, die vorher zeigt, welche Lieder worauf
// gebracht werden, und danach mitschreibt, was mit jedem passiert ist.

/// <summary>Wie weit ein einzelnes Lied in einer Sammelarbeit ist.</summary>
internal enum JobStep { Working, Done, Failed }

public sealed partial class MainWindow
{
    private async void OnAlign(object sender, RoutedEventArgs e)
    {
        var analysis = ActiveTab.Analysis;
        var target = ActiveTab.Target;
        if (analysis is null || target is null) return;

        var outliers = analysis.Outliers(target).ToList();
        if (outliers.Count == 0) return;

        var goal = $"{target.Format} · {FormatRate(target.SampleRate)}";

        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Strings.T("{0} file(s) will be converted to {1} · {2}.",
                             outliers.Count, target.Format, FormatRate(target.SampleRate))
                   + " " + Strings.T("The originals are replaced. Each file is backed up first, and "
                                     + "the backup can be played back from the history."),
        };

        var rows = outliers.Select(t => new AlignRow(t, goal)).ToList();
        var list = new StackPanel { Spacing = 2 };
        foreach (var row in rows) list.Children.Add(row.Element);

        var scroll = new ScrollViewer { MaxHeight = 360, Content = list, Padding = new Thickness(0, 0, 12, 0) };
        var panel = new StackPanel { Spacing = 14, Width = 520 };
        panel.Children.Add(summary);
        panel.Children.Add(scroll);

        var dialog = new ContentDialog
        {
            Title = Strings.T("Align folder"),
            Content = panel,
            PrimaryButtonText = Strings.T("Align"),
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };

        var running = false;
        var finished = false;

        // Während gearbeitet wird, bleibt die Box offen: Escape würde sie
        // sonst schließen, und das Protokoll wäre weg, bevor es fertig ist.
        dialog.Closing += (_, args) => { if (running) args.Cancel = true; };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (finished) return;   // „Schließen"
            args.Cancel = true;

            running = true;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.CloseButtonText = "";
            summary.Text = Strings.T("Converting {0} file(s)", outliers.Count) + " …";

            await RunJobAsync(Strings.T("Align {0} file(s)", outliers.Count), outliers,
                _ => (true, target.Format, target.SampleRate, null),
                step: (i, state, message) =>
                {
                    rows[i].Show(state, message);
                    if (state == JobStep.Working) rows[i].Element.StartBringIntoView();
                },
                report: false);

            var failed = rows.Count(r => r.Failed);
            summary.Text = failed == 0
                ? Strings.T("{0} file(s) processed, backup created", rows.Count - failed)
                : Strings.T("{0} of {1} file(s) converted, {2} failed.", rows.Count - failed, rows.Count, failed);

            running = false;
            finished = true;
            dialog.PrimaryButtonText = Strings.T("Close");
            dialog.IsPrimaryButtonEnabled = true;
        };

        await dialog.ShowAsync();
    }

    /// <summary>Eine Zeile im Protokoll: Zustand, Datei, von was zu was.</summary>
    private sealed class AlignRow
    {
        public Grid Element { get; }
        public bool Failed { get; private set; }

        private readonly Grid _state = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBlock _note = new()
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        public AlignRow(AudioTrack track, string goal)
        {
            Element = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 5, 4, 5) };
            Element.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Element.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Show(null, null);
            Element.Children.Add(_state);

            var text = new StackPanel { Spacing = 1 };
            Grid.SetColumn(text, 1);
            text.Children.Add(new TextBlock
            {
                Text = track.FileName,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{track.Format} · {track.SampleRateLabel}  →  {goal}",
                FontSize = 11.5,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                Foreground = Brush("TextFillColorTertiaryBrush"),
            });
            text.Children.Add(_note);
            Element.Children.Add(text);
        }

        /// <summary>Null heißt: wartet noch.</summary>
        public void Show(JobStep? state, string? message)
        {
            _state.Children.Clear();
            _state.Children.Add(state switch
            {
                JobStep.Working => new ProgressRing
                {
                    IsActive = true, Width = 14, Height = 14, MinWidth = 14, MinHeight = 14,
                    Foreground = Brush("AccentBrush"),
                },
                JobStep.Done => Icon("", "AccentBrush"),
                JobStep.Failed => Icon("", "WarnBrush"),
                _ => Icon("", "TextFillColorTertiaryBrush"),
            });

            Failed = state == JobStep.Failed;
            _note.Text = message ?? "";
            _note.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            _note.Foreground = Brush(Failed ? "WarnBrush" : "TextFillColorSecondaryBrush");
        }

        private static FontIcon Icon(string glyph, string brush) => new()
        {
            Glyph = glyph,
            FontSize = 13,
            Foreground = Brush(brush),
        };

        private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    }
}
