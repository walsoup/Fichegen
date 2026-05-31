import Foundation

public enum GenerationType: String, Codable {
    case fiche = "Fiche"
    case evaluation = "Évaluation"
    case quiz = "Quiz"
}

public struct GenerationHistoryItem: Codable, Identifiable, Equatable {
    public let id: UUID
    public let date: Date
    public let type: GenerationType
    public let classLevel: String
    public let subject: String
    public let topic: String
    public let markdown: String
    public var isFavorite: Bool
    
    public init(id: UUID = UUID(), date: Date = Date(), type: GenerationType = .fiche, classLevel: String, subject: String, topic: String, markdown: String, isFavorite: Bool = false) {
        self.id = id
        self.date = date
        self.type = type
        self.classLevel = classLevel
        self.subject = subject
        self.topic = topic
        self.markdown = markdown
        self.isFavorite = isFavorite
    }
}
