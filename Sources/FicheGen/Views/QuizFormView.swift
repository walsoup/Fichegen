import SwiftUI

struct QuizFormView: View {
    @EnvironmentObject var state: AppState

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                // ── API Key Warning Banner ──────────────────────────────
                if state.geminiApiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    HStack(spacing: 8) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundColor(.orange)
                        Text("⚠️ Gemini API Key not configured. Please open Settings (Cmd + ,) to configure it.")
                            .font(.subheadline)
                            .foregroundColor(.primary)
                        Spacer()
                    }
                    .padding(12)
                    .background(Color.orange.opacity(0.1))
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(Color.orange, lineWidth: 1)
                    )
                }

                // ── Class & Subject ──────────────────────────────────────
                GroupBox("Classe & Matière") {
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
                GroupBox("Sujet") {
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
                GroupBox("Paramètres") {
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

                // ── Action ───────────────────────────────────────────────
                GenerateButton(label: "Générer le Quiz") {
                    state.generateQuiz()
                }
            }
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .onAppear {
            state.loadAvailableLessons(classLevel: state.quizClassLevel)
        }
        .onChange(of: state.quizClassLevel) { _, newValue in
            state.loadAvailableLessons(classLevel: newValue)
        }
    }
}
