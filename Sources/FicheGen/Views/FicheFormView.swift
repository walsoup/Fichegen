import SwiftUI
import UniformTypeIdentifiers

struct FicheFormView: View {
    @EnvironmentObject var state: AppState
    @State private var showBatchSheet = false
    @State private var isTargeted = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                // ── API Key Warning Banner ──────────────────────────────
                if !state.isConfigured {
                    HStack(alignment: .top, spacing: 12) {
                        Image(systemName: "exclamationmark.shield.fill")
                            .font(.title2)
                            .foregroundColor(.orange)
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Configuration API manquante")
                                .font(.headline)
                                .foregroundColor(.primary)
                            Text("Veuillez configurer votre clé API (Gemini, Proxy, etc.) dans les Préférences (Cmd + ,) pour pouvoir générer des fiches.")
                                .font(.subheadline)
                                .foregroundColor(.secondary)
                        }
                        Spacer()
                    }
                    .padding(16)
                    .background(.ultraThinMaterial)
                    .cornerRadius(12)
                    .overlay(
                        RoundedRectangle(cornerRadius: 12)
                            .stroke(Color.orange.opacity(0.4), lineWidth: 1)
                    )
                }

                // ── Class & Subject ──────────────────────────────────────
                GroupBox(label: Label("Classe & Matière", systemImage: "graduationcap")) {
                    Form {
                        Picker("Classe", selection: $state.ficheClassLevel) {
                            ForEach(state.classLevels, id: \.self) { Text($0) }
                        }
                        .pickerStyle(.menu)
                        
                        TextField("Matière", text: $state.ficheSubject)
                            .textFieldStyle(.roundedBorder)
                    }
                }

                // ── Lesson ───────────────────────────────────────────────
                GroupBox(label: Label("Leçon & Programme", systemImage: "book")) {
                    VStack(alignment: .leading, spacing: 10) {
                        if !state.availableLessons.isEmpty {
                            VStack(alignment: .leading, spacing: 4) {
                                HStack {
                                    Text("📚 Sélectionner une leçon suggérée :")
                                        .font(.subheadline)
                                        .fontWeight(.medium)
                                        .foregroundStyle(.secondary)
                                    Spacer()
                                    Button {
                                        state.loadAvailableLessons(classLevel: state.ficheClassLevel)
                                    } label: {
                                        Image(systemName: "arrow.triangle.2.circlepath")
                                    }
                                    .buttonStyle(.plain)
                                    .help("Mettre à jour la table des matières")
                                }
                                Picker("", selection: $state.ficheLessonTopic) {
                                    Text("Saisir un titre personnalisé...").tag("")
                                    ForEach(state.availableLessons, id: \.self) { lesson in
                                        Text(lesson).tag(lesson)
                                    }
                                }
                                .pickerStyle(.menu)
                                .labelsHidden()
                                .frame(maxWidth: .infinity)
                            }
                        }
                        
                        Text("Titre de la leçon (Sujet) :")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        
                        TextField("Saisir le sujet ou titre de la leçon", text: $state.ficheLessonTopic)
                            .textFieldStyle(.roundedBorder)
                        
                        if state.availableLessons.isEmpty {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("Aucune leçon en cache pour ce niveau.")
                                    .font(.caption)
                                    .foregroundStyle(.orange)
                                Button {
                                    state.loadAvailableLessons(classLevel: state.ficheClassLevel)
                                } label: {
                                    Label("Scanner le manuel pédagogique", systemImage: "magnifyingglass")
                                        .font(.caption)
                                }
                                .buttonStyle(.bordered)
                                .controlSize(.small)
                            }
                            .padding(.top, 2)
                        }
                        
                        Text("Pages spécifiques (ex: 12, 13-15) :")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        
                        TextField("Laisser vide pour tout le manuel", text: $state.fichePagesOverride)
                            .textFieldStyle(.roundedBorder)
                    }
                    .padding(.vertical, 2)
                }
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(isTargeted ? Color.accentColor : Color.clear, lineWidth: 3)
                )
                .onDrop(of: [.fileURL], isTargeted: $isTargeted) { providers in
                    if let provider = providers.first {
                        provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, _ in
                            if let data = item as? Data, let url = URL(dataRepresentation: data, relativeTo: nil) {
                                if url.pathExtension.lowercased() == "pdf" {
                                    DispatchQueue.main.async { state.droppedPDFURL = url }
                                }
                            } else if let url = item as? URL, url.pathExtension.lowercased() == "pdf" {
                                DispatchQueue.main.async { state.droppedPDFURL = url }
                            }
                        }
                        return true
                    }
                    return false
                }
                
                if let dropped = state.droppedPDFURL {
                    HStack {
                        Image(systemName: "doc.text.fill")
                            .foregroundColor(.blue)
                        Text("PDF Source : \(dropped.lastPathComponent)")
                            .font(.subheadline)
                        Spacer()
                        Button(action: { state.droppedPDFURL = nil }) {
                            Image(systemName: "xmark.circle.fill")
                                .foregroundColor(.secondary)
                        }
                        .buttonStyle(.plain)
                    }
                    .padding(10)
                    .background(Color.blue.opacity(0.1))
                    .cornerRadius(8)
                }

                // ── Parameters ───────────────────────────────────────────
                GroupBox(label: Label("Paramètres de génération", systemImage: "slider.horizontal.3")) {
                    Form {
                        Stepper("Durée: \(state.ficheDurationMinutes) min",
                                value: $state.ficheDurationMinutes,
                                in: 15...120, step: 5)
                        
                        VStack(alignment: .leading, spacing: 2) {
                            HStack {
                                Text("Créativité")
                                Spacer()
                                Text(String(format: "%.1f", state.ficheTemperature))
                                    .foregroundStyle(.secondary)
                                    .monospacedDigit()
                            }
                            Slider(value: $state.ficheTemperature, in: 0...1, step: 0.1)
                        }
                        
                        Toggle("Image illustrée", isOn: $state.ficheGenerateImage)
                        Toggle("Exemples top-rated", isOn: $state.ficheUseTopRated)
                    }
                }

                // ── Instructions ─────────────────────────────────────────
                GroupBox(label: Label("Instructions spéciales (Optionnel)", systemImage: "lightbulb")) {
                    TextEditor(text: $state.ficheSpecialInstructions)
                        .font(.system(.body, design: .default))
                        .frame(minHeight: 70, maxHeight: 120)
                        .scrollContentBackground(.hidden)
                }
                
                // ── Advanced Routing ─────────────────────────────────────
                if state.expShowAdvancedRoutingInForms {
                    GroupBox(label: Label("Routage Avancé (Expérimental)", systemImage: "network")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Picker("Fournisseur", selection: Binding(
                                get: { state.routingFicheProvider },
                                set: { state.updateSetting(key: "routing_fiche_provider", value: $0) }
                            )) {
                                Text("Par défaut").tag("default")
                                Text("Gemini Studio").tag("gemini")
                                Text("Vertex AI").tag("vertex")
                                Text("Proxy API").tag("proxy")
                                Text("Vercel AI").tag("vercel")
                            }
                            .pickerStyle(.menu)
                            
                            HStack(spacing: 4) {
                                TextField("Nom de modèle spécifique (Optionnel)", text: Binding(
                                    get: { state.routingFicheModel },
                                    set: { state.updateSetting(key: "routing_fiche_model", value: $0) }
                                ))
                                .textFieldStyle(.roundedBorder)
                                
                                Menu {
                                    Button("gemini-3.5-flash") {
                                        state.updateSetting(key: "routing_fiche_model", value: "gemini-3.5-flash")
                                    }
                                    Button("gemini-2.5-pro") {
                                        state.updateSetting(key: "routing_fiche_model", value: "gemini-2.5-pro")
                                    }
                                    Button("gemini-2.5-flash") {
                                        state.updateSetting(key: "routing_fiche_model", value: "gemini-2.5-flash")
                                    }
                                    Button("gemma-4-31b-it") {
                                        state.updateSetting(key: "routing_fiche_model", value: "gemma-4-31b-it")
                                    }
                                    Button("gemma-4-27b-e4b-it") {
                                        state.updateSetting(key: "routing_fiche_model", value: "gemma-4-27b-e4b-it")
                                    }
                                    Divider()
                                    Button("gpt-4o") {
                                        state.updateSetting(key: "routing_fiche_model", value: "gpt-4o")
                                    }
                                    Button("gpt-4o-mini") {
                                        state.updateSetting(key: "routing_fiche_model", value: "gpt-4o-mini")
                                    }
                                    Button("claude-3-5-sonnet") {
                                        state.updateSetting(key: "routing_fiche_model", value: "claude-3-5-sonnet")
                                    }
                                } label: {
                                    Image(systemName: "cpu")
                                        .foregroundStyle(.secondary)
                                }
                                .menuStyle(.borderlessButton)
                                .fixedSize()
                            }
                        }
                    }
                }

                // ── Action ───────────────────────────────────────────────
                HStack {
                    GenerateButton(
                        label: "Générer la Fiche",
                        isEnabled: !state.ficheSubject.trimmingCharacters(in: .whitespaces).isEmpty &&
                                   !state.ficheLessonTopic.trimmingCharacters(in: .whitespaces).isEmpty &&
                                   state.isConfigured
                    ) {
                        state.generateFiche()
                    }
                    
                    if state.expEnableBatch {
                        Button {
                            showBatchSheet = true
                        } label: {
                            Label("Générer en Lot (Batch)", systemImage: "square.grid.2x2.fill")
                        }
                        .buttonStyle(.bordered)
                        .controlSize(.large)
                    }
                }
            }
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .sheet(isPresented: $showBatchSheet) {
            BatchSelectionView(generationType: "fiche")
        }
        .onChange(of: state.ficheClassLevel) { _, newValue in
            state.loadAvailableLessons(classLevel: newValue)
        }
    }
}

struct BatchSelectionView: View {
    @EnvironmentObject var state: AppState
    let generationType: String // "fiche", "eval", "quiz"
    @Environment(\.dismiss) var dismiss
    
    @State private var selectedLessons = Set<String>()
    @State private var customTopics: String = ""
    @State private var isProcessing = false
    @State private var batchId: String? = nil
    @State private var errorMessage: String? = nil
    
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text("Génération par Lot (\(generationType.capitalized))")
                .font(.title2)
                .bold()
            
            if !state.availableLessons.isEmpty {
                GroupBox("Leçons Suggérées") {
                    ScrollView {
                        VStack(alignment: .leading) {
                            ForEach(state.availableLessons, id: \.self) { lesson in
                                Toggle(lesson, isOn: Binding(
                                    get: { selectedLessons.contains(lesson) },
                                    set: { isOn in
                                        if isOn { selectedLessons.insert(lesson) }
                                        else { selectedLessons.remove(lesson) }
                                    }
                                ))
                            }
                        }
                    }
                    .frame(minHeight: 100, maxHeight: 200)
                }
            }
            
            GroupBox("Sujets personnalisés (un par ligne)") {
                TextEditor(text: $customTopics)
                    .frame(height: 100)
                    .scrollContentBackground(.hidden)
            }
            
            if let batchId = batchId {
                Text("Batch créé avec succès ! ID: \(batchId)")
                    .foregroundColor(.green)
            }
            if let error = errorMessage {
                Text(error)
                    .foregroundColor(.red)
            }
            
            HStack {
                Button("Annuler") { dismiss() }
                Spacer()
                Button(isProcessing ? "Traitement..." : "Lancer le Batch") {
                    runBatch()
                }
                .buttonStyle(.borderedProminent)
                .disabled(isProcessing || (selectedLessons.isEmpty && customTopics.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty))
            }
        }
        .padding(20)
        .frame(width: 500)
    }
    
    private func runBatch() {
        isProcessing = true
        errorMessage = nil
        batchId = nil
        
        Task {
            do {
                var allTopics = Array(selectedLessons)
                let custom = customTopics.split(separator: "\n").map { String($0).trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
                allTopics.append(contentsOf: custom)
                
                var items: [BatchRequestItem] = []
                for (index, topic) in allTopics.enumerated() {
                    let prompt: String
                    switch generationType {
                    case "fiche":
                        prompt = GenerationEngine.shared.buildFichePrompt(
                            lessonTopic: topic,
                            classLevel: state.ficheClassLevel,
                            subject: state.ficheSubject,
                            lessonText: "", 
                            customTemplate: "",
                            durationMinutes: state.ficheDurationMinutes,
                            specialInstructions: state.ficheSpecialInstructions,
                            useTopRatedExamples: state.ficheUseTopRated
                        )
                    case "eval":
                        prompt = GenerationEngine.shared.buildEvaluationPrompt(
                            classLevel: state.evalClassLevel,
                            topics: [topic],
                            subject: state.evalSubject,
                            durationMinutes: state.evalDuration,
                            difficulty: state.evalDifficulty,
                            schoolName: state.evalSchoolName,
                            sessionLabel: state.evalSession,
                            totalPoints: state.evalTotalPoints,
                            extraInstructions: "",
                            extractedContent: ""
                        )
                    case "quiz":
                        prompt = GenerationEngine.shared.buildQuizPrompt(
                            classLevel: state.quizClassLevel,
                            topic: topic,
                            subject: state.quizSubject,
                            durationMinutes: state.quizDuration,
                            numQuestions: state.quizNumQuestions,
                            difficulty: state.quizDifficulty,
                            schoolName: state.quizSchoolName,
                            session: state.quizSession,
                            totalPoints: state.quizTotalPoints
                        )
                    default:
                        prompt = ""
                    }
                    
                    let model = state.chatModel.isEmpty ? "gemini-2.5-pro" : state.chatModel
                    let body = ChatCompletionBody(
                        model: model,
                        messages: [ChatMessagePayload(role: "user", content: prompt)],
                        temperature: 0.5,
                        max_tokens: 8000
                    )
                    
                    items.append(BatchRequestItem(
                        custom_id: "req-\(index)-\(generationType)",
                        method: "POST",
                        url: "/v1/chat/completions",
                        body: body
                    ))
                }
                
                let resultId = try await BatchService.shared.runBatch(
                    items: items,
                    baseURL: state.proxyBaseURL,
                    apiKey: state.proxyApiKey
                )
                
                batchId = resultId
                state.appendLog("✅ Batch \(generationType) soumis avec succès : \(resultId)")
                
                try await Task.sleep(nanoseconds: 1_500_000_000)
                dismiss()
                
            } catch {
                errorMessage = error.localizedDescription
                state.appendLog("❌ Erreur Batch : \(error.localizedDescription)")
            }
            isProcessing = false
        }
    }
}
