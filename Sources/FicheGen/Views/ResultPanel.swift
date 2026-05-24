import SwiftUI
import WebKit
import UniformTypeIdentifiers

// MARK: - Result Panel (right pane)

struct ResultPanel: View {
    @EnvironmentObject var state: AppState
    @Binding var showChatInspector: Bool
    @State private var selectedTab: ResultTab = .preview

    enum ResultTab: String, CaseIterable, Identifiable {
        case preview = "Preview"
        case source  = "Markdown"
        case log     = "Log"
        var id: String { rawValue }
    }

    var body: some View {
        VStack(spacing: 0) {
            // ── Toolbar ───────────────────────────────────────────────
            HStack(spacing: 0) {
                Picker("", selection: $selectedTab) {
                    ForEach(ResultTab.allCases) { tab in
                        Text(tab.rawValue).tag(tab)
                    }
                }
                .pickerStyle(.segmented)
                .frame(maxWidth: 280)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)

                Spacer()

                if !state.generatedMarkdown.isEmpty {
                    HStack(spacing: 8) {
                        Button(action: { showChatInspector.toggle() }) {
                            Image(systemName: "wand.and.stars")
                        }
                        .help("Ouvrir l'assistant")
                        
                        Button("Exporter PDF") {
                            let generator = PDFGenerator()
                            let panel = NSSavePanel()
                            panel.title = "Exporter en PDF"
                            panel.allowedContentTypes = [.pdf]
                            panel.nameFieldStringValue = "fiche.pdf"
                            panel.canCreateDirectories = true
                            if panel.runModal() == .OK, let url = panel.url {
                                // Default to Modern style for now, can be expanded to let the user pick
                                generator.generatePDF(from: state.generatedMarkdown, styleName: "Modern", outputURL: url) { result in
                                    DispatchQueue.main.async {
                                        switch result {
                                        case .success(let savedURL):
                                            NSWorkspace.shared.selectFile(savedURL.path, inFileViewerRootedAtPath: "")
                                        case .failure(let error):
                                            print("Erreur d'export PDF: \(error)")
                                        }
                                    }
                                }
                            }
                        }
                        
                        Button("Markdown") {
                            saveMarkdown(state.generatedMarkdown)
                        }
                        
                        Button("Copier") {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.setString(state.generatedMarkdown, forType: .string)
                        }
                    }
                    .padding(.trailing, 12)
                }

                if state.isGenerating {
                    ProgressView()
                        .scaleEffect(0.6)
                        .padding(.trailing, 8)
                }
            }
            .background(Material.bar)

            Divider()

            // ── Progress bar ──────────────────────────────────────────
            if state.isGenerating && state.progress > 0 {
                ProgressView(value: Double(state.progress), total: 100)
                    .progressViewStyle(.linear)
                    .padding(.horizontal, 0)
                    .frame(height: 2)
            }

            // ── Content ───────────────────────────────────────────────
            Group {
                switch selectedTab {
                case .preview:
                    if state.generatedMarkdown.isEmpty {
                        EmptyStateView()
                    } else {
                        MarkdownWebView(markdown: state.generatedMarkdown)
                    }
                case .source:
                    SourceEditor(text: $state.generatedMarkdown)
                case .log:
                    LogView()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(minWidth: 400, minHeight: 400)
        .background(.ultraThinMaterial)
        .animation(.spring(), value: selectedTab)
        .animation(.spring(), value: state.generatedMarkdown.isEmpty)
    }
    
    private func saveMarkdown(_ markdown: String) {
        let panel = NSSavePanel()
        panel.title = "Exporter la fiche"
        panel.allowedContentTypes = [.init(filenameExtension: "md")!]
        panel.nameFieldStringValue = "fiche.md"
        panel.canCreateDirectories = true
        if panel.runModal() == .OK, let url = panel.url {
            try? markdown.write(to: url, atomically: true, encoding: .utf8)
            NSWorkspace.shared.selectFile(url.path, inFileViewerRootedAtPath: "")
        }
    }
}

// MARK: - Empty state

struct EmptyStateView: View {
    @EnvironmentObject var state: AppState
    var body: some View {
        VStack(spacing: 12) {
            Image(systemName: "doc.text.magnifyingglass")
                .font(.system(size: 48))
                .foregroundStyle(.tertiary)
            Text(state.isGenerating ? "Génération en cours…" : "Remplissez le formulaire et cliquez sur Générer")
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

// MARK: - Markdown WebView

struct MarkdownWebView: NSViewRepresentable {
    let markdown: String

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        let wv = WKWebView(frame: .zero, configuration: config)
        wv.setValue(false, forKey: "drawsBackground")
        return wv
    }

    func updateNSView(_ wv: WKWebView, context: Context) {
        let escaped = markdown
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "`", with: "\\`")
        
        let markedJS: String
        if let jsURL = Bundle.main.url(forResource: "marked.min", withExtension: "js"),
           let jsContent = try? String(contentsOf: jsURL, encoding: .utf8) {
            markedJS = jsContent
        } else {
            markedJS = ""
        }

        let scriptTag = markedJS.isEmpty 
            ? "<script src=\"https://cdn.jsdelivr.net/npm/marked/marked.min.js\"></script>"
            : "<script>\(markedJS)</script>"

        let html = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="color-scheme" content="light dark">
        \(scriptTag)
        <style>
          body {
            font-family: -apple-system, sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 24px 28px;
            color: #1d1d1f;
            max-width: 900px;
            margin: 0 auto;
          }
          @media (prefers-color-scheme: dark) {
            body { background: #1e1e1e; color: #ececec; }
            code { background: #2d2d2d; }
          }
          h1 { font-size: 1.5em; font-weight: 700; margin-bottom: 4px; }
          h2 { font-size: 1.15em; font-weight: 600; margin-top: 24px; border-bottom: 1px solid #e0e0e0; padding-bottom: 4px; }
          h3 { font-size: 1em; font-weight: 600; margin-top: 16px; }
          code { background: #f4f4f4; border-radius: 4px; padding: 1px 5px; font-size: 0.9em; }
          blockquote { border-left: 3px solid #007aff; margin-left: 0; padding-left: 14px; color: #555; }
          table { border-collapse: collapse; width: 100%; }
          td, th { border: 1px solid #ddd; padding: 6px 10px; }
          th { background: #f8f8f8; font-weight: 600; }
        </style>
        </head>
        <body>
        <div id="content"></div>
        <script>
          document.getElementById('content').innerHTML = marked.parse(`\(escaped)`);
        </script>
        </body>
        </html>
        """
        wv.loadHTMLString(html, baseURL: nil)
    }
}

// MARK: - Source editor

struct SourceEditor: View {
    @Binding var text: String
    var body: some View {
        TextEditor(text: $text)
            .font(.system(.body, design: .monospaced))
            .scrollContentBackground(.visible)
    }
}

// MARK: - Log view

struct LogView: View {
    @EnvironmentObject var state: AppState
    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 2) {
                    ForEach(Array(state.logMessages.enumerated()), id: \.offset) { idx, msg in
                        Text(msg)
                            .font(.system(.caption, design: .monospaced))
                            .foregroundStyle(logColor(msg))
                            .textSelection(.enabled)
                            .id(idx)
                    }
                }
                .padding(12)
            }
            .onChange(of: state.logMessages.count) { _, _ in
                if let last = state.logMessages.indices.last {
                    withAnimation { proxy.scrollTo(last, anchor: .bottom) }
                }
            }
        }
        .background(Color(nsColor: .textBackgroundColor))
    }

    private func logColor(_ msg: String) -> Color {
        if msg.hasPrefix("❌") || msg.hasPrefix("💥") { return .red }
        if msg.hasPrefix("✅") { return .green }
        if msg.hasPrefix("⚠️") { return .orange }
        return .primary
    }

}
