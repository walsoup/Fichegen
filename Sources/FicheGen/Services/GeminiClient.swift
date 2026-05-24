import Foundation

// MARK: - Provider Enum

enum AIProvider: String, CaseIterable {
    case gemini   = "gemini"
    case vertex   = "vertex"
    case proxy    = "proxy"
    case vercel   = "vercel"
    case `default` = "default"
}

// MARK: - Value-type config snapshot (no actor isolation issues)

/// Snapshot of all routing & credential settings captured on MainActor,
/// safe to pass into any actor or Task.
struct AIConfig: Sendable {
    // Credentials
    let geminiApiKey: String
    let proxyApiKey: String
    let vercelApiKey: String
    // Global routing
    let apiRoute: String          // "aistudio" | "vertex"
    let proxyEnabled: Bool
    let proxyBaseURL: String
    let vercelEnabled: Bool
    let vercelBaseURL: String
    // Vertex
    let vertexProject: String
    let vertexLocation: String
    // Models
    let geminiModel: String
    // Per-function routing
    let routingFicheProvider: String;  let routingFicheModel: String
    let routingEvalProvider: String;   let routingEvalModel: String
    let routingQuizProvider: String;   let routingQuizModel: String
    let routingTocProvider: String;    let routingTocModel: String
    let routingOffsetProvider: String; let routingOffsetModel: String
    let routingSyntaxProvider: String; let routingSyntaxModel: String
    // Experimental
    let expMaxRetries: Int
    let expRequestTimeout: Int
    let expMultiPassGen: Bool
    let expMultiPassIterations: Int

    /// Capture current AppState values — call from @MainActor context only.
    @MainActor
    init(from state: AppState) {
        geminiApiKey        = state.geminiApiKey
        proxyApiKey         = state.proxyApiKey
        vercelApiKey        = state.vercelApiKey
        apiRoute            = state.apiRoute
        proxyEnabled        = state.proxyEnabled
        proxyBaseURL        = state.proxyBaseURL
        vercelEnabled       = state.vercelEnabled
        vercelBaseURL       = state.vercelBaseURL
        vertexProject       = state.vertexProject
        vertexLocation      = state.vertexLocation
        geminiModel         = state.geminiModel
        routingFicheProvider  = state.routingFicheProvider
        routingFicheModel     = state.routingFicheModel
        routingEvalProvider   = state.routingEvalProvider
        routingEvalModel      = state.routingEvalModel
        routingQuizProvider   = state.routingQuizProvider
        routingQuizModel      = state.routingQuizModel
        routingTocProvider    = state.routingTocProvider
        routingTocModel       = state.routingTocModel
        routingOffsetProvider = state.routingOffsetProvider
        routingOffsetModel    = state.routingOffsetModel
        routingSyntaxProvider = state.routingSyntaxProvider
        routingSyntaxModel    = state.routingSyntaxModel
        expMaxRetries       = state.expMaxRetries
        expRequestTimeout   = state.expRequestTimeout
        expMultiPassGen     = state.expMultiPassGen
        expMultiPassIterations = state.expMultiPassIterations
    }
}

// MARK: - Request / Response types

private struct GeminiMessage: Codable {
    let role: String
    let parts: [GeminiPart]
}
private struct GeminiPart: Codable { let text: String }

private struct GeminiRequest: Codable {
    let contents: [GeminiMessage]
    let generationConfig: GeminiGenerationConfig
}

private struct GeminiGenerationConfig: Codable {
    let temperature: Double
    let responseMimeType: String?
    let thinkingConfig: GeminiThinkingConfig?
    enum CodingKeys: String, CodingKey {
        case temperature, responseMimeType, thinkingConfig
    }
}

private struct GeminiThinkingConfig: Codable {
    let thinkingBudget: Int?
}

private struct GeminiResponse: Codable {
    struct Candidate: Codable {
        struct Content: Codable {
            let parts: [GeminiPart]
        }
        let content: Content
        let finishReason: String?
    }
    let candidates: [Candidate]?
    let error: GeminiAPIError?
}

private struct GeminiAPIError: Codable {
    let code: Int
    let message: String
}

// MARK: - OpenAI-compatible proxy (Ollama / OpenRouter / custom)

private struct OpenAIChatRequest: Codable {
    struct Message: Codable { let role: String; let content: String }
    struct ResponseFormat: Codable { let type: String }
    let model: String
    let messages: [Message]
    let temperature: Double
    let response_format: ResponseFormat?  // swiftlint:disable:this identifier_name
}

private struct OpenAIChatResponse: Codable {
    struct Choice: Codable {
        struct Message: Codable { let content: String }
        let message: Message
    }
    let choices: [Choice]?
    let error: OpenAIProxyError?
}

private struct OpenAIProxyError: Codable { let message: String }

// MARK: - GeminiClient

/// Native Swift replacement for core/ai.py.
/// `actor` isolation ensures URLSession calls are safe across concurrency domains.
/// All `AppState` access is done via `AIConfig` (a captured value-type snapshot).
actor GeminiClient {

    static let shared = GeminiClient()
    private let session: URLSession

    init() {
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest  = 120
        cfg.timeoutIntervalForResource = 300
        session = URLSession(configuration: cfg)
    }

    // MARK: - Public entry point

    /// Generate content using the routing hierarchy in `config`.
    /// `config` must be created on `@MainActor` via `AIConfig(from: appState)`.
    func generate(
        prompt: String,
        purpose: String,
        temperature: Double,
        responseJSON: Bool = false,
        config: AIConfig
    ) async throws -> String {

        let (provider, model) = resolveRouting(purpose: purpose, config: config)

        switch provider {
        case .proxy:
            return try await callProxy(
                baseURL: config.proxyBaseURL,
                apiKey: config.proxyApiKey,
                model: model ?? "gpt-4o-mini",
                prompt: prompt,
                temperature: temperature,
                responseJSON: responseJSON,
                timeout: config.expRequestTimeout,
                maxRetries: config.expMaxRetries
            )
        case .vertex:
            return try await callVertex(
                project: config.vertexProject,
                location: config.vertexLocation,
                model: model ?? config.geminiModel,
                prompt: prompt,
                temperature: temperature,
                responseJSON: responseJSON,
                maxRetries: config.expMaxRetries
            )
        case .vercel:
            return try await callVercel(
                baseURL: config.vercelBaseURL,
                apiKey: config.vercelApiKey,
                model: model ?? "gemini-3.5-flash",
                prompt: prompt,
                temperature: temperature,
                responseJSON: responseJSON,
                timeout: config.expRequestTimeout,
                maxRetries: config.expMaxRetries
            )
        default: // .gemini / .default
            return try await callGeminiAIStudio(
                apiKey: config.geminiApiKey,
                model: model ?? config.geminiModel,
                prompt: prompt,
                temperature: temperature,
                responseJSON: responseJSON,
                maxRetries: config.expMaxRetries
            )
        }
    }

    // MARK: - Routing resolution

    private func resolveRouting(
        purpose: String,
        config: AIConfig
    ) -> (provider: AIProvider, model: String?) {

        let funcKey = funcKeyFor(purpose: purpose)

        var perProvider: AIProvider = .default
        var perModel: String? = nil

        if let key = funcKey {
            perProvider = AIProvider(rawValue: providerForFunc(key, config: config)) ?? .default
            let m = modelForFunc(key, config: config)
            perModel = m.isEmpty ? nil : m
        }

        // Resolve .default → global setting
        if perProvider == .default {
            if config.proxyEnabled {
                perProvider = .proxy
            } else if config.vercelEnabled {
                perProvider = .vercel
            } else if config.apiRoute == "vertex" {
                perProvider = .vertex
            } else {
                perProvider = .gemini
            }
        }

        return (perProvider, perModel)
    }

    private func funcKeyFor(purpose: String) -> String? {
        let p = purpose.lowercased()
        if p.contains("fiche")  { return "fiche" }
        if p.contains("eval")   { return "eval" }
        if p.contains("quiz")   { return "quiz" }
        if p.contains("toc")    { return "toc" }
        if p.contains("offset") { return "offset" }
        if p.contains("syntax") { return "syntax" }
        return nil
    }

    private func providerForFunc(_ key: String, config: AIConfig) -> String {
        switch key {
        case "fiche":  return config.routingFicheProvider
        case "eval":   return config.routingEvalProvider
        case "quiz":   return config.routingQuizProvider
        case "toc":    return config.routingTocProvider
        case "offset": return config.routingOffsetProvider
        case "syntax": return config.routingSyntaxProvider
        default:       return "default"
        }
    }

    private func modelForFunc(_ key: String, config: AIConfig) -> String {
        switch key {
        case "fiche":  return config.routingFicheModel
        case "eval":   return config.routingEvalModel
        case "quiz":   return config.routingQuizModel
        case "toc":    return config.routingTocModel
        case "offset": return config.routingOffsetModel
        case "syntax": return config.routingSyntaxModel
        default:       return ""
        }
    }

    // MARK: - Gemini AI Studio

    func callGeminiAIStudio(
        apiKey: String,
        model: String,
        prompt: String,
        temperature: Double,
        responseJSON: Bool,
        maxRetries: Int = 2
    ) async throws -> String {
        guard !apiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw AIClientError.missingAPIKey(
                "Gemini API key is not configured. Add it in Preferences → IA & Modèles."
            )
        }

        let urlStr = "https://generativelanguage.googleapis.com/v1beta/models/\(model):generateContent?key=\(apiKey)"
        guard let url = URL(string: urlStr) else { throw AIClientError.invalidURL(urlStr) }

        let body = makeGeminiBody(prompt: prompt, temperature: temperature, responseJSON: responseJSON, model: model)

        return try await withRetry(maxRetries) {
            try await self.post(url: url, body: body, as: GeminiResponse.self) { resp in
                if let err = resp.error { throw AIClientError.apiError("Gemini: \(err.message)") }
                guard let text = resp.candidates?.first?.content.parts.first?.text else {
                    throw AIClientError.emptyResponse("Gemini returned no content (model: \(model)).")
                }
                return text
            }
        }
    }

    // MARK: - Vertex AI

    func callVertex(
        project: String,
        location: String,
        model: String,
        prompt: String,
        temperature: Double,
        responseJSON: Bool,
        maxRetries: Int = 2
    ) async throws -> String {
        guard !project.isEmpty else {
            throw AIClientError.missingAPIKey(
                "Vertex AI project ID is not configured. Add it in Preferences → IA & Modèles."
            )
        }

        let token = try await gcloudToken()
        let urlStr = "https://\(location)-aiplatform.googleapis.com/v1/projects/\(project)/locations/\(location)/publishers/google/models/\(model):generateContent"
        guard let url = URL(string: urlStr) else { throw AIClientError.invalidURL(urlStr) }

        let body = makeGeminiBody(prompt: prompt, temperature: temperature, responseJSON: responseJSON, model: model)

        return try await withRetry(maxRetries) {
            var req = URLRequest(url: url)
            req.httpMethod = "POST"
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            req.httpBody = try JSONEncoder().encode(body)
            let (data, resp) = try await self.session.data(for: req)
            try self.checkHTTP(resp, data: data)
            let decoded = try JSONDecoder().decode(GeminiResponse.self, from: data)
            if let err = decoded.error { throw AIClientError.apiError("Vertex: \(err.message)") }
            guard let text = decoded.candidates?.first?.content.parts.first?.text else {
                throw AIClientError.emptyResponse("Vertex returned no content.")
            }
            return text
        }
    }

    // MARK: - OpenAI-compatible proxy

    func callProxy(
        baseURL: String,
        apiKey: String,
        model: String,
        prompt: String,
        temperature: Double,
        responseJSON: Bool,
        timeout: Int = 90,
        maxRetries: Int = 1
    ) async throws -> String {
        let endpoint = baseURL.trimmingCharacters(in: CharacterSet(charactersIn: "/")) + "/chat/completions"
        guard let url = URL(string: endpoint) else { throw AIClientError.invalidURL(endpoint) }

        var content = prompt
        if responseJSON && !content.lowercased().contains("json") {
            content += "\n\nReturn your response as valid JSON."
        }

        let body = OpenAIChatRequest(
            model: model,
            messages: [.init(role: "user", content: content)],
            temperature: temperature,
            response_format: responseJSON ? .init(type: "json_object") : nil
        )

        return try await withRetry(maxRetries) {
            var req = URLRequest(url: url)
            req.httpMethod = "POST"
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            if !apiKey.isEmpty { req.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization") }
            req.timeoutInterval = Double(timeout)
            req.httpBody = try JSONEncoder().encode(body)
            let (data, resp) = try await self.session.data(for: req)
            try self.checkHTTP(resp, data: data)
            let decoded = try JSONDecoder().decode(OpenAIChatResponse.self, from: data)
            if let err = decoded.error { throw AIClientError.apiError("Proxy: \(err.message)") }
            guard let text = decoded.choices?.first?.message.content else {
                throw AIClientError.emptyResponse("Proxy returned no content (model: \(model)).")
            }
            return text
        }
    }

    // MARK: - Vercel AI SDK route

    func callVercel(
        baseURL: String,
        apiKey: String,
        model: String,
        prompt: String,
        temperature: Double,
        responseJSON: Bool,
        timeout: Int = 90,
        maxRetries: Int = 1
    ) async throws -> String {
        let endpoint = baseURL.trimmingCharacters(in: CharacterSet(charactersIn: "/")) + "/chat/completions"
        guard let url = URL(string: endpoint) else { throw AIClientError.invalidURL(endpoint) }

        var content = prompt
        if responseJSON && !content.lowercased().contains("json") {
            content += "\n\nReturn your response as valid JSON."
        }

        let body = OpenAIChatRequest(
            model: model,
            messages: [.init(role: "user", content: content)],
            temperature: temperature,
            response_format: responseJSON ? .init(type: "json_object") : nil
        )

        return try await withRetry(maxRetries) {
            var req = URLRequest(url: url)
            req.httpMethod = "POST"
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            if !apiKey.isEmpty { req.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization") }
            req.timeoutInterval = Double(timeout)
            req.httpBody = try JSONEncoder().encode(body)
            let (data, resp) = try await self.session.data(for: req)
            try self.checkHTTP(resp, data: data)
            let decoded = try JSONDecoder().decode(OpenAIChatResponse.self, from: data)
            if let err = decoded.error { throw AIClientError.apiError("Vercel: \(err.message)") }
            guard let text = decoded.choices?.first?.message.content else {
                throw AIClientError.emptyResponse("Vercel returned no content (model: \(model)).")
            }
            return text
        }
    }

    // MARK: - Helpers

    private func makeGeminiBody(
        prompt: String,
        temperature: Double,
        responseJSON: Bool,
        model: String
    ) -> GeminiRequest {
        let t = max(0.0, min(2.0, temperature))
        let budget = thinkingBudget(for: model)
        return GeminiRequest(
            contents: [GeminiMessage(role: "user", parts: [GeminiPart(text: prompt)])],
            generationConfig: GeminiGenerationConfig(
                temperature: t,
                responseMimeType: responseJSON ? "application/json" : nil,
                thinkingConfig: budget.map { GeminiThinkingConfig(thinkingBudget: $0) }
            )
        )
    }

    private func thinkingBudget(for model: String) -> Int? {
        let m = model.lowercased()
        guard m.contains("2.5-pro") || m.contains("2.5-flash") else { return nil }
        return 8192
    }

    private func post<T: Decodable, R>(
        url: URL,
        body: some Encodable,
        as: T.Type,
        transform: (T) throws -> R
    ) async throws -> R {
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.httpBody = try JSONEncoder().encode(body)
        let (data, resp) = try await session.data(for: req)
        try checkHTTP(resp, data: data)
        return try transform(try JSONDecoder().decode(T.self, from: data))
    }

    private func checkHTTP(_ response: URLResponse, data: Data) throws {
        guard let h = response as? HTTPURLResponse, !(200..<300).contains(h.statusCode) else { return }
        let body = String(data: data, encoding: .utf8) ?? "(no body)"
        throw AIClientError.httpError(h.statusCode, body)
    }

    private func withRetry<T>(_ maxRetries: Int, work: () async throws -> T) async throws -> T {
        var lastError: Error?
        var backoff: UInt64 = 1_000_000_000
        for attempt in 0 ... maxRetries {
            do {
                return try await work()
            } catch let e as AIClientError {
                switch e {
                case .missingAPIKey, .invalidURL, .apiError:
                    throw e  // Non-retryable
                default:
                    lastError = e
                }
            } catch {
                lastError = error
            }
            if attempt < maxRetries {
                try? await Task.sleep(nanoseconds: backoff)
                backoff = min(backoff * 2, 30_000_000_000)
            }
        }
        throw lastError ?? AIClientError.emptyResponse("Unknown failure.")
    }

    private func gcloudToken() async throws -> String {
        try await withCheckedThrowingContinuation { cont in
            DispatchQueue.global(qos: .userInitiated).async {
                let proc = Process()
                proc.executableURL = URL(fileURLWithPath: "/usr/bin/env")
                proc.arguments = ["gcloud", "auth", "print-access-token"]
                let pipe = Pipe()
                proc.standardOutput = pipe
                proc.standardError  = Pipe()
                do {
                    try proc.run()
                    proc.waitUntilExit()
                    let raw = pipe.fileHandleForReading.readDataToEndOfFile()
                    let tok = String(data: raw, encoding: .utf8)?
                        .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                    if tok.isEmpty {
                        cont.resume(throwing: AIClientError.missingAPIKey(
                            "Could not obtain Vertex AI token. Run 'gcloud auth login' in Terminal."
                        ))
                    } else {
                        cont.resume(returning: tok)
                    }
                } catch {
                    cont.resume(throwing: AIClientError.missingAPIKey(
                        "gcloud not found. Install Google Cloud SDK for Vertex AI support."
                    ))
                }
            }
        }
    }
}

// MARK: - Errors

enum AIClientError: LocalizedError {
    case missingAPIKey(String)
    case invalidURL(String)
    case httpError(Int, String)
    case apiError(String)
    case emptyResponse(String)

    var errorDescription: String? {
        switch self {
        case .missingAPIKey(let m):         return m
        case .invalidURL(let u):            return "Invalid URL: \(u)"
        case .httpError(let code, let b):   return "HTTP \(code): \(b.prefix(300))"
        case .apiError(let m):              return m
        case .emptyResponse(let m):         return m
        }
    }
}
