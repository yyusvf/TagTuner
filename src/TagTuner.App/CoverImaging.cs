using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TagTuner.App;

/// <summary>Was in einem Coverbild steckt, ohne es zu dekodieren.</summary>
public sealed record ImageInfo(uint Width, uint Height, string Format, int Bytes)
{
    public bool IsSquare => Width == Height;

    /// <summary>„JPEG", „PNG" — für die Anzeige, nicht der MIME-Typ.</summary>
    public static string ShortName(string mimeOrFormat) =>
        mimeOrFormat.ToLowerInvariant() switch
        {
            var s when s.Contains("jpeg") || s.Contains("jpg") => "JPEG",
            var s when s.Contains("png") => "PNG",
            var s when s.Contains("webp") => "WebP",
            var s when s.Contains("bmp") => "BMP",
            var s when s.Contains("gif") => "GIF",
            var s when s.Contains("tiff") => "TIFF",
            "" => "unbekannt",
            _ => mimeOrFormat.ToUpperInvariant(),
        };
}

/// <summary>
/// Zuschneiden, Skalieren und Neukodieren von Coverbildern.
///
/// Liegt in der App und nicht im Core: <c>Windows.Graphics.Imaging</c> gibt es
/// nur mit dem Windows-SDK-Zielframework, und der Core soll ohne auskommen.
/// </summary>
public static class CoverImaging
{
    public static async Task<ImageInfo?> MeasureAsync(byte[] data)
    {
        try
        {
            using var stream = ToStream(data);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return new ImageInfo(decoder.PixelWidth, decoder.PixelHeight,
                                 FormatOf(decoder.DecoderInformation.CodecId), data.Length);
        }
        catch { return null; }
    }

    /// <summary>
    /// Schneidet ein Quadrat heraus und skaliert es in einem Durchgang.
    ///
    /// Die Reihenfolge in <see cref="BitmapTransform"/> ist Skalieren, dann
    /// Zuschneiden — <see cref="BitmapTransform.Bounds"/> zählt also im
    /// skalierten Bild, nicht im Original. Darum wird der Ausschnitt hier
    /// mitskaliert statt in Originalkoordinaten übergeben.
    /// </summary>
    public static async Task<byte[]?> CropAsync(
        byte[] source, CropRect crop, uint outputSize, bool asPng, double quality = 0.9)
    {
        try
        {
            using var input = ToStream(source);
            var decoder = await BitmapDecoder.CreateAsync(input);

            var scale = outputSize / crop.Size;
            var scaledW = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
            var scaledH = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));

            var x = (uint)Math.Clamp(Math.Round(crop.X * scale), 0, Math.Max(0, scaledW - 1));
            var y = (uint)Math.Clamp(Math.Round(crop.Y * scale), 0, Math.Max(0, scaledH - 1));
            var side = Math.Min(outputSize, Math.Min(scaledW - x, scaledH - y));

            var transform = new BitmapTransform
            {
                ScaledWidth = scaledW,
                ScaledHeight = scaledH,
                Bounds = new BitmapBounds { X = x, Y = y, Width = side, Height = side },
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            return await EncodeAsync(decoder, transform, asPng, quality);
        }
        catch { return null; }
    }

    /// <summary>Verkleinert auf höchstens <paramref name="maxSize"/> Pixel Kantenlänge.</summary>
    public static async Task<byte[]?> ResizeAsync(
        byte[] source, uint maxSize, bool asPng, double quality = 0.85)
    {
        try
        {
            using var input = ToStream(source);
            var decoder = await BitmapDecoder.CreateAsync(input);

            var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);

            // Hochrechnen bringt keine Bildinformation zurück, kostet aber Platz.
            var scale = Math.Min(1.0, maxSize / (double)longest);

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            return await EncodeAsync(decoder, transform, asPng, quality);
        }
        catch { return null; }
    }

    /// <summary>Bringt beliebige Bilddaten in ein Format, das in Tags zuverlässig ankommt.</summary>
    public static Task<byte[]?> NormalizeAsync(byte[] source, bool asPng, double quality = 0.92) =>
        ResizeAsync(source, uint.MaxValue, asPng, quality);

    private static async Task<byte[]?> EncodeAsync(
        BitmapDecoder decoder, BitmapTransform transform, bool asPng, double quality)
    {
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            asPng ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        using var output = new InMemoryRandomAccessStream();

        BitmapEncoder encoder;
        if (asPng)
        {
            encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        }
        else
        {
            var options = new BitmapPropertySet
            {
                { "ImageQuality", new BitmapTypedValue(Math.Clamp(quality, 0.1, 1.0), Windows.Foundation.PropertyType.Single) },
            };
            encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, options);
        }

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            asPng ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
            transform.Bounds.Width > 0 ? transform.Bounds.Width : transform.ScaledWidth,
            transform.Bounds.Height > 0 ? transform.Bounds.Height : transform.ScaledHeight,
            decoder.DpiX, decoder.DpiY, pixels.DetachPixelData());

        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static InMemoryRandomAccessStream ToStream(byte[] data)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(data);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }
        stream.Seek(0);
        return stream;
    }

    private static string FormatOf(Guid codec) =>
        codec == BitmapDecoder.JpegDecoderId ? "JPEG"
        : codec == BitmapDecoder.PngDecoderId ? "PNG"
        : codec == BitmapDecoder.BmpDecoderId ? "BMP"
        : codec == BitmapDecoder.GifDecoderId ? "GIF"
        : codec == BitmapDecoder.TiffDecoderId ? "TIFF"
        : "unbekannt";
}

/// <summary>Ein quadratischer Ausschnitt in Originalpixeln.</summary>
public readonly record struct CropRect(double X, double Y, double Size);
