import Foundation

public class HistoryStorage {
    private static let fileName = "history.json"
    
    private static var storageURL: URL? {
        guard let appSupportURL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return nil }
        let appDir = appSupportURL.appendingPathComponent("FicheGen", isDirectory: true)
        
        if !FileManager.default.fileExists(atPath: appDir.path) {
            try? FileManager.default.createDirectory(at: appDir, withIntermediateDirectories: true, attributes: nil)
        }
        
        return appDir.appendingPathComponent(fileName)
    }
    
    public static func load() -> [GenerationHistoryItem] {
        guard let url = storageURL, FileManager.default.fileExists(atPath: url.path) else { return [] }
        
        do {
            let data = try Data(contentsOf: url)
            let decoder = JSONDecoder()
            let history = try decoder.decode([GenerationHistoryItem].self, from: data)
            // Sort by date descending (newest first)
            return history.sorted { $0.date > $1.date }
        } catch {
            print("Error loading history: \\(error)")
            return []
        }
    }
    
    public static func save(_ history: [GenerationHistoryItem]) {
        guard let url = storageURL else { return }
        
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = .prettyPrinted
            let data = try encoder.encode(history)
            try data.write(to: url, options: .atomic)
        } catch {
            print("Error saving history: \\(error)")
        }
    }
}
