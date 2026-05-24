import Foundation
import Combine

/// Central observable state shared across all views.
@MainActor
final class AppState: ObservableObject {

    private static let mainContentDefaultModel = "gemma-27b-a4b-it"
    private static let auxiliaryTasksDefaultModel = "gemma-27b-a4b-it"

    // MARK: - Server

    // MARK: - Generation state

    @Published var isGenerating = false
    @Published var progress: Int = 0
    @Published var logMessages: [String] = []
    @Published var chatHistory: [ChatMessage] = []
    @Published var generatedMarkdown: String = ""

    // MARK: - Preferences Settings
    @Published var geminiApiKey: String = ""
    @Published var guidesDir: String = ""
    @Published var textbookDir: String = ""
    @Published var outputDir: String = ""
    @Published var temperatureSetting: Double = 0.5
    @Published var defaultDuration: Int = 45
    @Published var defaultSubject: String = ""
    @Published var geminiModel: String = Self.mainContentDefaultModel

    // MARK: - Advanced API & Routing Settings
    @Published var apiRoute: String = "aistudio"
    @Published var vertexProject: String = ""
    @Published var vertexLocation: String = "us-central1"
    @Published var proxyEnabled: Bool = false
    @Published var proxyBaseURL: String = "http://localhost:11434/v1"
    @Published var proxyApiKey: String = ""
    @Published var vercelEnabled: Bool = false
    @Published var vercelBaseURL: String = "https://api.vercel.ai/v1"
    @Published var vercelApiKey: String = ""

    // MARK: - Per-Function Providers
    @Published var routingFicheProvider: String = "default"
    @Published var routingFicheModel: String = ""
    @Published var routingEvalProvider: String = "default"
    @Published var routingEvalModel: String = ""
    @Published var routingQuizProvider: String = "default"
    @Published var routingQuizModel: String = ""
    @Published var routingTocProvider: String = "default"
    @Published var routingTocModel: String = ""
    @Published var routingOffsetProvider: String = "default"
    @Published var routingOffsetModel: String = ""
    @Published var routingSyntaxProvider: String = "default"
    @Published var routingSyntaxModel: String = ""
    @Published var routingChatProvider: String = "default"
    @Published var routingChatModel: String = ""

    // MARK: - Defaults
    @Published var defaultPdfStyle: String = "Normal"
    @Published var auxiliaryTasksModel: String = Self.auxiliaryTasksDefaultModel

    // MARK: - General Settings
    @Published var useTopExamples: Bool = true
    @Published var previewSource: Bool = false
    @Published var saveLogs: Bool = false
    @Published var autoUpdateChecks: Bool = true
    @Published var quitOnClose: Bool = true

    // MARK: - Advanced settings
    @Published var specialInstructions: String = ""
    @Published var advancedEnablePromptEditing: Bool = false
    @Published var advancedTocPrompt: String = ""
    @Published var advancedPageFindingPrompt: String = ""
    @Published var advancedFichePrompt: String = ""
    @Published var advancedShowLogTab: Bool = false
    @Published var chatModel: String = Self.auxiliaryTasksDefaultModel


    // MARK: - Appearance Settings
    @Published var uiLanguage: String = "fr"
    @Published var uiCompactSidebar: Bool = false
    @Published var uiShowEvalAdvancedControls: Bool = false
    @Published var pdfShowMeta: Bool = false

    // MARK: - Basic Experimental Settings
    @Published var expStreamingResponse: Bool = false
    @Published var expMaxRetries: Int = 2
    @Published var expRequestTimeout: Int = 90
    @Published var expEnableCache: Bool = true
    @Published var expParallelToc: Bool = false
    @Published var expShowAdvancedRoutingInForms: Bool = false

    // MARK: - Highly Experimental Settings
    @Published var expMultiPassGen: Bool = false
    @Published var expMultiPassIterations: Int = 2
    @Published var expStyleTransfer: Bool = false
    @Published var expAutoGradeDifficulty: Bool = false
    @Published var expChainOfThought: Bool = false
    @Published var expJsonValidation: Bool = true
    @Published var expSpeculativeDecoding: Bool = false
    @Published var expAgenticLoop: Bool = false

    // MARK: - Available Lessons (ToC Cache)
    @Published var availableLessons: [String] = []

    // MARK: - Form — Fiche

    @Published var ficheClassLevel: String = "CM1"
    @Published var ficheLessonTopic: String = ""
    @Published var fichePagesOverride: String = ""
    @Published var ficheTemperature: Double = 0.5
    @Published var ficheDurationMinutes: Int = 45
    @Published var ficheSubject: String = ""
    @Published var ficheSpecialInstructions: String = ""
    @Published var ficheGenerateImage: Bool = false
    @Published var ficheUseTopRated: Bool = true

    // MARK: - Form — Evaluation

    @Published var evalClassLevel: String = "CM1"
    @Published var evalTopics: String = ""
    @Published var evalSubject: String = ""
    @Published var evalDuration: Int = 45
    @Published var evalDifficulty: String = "medium"
    @Published var evalTemperature: Double = 0.5

    // MARK: - Form — Quiz

    @Published var quizClassLevel: String = "CM1"
    @Published var quizTopic: String = ""
    @Published var quizSubject: String = ""
    @Published var quizDuration: Int = 20
    @Published var quizNumQuestions: Int = 10
    @Published var quizDifficulty: String = "medium"

    var isConfigured: Bool {
        switch apiRoute {
        case "aistudio":
            return !geminiApiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case "vertex":
            return !vertexProject.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && 
                   !vertexLocation.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case "proxy":
            return proxyEnabled && !proxyBaseURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case "vercel":
            return vercelEnabled && !vercelBaseURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && 
                   !vercelApiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        default:
            return !geminiApiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
    }

    // MARK: - Available class levels

    let classLevels = ["CP", "CE1", "CE2", "CM1", "CM2", "6e", "5e", "4e", "3e"]
    let difficulties = ["easy", "medium", "hard"]

    private static func normalizedMainModel(_ model: String) -> String {
        switch model {
        case "gemini-1.5-pro", "gemini-1.5-flash":
            return mainContentDefaultModel
        default:
            return model
        }
    }

    private static func normalizedAuxiliaryModel(_ model: String) -> String {
        switch model {
        case "gemini-1.5-pro", "gemini-1.5-flash":
            return auxiliaryTasksDefaultModel
        default:
            return model
        }
    }

    private var cancellables = Set<AnyCancellable>()
    private var generationTask: Task<Void, Never>?
    private var lessonsTask: Task<Void, Never>?

    init() {
        // Init happens. Sinks react to class level and guides directory changes.

        $ficheClassLevel
            .sink { [weak self] newLevel in
                self?.loadAvailableLessons(classLevel: newLevel)
            }
            .store(in: &cancellables)

        $evalClassLevel
            .sink { [weak self] newLevel in
                self?.loadAvailableLessons(classLevel: newLevel)
            }
            .store(in: &cancellables)

        $quizClassLevel
            .sink { [weak self] newLevel in
                self?.loadAvailableLessons(classLevel: newLevel)
            }
            .store(in: &cancellables)

        $guidesDir
            .dropFirst()
            .sink { [weak self] _ in
                guard let self = self else { return }
                self.loadAvailableLessons(classLevel: self.ficheClassLevel)
            }
            .store(in: &cancellables)
    }

    // MARK: - Generation (fully native — no Python server)

    func generateFiche() {
        guard !isGenerating else { return }
        isGenerating = true
        progress = 0
        logMessages = []
        generatedMarkdown = ""
        appendLog("🚀 Lancement de la génération native...")
        progress = 10

        // Snapshot all @MainActor state before crossing actor boundary
        let cfg = AIConfig(from: self)
        let classLevel = ficheClassLevel
        let lessonTopic = ficheLessonTopic
        let pagesOverride = fichePagesOverride
        let temperature = ficheTemperature
        let duration = ficheDurationMinutes
        let subject = ficheSubject
        let specialInstructions = ficheSpecialInstructions
        let useTopRated = ficheUseTopRated
        let guidesDir = self.guidesDir

        generationTask = Task {
            progress = 30
            let lessonText = await Task.detached(priority: .userInitiated) { () -> String in
                guard !Task.isCancelled else { return "" }
                guard let guideURL = PDFProcessor.findGuideFile(classLevel: classLevel, guidesDir: guidesDir) else {
                    return ""
                }
                if let cached = PDFProcessor.loadCachedTOC(pdfURL: guideURL, guidesDir: guidesDir) {
                    let withRanges = PDFProcessor.computePageRanges(for: cached)
                    if let match = withRanges.first(where: {
                        $0.entry.topic.localizedCaseInsensitiveContains(lessonTopic) ||
                        lessonTopic.localizedCaseInsensitiveContains($0.entry.topic)
                    }) {
                        let offset = PDFProcessor.detectPageOffset(pdfURL: guideURL)
                        let physicalPages = match.pages.map { $0 + offset }
                        return PDFProcessor.extractText(from: guideURL, pages: physicalPages)
                    }
                }
                return ""
            }.value

            guard !Task.isCancelled else { return }
            if !lessonText.isEmpty {
                appendLog("✅ \(lessonText.count) caractères extraits du guide (avec décalage de page corrigé).")
            } else {
                appendLog("ℹ️ Génération directe (aucune source guide pédagogique correspondante).")
            }
            progress = 60

            do {
                let markdown = try await GenerationEngine.shared.generateFiche(
                    classLevel: classLevel,
                    lessonTopic: lessonTopic,
                    pagesOverride: pagesOverride,
                    temperature: temperature,
                    durationMinutes: duration,
                    subject: subject,
                    specialInstructions: specialInstructions,
                    useTopRatedExamples: useTopRated,
                    lessonText: lessonText,
                    config: cfg,
                    onLog: { [weak self] msg in
                        guard let self = self, !Task.isCancelled else { return }
                        self.appendLog(msg)
                    },
                    onProgress: { [weak self] v in
                        guard let self = self, !Task.isCancelled else { return }
                        self.progress = v
                    }
                )
                guard !Task.isCancelled else { return }
                generatedMarkdown = markdown
            } catch is CancellationError {
                // Swallowed
            } catch let error as URLError where error.code == .cancelled {
                // Swallowed
            } catch {
                guard !Task.isCancelled else { return }
                appendLog("❌ \(error.localizedDescription)")
            }
            guard !Task.isCancelled else { return }
            isGenerating = false
        }
    }

    func generateEvaluation() {
        guard !isGenerating else { return }
        isGenerating = true
        progress = 0
        logMessages = []
        generatedMarkdown = ""
        appendLog("🚀 Lancement de la génération d'évaluation...")
        progress = 10

        let cfg = AIConfig(from: self)
        let classLevel = evalClassLevel
        let topics = evalTopics
            .split(separator: "\n")
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
        let subject = evalSubject
        let duration = evalDuration
        let difficulty = evalDifficulty
        let temperature = evalTemperature

        generationTask = Task {
            do {
                let markdown = try await GenerationEngine.shared.generateEvaluation(
                    classLevel: classLevel,
                    topics: topics,
                    subject: subject,
                    durationMinutes: duration,
                    difficulty: difficulty,
                    temperature: temperature,
                    extraInstructions: "",
                    lessonText: "",
                    config: cfg,
                    onLog: { [weak self] msg in
                        guard let self = self, !Task.isCancelled else { return }
                        self.appendLog(msg)
                    },
                    onProgress: { [weak self] v in
                        guard let self = self, !Task.isCancelled else { return }
                        self.progress = v
                    }
                )
                guard !Task.isCancelled else { return }
                generatedMarkdown = markdown
            } catch is CancellationError {
                // Swallowed
            } catch let error as URLError where error.code == .cancelled {
                // Swallowed
            } catch {
                guard !Task.isCancelled else { return }
                appendLog("❌ \(error.localizedDescription)")
            }
            guard !Task.isCancelled else { return }
            isGenerating = false
        }
    }

    func generateQuiz() {
        guard !isGenerating else { return }
        isGenerating = true
        progress = 0
        logMessages = []
        generatedMarkdown = ""
        appendLog("🚀 Lancement de la génération du quiz...")
        progress = 10

        let cfg = AIConfig(from: self)
        let classLevel = quizClassLevel
        let topic = quizTopic
        let subject = quizSubject
        let duration = quizDuration
        let numQuestions = quizNumQuestions
        let difficulty = quizDifficulty
        let temperature = ficheTemperature

        generationTask = Task {
            do {
                let markdown = try await GenerationEngine.shared.generateQuiz(
                    classLevel: classLevel,
                    topic: topic,
                    subject: subject,
                    durationMinutes: duration,
                    numQuestions: numQuestions,
                    difficulty: difficulty,
                    temperature: temperature,
                    config: cfg,
                    onLog: { [weak self] msg in
                        guard let self = self, !Task.isCancelled else { return }
                        self.appendLog(msg)
                    },
                    onProgress: { [weak self] v in
                        guard let self = self, !Task.isCancelled else { return }
                        self.progress = v
                    }
                )
                guard !Task.isCancelled else { return }
                generatedMarkdown = markdown
            } catch is CancellationError {
                // Swallowed
            } catch let error as URLError where error.code == .cancelled {
                // Swallowed
            } catch {
                guard !Task.isCancelled else { return }
                appendLog("❌ \(error.localizedDescription)")
            }
            guard !Task.isCancelled else { return }
            isGenerating = false
        }
    }

    func cancelGeneration() {
        generationTask?.cancel()
        generationTask = nil
        isGenerating = false
        appendLog("⏹️ Génération annulée.")
    }

    func appendLog(_ message: String) {
        logMessages.append(message)
    }

    // MARK: - Settings Operations

    // MARK: - Settings: fetch on startup from Keychain + UserDefaults

    func fetchSettingsFromServer() async {
        let settings = PreferencesStorage.load()
        
        func str(_ key: String, _ fallback: String) -> String { settings[key] as? String ?? fallback }
        func bool(_ key: String, _ fallback: Bool) -> Bool { settings[key] as? Bool ?? fallback }
        func int_(_ key: String, _ fallback: Int) -> Int { settings[key] as? Int ?? fallback }
        func dbl(_ key: String, _ fallback: Double) -> Double { settings[key] as? Double ?? fallback }
        
        geminiApiKey    = str("gemini_api_key", "")
        proxyApiKey     = str("proxy_api_key", "")

        guidesDir       = str("guides_dir", "")
        textbookDir     = str("textbook_dir", "")
        outputDir       = str("output_dir", "")
        temperatureSetting = dbl("temperature", 0.5)
        defaultDuration = int_("default_duration", 45)
        defaultSubject  = str("default_subject", "")
        geminiModel     = Self.normalizedMainModel(str("gemini_model", Self.mainContentDefaultModel))
        apiRoute        = str("api_route", "aistudio")
        vertexProject   = str("vertex_project", "")
        vertexLocation  = str("vertex_location", "us-central1")
        proxyEnabled    = bool("proxy_enabled", false)
        proxyBaseURL    = str("proxy_base_url", "http://localhost:11434/v1")
        vercelEnabled   = bool("vercel_enabled", false)
        vercelBaseURL   = str("vercel_base_url", "https://api.vercel.ai/v1")
        vercelApiKey    = str("vercel_api_key", "")
        routingFicheProvider  = str("routing_fiche_provider", "default")
        routingFicheModel     = Self.normalizedMainModel(str("routing_fiche_model", ""))
        routingEvalProvider   = str("routing_eval_provider", "default")
        routingEvalModel      = Self.normalizedAuxiliaryModel(str("routing_eval_model", ""))
        routingQuizProvider   = str("routing_quiz_provider", "default")
        routingQuizModel      = Self.normalizedAuxiliaryModel(str("routing_quiz_model", ""))
        routingTocProvider    = str("routing_toc_provider", "default")
        routingTocModel       = Self.normalizedAuxiliaryModel(str("routing_toc_model", ""))
        routingOffsetProvider = str("routing_offset_provider", "default")
        routingOffsetModel    = Self.normalizedAuxiliaryModel(str("routing_offset_model", ""))
        routingSyntaxProvider = str("routing_syntax_provider", "default")
        routingSyntaxModel    = Self.normalizedAuxiliaryModel(str("routing_syntax_model", ""))
        routingChatProvider   = str("routing_chat_provider", "default")
        routingChatModel      = Self.normalizedAuxiliaryModel(str("routing_chat_model", ""))
        defaultPdfStyle = str("default_pdf_style", "Normal")
        auxiliaryTasksModel = Self.normalizedAuxiliaryModel(str("auxiliary_tasks_model", str("chat_model", Self.auxiliaryTasksDefaultModel)))
        useTopExamples  = bool("use_top_examples", true)
        previewSource   = bool("preview_source", false)
        saveLogs        = bool("save_logs", false)
        autoUpdateChecks = bool("updates_auto_check", true)
        quitOnClose     = bool("quit_on_close", true)
        specialInstructions = str("special_instructions", "")
        advancedEnablePromptEditing = bool("advanced_enable_prompt_editing", false)
        advancedTocPrompt  = str("advanced_toc_prompt", "")
        advancedPageFindingPrompt = str("advanced_page_finding_prompt", "")
        advancedFichePrompt = str("advanced_fiche_prompt", "")
        chatModel       = auxiliaryTasksModel
        uiLanguage      = str("ui_language", "fr")
        uiCompactSidebar = bool("ui_compact_sidebar", false)
        uiShowEvalAdvancedControls = bool("ui_show_eval_advanced_controls", false)
        pdfShowMeta     = bool("pdf_show_meta", false)
        expStreamingResponse = bool("exp_streaming_response", false)
        expMaxRetries   = int_("exp_max_retries", 2)
        expRequestTimeout = int_("exp_request_timeout", 90)
        expEnableCache  = bool("exp_enable_cache", true)
        expParallelToc  = bool("exp_parallel_toc", false)
        expShowAdvancedRoutingInForms = bool("exp_show_advanced_routing_in_forms", false)
        expMultiPassGen = bool("exp_multi_pass_gen", false)
        expMultiPassIterations = int_("exp_multi_pass_iterations", 2)
        expStyleTransfer = bool("exp_style_transfer", false)
        expAutoGradeDifficulty = bool("exp_auto_grade_difficulty", false)
        expChainOfThought = bool("exp_chain_of_thought", false)
        expJsonValidation = bool("exp_json_validation", true)
        expSpeculativeDecoding = bool("exp_speculative_decoding", false)
        expAgenticLoop  = bool("exp_agentic_loop", false)

        // Apply defaults to form states
        ficheTemperature  = temperatureSetting
        ficheDurationMinutes = defaultDuration
        ficheSubject = defaultSubject
        evalTemperature  = temperatureSetting
        evalDuration     = defaultDuration
        evalSubject      = defaultSubject
        quizDuration     = defaultDuration
        quizSubject      = defaultSubject
        
        // After loading guidesDir, we can safely fetch the TOC cache
        loadAvailableLessons(classLevel: ficheClassLevel)
    }

    // MARK: - Settings: persist to JSON

    func saveSettingsToServer() {
        let settings: [String: Any] = [
            "gemini_api_key": geminiApiKey, "proxy_api_key": proxyApiKey,
            "guides_dir": guidesDir,   "textbook_dir": textbookDir,  "output_dir": outputDir,
            "temperature": temperatureSetting, "default_duration": defaultDuration,
            "default_subject": defaultSubject, "gemini_model": geminiModel,
            "auxiliary_tasks_model": auxiliaryTasksModel, "chat_model": auxiliaryTasksModel,
            "api_route": apiRoute,  "vertex_project": vertexProject, "vertex_location": vertexLocation,
            "proxy_enabled": proxyEnabled,  "proxy_base_url": proxyBaseURL,
            "vercel_enabled": vercelEnabled, "vercel_base_url": vercelBaseURL, "vercel_api_key": vercelApiKey,
            "routing_fiche_provider": routingFicheProvider, "routing_fiche_model": routingFicheModel,
            "routing_eval_provider": routingEvalProvider,   "routing_eval_model": routingEvalModel,
            "routing_quiz_provider": routingQuizProvider,   "routing_quiz_model": routingQuizModel,
            "routing_toc_provider": routingTocProvider,     "routing_toc_model": routingTocModel,
            "routing_offset_provider": routingOffsetProvider, "routing_offset_model": routingOffsetModel,
            "routing_syntax_provider": routingSyntaxProvider, "routing_syntax_model": routingSyntaxModel,
            "routing_chat_provider": routingChatProvider, "routing_chat_model": routingChatModel,
            "default_pdf_style": defaultPdfStyle, "use_top_examples": useTopExamples,
            "preview_source": previewSource,  "save_logs": saveLogs,
            "updates_auto_check": autoUpdateChecks, "quit_on_close": quitOnClose,
            "special_instructions": specialInstructions,
            "advanced_enable_prompt_editing": advancedEnablePromptEditing,
            "advanced_toc_prompt": advancedTocPrompt, "advanced_page_finding_prompt": advancedPageFindingPrompt,
            "advanced_fiche_prompt": advancedFichePrompt, "chat_model": chatModel,
            "ui_language": uiLanguage, "ui_compact_sidebar": uiCompactSidebar,
            "ui_show_eval_advanced_controls": uiShowEvalAdvancedControls, "pdf_show_meta": pdfShowMeta,
            "exp_streaming_response": expStreamingResponse, "exp_max_retries": expMaxRetries,
            "exp_request_timeout": expRequestTimeout, "exp_enable_cache": expEnableCache,
            "exp_parallel_toc": expParallelToc, "exp_show_advanced_routing_in_forms": expShowAdvancedRoutingInForms,
            "exp_multi_pass_gen": expMultiPassGen,
            "exp_multi_pass_iterations": expMultiPassIterations,
            "exp_style_transfer": expStyleTransfer, "exp_auto_grade_difficulty": expAutoGradeDifficulty,
            "exp_chain_of_thought": expChainOfThought, "exp_json_validation": expJsonValidation,
            "exp_speculative_decoding": expSpeculativeDecoding, "exp_agentic_loop": expAgenticLoop
        ]
        PreferencesStorage.save(settings)
    }

    func updateSetting(key: String, value: Any) {
        switch key {
        case "gemini_api_key":
            if let val = value as? String { geminiApiKey = val }
        case "proxy_api_key":
            if let val = value as? String { proxyApiKey = val }
        case "guides_dir", "input_dir":
            if let val = value as? String { guidesDir = val }
        case "textbook_dir":
            if let val = value as? String { textbookDir = val }
        case "output_dir":
            if let val = value as? String { outputDir = val }
        case "temperature":
            if let val = value as? Double { temperatureSetting = val }
        case "default_duration":
            if let val = value as? Int { defaultDuration = val }
        case "default_subject":
            if let val = value as? String { defaultSubject = val }
        case "gemini_model":
            if let val = value as? String { geminiModel = Self.normalizedMainModel(val) }
        case "api_route":
            if let val = value as? String { apiRoute = val }
        case "vertex_project":
            if let val = value as? String { vertexProject = val }
        case "vertex_location":
            if let val = value as? String { vertexLocation = val }
        case "proxy_enabled":
            if let val = value as? Bool { proxyEnabled = val }
        case "proxy_base_url":
            if let val = value as? String { proxyBaseURL = val }
        case "vercel_enabled":
            if let val = value as? Bool { vercelEnabled = val }
        case "vercel_base_url":
            if let val = value as? String { vercelBaseURL = val }
        case "vercel_api_key":
            if let val = value as? String { vercelApiKey = val }
        case "routing_fiche_provider":
            if let val = value as? String { routingFicheProvider = val }
        case "routing_fiche_model":
            if let val = value as? String { routingFicheModel = Self.normalizedMainModel(val) }
        case "routing_eval_provider":
            if let val = value as? String { routingEvalProvider = val }
        case "routing_eval_model":
            if let val = value as? String { routingEvalModel = Self.normalizedAuxiliaryModel(val) }
        case "routing_quiz_provider":
            if let val = value as? String { routingQuizProvider = val }
        case "routing_quiz_model":
            if let val = value as? String { routingQuizModel = Self.normalizedAuxiliaryModel(val) }
        case "routing_toc_provider":
            if let val = value as? String { routingTocProvider = val }
        case "routing_toc_model":
            if let val = value as? String { routingTocModel = Self.normalizedAuxiliaryModel(val) }
        case "routing_offset_provider":
            if let val = value as? String { routingOffsetProvider = val }
        case "routing_offset_model":
            if let val = value as? String { routingOffsetModel = Self.normalizedAuxiliaryModel(val) }
        case "routing_syntax_provider":
            if let val = value as? String { routingSyntaxProvider = val }
        case "routing_syntax_model":
            if let val = value as? String { routingSyntaxModel = Self.normalizedAuxiliaryModel(val) }
        case "routing_chat_provider":
            if let val = value as? String { routingChatProvider = val }
        case "routing_chat_model":
            if let val = value as? String { routingChatModel = Self.normalizedAuxiliaryModel(val) }
        case "auxiliary_tasks_model":
            if let val = value as? String {
                auxiliaryTasksModel = Self.normalizedAuxiliaryModel(val)
                chatModel = auxiliaryTasksModel
            }
        case "default_pdf_style":
            if let val = value as? String { defaultPdfStyle = val }
        case "use_top_examples":
            if let val = value as? Bool { useTopExamples = val }
        case "preview_source":
            if let val = value as? Bool { previewSource = val }
        case "save_logs":
            if let val = value as? Bool { saveLogs = val }
        case "updates_auto_check":
            if let val = value as? Bool { autoUpdateChecks = val }
        case "quit_on_close":
            if let val = value as? Bool { quitOnClose = val }
        case "special_instructions":
            if let val = value as? String { specialInstructions = val }
        case "advanced_enable_prompt_editing":
            if let val = value as? Bool { advancedEnablePromptEditing = val }
        case "advanced_toc_prompt":
            if let val = value as? String { advancedTocPrompt = val }
        case "advanced_page_finding_prompt":
            if let val = value as? String { advancedPageFindingPrompt = val }
        case "advanced_fiche_prompt":
            if let val = value as? String { advancedFichePrompt = val }
        case "chat_model":
            if let val = value as? String {
                auxiliaryTasksModel = Self.normalizedAuxiliaryModel(val)
                chatModel = auxiliaryTasksModel
            }
        case "ui_language":
            if let val = value as? String { uiLanguage = val }
        case "ui_compact_sidebar":
            if let val = value as? Bool { uiCompactSidebar = val }
        case "ui_show_eval_advanced_controls":
            if let val = value as? Bool { uiShowEvalAdvancedControls = val }
        case "pdf_show_meta":
            if let val = value as? Bool { pdfShowMeta = val }
        case "exp_streaming_response":
            if let val = value as? Bool { expStreamingResponse = val }
        case "exp_max_retries":
            if let val = value as? Int { expMaxRetries = val }
        case "exp_request_timeout":
            if let val = value as? Int { expRequestTimeout = val }
        case "exp_enable_cache":
            if let val = value as? Bool { expEnableCache = val }
        case "exp_parallel_toc":
            if let val = value as? Bool { expParallelToc = val }
        case "exp_show_advanced_routing_in_forms":
            if let val = value as? Bool { expShowAdvancedRoutingInForms = val }
        case "exp_multi_pass_gen":
            if let val = value as? Bool { expMultiPassGen = val }
        case "exp_multi_pass_iterations":
            if let val = value as? Int { expMultiPassIterations = val }
        case "exp_style_transfer":
            if let val = value as? Bool { expStyleTransfer = val }
        case "exp_auto_grade_difficulty":
            if let val = value as? Bool { expAutoGradeDifficulty = val }
        case "exp_chain_of_thought":
            if let val = value as? Bool { expChainOfThought = val }
        case "exp_json_validation":
            if let val = value as? Bool { expJsonValidation = val }
        case "exp_speculative_decoding":
            if let val = value as? Bool { expSpeculativeDecoding = val }
        case "exp_agentic_loop":
            if let val = value as? Bool { expAgenticLoop = val }
        default:
            break
        }
        
        saveSettingsToServer()
    }

    // MARK: - Lesson Operations

    func loadAvailableLessons(classLevel: String) {
        lessonsTask?.cancel()
        let currentGuidesDir = self.guidesDir
        lessonsTask = Task {
            let lessons = await Task.detached(priority: .userInitiated) { () -> [String]? in
                guard !Task.isCancelled else { return nil }
                guard let guideURL = PDFProcessor.findGuideFile(classLevel: classLevel, guidesDir: currentGuidesDir) else {
                    return nil
                }
                
                if let cached = PDFProcessor.loadCachedTOC(pdfURL: guideURL, guidesDir: currentGuidesDir) {
                    return cached.map { $0.topic }
                }
                
                if let rawText = PDFProcessor.extractRawTOCText(pdfURL: guideURL),
                   let parsed = PDFProcessor.parseTOCWithHeuristics(tocText: rawText) {
                    PDFProcessor.saveTOCToCache(pdfURL: guideURL, guidesDir: currentGuidesDir, toc: parsed)
                    return parsed.map { $0.topic }
                }
                return nil
            }.value
            
            guard !Task.isCancelled else { return }
            if let lessons = lessons {
                self.availableLessons = lessons
            } else {
                self.availableLessons = []
            }
        }
    }
    
    private func loadAvailableLessonsFromServer(classLevel: String) async {
        // Server no longer used — just leave lessons empty; user can still type a lesson name.
        appendLog("ℹ️ No guide PDF found for \(classLevel). Set the guides directory in Preferences.")
    }
}
