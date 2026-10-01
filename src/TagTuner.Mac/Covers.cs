using ImageIO;

namespace TagTuner.Mac;

/// <summary>
/// Bilder für Cover. Über ImageIO statt NSImage, weil das auch im Hintergrund
/// sicher läuft und gleich verkleinert dekodiert, statt ein 3000er-Cover in
/// voller Größe für ein 22-Punkt-Kästchen zu entpacken.
/// </summary>
internal static class Covers
{
    public static NSImage? Thumbnail(byte[]? data, int maxPixels)
    {
        if (data is not { Length: > 0 }) return null;
        try
        {
            using var src = CGImageSource.FromData(NSData.FromArray(data));
            if (src is null) return null;
            var opts = new CGImageThumbnailOptions
            {
                CreateThumbnailFromImageAlways = true,
                MaxPixelSize = maxPixels,
                CreateThumbnailWithTransform = true,
            };
            using var cg = src.CreateThumbnail(0, opts);
            return cg is null ? null : new NSImage(cg, new CGSize(cg.Width, cg.Height));
        }
        catch { return null; }
    }

    public static NSImage? Full(byte[]? data) =>
        data is { Length: > 0 } ? new NSImage(NSData.FromArray(data)) : null;

    /// <summary>Breite und Höhe in Pixeln, ohne das Bild zu dekodieren.</summary>
    public static (int Width, int Height)? Size(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;
        try
        {
            using var src = CGImageSource.FromData(NSData.FromArray(data));
            var props = src?.GetProperties(0, new CGImageOptions());
            return props is { PixelWidth: { } w, PixelHeight: { } h } ? (w, h) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Macht aus einer Bilddatei oder einem Bild aus der Zwischenablage die
    /// Bytes fürs Cover. JPEG und PNG bleiben, wie sie sind; alles andere
    /// (HEIC, TIFF aus der Zwischenablage) wird zu JPEG, weil nicht jeder
    /// Player mehr versteht.
    /// </summary>
    public static (byte[] Data, string Mime)? Prepare(NSData data)
    {
        var bytes = data.ToArray();
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) return (bytes, "image/jpeg");
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50) return (bytes, "image/png");

        var rep = NSBitmapImageRep.ImageRepFromData(data) as NSBitmapImageRep
                  ?? (new NSImage(data).Representations().OfType<NSBitmapImageRep>().FirstOrDefault());
        if (rep is null)
        {
            var img = new NSImage(data);
            if (img.CGImage is not { } cg) return null;
            rep = new NSBitmapImageRep(cg);
        }
        var jpeg = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Jpeg,
            new NSDictionary(NSBitmapImageRep.CompressionFactor, NSNumber.FromDouble(0.9)));
        return jpeg is null ? null : (jpeg.ToArray(), "image/jpeg");
    }

    /// <summary>Das volle Bild als CGImage, Drehung aus den EXIF-Angaben schon angewandt.</summary>
    public static CGImage? Decode(byte[] data)
    {
        try
        {
            using var src = CGImageSource.FromData(NSData.FromArray(data));
            if (src is null) return null;
            var size = Size(data);
            var opts = new CGImageThumbnailOptions
            {
                CreateThumbnailFromImageAlways = true,
                MaxPixelSize = Math.Max(size?.Width ?? 4000, size?.Height ?? 4000),
                CreateThumbnailWithTransform = true,
            };
            return src.CreateThumbnail(0, opts);
        }
        catch { return null; }
    }

    /// <summary>
    /// Ein Quadrat aus dem Bild, auf höchstens <paramref name="maxEdge"/>
    /// Pixel verkleinert und neu kodiert. PNG bleibt PNG (Transparenz),
    /// alles andere wird JPEG.
    /// </summary>
    public static (byte[] Data, string Mime)? Render(CGImage image, CGRect source, int maxEdge, bool asPng, double quality = 0.92)
    {
        var edge = (int)Math.Min(maxEdge, Math.Round(Math.Max(source.Width, source.Height)));
        var w = (int)Math.Round(source.Width * edge / Math.Max(source.Width, source.Height));
        var h = (int)Math.Round(source.Height * edge / Math.Max(source.Width, source.Height));
        if (w < 1 || h < 1) return null;

        using var cropped = image.WithImageInRect(source);
        if (cropped is null) return null;
        using var space = CGColorSpace.CreateSrgb();
        using var ctx = new CGBitmapContext(null, w, h, 8, 0, space, CGImageAlphaInfo.PremultipliedLast);
        ctx.InterpolationQuality = CGInterpolationQuality.High;
        ctx.DrawImage(new CGRect(0, 0, w, h), cropped);
        using var result = ctx.ToImage();
        if (result is null) return null;

        var rep = new NSBitmapImageRep(result);
        var bytes = asPng
            ? rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary())
            : rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Jpeg,
                new NSDictionary(NSBitmapImageRep.CompressionFactor, NSNumber.FromDouble(quality)));
        return bytes is null ? null : (bytes.ToArray(), asPng ? "image/png" : "image/jpeg");
    }

    public static bool IsPng(byte[] data) => data.Length > 8 && data[0] == 0x89 && data[1] == 0x50;
}
