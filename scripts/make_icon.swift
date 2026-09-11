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

func drawText(_ text: String, pixels: CGFloat, center: NSPoint, size: CGFloat, weight: NSFont.Weight, color: NSColor) {
    guard pixels >= 64 else { return }
    let string = text as NSString
    let attrs: [NSAttributedString.Key: Any] = [
        .font: NSFont.monospacedSystemFont(ofSize: size, weight: weight),
        .foregroundColor: color
    ]
    let textSize = string.size(withAttributes: attrs)
    string.draw(
        at: NSPoint(x: center.x - textSize.width * 0.5, y: center.y - textSize.height * 0.5),
        withAttributes: attrs
    )
}

func drawArrow(from start: NSPoint, to end: NSPoint, width: CGFloat, color: NSColor) {
    color.setStroke()
    let path = NSBezierPath()
    path.lineWidth = width
    path.lineCapStyle = .round
    path.lineJoinStyle = .round
    path.move(to: start)
    path.line(to: end)
    path.stroke()

    color.setFill()
    let head = NSBezierPath()
    let headSize = width * 2.8
    head.move(to: end)
    head.line(to: NSPoint(x: end.x - headSize, y: end.y + headSize * 0.64))
    head.line(to: NSPoint(x: end.x - headSize * 0.58, y: end.y))
    head.line(to: NSPoint(x: end.x - headSize, y: end.y - headSize * 0.64))
    head.close()
    head.fill()
}

for variant in variants {
    let p = variant.pixels
    let image = NSImage(size: NSSize(width: p, height: p))
    image.lockFocus()

    let bounds = NSRect(x: 0, y: 0, width: p, height: p)
    let outer = bounds.insetBy(dx: p * 0.035, dy: p * 0.035)
    let outerPath = rounded(outer, p * 0.235)

    c(0.12, 0.12, 0.11).setFill()
    outerPath.fill()

    c(1, 1, 1, 0.18).setStroke()
    outerPath.lineWidth = max(1, p * 0.010)
    outerPath.stroke()

    let discRect = NSRect(x: p * 0.16, y: p * 0.30, width: p * 0.38, height: p * 0.38)
    c(0.93, 0.91, 0.87).setFill()
    NSBezierPath(ovalIn: discRect).fill()
    c(0.02, 0.02, 0.02, 0.94).setStroke()
    let discOutline = NSBezierPath(ovalIn: discRect)
    discOutline.lineWidth = max(1, p * 0.014)
    discOutline.stroke()

    let center = NSRect(x: p * 0.29, y: p * 0.43, width: p * 0.12, height: p * 0.12)
    c(0.12, 0.12, 0.11).setFill()
    NSBezierPath(ovalIn: center).fill()

    let note = "♪" as NSString
    let noteAttrs: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: p * 0.20, weight: .bold),
        .foregroundColor: c(0.06, 0.06, 0.06, 0.96)
    ]
    let noteSize = note.size(withAttributes: noteAttrs)
    if p >= 64 {
        note.draw(
            at: NSPoint(x: discRect.midX - noteSize.width * 0.5, y: discRect.midY - noteSize.height * 0.43),
            withAttributes: noteAttrs
        )
    }

    let fileRect = NSRect(x: p * 0.62, y: p * 0.34, width: p * 0.22, height: p * 0.28)
    let filePath = rounded(fileRect, p * 0.055)
    c(0.93, 0.91, 0.87).setFill()
    filePath.fill()
    c(0.02, 0.02, 0.02, 0.94).setStroke()
    filePath.lineWidth = max(1, p * 0.012)
    filePath.stroke()

    c(0.12, 0.12, 0.11).setStroke()
    for index in 0..<3 {
        let y = fileRect.minY + p * (0.075 + CGFloat(index) * 0.055)
        let line = NSBezierPath()
        line.lineWidth = max(1, p * 0.012)
        line.lineCapStyle = .round
        line.move(to: NSPoint(x: fileRect.minX + p * 0.052, y: y))
        line.line(to: NSPoint(x: fileRect.maxX - p * 0.052, y: y))
        line.stroke()
    }

    drawArrow(
        from: NSPoint(x: p * 0.51, y: p * 0.49),
        to: NSPoint(x: p * 0.62, y: p * 0.49),
        width: max(1.2, p * 0.018),
        color: c(0.93, 0.91, 0.87, 0.94)
    )

    image.unlockFocus()

    guard let tiff = image.tiffRepresentation,
          let bitmap = NSBitmapImageRep(data: tiff),
          let png = bitmap.representation(using: .png, properties: [:]) else {
        fatalError("Could not render icon")
    }
    try png.write(to: output.appendingPathComponent(variant.name))
}
