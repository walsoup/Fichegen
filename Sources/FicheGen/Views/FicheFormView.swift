import SwiftUI

struct FicheFormView: View {
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
                            Text("Veuillez configurer votre clé dans les Préférences (Cmd + ,) pour pouvoir générer des fiches.")
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
                GenerateButton(
                    label: "Générer la Fiche",
                    isEnabled: !state.ficheSubject.trimmingCharacters(in: .whitespaces).isEmpty &&
                               !state.ficheLessonTopic.trimmingCharacters(in: .whitespaces).isEmpty &&
                               state.isConfigured
                ) {
                    state.generateFiche()
                }
            }
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .onChange(of: state.ficheClassLevel) { _, newValue in
            state.loadAvailableLessons(classLevel: newValue)
        }
    }
}
