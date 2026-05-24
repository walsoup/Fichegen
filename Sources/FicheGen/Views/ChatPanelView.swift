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
            .background(.regularMaterial)
            
            Divider()
            
            // Message Area / Empty State
            if state.chatHistory.isEmpty {
                VStack(spacing: 16) {
                    Image(systemName: "wand.and.stars")
                        .font(.system(size: 44))
                        .foregroundStyle(LinearGradient(colors: [.blue, .purple], startPoint: .topLeading, endPoint: .bottomTrailing))
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
                        VStack(spacing: 12) {
                            ForEach(state.chatHistory) { message in
                                HStack {
                                    if message.role == "user" {
                                        Spacer()
                                        Text(message.text)
                                            .padding(10)
                                            .background(
                                                LinearGradient(
                                                    colors: [Color.blue, Color.indigo],
                                                    startPoint: .topLeading,
                                                    endPoint: .bottomTrailing
                                                )
                                            )
                                            .foregroundColor(.white)
                                            .cornerRadius(12)
                                            .shadow(color: Color.blue.opacity(0.15), radius: 3, x: 0, y: 2)
                                            .textSelection(.enabled)
                                            .transition(.scale.combined(with: .opacity))
                                    } else {
                                        Text(message.text)
                                            .padding(10)
                                            .background(.ultraThinMaterial)
                                            .cornerRadius(12)
                                            .overlay(
                                                RoundedRectangle(cornerRadius: 12)
                                                    .stroke(Color.primary.opacity(0.1), lineWidth: 1)
                                            )
                                            .textSelection(.enabled)
                                            .transition(.slide.combined(with: .opacity))
                                        Spacer()
                                    }
                                }
                                .padding(.horizontal)
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
            
            // Input Area
            VStack {
                HStack {
                    TextField("Demander des modifications...", text: $inputText)
                        .textFieldStyle(RoundedBorderTextFieldStyle())
                        .disabled(isProcessing)
                        .onSubmit {
                            sendMessage()
                        }
                    
                    if isProcessing {
                        ProgressView()
                            .scaleEffect(0.7)
                            .frame(width: 20, height: 20)
                    } else {
                        Button(action: sendMessage) {
                            Image(systemName: "paperplane.fill")
                                .foregroundColor(inputText.isEmpty ? .gray : .blue)
                        }
                        .disabled(inputText.isEmpty)
                        .buttonStyle(.plain)
                    }
                }
                .padding()
            }
            .background(.regularMaterial)
        }
        .frame(minWidth: 300)
        .background(.ultraThinMaterial)
    }
    
    private func sendMessage() {
        guard !inputText.isEmpty else { return }
        
        let instruction = inputText
        inputText = ""
        isProcessing = true
        
        // Append user message
        state.chatHistory.append(ChatMessage(role: "user", text: instruction))
        
        // Append placeholder assistant message
        let placeholderMessage = ChatMessage(role: "assistant", text: "Application des modifications...")
        state.chatHistory.append(placeholderMessage)
        
        let cfg = AIConfig(from: state)
        
        Task {
            do {
                let newMarkdown = try await GenerationEngine.shared.editFiche(
                    currentMarkdown: state.generatedMarkdown,
                    instructions: instruction,
                    config: cfg,
                    onLog: { msg in state.appendLog(msg) },
                    onProgress: { p in state.progress = p }
                )
                
                await MainActor.run {
                    if let index = state.chatHistory.firstIndex(where: { $0.id == placeholderMessage.id }) {
                        state.chatHistory[index] = ChatMessage(role: "assistant", text: "Modification appliquée avec succès !")
                    }
                    state.generatedMarkdown = newMarkdown
                    isProcessing = false
                }
            } catch {
                await MainActor.run {
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
