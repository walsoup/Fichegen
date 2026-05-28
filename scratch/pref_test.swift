import Foundation

let bundleId = "com.fichegen.app"
guard let appSupportDir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
    print("No app support dir")
    exit(1)
}
let appDir = appSupportDir.appendingPathComponent(bundleId, isDirectory: true)

if !FileManager.default.fileExists(atPath: appDir.path) {
    do {
        try FileManager.default.createDirectory(at: appDir, withIntermediateDirectories: true, attributes: nil)
    } catch {
        print("Failed to create Application Support directory: \(error)")
        exit(1)
    }
}

let url = appDir.appendingPathComponent("settings.json")
print("Path: \(url.path)")
if let data = try? Data(contentsOf: url), let str = String(data: data, encoding: .utf8) {
    print("Content:\n\(str)")
} else {
    print("File not found or unreadable.")
}
