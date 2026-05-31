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
    
    // MARK: - Keychain
    
    private static func saveToKeychain(key: String, value: String) {
        let addQuery: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleId,
            kSecAttrAccount as String: key,
            kSecValueData as String: value.data(using: .utf8)!
        ]
        
        let deleteQuery: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleId,
            kSecAttrAccount as String: key
        ]
        SecItemDelete(deleteQuery as CFDictionary)
        
        let status = SecItemAdd(addQuery as CFDictionary, nil)
        if status != errSecSuccess {
            print("Keychain Save Error: \\(status) for key: \\(key)")
        }
    }
    
    private static func loadFromKeychain(key: String) -> String? {
        let getQuery: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleId,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true
        ]
        
        var item: CFTypeRef?
        let status = SecItemCopyMatching(getQuery as CFDictionary, &item)
        
        if status == errSecSuccess, let data = item as? Data {
            return String(data: data, encoding: .utf8)
        }
        return nil
    }

    public static func save(_ settings: [String: Any]) {
        var jsonSettings = settings
        
        // Extract sensitive keys and save to keychain
        let sensitiveKeys = ["gemini_api_key", "proxy_api_key", "vercel_api_key"]
        for key in sensitiveKeys {
            if let value = settings[key] as? String {
                saveToKeychain(key: key, value: value)
            }
            jsonSettings.removeValue(forKey: key)
        }
        
        guard let url = storageURL else { return }
        do {
            let data = try JSONSerialization.data(withJSONObject: jsonSettings, options: [.prettyPrinted])
            try data.write(to: url, options: .atomic)
        } catch {
            print("Failed to save settings to JSON: \(error)")
        }
    }
    
    public static func load() -> [String: Any] {
        var settings: [String: Any] = [:]
        
        if let url = storageURL, FileManager.default.fileExists(atPath: url.path) {
            do {
                let data = try Data(contentsOf: url)
                if let dict = try JSONSerialization.jsonObject(with: data, options: []) as? [String: Any] {
                    settings = dict
                }
            } catch {
                print("Failed to load settings from JSON: \(error)")
            }
        }
        
        // Load sensitive keys from keychain
        let sensitiveKeys = ["gemini_api_key", "proxy_api_key", "vercel_api_key"]
        for key in sensitiveKeys {
            if let val = loadFromKeychain(key: key) {
                settings[key] = val
            }
        }
        
        return settings
    }
}
