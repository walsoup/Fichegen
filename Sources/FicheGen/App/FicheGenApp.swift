import SwiftUI

@main
struct FicheGenApp: App {
    @StateObject private var state = AppState()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(state)
                .onAppear {
                    // Load all settings from Keychain + UserDefaults (no Python needed)
                    Task { await state.fetchSettingsFromServer() }
                    state.loadAvailableLessons(classLevel: state.ficheClassLevel)
                }
        }
        .windowStyle(.titleBar)
        .windowToolbarStyle(.unified)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandMenu("Génération") {
                Button("Générer une Fiche") { state.generateFiche() }
                    .keyboardShortcut("g", modifiers: [.command])
                    .disabled(state.isGenerating)
                Button("Générer une Évaluation") { state.generateEvaluation() }
                    .keyboardShortcut("e", modifiers: [.command, .shift])
                    .disabled(state.isGenerating)
                Button("Générer un Quiz") { state.generateQuiz() }
                    .keyboardShortcut("q", modifiers: [.command, .shift])
                    .disabled(state.isGenerating)
                Divider()
                Button("Annuler") { state.cancelGeneration() }
                    .keyboardShortcut(".", modifiers: [.command])
                    .disabled(!state.isGenerating)
            }
        }

        #if os(macOS)
        Settings {
            PreferencesView()
                .environmentObject(state)
        }
        #endif
    }
}
