import SwiftUI
import WebKit
import UniformTypeIdentifiers

// MARK: - Result Panel (right pane)

struct ResultPanel: View {
    @EnvironmentObject var state: AppState
    @Binding var showChatInspector: Bool
    @State private var selectedTab: ResultTab = .preview
    @State private var pdfGenerator = PDFGenerator()
    @State private var showToast = false
    @State private var toastMessage = ""

    enum ResultTab: String, CaseIterable, Identifiable {
        case preview = "Preview"
        case source  = "HTML"
        case log     = "Log"
        var id: String { rawValue }
    }

    var body: some View {
        ZStack {
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

                HStack(spacing: 6) {
                    Button(action: { showChatInspector.toggle() }) {
                        Label("Assistant", systemImage: "wand.and.stars")
                    }
                    .buttonStyle(.bordered)
                    .tint(.accentColor)
                    .help("Ouvrir l'assistant IA")
                }
                .padding(.trailing, 12)
                .controlSize(.small)

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
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            
            // ── Floating Action Bar ──────────────────────────────────────────
            if !state.generatedMarkdown.isEmpty && selectedTab == .preview {
                VStack {
                    Spacer()
                    HStack(spacing: 12) {
                        Spacer()
                        
                        // Floating Export Group
                        HStack(spacing: 8) {
                            Button(action: {
                                NSPasteboard.general.clearContents()
                                NSPasteboard.general.setString(state.generatedMarkdown, forType: .string)
                                showToastMessage("Copié dans le presse-papiers")
                            }) {
                                Image(systemName: "doc.on.doc")
                            }
                            .help("Copier")
                            
                            Button(action: { saveHTML(state.generatedMarkdown) }) {
                                Image(systemName: "doc.text")
                            }
                            .help("Exporter HTML")
                            
                            Button(action: { exportPDF() }) {
                                Image(systemName: "doc.plaintext")
                            }
                            .help("Exporter PDF")
                        }
                        .padding(12)
                        .background(.ultraThinMaterial)
                        .cornerRadius(12)
                        .shadow(color: .black.opacity(0.1), radius: 5, y: 2)
                        .controlSize(.large)
                        .buttonStyle(.plain)
                        .padding(.trailing, 24)
                        .padding(.bottom, 24)
                    }
                }
                .transition(.opacity.combined(with: .move(edge: .bottom)))
            }
            
            // ── Toast Notification ───────────────────────────────────────────
            if showToast {
                VStack {
                    Spacer()
                    Text(toastMessage)
                        .font(.subheadline)
                        .foregroundColor(.white)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 8)
                        .background(Color.black.opacity(0.75))
                        .cornerRadius(20)
                        .padding(.bottom, 40)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                }
            }
        }
        .frame(minWidth: 400, minHeight: 400)
        .background(.ultraThinMaterial)
        .animation(.spring(), value: selectedTab)
        .animation(.spring(), value: state.generatedMarkdown.isEmpty)
        .animation(.spring(), value: showToast)
    }
    
    private func showToastMessage(_ msg: String) {
        toastMessage = msg
        withAnimation { showToast = true }
        DispatchQueue.main.asyncAfter(deadline: .now() + 3) {
            withAnimation { showToast = false }
        }
    }
    
    private func getOutputURL(extension ext: String) -> URL {
        let filename = "\(!state.ficheLessonTopic.isEmpty ? state.ficheLessonTopic : "Fiche")-\(Int(Date().timeIntervalSince1970)).\(ext)"
        let dirPath = state.outputDir.trimmingCharacters(in: .whitespacesAndNewlines)
        if !dirPath.isEmpty {
            let fm = FileManager.default
            let url = URL(fileURLWithPath: dirPath)
            if fm.fileExists(atPath: url.path) {
                return url.appendingPathComponent(filename)
            }
        }
        return FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask)[0].appendingPathComponent(filename)
    }

    private func exportPDF() {
        let url = getOutputURL(extension: "pdf")
        pdfGenerator.generatePDF(from: state.generatedMarkdown, styleName: state.defaultPdfStyle, outputURL: url) { result in
            DispatchQueue.main.async {
                switch result {
                case .success(let savedURL):
                    showToastMessage("PDF sauvé: \(savedURL.lastPathComponent)")
                case .failure(let error):
                    showToastMessage("Erreur: \(error.localizedDescription)")
                }
            }
        }
    }
    
    private func saveHTML(_ html: String) {
        let url = getOutputURL(extension: "html")
        do {
            try html.write(to: url, atomically: true, encoding: .utf8)
            showToastMessage("HTML sauvé: \(url.lastPathComponent)")
        } catch {
            showToastMessage("Erreur: \(error.localizedDescription)")
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
                            LinearGradient(colors: [.accentColor, .purple], startPoint: .topLeading, endPoint: .bottomTrailing),
                            lineWidth: 3
                        )
                        .frame(width: 64, height: 64)
                        .scaleEffect(isAnimating ? 1.2 : 0.8)
                        .opacity(isAnimating ? 0.3 : 0.8)
                    
                    Image(systemName: "sparkles")
                        .font(.system(size: 28))
                        .foregroundStyle(
                            LinearGradient(colors: [.accentColor, .purple], startPoint: .top, endPoint: .bottom)
                        )
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
                        .foregroundStyle(
                            LinearGradient(colors: [.accentColor, .indigo], startPoint: .topLeading, endPoint: .bottomTrailing)
                        )
                        .opacity(0.8)
                        .offset(x: -4, y: -4)

                    Image(systemName: "wand.and.stars")
                        .font(.system(size: 24))
                        .foregroundStyle(.purple)
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
        
        let base64Content = htmlContent.data(using: .utf8)?.base64EncodedString() ?? ""
        let initialHTML = getScaffolding(contentBase64: base64Content)
        wv.loadHTMLString(initialHTML, baseURL: nil)
        return wv
    }

    func updateNSView(_ wv: WKWebView, context: Context) {
        let base64Content = htmlContent.data(using: .utf8)?.base64EncodedString() ?? ""
        let js = "window.updateContent('\(base64Content)');"
        wv.evaluateJavaScript(js, completionHandler: nil)
    }

    private func getScaffolding(contentBase64: String) -> String {
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
            th { background: #2d2d2d; color: #ececec; }
            td, th { border: 1px solid #444; }
          }
          h1 { font-size: 1.5em; font-weight: 700; margin-bottom: 4px; }
          h2 { font-size: 1.15em; font-weight: 600; margin-top: 24px; border-bottom: 1px solid #e0e0e0; padding-bottom: 4px; }
          @media (prefers-color-scheme: dark) { h2 { border-bottom: 1px solid #444; } }
          h3 { font-size: 1em; font-weight: 600; margin-top: 16px; }
          code { background: #f4f4f4; border-radius: 4px; padding: 1px 5px; font-size: 0.9em; }
          blockquote { border-left: 3px solid #007aff; margin-left: 0; padding-left: 14px; color: #555; }
          @media (prefers-color-scheme: dark) { blockquote { color: #aaa; } }
          table { border-collapse: collapse; width: 100%; }
          td, th { border: 1px solid #ddd; padding: 6px 10px; }
          th { background: #f8f8f8; font-weight: 600; }
        </style>
        </head>
        <body>
        <div id="content"></div>
        <script>
          window.updateContent = function(base64Str) {
            if (!base64Str) {
              document.getElementById('content').innerHTML = '';
              return;
            }
            try {
              const decoded = decodeURIComponent(escape(atob(base64Str)));
              document.getElementById('content').innerHTML = decoded;
            } catch (e) {
              console.error('Decoding error:', e);
            }
          };
          
          window.addEventListener('DOMContentLoaded', () => {
             window.updateContent('\(contentBase64)');
          });
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
