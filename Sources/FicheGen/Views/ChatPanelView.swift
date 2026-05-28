import SwiftUI

struct ChatPanelView: View {
    @EnvironmentObject var state: AppState
    @Binding var isPresented: Bool
    @State private var inputText: String = ""
    @State private var isProcessing: Bool = false

    var body: some View {
        VStack(spacing: 0) {
            // Header with title and Close button
            HStack {
                Spacer()
                Text("Assistant Pédagogique")
                    .font(.headline)
                    .padding(.leading, 32)
                Spacer()
                Button(action: { isPresented = false }) {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundColor(.secondary)
                        .font(.title3)
                }
                .buttonStyle(.plain)
                .padding(.trailing, 12)
            }
            .frame(height: 48)
            .background(Color(nsColor: .windowBackgroundColor))
            
            Divider()
            
            // Message Area / Empty State
            if state.chatHistory.isEmpty {
                VStack(spacing: 16) {
                    Image(systemName: "wand.and.stars")
                        .font(.system(size: 44))
                        .foregroundStyle(Color.accentColor)
                    Text("Modifier la fiche avec l'IA")
                        .font(.headline)
                    Text("Demandez des ajustements ou des ajouts (ex: 'Rends le texte plus simple', 'Ajoute des exercices sur le vocabulaire').")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 24)
                    
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Suggestions :")
                            .font(.caption)
                            .fontWeight(.semibold)
                            .foregroundStyle(.secondary)
                            .padding(.horizontal, 24)
                        
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 8) {
                                suggestionChip("💡 Rendre plus simple")
                                suggestionChip("📝 Ajouter des exercices")
                                suggestionChip("🇬🇧 Traduire en anglais")
                                suggestionChip("🎨 Surligner les mots-clés")
                            }
                            .padding(.horizontal, 24)
                        }
                    }
                    .padding(.top, 8)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollViewReader { scrollProxy in
                    ScrollView {
                        VStack(spacing: 16) {
                            ForEach(state.chatHistory) { message in
                                HStack(alignment: .top) {
                                    if message.role == "user" {
                                        Spacer()
                                        Text(message.text)
                                            .padding(.horizontal, 12)
                                            .padding(.vertical, 8)
                                            .background(Color.accentColor)
                                            .foregroundColor(.white)
                                            .cornerRadius(12)
                                            .shadow(color: Color.black.opacity(0.1), radius: 2, x: 0, y: 1)
                                            .textSelection(.enabled)
                                            .transition(.scale.combined(with: .opacity))
                                    } else {
                                        VStack(alignment: .leading, spacing: 6) {
                                            Text(message.text)
                                                .padding(.horizontal, 12)
                                                .padding(.vertical, 8)
                                                .background(.ultraThinMaterial)
                                                .cornerRadius(12)
                                                .overlay(
                                                    RoundedRectangle(cornerRadius: 12)
                                                        .stroke(Color.primary.opacity(0.1), lineWidth: 1)
                                                )
                                                .textSelection(.enabled)
                                            
                                            if let diff = message.diffLines {
                                                DiffView(diffLines: diff)
                                                    .frame(maxWidth: 380)
                                            }
                                        }
                                        .transition(.slide.combined(with: .opacity))
                                        Spacer()
                                    }
                                }
                                .padding(.horizontal, 12)
                                .id(message.id)
                            }
                        }
                        .padding(.vertical)
                    }
                    .onChange(of: state.chatHistory.count) { _, _ in
                        if let lastMessage = state.chatHistory.last {
                            withAnimation(.spring()) {
                                scrollProxy.scrollTo(lastMessage.id, anchor: .bottom)
                            }
                        }
                    }
                }
            }
            
            Divider()
                       VStack {
                HStack(alignment: .bottom, spacing: 8) {
                    ZStack(alignment: .topLeading) {
                        if inputText.isEmpty {
                            Text("Demander des modifications...")
                                .foregroundColor(.gray.opacity(0.7))
                                .padding(.horizontal, 8)
                                .padding(.vertical, 8)
                                .allowsHitTesting(false)
                        }
                        TextEditor(text: $inputText)
                            .padding(4)
                            .scrollContentBackground(.hidden)
                            .font(.system(.body))
                    }
                    .frame(minHeight: 60, maxHeight: 120)
                    .background(Color(nsColor: .controlBackgroundColor))
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(Color(nsColor: .separatorColor), lineWidth: 1)
                    )
                    .disabled(isProcessing)
                    
                    if isProcessing {
                        ProgressView()
                            .scaleEffect(0.7)
                            .frame(width: 28, height: 28)
                            .padding(.bottom, 6)
                    } else {
                        Button(action: sendMessage) {
                            Image(systemName: "paperplane.fill")
                                .font(.system(size: 16, weight: .bold))
                                .foregroundColor(inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? .gray : .accentColor)
                                .frame(width: 28, height: 28)
                                .background(inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? Color.clear : Color.accentColor.opacity(0.1))
                                .cornerRadius(6)
                        }
                        .disabled(inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .buttonStyle(.plain)
                        .padding(.bottom, 6)
                    }
                }
                .padding()
            }
            .background(Color(nsColor: .windowBackgroundColor))
        }
        .frame(minWidth: 320)
        .background(Color(nsColor: .controlBackgroundColor))
    }
    
    private func sendMessage() {
        guard !inputText.isEmpty else { return }
        
        let instruction = inputText
        inputText = ""
        isProcessing = true
        
        // Append user message
        state.chatHistory.append(ChatMessage(role: "user", text: instruction))
        
        // Append placeholder assistant message
        let placeholderMessage = ChatMessage(role: "assistant", text: "Analyse de la demande...")
        state.chatHistory.append(placeholderMessage)
        
        let cfg = AIConfig(from: state)
        let originalHTML = state.generatedMarkdown
        let hasFiche = !originalHTML.isEmpty
        
        Task {
            do {
                // 1. Analyze Intent
                let intentResp = try await GenerationEngine.shared.analyzeIntent(prompt: instruction, hasFiche: hasFiche, config: cfg)
                
                if intentResp.intent == "generate" {
                    await MainActor.run {
                        let topicMsg = intentResp.topic ?? "ce sujet"
                        let levelMsg = intentResp.classLevel ?? "la classe"
                        if let index = state.chatHistory.firstIndex(where: { $0.id == placeholderMessage.id }) {
                            state.chatHistory[index] = ChatMessage(role: "assistant", text: "Je lance la génération de la leçon sur **\(topicMsg)** pour **\(levelMsg)**...")
                        }
                        if let cl = intentResp.classLevel, !cl.isEmpty { state.ficheClassLevel = cl }
                        if let t = intentResp.topic, !t.isEmpty { state.ficheLessonTopic = t }
                        isProcessing = false
                        state.generateFiche() // Trigger generation loop
                    }
                    return
                }
                
                // 2. Edit Mode
                await MainActor.run {
                    if let index = state.chatHistory.firstIndex(where: { $0.id == placeholderMessage.id }) {
                        state.chatHistory[index] = ChatMessage(role: "assistant", text: "Application des modifications...")
                    }
                }
                
                if !hasFiche {
                    await MainActor.run {
                        if let index = state.chatHistory.firstIndex(where: { $0.id == placeholderMessage.id }) {
                            state.chatHistory[index] = ChatMessage(role: "assistant", text: "Aucune fiche n'est actuellement chargée. Demandez-moi d'en générer une !")
                        }
                        isProcessing = false
                    }
                    return
                }

                await MainActor.run { state.generatedMarkdown = "" }
                try await GenerationEngine.shared.editFicheStream(
                    currentHTML: originalHTML,
                    instructions: instruction,
                    config: cfg,
                    onLog: { msg in state.appendLog(msg) },
                    onProgress: { p in state.progress = p },
                    onDelta: { chunk in
                        Task { @MainActor in
                            state.generatedMarkdown += chunk
                        }
                    }
                )
                
                var updatedHTML = state.generatedMarkdown
                // Strip markdown wrappers if the LLM hallucinated them
                if updatedHTML.hasPrefix("```html\n") { updatedHTML.removeFirst(8) }
                else if updatedHTML.hasPrefix("```\n") { updatedHTML.removeFirst(4) }
                if updatedHTML.hasSuffix("\n```") { updatedHTML.removeLast(4) }
                else if updatedHTML.hasSuffix("```") { updatedHTML.removeLast(3) }
                
                let finalHTML = updatedHTML.trimmingCharacters(in: .whitespacesAndNewlines)
                
                let diff = await Task.detached(priority: .userInitiated) {
                    computeDisplayDiff(old: originalHTML, new: finalHTML)
                }.value
                
                await MainActor.run {
                    state.generatedMarkdown = finalHTML
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
            inputText = String(text.dropFirst(2))
            sendMessage()
        }) {
            Text(text)
                .font(.subheadline)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(.ultraThinMaterial)
                .cornerRadius(16)
                .overlay(
                    RoundedRectangle(cornerRadius: 16)
                        .stroke(Color.primary.opacity(0.15), lineWidth: 1)
                )
        }
        .buttonStyle(.plain)
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
                    .foregroundColor(.blue)
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
