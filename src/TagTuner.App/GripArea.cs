using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace TagTuner.App;

/// <summary>
/// Die anfassbare Fläche zwischen zwei Panelspalten.
///
/// Eigene Klasse nur wegen des Mauszeigers: <c>ProtectedCursor</c> ist auf
/// UIElement geschützt, ein Window kommt also nicht daran. Wer den Zeiger
/// ändern will, muss das Element selbst sein.
/// </summary>
public sealed class GripArea : Grid
{
    public void ShowResizeCursor(bool on) =>
        ProtectedCursor = on
            ? InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast)
            : null;
}
