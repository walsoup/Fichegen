import Foundation

public final class PreferencesStorage {
    private static let fileName = "settings.json"
    private static let bundleId = Bundle.main.bundleIdentifier ?? "com.fichegen.app"
    
    private static var storageURL: URL? {
        guard let appSupportDir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
            return nil
        }
        let appDir = appSupportDir.appendingPathComponent(bundleId, isDirectory: true)
        
        if !FileManager.default.fileExists(atPath: appDir.path) {
            do {
                try FileManager.default.createDirectory(at: appDir, withIntermediateDirectories: true, attributes: nil)
            } catch {
                print("Failed to create Application Support directory: \(error)")
                return nil
            }
        }
        
        return appDir.appendingPathComponent(fileName)
    }
    
    public static func save(_ settings: [String: Any]) {
        guard let url = storageURL else { return }
        do {
            let data = try JSONSerialization.data(withJSONObject: settings, options: [.prettyPrinted])
            try data.write(to: url, options: .atomic)
        } catch {
            print("Failed to save settings to JSON: \(error)")
        }
    }
    
    public static func load() -> [String: Any] {
        guard let url = storageURL, FileManager.default.fileExists(atPath: url.path) else {
            return [:]
        }
        do {
            let data = try Data(contentsOf: url)
            if let dict = try JSONSerialization.jsonObject(with: data, options: []) as? [String: Any] {
                return dict
            }
        } catch {
            print("Failed to load settings from JSON: \(error)")
        }
        return [:]
    }
}
