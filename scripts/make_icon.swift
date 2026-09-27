import AppKit
import Foundation

let output = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

struct IconVariant {
    let name: String
    let pixels: CGFloat
}

let variants: [IconVariant] = [
    .init(name: "icon_16x16.png", pixels: 16),
    .init(name: "icon_16x16@2x.png", pixels: 32),
    .init(name: "icon_32x32.png", pixels: 32),
    .init(name: "icon_32x32@2x.png", pixels: 64),
    .init(name: "icon_128x128.png", pixels: 128),
    .init(name: "icon_128x128@2x.png", pixels: 256),
    .init(name: "icon_256x256.png", pixels: 256),
    .init(name: "icon_256x256@2x.png", pixels: 512),
    .init(name: "icon_512x512.png", pixels: 512),
    .init(name: "icon_512x512@2x.png", pixels: 1024)
]

func c(_ red: CGFloat, _ green: CGFloat, _ blue: CGFloat, _ alpha: CGFloat = 1) -> NSColor {
    NSColor(calibratedRed: red, green: green, blue: blue, alpha: alpha)
}

func rounded(_ rect: NSRect, _ radius: CGFloat) -> NSBezierPath {
    NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
}

func drawArrow(from start: NSPoint, to end: NSPoint, width: CGFloat, color: NSColor) {
    color.setStroke()
    let path = NSBezierPath()
    path.lineWidth = width
    path.lineCapStyle = .round
    path.lineJoinStyle = .round
    path.move(to: start)
    path.line(to: end)
    let headSize = width * 1.8
    path.move(to: NSPoint(x: end.x - headSize, y: end.y + headSize))
    path.line(to: end)
    path.line(to: NSPoint(x: end.x - headSize, y: end.y - headSize))
    path.stroke()
}

func drawNote(in disc: NSRect, color: NSColor) {
    NSGraphicsContext.saveGraphicsState()
    let transform = NSAffineTransform()
    transform.translateX(by: disc.minX, yBy: disc.minY)
    transform.scaleX(by: disc.width, yBy: disc.height)
    transform.concat()

    color.setFill()
    NSBezierPath(ovalIn: NSRect(x: 0.28, y: 0.23, width: 0.28, height: 0.19)).fill()
    NSBezierPath(rect: NSRect(x: 0.48, y: 0.33, width: 0.08, height: 0.43)).fill()
    let note = NSBezierPath()
    note.move(to: NSPoint(x: 0.53, y: 0.76))
    note.curve(to: NSPoint(x: 0.72, y: 0.58), controlPoint1: NSPoint(x: 0.55, y: 0.66), controlPoint2: NSPoint(x: 0.76, y: 0.69))
    note.curve(to: NSPoint(x: 0.65, y: 0.49), controlPoint1: NSPoint(x: 0.73, y: 0.54), controlPoint2: NSPoint(x: 0.69, y: 0.51))
    note.curve(to: NSPoint(x: 0.53, y: 0.63), controlPoint1: NSPoint(x: 0.69, y: 0.60), controlPoint2: NSPoint(x: 0.58, y: 0.60))
    note.close()
    note.fill()
    NSGraphicsContext.restoreGraphicsState()
}

for variant in variants {
    let p = variant.pixels
    // lockFocus uses the display's backing scale; icon files need exact pixel sizes.
    guard let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: Int(p), pixelsHigh: Int(p),
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    ), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
        fatalError("Could not create icon bitmap")
    }
    bitmap.size = NSSize(width: p, height: p)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    context.shouldAntialias = true

    let bounds = NSRect(x: 0, y: 0, width: p, height: p)
    context.cgContext.clear(bounds)
    let outer = bounds.insetBy(dx: p * 0.045, dy: p * 0.045)
    let outerPath = rounded(outer, p * 0.245)

    NSGraphicsContext.saveGraphicsState()
    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.12)
    shadow.shadowOffset = NSSize(width: 0, height: -p * 0.018)
    shadow.shadowBlurRadius = p * 0.038
    shadow.set()
    c(0.98, 0.975, 0.955).setFill()
    outerPath.fill()
    NSGraphicsContext.restoreGraphicsState()

    c(0.10, 0.18, 0.20, 0.16).setStroke()
    outerPath.lineWidth = max(0.5, p * 0.008)
    outerPath.stroke()

    let discRect = NSRect(x: p * 0.12, y: p * 0.32, width: p * 0.36, height: p * 0.36)
    c(0.79, 0.92, 0.93).setFill()
    NSBezierPath(ovalIn: discRect).fill()
    c(0.08, 0.20, 0.23, 0.72).setStroke()
    let discOutline = NSBezierPath(ovalIn: discRect)
    discOutline.lineWidth = max(0.6, p * 0.012)
    discOutline.stroke()

    drawNote(in: discRect, color: c(0.07, 0.18, 0.21))

    let fileRect = NSRect(x: p * 0.67, y: p * 0.35, width: p * 0.20, height: p * 0.30)
    let filePath = rounded(fileRect, p * 0.050)
    c(1, 1, 1, 0.96).setFill()
    filePath.fill()
    c(0.11, 0.45, 0.50, 0.88).setStroke()
    filePath.lineWidth = max(0.6, p * 0.011)
    filePath.stroke()

    c(0.11, 0.45, 0.50, 0.72).setStroke()
    for index in 0..<2 {
        let line = NSBezierPath()
        line.lineWidth = max(0.6, p * 0.011)
        line.lineCapStyle = .round
        let y = fileRect.minY + p * (0.10 + CGFloat(index) * 0.070)
        line.move(to: NSPoint(x: fileRect.minX + p * 0.052, y: y))
        line.line(to: NSPoint(x: fileRect.maxX - p * 0.052, y: y))
        line.stroke()
    }

    drawArrow(
        from: NSPoint(x: p * 0.525, y: p * 0.50),
        to: NSPoint(x: p * 0.62, y: p * 0.50),
        width: max(0.65, p * 0.018),
        color: c(0.08, 0.50, 0.55)
    )

    context.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()

    guard let png = bitmap.representation(using: .png, properties: [:]) else {
        fatalError("Could not render icon")
    }
    try png.write(to: output.appendingPathComponent(variant.name))
}
