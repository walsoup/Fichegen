import SwiftUI

struct EvaluationFormView: View {
    @EnvironmentObject var state: AppState
    @State private var selectedLessons = Set<String>()

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
                        Picker("Classe", selection: $state.evalClassLevel) {
                            ForEach(state.classLevels, id: \.self) { Text($0) }
                        }
                        .pickerStyle(.menu)
                        
                        TextField("Matière", text: $state.evalSubject)
                            .textFieldStyle(.roundedBorder)
                    }
                }

                // ── Lessons Topics ───────────────────────────────────────
                GroupBox("Sujets de leçons") {
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
                GroupBox("Paramètres") {
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

                // ── Action ───────────────────────────────────────────────
                GenerateButton(label: "Générer l'Évaluation") {
                    state.generateEvaluation()
                }
            }
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .onAppear {
            state.loadAvailableLessons(classLevel: state.evalClassLevel)
        }
        .onChange(of: state.evalClassLevel) { _, newValue in
            state.loadAvailableLessons(classLevel: newValue)
        }
    }
}
