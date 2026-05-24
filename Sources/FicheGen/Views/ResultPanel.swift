import SwiftUI
import WebKit
import UniformTypeIdentifiers

// MARK: - Result Panel (right pane)

struct ResultPanel: View {
    @EnvironmentObject var state: AppState
    @Binding var showChatInspector: Bool
    @State private var selectedTab: ResultTab = .preview
    @State private var pdfGenerator = PDFGenerator()

    enum ResultTab: String, CaseIterable, Identifiable {
        case preview = "Preview"
        case source  = "HTML"
        case log     = "Log"
        var id: String { rawValue }
    }

    var body: some View {
        VStack(spacing: 0) {
            // ── Toolbar ───────────────────────────────────────────────
            HStack(spacing: 0) {
                Picker("", selection: $selectedTab) {
                    ForEach(ResultTab.allCases.filter { $0 != .log || state.advancedShowLogTab }) { tab in
                        Text(tab.rawValue).tag(tab)
                    }
                }
                .pickerStyle(.segmented)
                .frame(maxWidth: 280)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)

                Spacer()

                if !state.generatedMarkdown.isEmpty {
                    HStack(spacing: 6) {
                        Button(action: { showChatInspector.toggle() }) {
                            Label("Assistant", systemImage: "wand.and.stars")
                        }
                        .buttonStyle(.bordered)
                        .tint(.accentColor)
                        .help("Ouvrir l'assistant IA")
                        
                        Button(action: {
                            let panel = NSSavePanel()
                            panel.title = "Exporter en PDF"
                            panel.allowedContentTypes = [.pdf]
                            panel.nameFieldStringValue = "\(!state.ficheLessonTopic.isEmpty ? state.ficheLessonTopic : "Fiche")-\(Int(Date().timeIntervalSince1970)).pdf"
                            panel.canCreateDirectories = true
                            if panel.runModal() == .OK, let url = panel.url {
                                pdfGenerator.generatePDF(from: state.generatedMarkdown, styleName: state.defaultPdfStyle, outputURL: url) { result in
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
                        }) {
                            Label("PDF", systemImage: "doc.plaintext")
                        }
                        .buttonStyle(.bordered)
                        .help("Exporter au format PDF")
                        
                        Button(action: {
                            saveHTML(state.generatedMarkdown)
                        }) {
                            Label("HTML", systemImage: "doc.text")
                        }
                        .buttonStyle(.bordered)
                        .help("Exporter au format HTML")
                        
                        Button(action: {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.setString(state.generatedMarkdown, forType: .string)
                        }) {
                            Label("Copier", systemImage: "doc.on.doc")
                        }
                        .buttonStyle(.bordered)
                        .help("Copier le texte dans le presse-papiers")
                    }
                    .padding(.trailing, 12)
                    .controlSize(.small)
                }

                if state.isGenerating {
                    ProgressView()
                        .scaleEffect(0.6)
                        .padding(.trailing, 8)
                }
            }
            .background(Material.bar)

            Divider()

            // ── Progress indicator ──────────────────────────────────────
            if state.isGenerating {
                PulsingProgressView(message: state.logMessages.last ?? "Génération en cours...")
            }

            // ── Content ───────────────────────────────────────────────
            Group {
                switch selectedTab {
                case .preview:
                    if state.generatedMarkdown.isEmpty {
                        EmptyStateView()
                    } else {
                        MarkdownWebView(htmlContent: state.generatedMarkdown)
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
    
    private func saveHTML(_ html: String) {
        let panel = NSSavePanel()
        panel.title = "Exporter la fiche"
        panel.allowedContentTypes = [.init(filenameExtension: "html")!]
        panel.nameFieldStringValue = "\(!state.ficheLessonTopic.isEmpty ? state.ficheLessonTopic : "Fiche")-\(Int(Date().timeIntervalSince1970)).html"
        panel.canCreateDirectories = true
        if panel.runModal() == .OK, let url = panel.url {
            try? html.write(to: url, atomically: true, encoding: .utf8)
            NSWorkspace.shared.selectFile(url.path, inFileViewerRootedAtPath: "")
        }
    }
}

// MARK: - Pulsing Progress View

struct PulsingProgressView: View {
    let message: String
    @State private var isPulsing = false
    
    var body: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(Color.accentColor)
                .frame(width: 8, height: 8)
                .scaleEffect(isPulsing ? 1.2 : 0.8)
                .opacity(isPulsing ? 0.5 : 1.0)
                .onAppear {
                    withAnimation(.easeInOut(duration: 0.8).repeatForever(autoreverses: true)) {
                        isPulsing = true
                    }
                }
            
            Text(message)
                .font(.caption)
                .foregroundColor(.secondary)
                .lineLimit(1)
            
            Spacer()
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .background(Color(NSColor.windowBackgroundColor))
    }
}

// MARK: - Empty state

struct EmptyStateView: View {
    @EnvironmentObject var state: AppState
    @State private var isAnimating = false

    var body: some View {
        VStack(spacing: 20) {
            if state.isGenerating {
                ZStack {
                    Circle()
                        .stroke(
                            LinearGradient(colors: [Color.accentColor, Color.secondary], startPoint: .topLeading, endPoint: .bottomTrailing),
                            lineWidth: 3
                        )
                        .frame(width: 64, height: 64)
                        .scaleEffect(isAnimating ? 1.2 : 0.8)
                        .opacity(isAnimating ? 0.3 : 0.8)
                    
                    Image(systemName: "sparkles")
                        .font(.system(size: 28))
                        .foregroundStyle(.secondary)
                        .rotationEffect(.degrees(isAnimating ? 360 : 0))
                }
                .frame(width: 80, height: 80)
                .onAppear {
                    withAnimation(.linear(duration: 2).repeatForever(autoreverses: false)) {
                        isAnimating = true
                    }
                }
                .onDisappear {
                    isAnimating = false
                }
                
                VStack(spacing: 8) {
                    Text("Création en cours...")
                        .font(.headline)
                        .foregroundStyle(.primary)
                    
                    if let lastLog = state.logMessages.last {
                        Text(lastLog)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                            .padding(.horizontal, 32)
                            .transition(.opacity)
                    } else {
                        Text("Initialisation de la génération...")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                }
            } else {
                ZStack {
                    Circle()
                        .fill(Color.primary.opacity(0.03))
                        .frame(width: 100, height: 100)
                    
                    Image(systemName: "doc.text.fill")
                        .font(.system(size: 40))
                        .foregroundStyle(Color.accentColor)
                        .opacity(0.8)
                        .offset(x: -4, y: -4)

                    Image(systemName: "wand.and.stars")
                        .font(.system(size: 24))
                        .foregroundStyle(.secondary)
                        .offset(x: 18, y: 18)
                }
                .padding(.bottom, 8)
                
                Text("Prêt pour la création")
                    .font(.title3)
                    .fontWeight(.semibold)
                    .foregroundStyle(.primary)
                
                Text("Remplissez le formulaire de gauche puis cliquez sur Générer pour créer vos fiches de cours.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 48)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .transition(.opacity.combined(with: .scale(scale: 0.95)))
    }
}

// MARK: - Markdown WebView

struct MarkdownWebView: NSViewRepresentable {
    let htmlContent: String

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        let wv = WKWebView(frame: .zero, configuration: config)
        wv.setValue(false, forKey: "drawsBackground")
        
        let escapedHTML = htmlContent
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "`", with: "\\`")
            .replacingOccurrences(of: "$", with: "\\$")
        
        let initialHTML = getScaffolding(content: htmlContent.isEmpty ? "" : escapedHTML)
        wv.loadHTMLString(initialHTML, baseURL: nil)
        return wv
    }

    func updateNSView(_ wv: WKWebView, context: Context) {
        let escapedHTML = htmlContent
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "`", with: "\\`")
            .replacingOccurrences(of: "$", with: "\\$")
        
        let js = "document.getElementById('content').innerHTML = `\(escapedHTML)`;"
        wv.evaluateJavaScript(js, completionHandler: nil)
    }

    private func getScaffolding(content: String) -> String {
        return """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="color-scheme" content="light dark">
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
          if (`\(content)` !== '') {
            document.getElementById('content').innerHTML = `\(content)`;
          }
        </script>
        </body>
        </html>
        """
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
