import Cocoa

let size = NSSize(width: 600, height: 400)
let image = NSImage(size: size)
image.lockFocus()

guard let context = NSGraphicsContext.current?.cgContext else {
    exit(1)
}

// 1. Draw background gradient
let colorSpace = CGColorSpaceCreateDeviceRGB()
let colors = [
    NSColor(red: 12.0/255.0, green: 16.0/255.0, blue: 32.0/255.0, alpha: 1.0).cgColor, // #0c1020 (Dark Navy)
    NSColor(red: 29.0/255.0, green: 78.0/255.0, blue: 216.0/255.0, alpha: 1.0).cgColor  // #1d4ed8 (Blue 700)
] as CFArray
let locations: [CGFloat] = [0.0, 1.0]
guard let gradient = CGGradient(colorsSpace: colorSpace, colors: colors, locations: locations) else {
    exit(1)
}
context.drawLinearGradient(gradient, start: CGPoint(x: 0, y: 400), end: CGPoint(x: 600, y: 0), options: [])

// 2. Draw Target Rings
let ringRadius: CGFloat = 55
let leftCenter = CGPoint(x: 150, y: 220)
let rightCenter = CGPoint(x: 450, y: 220)

func drawRing(at center: CGPoint, label: String) {
    context.saveGState()
    
    // Draw outer glow/shadow
    context.setStrokeColor(NSColor.white.withAlphaComponent(0.15).cgColor)
    context.setLineWidth(4)
    context.strokeEllipse(in: CGRect(x: center.x - ringRadius, y: center.y - ringRadius, width: ringRadius*2, height: ringRadius*2))
    
    // Draw dashed outline
    context.setStrokeColor(NSColor(red: 59.0/255.0, green: 130.0/255.0, blue: 246.0/255.0, alpha: 0.6).cgColor) // Blue 500
    context.setLineWidth(2)
    context.setLineDash(phase: 0, lengths: [6, 4])
    context.strokeEllipse(in: CGRect(x: center.x - ringRadius, y: center.y - ringRadius, width: ringRadius*2, height: ringRadius*2))
    
    context.restoreGState()
}

drawRing(at: leftCenter, label: "FicheGen")
drawRing(at: rightCenter, label: "Applications")

// 3. Draw Curving Arrow
context.saveGState()
let arrowPath = CGMutablePath()
// Start arrow after left ring and end before right ring
let arrowStart = CGPoint(x: 215, y: 220)
let arrowEnd = CGPoint(x: 385, y: 220)
let controlPoint = CGPoint(x: 300, y: 260) // Curve upwards

arrowPath.move(to: arrowStart)
arrowPath.addQuadCurve(to: arrowEnd, control: controlPoint)

// Set arrow style: wide gradient stroke
context.setStrokeColor(NSColor(red: 59.0/255.0, green: 130.0/255.0, blue: 246.0/255.0, alpha: 0.8).cgColor) // Blue 500
context.setLineWidth(4)
context.setLineCap(.round)
context.addPath(arrowPath)
context.strokePath()

// Draw arrow head
context.beginPath()
let headAngle: CGFloat = CGFloat.pi / 6 // 30 degrees
let arrowHeadLength: CGFloat = 16
// Calculate tangent at the end of the curve
let dx = arrowEnd.x - controlPoint.x
let dy = arrowEnd.y - controlPoint.y
let angle = atan2(dy, dx)

let p1 = CGPoint(
    x: arrowEnd.x - arrowHeadLength * cos(angle - headAngle),
    y: arrowEnd.y - arrowHeadLength * sin(angle - headAngle)
)
let p2 = CGPoint(
    x: arrowEnd.x - arrowHeadLength * cos(angle + headAngle),
    y: arrowEnd.y - arrowHeadLength * sin(angle + headAngle)
)

context.move(to: arrowEnd)
context.addLine(to: p1)
context.addLine(to: p2)
context.closePath()
context.setFillColor(NSColor(red: 59.0/255.0, green: 130.0/255.0, blue: 246.0/255.0, alpha: 0.95).cgColor)
context.fillPath()
context.restoreGState()

// 4. Draw Typography
func drawText(_ text: String, point: CGPoint, fontSize: CGFloat, isBold: Bool, color: NSColor) {
    let font = isBold ? NSFont.boldSystemFont(ofSize: fontSize) : NSFont.systemFont(ofSize: fontSize)
    let paragraphStyle = NSMutableParagraphStyle()
    paragraphStyle.alignment = .center
    
    let attributes: [NSAttributedString.Key: Any] = [
        .font: font,
        .foregroundColor: color,
        .paragraphStyle: paragraphStyle
    ]
    
    let size = text.size(withAttributes: attributes)
    let rect = NSRect(x: point.x - size.width/2, y: point.y - size.height/2, width: size.width, height: size.height)
    text.draw(in: rect, withAttributes: attributes)
}

// Title FicheGen v3.2.6
drawText("FicheGen", point: CGPoint(x: 300, y: 340), fontSize: 28, isBold: true, color: .white)
drawText("v3.2.6", point: CGPoint(x: 300, y: 310), fontSize: 16, isBold: false, color: NSColor.white.withAlphaComponent(0.6))

// Instruction Text
drawText("Glissez FicheGen pour installer l'application", point: CGPoint(x: 300, y: 80), fontSize: 14, isBold: true, color: NSColor.white.withAlphaComponent(0.8))
drawText("Drag FicheGen to install the application", point: CGPoint(x: 300, y: 55), fontSize: 12, isBold: false, color: NSColor.white.withAlphaComponent(0.5))

image.unlockFocus()

// Save to file
guard let tiffData = image.tiffRepresentation,
      let bitmap = NSBitmapImageRep(data: tiffData),
      let pngData = bitmap.representation(using: .png, properties: [:]) else {
    exit(1)
}

try! pngData.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
print("Background generated successfully at \(CommandLine.arguments[1])")
