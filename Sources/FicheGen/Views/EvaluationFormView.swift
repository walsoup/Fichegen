import SwiftUI

struct EvaluationFormView: View {
    @EnvironmentObject var state: AppState
    @State private var selectedLessons = Set<String>()

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
                            Text("Veuillez configurer votre clé dans les Préférences (Cmd + ,) pour pouvoir générer des évaluations.")
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
                        Picker("Classe", selection: $state.evalClassLevel) {
                            ForEach(state.classLevels, id: \.self) { Text($0) }
                        }
                        .pickerStyle(.menu)
                        
                        TextField("Matière", text: $state.evalSubject)
                            .textFieldStyle(.roundedBorder)
                    }
                }

                // ── Lessons Topics ───────────────────────────────────────
                GroupBox(label: Label("Sujets de leçons", systemImage: "checklist")) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Un sujet par ligne")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        
                        TextEditor(text: $state.evalTopics)
                            .font(.body)
                            .frame(minHeight: 80, maxHeight: 120)
                            .scrollContentBackground(.hidden)
                            .overlay(
                                RoundedRectangle(cornerRadius: 6)
                                    .stroke(Color(nsColor: .separatorColor), lineWidth: 0.5)
                            )
                        
                        // Lesson Suggestions List
                        if state.availableLessons.isEmpty {
                            Text("Aucune leçon en cache. Générez une Fiche d'abord pour ce niveau pour analyser le guide pédagogique.")
                                .font(.caption)
                                .foregroundStyle(.orange)
                                .padding(.top, 2)
                        } else {
                            VStack(alignment: .leading, spacing: 6) {
                                Text("Sélectionner des leçons à ajouter :")
                                    .font(.caption)
                                    .fontWeight(.semibold)
                                    .foregroundStyle(.secondary)
                                
                                ScrollView(.vertical) {
                                    VStack(alignment: .leading, spacing: 4) {
                                        ForEach(state.availableLessons, id: \.self) { lesson in
                                            Toggle(lesson, isOn: Binding(
                                                get: { selectedLessons.contains(lesson) },
                                                set: { isSelected in
                                                    if isSelected {
                                                        selectedLessons.insert(lesson)
                                                    } else {
                                                        selectedLessons.remove(lesson)
                                                    }
                                                }
                                            ))
                                            .toggleStyle(.checkbox)
                                        }
                                    }
                                    .padding(6)
                                }
                                .frame(height: 100)
                                .background(Color(nsColor: .controlBackgroundColor))
                                .cornerRadius(6)
                                .overlay(
                                    RoundedRectangle(cornerRadius: 6)
                                        .stroke(Color(nsColor: .separatorColor), lineWidth: 0.5)
                                )
                                
                                Button("Ajouter les leçons sélectionnées") {
                                    let currentLines = state.evalTopics
                                        .split(separator: "\n")
                                        .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                                        .filter { !$0.isEmpty }
                                    
                                    var newLines = currentLines
                                    for lesson in selectedLessons {
                                        if !newLines.contains(lesson) {
                                            newLines.append(lesson)
                                        }
                                    }
                                    
                                    state.evalTopics = newLines.joined(separator: "\n")
                                    selectedLessons.removeAll()
                                }
                                .buttonStyle(.borderedProminent)
                                .controlSize(.small)
                                .disabled(selectedLessons.isEmpty)
                            }
                            .padding(.top, 4)
                        }
                    }
                    .padding(.vertical, 2)
                }

                // ── Parameters ───────────────────────────────────────────
                GroupBox(label: Label("Paramètres de génération", systemImage: "slider.horizontal.3")) {
                    Form {
                        Stepper("Durée: \(state.evalDuration) min",
                                value: $state.evalDuration,
                                in: 20...120, step: 5)
                        
                        Picker("Difficulté", selection: $state.evalDifficulty) {
                            Text("Facile").tag("easy")
                            Text("Moyen").tag("medium")
                            Text("Difficile").tag("hard")
                        }
                        .pickerStyle(.menu)
                        
                        VStack(alignment: .leading, spacing: 2) {
                            HStack {
                                Text("Créativité")
                                Spacer()
                                Text(String(format: "%.1f", state.evalTemperature))
                                    .foregroundStyle(.secondary)
                                    .monospacedDigit()
                            }
                            Slider(value: $state.evalTemperature, in: 0...1, step: 0.1)
                        }
                    }
                }

                // ── Advanced Routing ─────────────────────────────────────
                if state.expShowAdvancedRoutingInForms {
                    GroupBox(label: Label("Routage Avancé (Expérimental)", systemImage: "network")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Picker("Fournisseur", selection: Binding(
                                get: { state.routingEvalProvider },
                                set: { state.updateSetting(key: "routing_eval_provider", value: $0) }
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
                                    get: { state.routingEvalModel },
                                    set: { state.updateSetting(key: "routing_eval_model", value: $0) }
                                ))
                                .textFieldStyle(.roundedBorder)
                                
                                Menu {
                                    Button("gemini-27b-e4b-it") {
                                        state.updateSetting(key: "routing_eval_model", value: "gemini-27b-e4b-it")
                                    }
                                    Button("gemini-3.5-flash") {
                                        state.updateSetting(key: "routing_eval_model", value: "gemini-3.5-flash")
                                    }
                                    Button("gemini-2.5-pro") {
                                        state.updateSetting(key: "routing_eval_model", value: "gemini-2.5-pro")
                                    }
                                    Button("gemini-2.5-flash") {
                                        state.updateSetting(key: "routing_eval_model", value: "gemini-2.5-flash")
                                    }
                                    Button("gemma-27b-a4b-it") {
                                        state.updateSetting(key: "routing_eval_model", value: "gemma-27b-a4b-it")
                                    }
                                    Button("gemma-4-27b-e4b-it") {
                                        state.updateSetting(key: "routing_eval_model", value: "gemma-4-27b-e4b-it")
                                    }
                                    Divider()
                                    Button("gpt-4o") {
                                        state.updateSetting(key: "routing_eval_model", value: "gpt-4o")
                                    }
                                    Button("gpt-4o-mini") {
                                        state.updateSetting(key: "routing_eval_model", value: "gpt-4o-mini")
                                    }
                                    Button("claude-3-5-sonnet") {
                                        state.updateSetting(key: "routing_eval_model", value: "claude-3-5-sonnet")
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
                    label: "Générer l'Évaluation",
                    isEnabled: !state.evalSubject.trimmingCharacters(in: .whitespaces).isEmpty &&
                               !state.evalTopics.trimmingCharacters(in: .whitespaces).isEmpty &&
                               state.isConfigured
                ) {
                    state.generateEvaluation()
                }
            }
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .onChange(of: state.evalClassLevel) { _, newValue in
            state.loadAvailableLessons(classLevel: newValue)
        }
    }
}
