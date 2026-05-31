import Foundation

public struct StylePreset: Codable, Identifiable, Equatable {
    public var id: UUID
    public var name: String
    public var css: String
    
    public init(id: UUID = UUID(), name: String, css: String) {
        self.id = id
        self.name = name
        self.css = css
    }
}
