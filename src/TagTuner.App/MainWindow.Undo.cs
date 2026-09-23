using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace TagTuner.App;

// Strg+Z und „Rückgängig" in der Statuszeile: der letzte Schreibvorgang,
// ohne den Verlauf zu öffnen.

public sealed partial class MainWindow
{
    /// <summary>Der Eintrag, den „Rückgängig" in der Statuszeile gerade meint.</summary>
    private HistoryEntry? _undoable;

    private void InitUndo()
    {
        _history.Added += entry =>
        {
            _undoable = entry.CanUndo ? entry : null;
            UndoLink.Visibility = _undoable is null ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(UndoLink, _undoable?.Description);
        };
    }

    private void OnUndoShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // In einem Textfeld gehört Strg+Z dem Text: Wer sich vertippt hat,
        // will den Buchstaben zurück, nicht den letzten Schreibvorgang.
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or AutoSuggestBox or PasswordBox)
            return;

        args.Handled = true;
        _ = UndoLastAsync();
    }

    private async void OnUndoLast(object sender, RoutedEventArgs e) => await UndoLastAsync();

    /// <summary>Nimmt den jüngsten Eintrag im Verlauf zurück.</summary>
    private async Task UndoLastAsync()
    {
        var entry = _history.Entries.FirstOrDefault();
        UndoLink.Visibility = Visibility.Collapsed;
        _undoable = null;

        if (entry is null)
        {
            StatusText.Text = Strings.T("Nothing to undo.");
            return;
        }
        if (!entry.CanUndo)
        {
            StatusText.Text = Strings.T("\"{0}\" cannot be undone, its backup is gone.", entry.Description);
            return;
        }

        // Der Player darf nichts offen halten, was gleich zurückgeschrieben wird.
        _player.ReleaseIfPlaying(entry.Files.Select(f => f.Original));

        try
        {
            var result = _history.Undo(entry.Id);
            StatusText.Text = result.Failed == 0
                ? Strings.T("Undone: {0}", entry.Description)
                : Strings.T("{0} restored, {1} failed.", result.Restored, result.Failed);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        TrackArt.Reload();
        InvalidateIndex();
        foreach (var tab in _tabs.Where(t => t.Analysis is not null))
            await MergeTabAsync(tab);
    }
}
