import SwiftUI

struct HistoryView: View {
    @EnvironmentObject var state: AppState
    @State private var searchText = ""
    
    var filteredHistory: [GenerationHistoryItem] {
        let sorted = state.generationHistory.sorted { 
            if $0.isFavorite == $1.isFavorite { return $0.date > $1.date }
            return $0.isFavorite && !$1.isFavorite
        }
        if searchText.isEmpty {
            return sorted
        } else {
            return sorted.filter { 
                $0.topic.localizedCaseInsensitiveContains(searchText) || 
                $0.classLevel.localizedCaseInsensitiveContains(searchText) ||
                $0.subject.localizedCaseInsensitiveContains(searchText) ||
                $0.type.rawValue.localizedCaseInsensitiveContains(searchText)
            }
        }
    }
    
    var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Image(systemName: "clock.fill")
                    .font(.title2)
                    .foregroundColor(.accentColor)
                Text("Historique des Générations")
                    .font(.headline)
                Spacer()
            }
            .padding()
            .background(Color(nsColor: .windowBackgroundColor))
            
            Divider()
            
            // Search
            HStack {
                Image(systemName: "magnifyingglass")
                    .foregroundColor(.secondary)
                TextField("Rechercher...", text: $searchText)
                    .textFieldStyle(.plain)
                if !searchText.isEmpty {
                    Button(action: { searchText = "" }) {
                        Image(systemName: "xmark.circle.fill")
                            .foregroundColor(.secondary)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(8)
            .background(.ultraThinMaterial)
            .cornerRadius(8)
            .padding()
            
            // List
            if state.generationHistory.isEmpty {
        VStack(spacing: 12) {
            Spacer()
            Image(systemName: "tray")
                .font(.system(size: 40))
                .foregroundColor(.secondary)
            Text("Aucune génération dans l'historique")
                .font(.headline)
            Text("Vos créations apparaîtront ici.")
                        .font(.subheadline)
                        .foregroundColor(.secondary)
                        .multilineTextAlignment(.center)
                    Spacer()
                }
            } else {
                List {
                    ForEach(filteredHistory) { item in
                        HistoryRow(item: item)
                            .contentShape(Rectangle())
                            .onTapGesture {
                                // Load this item into the preview
                                state.generatedMarkdown = item.markdown
                                state.chatHistory.removeAll()
                                state.markdownHistory.removeAll()
                                state.appendLog("ℹ️ \(item.type.rawValue) chargée depuis l'historique : \(item.topic)")
                            }
                            .contextMenu {
                                Button {
                                    if let index = state.generationHistory.firstIndex(where: { $0.id == item.id }) {
                                        state.generationHistory[index].isFavorite.toggle()
                                        HistoryStorage.save(state.generationHistory)
                                    }
                                } label: {
                                    Label(item.isFavorite ? "Retirer des favoris" : "Ajouter aux favoris", systemImage: item.isFavorite ? "star.slash" : "star")
                                }
                                
                                Button(role: .destructive) {
                                    if let index = state.generationHistory.firstIndex(where: { $0.id == item.id }) {
                                        state.generationHistory.remove(at: index)
                                        HistoryStorage.save(state.generationHistory)
                                    }
                                } label: {
                                    Label("Supprimer", systemImage: "trash")
                                }
                            }
                    }
                }
                .listStyle(.sidebar)
            }
        }
        .background(Color(nsColor: .windowBackgroundColor))
    }
}

struct HistoryRow: View {
    let item: GenerationHistoryItem
    
    private let formatter: DateFormatter = {
        let df = DateFormatter()
        df.dateStyle = .short
        df.timeStyle = .short
        return df
    }()
    
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(item.topic.isEmpty ? "Document sans titre" : item.topic)
                    .font(.headline)
                    .lineLimit(1)
                Spacer()
                if item.isFavorite {
                    Image(systemName: "star.fill")
                        .foregroundColor(.yellow)
                        .font(.caption)
                }
            }
            
            HStack {
                Text(item.type.rawValue)
                    .font(.caption)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Color.purple.opacity(0.1))
                    .cornerRadius(4)
                    .foregroundColor(.purple)
                
                Text(item.classLevel)
                    .font(.caption)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Color.accentColor.opacity(0.1))
                    .cornerRadius(4)
                    .foregroundColor(.accentColor)
                
                Text(item.subject.isEmpty ? "Sujet non spécifié" : item.subject)
                    .font(.caption)
                    .foregroundColor(.secondary)
                    .lineLimit(1)
            }
            
            Text(formatter.string(from: item.date))
                .font(.caption2)
                .foregroundColor(.secondary)
        }
        .padding(.vertical, 4)
    }
}
