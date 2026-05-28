import Foundation

// MARK: - Generation Request

struct FicheRequest: Encodable {
    let classLevel: String
    let lessonTopic: String
    let pagesOverride: String
    let temperature: Double
    let durationMinutes: Int
    let subject: String
    let specialInstructions: String
    let generateImage: Bool
    let useStudentTextbook: Bool
    let useTopRatedExamples: Bool

    enum CodingKeys: String, CodingKey {
        case classLevel = "class_level"
        case lessonTopic = "lesson_topic"
        case pagesOverride = "pages_override"
        case temperature
        case durationMinutes = "duration_minutes"
        case subject
        case specialInstructions = "special_instructions"
        case generateImage = "generate_image"
        case useStudentTextbook = "use_student_textbook"
        case useTopRatedExamples = "use_top_rated_examples"
    }
}

struct EvaluationRequest: Encodable {
    let classLevel: String
    let topicsList: [String]
    let subject: String
    let duration: Int
    let questionTypes: [String]
    let difficulty: String
    let temperature: Double
    let extraInstructions: String

    enum CodingKeys: String, CodingKey {
        case classLevel = "class_level"
        case topicsList = "topics_list"
        case subject, duration
        case questionTypes = "question_types"
        case difficulty, temperature
        case extraInstructions = "extra_instructions"
    }
}

struct QuizRequest: Encodable {
    let classLevel: String
    let topic: String
    let subject: String
    let duration: Int
    let numQuestions: Int
    let questionTypes: [String]
    let difficulty: String
    let temperature: Double

    enum CodingKeys: String, CodingKey {
        case classLevel = "class_level"
        case topic, subject, duration
        case numQuestions = "num_questions"
        case questionTypes = "question_types"
        case difficulty, temperature
    }
}

// MARK: - SSE Events

enum SSEEvent {
    case log(String)
    case progress(Int)
    case content(String)
    case done(String)
    case error(String)
}

// MARK: - Job Response

struct JobResponse: Decodable {
    let jobId: String
    enum CodingKeys: String, CodingKey { case jobId = "job_id" }
}

// MARK: - Save

struct SaveRequest: Encodable {
    let markdown: String
    let template: String
    let classLevel: String
    let subject: String
    enum CodingKeys: String, CodingKey {
        case markdown, template
        case classLevel = "class_level"
        case subject
    }
}

struct SaveResponse: Decodable {
    let ok: Bool
    let path: String?
    let error: String?
}

// MARK: - Rating

struct RatingRecord: Encodable {
    let topic: String
    let classLevel: String
    let rating: Int
    let content: String
    let timestamp: String
    enum CodingKeys: String, CodingKey {
        case topic
        case classLevel = "class_level"
        case rating, content, timestamp
    }
}

// MARK: - Chat Message

struct DiffLine: Identifiable, Equatable {
    let id = UUID()
    enum Kind: String, Codable {
        case added
        case removed
        case unchanged
    }
    let kind: Kind
    let text: String
}

struct ChatMessage: Identifiable, Equatable {
    let id = UUID()
    let role: String // "user" or "assistant"
    var text: String
    let timestamp = Date()
    var diffLines: [DiffLine]? = nil
}
