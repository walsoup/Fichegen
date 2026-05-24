import SwiftUI

struct FicheFormView: View {
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
                        Picker("Classe", selection: $state.ficheClassLevel) {
                            ForEach(state.classLevels, id: \.self) { Text($0) }
                        }
                        .pickerStyle(.menu)
                        
                        TextField("Matière", text: $state.ficheSubject)
                            .textFieldStyle(.roundedBorder)
                    }
                }

                // ── Lesson ───────────────────────────────────────────────
                GroupBox("Leçon") {
                    VStack(alignment: .leading, spacing: 10) {
                        if !state.availableLessons.isEmpty {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("📚 Sélectionner une leçon suggérée :")
                                    .font(.subheadline)
                                    .fontWeight(.medium)
                                    .foregroundStyle(.secondary)
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
                            Text("Aucune leçon en cache. Générez une Fiche d'abord pour ce niveau pour analyser le guide pédagogique.")
                                .font(.caption)
                                .foregroundStyle(.orange)
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

                // ── Parameters ───────────────────────────────────────────
                GroupBox("Paramètres") {
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
                GroupBox("Instructions spéciales (optionnel)") {
                    TextEditor(text: $state.ficheSpecialInstructions)
                        .font(.system(.body, design: .default))
                        .frame(minHeight: 70, maxHeight: 120)
                        .scrollContentBackground(.hidden)
                }

                // ── Action ───────────────────────────────────────────────
                GenerateButton(label: "Générer la Fiche") {
                    state.generateFiche()
                }
            }
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .onAppear {
            state.loadAvailableLessons(classLevel: state.ficheClassLevel)
        }
        .onChange(of: state.ficheClassLevel) { _, newValue in
            state.loadAvailableLessons(classLevel: newValue)
        }
    }
}
