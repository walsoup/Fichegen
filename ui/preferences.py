from PyQt6 import QtWidgets, QtCore, QtGui
from config import (
    PDF_TEMPLATES,
    DEFAULT_PRO_MODEL,
    DEFAULT_FLASH_MODEL,
    GEMINI_MODEL,
    GEMINI_TOC_MODEL,
    GEMINI_OFFSET_MODEL,
    GEMMA_SYNTAX_MODEL,
    DEFAULT_TOC_PROMPT,
    DEFAULT_PAGE_FINDING_PROMPT,
    DEFAULT_FICHE_PROMPT,
    DEFAULT_INPUT_DIR,
    DEFAULT_OUTPUT_DIR
)

class PreferencesDialog(QtWidgets.QDialog):
    """macOS-style preferences dialog"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Preferences")
        self.setModal(True)
        self.resize(600, 500)
        
        # Create tab widget for different preference categories
        self.tab_widget = QtWidgets.QTabWidget()
        
        # General tab
        general_tab = self._create_general_tab()
        self.tab_widget.addTab(general_tab, "General")
        
        # AI & Models tab
        ai_tab = self._create_ai_tab()
        self.tab_widget.addTab(ai_tab, "AI & Models")
        
        # Folders tab
        folders_tab = self._create_folders_tab()
        self.tab_widget.addTab(folders_tab, "Folders")

        # Advanced tab
        advanced_tab = self._create_advanced_tab()
        self.tab_widget.addTab(advanced_tab, "Advanced")

        # Appearance tab
        appearance_tab = self._create_appearance_tab()
        self.tab_widget.addTab(appearance_tab, "Appearance")

        # Layout
        layout = QtWidgets.QVBoxLayout(self)
        layout.addWidget(self.tab_widget)

        # Button box
        button_box = QtWidgets.QDialogButtonBox(
            QtWidgets.QDialogButtonBox.StandardButton.Ok |
            QtWidgets.QDialogButtonBox.StandardButton.Cancel
        )
        button_box.accepted.connect(self.accept)
        button_box.rejected.connect(self.reject)
        layout.addWidget(button_box)
        
    def _create_general_tab(self):
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QFormLayout(widget)
        layout.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)
        
        # Use top-rated examples
        self.use_top_examples_chk = QtWidgets.QCheckBox("Use top-rated fiches as style examples")
        self.use_top_examples_chk.setChecked(True)
        layout.addRow("Examples:", self.use_top_examples_chk)
        
        # Preview source text
        self.preview_source_chk = QtWidgets.QCheckBox("Preview source text before generation")
        self.preview_source_chk.setToolTip("Show extracted PDF text for confirmation before generating")
        layout.addRow("Preview:", self.preview_source_chk)
        
        # Save logs
        self.save_logs_chk = QtWidgets.QCheckBox("Save logs to file during generation")
        layout.addRow("Logging:", self.save_logs_chk)

        # Defaults
        layout.addRow("", QtWidgets.QLabel("<b>Defaults</b>"))
        self.default_duration_spin = QtWidgets.QSpinBox()
        self.default_duration_spin.setRange(15, 180)
        self.default_duration_spin.setSingleStep(5)
        self.default_duration_spin.setValue(45)
        layout.addRow("Default Duration:", self.default_duration_spin)

        self.default_pdf_style_combo = QtWidgets.QComboBox()
        self.default_pdf_style_combo.addItems(list(PDF_TEMPLATES.keys()))
        layout.addRow("Default PDF Style:", self.default_pdf_style_combo)

        self.default_subject_combo = QtWidgets.QComboBox()
        self.default_subject_combo.addItems([
            "", "Mathématiques", "Sciences", "Français", "Histoire", 
            "Géographie", "Éducation civique", "Arabe", "Anglais", "Islamique"
        ])
        layout.addRow("Default Subject:", self.default_subject_combo)
        
        return widget
        
    def _create_ai_tab(self):
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QFormLayout(widget)
        layout.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)
        
        # API Keys section
        keys_label = QtWidgets.QLabel("<b>API Key</b>")
        layout.addRow("", keys_label)
        
        # Help text for API keys
        help_text = QtWidgets.QLabel(
            'Get your free Gemini API key:<br>'
            '• <a href="https://ai.google.dev/">Google AI Studio</a> - Free Gemini access'
        )
        help_text.setOpenExternalLinks(True)
        help_text.setWordWrap(True)
        help_text.setStyleSheet("color: #666; font-size: 11px; margin-bottom: 8px;")
        layout.addRow("", help_text)
        
        # Gemini API Key
        self.gemini_key_edit = QtWidgets.QLineEdit()
        self.gemini_key_edit.setEchoMode(QtWidgets.QLineEdit.EchoMode.Password)
        self.gemini_key_edit.setPlaceholderText("Enter your Gemini API key...")
        show_gemini_btn = QtWidgets.QPushButton("👁")
        show_gemini_btn.setMaximumWidth(30)
        show_gemini_btn.setCheckable(True)
        show_gemini_btn.toggled.connect(lambda checked: self.gemini_key_edit.setEchoMode(
            QtWidgets.QLineEdit.EchoMode.Normal if checked else QtWidgets.QLineEdit.EchoMode.Password
        ))
        
        gemini_widget = QtWidgets.QWidget()
        gemini_layout = QtWidgets.QHBoxLayout(gemini_widget)
        gemini_layout.setContentsMargins(0, 0, 0, 0)
        gemini_layout.addWidget(self.gemini_key_edit, 1)
        gemini_layout.addWidget(show_gemini_btn)
        layout.addRow("Gemini Key:", gemini_widget)
        
        # Add some spacing
        layout.addRow("", QtWidgets.QLabel(""))
        
        # Temperature slider
        temp_label_header = QtWidgets.QLabel("<b>Generation Settings</b>")
        layout.addRow("", temp_label_header)
        
        temp_widget = QtWidgets.QWidget()
        temp_layout = QtWidgets.QHBoxLayout(temp_widget)
        temp_layout.setContentsMargins(0, 0, 0, 0)
        
        self.temp_slider = QtWidgets.QSlider(QtCore.Qt.Orientation.Horizontal)
        self.temp_slider.setRange(0, 100)
        self.temp_slider.setValue(50)
        self.temp_label = QtWidgets.QLabel("0.50")
        self.temp_slider.valueChanged.connect(lambda v: self.temp_label.setText(f"{v/100:.2f}"))
        
        temp_layout.addWidget(self.temp_slider, 1)
        temp_layout.addWidget(self.temp_label)
        layout.addRow("Temperature:", temp_widget)
        
        return widget
        
    def _create_folders_tab(self):
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QFormLayout(widget)
        layout.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)
        
        # Input folder
        input_widget = QtWidgets.QWidget()
        input_layout = QtWidgets.QHBoxLayout(input_widget)
        input_layout.setContentsMargins(0, 0, 0, 0)
        
        self.input_edit = QtWidgets.QLineEdit()
        self.input_btn = QtWidgets.QPushButton("Browse…")
        self.input_btn.clicked.connect(self._browse_input_folder)
        
        input_layout.addWidget(self.input_edit, 1)
        input_layout.addWidget(self.input_btn)
        layout.addRow("Input Guides:", input_widget)
        
        # Textbook folder
        textbook_widget = QtWidgets.QWidget()
        textbook_layout = QtWidgets.QHBoxLayout(textbook_widget)
        textbook_layout.setContentsMargins(0, 0, 0, 0)
        
        self.textbook_edit = QtWidgets.QLineEdit()
        self.textbook_edit.setPlaceholderText("Optional: folder with student textbook PDFs")
        self.textbook_btn = QtWidgets.QPushButton("Browse…")
        self.textbook_btn.clicked.connect(self._browse_textbook_folder)
        
        textbook_layout.addWidget(self.textbook_edit, 1)
        textbook_layout.addWidget(self.textbook_btn)
        layout.addRow("Student Books:", textbook_widget)
        
        # Output folder
        output_widget = QtWidgets.QWidget()
        output_layout = QtWidgets.QHBoxLayout(output_widget)
        output_layout.setContentsMargins(0, 0, 0, 0)
        
        self.output_edit = QtWidgets.QLineEdit()
        self.output_btn = QtWidgets.QPushButton("Browse…")
        self.output_btn.clicked.connect(self._browse_output_folder)
        
        output_layout.addWidget(self.output_edit, 1)
        output_layout.addWidget(self.output_btn)
        layout.addRow("Output Folder:", output_widget)
        
        return widget
        
    def _create_advanced_tab(self):
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        
        # Model Configuration Group
        model_group = QtWidgets.QGroupBox("AI Model Configuration")
        model_layout = QtWidgets.QFormLayout(model_group)
        
        # Pro/Flash model selection (main toggle models)
        model_layout.addRow(QtWidgets.QLabel("📌 Main Generation Models:"))
        
        self.pro_model_edit = QtWidgets.QLineEdit()
        self.pro_model_edit.setPlaceholderText(DEFAULT_PRO_MODEL)
        self.pro_model_edit.setToolTip("The 'Pro' model used when Pro is selected in the sidebar toggle")
        model_layout.addRow("Pro Model:", self.pro_model_edit)
        
        self.flash_model_edit = QtWidgets.QLineEdit()
        self.flash_model_edit.setPlaceholderText(DEFAULT_FLASH_MODEL)
        self.flash_model_edit.setToolTip("The 'Flash' model used when Flash is selected (also used as fallback)")
        model_layout.addRow("Flash Model:", self.flash_model_edit)
        
        # Fallback option
        self.enable_fallback_chk = QtWidgets.QCheckBox("Auto-fallback to Flash if Pro fails")
        self.enable_fallback_chk.setToolTip("If the Pro model fails or times out, automatically retry with Flash model")
        self.enable_fallback_chk.setChecked(True)
        model_layout.addRow("", self.enable_fallback_chk)
        
        model_layout.addRow(QtWidgets.QLabel(""))  # Spacer
        model_layout.addRow(QtWidgets.QLabel("🔧 Utility Models:"))
        
        # Gemini models (keep existing but updated labels)
        self.gemini_model_edit = QtWidgets.QLineEdit()
        self.gemini_model_edit.setPlaceholderText(DEFAULT_FLASH_MODEL)
        self.gemini_model_edit.setToolTip("Override for specific advanced use cases")
        model_layout.addRow("Advanced Override:", self.gemini_model_edit)
        
        self.gemini_toc_model_edit = QtWidgets.QLineEdit()
        self.gemini_toc_model_edit.setPlaceholderText(DEFAULT_FLASH_MODEL)
        model_layout.addRow("ToC Extraction Model:", self.gemini_toc_model_edit)
        
        self.gemini_offset_model_edit = QtWidgets.QLineEdit()
        self.gemini_offset_model_edit.setPlaceholderText("gemini-2.5-flash-lite")
        model_layout.addRow("Offset Detection Model:", self.gemini_offset_model_edit)
        
        self.gemma_syntax_model_edit = QtWidgets.QLineEdit()
        self.gemma_syntax_model_edit.setPlaceholderText("gemma-3-27b-it")
        model_layout.addRow("Syntax Correction Model:", self.gemma_syntax_model_edit)
        
        layout.addWidget(model_group)
        
        # Special Instructions
        layout.addWidget(QtWidgets.QLabel("Special Instructions:"))
        self.special_instructions_edit = QtWidgets.QTextEdit()
        self.special_instructions_edit.setPlaceholderText(
            "e.g., No group activities, focus on individual work, do not suggest videos..."
        )
        self.special_instructions_edit.setMaximumHeight(120)
        layout.addWidget(self.special_instructions_edit)
        
        # Prompt Editing Section
        prompt_group = QtWidgets.QGroupBox("Advanced Prompt Configuration")
        prompt_layout = QtWidgets.QVBoxLayout(prompt_group)
        
        # Toggle for showing prompt editing
        self.enable_prompt_editing_chk = QtWidgets.QCheckBox("Enable advanced prompt editing")
        self.enable_prompt_editing_chk.toggled.connect(self._toggle_prompt_editing)
        prompt_layout.addWidget(self.enable_prompt_editing_chk)
        
        # Prompt editing area (initially hidden)
        self.prompt_editing_widget = QtWidgets.QWidget()
        prompt_edit_layout = QtWidgets.QVBoxLayout(self.prompt_editing_widget)
        prompt_edit_layout.setContentsMargins(0, 0, 0, 0)
        
        # Warning label
        warning_label = QtWidgets.QLabel("⚠️ Warning: Modifying these prompts may affect fiche quality. Edit with caution.")
        warning_label.setStyleSheet("color: #FF6600; font-weight: bold;")
        prompt_edit_layout.addWidget(warning_label)
        
        # ToC parsing prompt
        prompt_edit_layout.addWidget(QtWidgets.QLabel("Table of Contents Parsing Prompt:"))
        self.toc_prompt_edit = QtWidgets.QTextEdit()
        self.toc_prompt_edit.setMaximumHeight(150)
        self.toc_prompt_edit.setPlaceholderText("Prompt for parsing PDF table of contents...")
        prompt_edit_layout.addWidget(self.toc_prompt_edit)
        
        # Page finding prompt
        prompt_edit_layout.addWidget(QtWidgets.QLabel("Page Finding Prompt:"))
        self.page_finding_prompt_edit = QtWidgets.QTextEdit()
        self.page_finding_prompt_edit.setMaximumHeight(150)
        self.page_finding_prompt_edit.setPlaceholderText("Prompt for finding specific lesson pages...")
        prompt_edit_layout.addWidget(self.page_finding_prompt_edit)
        
        # Fiche generation prompt
        prompt_edit_layout.addWidget(QtWidgets.QLabel("Fiche Generation Prompt:"))
        self.fiche_prompt_edit = QtWidgets.QTextEdit()
        self.fiche_prompt_edit.setMaximumHeight(200)
        self.fiche_prompt_edit.setPlaceholderText("Main prompt for generating pedagogical fiches...")
        prompt_edit_layout.addWidget(self.fiche_prompt_edit)
        
        # Reset to defaults button
        reset_btn = QtWidgets.QPushButton("Reset Prompts to Defaults")
        reset_btn.clicked.connect(self._reset_prompts_to_defaults)
        prompt_edit_layout.addWidget(reset_btn)
        
        self.prompt_editing_widget.setVisible(False)  # Initially hidden
        prompt_layout.addWidget(self.prompt_editing_widget)
        
        layout.addWidget(prompt_group)
        
        layout.addStretch(1)
        return widget
    
    def _toggle_prompt_editing(self, enabled):
        """Show/hide the prompt editing interface"""
        self.prompt_editing_widget.setVisible(enabled)
        
    def _reset_prompts_to_defaults(self):
        """Reset all prompts to their default values"""
        if QtWidgets.QMessageBox.question(
            self, "Reset Prompts", 
            "Are you sure you want to reset all prompts to their default values? This will overwrite any custom changes.",
            QtWidgets.QMessageBox.StandardButton.Yes | QtWidgets.QMessageBox.StandardButton.No
        ) == QtWidgets.QMessageBox.StandardButton.Yes:
            self.toc_prompt_edit.setPlainText(DEFAULT_TOC_PROMPT)
            self.page_finding_prompt_edit.setPlainText(DEFAULT_PAGE_FINDING_PROMPT)
            self.fiche_prompt_edit.setPlainText(DEFAULT_FICHE_PROMPT)

    def _create_appearance_tab(self):
        widget = QtWidgets.QWidget()
        form = QtWidgets.QFormLayout(widget)
        form.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)

        # Language selector
        self.language_combo = QtWidgets.QComboBox()
        self.language_combo.addItem("English", "en")
        self.language_combo.addItem("Français", "fr")
        self.language_combo.setToolTip("Change the interface language (requires restart)")
        form.addRow("Language / Langue:", self.language_combo)
        
        # Add explanatory text
        lang_note = QtWidgets.QLabel("Note: Restart the app after changing language")
        lang_note.setStyleSheet("color: #666; font-size: 11px; font-style: italic;")
        form.addRow("", lang_note)

        # Compact sidebar spacing
        self.compact_sidebar_chk = QtWidgets.QCheckBox("Compact sidebar spacing")
        form.addRow("Sidebar:", self.compact_sidebar_chk)

        # Show PDF meta banner
        self.pdf_meta_banner_chk = QtWidgets.QCheckBox("Show PDF meta banner (title, classe, durée)")
        self.pdf_meta_banner_chk.setChecked(False)
        form.addRow("PDF:", self.pdf_meta_banner_chk)

        return widget
        
    def _on_provider_change(self, provider_text):
        """No-op - kept for backwards compatibility"""
        pass
        
    def _browse_input_folder(self):
        folder = QtWidgets.QFileDialog.getExistingDirectory(self, "Choose input folder")
        if folder:
            self.input_edit.setText(folder)
            
    def _browse_textbook_folder(self):
        folder = QtWidgets.QFileDialog.getExistingDirectory(self, "Choose textbook folder")
        if folder:
            self.textbook_edit.setText(folder)
            
    def _browse_output_folder(self):
        folder = QtWidgets.QFileDialog.getExistingDirectory(self, "Choose output folder")
        if folder:
            self.output_edit.setText(folder)
            
    def load_from_settings(self, settings):
        """Load preferences from QSettings"""
        # Load API keys
        self.gemini_key_edit.setText(settings.value("gemini_api_key", ""))
        
        # Load other settings
        self.input_edit.setText(settings.value("input_dir", DEFAULT_INPUT_DIR))
        self.textbook_edit.setText(settings.value("textbook_dir", ""))
        self.output_edit.setText(settings.value("output_dir", DEFAULT_OUTPUT_DIR))
        self.temp_slider.setValue(int(float(settings.value("temperature", "0.5")) * 100))
        self.use_top_examples_chk.setChecked(settings.value("use_top_examples", "true") == "true")
        self.save_logs_chk.setChecked(settings.value("save_logs", "false") == "true")
        self.preview_source_chk.setChecked(settings.value("preview_source", "false") == "true")
        self.special_instructions_edit.setText(settings.value("special_instructions", ""))
        self.compact_sidebar_chk.setChecked(settings.value("ui_compact_sidebar", "false") == "true")
        self.pdf_meta_banner_chk.setChecked(settings.value("pdf_show_meta", "false") == "true")
        
        # Load language setting
        lang_code = settings.value("ui_language", "fr")  # Default to French for your mom
        for i in range(self.language_combo.count()):
            if self.language_combo.itemData(i) == lang_code:
                self.language_combo.setCurrentIndex(i)
                break
        
        # Defaults
        self.default_duration_spin.setValue(int(settings.value("default_duration", "45")))
        self.default_pdf_style_combo.setCurrentText(settings.value("default_pdf_style", list(PDF_TEMPLATES.keys())[0]))
        self.default_subject_combo.setCurrentText(settings.value("default_subject", ""))
        
        # Load Pro/Flash model configuration
        self.pro_model_edit.setText(settings.value("custom_pro_model", DEFAULT_PRO_MODEL))
        self.flash_model_edit.setText(settings.value("custom_flash_model", DEFAULT_FLASH_MODEL))
        self.enable_fallback_chk.setChecked(settings.value("enable_model_fallback", "true") == "true")
        
        # Load advanced model configuration
        self.gemini_model_edit.setText(settings.value("advanced_gemini_model", GEMINI_MODEL))
        self.gemini_toc_model_edit.setText(settings.value("advanced_gemini_toc_model", GEMINI_TOC_MODEL))
        self.gemini_offset_model_edit.setText(settings.value("advanced_gemini_offset_model", GEMINI_OFFSET_MODEL))
        self.gemma_syntax_model_edit.setText(settings.value("advanced_gemma_syntax_model", GEMMA_SYNTAX_MODEL))
        
        # OpenRouter removed - no longer used
        
        # Load prompt editing settings
        self.enable_prompt_editing_chk.setChecked(settings.value("advanced_enable_prompt_editing", "false") == "true")
        
        # Load prompts, showing defaults as placeholders if custom prompts are empty
        toc_prompt = settings.value("advanced_toc_prompt", "").strip()
        page_prompt = settings.value("advanced_page_finding_prompt", "").strip()
        fiche_prompt = settings.value("advanced_fiche_prompt", "").strip()
        
        self.toc_prompt_edit.setPlainText(toc_prompt if toc_prompt else "")
        self.toc_prompt_edit.setPlaceholderText("Default ToC parsing prompt will be used if empty")
        
        self.page_finding_prompt_edit.setPlainText(page_prompt if page_prompt else "")
        self.page_finding_prompt_edit.setPlaceholderText("Default page finding prompt will be used if empty")
        
        self.fiche_prompt_edit.setPlainText(fiche_prompt if fiche_prompt else "")
        self.fiche_prompt_edit.setPlaceholderText("Default fiche generation prompt will be used if empty")
        
        # Update prompt editing visibility
        self._toggle_prompt_editing(self.enable_prompt_editing_chk.isChecked())
        
        self._on_provider_change("")
        
    def save_to_settings(self, settings):
        """Save preferences to QSettings"""
        # Save API keys
        settings.setValue("gemini_api_key", self.gemini_key_edit.text())
        
        # Save other settings
        settings.setValue("input_dir", self.input_edit.text() or DEFAULT_INPUT_DIR)
        settings.setValue("textbook_dir", self.textbook_edit.text())
        settings.setValue("output_dir", self.output_edit.text() or DEFAULT_OUTPUT_DIR)
        settings.setValue("temperature", f"{self.temp_slider.value()/100:.2f}")
        settings.setValue("use_top_examples", "true" if self.use_top_examples_chk.isChecked() else "false")
        settings.setValue("save_logs", "true" if self.save_logs_chk.isChecked() else "false")
        settings.setValue("preview_source", "true" if self.preview_source_chk.isChecked() else "false")
        settings.setValue("special_instructions", self.special_instructions_edit.toPlainText())
        settings.setValue("ui_compact_sidebar", "true" if self.compact_sidebar_chk.isChecked() else "false")
        settings.setValue("pdf_show_meta", "true" if self.pdf_meta_banner_chk.isChecked() else "false")
        settings.setValue("ui_language", self.language_combo.currentData())
        settings.setValue("default_duration", str(self.default_duration_spin.value()))
        settings.setValue("default_pdf_style", self.default_pdf_style_combo.currentText())
        settings.setValue("default_subject", self.default_subject_combo.currentText())
        
        # Save Pro/Flash model configuration
        pro_model = self.pro_model_edit.text().strip() or DEFAULT_PRO_MODEL
        flash_model = self.flash_model_edit.text().strip() or DEFAULT_FLASH_MODEL
        settings.setValue("custom_pro_model", pro_model)
        settings.setValue("custom_flash_model", flash_model)
        settings.setValue("enable_model_fallback", "true" if self.enable_fallback_chk.isChecked() else "false")
        
        # Save advanced model configuration
        settings.setValue("advanced_gemini_model", self.gemini_model_edit.text())
        settings.setValue("advanced_gemini_toc_model", self.gemini_toc_model_edit.text())
        settings.setValue("advanced_gemini_offset_model", self.gemini_offset_model_edit.text())
        settings.setValue("advanced_gemma_syntax_model", self.gemma_syntax_model_edit.text())
        
        # Save prompt editing settings
        settings.setValue("advanced_enable_prompt_editing", "true" if self.enable_prompt_editing_chk.isChecked() else "false")
        settings.setValue("advanced_toc_prompt", self.toc_prompt_edit.toPlainText())
        settings.setValue("advanced_page_finding_prompt", self.page_finding_prompt_edit.toPlainText())
        settings.setValue("advanced_fiche_prompt", self.fiche_prompt_edit.toPlainText())
