import Foundation

/// All communication with the Python FastAPI backend.
@MainActor
final class APIClient: ObservableObject {

    static let shared = APIClient()
    let baseURL = URL(string: "http://127.0.0.1:58234")!
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    // MARK: - Health

    func isServerRunning() async -> Bool {
        let url = baseURL.appending(path: "/health")
        guard let (_, resp) = try? await URLSession.shared.data(from: url),
              let http = resp as? HTTPURLResponse else { return false }
        return http.statusCode == 200
    }

    // MARK: - Start a generation job

    func startFiche(_ req: FicheRequest) async throws -> String {
        return try await postJob(path: "/generate/fiche", body: req)
    }

    func startEvaluation(_ req: EvaluationRequest) async throws -> String {
        return try await postJob(path: "/generate/evaluation", body: req)
    }

    func startQuiz(_ req: QuizRequest) async throws -> String {
        return try await postJob(path: "/generate/quiz", body: req)
    }

    private func postJob<T: Encodable>(path: String, body: T) async throws -> String {
        var request = URLRequest(url: baseURL.appending(path: path))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try encoder.encode(body)
        let (data, _) = try await URLSession.shared.data(for: request)
        let job = try decoder.decode(JobResponse.self, from: data)
        return job.jobId
    }

    // MARK: - SSE stream

    /// Returns an AsyncStream of SSEEvents for a running job.
    func stream(jobId: String) -> AsyncStream<SSEEvent> {
        let url = baseURL.appending(path: "/stream/\(jobId)")
        return AsyncStream { continuation in
            Task {
                var request = URLRequest(url: url)
                request.setValue("text/event-stream", forHTTPHeaderField: "Accept")
                guard let (bytes, _) = try? await URLSession.shared.bytes(for: request) else {
                    continuation.finish()
                    return
                }
                var eventType = ""
                for try await line in bytes.lines {
                    if line.hasPrefix("event: ") {
                        eventType = String(line.dropFirst("event: ".count))
                    } else if line.hasPrefix("data: ") {
                        let raw = String(line.dropFirst("data: ".count))
                        if let event = parseSSE(type: eventType, raw: raw) {
                            continuation.yield(event)
                            if case .done = event { break }
                            if case .error = event { break }
                        }
                    }
                }
                continuation.finish()
            }
        }
    }

    private func parseSSE(type: String, raw: String) -> SSEEvent? {
        guard let data = raw.data(using: .utf8),
              let dict = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return nil }

        switch type {
        case "log":
            return .log(dict["message"] as? String ?? "")
        case "progress":
            return .progress(dict["value"] as? Int ?? 0)
        case "content":
            return .content(dict["markdown"] as? String ?? "")
        case "done":
            return .done(dict["message"] as? String ?? "")
        case "error":
            return .error(dict["message"] as? String ?? "Unknown error")
        default:
            return nil
        }
    }

    // MARK: - Cancel

    func cancel(jobId: String) async {
        var req = URLRequest(url: baseURL.appending(path: "/cancel/\(jobId)"))
        req.httpMethod = "POST"
        _ = try? await URLSession.shared.data(for: req)
    }

    // MARK: - Save

    func savePDF(markdown: String, template: String = "default") async throws -> String {
        return try await saveDoc(path: "/save/pdf", markdown: markdown, template: template)
    }

    func saveDOCX(markdown: String) async throws -> String {
        return try await saveDoc(path: "/save/docx", markdown: markdown, template: "default")
    }

    private func saveDoc(path: String, markdown: String, template: String) async throws -> String {
        var request = URLRequest(url: baseURL.appending(path: path))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let body = SaveRequest(markdown: markdown, template: template, classLevel: "", subject: "")
        request.httpBody = try encoder.encode(body)
        let (data, _) = try await URLSession.shared.data(for: request)
        let resp = try decoder.decode(SaveResponse.self, from: data)
        if let err = resp.error { throw URLError(.badServerResponse) }
        return resp.path ?? ""
    }

    // MARK: - Settings

    func fetchSettings() async throws -> [String: Any] {
        let (data, _) = try await URLSession.shared.data(from: baseURL.appending(path: "/settings"))
        return (try JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
    }

    func updateSettings(_ updates: [String: Any]) async throws {
        var request = URLRequest(url: baseURL.appending(path: "/settings"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let body: [String: Any] = ["updates": updates]
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        _ = try? await URLSession.shared.data(for: request)
    }

    // MARK: - Lessons

    func fetchLessons(classLevel: String) async -> [String] {
        guard let (data, _) = try? await URLSession.shared.data(
            from: baseURL.appending(path: "/lessons/\(classLevel)")
        ),
        let dict = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
        let lessons = dict["lessons"] as? [String]
        else { return [] }
        return lessons
    }

    // MARK: - Rating

    func saveRating(_ record: RatingRecord) async {
        var request = URLRequest(url: baseURL.appending(path: "/ratings"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? encoder.encode(record)
        _ = try? await URLSession.shared.data(for: request)
    }
}
