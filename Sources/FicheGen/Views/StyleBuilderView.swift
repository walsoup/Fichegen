import SwiftUI

extension Color: RawRepresentable {
    public init?(rawValue: String) {
        guard let data = Data(base64Encoded: rawValue) else { return nil }
        do {
            if let nsColor = try NSKeyedUnarchiver.unarchivedObject(ofClass: NSColor.self, from: data) {
                self = Color(nsColor: nsColor)
                return
            }
        } catch { }
        return nil
    }

    public var rawValue: String {
        do {
            let data = try NSKeyedArchiver.archivedData(withRootObject: NSColor(self), requiringSecureCoding: false)
            return data.base64EncodedString()
        } catch {
            return ""
        }
    }
}

struct StyleBuilderSections: View {
    @AppStorage("customPdfPrimaryColor") private var primaryColor: Color = Color.accentColor
    @AppStorage("customPdfSecondaryColor") private var secondaryColor: Color = Color(nsColor: .secondaryLabelColor)
    @AppStorage("customPdfFont") private var fontName: String = "Helvetica"
    @AppStorage("customPdfMargin") private var margin: Double = 20.0
    @State private var didMigrateLegacyColors = false
    
    let availableFonts = ["Helvetica", "Times New Roman", "Courier", "Avenir", "Georgia", "San Francisco"]

    var body: some View {
        Group {
            Section("Couleurs du PDF") {
                ColorPicker("Couleur Principale", selection: $primaryColor)
                ColorPicker("Couleur Secondaire", selection: $secondaryColor)
            }
            
            Section("Typographie") {
                Picker("Police par défaut", selection: $fontName) {
                    ForEach(availableFonts, id: \.self) { font in
                        Text(font).tag(font)
                    }
                }
                .pickerStyle(.menu)
            }
            
            Section("Marges") {
                VStack {
                    HStack {
                        Text("Marge globale :")
                        Spacer()
                        Text("\(Int(margin)) mm")
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                    }
                    Slider(value: $margin, in: 10...50, step: 1)
                }
            }
            
            Section("Aperçu") {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Titre de la fiche")
                        .font(.custom(fontName, size: 24))
                        .foregroundStyle(primaryColor)
                        .padding(.bottom, 4)
                    
                    Text("Ceci est un paragraphe de texte pour prévisualiser le rendu du PDF généré. Il utilise la police sélectionnée et les marges configurées.")
                        .font(.custom(fontName, size: 14))
                        .foregroundStyle(secondaryColor)
                }
                .padding(margin)
                .background(Color(nsColor: .textBackgroundColor))
                .cornerRadius(8)
                .shadow(radius: 2)
            }
        }
        .onAppear(perform: migrateLegacyColorDefaultsIfNeeded)
    }

    private func migrateLegacyColorDefaultsIfNeeded() {
        guard !didMigrateLegacyColors else { return }
        didMigrateLegacyColors = true

        let defaults = UserDefaults.standard
        if defaults.string(forKey: "customPdfPrimaryColor") == Color.blue.rawValue {
            defaults.set(Color(nsColor: .controlAccentColor).rawValue, forKey: "customPdfPrimaryColor")
        }
        if defaults.string(forKey: "customPdfSecondaryColor") == Color.gray.rawValue {
            defaults.set(Color(nsColor: .secondaryLabelColor).rawValue, forKey: "customPdfSecondaryColor")
        }
    }
}

struct StyleBuilderView: View {
    var body: some View {
        Form {
            StyleBuilderSections()
        }
        .formStyle(.grouped)
        .padding()
        .presentationBackground(.ultraThinMaterial)
    }
}
