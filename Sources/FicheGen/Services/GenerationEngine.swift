import Foundation

// MARK: - GenerationEngine
// Native Swift replacement for core/workers.py + core/ai.py rendering logic.
// Handles fiche, evaluation, and quiz generation entirely in Swift via GeminiClient.

@MainActor
final class GenerationEngine {

    static let shared = GenerationEngine()

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

        // Parse JSON → render Markdown
        if let data = responseText.data(using: .utf8),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           !json.isEmpty {
            var markdown = renderFicheMarkdown(json)
            if !markdown.isEmpty {
                if config.expMultiPassGen && config.expMultiPassIterations > 1 {
                    markdown = try await performMultiPass(
                        initialMarkdown: markdown,
                        iterations: config.expMultiPassIterations,
                        config: config,
                        onLog: onLog,
                        onProgress: onProgress
                    )
                }

                onLog("✅ Fiche générée.")
                onProgress(100)
                return markdown
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
        initialMarkdown: String,
        iterations: Int,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void
    ) async throws -> String {
        var currentMarkdown = initialMarkdown
        let loops = max(1, iterations - 1)
        
        for i in 1...loops {
            onLog("🔄 Passe de raffinement \(i)/\(loops)...")
            onProgress(95 + (i * 4 / loops))
            
            let prompt = """
            Tu es un expert en pédagogie. Analyse et améliore la fiche pédagogique suivante.
            Corrige les incohérences, améliore la formulation, assure-toi que les durées correspondent, et enrichis le contenu si nécessaire.
            Ne retourne QUE le Markdown amélioré, sans texte introductif.
            
            FICHE ACTUELLE:
            ---
            \(currentMarkdown)
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
                currentMarkdown = refined
            }
        }
        return currentMarkdown
    }

    // MARK: - Evaluation Generation

    func generateEvaluation(
        classLevel: String,
        topics: [String],
        subject: String,
        durationMinutes: Int,
        difficulty: String,
        temperature: Double,
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

        if let data = responseText.data(using: .utf8),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           !json.isEmpty {
            let markdown = renderEvaluationMarkdown(json)
            if !markdown.isEmpty {
                onLog("✅ Évaluation générée.")
                onProgress(100)
                return markdown
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
            difficulty: difficulty
        )

        let responseText = try await GeminiClient.shared.generate(
            prompt: prompt,
            purpose: "quiz-generation",
            temperature: max(0.0, min(1.0, temperature)),
            responseJSON: true,
            config: config
        )

        onProgress(95)

        if let data = responseText.data(using: .utf8),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           !json.isEmpty {
            let markdown = renderQuizMarkdown(json)
            if !markdown.isEmpty {
                onLog("✅ Quiz généré.")
                onProgress(100)
                return markdown
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
        currentMarkdown: String,
        instructions: String,
        config: AIConfig,
        onLog: @escaping (String) -> Void,
        onProgress: @escaping (Int) -> Void
    ) async throws -> String {
        onLog("✨ Modification de la fiche en cours...")
        onProgress(50)

        let prompt = """
        Tu es un expert en pédagogie.
        On t'a fourni une fiche pédagogique existante au format Markdown.
        Le professeur demande les modifications suivantes :
        "\(instructions)"

        FICHE ACTUELLE:
        ---
        \(currentMarkdown)
        ---

        INSTRUCTIONS:
        Applique les modifications demandées à la fiche existante.
        Retourne UNIQUEMENT le code Markdown mis à jour, sans aucun texte introductif ni balises markdown (```).
        Ne modifie pas les éléments qui ne sont pas concernés par la demande.
        """

        let responseText = try await GeminiClient.shared.generate(
            prompt: prompt,
            purpose: "fiche-edit",
            temperature: 0.7,
            responseJSON: false,
            config: config
        )

        onProgress(100)
        onLog("✅ Fiche mise à jour.")
        return responseText.trimmingCharacters(in: .whitespacesAndNewlines)
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
          "school_name": "Groupe Scolaire",
          "header": {
            "class_level": "\(classLevel.uppercased())",
            "academic_year": "2025/2026",
            "evaluation_number": 1,
            "semester": "1",
            "session_label": "1er contrôle du 1er semestre",
            "duration_minutes": \(durationMinutes),
            "max_score": 20,
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
        difficulty: String
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

    // MARK: - JSON → Markdown Renderers
    // Port of core/ai.py: _render_fiche_markdown, _render_evaluation_markdown, _render_quiz_markdown

    func renderFicheMarkdown(_ data: [String: Any]) -> String {
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
        let duration = metadata["duration_minutes"] as? Int
        let chapterTitle = metadata["chapter_title"] as? String
        let materials = cleanList(metadata["materials"])
        let objectives = cleanList(data["objectives"])
        let phases = data["phases"] as? [[String: Any]] ?? []

        var lines: [String] = ["# \(title)"]

        // Summary line
        var summaryParts: [String] = []
        if let cl = classLevel { summaryParts.append("Classe \(cl)") }
        if let s = subject, !s.isEmpty { summaryParts.append(s) }
        if let d = duration { summaryParts.append("\(d) min") }
        if let ch = chapterTitle, !ch.isEmpty { summaryParts.append("Ch: \(ch)") }
        if !summaryParts.isEmpty {
            lines.append("")
            lines.append("> **Fiche pédagogique** · \(summaryParts.joined(separator: " · "))")
        }

        if !materials.isEmpty {
            lines.append("")
            lines.append("**Matériel** : \(materials.joined(separator: ", "))")
        }

        if !objectives.isEmpty {
            lines.append("")
            lines.append("## Objectifs")
            for (i, obj) in objectives.enumerated() {
                lines.append("\(i + 1). \(obj)")
            }
        }

        if !phases.isEmpty {
            lines.append("")
            lines.append("## Plan de séance")
            for (i, phase) in phases.enumerated() {
                let name = (phase["name"] as? String) ?? "Phase \(i + 1)"
                let goal = (phase["goal"] as? String ?? "").trimmingCharacters(in: .whitespaces)
                let dur = phase["duration_minutes"] as? Int
                let durText = dur.map { "\($0) min" } ?? "à adapter"
                if goal.isEmpty {
                    lines.append("- **Bloc \(i + 1) (\(durText))**: \(name)")
                } else {
                    lines.append("- **Bloc \(i + 1) (\(durText))**: \(name) - \(goal)")
                }
            }

            lines.append("")
            lines.append("## Déroulement détaillé")
            for (i, phase) in phases.enumerated() {
                let name = (phase["name"] as? String) ?? "Phase \(i + 1)"
                let dur = phase["duration_minutes"] as? Int
                var header = "### Bloc \(i + 1) - \(name)"
                if let d = dur { header += " (\(d) min)" }
                lines.append(header)

                if let goal = phase["goal"] as? String, !goal.trimmingCharacters(in: .whitespaces).isEmpty {
                    lines.append("**Objectif de la phase** : \(goal)")
                }
                let teacherSteps = cleanList(phase["teacher_steps"])
                if !teacherSteps.isEmpty {
                    lines.append("**Actions de l'enseignant**")
                    teacherSteps.forEach { lines.append("- \($0)") }
                }
                let studentSteps = cleanList(phase["student_steps"])
                if !studentSteps.isEmpty {
                    lines.append("**Actions des élèves**")
                    studentSteps.forEach { lines.append("- \($0)") }
                }
                let phaseMaterials = cleanList(phase["materials"])
                if !phaseMaterials.isEmpty {
                    lines.append("**Supports** : \(phaseMaterials.joined(separator: ", "))")
                }
                if let diff = phase["differentiation"] as? String, !diff.trimmingCharacters(in: .whitespaces).isEmpty {
                    lines.append("**Différenciation** : \(diff)")
                }
                lines.append("")
            }
        }

        let evaluation = data["evaluation"] as? [String: Any] ?? [:]
        if !evaluation.isEmpty {
            lines.append("## Évaluation")
            if let strategy = evaluation["strategy"] as? String, !strategy.isEmpty {
                lines.append("**Modalité** : \(strategy)")
            }
            let questions = cleanList(evaluation["questions"])
            if !questions.isEmpty {
                lines.append("### Questions")
                questions.enumerated().forEach { lines.append("\($0.offset + 1). \($0.element)") }
            }
            let answers = cleanList(evaluation["answer_key"])
            if !answers.isEmpty {
                lines.append("### Éléments de correction")
                answers.enumerated().forEach { lines.append("\($0.offset + 1). \($0.element)") }
            }
            lines.append("")
        }

        if let reminders = data["reminders"] as? String, !reminders.trimmingCharacters(in: .whitespaces).isEmpty {
            lines.append("## Remarques")
            lines.append("- \(reminders)")
            lines.append("")
        }

        if let conclusion = data["conclusion"] as? String, !conclusion.trimmingCharacters(in: .whitespaces).isEmpty {
            lines.append("## Conclusion")
            lines.append(conclusion)
        }

        return lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func renderEvaluationMarkdown(_ data: [String: Any]) -> String {
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
            let evalNum = (header["evaluation_number"] as? Int) ?? 1
            let semester = cleanText(header["semester"], fallback: "1")
            let numWord = evalNum == 1 ? "1er" : "\(evalNum)e"
            let semWord = semester == "1" ? "1er" : "\(semester)e"
            sessionLabel = "\(numWord) contrôle du \(semWord) semestre"
        }

        var lines: [String] = []
        lines.append("# ÉPREUVE D'ÉVALUATION")
        lines.append("")
        lines.append("> **\(schoolName)**")
        lines.append("> **Session** : \(sessionLabel)")
        lines.append("> **Niveau** : \(classLevel)")
        lines.append("> **Matière** : \(subject)")
        lines.append("> **Durée** : \(duration) min")
        lines.append("> **Total** : \(maxScore) points")
        lines.append("")
        lines.append("## Cadre élève")
        lines.append("- Nom et prénom : ________________________________________________")
        lines.append("- Date : ____________________")
        lines.append("- Classe : __________________")
        lines.append("- Note finale : ________ / \(maxScore)")
        lines.append("")
        lines.append("## Consignes générales")
        lines.append("1. Lis chaque consigne avec attention et respecte la durée (\(duration) min).")
        lines.append("2. Soigne la présentation et justifie quand la consigne l'exige.")
        lines.append("3. Réponds directement dans les espaces prévus.")
        lines.append("")

        let exercises = data["exercises"] as? [[String: Any]] ?? []
        if !exercises.isEmpty {
            lines.append("## Barème de l'épreuve")
            lines.append("| Exercice | Points |")
            lines.append("|:---------|------:|")
            for (i, ex) in exercises.enumerated() {
                let label = cleanText(ex["title"], fallback: "Exercice \(i + 1)")
                let pts = scoreText(ex["points"])
                lines.append("| \(label) | \(pts) |")
            }
            lines.append("| **Total** | **\(maxScore)** |")
            lines.append("")
        }

        lines.append("---")
        lines.append("")

        for (i, exercise) in exercises.enumerated() {
            let title = cleanText(exercise["title"], fallback: "Exercice \(i + 1)")
            let instructions = cleanText(exercise["instructions"])
            let points = scoreText(exercise["points"])
            lines.append("## Exercice \(i + 1) - \(title)")
            lines.append("**Points :** \(points)")
            if !instructions.isEmpty { lines.append("**Consigne :** \(instructions)") }
            lines.append("")

            let questions = exercise["questions"] as? [[String: Any]] ?? []
            for (j, question) in questions.enumerated() {
                let prompt = cleanText(question["prompt"])
                if !prompt.isEmpty {
                    let answerType = cleanText(question["answer_type"], fallback: "réponse ouverte")
                    lines.append("### Q\(i + 1).\(j + 1) [\(answerType)]")
                    lines.append(prompt)
                    lines.append("")
                    lines.append("Réponse : __________________________________________________________")
                    lines.append("____________________________________________________________")
                    lines.append("")
                }
            }
            lines.append("")
            lines.append("---")
            lines.append("")
        }

        if let answerKey = data["answer_key"] as? [Any], !answerKey.isEmpty {
            lines.append("## Corrigé enseignant")
            lines.append("")
            for answer in answerKey {
                if let d = answer as? [String: Any] {
                    let ref = cleanText(d["reference"], fallback: "Question")
                    let val = cleanText(d["answer"])
                    lines.append("- **\(ref)** : \(val)")
                } else {
                    lines.append("- \(answer)")
                }
            }
            lines.append("")
        }

        return lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func renderQuizMarkdown(_ data: [String: Any]) -> String {
        let title = (data["title"] as? String) ?? "Quiz: \(data["topic"] as? String ?? "Sujet")"
        let classLevel = data["class_level"] as? String ?? ""
        let duration = data["duration_minutes"] as? Int
        let subject = data["subject"] as? String ?? ""
        let topic = data["topic"] as? String ?? ""

        var lines: [String] = ["# \(title)", ""]
        var meta: [String] = []
        if !classLevel.isEmpty { meta.append("**Classe** : \(classLevel)") }
        if !subject.isEmpty    { meta.append("**Matière** : \(subject)") }
        if !topic.isEmpty      { meta.append("**Sujet** : \(topic)") }
        if let d = duration    { meta.append("**Durée** : \(d) min") }
        if !meta.isEmpty { lines.append(meta.joined(separator: " | ")); lines.append("") }

        if let instructions = data["instructions"] as? [String], !instructions.isEmpty {
            lines.append("## Consignes")
            instructions.forEach { lines.append("- \($0)") }
            lines.append("")
        }

        lines.append("## Questions")
        lines.append("")
        for q in data["questions"] as? [[String: Any]] ?? [] {
            let number = q["number"] as? Int ?? 0
            let type_ = q["type"] as? String ?? "question"
            let prompt = q["prompt"] as? String ?? ""
            lines.append("### Question \(number) (\(type_))")
            lines.append(prompt)
            if let options = q["options"] as? [String], !options.isEmpty {
                for (i, opt) in options.enumerated() {
                    lines.append("- \(String(UnicodeScalar(65 + i)!)). \(opt)")
                }
            }
            lines.append("")
        }

        if let answerKey = data["answer_key"] as? [String], !answerKey.isEmpty {
            lines.append("## Corrigé")
            lines.append("")
            answerKey.forEach { lines.append("- \($0)") }
        }

        return lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

extension GenerationEngine {
    func editFiche(instruction: String, markdown: String) async throws -> String {
        return markdown // User will implement this
    }
}
