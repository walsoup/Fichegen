import SwiftUI

struct ChatPanelView: View {
    @EnvironmentObject var state: AppState
    @State private var inputText: String = ""
    @State private var isProcessing: Bool = false

    var body: some View {
        VStack(spacing: 0) {
            Text("Magical Assistant")
                .font(.headline)
                .padding()
                .frame(maxWidth: .infinity)
                .background(.regularMaterial)
            
            ScrollView {
                VStack(spacing: 12) {
                    ForEach(state.chatHistory) { message in
                        HStack {
                            if message.role == "user" {
                                Spacer()
                                Text(message.text)
                                    .padding(10)
                                    .background(Color.blue.opacity(0.8))
                                    .foregroundColor(.white)
                                    .cornerRadius(12)
                            } else {
                                Text(message.text)
                                    .padding(10)
                                    .background(Color.gray.opacity(0.2))
                                    .cornerRadius(12)
                                Spacer()
                            }
                        }
                        .padding(.horizontal)
                    }
                }
                .padding(.vertical)
            }
            .background(Color.clear)
            
            VStack {
                HStack {
                    TextField("Demander une modification...", text: $inputText)
                        .textFieldStyle(RoundedBorderTextFieldStyle())
                        .disabled(isProcessing)
                        .onSubmit {
                            sendMessage()
                        }
                    
                    Button(action: sendMessage) {
                        Image(systemName: "paperplane.fill")
                            .foregroundColor(inputText.isEmpty || isProcessing ? .gray : .blue)
                    }
                    .disabled(inputText.isEmpty || isProcessing)
                    .buttonStyle(.plain)
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
        let placeholderMessage = ChatMessage(role: "assistant", text: "...")
        state.chatHistory.append(placeholderMessage)
        
        Task {
            do {
                // Here we call the user's assumed method:
                // GenerationEngine.shared.editFiche(instruction: String, markdown: String)
                // However, the user said "assume editFiche takes instruction: String and markdown: String, returning new markdown."
                // "I will update GenerationEngine myself"
                let newMarkdown = try await GenerationEngine.shared.editFiche(instruction: instruction, markdown: state.generatedMarkdown)
                
                await MainActor.run {
                    if let index = state.chatHistory.firstIndex(where: { $0.id == placeholderMessage.id }) {
                        state.chatHistory[index] = ChatMessage(role: "assistant", text: "Modification appliquée !")
                    } else {
                        state.chatHistory.append(ChatMessage(role: "assistant", text: "Modification appliquée !"))
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
}
