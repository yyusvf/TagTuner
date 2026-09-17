using TagTuner.Core.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TagTuner.App;

/// <summary>
/// Der Verlauf mit Rückgängig.
///
/// Ohne diese Ansicht wären die Sicherungen wertlos: Sie werden bei jedem
/// Schreibvorgang angelegt, aber ohne Weg, sie zurückzuspielen, kosten sie
/// nur Platz.
/// </summary>
public static class HistoryDialog
{
    /// <summary>Gibt true zurück, wenn etwas zurückgespielt wurde — dann muss neu eingelesen werden.</summary>
    public static async Task<bool> ShowAsync(XamlRoot root, HistoryStore history)
    {
        var undone = false;

        var list = new StackPanel { Spacing = 8, Width = 480 };

        void Rebuild()
        {
            list.Children.Clear();

            if (history.Entries.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "Noch keine Vorgänge.",
                    Opacity = 0.6,
                    Margin = new Thickness(0, 8, 0, 8),
                });
                return;
            }

            foreach (var entry in history.Entries.Take(40))
            {
                var grid = new Grid { ColumnSpacing = 10 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Spacing = 1 };
                info.Children.Add(new TextBlock
                {
                    Text = entry.Description,
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"{entry.Timestamp:dd.MM.yyyy HH:mm} · {entry.Files.Count} Datei(en)" +
                           (entry.CanUndo ? "" : " · keine Sicherung mehr"),
                    FontSize = 11,
                    Opacity = 0.6,
                });
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var btn = new Button
                {
                    Content = "Rückgängig",
                    IsEnabled = entry.CanUndo,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (!entry.CanUndo)
                    ToolTipService.SetToolTip(btn,
                        "Die Sicherung wurde gelöscht oder ist abgelaufen.");

                btn.Click += (_, _) =>
                {
                    try
                    {
                        var r = history.Undo(entry.Id);
                        undone = true;
                        Rebuild();
                        list.Children.Insert(0, new TextBlock
                        {
                            Text = r.Failed == 0
                                ? $"{r.Restored} Datei(en) wiederhergestellt."
                                : $"{r.Restored} wiederhergestellt, {r.Failed} fehlgeschlagen.",
                            FontSize = 12,
                            Margin = new Thickness(0, 0, 0, 4),
                        });
                    }
                    catch (Exception ex)
                    {
                        list.Children.Insert(0, new TextBlock
                        {
                            Text = ex.Message,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 4),
                        });
                    }
                };
                Grid.SetColumn(btn, 1);
                grid.Children.Add(btn);

                list.Children.Add(new Border
                {
                    Padding = new Thickness(11, 9, 11, 9),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                    Child = grid,
                });
            }
        }

        Rebuild();

        var dlg = new ContentDialog
        {
            Title = "Verlauf",
            Content = new ScrollViewer { Content = list, MaxHeight = 480 },
            CloseButtonText = "Schließen",
            SecondaryButtonText = "Verlauf leeren",
            XamlRoot = root,
        };

        dlg.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;   // Dialog offen lassen, damit man das Ergebnis sieht
            history.Clear();
            Rebuild();
        };

        await dlg.ShowAsync();
        return undone;
    }
}
