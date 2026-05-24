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

        var css: String {
            switch self {
            case .modern:
                return "body { font-family: -apple-system, sans-serif; line-height: 1.6; color: #333; max-width: 800px; margin: 0 auto; padding: 2rem; } h1, h2, h3 { color: #111; border-bottom: 1px solid #eaeaea; padding-bottom: 0.3em; }"
            case .classic:
                return "body { font-family: 'Times New Roman', serif; line-height: 1.5; color: #000; max-width: 800px; margin: 0 auto; padding: 2rem; } h1, h2 { text-align: center; border-bottom: 2px solid #000; margin-bottom: 1rem; }"
            case .minimal:
                return "body { font-family: system-ui, sans-serif; font-weight: 300; line-height: 1.8; color: #444; margin: 2rem; } h1, h2, h3 { font-weight: 400; color: #222; margin-top: 2rem; }"
            case .academic:
                return "body { font-family: 'Georgia', serif; line-height: 1.6; color: #222; text-align: justify; margin: 3rem; } h1 { text-align: center; font-size: 2em; margin-bottom: 1em; } h2 { font-variant: small-caps; }"
            case .playful:
                return "body { font-family: 'Comic Sans MS', cursive, sans-serif; line-height: 1.6; color: #555; background-color: #fafafa; margin: 2rem; padding: 2rem; border-radius: 15px; border: 4px dashed #ffb6c1; } h1 { color: #ff69b4; text-align: center; } h2 { color: #8a2be2; }"
            }
        }
    }
    
    private var webView: WKWebView?
    private var completion: ((Result<URL, Error>) -> Void)?
    private var outputURL: URL?
    private var isRendering = false
    
    public func generatePDF(from markdown: String, styleName: String, outputURL: URL, completion: @escaping (Result<URL, Error>) -> Void) {
        guard !isRendering else {
            completion(.failure(GeneratorError.printOperationFailed))
            return
        }
        
        self.isRendering = true
        self.completion = completion
        self.outputURL = outputURL
        
        let style = PDFStyle(rawValue: styleName) ?? .modern
        let css = style.css
        
        let escapedMarkdown = markdown
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "`", with: "\\`")
            .replacingOccurrences(of: "$", with: "\\$")
        
        let htmlTemplate = """
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="utf-8">
            <script src="https://cdn.jsdelivr.net/npm/marked/marked.min.js"></script>
            <style>
                \(css)
            </style>
        </head>
        <body>
            <div id="content"></div>
            <script>
                document.addEventListener("DOMContentLoaded", () => {
                    const markdown = `\(escapedMarkdown)`;
                    document.getElementById('content').innerHTML = marked.parse(markdown);
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
        
        var printInfoDict = NSPrintInfo.shared.dictionary() as! [NSPrintInfo.AttributeKey: Any]
        printInfoDict[.jobSavingURL] = outputURL
        
        let newPrintInfo = NSPrintInfo(dictionary: printInfoDict)
        newPrintInfo.jobDisposition = .save
        newPrintInfo.horizontalPagination = .fit
        newPrintInfo.verticalPagination = .automatic
        newPrintInfo.topMargin = 36
        newPrintInfo.bottomMargin = 36
        newPrintInfo.leftMargin = 36
        newPrintInfo.rightMargin = 36
        
        let printOp = webView.printOperation(with: newPrintInfo)
        printOp.showsPrintPanel = false
        printOp.showsProgressPanel = false
        
        DispatchQueue.main.async {
            let success = printOp.run()
            if success {
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
        
        currentCompletion?(result)
    }
}
