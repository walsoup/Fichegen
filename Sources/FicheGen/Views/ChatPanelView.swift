import AppKit
import SwiftUI

struct ChatPanelView: View {
    @EnvironmentObject var state: AppState
    @Binding var isPresented: Bool
    @State private var inputText: String = ""
    @State private var isProcessing: Bool = false

    private let quickSuggestions = [
        "💡 Rendre plus simple",
        "📝 Ajouter des exercices",
        "🇬🇧 Traduire en anglais",
        "🎨 Surligner les mots-clés"
    ]

    var body: some View {
        VStack(spacing: 0) {
            headerView
            Divider()

            if state.chatHistory.isEmpty {
                emptyStateView
            } else {
                messageListView
            }

            Divider()
            composerView
        }
        .frame(minWidth: 380, minHeight: 520)
        .background(Color(nsColor: .windowBackgroundColor))
    }

    private var headerView: some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Assistant Pédagogique")
                    .font(.headline)
                Text("Entrée envoie, Maj+Entrée ajoute une ligne")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            Button(action: { isPresented = false }) {
                Image(systemName: "xmark.circle.fill")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 16)
        .frame(height: 56)
        .background(Material.bar)
    }

    private var messageListView: some View {
        ScrollViewReader { scrollProxy in
            ScrollView {
                LazyVStack(spacing: 14) {
                    ForEach(state.chatHistory) { message in
                        messageRow(message)
                            .id(message.id)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 16)
            }
            .background(Color(nsColor: .textBackgroundColor))
            .onChange(of: state.chatHistory.count) { _, _ in
                guard let lastMessage = state.chatHistory.last else { return }
                withAnimation(.spring(response: 0.25, dampingFraction: 0.9)) {
                    scrollProxy.scrollTo(lastMessage.id, anchor: .bottom)
                }
            }
        }
    }

    private func messageRow(_ message: ChatMessage) -> some View {
        let isUser = message.role == "user"

        return HStack(alignment: .top, spacing: 12) {
            if isUser { Spacer(minLength: 28) }

            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 6) {
                    Image(systemName: isUser ? "person.fill" : "sparkles")
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.secondary)
                    Text(isUser ? "Vous" : "Assistant")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)
                    Spacer(minLength: 0)
                }

                Text(message.text)
                    .font(.body)
                    .foregroundStyle(.primary)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)

                if let diff = message.diffLines {
                    DiffView(diffLines: diff)
                        .frame(maxWidth: 520, alignment: .leading)
                }
            }
            .padding(14)
            .frame(maxWidth: 520, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(isUser ? Color(nsColor: .controlBackgroundColor) : Color(nsColor: .textBackgroundColor))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .stroke(Color.primary.opacity(0.08), lineWidth: 1)
            )
            .shadow(color: Color.black.opacity(0.03), radius: 4, x: 0, y: 1)

            if !isUser { Spacer(minLength: 28) }
        }
        .frame(maxWidth: .infinity, alignment: isUser ? .trailing : .leading)
    }

    private var emptyStateView: some View {
        VStack(spacing: 18) {
            Spacer(minLength: 12)

            ZStack {
                Circle()
                    .fill(Color(nsColor: .controlBackgroundColor))
                    .frame(width: 84, height: 84)
                Image(systemName: "wand.and.stars")
                    .font(.system(size: 34, weight: .semibold))
                    .foregroundStyle(.secondary)
            }
            .overlay(
                Circle()
                    .stroke(Color.primary.opacity(0.08), lineWidth: 1)
            )

            VStack(spacing: 8) {
                Text("Modifier la fiche avec l'IA")
                    .font(.title3.weight(.semibold))
                Text("Expliquez le changement voulu. Entrée envoie, Maj+Entrée insère une nouvelle ligne.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: 380)
            }

            VStack(alignment: .leading, spacing: 10) {
                Text("Suggestions rapides")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)

                LazyVGrid(
                    columns: [GridItem(.adaptive(minimum: 160), spacing: 8, alignment: .leading)],
                    alignment: .leading,
                    spacing: 8
                ) {
                    ForEach(quickSuggestions, id: \.self) { suggestion in
                        suggestionChip(suggestion)
                    }
                }
            }
            .padding(16)
            .frame(maxWidth: 560)
            .background(
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(.thinMaterial)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .stroke(Color.primary.opacity(0.08), lineWidth: 1)
            )

            Spacer(minLength: 12)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(24)
        .background(Color(nsColor: .textBackgroundColor))
    }

    private var composerView: some View {
        HStack(alignment: .bottom, spacing: 10) {
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(Color(nsColor: .textBackgroundColor))

                ChatComposerTextView(
                    text: $inputText,
                    isEnabled: !isProcessing,
                    onSubmit: sendMessage
                )
                .padding(6)

                if inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text("Demander des modifications...")
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 14)
                        .allowsHitTesting(false)
                }
            }
            .frame(minHeight: 64, maxHeight: 132)
            .overlay(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .stroke(Color.primary.opacity(0.10), lineWidth: 1)
            )

            if isProcessing {
                ProgressView()
                    .controlSize(.small)
                    .frame(width: 34, height: 34)
                    .padding(.bottom, 4)
            } else {
                Button(action: sendMessage) {
                    Image(systemName: "paperplane.fill")
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(canSend ? Color.white : .secondary)
                        .frame(width: 34, height: 34)
                        .background(
                            Circle()
                                .fill(canSend ? Color.accentColor : Color(nsColor: .controlBackgroundColor))
                        )
                }
                .disabled(!canSend)
                .buttonStyle(.plain)
                .padding(.bottom, 4)
            }
        }
        .padding(14)
        .background(Color(nsColor: .windowBackgroundColor))
    }

    private var trimmedInput: String {
        inputText.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private var canSend: Bool {
        !trimmedInput.isEmpty && !isProcessing
    }
    
    private func sendMessage() {
        guard canSend else { return }
        
        let instruction = trimmedInput
        inputText = ""
        isProcessing = true
        
        // Append user message
        state.chatHistory.append(ChatMessage(role: "user", text: instruction))
        
        // Append placeholder assistant message
        let placeholderMessage = ChatMessage(role: "assistant", text: "Application des modifications...")
        state.chatHistory.append(placeholderMessage)
        
        let cfg = AIConfig(from: state)
        let originalHTML = state.generatedMarkdown
        
        Task {
            do {
                let updatedHTML = try await GenerationEngine.shared.editFiche(
                    currentHTML: originalHTML,
                    instructions: instruction,
                    config: cfg,
                    onLog: { msg in state.appendLog(msg) },
                    onProgress: { p in state.progress = p }
                )
                
                let diff = await Task.detached(priority: .userInitiated) {
                    computeDisplayDiff(old: originalHTML, new: updatedHTML)
                }.value
                
                await MainActor.run {
                    state.generatedMarkdown = updatedHTML
                    if let index = state.chatHistory.firstIndex(where: { $0.id == placeholderMessage.id }) {
                        state.chatHistory[index] = ChatMessage(role: "assistant", text: "Modification appliquée avec succès !", diffLines: diff)
                    }
                    isProcessing = false
                }
            } catch {
                await MainActor.run {
                    state.generatedMarkdown = originalHTML
                    if let index = state.chatHistory.firstIndex(where: { $0.id == placeholderMessage.id }) {
                        state.chatHistory[index] = ChatMessage(role: "assistant", text: "Erreur: \(error.localizedDescription)")
                    }
                    isProcessing = false
                }
            }
        }
    }

    @ViewBuilder
    private func suggestionChip(_ text: String) -> some View {
        Button(action: {
            guard !isProcessing else { return }
            inputText = String(text.dropFirst(2))
            sendMessage()
        }) {
            Text(text)
                .font(.subheadline)
                .foregroundStyle(.primary)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 12)
                .padding(.vertical, 10)
                .background(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .fill(Color(nsColor: .controlBackgroundColor))
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .stroke(Color.primary.opacity(0.08), lineWidth: 1)
                )
        }
        .buttonStyle(.plain)
        .disabled(isProcessing)
    }
}

struct ChatComposerTextView: NSViewRepresentable {
    @Binding var text: String
    var isEnabled: Bool
    var onSubmit: () -> Void

    func makeCoordinator() -> Coordinator {
        Coordinator(parent: self)
    }

    func makeNSView(context: Context) -> NSScrollView {
        let scrollView = NSScrollView()
        scrollView.drawsBackground = false
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.borderType = .noBorder
        scrollView.backgroundColor = .clear
        scrollView.documentView = makeTextView(context: context)
        return scrollView
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {
        guard let textView = nsView.documentView as? ComposerTextView else { return }

        if textView.string != text {
            textView.string = text
        }
        textView.isEditable = isEnabled
        textView.isSelectable = isEnabled
        textView.onSubmit = onSubmit
        textView.textColor = isEnabled ? .labelColor : .tertiaryLabelColor
    }

    private func makeTextView(context: Context) -> ComposerTextView {
        let textView = ComposerTextView()
        textView.delegate = context.coordinator
        textView.isRichText = false
        textView.importsGraphics = false
        textView.allowsUndo = true
        textView.isHorizontallyResizable = false
        textView.isVerticallyResizable = true
        textView.maxSize = NSSize(width: .greatestFiniteMagnitude, height: .greatestFiniteMagnitude)
        textView.minSize = .zero
        textView.textContainerInset = NSSize(width: 8, height: 10)
        textView.textContainer?.containerSize = NSSize(width: .greatestFiniteMagnitude, height: .greatestFiniteMagnitude)
        textView.textContainer?.widthTracksTextView = true
        textView.drawsBackground = false
        textView.backgroundColor = .clear
        textView.font = NSFont.systemFont(ofSize: NSFont.systemFontSize)
        textView.string = text
        textView.isEditable = isEnabled
        textView.isSelectable = isEnabled
        textView.onSubmit = onSubmit
        return textView
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: ChatComposerTextView

        init(parent: ChatComposerTextView) {
            self.parent = parent
        }

        func textDidChange(_ notification: Notification) {
            guard let textView = notification.object as? NSTextView else { return }
            parent.text = textView.string
        }
    }

    final class ComposerTextView: NSTextView {
        var onSubmit: (() -> Void)?

        override func keyDown(with event: NSEvent) {
            if (event.keyCode == 36 || event.keyCode == 76) && !event.modifierFlags.contains(.shift) {
                onSubmit?()
                return
            }
            super.keyDown(with: event)
        }
    }
}

// MARK: - Diff Components & Helpers

struct DiffView: View {
    let diffLines: [DiffLine]
    @State private var isExpanded = false
    
    var addedCount: Int {
        diffLines.filter { $0.kind == .added }.count
    }
    
    var removedCount: Int {
        diffLines.filter { $0.kind == .removed }.count
    }
    
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: "doc.text.fill.viewfinder")
                    .foregroundStyle(.secondary)
                Text("Modification du document")
                    .font(.subheadline)
                    .fontWeight(.semibold)
                Spacer()
                
                HStack(spacing: 8) {
                    if addedCount > 0 {
                        Text("+\(addedCount)")
                            .font(.caption)
                            .fontWeight(.bold)
                            .foregroundColor(.green)
                    }
                    if removedCount > 0 {
                        Text("-\(removedCount)")
                            .font(.caption)
                            .fontWeight(.bold)
                            .foregroundColor(.red)
                    }
                }
            }
            .padding(.vertical, 2)
            
            Button(action: { isExpanded.toggle() }) {
                HStack {
                    Text(isExpanded ? "Masquer les détails" : "Afficher les détails de la modification")
                    Spacer()
                    Image(systemName: isExpanded ? "chevron.up" : "chevron.down")
                }
                .font(.caption)
                .foregroundColor(.accentColor)
            }
            .buttonStyle(.plain)
            
            if isExpanded {
                ScrollView(.vertical) {
                    VStack(alignment: .leading, spacing: 2) {
                        ForEach(diffLines) { line in
                            diffLineRow(line)
                        }
                    }
                }
                .frame(maxHeight: 250)
                .padding(6)
                .background(Color(NSColor.textBackgroundColor))
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(Color.primary.opacity(0.15), lineWidth: 1)
                )
            }
        }
        .padding(10)
        .background(Color.primary.opacity(0.03))
        .cornerRadius(8)
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(Color.primary.opacity(0.08), lineWidth: 1)
        )
    }
    
    @ViewBuilder
    private func diffLineRow(_ line: DiffLine) -> some View {
        if line.text.hasPrefix("@@ Skip") {
            Text(line.text)
                .font(.system(.caption2, design: .monospaced))
                .foregroundColor(.secondary)
                .frame(maxWidth: .infinity, alignment: .center)
                .padding(.vertical, 4)
                .background(Color.primary.opacity(0.03))
        } else {
            HStack(alignment: .top, spacing: 4) {
                Text(line.kind == .added ? "+" : (line.kind == .removed ? "-" : " "))
                    .font(.system(.caption2, design: .monospaced))
                    .foregroundColor(line.kind == .added ? .green : (line.kind == .removed ? .red : .secondary))
                    .frame(width: 12, alignment: .leading)
                
                Text(line.text)
                    .font(.system(.caption2, design: .monospaced))
                    .foregroundColor(line.kind == .added ? .green : (line.kind == .removed ? .red : .primary))
                    .lineLimit(nil)
                    .multilineTextAlignment(.leading)
            }
            .padding(.horizontal, 4)
            .padding(.vertical, 1)
            .background(
                line.kind == .added ? Color.green.opacity(0.15) :
                (line.kind == .removed ? Color.red.opacity(0.15) : Color.clear)
            )
        }
    }
}

private func computeDiff(old: String, new: String) -> [DiffLine] {
    let oldLines = old.components(separatedBy: .newlines)
    let newLines = new.components(separatedBy: .newlines)
    
    let n = oldLines.count
    let m = newLines.count
    
    if n > 500 || m > 500 {
        var diff = [DiffLine]()
        for line in oldLines {
            diff.append(DiffLine(kind: .removed, text: line))
        }
        for line in newLines {
            diff.append(DiffLine(kind: .added, text: line))
        }
        return diff
    }
    
    var dp = Array(repeating: Array(repeating: 0, count: m + 1), count: n + 1)
    
    for i in 1...n {
        for j in 1...m {
            if oldLines[i - 1] == newLines[j - 1] {
                dp[i][j] = dp[i - 1][j - 1] + 1
            } else {
                dp[i][j] = max(dp[i - 1][j], dp[i][j - 1])
            }
        }
    }
    
    var diff = [DiffLine]()
    var i = n
    var j = m
    
    while i > 0 || j > 0 {
        if i > 0 && j > 0 && oldLines[i - 1] == newLines[j - 1] {
            diff.append(DiffLine(kind: .unchanged, text: oldLines[i - 1]))
            i -= 1
            j -= 1
        } else if j > 0 && (i == 0 || dp[i][j - 1] >= dp[i - 1][j]) {
            diff.append(DiffLine(kind: .added, text: newLines[j - 1]))
            j -= 1
        } else if i > 0 && (j == 0 || dp[i][j - 1] < dp[i - 1][j]) {
            diff.append(DiffLine(kind: .removed, text: oldLines[i - 1]))
            i -= 1
        }
    }
    
    return diff.reversed()
}

private func computeDisplayDiff(old: String, new: String) -> [DiffLine] {
    let fullDiff = computeDiff(old: old, new: new)
    var result = [DiffLine]()
    let contextSize = 2
    
    let count = fullDiff.count
    var keep = Array(repeating: false, count: count)
    
    for i in 0..<count {
        if fullDiff[i].kind == .added || fullDiff[i].kind == .removed {
            let start = max(0, i - contextSize)
            let end = min(count - 1, i + contextSize)
            for j in start...end {
                keep[j] = true
            }
        }
    }
    
    var inSkip = false
    var skipStart = 0
    
    for i in 0..<count {
        if keep[i] {
            if inSkip {
                let skipped = i - skipStart
                result.append(DiffLine(kind: .unchanged, text: "@@ Skip \(skipped) lignes @@"))
                inSkip = false
            }
            result.append(fullDiff[i])
        } else {
            if !inSkip {
                inSkip = true
                skipStart = i
            }
        }
    }
    if inSkip {
        let skipped = count - skipStart
        result.append(DiffLine(kind: .unchanged, text: "@@ Skip \(skipped) lignes @@"))
    }
    
    return result
}
