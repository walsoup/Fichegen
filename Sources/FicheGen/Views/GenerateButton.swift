import SwiftUI

/// Reusable generate/cancel button with built-in loading state.
struct GenerateButton: View {
    @EnvironmentObject var state: AppState
    let label: String
    var isEnabled: Bool = true
    let action: () -> Void

    var body: some View {
        HStack {
            Button(action: state.isGenerating ? state.cancelGeneration : action) {
                if state.isGenerating {
                    Label("Annuler", systemImage: "stop.fill")
                        .frame(maxWidth: .infinity)
                } else {
                    Label(label, systemImage: "sparkles")
                        .frame(maxWidth: .infinity)
                }
            }
            .controlSize(.large)
            .buttonStyle(.borderedProminent)
            .tint(state.isGenerating ? .red : .accentColor)
            .disabled(!state.isGenerating && !isEnabled)
            .keyboardShortcut(state.isGenerating ? KeyEquivalent.escape : KeyEquivalent.return, modifiers: state.isGenerating ? [] : [.command])
        }
    }
}
