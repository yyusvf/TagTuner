using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace TagTuner.App;

/// <summary>
/// Ein Auswahlfeld, dessen Liste direkt unter ihm aufklappt, mit einem Haken
/// rechts am gewählten Eintrag. Überall in der App statt der ComboBox.
///
/// Die ComboBox legt ihre Liste so, dass der gewählte Eintrag über dem Feld
/// liegt. Bei langen Listen wie den Sprachen rückt sie dafür bis an den
/// oberen Rand, und das Gewählte steht weit weg vom Feld.
///
/// Nach außen wie eine ComboBox mit Texten: SelectedIndex, SelectedItem und
/// SelectionChanged, auch wenn der Code die Auswahl setzt. -1 heißt nichts
/// gewählt, das Feld bleibt dann leer.
/// </summary>
public sealed class Dropdown : DropDownButton
{
    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly MenuFlyout _menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
    private List<string> _items = [];
    private int _index = -1;

    public event SelectionChangedEventHandler? SelectionChanged;

    public Dropdown()
    {
        Content = _text;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Flyout = _menu;

        // Mindestens so breit wie das Feld, damit die Liste wie seine
        // Fortsetzung aussieht. Einmal gesetzt: Den Stil beim Aufklappen
        // auszutauschen brachte WinUI zum Absturz.
        SizeChanged += (_, e) =>
        {
            if (_menu.IsOpen || e.NewSize.Width <= 0) return;
            var style = new Style(typeof(MenuFlyoutPresenter));
            style.Setters.Add(new Setter(MinWidthProperty, e.NewSize.Width));
            _menu.MenuFlyoutPresenterStyle = style;
        };
    }

    public IReadOnlyList<string> Items
    {
        get => _items;
        set
        {
            _items = [.. value];
            _menu.Items.Clear();
            for (var i = 0; i < _items.Count; i++)
            {
                var at = i;
                var item = new MenuFlyoutItem { Text = _items[i] };
                item.Click += (_, _) => SelectedIndex = at;
                _menu.Items.Add(item);
            }
            if (_index >= _items.Count) _index = -1;
            Show();
        }
    }

    public int SelectedIndex
    {
        get => _index;
        set
        {
            var next = value >= 0 && value < _items.Count ? value : -1;
            if (next == _index) return;
            _index = next;
            Show();
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs([], []));
        }
    }

    /// <summary>Der gewählte Text; setzen wählt den gleichnamigen Eintrag, null leert.</summary>
    public string? SelectedItem
    {
        get => _index >= 0 ? _items[_index] : null;
        set => SelectedIndex = value is null
            ? -1
            : _items.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
    }

    private void Show()
    {
        _text.Text = SelectedItem ?? "";

        // Der Haken steht rechts, an der Stelle, an der Menüs ihre
        // Tastenkürzel zeigen, statt als Häkchen-Spalte links vor jedem Text.
        for (var i = 0; i < _menu.Items.Count; i++)
        {
            if (_menu.Items[i] is MenuFlyoutItem item)
                item.KeyboardAcceleratorTextOverride = i == _index ? "✓" : "";
        }
    }
}
