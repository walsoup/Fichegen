import SwiftUI

struct TemplatesPrefsTab: View {
    @EnvironmentObject var state: AppState
    @AppStorage("selectedTemplatePath") private var selectedTemplatePath: String = ""
    @State private var importedTemplates: [String] = []
    
    @State private var isGeneratingStyle = false
    @State private var styleGenStatus = ""

    var body: some View {
        Form {
            Section("Modèle de structure (.md)") {
                HStack {
                    Text("Modèle sélectionné :")
                        .frame(width: 140, alignment: .trailing)
                    
                    TextField("Aucun modèle", text: $selectedTemplatePath)
                        .disabled(true)
                        .textFieldStyle(.roundedBorder)
                    
                    Button("Importer…") {
                        importTemplate()
                    }
                }
                if !selectedTemplatePath.isEmpty {
                    Button("Réinitialiser le modèle") {
                        selectedTemplatePath = ""
                    }
                    .foregroundStyle(.red)
                    .padding(.leading, 148)
                }
                
                Text("Utilisez un fichier .md contenant la structure (ex: # Titre, ## Sous-titre) pour guider la génération de l'IA.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .padding(.top, 4)
            }
        }
        
        Form {
            Section("Styles & Thèmes (CSS)") {
                if state.stylePresets.isEmpty {
                    Text("Aucun style personnalisé.")
                        .foregroundColor(.secondary)
                } else {
                    List {
                        ForEach(state.stylePresets) { preset in
                            HStack {
                                Text(preset.name)
                                Spacer()
                                Button(role: .destructive) {
                                    state.stylePresets.removeAll { $0.id == preset.id }
                                    state.saveSettingsToServer()
                                } label: {
                                    Image(systemName: "trash")
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                    .frame(minHeight: 100)
                }
            }
            
            Section("Générateur de Style IA (Expérimental)") {
                Text("Fournissez un document de référence (ex: un manuel) et l'IA en extraira l'identité visuelle pour générer un thème CSS compatible avec FicheGen.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                
                if isGeneratingStyle {
                    HStack {
                        ProgressView().controlSize(.small)
                        Text(styleGenStatus)
                            .font(.subheadline)
                    }
                } else {
                    Button("Créer un style à partir d'un PDF...") {
                        generateStyleFromPDF()
                    }
                }
            }
        }
        .formStyle(.grouped)
        .presentationBackground(.ultraThinMaterial)
    }
    
    private func importTemplate() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowedContentTypes = [.utf8PlainText] // .md and .txt
        
        panel.begin { response in
            if response == .OK, let url = panel.url {
                self.selectedTemplatePath = url.path
            }
        }
    }
    
    private func generateStyleFromPDF() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.pdf]
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        
        panel.begin { response in
            if response == .OK, let url = panel.url {
                Task {
                    await performStyleGeneration(pdfURL: url)
                }
            }
        }
    }
    
    private func performStyleGeneration(pdfURL: URL) async {
        isGeneratingStyle = true
        styleGenStatus = "Extraction du texte..."
        
        // 1. Extract Text
        let text = await Task.detached(priority: .userInitiated) {
            PDFProcessor.extractText(from: pdfURL, pages: [1, 2, 3, 4, 5])
        }.value
        
        if text.isEmpty {
            styleGenStatus = "Erreur : PDF vide ou illisible."
            try? await Task.sleep(nanoseconds: 2_000_000_000)
            isGeneratingStyle = false
            return
        }
        
        styleGenStatus = "Génération du CSS par l'IA..."
        let prompt = """
        Voici un extrait de texte issu d'un document ou d'un manuel scolaire.
        Analyse ce texte et déduis-en une identité visuelle et un style de mise en page.
        Génère ensuite un code CSS pur (sans balises HTML) qui pourra être utilisé pour styliser le rendu HTML (body, h1, h2, h3, p, table, th, td, blockquote, code) d'une fiche de révision.
        Le style doit être moderne, élégant, et utiliser des couleurs inspirées par le contenu si possible.
        Renvoie UNIQUEMENT le code CSS, sans blocs markdown (```).
        
        Extrait:
        \(String(text.prefix(3000)))
        """
        
        do {
            let model = state.chatModel.isEmpty ? "gemini-2.5-pro" : state.chatModel
            let cfg = AIConfig(from: state)
            let result = try await GeminiClient.shared.generate(
                prompt: prompt,
                purpose: "chat",
                temperature: 0.7,
                responseJSON: false,
                config: cfg
            )
            
            let cleanCSS = result.replacingOccurrences(of: "```css", with: "")
                                 .replacingOccurrences(of: "```", with: "")
                                 .trimmingCharacters(in: CharacterSet.whitespacesAndNewlines)
            
            let preset = StylePreset(name: "Style généré (\(pdfURL.lastPathComponent))", css: cleanCSS)
            
            await MainActor.run {
                state.stylePresets.append(preset)
                state.saveSettingsToServer()
                styleGenStatus = "Terminé !"
            }
        } catch {
            await MainActor.run {
                styleGenStatus = "Erreur: \(error.localizedDescription)"
            }
        }
        
        try? await Task.sleep(nanoseconds: 2_000_000_000)
        isGeneratingStyle = false
    }
}
