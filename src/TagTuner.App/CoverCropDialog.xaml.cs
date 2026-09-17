using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace TagTuner.App;

/// <summary>
/// Der quadratische Ausschnitt für ein Cover, das nicht quadratisch ist.
///
/// Gerechnet wird durchgehend in Bühnenkoordinaten — also in dem, was man
/// sieht — und erst beim Übernehmen in Originalpixel umgerechnet. Andersherum
/// müsste bei jedem Ziehen hin und zurück gerechnet werden, und die
/// Rundungsfehler summierten sich auf.
/// </summary>
public sealed partial class CoverCropDialog : ContentDialog
{
    private const double StageSize = 360;

    private readonly byte[] _source;
    private readonly ImageInfo _info;

    // Das Bild innerhalb der Bühne: gleichmäßig skaliert, zentriert.
    private readonly double _imageX, _imageY, _imageW, _imageH;

    // Die Auswahl, ebenfalls in Bühnenkoordinaten.
    private double _cropX, _cropY, _cropSide;

    private bool _dragging;
    private Point _grabOffset;

    /// <summary>
    /// Erst wahr, wenn der Aufbau durch ist.
    ///
    /// <c>InitializeComponent</c> setzt den Schieber auf seinen Startwert, und
    /// der meldet ein ValueChanged — zu einem Zeitpunkt, an dem es noch keine
    /// Bildmaße gibt. Ohne diese Sperre lief der Aufbau in eine
    /// NullReferenceException und der Dialog erschien einfach nicht.
    /// </summary>
    private bool _ready;

    public CoverCropDialog(byte[] source, ImageInfo info)
    {
        InitializeComponent();
        _source = source;
        _info = info;

        var scale = Math.Min(StageSize / info.Width, StageSize / info.Height);
        _imageW = info.Width * scale;
        _imageH = info.Height * scale;
        _imageX = (StageSize - _imageW) / 2;
        _imageY = (StageSize - _imageH) / 2;

        Preview.Source = Load(source);
        Preview.Width = _imageW;
        Preview.Height = _imageH;
        Preview.HorizontalAlignment = HorizontalAlignment.Left;
        Preview.VerticalAlignment = VerticalAlignment.Top;
        Preview.Margin = new Thickness(_imageX, _imageY, 0, 0);

        // Größtmögliches Quadrat, mittig — das ist fast immer der gewünschte
        // Ausschnitt und spart im Normalfall jedes Ziehen.
        _cropSide = Math.Min(_imageW, _imageH);
        _cropX = _imageX + (_imageW - _cropSide) / 2;
        _cropY = _imageY + (_imageH - _cropSide) / 2;

        _ready = true;
        Redraw();
    }

    /// <summary>Der gewählte Ausschnitt in Originalpixeln.</summary>
    public CropRect Selection
    {
        get
        {
            var toSource = _info.Width / _imageW;
            return new CropRect(
                Math.Max(0, (_cropX - _imageX) * toSource),
                Math.Max(0, (_cropY - _imageY) * toSource),
                Math.Max(1, _cropSide * toSource));
        }
    }

    private static BitmapImage Load(byte[] data)
    {
        var bmp = new BitmapImage();
        try
        {
            using var ms = new MemoryStream(data);
            bmp.SetSource(ms.AsRandomAccessStream());
        }
        catch { }
        return bmp;
    }

    // ── Ziehen ───────────────────────────────────────────────────

    private void OnStagePressed(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Stage).Position;

        // Außerhalb des Rahmens: dorthin springen und von da weiterziehen.
        if (p.X < _cropX || p.X > _cropX + _cropSide || p.Y < _cropY || p.Y > _cropY + _cropSide)
        {
            Move(p.X - _cropSide / 2, p.Y - _cropSide / 2);
            _grabOffset = new Point(_cropSide / 2, _cropSide / 2);
        }
        else
        {
            _grabOffset = new Point(p.X - _cropX, p.Y - _cropY);
        }

        _dragging = true;
        Stage.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnStageMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetCurrentPoint(Stage).Position;
        Move(p.X - _grabOffset.X, p.Y - _grabOffset.Y);
        e.Handled = true;
    }

    private void OnStageReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Stage.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnSizeChanged(
        object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;

        // Um die Mitte wachsen lassen, sonst wandert der Ausschnitt beim
        // Verkleinern nach rechts unten aus dem Motiv heraus.
        var centerX = _cropX + _cropSide / 2;
        var centerY = _cropY + _cropSide / 2;

        _cropSide = Math.Min(_imageW, _imageH) * e.NewValue / 100.0;
        Move(centerX - _cropSide / 2, centerY - _cropSide / 2);
    }

    /// <summary>Verschiebt die Auswahl und hält sie innerhalb des Bildes.</summary>
    private void Move(double x, double y)
    {
        _cropX = Math.Clamp(x, _imageX, _imageX + _imageW - _cropSide);
        _cropY = Math.Clamp(y, _imageY, _imageY + _imageH - _cropSide);
        Redraw();
    }

    private void Redraw()
    {
        Frame.Width = _cropSide;
        Frame.Height = _cropSide;
        Frame.Margin = new Thickness(_cropX, _cropY, 0, 0);

        Shade(ShadeTop, 0, 0, StageSize, _cropY);
        Shade(ShadeBottom, 0, _cropY + _cropSide, StageSize, StageSize - _cropY - _cropSide);
        Shade(ShadeLeft, 0, _cropY, _cropX, _cropSide);
        Shade(ShadeRight, _cropX + _cropSide, _cropY, StageSize - _cropX - _cropSide, _cropSide);

        var pixels = (int)Math.Round(Selection.Size);
        SizeLabel.Text = $"Ausschnitt: {pixels} × {pixels} Pixel";
        ResultLabel.Text = $"Original {_info.Width} × {_info.Height} · {_info.Format}";

        static void Shade(Microsoft.UI.Xaml.Shapes.Rectangle r,
                          double x, double y, double w, double h)
        {
            r.Width = Math.Max(0, w);
            r.Height = Math.Max(0, h);
            r.Margin = new Thickness(x, y, 0, 0);
        }
    }

    /// <summary>
    /// Zeigt den Dialog und liefert das zugeschnittene Bild — oder null, wenn
    /// abgebrochen wurde.
    /// </summary>
    public static async Task<byte[]?> CropAsync(
        XamlRoot root, byte[] source, ImageInfo info, bool asPng)
    {
        var dialog = new CoverCropDialog(source, info) { XamlRoot = root };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var crop = dialog.Selection;
        var side = (uint)Math.Max(1, Math.Round(crop.Size));
        return await CoverImaging.CropAsync(source, crop, side, asPng);
    }
}
