import SwiftUI

struct QuizFormView: View {
    @EnvironmentObject var state: AppState

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                // ── API Key Warning Banner ──────────────────────────────
                if state.geminiApiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    HStack(alignment: .top, spacing: 12) {
                        Image(systemName: "exclamationmark.shield.fill")
                            .font(.title2)
                            .foregroundColor(.orange)
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Clé API Google AI Studio manquante")
                                .font(.headline)
                                .foregroundColor(.primary)
                            Text("Veuillez configurer votre clé dans les Préférences (Cmd + ,) pour pouvoir générer des quiz.")
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
                        Picker("Classe", selection: $state.quizClassLevel) {
                            ForEach(state.classLevels, id: \.self) { Text($0) }
                        }
                        .pickerStyle(.menu)
                        
                        TextField("Matière", text: $state.quizSubject)
                            .textFieldStyle(.roundedBorder)
                    }
                }

                // ── Topic / Lesson ───────────────────────────────────────
                GroupBox(label: Label("Sujet & Leçon", systemImage: "book")) {
                    VStack(alignment: .leading, spacing: 10) {
                        if !state.availableLessons.isEmpty {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("📚 Sélectionner une leçon suggérée :")
                                    .font(.subheadline)
                                    .fontWeight(.medium)
                                    .foregroundStyle(.secondary)
                                Picker("", selection: $state.quizTopic) {
                                    Text("Saisir un sujet personnalisé...").tag("")
                                    ForEach(state.availableLessons, id: \.self) { lesson in
                                        Text(lesson).tag(lesson)
                                    }
                                }
                                .pickerStyle(.menu)
                                .labelsHidden()
                                .frame(maxWidth: .infinity)
                            }
                        }
                        
                        Text("Titre du quiz / leçon :")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        
                        TextField("Saisir le sujet du quiz", text: $state.quizTopic)
                            .textFieldStyle(.roundedBorder)
                        
                        if state.availableLessons.isEmpty {
                            Text("Aucune leçon en cache. Générez une Fiche d'abord pour ce niveau pour analyser le guide pédagogique.")
                                .font(.caption)
                                .foregroundStyle(.orange)
                                .padding(.top, 2)
                        }
                    }
                    .padding(.vertical, 2)
                }

                // ── Parameters ───────────────────────────────────────────
                GroupBox(label: Label("Paramètres de génération", systemImage: "slider.horizontal.3")) {
                    Form {
                        Stepper("Questions: \(state.quizNumQuestions)",
                                value: $state.quizNumQuestions,
                                in: 3...30, step: 1)
                        
                        Stepper("Durée: \(state.quizDuration) min",
                                value: $state.quizDuration,
                                in: 5...60, step: 5)
                        
                        Picker("Difficulté", selection: $state.quizDifficulty) {
                            Text("Facile").tag("easy")
                            Text("Moyen").tag("medium")
                            Text("Difficile").tag("hard")
                        }
                        .pickerStyle(.menu)
                    }
                }

                // ── Advanced Routing ─────────────────────────────────────
                if state.expShowAdvancedRoutingInForms {
                    GroupBox(label: Label("Routage Avancé (Expérimental)", systemImage: "network")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Picker("Fournisseur", selection: Binding(
                                get: { state.routingQuizProvider },
                                set: { state.updateSetting(key: "routing_quiz_provider", value: $0) }
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
                                    get: { state.routingQuizModel },
                                    set: { state.updateSetting(key: "routing_quiz_model", value: $0) }
                                ))
                                .textFieldStyle(.roundedBorder)
                                
                                Menu {
                                    Button("gemini-3.5-flash") {
                                        state.updateSetting(key: "routing_quiz_model", value: "gemini-3.5-flash")
                                    }
                                    Button("gemini-2.5-pro") {
                                        state.updateSetting(key: "routing_quiz_model", value: "gemini-2.5-pro")
                                    }
                                    Button("gemini-2.5-flash") {
                                        state.updateSetting(key: "routing_quiz_model", value: "gemini-2.5-flash")
                                    }
                                    Button("gemma-4-31b-it") {
                                        state.updateSetting(key: "routing_quiz_model", value: "gemma-4-31b-it")
                                    }
                                    Button("gemma-4-27b-e4b-it") {
                                        state.updateSetting(key: "routing_quiz_model", value: "gemma-4-27b-e4b-it")
                                    }
                                    Divider()
                                    Button("gpt-4o") {
                                        state.updateSetting(key: "routing_quiz_model", value: "gpt-4o")
                                    }
                                    Button("gpt-4o-mini") {
                                        state.updateSetting(key: "routing_quiz_model", value: "gpt-4o-mini")
                                    }
                                    Button("claude-3-5-sonnet") {
                                        state.updateSetting(key: "routing_quiz_model", value: "claude-3-5-sonnet")
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
                GenerateButton(
                    label: "Générer le Quiz",
                    isEnabled: !state.quizSubject.trimmingCharacters(in: .whitespaces).isEmpty &&
                               !state.quizTopic.trimmingCharacters(in: .whitespaces).isEmpty &&
                               state.isConfigured
                ) {
                    state.generateQuiz()
                }
            }
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .onChange(of: state.quizClassLevel) { _, newValue in
            state.loadAvailableLessons(classLevel: newValue)
        }
    }
}
