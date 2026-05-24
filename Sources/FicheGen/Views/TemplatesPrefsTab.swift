import SwiftUI

struct TemplatesPrefsTab: View {
    @AppStorage("selectedTemplatePath") private var selectedTemplatePath: String = ""
    @State private var importedTemplates: [String] = []

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
}
