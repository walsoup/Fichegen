import Cocoa

let size = NSSize(width: 600, height: 400)
let image = NSImage(size: size)
image.lockFocus()

guard let context = NSGraphicsContext.current?.cgContext else {
    exit(1)
}

// 1. Draw solid background
// Clean, modern Apple-like off-white background
context.setFillColor(NSColor(red: 245.0/255.0, green: 245.0/255.0, blue: 247.0/255.0, alpha: 1.0).cgColor)
context.fill(CGRect(x: 0, y: 0, width: 600, height: 400))

// 2. Draw clean simple arrow
context.saveGState()
let arrowPath = CGMutablePath()
let arrowStart = CGPoint(x: 230, y: 220)
let arrowEnd = CGPoint(x: 370, y: 220)

arrowPath.move(to: arrowStart)
arrowPath.addLine(to: arrowEnd)

// Clean thin gray arrow
context.setStrokeColor(NSColor(white: 0.75, alpha: 1.0).cgColor)
context.setLineWidth(2)
context.setLineCap(.round)
context.addPath(arrowPath)
context.strokePath()

// Arrow head
context.beginPath()
let headAngle: CGFloat = CGFloat.pi / 6 // 30 degrees
let arrowHeadLength: CGFloat = 12

let p1 = CGPoint(
    x: arrowEnd.x - arrowHeadLength * cos(headAngle),
    y: arrowEnd.y - arrowHeadLength * sin(headAngle)
)
let p2 = CGPoint(
    x: arrowEnd.x - arrowHeadLength * cos(-headAngle),
    y: arrowEnd.y - arrowHeadLength * sin(-headAngle)
)

context.move(to: arrowEnd)
context.addLine(to: p1)
context.addLine(to: p2)
context.closePath()
context.setFillColor(NSColor(white: 0.75, alpha: 1.0).cgColor)
context.fillPath()
context.restoreGState()

// 3. Draw Typography
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

// Title
drawText("FicheGen", point: CGPoint(x: 300, y: 340), fontSize: 28, isBold: true, color: NSColor(white: 0.1, alpha: 1.0))
drawText("v3.3.8", point: CGPoint(x: 300, y: 310), fontSize: 16, isBold: false, color: NSColor(white: 0.4, alpha: 1.0))

// Instruction Text
drawText("Glissez FicheGen pour installer", point: CGPoint(x: 300, y: 80), fontSize: 14, isBold: true, color: NSColor(white: 0.3, alpha: 1.0))
drawText("Drag FicheGen to install", point: CGPoint(x: 300, y: 55), fontSize: 12, isBold: false, color: NSColor(white: 0.5, alpha: 1.0))

image.unlockFocus()

guard let tiffData = image.tiffRepresentation,
      let bitmap = NSBitmapImageRep(data: tiffData),
      let pngData = bitmap.representation(using: .png, properties: [:]) else {
    exit(1)
}

try! pngData.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
print("Background generated successfully at \\(CommandLine.arguments[1])")
