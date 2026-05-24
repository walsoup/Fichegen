import SwiftUI

@main
struct FicheGenApp: App {
    #if os(macOS)
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    #endif

    @StateObject private var state = AppState()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(state)
                .onAppear {
                    // Load all settings from JSON (no Python needed)
                    Task { await state.fetchSettingsFromServer() }
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

#if os(macOS)
class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        // Read directly from AppState (via defaults or preferences storage if needed)
        // Since PreferencesStorage is JSON, we can load it to check the setting.
        let settings = PreferencesStorage.load()
        if let quitOnClose = settings["quit_on_close"] as? Bool {
            return quitOnClose
        }
        return true // Default to true
    }
}
#endif
}
