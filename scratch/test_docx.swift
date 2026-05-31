import Cocoa

let html = "<h1>Hello World</h1><p>This is a <b>test</b>.</p>"
guard let data = html.data(using: .utf8) else { fatalError() }

do {
    let attrStr = try NSAttributedString(data: data, options: [.documentType: NSAttributedString.DocumentType.html], documentAttributes: nil)
    
    let docxData = try attrStr.data(from: NSRange(location: 0, length: attrStr.length), documentAttributes: [.documentType: NSAttributedString.DocumentType.wordML])
    
    let url = URL(fileURLWithPath: "/Users/wal/fichegen/scratch/test.docx")
    try docxData.write(to: url)
    print("Success: DOCX generated")
} catch {
    print("Error: \(error)")
}
