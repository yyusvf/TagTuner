using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TagTuner.Core.Settings;

namespace TagTuner.App;

/// <summary>
/// Übersetzt die festen Texte aus dem Markup.
///
/// Ein Durchlauf über den Elementbaum statt einer Bindung an jeder Stelle:
/// Die Sprache steht beim Aufbau fest und ändert sich erst beim nächsten
/// Start wieder, also reicht ein einziger Durchgang. Was nicht in der Tabelle
/// steht, bleibt unangetastet, und Platzhalter wie „#" oder „·" laufen
/// einfach durch.
/// </summary>
public static class Localizer
{
    public static void Apply(DependencyObject? root)
    {
        if (root is null) return;

        switch (root)
        {
            case TextBlock text:
                text.Text = Strings.T(text.Text);
                break;

            case TextBox box:
                box.PlaceholderText = Strings.T(box.PlaceholderText);
                break;

            case ToggleSwitch toggle:
                if (toggle.Header is string header) toggle.Header = Strings.T(header);
                if (toggle.OnContent is string on) toggle.OnContent = Strings.T(on);
                if (toggle.OffContent is string off) toggle.OffContent = Strings.T(off);
                break;

            case ContentControl control:
                if (control.Content is string content) control.Content = Strings.T(content);
                break;
        }

        if (root is FrameworkElement element
            && ToolTipService.GetToolTip(element) is string tip)
        {
            ToolTipService.SetToolTip(element, Strings.T(tip));
        }

        // ContentControl-Inhalte hängen nicht im Visual Tree, solange sie noch
        // nicht dargestellt sind. Darum beide Wege gehen.
        if (root is ContentControl host && host.Content is DependencyObject inner)
            Apply(inner);

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            Apply(VisualTreeHelper.GetChild(root, i));

        if (root is Panel panel)
            foreach (var child in panel.Children)
                Apply(child);

        if (root is Border border)
            Apply(border.Child);
    }

    /// <summary>Die Beschriftungen eines Dialogs, den es im Markup nicht gibt.</summary>
    public static void Apply(ContentDialog dialog)
    {
        dialog.Title = dialog.Title is string title ? Strings.T(title) : dialog.Title;
        dialog.PrimaryButtonText = Strings.T(dialog.PrimaryButtonText ?? "");
        dialog.SecondaryButtonText = Strings.T(dialog.SecondaryButtonText ?? "");
        dialog.CloseButtonText = Strings.T(dialog.CloseButtonText ?? "");

        if (dialog.Content is DependencyObject content) Apply(content);
    }
}
