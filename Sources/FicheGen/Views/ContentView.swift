import SwiftUI

struct ContentView: View {
    @EnvironmentObject var state: AppState
    @State private var selectedTab: Tab = .fiche
    @State private var showChatInspector: Bool = false

    enum Tab: String, CaseIterable, Identifiable {
        case fiche = "Fiches"
        case evaluation = "Évaluations"
        case quiz = "Quiz"
        var id: String { rawValue }
        var icon: String {
            switch self {
            case .fiche:       return "doc.text"
            case .evaluation:  return "checkmark.square"
            case .quiz:        return "questionmark.circle"
            }
        }
    }

    var body: some View {
        NavigationSplitView {
            // Sidebar
            List(Tab.allCases, selection: $selectedTab) { tab in
                Label(tab.rawValue, systemImage: tab.icon)
                    .tag(tab)
            }
            .navigationSplitViewColumnWidth(min: 160, ideal: 180)
            .listStyle(.sidebar)
        } detail: {
            HSplitView {
                // Form panel
                formPanel
                    .frame(minWidth: 340, idealWidth: 380, maxWidth: 460)

                // Result panel
                ResultPanel(showChatInspector: $showChatInspector)
                    .frame(minWidth: 480)
            }
        }
        .inspector(isPresented: $showChatInspector) {
            ChatPanelView(isPresented: $showChatInspector)
                .inspectorColumnWidth(min: 280, ideal: 360, max: 600)
        }
        .navigationTitle("FicheGen")
        .presentationBackground(.ultraThinMaterial)
        .background(.ultraThinMaterial)
        .animation(.spring(), value: selectedTab)
    }

    @ViewBuilder
    private var formPanel: some View {
        switch selectedTab {
        case .fiche:       FicheFormView()
        case .evaluation:  EvaluationFormView()
        case .quiz:        QuizFormView()
        }
    }

}
