import SwiftUI

struct PreferencesView: View {
    @EnvironmentObject var state: AppState
    @State private var selectedTab = "general"
    
    var body: some View {
        TabView(selection: $selectedTab) {
            GeneralPrefsTab()
                .tabItem {
                    Label("Général", systemImage: "gearshape")
                }
                .tag("general")
            
            AIModelsPrefsTab()
                .tabItem {
                    Label("IA & Modèles", systemImage: "cpu")
                }
                .tag("ai")
            
            FoldersPrefsTab()
                .tabItem {
                    Label("Dossiers", systemImage: "folder")
                }
                .tag("folders")
            
            AdvancedPrefsTab()
                .tabItem {
                    Label("Avancé & Expérimental", systemImage: "slider.horizontal.3")
                }
                .tag("advanced")
            
            AppearancePrefsTab()
                .tabItem {
                    Label("Apparence", systemImage: "paintpalette")
                }
                .tag("appearance")
                
            TemplatesPrefsTab()
                .tabItem {
                    Label("Modèles", systemImage: "doc.text.magnifyingglass")
                }
                .tag("templates")
        }
        .padding(20)
        .frame(width: 700, height: 600)
        .navigationTitle("Préférences")
        .presentationBackground(.ultraThinMaterial)
        .animation(.spring(), value: selectedTab)
    }
}

struct GeneralPrefsTab: View {
    @EnvironmentObject var state: AppState
    
    let pdfStyles = ["Normal", "Professional", "Coral", "Aesthetic", "Minimal Pro", "Classic Serif"]
    
    var body: some View {
        Form {
            Section("Paramètres par défaut") {
                Stepper("Durée par défaut : \(state.defaultDuration) min", value: Binding(
                    get: { state.defaultDuration },
                    set: { state.updateSetting(key: "default_duration", value: $0) }
                ), in: 15...120, step: 5)
                
                HStack {
                    Text("Matière par défaut :")
                    TextField("Ex: Mathématiques, Sciences", text: Binding(
                        get: { state.defaultSubject },
                        set: { state.updateSetting(key: "default_subject", value: $0) }
                    ))
                    .textFieldStyle(.roundedBorder)
                }
                
                Picker("Style PDF par défaut", selection: Binding(
                    get: { state.defaultPdfStyle },
                    set: { state.updateSetting(key: "default_pdf_style", value: $0) }
                )) {
                    ForEach(pdfStyles, id: \.self) { style in
                        Text(style).tag(style)
                    }
                }
                .pickerStyle(.menu)
            }
            
            Section("Comportement") {
                Toggle("Utiliser les meilleures fiches comme exemples de style", isOn: Binding(
                    get: { state.useTopExamples },
                    set: { state.updateSetting(key: "use_top_examples", value: $0) }
                ))
                
                Toggle("Aperçu du texte source avant génération", isOn: Binding(
                    get: { state.previewSource },
                    set: { state.updateSetting(key: "preview_source", value: $0) }
                ))
                
                Toggle("Enregistrer les journaux de génération dans un fichier", isOn: Binding(
                    get: { state.saveLogs },
                    set: { state.updateSetting(key: "save_logs", value: $0) }
                ))
                
                Toggle("Vérifier automatiquement les mises à jour au démarrage", isOn: Binding(
                    get: { state.autoUpdateChecks },
                    set: { state.updateSetting(key: "updates_auto_check", value: $0) }
                ))
            }
        }
        .formStyle(.grouped)
    }
}

struct AIModelsPrefsTab: View {
    @EnvironmentObject var state: AppState
    @State private var showGeminiKey = false
    @State private var showProxyKey = false
    
    let geminiModels = [
        "gemini-2.5-pro",
        "gemini-2.5-flash",
        "gemini-1.5-pro",
        "gemini-1.5-flash",
        "gemini-flash-latest"
    ]
    
    var body: some View {
        ScrollView {
            Form {
                Section("Configuration Google AI Studio (Principale)") {
                    HStack {
                        if showGeminiKey {
                            TextField("Clé API Gemini", text: Binding(
                                get: { state.geminiApiKey },
                                set: { state.updateSetting(key: "gemini_api_key", value: $0) }
                            ))
                            .textFieldStyle(.roundedBorder)
                        } else {
                            SecureField("Clé API Gemini", text: Binding(
                                get: { state.geminiApiKey },
                                set: { state.updateSetting(key: "gemini_api_key", value: $0) }
                            ))
                            .textFieldStyle(.roundedBorder)
                        }
                        Button(showGeminiKey ? "Masquer" : "Afficher") {
                            showGeminiKey.toggle()
                        }
                    }
                }
                
                Section("Route Alternative : Google Cloud Vertex AI") {
                    Toggle("Activer Google Cloud Vertex AI", isOn: Binding(
                        get: { state.apiRoute == "vertex" },
                        set: { state.updateSetting(key: "api_route", value: $0 ? "vertex" : "aistudio") }
                    ))
                    
                    if state.apiRoute == "vertex" {
                        TextField("ID de projet Google Cloud", text: Binding(
                            get: { state.vertexProject },
                            set: { state.updateSetting(key: "vertex_project", value: $0) }
                        ))
                        .textFieldStyle(.roundedBorder)
                        
                        TextField("Emplacement Vertex AI", text: Binding(
                            get: { state.vertexLocation },
                            set: { state.updateSetting(key: "vertex_location", value: $0) }
                        ))
                        .textFieldStyle(.roundedBorder)
                    }
                }
                
                Section("Route Alternative : Proxy OpenAI / API Alternative") {
                    Toggle("Activer le Proxy OpenAI (Ollama, OpenRouter, etc.)", isOn: Binding(
                        get: { state.proxyEnabled },
                        set: { state.updateSetting(key: "proxy_enabled", value: $0) }
                    ))
                    
                    if state.proxyEnabled {
                        TextField("URL de base du Proxy", text: Binding(
                            get: { state.proxyBaseURL },
                            set: { state.updateSetting(key: "proxy_base_url", value: $0) }
                        ))
                        .textFieldStyle(.roundedBorder)
                        
                        HStack {
                            if showProxyKey {
                                TextField("Clé API Proxy", text: Binding(
                                    get: { state.proxyApiKey },
                                    set: { state.updateSetting(key: "proxy_api_key", value: $0) }
                                ))
                                .textFieldStyle(.roundedBorder)
                            } else {
                                SecureField("Clé API Proxy", text: Binding(
                                    get: { state.proxyApiKey },
                                    set: { state.updateSetting(key: "proxy_api_key", value: $0) }
                                ))
                                .textFieldStyle(.roundedBorder)
                            }
                            Button(showProxyKey ? "Masquer" : "Afficher") {
                                showProxyKey.toggle()
                            }
                        }
                    }
                }
                
                Section("Modèles par Défaut") {
                    Picker("Modèle Gemini principal", selection: Binding(
                        get: { state.geminiModel },
                        set: { state.updateSetting(key: "gemini_model", value: $0) }
                    )) {
                        ForEach(geminiModels, id: \.self) { model in
                            Text(model).tag(model)
                        }
                    }
                    .pickerStyle(.menu)
                    
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Text("Créativité (Température)")
                            Spacer()
                            Text(String(format: "%.1f", state.temperatureSetting))
                                .foregroundStyle(.secondary)
                                .monospacedDigit()
                        }
                        Slider(value: Binding(
                            get: { state.temperatureSetting },
                            set: { state.updateSetting(key: "temperature", value: $0) }
                        ), in: 0...1, step: 0.1)
                    }
                    .padding(.vertical, 4)
                }
            }
            .formStyle(.grouped)
        }
    }
}

struct FoldersPrefsTab: View {
    @EnvironmentObject var state: AppState
    
    var body: some View {
        Form {
            Section("Chemins des dossiers") {
                VStack(alignment: .leading, spacing: 12) {
                    HStack {
                        Text("Guides d'entrée:")
                            .frame(width: 130, alignment: .trailing)
                        TextField("Chemin du dossier des guides", text: Binding(
                            get: { state.guidesDir },
                            set: { state.updateSetting(key: "guides_dir", value: $0) }
                        ))
                        .textFieldStyle(.roundedBorder)
                        Button("Parcourir…") {
                            browseDirectory(current: state.guidesDir) { path in
                                state.updateSetting(key: "guides_dir", value: path)
                            }
                        }
                    }
                    
                    HStack {
                        Text("Manuels des élèves:")
                            .frame(width: 130, alignment: .trailing)
                        TextField("Chemin du dossier des manuels", text: Binding(
                            get: { state.textbookDir },
                            set: { state.updateSetting(key: "textbook_dir", value: $0) }
                        ))
                        .textFieldStyle(.roundedBorder)
                        Button("Parcourir…") {
                            browseDirectory(current: state.textbookDir) { path in
                                state.updateSetting(key: "textbook_dir", value: path)
                            }
                        }
                    }
                    
                    HStack {
                        Text("Dossier de sortie:")
                            .frame(width: 130, alignment: .trailing)
                        TextField("Chemin du dossier de sortie", text: Binding(
                            get: { state.outputDir },
                            set: { state.updateSetting(key: "output_dir", value: $0) }
                        ))
                        .textFieldStyle(.roundedBorder)
                        Button("Parcourir…") {
                            browseDirectory(current: state.outputDir) { path in
                                state.updateSetting(key: "output_dir", value: path)
                            }
                        }
                    }
                }
            }
        }
        .formStyle(.grouped)
    }
    
    private func browseDirectory(current: String, completion: @escaping (String) -> Void) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        if !current.isEmpty {
            panel.directoryURL = URL(fileURLWithPath: current)
        }
        panel.begin { response in
            if response == .OK, let url = panel.url {
                completion(url.path)
            }
        }
    }
}

struct AdvancedPrefsTab: View {
    @EnvironmentObject var state: AppState
    
    var body: some View {
        ScrollView {
            Form {
                Section("Instructions Spéciales") {
                    TextEditor(text: Binding(
                        get: { state.specialInstructions },
                        set: { state.updateSetting(key: "special_instructions", value: $0) }
                    ))
                    .frame(height: 70)
                    .scrollContentBackground(.hidden)
                    .overlay(
                        RoundedRectangle(cornerRadius: 6)
                            .stroke(Color(nsColor: .separatorColor), lineWidth: 0.5)
                    )
                }
                
                Section("Routage Par Fonction") {
                    VStack(alignment: .leading, spacing: 8) {
                        routingRow(label: "Génération Fiche", providerKey: "routing_fiche_provider", modelKey: "routing_fiche_model", provider: state.routingFicheProvider, model: state.routingFicheModel)
                        Divider()
                        routingRow(label: "Génération Éval", providerKey: "routing_eval_provider", modelKey: "routing_eval_model", provider: state.routingEvalProvider, model: state.routingEvalModel)
                        Divider()
                        routingRow(label: "Génération Quiz", providerKey: "routing_quiz_provider", modelKey: "routing_quiz_model", provider: state.routingQuizProvider, model: state.routingQuizModel)
                        Divider()
                        routingRow(label: "Parseur ToC", providerKey: "routing_toc_provider", modelKey: "routing_toc_model", provider: state.routingTocProvider, model: state.routingTocModel)
                        Divider()
                        routingRow(label: "Décalage Pages", providerKey: "routing_offset_provider", modelKey: "routing_offset_model", provider: state.routingOffsetProvider, model: state.routingOffsetModel)
                        Divider()
                        routingRow(label: "Correction Syntaxe", providerKey: "routing_syntax_provider", modelKey: "routing_syntax_model", provider: state.routingSyntaxProvider, model: state.routingSyntaxModel)
                    }
                    .padding(.vertical, 4)
                }
                
                Section("Paramètres Expérimentaux") {
                    Toggle("Aperçu de la réponse en streaming", isOn: Binding(
                        get: { state.expStreamingResponse },
                        set: { state.updateSetting(key: "exp_streaming_response", value: $0) }
                    ))
                    
                    Stepper("Nombre maximal de tentatives : \(state.expMaxRetries)", value: Binding(
                        get: { state.expMaxRetries },
                        set: { state.updateSetting(key: "exp_max_retries", value: $0) }
                    ), in: 1...5)
                    
                    Stepper("Délai d'expiration de la requête : \(state.expRequestTimeout)s", value: Binding(
                        get: { state.expRequestTimeout },
                        set: { state.updateSetting(key: "exp_request_timeout", value: $0) }
                    ), in: 30...300, step: 10)
                    
                    Toggle("Activer le cache de génération ToC", isOn: Binding(
                        get: { state.expEnableCache },
                        set: { state.updateSetting(key: "exp_enable_cache", value: $0) }
                    ))
                    
                    Toggle("Extraction de la table des matières en parallèle", isOn: Binding(
                        get: { state.expParallelToc },
                        set: { state.updateSetting(key: "exp_parallel_toc", value: $0) }
                    ))
                }
                
                Section("Paramètres Hautement Expérimentaux (⚠️ Instables)") {
                    Toggle("Génération multi-passes (Boucle d'auto-critique)", isOn: Binding(
                        get: { state.expMultiPassGen },
                        set: { state.updateSetting(key: "exp_multi_pass_gen", value: $0) }
                    ))
                    
                    Stepper("Itérations multi-passes : \(state.expMultiPassIterations)", value: Binding(
                        get: { state.expMultiPassIterations },
                        set: { state.updateSetting(key: "exp_multi_pass_iterations", value: $0) }
                    ), in: 2...5)
                    
                    
                    Toggle("Transfert de style à partir de fiches existantes", isOn: Binding(
                        get: { state.expStyleTransfer },
                        set: { state.updateSetting(key: "exp_style_transfer", value: $0) }
                    ))
                    
                    Toggle("Évaluation auto de la difficulté par rapport au programme", isOn: Binding(
                        get: { state.expAutoGradeDifficulty },
                        set: { state.updateSetting(key: "exp_auto_grade_difficulty", value: $0) }
                    ))
                    
                    Toggle("Activer le prompt de type Chain-of-Thought (Raisonnement)", isOn: Binding(
                        get: { state.expChainOfThought },
                        set: { state.updateSetting(key: "exp_chain_of_thought", value: $0) }
                    ))
                    
                    Toggle("Forcer la validation stricte du schéma JSON", isOn: Binding(
                        get: { state.expJsonValidation },
                        set: { state.updateSetting(key: "exp_json_validation", value: $0) }
                    ))
                    
                    Toggle("Activer les conseils de décodage spéculatif", isOn: Binding(
                        get: { state.expSpeculativeDecoding },
                        set: { state.updateSetting(key: "exp_speculative_decoding", value: $0) }
                    ))
                    
                    Toggle("Activer le mode boucle agentique autonome", isOn: Binding(
                        get: { state.expAgenticLoop },
                        set: { state.updateSetting(key: "exp_agentic_loop", value: $0) }
                    ))
                }
                
                Section("Configuration Avancée des Prompts") {
                    Toggle("Activer la modification des prompts système", isOn: Binding(
                        get: { state.advancedEnablePromptEditing },
                        set: { state.updateSetting(key: "advanced_enable_prompt_editing", value: $0) }
                    ))
                    
                    if state.advancedEnablePromptEditing {
                        VStack(alignment: .leading, spacing: 8) {
                            Text("⚠️ Les modifications peuvent affecter la qualité des générations.")
                                .foregroundStyle(.orange)
                                .font(.caption)
                            
                            Text("Prompt d'analyse ToC:")
                                .font(.caption)
                            TextEditor(text: Binding(
                                get: { state.advancedTocPrompt },
                                set: { state.updateSetting(key: "advanced_toc_prompt", value: $0) }
                            ))
                            .frame(height: 100)
                            .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.gray.opacity(0.3), lineWidth: 1))
                            
                            Text("Prompt de recherche de pages:")
                                .font(.caption)
                            TextEditor(text: Binding(
                                get: { state.advancedPageFindingPrompt },
                                set: { state.updateSetting(key: "advanced_page_finding_prompt", value: $0) }
                            ))
                            .frame(height: 100)
                            .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.gray.opacity(0.3), lineWidth: 1))
                            
                            Text("Prompt principal de génération de Fiche:")
                                .font(.caption)
                            TextEditor(text: Binding(
                                get: { state.advancedFichePrompt },
                                set: { state.updateSetting(key: "advanced_fiche_prompt", value: $0) }
                            ))
                            .frame(height: 120)
                            .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.gray.opacity(0.3), lineWidth: 1))
                        }
                    }
                }
            }
            .formStyle(.grouped)
        }
    }
    
    @ViewBuilder
    private func routingRow(label: String, providerKey: String, modelKey: String, provider: String, model: String) -> some View {
        HStack {
            Text(label)
                .frame(width: 140, alignment: .leading)
            
            Picker("", selection: Binding(
                get: { provider },
                set: { state.updateSetting(key: providerKey, value: $0) }
            )) {
                Text("Par défaut").tag("default")
                Text("Gemini Studio").tag("gemini")
                Text("Vertex AI").tag("vertex")
                Text("Proxy API").tag("proxy")
            }
            .pickerStyle(.menu)
            .frame(width: 130)
            
            TextField("Modèle personnalisé (facultatif)", text: Binding(
                get: { model },
                set: { state.updateSetting(key: modelKey, value: $0) }
            ))
            .textFieldStyle(.roundedBorder)
        }
    }
}

struct AppearancePrefsTab: View {
    @EnvironmentObject var state: AppState
    
    var body: some View {
        Form {
            Section("Langue") {
                Picker("Langue de l'interface", selection: Binding(
                    get: { state.uiLanguage },
                    set: { state.updateSetting(key: "ui_language", value: $0) }
                )) {
                    Text("Français").tag("fr")
                    Text("English").tag("en")
                }
                .pickerStyle(.menu)
            }
            
            Section("Interface & PDF") {
                Toggle("Espacement compact de la barre latérale", isOn: Binding(
                    get: { state.uiCompactSidebar },
                    set: { state.updateSetting(key: "ui_compact_sidebar", value: $0) }
                ))
                
                Toggle("Afficher les contrôles d'évaluation avancés", isOn: Binding(
                    get: { state.uiShowEvalAdvancedControls },
                    set: { state.updateSetting(key: "ui_show_eval_advanced_controls", value: $0) }
                ))
                
                Toggle("Afficher la bannière de métadonnées PDF", isOn: Binding(
                    get: { state.pdfShowMeta },
                    set: { state.updateSetting(key: "pdf_show_meta", value: $0) }
                ))
            }
            
            StyleBuilderView()
        }
        .formStyle(.grouped)
    }
}
