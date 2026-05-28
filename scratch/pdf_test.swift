import AppKit
import WebKit

let html = "<html><body><h1>Hello World</h1><p>Test</p></body></html>"

let webView = WKWebView(frame: CGRect(x: 0, y: 0, width: 800, height: 1100))
let window = NSWindow(contentRect: CGRect(x: -10000, y: -10000, width: 800, height: 1100), styleMask: .borderless, backing: .buffered, defer: false)
window.contentView = webView
window.alphaValue = 1.0
window.makeKeyAndOrderFront(nil)

class Delegate: NSObject, WKNavigationDelegate {
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        let pdfConfig = WKPDFConfiguration()
        webView.createPDF(configuration: pdfConfig) { result in
            switch result {
            case .success(let data):
                let url = URL(fileURLWithPath: "test.pdf")
                try? data.write(to: url)
                print("PDF generated successfully, size: \(data.count) bytes")
                exit(0)
            case .failure(let error):
                print("Error generating PDF: \(error)")
                exit(1)
            }
        }
    }
}

let delegate = Delegate()
webView.navigationDelegate = delegate
webView.loadHTMLString(html, baseURL: nil)

RunLoop.main.run()
