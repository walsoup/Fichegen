import Foundation

// MARK: - GenerationEngine
// Native Swift replacement for core/workers.py + core/ai.py rendering logic.
// Handles fiche, evaluation, and quiz generation entirely in Swift via GeminiClient.

@MainActor
final class GenerationEngine {

    static let shared = GenerationEngine()

    static func cleanJSONResponse(_ text: String) -> String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.hasPrefix("```") else { return trimmed }
        
        let pattern = "^```(?:json)?\\s*(.*?)\\s*```$"
        if let regex = try? NSRegularExpression(pattern: pattern, options: [.dotMatchesLineSeparators]),
           let match = regex.firstMatch(in: trimmed, options: [], range: NSRange(location: 0, length: trimmed.utf16.count)),
           let range = Range(match.range(at: 1), in: trimmed) {
            return String(trimmed[range])
        }
        return trimmed
    }

    static func safeInt(_ val: Any?) -> Int? {
        if let i = val as? Int { return i }
        if let s = val as? String { return Int(s.trimmingCharacters(in: CharacterSet.decimalDigits.inverted)) }
        if let d = val as? Double { return Int(d) }
        return nil
    }

    private let defaultFicheStructure = """
    **Titre du chapitre** : (à déduire du manuel)
    **Titre de la leçon** : (à déduire de la leçon)
    **Durée** : (à déduire) min
    **Classe** : (à déduire)
    **Matière** : (à déduire si pertinent)

    ## Objectifs
    - Identifier ...
    - Décrire ...

    ## Déroulement de la séance

    ### Introduction (5-10 min)
    Je commence la séance par une petite question/rappel pour éveiller la curiosité des élèves.
    Je présente le titre de la leçon et j'annonce ce que nous allons apprendre aujourd'hui.

    ### Activité de découverte (15-20 min)
    Je demande aux élèves de prendre leur manuel p. X.
    Nous observons ensemble les images / le texte.
    Je pose des questions simples : "Que voyez-vous ? Que remarquez-vous ?"
    Je note les réponses des élèves au tableau.
    Je les guide vers la découverte de la notion de la leçon.
    Les élèves réalisent les exercices indiqués.
    Je circule dans la classe pour vérifier et aider.
    On corrige collectivement.

    ### Synthèse et structuration (10-15 min)
    Nous reprenons les points essentiels.
    Je formule avec les élèves la règle ou la conclusion.
    Les élèves recopient la conclusion dans leurs cahiers.

    ## Évaluation
    Je propose un court exercice (oral ou écrit) pour vérifier que chacun a compris.

    ## Remarques et conclusion
    Rappeler aux élèves l'idée principale de la leçon.
    """

    // MARK: - Fiche Generation

    func generateFiche(
        classLevel: String,
        lessonTopic: String,
        pagesOverride: String,
        temperature: Double,
        durationMinutes: Int,
        subject: String,
        specialInstructions: String,
        useTopRatedExamples: Bool,
        lessonText: String,
        customTemplate: String = "",
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void
    ) async throws -> String {

        onLog("🤖 Génération de la fiche en cours...")
        onProgress(80)

        let prompt = buildFichePrompt(
            lessonTopic: lessonTopic,
            classLevel: classLevel,
            subject: subject,
            lessonText: lessonText,
            customTemplate: customTemplate,
            durationMinutes: durationMinutes,
            specialInstructions: specialInstructions,
            useTopRatedExamples: useTopRatedExamples
        )

        let responseText = try await GeminiClient.shared.generate(
            prompt: prompt,
            purpose: "fiche-generation",
            temperature: max(0.0, min(1.0, temperature)),
            responseJSON: true,
            config: config
        )

        onProgress(95)

        // Parse JSON → render HTML
        let cleanedText = GenerationEngine.cleanJSONResponse(responseText)
        if let data = cleanedText.data(using: .utf8),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           !json.isEmpty {
            var html = renderFicheHTML(json)
            if !html.isEmpty {
                if config.expMultiPassGen && config.expMultiPassIterations > 1 {
                    html = try await performMultiPass(
                        initialHTML: html,
                        iterations: config.expMultiPassIterations,
                        config: config,
                        onLog: onLog,
                        onProgress: onProgress
                    )
                }

                onLog("✅ Fiche générée.")
                onProgress(100)
                return html
            }
        }

        // Fallback: return raw text if JSON parsing fails
        let fallback = responseText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !fallback.isEmpty else {
            throw AIClientError.emptyResponse("Le modèle a renvoyé une réponse vide pour la fiche.")
        }
        onLog("⚠️ JSON parse failed — using raw response.")
        onProgress(100)
        return fallback
    }

    private func performMultiPass(
        initialHTML: String,
        iterations: Int,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void
    ) async throws -> String {
        var currentHTML = initialHTML
        let loops = max(1, iterations - 1)
        
        for i in 1...loops {
            onLog("🔄 Passe de raffinement \(i)/\(loops)...")
            onProgress(95 + (i * 4 / loops))
            
            let prompt = """
            Tu es un expert en pédagogie. Analyse et améliore la fiche pédagogique suivante.
            Corrige les incohérences, améliore la formulation, assure-toi que les durées correspondent, et enrichis le contenu si nécessaire.
            Ne retourne QUE le code HTML amélioré, sans texte introductif.
            
            FICHE ACTUELLE:
            ---
            \(currentHTML)
            ---
            """
            
            let response = try await GeminiClient.shared.generate(
                prompt: prompt,
                purpose: "fiche-refinement",
                temperature: 0.5,
                responseJSON: false,
                config: config
            )
            let refined = response.trimmingCharacters(in: .whitespacesAndNewlines)
            if !refined.isEmpty {
                currentHTML = refined
            }
        }
        return currentHTML
    }

    // MARK: - Evaluation Generation

    func generateEvaluation(
        classLevel: String,
        topics: [String],
        subject: String,
        durationMinutes: Int,
        difficulty: String,
        temperature: Double,
        schoolName: String,
        sessionLabel: String,
        totalPoints: Int,
        extraInstructions: String,
        lessonText: String,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void
    ) async throws -> String {

        onLog("📝 Génération de l'évaluation...")
        onLog("📚 Sujets: \(topics.joined(separator: ", "))")
        onLog("🎯 Classe: \(classLevel) | Matière: \(subject)")
        onProgress(70)

        let prompt = buildEvaluationPrompt(
            classLevel: classLevel,
            topics: topics,
            subject: subject,
            durationMinutes: durationMinutes,
            difficulty: difficulty,
            schoolName: schoolName,
            sessionLabel: sessionLabel,
            totalPoints: totalPoints,
            extraInstructions: extraInstructions,
            extractedContent: lessonText
        )

        let responseText = try await GeminiClient.shared.generate(
            prompt: prompt,
            purpose: "evaluation-generation",
            temperature: max(0.0, min(1.0, temperature)),
            responseJSON: true,
            config: config
        )

        onProgress(95)

        let cleanedText = GenerationEngine.cleanJSONResponse(responseText)
        if let data = cleanedText.data(using: .utf8),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           !json.isEmpty {
            let html = renderEvaluationHTML(json)
            if !html.isEmpty {
                onLog("✅ Évaluation générée.")
                onProgress(100)
                return html
            }
        }

        let fallback = responseText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !fallback.isEmpty else {
            throw AIClientError.emptyResponse("Le modèle a renvoyé une réponse vide pour l'évaluation.")
        }
        onLog("⚠️ JSON parse failed — using raw response.")
        onProgress(100)
        return fallback
    }

    // MARK: - Quiz Generation

    func generateQuiz(
        classLevel: String,
        topic: String,
        subject: String,
        durationMinutes: Int,
        numQuestions: Int,
        difficulty: String,
        schoolName: String,
        session: String,
        totalPoints: Int,
        temperature: Double,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void
    ) async throws -> String {

        onLog("🧠 Génération du quiz...")
        onLog("📌 Sujet: \(topic) | Classe: \(classLevel)")
        onProgress(70)

        let prompt = buildQuizPrompt(
            classLevel: classLevel,
            topic: topic,
            subject: subject,
            durationMinutes: durationMinutes,
            numQuestions: numQuestions,
            difficulty: difficulty,
            schoolName: schoolName,
            session: session,
            totalPoints: totalPoints
        )

        let responseText = try await GeminiClient.shared.generate(
            prompt: prompt,
            purpose: "quiz-generation",
            temperature: max(0.0, min(1.0, temperature)),
            responseJSON: true,
            config: config
        )

        onProgress(95)

        let cleanedText = GenerationEngine.cleanJSONResponse(responseText)
        if let data = cleanedText.data(using: .utf8),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           !json.isEmpty {
            let html = renderQuizHTML(json)
            if !html.isEmpty {
                onLog("✅ Quiz généré.")
                onProgress(100)
                return html
            }
        }

        let fallback = responseText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !fallback.isEmpty else {
            throw AIClientError.emptyResponse("Le modèle a renvoyé une réponse vide pour le quiz.")
        }
        onLog("⚠️ JSON parse failed — using raw response.")
        onProgress(100)
        return fallback
    }

    // MARK: - AI Editor

    func editFiche(
        currentHTML: String,
        instructions: String,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void
    ) async throws -> String {
        onLog("✨ Modification de la fiche en cours...")
        onProgress(50)

        let prompt = """
        Tu es un expert en pédagogie.
        On t'a fourni une fiche pédagogique existante au format HTML.
        Le professeur demande les modifications suivantes :
        "\(instructions)"

        FICHE ACTUELLE:
        ---
        \(currentHTML)
        ---

        INSTRUCTIONS:
        Applique les modifications demandées à la fiche existante.
        RÈGLE ABSOLUE : Retourne UNIQUEMENT le code HTML brut. N'inclus JAMAIS de blocs markdown (comme ```html ou ```). Ton retour doit commencer directement par les balises HTML.
        Ne modifie pas les éléments qui ne sont pas concernés par la demande.
        """

        let responseText = try await GeminiClient.shared.generate(
            prompt: prompt,
            purpose: "chat",
            temperature: 0.7,
            responseJSON: false,
            config: config
        )

        onProgress(100)
        onLog("✅ Fiche mise à jour.")
        return responseText.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    struct AgentIntentResponse: Codable {
        let intent: String
        let classLevel: String?
        let topic: String?
    }

    func analyzeIntent(prompt: String, hasFiche: Bool, config: AIConfig) async throws -> AgentIntentResponse {
        let systemPrompt = """
        Tu es un agent assistant pédagogique intelligent. Analyse la demande de l'utilisateur.
        L'utilisateur a actuellement une fiche générée : \(hasFiche ? "Oui" : "Non").
        Détermine si l'utilisateur veut modifier la fiche existante ("edit") ou s'il te demande de générer/créer une toute nouvelle leçon ("generate").
        Si la demande ressemble à "génère une leçon sur...", "crée une fiche pour...", c'est "generate".
        Si la demande ressemble à "ajoute un exercice", "corrige la faute", ou n'est qu'une conversation, c'est "edit".
        Si "generate", extrais le niveau de classe (ex: "CM1", "6ème") et le sujet de la leçon.
        Retourne UNIQUEMENT un objet JSON valide avec ce format:
        { "intent": "edit" | "generate", "classLevel": "...", "topic": "..." }
        """

        let jsonString = try await GeminiClient.shared.generate(
            prompt: systemPrompt + "\n\nDemande utilisateur: " + prompt,
            purpose: "chat",
            temperature: 0.1,
            responseJSON: true,
            config: config
        )

        // Parse JSON (strip possible markdown wrappers first)
        var cleanedJSON = jsonString.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanedJSON.hasPrefix("```json\n") { cleanedJSON.removeFirst(8) }
        else if cleanedJSON.hasPrefix("```\n") { cleanedJSON.removeFirst(4) }
        if cleanedJSON.hasSuffix("\n```") { cleanedJSON.removeLast(4) }
        else if cleanedJSON.hasSuffix("```") { cleanedJSON.removeLast(3) }
        
        guard let data = cleanedJSON.data(using: .utf8) else {
            return AgentIntentResponse(intent: "edit", classLevel: nil, topic: nil)
        }
        return try JSONDecoder().decode(AgentIntentResponse.self, from: data)
    }

    func editFicheStream(
        currentHTML: String,
        instructions: String,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void,
        onDelta: @escaping (String) -> Void
    ) async throws {
        onLog("✨ Modification de la fiche en cours...")
        onProgress(50)

        let prompt = """
        Tu es un expert en pédagogie.
        On t'a fourni une fiche pédagogique existante au format HTML.
        Le professeur demande les modifications suivantes :
        "\(instructions)"

        FICHE ACTUELLE:
        ---
        \(currentHTML)
        ---

        INSTRUCTIONS:
        Applique les modifications demandées à la fiche existante.
        RÈGLE ABSOLUE : Retourne UNIQUEMENT le code HTML brut. N'inclus JAMAIS de blocs markdown (comme ```html ou ```). Ton retour doit commencer directement par les balises HTML.
        Ne modifie pas les éléments qui ne sont pas concernés par la demande.
        """

        let stream = await GeminiClient.shared.generateStream(
            prompt: prompt,
            purpose: "chat",
            temperature: 0.7,
            responseJSON: false,
            config: config
        )

        for try await chunk in stream {
            onDelta(chunk)
        }

        onProgress(100)
        onLog("✅ Fiche mise à jour.")
    }

    func askQuestionStream(
        currentHTML: String,
        question: String,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void,
        onDelta: @escaping (String) -> Void
    ) async throws {
        onLog("💬 Réponse à la question en cours...")
        onProgress(50)

        let prompt = """
        Tu es un expert en pédagogie et assistant du professeur.
        On t'a fourni la fiche pédagogique actuelle au format HTML comme contexte.
        Le professeur te pose la question suivante ou demande le conseil suivant :
        "\(question)"

        FICHE ACTUELLE:
        ---
        \(currentHTML)
        ---

        INSTRUCTIONS:
        Réponds directement à la question du professeur de manière concise et utile.
        Utilise le format Markdown pour formater ta réponse (gras, listes, etc.).
        Ne réécris pas le code HTML de la fiche, donne juste ta réponse textuelle ou tes conseils.
        """

        let stream = await GeminiClient.shared.generateStream(
            prompt: prompt,
            purpose: "chat",
            temperature: 0.7,
            responseJSON: false,
            config: config
        )

        for try await chunk in stream {
            onDelta(chunk)
        }

        onProgress(100)
        onLog("✅ Réponse terminée.")
    }

    // MARK: - Prompt Builders

    private func buildFichePrompt(
        lessonTopic: String,
        classLevel: String,
        subject: String,
        lessonText: String,
        customTemplate: String,
        durationMinutes: Int,
        specialInstructions: String,
        useTopRatedExamples: Bool
    ) -> String {
        let duree = max(10, durationMinutes)
        let active = max(5, duree - 15)
        let subjectLine = subject.isEmpty ? "" : "Matière: \(subject)"

        let sourceSection = lessonText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? "Aucune source extraite. Génère selon le programme du niveau scolaire francophone."
            : """
            SOURCE PÉDAGOGIQUE (GUIDE DE L'ENSEIGNANT):
            ---
            \(String(lessonText.prefix(4000)))
            ---
            """

        let instructionsBlock = specialInstructions.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? ""
            : """

            **INSTRUCTIONS SPÉCIALES:**
            ---
            \(specialInstructions)
            ---
            """



        let effectiveStructure = customTemplate.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? defaultFicheStructure
            : customTemplate

        return """
        Tu es un expert en pédagogie francophone (primaire/collège). \
        Crée une fiche pédagogique complète et détaillée, prête à être utilisée par un enseignant.

        CONTEXTE:
        - Leçon : \(lessonTopic)
        - Niveau : \(classLevel.uppercased())
        - \(subjectLine)
        - Durée : \(duree) minutes (dont ~\(active) min actives)

        \(sourceSection)
        \(instructionsBlock)

        STRUCTURE ATTENDUE:
        \(effectiveStructure)

        EXIGENCES PÉDAGOGIQUES (OBLIGATOIRES):
        1. La fiche doit être complète, opérationnelle et adaptée au niveau \(classLevel).
        2. Chaque phase doit préciser les actions enseignant ET élèves.
        3. Inclure des questions d'évaluation avec corrigé.
        4. Durée totale : exactement \(duree) min.
        5. Si pertinent pour l'exercice, ajoute un tag image: <generateimage:"description précise">

        FORMAT DE SORTIE (JSON UNIQUEMENT, aucun texte autour):
        {
          "title": "Titre de la fiche",
          "metadata": {
            "chapter_title": "...",
            "lesson_title": "\(lessonTopic)",
            "duration_minutes": \(duree),
            "class_level": "\(classLevel)",
            "subject": "\(subject)",
            "materials": ["..."]
          },
          "objectives": ["Identifier ...", "Décrire ..."],
          "phases": [
            {
              "name": "Introduction",
              "goal": "...",
              "duration_minutes": 10,
              "teacher_steps": ["Je commence par...", "Je présente..."],
              "student_steps": ["Les élèves écoutent...", "Ils répondent..."],
              "materials": ["Manuel p.X"],
              "differentiation": "..."
            }
          ],
          "evaluation": {
            "strategy": "...",
            "questions": ["Question 1", "Question 2"],
            "answer_key": ["Réponse 1", "Réponse 2"]
          },
          "reminders": "...",
          "conclusion": "..."
        }
        """
    }

    private func buildEvaluationPrompt(
        classLevel: String,
        topics: [String],
        subject: String,
        durationMinutes: Int,
        difficulty: String,
        schoolName: String,
        sessionLabel: String,
        totalPoints: Int,
        extraInstructions: String,
        extractedContent: String
    ) -> String {
        let topicsText = topics.joined(separator: ", ")
        let isEarlyGrade = ["cp", "ce1"].contains(classLevel.lowercased())
        let earlyNote = isEarlyGrade ? """
            Pour CP/CE1: privilégie relier, cocher, tableau simple, texte à trous avec banque de mots, réponses courtes.
            Évite les longues productions écrites.
            """ : ""

        let sourceSection = extractedContent.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? "Aucune source extraite. Génère selon le programme du niveau et les sujets fournis."
            : """
            SOURCE PÉDAGOGIQUE À EXPLOITER PRIORITAIREMENT:
            ---
            \(String(extractedContent.prefix(3500)))
            ---
            """

        let extraBlock = extraInstructions.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? ""
            : "\nINSTRUCTIONS ENSEIGNANT:\n\(extraInstructions)\n"

        let difficultyNote: String
        switch difficulty {
        case "easy":   difficultyNote = "Niveau facile: questions directes, vocabulaire simple."
        case "hard":   difficultyNote = "Niveau difficile: questions de synthèse, réflexion approfondie."
        default:       difficultyNote = "Niveau moyen: mélange de questions factuelles et de réflexion."
        }

        return """
        Tu es un expert en évaluation scolaire francophone (primaire/collège). Crée une évaluation exploitable immédiatement.

        CONTEXTE:
        - Niveau: \(classLevel.uppercased())
        - Matière: \(subject.isEmpty ? "Sciences" : subject)
        - Sujets: \(topicsText)
        - Durée: \(durationMinutes) minutes
        - Difficulté: \(difficultyNote)

        \(sourceSection)
        \(earlyNote)
        \(extraBlock)

        EXIGENCES PÉDAGOGIQUES (OBLIGATOIRES):
        1. Génère 3 à 5 exercices variés et non redondants.
        2. Inclus obligatoirement:
           - au moins un exercice en tableau,
           - au moins un exercice de type relier / matching,
           - au moins un exercice de type texte à trous ou production courte.
        3. Progression du plus simple au plus complexe.
        4. Consignes claires et adaptées au niveau.
        5. Total des points: exactement 20 points.

        FORMAT DE SORTIE (JSON UNIQUEMENT):
        {
          "school_name": "\(schoolName.isEmpty ? "Groupe Scolaire" : schoolName)",
          "header": {
            "class_level": "\(classLevel.uppercased())",
            "academic_year": "2025/2026",
            "evaluation_number": 1,
            "semester": "1",
            "session_label": "\(sessionLabel.isEmpty ? "Évaluation" : sessionLabel)",
            "duration_minutes": \(durationMinutes),
            "max_score": \(totalPoints),
            "subject": "\(subject.isEmpty ? "Sciences" : subject)"
          },
          "exercises": [
            {
              "title": "Exercice 1",
              "instructions": "...",
              "points": 5,
              "questions": [
                {
                  "prompt": "...",
                  "answer_type": "tableau|matching|fill_blanks|short_answer|qcm|vf",
                  "expected_answer": "..."
                }
              ]
            }
          ],
          "answer_key": ["..."]
        }
        """
    }

    private func buildQuizPrompt(
        classLevel: String,
        topic: String,
        subject: String,
        durationMinutes: Int,
        numQuestions: Int,
        difficulty: String,
        schoolName: String,
        session: String,
        totalPoints: Int
    ) -> String {
        let difficultyNote: String
        switch difficulty {
        case "easy":   difficultyNote = "Questions faciles et directes."
        case "hard":   difficultyNote = "Questions complexes nécessitant de la réflexion."
        default:       difficultyNote = "Mélange de questions factuelles et de compréhension."
        }

        return """
        Tu es un expert en évaluation scolaire francophone. Crée un quiz rapide de \(numQuestions) questions.

        CONTEXTE:
        - Niveau: \(classLevel.uppercased())
        - Sujet: \(topic)
        - Matière: \(subject.isEmpty ? "(à adapter)" : subject)
        - École: \(schoolName.isEmpty ? "(Non spécifié)" : schoolName)
        - Session: \(session.isEmpty ? "(Non spécifié)" : session)
        - Points totaux: \(totalPoints)
        - Durée: \(durationMinutes) minutes
        - Difficulté: \(difficultyNote)

        EXIGENCES:
        1. Exactement \(numQuestions) questions variées (QCM, Vrai/Faux, Réponse courte).
        2. Progression du plus simple au plus complexe.
        3. Fournir un corrigé complet.

        FORMAT DE SORTIE (JSON UNIQUEMENT):
        {
          "title": "Quiz: \(topic)",
          "class_level": "\(classLevel)",
          "topic": "\(topic)",
          "subject": "\(subject)",
          "school": "\(schoolName)",
          "session": "\(session)",
          "total_points": \(totalPoints),
          "duration_minutes": \(durationMinutes),
          "instructions": ["Lis chaque question attentivement.", "Réponds directement sur la feuille."],
          "questions": [
            {
              "number": 1,
              "type": "qcm|vf|short_answer",
              "prompt": "...",
              "options": ["A. ...", "B. ...", "C. ...", "D. ..."],
              "expected_answer": "..."
            }
          ],
          "answer_key": ["1. ...", "2. ..."]
        }
        """
    }

    // MARK: - JSON → HTML Renderers
    // Port of core/ai.py: _render_fiche_markdown, _render_evaluation_markdown, _render_quiz_markdown

    func renderFicheHTML(_ data: [String: Any]) -> String {
        func cleanList(_ val: Any?) -> [String] {
            guard let val = val else { return [] }
            if let arr = val as? [Any] {
                return arr.compactMap { ($0 as? String)?.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
            }
            if let s = val as? String, !s.trimmingCharacters(in: .whitespaces).isEmpty {
                return [s.trimmingCharacters(in: .whitespaces)]
            }
            return []
        }

        let metadata = data["metadata"] as? [String: Any] ?? [:]
        let title = ((data["title"] as? String) ?? (metadata["lesson_title"] as? String) ?? "Fiche pédagogique").trimmingCharacters(in: .whitespaces)
        let classLevel = metadata["class_level"] as? String
        let subject = metadata["subject"] as? String
        let duration = GenerationEngine.safeInt(metadata["duration_minutes"])
        let chapterTitle = metadata["chapter_title"] as? String
        let materials = cleanList(metadata["materials"])
        let objectives = cleanList(data["objectives"])
        let phases = data["phases"] as? [[String: Any]] ?? []

        var lines: [String] = ["<h1>\(title)</h1>"]

        // Summary line
        var summaryParts: [String] = []
        if let cl = classLevel { summaryParts.append("Classe \(cl)") }
        if let s = subject, !s.isEmpty { summaryParts.append(s) }
        if let d = duration { summaryParts.append("\(d) min") }
        if let ch = chapterTitle, !ch.isEmpty { summaryParts.append("Ch: \(ch)") }
        if !summaryParts.isEmpty {
            lines.append("<blockquote><strong>Fiche pédagogique</strong> · \(summaryParts.joined(separator: " · "))</blockquote>")
        }

        if !materials.isEmpty {
            lines.append("<p><strong>Matériel</strong> : \(materials.joined(separator: ", "))</p>")
        }

        if !objectives.isEmpty {
            lines.append("<h2>Objectifs</h2>")
            lines.append("<ul>")
            for obj in objectives {
                lines.append("<li>\(obj)</li>")
            }
            lines.append("</ul>")
        }

        if !phases.isEmpty {
            lines.append("<h2>Plan de séance</h2>")
            lines.append("<ul>")
            for (i, phase) in phases.enumerated() {
                let name = (phase["name"] as? String) ?? "Phase \(i + 1)"
                let goal = (phase["goal"] as? String ?? "").trimmingCharacters(in: .whitespaces)
                let dur = GenerationEngine.safeInt(phase["duration_minutes"])
                let durText = dur.map { "\($0) min" } ?? "à adapter"
                if goal.isEmpty {
                    lines.append("<li><strong>Bloc \(i + 1) (\(durText))</strong>: \(name)</li>")
                } else {
                    lines.append("<li><strong>Bloc \(i + 1) (\(durText))</strong>: \(name) - \(goal)</li>")
                }
            }
            lines.append("</ul>")

            lines.append("<h2>Déroulement détaillé</h2>")
            for (i, phase) in phases.enumerated() {
                let name = (phase["name"] as? String) ?? "Phase \(i + 1)"
                let dur = GenerationEngine.safeInt(phase["duration_minutes"])
                var header = "Bloc \(i + 1) - \(name)"
                if let d = dur { header += " (\(d) min)" }
                lines.append("<h3>\(header)</h3>")

                if let goal = phase["goal"] as? String, !goal.trimmingCharacters(in: .whitespaces).isEmpty {
                    lines.append("<p><strong>Objectif de la phase</strong> : \(goal)</p>")
                }
                let teacherSteps = cleanList(phase["teacher_steps"])
                if !teacherSteps.isEmpty {
                    lines.append("<p><strong>Actions de l'enseignant</strong></p>")
                    lines.append("<ul>")
                    teacherSteps.forEach { lines.append("<li>\($0)</li>") }
                    lines.append("</ul>")
                }
                let studentSteps = cleanList(phase["student_steps"])
                if !studentSteps.isEmpty {
                    lines.append("<p><strong>Actions des élèves</strong></p>")
                    lines.append("<ul>")
                    studentSteps.forEach { lines.append("<li>\($0)</li>") }
                    lines.append("</ul>")
                }
                let phaseMaterials = cleanList(phase["materials"])
                if !phaseMaterials.isEmpty {
                    lines.append("<p><strong>Supports</strong> : \(phaseMaterials.joined(separator: ", "))</p>")
                }
                if let diff = phase["differentiation"] as? String, !diff.trimmingCharacters(in: .whitespaces).isEmpty {
                    lines.append("<p><strong>Différenciation</strong> : \(diff)</p>")
                }
            }
        }

        let evaluation = data["evaluation"] as? [String: Any] ?? [:]
        if !evaluation.isEmpty {
            lines.append("<h2>Évaluation</h2>")
            if let strategy = evaluation["strategy"] as? String, !strategy.isEmpty {
                lines.append("<p><strong>Modalité</strong> : \(strategy)</p>")
            }
            let questions = cleanList(evaluation["questions"])
            if !questions.isEmpty {
                lines.append("<h3>Questions</h3>")
                lines.append("<ol>")
                questions.forEach { lines.append("<li>\($0)</li>") }
                lines.append("</ol>")
            }
            let answers = cleanList(evaluation["answer_key"])
            if !answers.isEmpty {
                lines.append("<h3>Éléments de correction</h3>")
                lines.append("<ol>")
                answers.forEach { lines.append("<li>\($0)</li>") }
                lines.append("</ol>")
            }
        }

        if let reminders = data["reminders"] as? String, !reminders.trimmingCharacters(in: .whitespaces).isEmpty {
            lines.append("<h2>Remarques</h2>")
            lines.append("<ul>")
            lines.append("<li>\(reminders)</li>")
            lines.append("</ul>")
        }

        if let conclusion = data["conclusion"] as? String, !conclusion.trimmingCharacters(in: .whitespaces).isEmpty {
            lines.append("<h2>Conclusion</h2>")
            lines.append("<p>\(conclusion)</p>")
        }

        return lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func renderEvaluationHTML(_ data: [String: Any]) -> String {
        func cleanText(_ val: Any?, fallback: String = "") -> String {
            let s = (val as? String ?? "\(val ?? "")").trimmingCharacters(in: .whitespaces)
            return s.isEmpty ? fallback : s
        }
        func scoreText(_ val: Any?) -> String {
            if let n = val as? Double { return n.truncatingRemainder(dividingBy: 1) == 0 ? String(Int(n)) : String(format: "%.1f", n) }
            if let n = val as? Int { return "\(n)" }
            return cleanText(val)
        }

        let header = data["header"] as? [String: Any] ?? [:]
        let schoolName = cleanText(data["school_name"], fallback: "Groupe Scolaire")
        let classLevel = cleanText(header["class_level"], fallback: "CM1")
        let subject = cleanText(header["subject"], fallback: "Matière")
        let duration = cleanText(header["duration_minutes"], fallback: "45")
        let maxScore = scoreText(header["max_score"] ?? 20)

        var sessionLabel = cleanText(header["session_label"])
        if sessionLabel.isEmpty {
            let evalNum = GenerationEngine.safeInt(header["evaluation_number"]) ?? 1
            let semester = cleanText(header["semester"], fallback: "1")
            let numWord = evalNum == 1 ? "1er" : "\(evalNum)e"
            let semWord = semester == "1" ? "1er" : "\(semester)e"
            sessionLabel = "\(numWord) contrôle du \(semWord) semestre"
        }

        var lines: [String] = []
        lines.append("<h1>ÉPREUVE D'ÉVALUATION</h1>")
        lines.append("<blockquote>")
        lines.append("<p><strong>\(schoolName)</strong><br>")
        lines.append("<strong>Session</strong> : \(sessionLabel)<br>")
        lines.append("<strong>Niveau</strong> : \(classLevel)<br>")
        lines.append("<strong>Matière</strong> : \(subject)<br>")
        lines.append("<strong>Durée</strong> : \(duration) min<br>")
        lines.append("<strong>Total</strong> : \(maxScore) points</p>")
        lines.append("</blockquote>")
        
        lines.append("<h2>Cadre élève</h2>")
        lines.append("<ul>")
        lines.append("<li>Nom et prénom : ________________________________________________</li>")
        lines.append("<li>Date : ____________________</li>")
        lines.append("<li>Classe : __________________</li>")
        lines.append("<li>Note finale : ________ / \(maxScore)</li>")
        lines.append("</ul>")
        
        lines.append("<h2>Consignes générales</h2>")
        lines.append("<ol>")
        lines.append("<li>Lis chaque consigne avec attention et respecte la durée (\(duration) min).</li>")
        lines.append("<li>Soigne la présentation et justifie quand la consigne l'exige.</li>")
        lines.append("<li>Réponds directement dans les espaces prévus.</li>")
        lines.append("</ol>")

        let exercises = data["exercises"] as? [[String: Any]] ?? []
        if !exercises.isEmpty {
            lines.append("<h2>Barème de l'épreuve</h2>")
            lines.append("<table>")
            lines.append("<thead>")
            lines.append("<tr><th>Exercice</th><th>Points</th></tr>")
            lines.append("</thead>")
            lines.append("<tbody>")
            for (i, ex) in exercises.enumerated() {
                let label = cleanText(ex["title"], fallback: "Exercice \(i + 1)")
                let pts = scoreText(ex["points"])
                lines.append("<tr><td>\(label)</td><td>\(pts)</td></tr>")
            }
            lines.append("<tr><td><strong>Total</strong></td><td><strong>\(maxScore)</strong></td></tr>")
            lines.append("</tbody>")
            lines.append("</table>")
            lines.append("<hr>")
        }

        for (i, exercise) in exercises.enumerated() {
            let title = cleanText(exercise["title"], fallback: "Exercice \(i + 1)")
            let instructions = cleanText(exercise["instructions"])
            let points = scoreText(exercise["points"])
            lines.append("<h2>Exercice \(i + 1) - \(title)</h2>")
            lines.append("<p><strong>Points :</strong> \(points)</p>")
            if !instructions.isEmpty { lines.append("<p><strong>Consigne :</strong> \(instructions)</p>") }

            let questions = exercise["questions"] as? [[String: Any]] ?? []
            for (j, question) in questions.enumerated() {
                let prompt = cleanText(question["prompt"])
                if !prompt.isEmpty {
                    let answerType = cleanText(question["answer_type"], fallback: "réponse ouverte")
                    lines.append("<h3>Q\(i + 1).\(j + 1) [\(answerType)]</h3>")
                    lines.append("<p>\(prompt)</p>")
                    lines.append("<p>Réponse : __________________________________________________________<br>")
                    lines.append("____________________________________________________________</p>")
                }
            }
            lines.append("<hr>")
        }

        if let answerKey = data["answer_key"] as? [Any], !answerKey.isEmpty {
            lines.append("<h2>Corrigé enseignant</h2>")
            lines.append("<ul>")
            for answer in answerKey {
                if let d = answer as? [String: Any] {
                    let ref = cleanText(d["reference"], fallback: "Question")
                    let val = cleanText(d["answer"])
                    lines.append("<li><strong>\(ref)</strong> : \(val)</li>")
                } else {
                    lines.append("<li>\(answer)</li>")
                }
            }
            lines.append("</ul>")
        }

        return lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func renderQuizHTML(_ data: [String: Any]) -> String {
        let title = (data["title"] as? String) ?? "Quiz: \(data["topic"] as? String ?? "Sujet")"
        let classLevel = data["class_level"] as? String ?? ""
        let duration = GenerationEngine.safeInt(data["duration_minutes"])
        let subject = data["subject"] as? String ?? ""
        let topic = data["topic"] as? String ?? ""

        var lines: [String] = ["<h1>\(title)</h1>"]
        var meta: [String] = []
        if !classLevel.isEmpty { meta.append("<strong>Classe</strong> : \(classLevel)") }
        if !subject.isEmpty    { meta.append("<strong>Matière</strong> : \(subject)") }
        if !topic.isEmpty      { meta.append("<strong>Sujet</strong> : \(topic)") }
        if let d = duration    { meta.append("<strong>Durée</strong> : \(d) min") }
        if !meta.isEmpty { 
            lines.append("<p>" + meta.joined(separator: " | ") + "</p>")
        }

        if let instructions = data["instructions"] as? [String], !instructions.isEmpty {
            lines.append("<h2>Consignes</h2>")
            lines.append("<ul>")
            instructions.forEach { lines.append("<li>\($0)</li>") }
            lines.append("</ul>")
        }

        lines.append("<h2>Questions</h2>")
        for q in data["questions"] as? [[String: Any]] ?? [] {
            let number = q["number"] as? Int ?? 0
            let type_ = q["type"] as? String ?? "question"
            let prompt = q["prompt"] as? String ?? ""
            lines.append("<h3>Question \(number) (\(type_))</h3>")
            lines.append("<p>\(prompt)</p>")
            if let options = q["options"] as? [String], !options.isEmpty {
                lines.append("<ul>")
                for (i, opt) in options.enumerated() {
                    lines.append("<li>\(String(UnicodeScalar(65 + i)!)). \(opt)</li>")
                }
                lines.append("</ul>")
            }
        }

        if let answerKey = data["answer_key"] as? [String], !answerKey.isEmpty {
            lines.append("<h2>Corrigé</h2>")
            lines.append("<ul>")
            answerKey.forEach { lines.append("<li>\($0)</li>") }
            lines.append("</ul>")
        }

        return lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

// Extension with redundant editFiche placeholder removed
