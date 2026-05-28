import AppKit
import WebKit

public class PDFGenerator: NSObject, WKNavigationDelegate {
    
    public enum GeneratorError: Error {
        case timeout
        case printOperationFailed
        case invalidHTML
    }
    
    public enum PDFStyle: String, CaseIterable {
        case modern = "Modern"
        case classic = "Classic"
        case minimal = "Minimal"
        case academic = "Academic"
        case playful = "Playful"
        case custom = "Custom"

        var css: String {
            let baseCSS = """
                :root { --accent-color: #2c3e50; --bg-color: #ffffff; --text-color: #333333; }
                body { margin: 0; padding: 2cm 2.5cm; color: var(--text-color); background: var(--bg-color); }
                h1 { font-size: 2.2em; font-weight: 700; color: var(--accent-color); margin-top: 0; padding-bottom: 0.3em; border-bottom: 2px solid var(--accent-color); }
                h2 { font-size: 1.6em; font-weight: 600; color: var(--accent-color); margin-top: 1.5em; }
                h3 { font-size: 1.3em; font-weight: 500; color: var(--text-color); margin-top: 1.2em; }
                p, li { font-size: 1.05em; line-height: 1.6; }
                ul, ol { margin-top: 0.5em; margin-bottom: 1em; padding-left: 1.5em; }
                li { margin-bottom: 0.4em; }
                strong { font-weight: 600; color: var(--accent-color); }
                blockquote { margin: 1.5em 0; padding: 1em 1.5em; border-left: 4px solid var(--accent-color); background: #f8f9fa; font-style: italic; }
                code { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; font-size: 0.9em; background: #f1f3f5; padding: 0.2em 0.4em; border-radius: 3px; }
                pre { background: #f8f9fa; padding: 1em; border-radius: 6px; overflow-x: auto; border: 1px solid #e9ecef; }
                pre code { background: none; padding: 0; }
                table { width: 100%; border-collapse: collapse; margin: 1.5em 0; }
                th, td { border: 1px solid #dee2e6; padding: 0.75em; text-align: left; }
                th { background-color: #f8f9fa; font-weight: 600; color: var(--accent-color); }
            """
            
            switch self {
            case .modern:
                return baseCSS + """
                body { font-family: system-ui, -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
                :root { --accent-color: #0056b3; }
                """
            case .classic:
                return baseCSS + """
                body { font-family: 'Times New Roman', Times, serif; font-size: 11pt; }
                :root { --accent-color: #000000; }
                h1, h2 { text-align: center; }
                """
            case .minimal:
                return baseCSS + """
                body { font-family: 'Inter', system-ui, -apple-system, sans-serif; font-weight: 300; }
                :root { --accent-color: #212529; }
                h1 { border-bottom: none; font-weight: 300; letter-spacing: -0.02em; }
                h2, h3 { font-weight: 400; }
                """
            case .academic:
                return baseCSS + """
                body { font-family: 'Georgia', serif; text-align: justify; }
                :root { --accent-color: #1a365d; }
                h1 { font-variant: small-caps; text-align: center; border-bottom: 1px solid #1a365d; }
                h2 { font-variant: small-caps; }
                """
            case .playful:
                return baseCSS + """
                body { font-family: 'Avenir Next', 'Nunito', system-ui, sans-serif; }
                :root { --accent-color: #7c3aed; }
                h1, h2 { color: #db2777; }
                blockquote { border-left-color: #db2777; background-color: #fdf2f8; }
                th { background-color: #f3f4f6; color: #db2777; }
                """
            case .custom:
                let primaryHex = PDFGenerator.getCustomColor(forKey: "customPdfPrimaryColor", defaultHex: "#0056b3")
                let secondaryHex = PDFGenerator.getCustomColor(forKey: "customPdfSecondaryColor", defaultHex: "#6c757d")
                let font = UserDefaults.standard.string(forKey: "customPdfFont") ?? "Helvetica"
                let marginVal = UserDefaults.standard.double(forKey: "customPdfMargin")
                let marginPx = marginVal > 0 ? marginVal : 20.0
                
                return """
                :root { --accent-color: \(primaryHex); --bg-color: #ffffff; --text-color: #333333; --secondary-color: \(secondaryHex); }
                body { font-family: '\(font)', system-ui, -apple-system, sans-serif; margin: 0; padding: \(marginPx)mm; color: var(--text-color); background: var(--bg-color); }
                h1 { font-size: 2.2em; font-weight: 700; color: var(--accent-color); margin-top: 0; padding-bottom: 0.3em; border-bottom: 2px solid var(--accent-color); }
                h2 { font-size: 1.6em; font-weight: 600; color: var(--accent-color); margin-top: 1.5em; }
                h3 { font-size: 1.3em; font-weight: 500; color: var(--text-color); margin-top: 1.2em; }
                p, li { font-size: 1.05em; line-height: 1.6; }
                ul, ol { margin-top: 0.5em; margin-bottom: 1em; padding-left: 1.5em; }
                li { margin-bottom: 0.4em; }
                strong { font-weight: 600; color: var(--accent-color); }
                blockquote { margin: 1.5em 0; padding: 1em 1.5em; border-left: 4px solid var(--accent-color); background: #f8f9fa; font-style: italic; }
                code { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; font-size: 0.9em; background: #f1f3f5; padding: 0.2em 0.4em; border-radius: 3px; }
                pre { background: #f8f9fa; padding: 1em; border-radius: 6px; overflow-x: auto; border: 1px solid #e9ecef; }
                pre code { background: none; padding: 0; }
                table { width: 100%; border-collapse: collapse; margin: 1.5em 0; }
                th, td { border: 1px solid #dee2e6; padding: 0.75em; text-align: left; }
                th { background-color: #f8f9fa; font-weight: 600; color: var(--accent-color); }
                """
            }
        }
    }
    
    private static func getCustomColor(forKey key: String, defaultHex: String) -> String {
        guard let base64 = UserDefaults.standard.string(forKey: key),
              let data = Data(base64Encoded: base64) else {
            return defaultHex
        }
        do {
            if let nsColor = try NSKeyedUnarchiver.unarchivedObject(ofClass: NSColor.self, from: data) {
                if let rgbColor = nsColor.usingColorSpace(.sRGB) {
                    let r = Int(rgbColor.redComponent * 255)
                    let g = Int(rgbColor.greenComponent * 255)
                    let b = Int(rgbColor.blueComponent * 255)
                    return String(format: "#%02X%02X%02X", r, g, b)
                }
            }
        } catch {}
        return defaultHex
    }
    
    private var webView: WKWebView?
    private var hiddenWindow: NSWindow?
    private var completion: ((Result<URL, Error>) -> Void)?
    private var outputURL: URL?
    private var isRendering = false
    
    public func generatePDF(from htmlContent: String, styleName: String, outputURL: URL, completion: @escaping (Result<URL, Error>) -> Void) {
        guard !isRendering else {
            completion(.failure(GeneratorError.printOperationFailed))
            return
        }
        
        self.isRendering = true
        self.completion = completion
        self.outputURL = outputURL
        
        let style = PDFStyle(rawValue: styleName) ?? .modern
        let css = style.css
        
        let base64Content = htmlContent.data(using: .utf8)?.base64EncodedString() ?? ""
        
        let htmlTemplate = """
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="utf-8">
            <style>
                \(css)
            </style>
        </head>
        <body>
            <div id="content"></div>
            <script>
                document.addEventListener("DOMContentLoaded", () => {
                    const base64Str = '\(base64Content)';
                    if (base64Str) {
                        try {
                            const decoded = decodeURIComponent(escape(atob(base64Str)));
                            document.getElementById('content').innerHTML = decoded;
                        } catch (e) {
                            console.error('Decoding error:', e);
                        }
                    }
                    window.location.href = "pdfgenerator://ready";
                });
            </script>
        </body>
        </html>
        """
        
        DispatchQueue.main.async {
            let config = WKWebViewConfiguration()
            let webView = WKWebView(frame: CGRect(x: 0, y: 0, width: 800, height: 1100), configuration: config)
            webView.navigationDelegate = self
            
            let window = NSWindow(contentRect: CGRect(x: 0, y: 0, width: 800, height: 1100), styleMask: .borderless, backing: .buffered, defer: false)
            window.contentView = webView
            window.isReleasedWhenClosed = false
            window.alphaValue = 0 // Invisible window
            window.makeKeyAndOrderFront(nil)
            
            self.hiddenWindow = window
            self.webView = webView
            
            webView.loadHTMLString(htmlTemplate, baseURL: URL(string: "https://localhost"))
        }
    }
    
    public func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if navigationAction.request.url?.scheme == "pdfgenerator" && navigationAction.request.url?.host == "ready" {
            decisionHandler(.cancel)
            generatePDFFromWebView(webView)
        } else {
            decisionHandler(.allow)
        }
    }
    
    public func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        finish(.failure(error))
    }
    
    public func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        finish(.failure(error))
    }
    
    private func generatePDFFromWebView(_ webView: WKWebView) {
        guard let outputURL = self.outputURL else {
            finish(.failure(GeneratorError.printOperationFailed))
            return
        }
        
        let printInfo = NSPrintInfo.shared.dictionary().mutableCopy() as! NSMutableDictionary
        printInfo[NSPrintInfo.AttributeKey.jobDisposition] = NSPrintInfo.JobDisposition.save
        printInfo[NSPrintInfo.AttributeKey.jobSavingURL] = outputURL
        
        let customPrintInfo = NSPrintInfo(dictionary: printInfo as! [NSPrintInfo.AttributeKey: Any])
        customPrintInfo.paperSize = NSSize(width: 595.2, height: 841.8) // A4
        customPrintInfo.topMargin = 0
        customPrintInfo.bottomMargin = 0
        customPrintInfo.leftMargin = 0
        customPrintInfo.rightMargin = 0
        
        let printOp = webView.printOperation(with: customPrintInfo)
        printOp.showsPrintPanel = false
        printOp.showsProgressPanel = false
        
        DispatchQueue.main.async {
            if printOp.run() {
                self.finish(.success(outputURL))
            } else {
                self.finish(.failure(GeneratorError.printOperationFailed))
            }
        }
    }
    
    private func finish(_ result: Result<URL, Error>) {
        let currentCompletion = self.completion
        self.completion = nil
        self.outputURL = nil
        self.isRendering = false
        self.webView = nil
        
        self.hiddenWindow?.close()
        self.hiddenWindow = nil
        
        currentCompletion?(result)
    }
}
