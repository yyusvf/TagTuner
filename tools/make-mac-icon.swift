// Zeichnet das App-Symbol für macOS aus derselben Form wie site/favicon.svg:
// grüne Fläche mit runden Ecken, darauf die Welle. Anders als unter Windows
// nicht randlos, sondern im Raster von macOS: eine 824er Fläche mittig auf
// 1024, mit leichtem Schatten. Jede Größe wird eigens gezeichnet, damit auch
// 16 Punkte scharf bleiben.
//
//   swift tools/make-mac-icon.swift src/TagTuner.Mac/Assets.xcassets/AppIcon.appiconset

import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let lime = NSColor(srgbRed: 0xC8 / 255.0, green: 0xF5 / 255.0, blue: 0x42 / 255.0, alpha: 1)
let ink = NSColor(srgbRed: 0x11 / 255.0, green: 0x14 / 255.0, blue: 0x0A / 255.0, alpha: 1)

func render(_ px: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let ctx = NSGraphicsContext.current!.cgContext

    // Alles in den Maßen der SVG (512) denken, auf 1024 mit 100er Rand setzen.
    let k = CGFloat(px) / 1024
    let body = CGRect(x: 100 * k, y: 100 * k, width: 824 * k, height: 824 * k)
    let s = 824 * k / 512

    // Schatten nur ab mittlerer Größe; bei 16 Punkten wäre er Matsch.
    if px >= 64 {
        ctx.setShadow(offset: CGSize(width: 0, height: -10 * k), blur: 28 * k,
                      color: NSColor.black.withAlphaComponent(0.3).cgColor)
    }
    let shape = NSBezierPath(roundedRect: body, xRadius: 112 * s, yRadius: 112 * s)
    lime.setFill()
    shape.fill()
    ctx.setShadow(offset: .zero, blur: 0, color: nil)

    // Die Welle aus der SVG. Deren y wächst nach unten, hier nach oben.
    func p(_ x: CGFloat, _ y: CGFloat) -> NSPoint { NSPoint(x: body.minX + x * s, y: body.maxY - y * s) }
    let wave = NSBezierPath()
    wave.move(to: p(72, 262))
    wave.curve(to: p(112, 200), controlPoint1: p(88, 222), controlPoint2: p(98, 200))
    var x: CGFloat = 112
    for i in 0..<4 {
        let down = i % 2 == 0
        let y1: CGFloat = down ? 200 : 300, y2: CGFloat = down ? 300 : 200
        wave.curve(to: p(x + 72, y2), controlPoint1: p(x + 24, y1), controlPoint2: p(x + 48, y2))
        x += 72
    }
    wave.curve(to: p(440, 262), controlPoint1: p(414, 200), controlPoint2: p(424, 222))
    wave.lineWidth = 40 * s
    wave.lineCapStyle = .round
    wave.lineJoinStyle = .round
    ink.setStroke()
    wave.stroke()

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

for size in [16, 32, 64, 128, 256, 512, 1024] {
    try! render(size).write(to: URL(fileURLWithPath: "\(out)/Icon\(size).png"))
}
print("Symbole in \(out)")
