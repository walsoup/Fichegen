import os
import json
import re
from datetime import datetime
from PyQt6 import QtWidgets, QtCore, QtGui
from PyQt6.QtGui import QAction

from config import (
    PDF_TEMPLATES,
    DEFAULT_INPUT_DIR,
    DEFAULT_OUTPUT_DIR,
    CLASS_LEVELS,
    API_KEYS,
    HAS_IMAGE_GENERATION,
    HAS_DOCX,
    ICON_PATH,
    tr,
    set_language,
    get_configured_pro_model,
    get_configured_flash_model,
    load_api_keys_from_settings,
    save_rating_record
)
from core.workers import GenerationWorker, EvaluationWorker, QuizWorker, ExportWorker
from core.toc import find_guide_file, get_cached_toc
from ui.preferences import PreferencesDialog

PYQT6 = True

class MainWindow(QtWidgets.QMainWindow):
    def __init__(self):
        super().__init__()
        
        # Settings persistence
        self.settings = QtCore.QSettings("FicheGen", "Pedago")
        
        # Load language setting first
        lang_code = self.settings.value("ui_language", "fr")  # Default to French
        set_language(lang_code)
        
        self.setWindowTitle(tr("app_title"))
        # Give the window its own icon (in addition to the app icon set in main()).
        try:
            if os.path.exists(ICON_PATH):
                self.setWindowIcon(QtGui.QIcon(ICON_PATH))
        except Exception:
            pass

        self.worker = None
        self.export_worker = None
        self.current_content = ""
        self.current_content_type = "unknown"
        self.current_topics_list = []
        self.preview_markdown_limit = 300_000
        self._preview_large_mode = False
        self.log_file_handle = None
        self.current_theme = "light"  # retained for settings compatibility

        # Central layout with splitter
        central = QtWidgets.QWidget()
        self.setCentralWidget(central)
        root_layout = QtWidgets.QVBoxLayout(central)
        root_layout.setContentsMargins(0, 0, 0, 0)  # Remove default margins for cleaner look
        root_layout.setSpacing(0)
        # No header widget - minimalist look

        self.main_splitter = QtWidgets.QSplitter()
        self.main_splitter.setOrientation(QtCore.Qt.Orientation.Horizontal)
        self.main_splitter.setChildrenCollapsible(False)  # Prevent panels from collapsing
        root_layout.addWidget(self.main_splitter, 1)

        # Left sidebar: Clean, compact controls in a scroll area
        left_sidebar_widget = self._build_left_sidebar()
        
        left_sidebar_scroll = QtWidgets.QScrollArea()
        left_sidebar_scroll.setWidgetResizable(True)
        left_sidebar_scroll.setFrameShape(QtWidgets.QFrame.Shape.NoFrame)
        left_sidebar_scroll.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        left_sidebar_scroll.setWidget(left_sidebar_widget)
        left_sidebar_scroll.setMinimumWidth(400)
        left_sidebar_scroll.setMaximumWidth(520)
            
        # Right: Tabs with better styling
        self.right_tabs = QtWidgets.QTabWidget()
        self.right_tabs.setObjectName("ContentTabs")
        self.right_tabs.setDocumentMode(True)
        self.right_tabs.setTabPosition(QtWidgets.QTabWidget.TabPosition.North)
        self.right_tabs.setElideMode(QtCore.Qt.TextElideMode.ElideRight)

        # Preview tab (with edit toggle + stacked editor/view)
        self.preview_tab = QtWidgets.QWidget()
        pv_lay = QtWidgets.QVBoxLayout(self.preview_tab)
        pv_lay.setContentsMargins(16, 12, 16, 16)
        pv_lay.setSpacing(8)

        # Edit toggle bar with better styling
        bar = QtWidgets.QHBoxLayout()
        bar.setContentsMargins(0, 0, 0, 0)
        self.preview_edit_toggle = QtWidgets.QCheckBox("✏️ Edit Markdown")
        self.preview_edit_toggle.setToolTip("Toggle to edit the Markdown directly here.")
        self.preview_edit_toggle.toggled.connect(self.on_preview_edit_toggled)
        bar.addWidget(self.preview_edit_toggle)
        bar.addStretch(1)
        pv_lay.addLayout(bar)

        # Stacked: editor (raw) and preview (rendered)
        self.preview_stack = QtWidgets.QStackedWidget()
        # Editor: raw markdown
        self.preview_editor = QtWidgets.QPlainTextEdit()
        self.preview_editor.setObjectName("preview_editor")
        self.preview_editor.setPlaceholderText("Edit the Markdown here...")
        self.preview_editor.textChanged.connect(self.on_editor_text_changed)
        # Preview: rendered markdown
        self.preview_edit = QtWidgets.QTextEdit()
        self.preview_edit.setObjectName("preview_view")
        self.preview_edit.setReadOnly(True)
        self.preview_edit.setPlaceholderText("Your generated fiche will appear here.")
        self.preview_stack.addWidget(self.preview_edit)   # index 0 = view
        self.preview_stack.addWidget(self.preview_editor) # index 1 = editor
        pv_lay.addWidget(self.preview_stack, 1)

        # Log tab with better styling
        log_widget = QtWidgets.QWidget()
        log_layout = QtWidgets.QVBoxLayout(log_widget)
        log_layout.setContentsMargins(16, 12, 16, 16)
        self.log_edit = QtWidgets.QPlainTextEdit()
        self.log_edit.setObjectName("log_view")
        self.log_edit.setReadOnly(True)
        self.log_edit.setMaximumBlockCount(5000)
        log_layout.addWidget(self.log_edit)

        # Add tabs
        self.right_tabs.addTab(self.preview_tab, tr("preview_tab"))
        self.right_tabs.addTab(log_widget, tr("log_tab"))

        self.main_splitter.addWidget(left_sidebar_scroll)
        self.main_splitter.addWidget(self.right_tabs)
        self.main_splitter.setStretchFactor(0, 0)  # Sidebar doesn't stretch
        self.main_splitter.setStretchFactor(1, 1)  # Right panel takes remaining space
        self.main_splitter.setSizes([450, 750])

        # Apply basic look and load persisted settings
        self._load_settings()
        self._apply_style(self.current_theme)

        # Install native menubar (mac-friendly)
        self._install_menubar()
        # Removed redundant toolbar to keep UI clean and focused

        # macOS niceties: unify title bar + toolbar
        try:
            self.setUnifiedTitleAndToolBarOnMac(True)
        except Exception:
            pass

        # Make comboboxes adjust to content (improves mac dropdown feel)
        for combo in [getattr(self, n, None) for n in (
            'class_combo', 'api_combo', 'model_combo', 'subject_combo', 'pdf_template_combo'
        )]:
            if isinstance(combo, QtWidgets.QComboBox):
                try:
                    combo.setSizeAdjustPolicy(QtWidgets.QComboBox.SizeAdjustPolicy.AdjustToContents)
                    combo.setMinimumContentsLength(1)
                except Exception:
                    pass

        # Status bar for subtle progress text
        if not self.statusBar():
            self.setStatusBar(QtWidgets.QStatusBar())
        self.statusBar().showMessage("Ready")
        self.resize(1200, 800)

    def _apply_style(self, mode: str):
        # On macOS we prefer native appearance with optional minimal polish
        self.current_theme = "light" if mode not in ("light", "dark") else mode
        try:
            # Apply minimal polish that doesn't override native look
            from ui.styles import get_stylesheet
            self.setStyleSheet(get_stylesheet(mode))
        except Exception:
            # Fallback: no stylesheet at all (pure native)
            try:
                self.setStyleSheet("")
            except Exception:
                pass
        # Minimal header label styling without overriding platform look
        try:
            if hasattr(self, 'title_label'):
                self.title_label.setStyleSheet("font-size: 20px; font-weight: 700;")
            if hasattr(self, 'subtitle_label'):
                self.subtitle_label.setStyleSheet("")
        except Exception:
            pass
        # Sidebar spacing preference
        try:
            compact = self.settings.value("ui_compact_sidebar", "false") == "true"
            if getattr(self, "_left_sidebar_layout", None):
                self._left_sidebar_layout.setSpacing(8 if compact else 16)
        except Exception:
            pass

    def _build_left_sidebar(self):
        """Build a clean, macOS-style left sidebar"""
        sidebar = QtWidgets.QWidget()
        sidebar_layout = QtWidgets.QVBoxLayout(sidebar)
        sidebar_layout.setContentsMargins(16, 16, 16, 16)
        sidebar_layout.setSpacing(16)
        # Keep a reference for compact mode adjustments from Preferences
        self._left_sidebar_layout = sidebar_layout

        # Tabbed workflow: fiches vs evaluations
        self.sidebar_tabs = QtWidgets.QTabWidget()
        self.sidebar_tabs.setObjectName("SidebarTabs")
        self.sidebar_tabs.setTabPosition(QtWidgets.QTabWidget.TabPosition.West)
        self.sidebar_tabs.setDocumentMode(True)
        self.sidebar_tabs.setElideMode(QtCore.Qt.TextElideMode.ElideRight)
        self.sidebar_tabs.addTab(self._build_fiche_tab(), tr("fiches_tab"))
        self.sidebar_tabs.addTab(self._build_evaluation_tab(), tr("evaluations_tab"))
        self.sidebar_tabs.addTab(self._build_quiz_tab(), tr("quizzes_tab"))
        sidebar_layout.addWidget(self.sidebar_tabs)

        # Progress section (shared)
        progress_group = self._build_progress_section()
        sidebar_layout.addWidget(progress_group)

        # Footer with rating and actions
        footer_group = self._build_sidebar_footer()
        sidebar_layout.addWidget(footer_group)

        sidebar_layout.addStretch(1)
        return sidebar

    def _build_fiche_tab(self):
        tab = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(tab)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        layout.addWidget(self._build_main_controls())
        layout.addWidget(self._build_quick_settings())
        layout.addStretch(1)
        return tab

    def _build_evaluation_tab(self):
        tab = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(tab)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        # Configuration (class & subject)
        config_group = QtWidgets.QGroupBox("Configuration")
        config_form = QtWidgets.QFormLayout(config_group)
        config_form.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)

        self.eval_class_combo = QtWidgets.QComboBox()
        self.eval_class_combo.addItems(CLASS_LEVELS)
        if hasattr(self, 'class_combo') and isinstance(self.class_combo, QtWidgets.QComboBox):
            self.eval_class_combo.setCurrentText(self.class_combo.currentText())
        self.eval_class_combo.currentTextChanged.connect(self._on_eval_class_changed)
        config_form.addRow("Class:", self.eval_class_combo)

        self.eval_subject_combo = QtWidgets.QComboBox()
        self.eval_subject_combo.setEditable(True)
        self.eval_subject_combo.addItems([
            "", "Mathématiques", "Sciences", "Français", "Histoire",
            "Géographie", "Éducation civique", "Arabe", "Anglais", "Islamique"
        ])
        if hasattr(self, 'subject_combo') and isinstance(self.subject_combo, QtWidgets.QComboBox):
            self.eval_subject_combo.setCurrentText(self.subject_combo.currentText())
        config_form.addRow("Subject:", self.eval_subject_combo)
        layout.addWidget(config_group)

        # Topics selection
        topics_group = QtWidgets.QGroupBox("Lesson Topics")
        topics_layout = QtWidgets.QVBoxLayout(topics_group)

        self.eval_lessons_list = QtWidgets.QListWidget()
        self.eval_lessons_list.setSelectionMode(QtWidgets.QAbstractItemView.SelectionMode.ExtendedSelection)
        self.eval_lessons_list.setMinimumHeight(150)
        self.eval_lessons_list.itemDoubleClicked.connect(lambda item: self._append_eval_topic(item.text()))
        topics_layout.addWidget(self.eval_lessons_list)

        list_buttons = QtWidgets.QHBoxLayout()
        self.eval_refresh_lessons_btn = QtWidgets.QPushButton("🔄 Refresh Lessons")
        self.eval_refresh_lessons_btn.clicked.connect(self._load_eval_lessons)
        self.eval_add_selected_btn = QtWidgets.QPushButton("➕ Add Selected")
        self.eval_add_selected_btn.clicked.connect(self._on_eval_add_selected_lessons)
        list_buttons.addWidget(self.eval_refresh_lessons_btn)
        list_buttons.addWidget(self.eval_add_selected_btn)
        list_buttons.addStretch()
        topics_layout.addLayout(list_buttons)

        self.eval_topics_edit = QtWidgets.QPlainTextEdit()
        self.eval_topics_edit.setPlaceholderText("Enter lesson topics, one per line...")
        self.eval_topics_edit.setMinimumHeight(100)
        topics_layout.addWidget(self.eval_topics_edit)
        layout.addWidget(topics_group)

        # Evaluation settings
        settings_group = QtWidgets.QGroupBox("Evaluation Settings")
        settings_form = QtWidgets.QFormLayout(settings_group)
        settings_form.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)

        # School name
        self.eval_school_name_edit = QtWidgets.QLineEdit()
        self.eval_school_name_edit.setText(self.settings.value("eval_school_name", "Groupe Scolaire"))
        self.eval_school_name_edit.setPlaceholderText("e.g., Groupe Scolaire Jabrane")
        settings_form.addRow("School Name:", self.eval_school_name_edit)

        # Academic year
        self.eval_academic_year_edit = QtWidgets.QLineEdit()
        self.eval_academic_year_edit.setText(self.settings.value("eval_academic_year", "2025/2026"))
        self.eval_academic_year_edit.setPlaceholderText("e.g., 2025/2026")
        settings_form.addRow("Academic Year:", self.eval_academic_year_edit)

        # Evaluation number and semester in one row
        eval_session_widget = QtWidgets.QWidget()
        eval_session_layout = QtWidgets.QHBoxLayout(eval_session_widget)
        eval_session_layout.setContentsMargins(0, 0, 0, 0)
        eval_session_layout.setSpacing(8)

        self.eval_number_spin = QtWidgets.QSpinBox()
        self.eval_number_spin.setRange(1, 10)
        self.eval_number_spin.setValue(int(self.settings.value("eval_number", "1")))
        self.eval_number_spin.setPrefix("N° ")
        eval_session_layout.addWidget(self.eval_number_spin)

        eval_session_layout.addWidget(QtWidgets.QLabel("Semester:"))
        self.eval_semester_combo = QtWidgets.QComboBox()
        self.eval_semester_combo.addItems(["1", "2"])
        self.eval_semester_combo.setCurrentText(self.settings.value("eval_semester", "1"))
        eval_session_layout.addWidget(self.eval_semester_combo)
        eval_session_layout.addStretch()

        settings_form.addRow("Evaluation:", eval_session_widget)

        # Max score (10 or 20)
        max_score_widget = QtWidgets.QWidget()
        max_score_layout = QtWidgets.QHBoxLayout(max_score_widget)
        max_score_layout.setContentsMargins(0, 0, 0, 0)
        max_score_layout.setSpacing(8)

        self.eval_max_score_10 = QtWidgets.QRadioButton("/ 10 points")
        self.eval_max_score_20 = QtWidgets.QRadioButton("/ 20 points")
        self.eval_max_score_10.setChecked(self.settings.value("eval_max_score", "10") == "10")
        self.eval_max_score_20.setChecked(self.settings.value("eval_max_score", "10") == "20")
        max_score_layout.addWidget(self.eval_max_score_10)
        max_score_layout.addWidget(self.eval_max_score_20)
        max_score_layout.addStretch()

        settings_form.addRow("Max Score:", max_score_widget)

        self.eval_duration_spin = QtWidgets.QSpinBox()
        self.eval_duration_spin.setRange(30, 180)
        self.eval_duration_spin.setSingleStep(15)
        self.eval_duration_spin.setSuffix(" min")
        self.eval_duration_spin.setValue(45)
        settings_form.addRow("Duration:", self.eval_duration_spin)

        self.eval_difficulty_combo = QtWidgets.QComboBox()
        self.eval_difficulty_combo.addItems(["Adapted to class level", "Easy", "Medium", "Hard", "Mixed"])
        settings_form.addRow("Difficulty:", self.eval_difficulty_combo)

        self.eval_question_types_edit = QtWidgets.QPlainTextEdit()
        self.eval_question_types_edit.setPlaceholderText("Question types (optional):\nMultiple choice, Short answers, Matching...")
        self.eval_question_types_edit.setMaximumHeight(90)
        settings_form.addRow("Question Types:", self.eval_question_types_edit)
        layout.addWidget(settings_group)

        # AI preferences (simplified - model selector is in footer)
        ai_group = QtWidgets.QGroupBox("AI Settings")
        ai_form = QtWidgets.QFormLayout(ai_group)
        ai_form.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)

        temp_widget = QtWidgets.QWidget()
        temp_layout = QtWidgets.QHBoxLayout(temp_widget)
        temp_layout.setContentsMargins(0, 0, 0, 0)
        self.eval_temperature_slider = QtWidgets.QSlider(QtCore.Qt.Orientation.Horizontal)
        self.eval_temperature_slider.setRange(0, 100)
        self.eval_temperature_slider.setValue(int(float(self.settings.value("temperature", "0.5")) * 100))
        self.eval_temperature_label = QtWidgets.QLabel(f"{self.eval_temperature_slider.value()/100:.2f}")
        self.eval_temperature_slider.valueChanged.connect(lambda v: self.eval_temperature_label.setText(f"{v/100:.2f}"))
        temp_layout.addWidget(self.eval_temperature_slider, 1)
        temp_layout.addWidget(self.eval_temperature_label)
        ai_form.addRow("Temperature:", temp_widget)
        
        # Note about model selection
        note_label = QtWidgets.QLabel("💡 Gemini model (Pro/Flash) is selected in the Output section below")
        note_label.setWordWrap(True)
        note_label.setStyleSheet("color: gray; font-size: 11px; font-style: italic;")
        ai_form.addRow("", note_label)
        
        layout.addWidget(ai_group)

        # Formatting preferences
        formatting_group = QtWidgets.QGroupBox("Formatting Preferences")
        formatting_layout = QtWidgets.QVBoxLayout(formatting_group)

        self.eval_include_tables_chk = QtWidgets.QCheckBox("Include tabular questions (tables)")
        self.eval_include_boxes_chk = QtWidgets.QCheckBox("Highlight instructions with callout boxes")
        self.eval_include_matching_chk = QtWidgets.QCheckBox("Include matching/connect-the-dots exercises")
        self.eval_include_answer_key_chk = QtWidgets.QCheckBox("Append answer key at the end")

        formatting_layout.addWidget(self.eval_include_tables_chk)
        formatting_layout.addWidget(self.eval_include_boxes_chk)
        formatting_layout.addWidget(self.eval_include_matching_chk)
        formatting_layout.addWidget(self.eval_include_answer_key_chk)

        # Image generation option (if available)
        if HAS_IMAGE_GENERATION:
            self.eval_generate_images_chk = QtWidgets.QCheckBox("Include illustrations/coloring pages (CP/CE1: coloring, others: diagrams)")
            self.eval_generate_images_chk.setChecked(self.settings.value("generate_eval_images", "false") == "true")
            self.eval_generate_images_chk.setToolTip("Generate simple educational illustrations or coloring pages (hand-drawn style, non-AI look)")
            self.eval_generate_images_chk.toggled.connect(lambda checked: self.settings.setValue("generate_eval_images", "true" if checked else "false"))
            formatting_layout.addWidget(self.eval_generate_images_chk)
            
            # Number of images selector
            images_count_layout = QtWidgets.QHBoxLayout()
            images_count_layout.addWidget(QtWidgets.QLabel("  Number of images:"))
            self.eval_images_count_spin = QtWidgets.QSpinBox()
            self.eval_images_count_spin.setRange(1, 5)
            self.eval_images_count_spin.setValue(int(self.settings.value("eval_images_count", "2")))
            self.eval_images_count_spin.setEnabled(self.eval_generate_images_chk.isChecked())
            self.eval_generate_images_chk.toggled.connect(self.eval_images_count_spin.setEnabled)
            self.eval_images_count_spin.valueChanged.connect(lambda v: self.settings.setValue("eval_images_count", str(v)))
            images_count_layout.addWidget(self.eval_images_count_spin)
            images_count_layout.addStretch()
            formatting_layout.addLayout(images_count_layout)

        self.eval_extra_instructions_edit = QtWidgets.QPlainTextEdit()
        self.eval_extra_instructions_edit.setPlaceholderText("Additional instructions for AI (optional)...")
        self.eval_extra_instructions_edit.setMaximumHeight(90)
        formatting_layout.addWidget(self.eval_extra_instructions_edit)
        layout.addWidget(formatting_group)

        # Action buttons
        action_layout = QtWidgets.QHBoxLayout()
        action_layout.addStretch()
        self.generate_eval_btn = QtWidgets.QPushButton(tr("generate_eval"))
        self.generate_eval_btn.setObjectName("generate_eval_btn")
        self.generate_eval_btn.clicked.connect(self.start_evaluation_generation)
        action_layout.addWidget(self.generate_eval_btn)
        layout.addLayout(action_layout)

        layout.addStretch(1)

        # Initial data load
        self._load_eval_lessons()

        return tab

    def _build_quiz_tab(self):
        """Build the quiz generation tab - for quick formative assessments."""
        tab = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(tab)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        # Configuration (class & subject)
        config_group = QtWidgets.QGroupBox("Configuration")
        config_form = QtWidgets.QFormLayout(config_group)
        config_form.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)

        self.quiz_class_combo = QtWidgets.QComboBox()
        self.quiz_class_combo.addItems(CLASS_LEVELS)
        self.quiz_class_combo.currentTextChanged.connect(self._on_quiz_class_changed)
        config_form.addRow("Class:", self.quiz_class_combo)

        self.quiz_subject_combo = QtWidgets.QComboBox()
        self.quiz_subject_combo.setEditable(True)
        self.quiz_subject_combo.addItems([
            "", "Mathématiques", "Sciences", "Français", "Histoire",
            "Géographie", "Éducation civique", "Arabe", "Anglais", "Islamique"
        ])
        config_form.addRow("Subject:", self.quiz_subject_combo)
        layout.addWidget(config_group)

        # Topic selection (single topic for quiz)
        topic_group = QtWidgets.QGroupBox("Lesson Topic")
        topic_layout = QtWidgets.QVBoxLayout(topic_group)

        self.quiz_lessons_list = QtWidgets.QListWidget()
        self.quiz_lessons_list.setSelectionMode(QtWidgets.QAbstractItemView.SelectionMode.SingleSelection)
        self.quiz_lessons_list.setMinimumHeight(120)
        self.quiz_lessons_list.itemDoubleClicked.connect(lambda item: self.quiz_topic_edit.setText(item.text()))
        topic_layout.addWidget(self.quiz_lessons_list)

        list_buttons = QtWidgets.QHBoxLayout()
        self.quiz_refresh_lessons_btn = QtWidgets.QPushButton("🔄 Refresh")
        self.quiz_refresh_lessons_btn.clicked.connect(self._load_quiz_lessons)
        self.quiz_use_selected_btn = QtWidgets.QPushButton("✓ Use Selected")
        self.quiz_use_selected_btn.clicked.connect(self._on_quiz_use_selected)
        list_buttons.addWidget(self.quiz_refresh_lessons_btn)
        list_buttons.addWidget(self.quiz_use_selected_btn)
        list_buttons.addStretch()
        topic_layout.addLayout(list_buttons)

        self.quiz_topic_edit = QtWidgets.QLineEdit()
        self.quiz_topic_edit.setPlaceholderText("Enter quiz topic...")
        topic_layout.addWidget(self.quiz_topic_edit)
        layout.addWidget(topic_group)

        # Quiz settings
        settings_group = QtWidgets.QGroupBox("Quiz Settings")
        settings_form = QtWidgets.QFormLayout(settings_group)
        settings_form.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)

        # Quiz type
        self.quiz_type_combo = QtWidgets.QComboBox()
        self.quiz_type_combo.addItems([
            "Quick Check (5 questions)",
            "Standard Quiz (10 questions)",
            "Comprehensive (15 questions)",
            "Mini Quiz (3 questions)"
        ])
        settings_form.addRow("Quiz Type:", self.quiz_type_combo)

        # Question format
        self.quiz_format_combo = QtWidgets.QComboBox()
        self.quiz_format_combo.addItems([
            "Mixed (MCQ + Short Answer)",
            "Multiple Choice Only",
            "Short Answer Only",
            "True/False + MCQ",
            "Fill in the Blanks"
        ])
        settings_form.addRow("Format:", self.quiz_format_combo)

        # Difficulty
        self.quiz_difficulty_combo = QtWidgets.QComboBox()
        self.quiz_difficulty_combo.addItems(["Adapted to class level", "Easy", "Medium", "Hard"])
        settings_form.addRow("Difficulty:", self.quiz_difficulty_combo)

        # Duration (shorter for quizzes)
        self.quiz_duration_spin = QtWidgets.QSpinBox()
        self.quiz_duration_spin.setRange(5, 30)
        self.quiz_duration_spin.setSingleStep(5)
        self.quiz_duration_spin.setSuffix(" min")
        self.quiz_duration_spin.setValue(10)
        settings_form.addRow("Duration:", self.quiz_duration_spin)

        # Include answer key
        self.quiz_include_answers_chk = QtWidgets.QCheckBox("Include answer key")
        self.quiz_include_answers_chk.setChecked(True)
        settings_form.addRow("", self.quiz_include_answers_chk)

        layout.addWidget(settings_group)

        # Additional instructions
        extra_group = QtWidgets.QGroupBox("Additional Instructions")
        extra_layout = QtWidgets.QVBoxLayout(extra_group)
        self.quiz_extra_instructions_edit = QtWidgets.QPlainTextEdit()
        self.quiz_extra_instructions_edit.setPlaceholderText("Optional: specific requirements, topics to emphasize...")
        self.quiz_extra_instructions_edit.setMaximumHeight(80)
        extra_layout.addWidget(self.quiz_extra_instructions_edit)
        layout.addWidget(extra_group)

        # Action buttons
        action_layout = QtWidgets.QHBoxLayout()
        action_layout.addStretch()
        self.generate_quiz_btn = QtWidgets.QPushButton(tr("generate_quiz"))
        self.generate_quiz_btn.setObjectName("generate_quiz_btn")
        self.generate_quiz_btn.clicked.connect(self.start_quiz_generation)
        action_layout.addWidget(self.generate_quiz_btn)
        layout.addLayout(action_layout)

        layout.addStretch(1)

        # Initial data load
        self._load_quiz_lessons()

        return tab

    def _on_quiz_class_changed(self, class_text):
        """Handle class change in quiz tab."""
        self._load_quiz_lessons()

    def _load_quiz_lessons(self):
        """Load lessons for the quiz tab from cached ToC."""
        try:
            self.quiz_lessons_list.clear()
            class_level = self.quiz_class_combo.currentText()
            guides_dir = self.settings.value("input_dir", DEFAULT_INPUT_DIR)
            
            # Create a dummy queue for the find function
            class DummyQueue:
                def put(self, msg): pass
            
            guide_path = find_guide_file(class_level, guides_dir, DummyQueue())
            if guide_path:
                cached_toc = get_cached_toc(guide_path, guides_dir)
                if cached_toc:
                    for topic_data in cached_toc:
                        if isinstance(topic_data, dict):
                            topic = topic_data.get("topic") or topic_data.get("title", "Unknown")
                        else:
                            topic = str(topic_data)
                        self.quiz_lessons_list.addItem(topic)
        except Exception as e:
            print(f"Error loading quiz lessons: {e}")

    def _on_quiz_use_selected(self):
        """Use the selected lesson for quiz generation."""
        selected = self.quiz_lessons_list.selectedItems()
        if selected:
            self.quiz_topic_edit.setText(selected[0].text())

    def start_quiz_generation(self):
        """Start generating a quiz."""
        # Check if already running
        if self.worker is not None and self.worker.isRunning():
            QtWidgets.QMessageBox.information(
                self, 
                "Please Wait", 
                "An operation is already running. Cancel it before starting a new one."
            )
            return
        
        # Check API key
        if not API_KEYS.get("GEMINI_API_KEY"):
            reply = QtWidgets.QMessageBox.warning(
                self,
                "API Key Missing",
                "Gemini API key is not configured.\n\nWould you like to open Preferences?",
                QtWidgets.QMessageBox.StandardButton.Yes | QtWidgets.QMessageBox.StandardButton.No,
                QtWidgets.QMessageBox.StandardButton.Yes
            )
            if reply == QtWidgets.QMessageBox.StandardButton.Yes:
                self._show_preferences()
            return

        # Gather inputs
        topic = self.quiz_topic_edit.text().strip()
        if not topic:
            QtWidgets.QMessageBox.warning(self, "Missing Topic", "Please enter a quiz topic.")
            return
        self.current_topics_list = [topic]

        class_level = self.quiz_class_combo.currentText()
        subject = self.quiz_subject_combo.currentText().strip()
        quiz_type = self.quiz_type_combo.currentText()
        quiz_format = self.quiz_format_combo.currentText()
        difficulty = self.quiz_difficulty_combo.currentText()
        duration = self.quiz_duration_spin.value()
        include_answers = self.quiz_include_answers_chk.isChecked()
        extra_instructions = self.quiz_extra_instructions_edit.toPlainText().strip()
        temperature = self.settings.value("temperature", "0.5")

        # Determine number of questions from quiz type
        if "5 questions" in quiz_type:
            num_questions = 5
        elif "10 questions" in quiz_type:
            num_questions = 10
        elif "15 questions" in quiz_type:
            num_questions = 15
        elif "3 questions" in quiz_type:
            num_questions = 3
        else:
            num_questions = 10

        # Clear previous content
        self.log_edit.clear()
        self.preview_editor.clear()
        self.preview_edit.clear()
        self.current_content = ""
        self.current_content_type = "quiz"
        self.save_pdf_btn.setEnabled(False)
        self.save_docx_btn.setEnabled(False)
        self._set_rating_enabled(False)
        self.cancel_btn.setEnabled(True)

        # Create and start worker
        self.worker = QuizWorker(
            class_level=class_level,
            topic=topic,
            subject=subject,
            quiz_type=quiz_type,
            quiz_format=quiz_format,
            difficulty=difficulty,
            duration=duration,
            num_questions=num_questions,
            include_answers=include_answers,
            extra_instructions=extra_instructions,
            temperature=float(temperature),
            guides_dir=self.settings.value("input_dir", DEFAULT_INPUT_DIR),
            textbook_dir=self.settings.value("textbook_dir", ""),
            use_student_textbook=self.use_student_textbook_chk.isChecked()
        )
        
        self.worker.log.connect(self.append_log)
        self.worker.progress.connect(self.progress.setValue)
        self.worker.content.connect(self.on_content_ready)
        self.worker.done.connect(self.on_done)
        self.worker.enable_buttons.connect(self.on_enable_buttons)
        self.worker.finished.connect(self.on_enable_buttons)
        self.worker.start()

        self.append_log(f"🎯 Starting quiz generation for: {topic}")

    def _build_main_controls(self):
        """Essential controls for generation"""
        group = QtWidgets.QGroupBox("Generate Fiche")
        layout = QtWidgets.QFormLayout(group)
        layout.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)
        layout.setLabelAlignment(QtCore.Qt.AlignmentFlag.AlignRight)
        layout.setFormAlignment(QtCore.Qt.AlignmentFlag.AlignLeft | QtCore.Qt.AlignmentFlag.AlignTop)
        layout.setVerticalSpacing(8)

        # Class Level
        self.class_combo = QtWidgets.QComboBox()
        self.class_combo.addItems(CLASS_LEVELS)
        self.class_combo.currentTextChanged.connect(self._on_class_changed)
        layout.addRow("Class:", self.class_combo)

        # Lesson Topic with dropdown selection
        topic_widget = QtWidgets.QWidget()
        topic_layout = QtWidgets.QVBoxLayout(topic_widget)
        topic_layout.setContentsMargins(0, 0, 0, 0)
        topic_layout.setSpacing(4)
        
        # Lesson selector dropdown (initially hidden)
        self.lesson_selector_combo = QtWidgets.QComboBox()
        self.lesson_selector_combo.setVisible(False)
        self.lesson_selector_combo.addItem("-- Select a lesson --")
        self.lesson_selector_combo.currentTextChanged.connect(self._on_lesson_selected)
        topic_layout.addWidget(self.lesson_selector_combo)
        
        # Manual topic input (always visible)
        self.topic_edit = QtWidgets.QLineEdit()
        self.topic_edit.setPlaceholderText("e.g., Les 5 sens (or select from dropdown above)")
        topic_layout.addWidget(self.topic_edit)
        
        layout.addRow("Topic:", topic_widget)
        
        # Load available lessons on startup
        self._load_available_lessons()

        # Subject (simplified)
        self.subject_combo = QtWidgets.QComboBox()
        self.subject_combo.addItems([
            "", "Mathématiques", "Sciences", "Français", "Histoire", 
            "Géographie", "Éducation civique", "Arabe", "Anglais", "Islamique"
        ])
        layout.addRow("Subject:", self.subject_combo)

        # Action button
        self.generate_btn = QtWidgets.QPushButton(tr("generate_fiche"))
        self.generate_btn.setObjectName("generate_btn")
        self.generate_btn.setDefault(True)
        self.generate_btn.clicked.connect(self.start_generation)
        layout.addRow("", self.generate_btn)
        return group

    def _build_quick_settings(self):
        """Quick access to common settings"""
        group = QtWidgets.QGroupBox("Quick Settings")
        layout = QtWidgets.QFormLayout(group)
        layout.setFieldGrowthPolicy(QtWidgets.QFormLayout.FieldGrowthPolicy.ExpandingFieldsGrow)
        layout.setLabelAlignment(QtCore.Qt.AlignmentFlag.AlignRight)
        layout.setVerticalSpacing(8)

        # Duration
        self.duration_spin = QtWidgets.QSpinBox()
        self.duration_spin.setRange(15, 180)
        self.duration_spin.setSingleStep(5)
        try:
            default_duration = int(self.settings.value("default_duration", "45"))
        except Exception:
            default_duration = 45
        self.duration_spin.setValue(default_duration)
        self.duration_spin.setSuffix(" min")
        layout.addRow("Duration:", self.duration_spin)

        # Pages override
        self.pages_edit = QtWidgets.QLineEdit()
        self.pages_edit.setPlaceholderText("e.g., 12-16,18")
        layout.addRow("Pages:", self.pages_edit)

        # Preview source text toggle
        self.quick_preview_source_chk = QtWidgets.QCheckBox("Preview source text")
        self.quick_preview_source_chk.setChecked(self.settings.value("preview_source", "false") == "true")
        self.quick_preview_source_chk.setToolTip("Show extracted PDF text for confirmation before generating")
        self.quick_preview_source_chk.toggled.connect(self._on_quick_preview_changed)
        layout.addRow("", self.quick_preview_source_chk)

        # Image generation toggle (if available)
        if HAS_IMAGE_GENERATION:
            self.generate_fiche_image_chk = QtWidgets.QCheckBox("Include illustration")
            self.generate_fiche_image_chk.setChecked(self.settings.value("generate_fiche_images", "false") == "true")
            self.generate_fiche_image_chk.setToolTip("Generate a simple educational illustration (hand-drawn style, non-AI look)")
            self.generate_fiche_image_chk.toggled.connect(lambda checked: self.settings.setValue("generate_fiche_images", "true" if checked else "false"))
            layout.addRow("", self.generate_fiche_image_chk)

        return group

    def _build_progress_section(self):
        """Progress indicator and status"""
        group = QtWidgets.QGroupBox("Progress")
        layout = QtWidgets.QVBoxLayout(group)
        layout.setSpacing(8)

        self.progress = QtWidgets.QProgressBar()
        self.progress.setRange(0, 100)
        self.progress.setValue(0)
        layout.addWidget(self.progress)

        # Status text (smaller, secondary)
        self.status_label = QtWidgets.QLabel("Ready to generate")
        self.status_label.setObjectName("status_label")
        self.status_label.setWordWrap(True)
        layout.addWidget(self.status_label)

        button_row = QtWidgets.QHBoxLayout()
        button_row.addStretch()
        self.cancel_btn = QtWidgets.QPushButton("Cancel")
        self.cancel_btn.setObjectName("cancel_btn")
        self.cancel_btn.setEnabled(False)
        self.cancel_btn.clicked.connect(self.cancel_generation)
        button_row.addWidget(self.cancel_btn)
        layout.addLayout(button_row)

        return group

    def _build_sidebar_footer(self):
        """Rating and save controls"""
        group = QtWidgets.QGroupBox("Output")
        layout = QtWidgets.QVBoxLayout(group)
        layout.setSpacing(8)

        # Preferences button at the top
        prefs_layout = QtWidgets.QHBoxLayout()
        self.prefs_btn = QtWidgets.QPushButton("⚙️ Preferences")
        self.prefs_btn.setObjectName("prefs_btn")
        self.prefs_btn.clicked.connect(self._show_preferences)
        self.prefs_btn.setToolTip("Open application preferences")
        prefs_layout.addWidget(self.prefs_btn)
        prefs_layout.addStretch()
        layout.addLayout(prefs_layout)

        # Gemini Model Toggle (Pro vs Flash) - Shared across fiches, evals, and quizzes
        model_group = QtWidgets.QGroupBox("AI Model")
        model_group_layout = QtWidgets.QHBoxLayout(model_group)
        model_group_layout.setContentsMargins(8, 8, 8, 8)
        
        self.model_toggle = QtWidgets.QRadioButton("Pro")
        self.model_toggle_flash = QtWidgets.QRadioButton("Flash")
        
        # Set tooltips with actual model names
        pro_model = get_configured_pro_model()
        flash_model = get_configured_flash_model()
        self.model_toggle.setToolTip(f"Use Pro model: {pro_model}\n(Higher quality, slower)")
        self.model_toggle_flash.setToolTip(f"Use Flash model: {flash_model}\n(Faster, good for drafts)")
        
        # Set default to Pro
        use_pro = self.settings.value("gemini_use_pro", "true") == "true"
        self.model_toggle.setChecked(use_pro)
        self.model_toggle_flash.setChecked(not use_pro)
        
        # Connect to update function
        self.model_toggle.toggled.connect(self._on_model_toggle_changed)
        self.model_toggle_flash.toggled.connect(self._on_model_toggle_changed)
        
        model_group_layout.addWidget(QtWidgets.QLabel("Gemini:"))
        model_group_layout.addWidget(self.model_toggle)
        model_group_layout.addWidget(self.model_toggle_flash)
        
        # Fallback indicator
        enable_fallback = self.settings.value("enable_model_fallback", "true") == "true"
        if enable_fallback:
            fallback_label = QtWidgets.QLabel("🔄")
            fallback_label.setToolTip("Auto-fallback enabled: if Pro fails, Flash will be used")
            model_group_layout.addWidget(fallback_label)
        
        model_group_layout.addStretch()
        
        layout.addWidget(model_group)

        # PDF Template selector (shared across fiches, evals, and quizzes)
        pdf_group = QtWidgets.QGroupBox("PDF Style")
        pdf_group_layout = QtWidgets.QHBoxLayout(pdf_group)
        pdf_group_layout.setContentsMargins(8, 8, 8, 8)
        
        self.pdf_template_combo = QtWidgets.QComboBox()
        self.pdf_template_combo.addItems(list(PDF_TEMPLATES.keys()))
        self.pdf_template_combo.setCurrentText(self.settings.value("default_pdf_style", list(PDF_TEMPLATES.keys())[0]))
        self.pdf_template_combo.setToolTip("Choose the visual style for PDF export")
        
        pdf_group_layout.addWidget(self.pdf_template_combo, 1)
        layout.addWidget(pdf_group)

        # Student textbook toggle
        self.use_student_textbook_chk = QtWidgets.QCheckBox("📚 Include student textbook context")
        self.use_student_textbook_chk.setChecked(self.settings.value("use_student_textbook", "false") == "true")
        self.use_student_textbook_chk.setToolTip("Extract content from student textbooks in addition to teacher guides\n(Useful for evaluations with exercises)")
        self.use_student_textbook_chk.toggled.connect(self._on_student_textbook_toggle)
        layout.addWidget(self.use_student_textbook_chk)

        # Rating section
        rating_layout = QtWidgets.QHBoxLayout()
        self.rating_label = QtWidgets.QLabel("Rate:")
        self.rating_combo = QtWidgets.QComboBox()
        self.rating_combo.addItems(["1 ★", "2 ★★", "3 ★★★", "4 ★★★★", "5 ★★★★★"])
        self.rating_combo.setCurrentIndex(4)
        self.rating_btn = QtWidgets.QPushButton("Save Rating")
        self.rating_btn.clicked.connect(self.save_current_rating)
        self._set_rating_enabled(False)

        rating_layout.addWidget(self.rating_label)
        rating_layout.addWidget(self.rating_combo, 1)
        rating_layout.addWidget(self.rating_btn)
        layout.addLayout(rating_layout)

        # Save buttons
        save_layout = QtWidgets.QHBoxLayout()
        self.save_pdf_btn = QtWidgets.QPushButton("PDF")
        self.save_pdf_btn.setObjectName("save_pdf_btn")
        self.save_pdf_btn.setEnabled(False)
        self.save_pdf_btn.clicked.connect(self.save_current_pdf)
        self.save_pdf_btn.setToolTip("Save as PDF (Cmd+S)")
        
        self.save_docx_btn = QtWidgets.QPushButton("DOCX")
        self.save_docx_btn.setObjectName("save_docx_btn")
        self.save_docx_btn.setEnabled(False)
        self.save_docx_btn.clicked.connect(self.save_current_docx)
        self.save_docx_btn.setToolTip("Save as DOCX (Shift+Cmd+S)")
        
        self.clear_btn = QtWidgets.QPushButton("Clear")
        self.clear_btn.setObjectName("clear_btn")
        self.clear_btn.clicked.connect(self.clear_preview)
        self.clear_btn.setToolTip("Clear preview (Cmd+Backspace)")

        save_layout.addWidget(self.save_pdf_btn)
        save_layout.addWidget(self.save_docx_btn)
        save_layout.addWidget(self.clear_btn)
        layout.addLayout(save_layout)
        
        return group

    # Native menubar (macOS style with standard shortcuts)
    def _install_menubar(self):
        """Install a complete macOS-style menubar with all standard menus."""
        mb = self.menuBar()
        
        # Use the correct QAction class for PyQt6
        from PyQt6.QtGui import QAction
        
        # Get MenuRole for macOS menu handling
        try:
            MenuRole = QAction.MenuRole
        except AttributeError:
            MenuRole = None

        # File menu
        file_menu = mb.addMenu("&File")
        
        # New/Open actions (typical for macOS apps)
        act_new = QAction("&New", self)
        act_new.setShortcut("Meta+N")
        act_new.triggered.connect(self.clear_preview)  # Clear for new document
        file_menu.addAction(act_new)
        
        file_menu.addSeparator()
        
        # Generate action
        act_generate = QAction("&Generate", self)
        act_generate.setShortcut("Meta+Return")
        act_generate.triggered.connect(self.start_generation)
        file_menu.addAction(act_generate)

        # Cancel action
        act_cancel = QAction("&Cancel", self)
        act_cancel.setShortcut("Esc")
        act_cancel.triggered.connect(self.cancel_generation)
        file_menu.addAction(act_cancel)

        file_menu.addSeparator()

        # Save actions
        act_save_pdf = QAction("Save as &PDF…", self)
        act_save_pdf.setShortcut("Meta+S")
        act_save_pdf.triggered.connect(self.save_current_pdf)
        file_menu.addAction(act_save_pdf)

        act_save_docx = QAction("Save as &DOCX…", self)
        act_save_docx.setShortcut("Shift+Meta+S")
        act_save_docx.triggered.connect(self.save_current_docx)
        file_menu.addAction(act_save_docx)

        file_menu.addSeparator()

        # Clear action
        act_clear = QAction("&Clear Preview", self)
        act_clear.setShortcut("Meta+Backspace")
        act_clear.triggered.connect(self.clear_preview)
        file_menu.addAction(act_clear)

        # Edit menu (standard macOS)
        edit_menu = mb.addMenu("&Edit")
        
        act_undo = QAction("&Undo", self)
        act_undo.setShortcut("Meta+Z")
        act_undo.setEnabled(False)  # Disabled for now
        edit_menu.addAction(act_undo)
        
        act_redo = QAction("&Redo", self)
        act_redo.setShortcut("Shift+Meta+Z")
        act_redo.setEnabled(False)  # Disabled for now
        edit_menu.addAction(act_redo)
        
        edit_menu.addSeparator()
        
        act_cut = QAction("Cu&t", self)
        act_cut.setShortcut("Meta+X")
        edit_menu.addAction(act_cut)
        
        act_copy = QAction("&Copy", self)
        act_copy.setShortcut("Meta+C")
        edit_menu.addAction(act_copy)
        
        act_paste = QAction("&Paste", self)
        act_paste.setShortcut("Meta+V")
        edit_menu.addAction(act_paste)
        
        act_select_all = QAction("Select &All", self)
        act_select_all.setShortcut("Meta+A")
        edit_menu.addAction(act_select_all)
        
        edit_menu.addSeparator()
        
        # Find actions
        act_find = QAction("&Find…", self)
        act_find.setShortcut("Meta+F")
        act_find.setEnabled(False)  # Disabled for now
        edit_menu.addAction(act_find)

        # View menu
        view_menu = mb.addMenu("&View")
        
        act_toggle_edit = QAction("Toggle &Markdown Edit", self)
        act_toggle_edit.setShortcut("Meta+E")
        act_toggle_edit.setCheckable(True)
        act_toggle_edit.setChecked(False)
        def _toggle_edit(checked):
            if hasattr(self, 'preview_edit_toggle'):
                self.preview_edit_toggle.setChecked(checked)
        act_toggle_edit.toggled.connect(_toggle_edit)
        view_menu.addAction(act_toggle_edit)
        
        view_menu.addSeparator()
        
        act_show_log = QAction("Show &Log", self)
        act_show_log.setShortcut("Meta+L")
        def _show_log():
            if hasattr(self, 'right_tabs'):
                self.right_tabs.setCurrentIndex(1)  # Switch to log tab
        act_show_log.triggered.connect(_show_log)
        view_menu.addAction(act_show_log)
        
        act_show_preview = QAction("Show &Preview", self)
        act_show_preview.setShortcut("Meta+P")
        def _show_preview():
            if hasattr(self, 'right_tabs'):
                self.right_tabs.setCurrentIndex(0)  # Switch to preview tab
        act_show_preview.triggered.connect(_show_preview)
        view_menu.addAction(act_show_preview)

        # Tools menu
        tools_menu = mb.addMenu("&Tools")
        
        act_settings_folders = QAction("&Choose Input Folder…", self)
        act_settings_folders.triggered.connect(self.choose_input_folder)
        tools_menu.addAction(act_settings_folders)
        
        act_settings_output = QAction("Choose &Output Folder…", self)
        act_settings_output.triggered.connect(self.choose_output_folder)
        tools_menu.addAction(act_settings_output)
        
        tools_menu.addSeparator()
        
        # Preferences in File menu for macOS convention
        file_menu.addSeparator()
        act_prefs = QAction("&Preferences…", self)
        act_prefs.setShortcut("Meta+,")
        if MenuRole is not None:
            try:
                act_prefs.setMenuRole(MenuRole.PreferencesRole)
            except Exception:
                pass
        act_prefs.triggered.connect(self._show_preferences)
        file_menu.addAction(act_prefs)

        file_menu.addSeparator()

        # Quit action
        act_quit = QAction("&Quit FicheGen", self)
        act_quit.setShortcut("Meta+Q")
        if MenuRole is not None:
            try:
                act_quit.setMenuRole(MenuRole.QuitRole)
            except Exception:
                pass
        act_quit.triggered.connect(self.close)
        file_menu.addAction(act_quit)
        # Window menu (macOS standard)
        window_menu = mb.addMenu("&Window")
        
        act_minimize = QAction("&Minimize", self)
        act_minimize.setShortcut("Meta+M")
        act_minimize.triggered.connect(self.showMinimized)
        window_menu.addAction(act_minimize)
        
        act_zoom = QAction("&Zoom", self)
        def _toggle_zoom():
            if self.isMaximized():
                self.showNormal()
            else:
                self.showMaximized()
        act_zoom.triggered.connect(_toggle_zoom)
        window_menu.addAction(act_zoom)

        # Help menu
        help_menu = mb.addMenu("&Help")
        
        act_help = QAction("&FicheGen Help", self)
        act_help.setShortcut("Meta+?")
        act_help.triggered.connect(self._show_api_help)  # Reuse existing help
        help_menu.addAction(act_help)
        
        help_menu.addSeparator()
        
        # About action
        act_about = QAction("&About FicheGen", self)
        if PYQT6:
            try:
                from PyQt6.QtGui import QAction
                MenuRole = QAction.MenuRole
                act_about.setMenuRole(MenuRole.AboutRole)
            except Exception:
                pass
        act_about.triggered.connect(self._show_about)
        help_menu.addAction(act_about)

    def _install_toolbar(self):
        # Toolbar removed as per design: redundant with main controls
        return

    def _openrouter_ui_enabled(self) -> bool:
        return self.settings.value("enable_openrouter_ui", "false") == "true"

    def _filter_openrouter_mentions(self, content: str) -> str:
        if self._openrouter_ui_enabled():
            return content
        filtered_lines = []
        for line in content.splitlines():
            if "openrouter" in line.lower():
                continue
            filtered_lines.append(line)
        sanitized = "\n".join(filtered_lines)
        # Tidy obvious leftover blank lines in long HTML strings.
        return re.sub(r"\n{3,}", "\n\n", sanitized)

    def _show_preferences(self):
        """Show the preferences dialog."""
        try:
            # Create the preferences dialog
            dialog = PreferencesDialog(self)
            
            # Load current settings into the dialog
            dialog.load_from_settings(self.settings)
            
            # Show the dialog and handle the result
            if PYQT6:
                result = dialog.exec()
                accepted = result == QtWidgets.QDialog.DialogCode.Accepted
            else:
                result = dialog.exec_()
                accepted = result == QtWidgets.QDialog.Accepted
            
            if accepted:
                # Save the settings from the dialog
                dialog.save_to_settings(self.settings)
                # Sync the main window with the new settings
                self._sync_from_preferences()
                
        except Exception as e:
            print(f"ERROR in _show_preferences: {e}")
            import traceback
            traceback.print_exc()
            # Show a simple message box instead of crashing
            QtWidgets.QMessageBox.warning(
                self, 
                "Preferences Error", 
                f"Failed to open preferences dialog:\n{e}\n\nPlease try again."
            )

    def _sync_from_preferences(self):
        """Sync main window controls from preferences"""
        # Reload API keys from settings
        load_api_keys_from_settings()
        # Update any controls that might be affected by preferences
        # Apply compact sidebar spacing immediately
        try:
            compact = self.settings.value("ui_compact_sidebar", "false") == "true"
            if getattr(self, "_left_sidebar_layout", None):
                self._left_sidebar_layout.setSpacing(8 if compact else 16)
        except Exception:
            pass
        
        # Refresh available lessons in case input directory changed
        self._load_available_lessons()

    def _load_available_lessons(self):
        """Load available lessons from toc_cache in the input directory for the selected class."""
        try:
            guides_dir = self.settings.value("input_dir", DEFAULT_INPUT_DIR)
            if not guides_dir or not os.path.isdir(guides_dir):
                self.lesson_selector_combo.setVisible(False)
                return
            
            toc_cache_dir = os.path.join(guides_dir, "toc_cache")
            if not os.path.isdir(toc_cache_dir):
                self.lesson_selector_combo.setVisible(False)
                return
            
            # Get current class level
            current_class = self.class_combo.currentText().lower()
            
            # Look for JSON file matching the class level
            target_json = f"guide_pedagogique_{current_class}.pdf.json"
            json_path = os.path.join(toc_cache_dir, target_json)
            
            if not os.path.exists(json_path):
                # No JSON for this class level - hide dropdown
                self.lesson_selector_combo.setVisible(False)
                self.topic_edit.setPlaceholderText("e.g., Les 5 sens")
                return
            
            # Load lessons from the specific class JSON file
            try:
                with open(json_path, 'r', encoding='utf-8') as f:
                    toc_data = json.load(f)
                    if isinstance(toc_data, list):
                        lessons = []
                        for item in toc_data:
                            if isinstance(item, dict) and 'topic' in item:
                                lesson_title = item['topic'].strip()
                                if lesson_title:
                                    lessons.append(lesson_title)
                        
                        # Populate dropdown with lessons in their original page order
                        if lessons:
                            self.lesson_selector_combo.clear()
                            self.lesson_selector_combo.addItem("-- Select a lesson --")
                            self.lesson_selector_combo.addItems(lessons)
                            self.lesson_selector_combo.setVisible(True)
                            
                            # Update placeholder text to indicate dropdown is available
                            self.topic_edit.setPlaceholderText("e.g., Les 5 sens (or select from dropdown above)")
                        else:
                            self.lesson_selector_combo.setVisible(False)
                            self.topic_edit.setPlaceholderText("e.g., Les 5 sens")
                    else:
                        self.lesson_selector_combo.setVisible(False)
                        self.topic_edit.setPlaceholderText("e.g., Les 5 sens")
            except (json.JSONDecodeError, IOError):
                self.lesson_selector_combo.setVisible(False)
                self.topic_edit.setPlaceholderText("e.g., Les 5 sens")
                
        except Exception as e:
            # Silently fail - lesson selection is optional
            self.lesson_selector_combo.setVisible(False)
            self.topic_edit.setPlaceholderText("e.g., Les 5 sens")

    def _on_lesson_selected(self, lesson_title):
        """Handle lesson selection from dropdown."""
        if lesson_title and lesson_title != "-- Select a lesson --":
            self.topic_edit.setText(lesson_title)

    def _on_class_changed(self):
        """Reload lessons when class level changes."""
        self._load_available_lessons()
        if hasattr(self, 'eval_class_combo') and isinstance(self.eval_class_combo, QtWidgets.QComboBox):
            if self.eval_class_combo.currentText() != self.class_combo.currentText():
                self.eval_class_combo.blockSignals(True)
                self.eval_class_combo.setCurrentText(self.class_combo.currentText())
                self.eval_class_combo.blockSignals(False)
            self._load_eval_lessons()

    def _on_eval_class_changed(self):
        """Reload evaluation lesson list when class changes."""
        self._load_eval_lessons()

    def _append_eval_topic(self, topic: str):
        topic = (topic or "").strip()
        if not topic:
            return
        existing_lines = [line.strip() for line in self.eval_topics_edit.toPlainText().splitlines() if line.strip()]
        if topic not in existing_lines:
            existing_lines.append(topic)
            self.eval_topics_edit.setPlainText("\n".join(existing_lines))

    def _on_eval_add_selected_lessons(self):
        selected_titles = [item.text().strip() for item in self.eval_lessons_list.selectedItems() if item.text().strip()]
        if not selected_titles:
            return
        for title in selected_titles:
            self._append_eval_topic(title)

    def _load_eval_lessons(self):
        if not hasattr(self, 'eval_lessons_list'):
            return

        self.eval_lessons_list.clear()
        guides_dir = self.settings.value("input_dir", DEFAULT_INPUT_DIR)
        if not guides_dir or not os.path.isdir(guides_dir):
            self.eval_lessons_list.addItem("No guides directory configured")
            self.eval_lessons_list.item(0).setFlags(QtCore.Qt.ItemFlag.NoItemFlags)
            return

        toc_cache_dir = os.path.join(guides_dir, "toc_cache")
        if not os.path.isdir(toc_cache_dir):
            self.eval_lessons_list.addItem("Generate fiches at least once to build ToC cache")
            self.eval_lessons_list.item(0).setFlags(QtCore.Qt.ItemFlag.NoItemFlags)
            return

        class_level = self.eval_class_combo.currentText().lower()
        candidates = [f"guide_pedagogique_{class_level}.pdf.json"]
        if class_level == "6e":
            candidates.append("guide_pedagogique_6eme.pdf.json")

        toc_data = None
        for candidate in candidates:
            json_path = os.path.join(toc_cache_dir, candidate)
            if os.path.exists(json_path):
                try:
                    with open(json_path, "r", encoding="utf-8") as f:
                        toc_data = json.load(f)
                        if isinstance(toc_data, list):
                            break
                except (json.JSONDecodeError, IOError):
                    toc_data = None

        if not toc_data:
            self.eval_lessons_list.addItem("No cached ToC for this class yet")
            self.eval_lessons_list.item(0).setFlags(QtCore.Qt.ItemFlag.NoItemFlags)
            return

        for entry in toc_data:
            if isinstance(entry, dict):
                title = (entry.get("topic") or "").strip()
                if title:
                    self.eval_lessons_list.addItem(title)

    def _set_rating_enabled(self, enabled: bool):
        self.rating_label.setEnabled(enabled)
        self.rating_combo.setEnabled(enabled)
        self.rating_btn.setEnabled(enabled)

    def _on_provider_change(self, provider_text):
        """This is now handled in preferences dialog"""
        pass

    # Help system methods
    def _show_help_dialog(self, title: str, content: str):
        """Show a help dialog with formatted content"""
        dialog = QtWidgets.QDialog(self)
        dialog.setWindowTitle(title)
        dialog.setModal(True)
        dialog.resize(800, 600)
        
        layout = QtWidgets.QVBoxLayout(dialog)
        
        # Scrollable text area
        scroll = QtWidgets.QScrollArea()
        text_widget = QtWidgets.QTextEdit()
        text_widget.setHtml(self._filter_openrouter_mentions(content))
        text_widget.setReadOnly(True)
        scroll.setWidget(text_widget)
        scroll.setWidgetResizable(True)
        layout.addWidget(scroll)
        
        # Close button
        button_box = QtWidgets.QDialogButtonBox(QtWidgets.QDialogButtonBox.StandardButton.Ok)
        button_box.accepted.connect(dialog.accept)
        layout.addWidget(button_box)
        
        dialog.exec()

    def _show_user_guide(self):
        """Show comprehensive user guide"""
        content = """
        <h1>📚 Guide d'utilisation FicheGen</h1>
        
        <h2>🚀 Démarrage rapide</h2>
        <p><strong>FicheGen</strong> est un assistant intelligent qui génère automatiquement des fiches pédagogiques 
        à partir des guides pédagogiques du Maroc. Voici comment l'utiliser :</p>
        
        <h3>1. Configuration initiale</h3>
        <ul>
            <li><strong>Clés API :</strong> Allez dans <em>Préférences > AI & Models</em> et ajoutez vos clés API
                <ul>
                    <li>OpenRouter : pour accéder à plusieurs modèles gratuits</li>
                    <li>Gemini : pour l'accès gratuit aux modèles Google</li>
                </ul>
            </li>
            <li><strong>Dossiers :</strong> Configurez vos dossiers dans <em>Préférences > Folders</em>
                <ul>
                    <li>Dossier guides : où se trouvent vos PDFs de guides pédagogiques</li>
                    <li>Dossier sortie : où seront sauvegardées vos fiches générées</li>
                </ul>
            </li>
        </ul>
        
        <h3>2. Génération d'une fiche</h3>
        <ol>
            <li><strong>Sélectionnez la classe :</strong> CP, CE1, CE2, CM1, CM2, 6e, etc.</li>
            <li><strong>Entrez le sujet :</strong> Exemple : "Le cycle de l'eau", "Les fractions"</li>
            <li><strong>Choisissez la matière :</strong> Sciences, Mathématiques, Français, etc.</li>
            <li><strong>Définissez la durée :</strong> Par défaut 45 minutes, ajustable</li>
            <li><strong>Cliquez sur "Générer" :</strong> Ou utilisez Cmd+Entrée</li>
        </ol>
        
        <h3>3. Révision et sauvegarde</h3>
        <ul>
            <li><strong>Aperçu :</strong> Visualisez votre fiche dans l'onglet "Preview"</li>
            <li><strong>Modification :</strong> Cochez "✏️ Edit Markdown" pour modifier directement</li>
            <li><strong>Évaluation :</strong> Notez la qualité de la fiche (1-5 étoiles)</li>
            <li><strong>Sauvegarde :</strong> 
                <ul>
                    <li>PDF : Cmd+S (plusieurs thèmes disponibles)</li>
                    <li>DOCX : Shift+Cmd+S (format Word)</li>
                </ul>
            </li>
        </ul>
        
        <h2>📝 Structure des fiches générées</h2>
        <p>Chaque fiche contient :</p>
        <ul>
            <li><strong>Informations générales :</strong> Titre, classe, durée, matière</li>
            <li><strong>Objectifs pédagogiques :</strong> 2-3 objectifs précis</li>
            <li><strong>Déroulement :</strong> Phases détaillées avec timings</li>
            <li><strong>Évaluation :</strong> Méthodes d'évaluation proposées</li>
            <li><strong>Conclusion :</strong> Résumé pour les élèves</li>
        </ul>
        
        <h2>🎯 Conseils pour de meilleurs résultats</h2>
        <ul>
            <li><strong>Soyez précis :</strong> "Les triangles isocèles" plutôt que "géométrie"</li>
            <li><strong>Vérifiez l'orthographe :</strong> L'app corrige automatiquement les erreurs courantes</li>
            <li><strong>Utilisez les évaluations :</strong> Notez vos fiches pour améliorer les suggestions futures</li>
            <li><strong>Explorez les thèmes PDF :</strong> Normal, Professionnel, Esthétique, etc.</li>
        </ul>
        
        <h2>⚙️ Raccourcis clavier</h2>
        <ul>
            <li><strong>Cmd+Entrée :</strong> Générer une fiche</li>
            <li><strong>Échap :</strong> Annuler la génération</li>
            <li><strong>Cmd+S :</strong> Sauvegarder en PDF</li>
            <li><strong>Shift+Cmd+S :</strong> Sauvegarder en DOCX</li>
            <li><strong>Cmd+, :</strong> Ouvrir les préférences</li>
            <li><strong>Cmd+Retour arrière :</strong> Effacer l'aperçu</li>
        </ul>
        """
        self._show_help_dialog("Guide d'utilisation", content)

    def _show_advanced_features(self):
        """Show advanced features documentation"""
        content = """
        <h1>🔧 Fonctionnalités avancées</h1>
        
        <h2>📖 Système de Table des Matières (ToC)</h2>
        <p>FicheGen analyse automatiquement les guides pédagogiques pour extraire les pages correspondantes à votre sujet.</p>
        
        <h3>Cache intelligent</h3>
        <ul>
            <li><strong>Première analyse :</strong> L'app utilise l'IA pour analyser la table des matières</li>
            <li><strong>Mise en cache :</strong> Les résultats sont sauvegardés dans <code>guides/toc_cache/</code></li>
            <li><strong>Réutilisation :</strong> Les analyses suivantes sont instantanées</li>
            <li><strong>Détection d'offset :</strong> Correction automatique de la numérotation des pages</li>
        </ul>
        
        <h3>Pages manuelles</h3>
        <ul>
            <li><strong>Format simple :</strong> <code>42</code> pour une page unique</li>
            <li><strong>Format plage :</strong> <code>42-46</code> pour plusieurs pages</li>
            <li><strong>Format multiple :</strong> <code>42,45,48-50</code> pour des pages non-consécutives</li>
        </ul>
        
        <h2>🎨 Thèmes PDF avancés</h2>
        
        <h3>Normal</h3>
        <ul>
            <li>Thème élégant et neutre</li>
            <li>Parfait pour un usage quotidien</li>
            <li>Couleurs sobres, lisibilité optimale</li>
        </ul>
        
        <h3>Professionnel</h3>
        <ul>
            <li>Style corporate avec hiérarchie marquée</li>
            <li>Marges larges, typographie moderne</li>
            <li>Idéal pour les présentations officielles</li>
        </ul>
        
        <h3>Esthétique</h3>
        <ul>
            <li>Design vibrant et créatif</li>
            <li>Espacement dramatique</li>
            <li>Parfait pour captiver l'attention</li>
        </ul>
        
        <h3>Minimal Pro</h3>
        <ul>
            <li>Ultra-épuré avec beaucoup d'espace blanc</li>
            <li>Lignes ultra-fines</li>
            <li>Style moderne et minimaliste</li>
        </ul>
        
        <h3>Classic Serif</h3>
        <ul>
            <li>Thème académique traditionnel</li>
            <li>Police serif pour un rendu formel</li>
            <li>Marges académiques standards</li>
        </ul>
        
        <h2>🤖 Système d'IA multi-modèles</h2>
        
        <h3>Fallback intelligent</h3>
        <p>Si votre modèle principal échoue, l'app essaie automatiquement :</p>
        <ol>
            <li>Votre modèle sélectionné</li>
            <li>Les autres modèles OpenRouter disponibles</li>
            <li>Gemini (si la clé est configurée)</li>
        </ol>
        
        <h3>Modèles spécialisés</h3>
        <ul>
            <li><strong>Table des matières :</strong> Gemini 2.5 Flash</li>
            <li><strong>Correction syntaxe :</strong> Gemma 3-27B</li>
            <li><strong>Détection offset :</strong> Gemini 2.5 Flash Lite</li>
            <li><strong>Génération fiches :</strong> Modèle de votre choix</li>
        </ul>
        
        <h2>📊 Système d'évaluation et apprentissage</h2>
        
        <h3>Évaluations</h3>
        <ul>
            <li><strong>1-2 étoiles :</strong> Fiche de faible qualité</li>
            <li><strong>3 étoiles :</strong> Fiche correcte</li>
            <li><strong>4-5 étoiles :</strong> Excellente fiche</li>
        </ul>
        
        <h3>Amélioration continue</h3>
        <ul>
            <li>Les fiches bien notées deviennent des exemples de style</li>
            <li>L'IA apprend de vos préférences</li>
            <li>Qualité progressivement améliorée</li>
        </ul>
        
        <h2>📄 Formats d'export</h2>
        
        <h3>PDF</h3>
        <ul>
            <li>Rendu professionnel avec thèmes</li>
            <li>Métadonnées automatiques</li>
            <li>Optimisé pour l'impression</li>
        </ul>
        
        <h3>DOCX</h3>
        <ul>
            <li>Compatible Microsoft Word</li>
            <li>Modification facile</li>
            <li>Partage collaboratif</li>
        </ul>
        
        <h2>⚡ Instructions spéciales</h2>
        <p>Dans <em>Préférences > Avancé</em>, vous pouvez ajouter des instructions personnalisées :</p>
        <ul>
            <li>"Pas d'activités de groupe, privilégier le travail individuel"</li>
            <li>"Ne pas suggérer de vidéos"</li>
            <li>"Adapter pour des élèves en difficulté"</li>
            <li>"Intégrer plus d'exemples concrets"</li>
        </ul>
        """
        self._show_help_dialog("Fonctionnalités avancées", content)

    def _show_api_help(self):
        """Show API configuration help"""
        content = """
        <h1>🔑 Configuration des clés API</h1>
        
        <h2>📋 Vue d'ensemble</h2>
        <p>FicheGen utilise des services d'intelligence artificielle pour analyser vos guides pédagogiques et générer des fiches. 
        Vous avez besoin d'au moins une clé API pour utiliser l'application.</p>
        
        <h2>🌟 OpenRouter (Recommandé)</h2>
        
        <h3>Avantages</h3>
        <ul>
            <li><strong>Modèles gratuits :</strong> Accès à Gemma, DeepSeek, Mistral, Llama</li>
            <li><strong>Diversité :</strong> Plusieurs modèles pour différents besoins</li>
            <li><strong>Fiabilité :</strong> Service stable et rapide</li>
            <li><strong>Pas de limite stricte :</strong> Usage généreux pour les modèles gratuits</li>
        </ul>
        
        <h3>Comment obtenir votre clé</h3>
        <ol>
            <li>Allez sur <a href="https://openrouter.ai/">openrouter.ai</a></li>
            <li>Créez un compte (gratuit)</li>
            <li>Allez dans <em>Keys</em> dans votre tableau de bord</li>
            <li>Cliquez sur <em>Create Key</em></li>
            <li>Copiez la clé et collez-la dans FicheGen</li>
        </ol>
        
        <h3>Modèles gratuits disponibles</h3>
        <ul>
            <li><strong>Google Gemma 2 27B :</strong> Excellent pour l'éducation</li>
            <li><strong>DeepSeek Chat V3.1 :</strong> Très créatif</li>
            <li><strong>DeepSeek R1 :</strong> Raisonnement avancé</li>
            <li><strong>Mistral Small :</strong> Rapide et efficace</li>
            <li><strong>Llama 4 Scout :</strong> Dernière génération Meta</li>
        </ul>
        
        <h2>🧠 Google Gemini</h2>
        
        <h3>Avantages</h3>
        <ul>
            <li><strong>Gratuit :</strong> Quota généreux sans carte de crédit</li>
            <li><strong>Rapide :</strong> Réponses très rapides</li>
            <li><strong>Français natif :</strong> Excellent en français</li>
            <li><strong>Analyse PDF :</strong> Optimisé pour l'analyse de documents</li>
        </ul>
        
        <h3>Comment obtenir votre clé</h3>
        <ol>
            <li>Allez sur <a href="https://ai.google.dev/">ai.google.dev</a></li>
            <li>Connectez-vous avec votre compte Google</li>
            <li>Cliquez sur <em>Get API Key</em></li>
            <li>Créez un nouveau projet ou utilisez un existant</li>
            <li>Copiez la clé et collez-la dans FicheGen</li>
        </ol>
        
        <h2>⚙️ Configuration dans FicheGen</h2>
        
        <h3>Méthode 1 : Préférences (Recommandée)</h3>
        <ol>
            <li>Ouvrez <em>Préférences</em> (Cmd+,)</li>
            <li>Allez dans l'onglet <em>AI & Models</em></li>
            <li>Collez vos clés dans les champs appropriés</li>
            <li>Cliquez sur l'œil 👁 pour vérifier</li>
            <li>Cliquez <em>OK</em> pour sauvegarder</li>
        </ol>
        
        <h3>Méthode 2 : Variables d'environnement</h3>
        <p>Ajoutez dans votre terminal :</p>
        <pre>
export OPENROUTER_API_KEY="votre_clé_ici"
export GEMINI_API_KEY="votre_clé_ici"
        </pre>
        
        <h3>Méthode 3 : Fichier keys.txt</h3>
        <p>Créez un fichier <code>keys.txt</code> dans le dossier de l'app :</p>
        <pre>
OPENROUTER_API_KEY=votre_clé_ici
GEMINI_API_KEY=votre_clé_ici
        </pre>
        
        <h2>🔒 Sécurité</h2>
        <ul>
            <li><strong>Stockage local :</strong> Vos clés restent sur votre ordinateur</li>
            <li><strong>Chiffrement système :</strong> Utilise le trousseau macOS</li>
            <li><strong>Aucun partage :</strong> Vos clés ne sont jamais transmises à FicheGen</li>
            <li><strong>Révocation :</strong> Vous pouvez révoquer vos clés à tout moment</li>
        </ul>
        
        <h2>💡 Conseils</h2>
        <ul>
            <li><strong>Deux clés :</strong> Configurez OpenRouter ET Gemini pour plus de fiabilité</li>
            <li><strong>Test :</strong> Générée une fiche de test après configuration</li>
            <li><strong>Quotas :</strong> Surveillez votre usage sur les plateformes</li>
            <li><strong>Sauvegarde :</strong> Notez vos clés dans un gestionnaire de mots de passe</li>
        </ul>
        
        <h2>❌ Résolution de problèmes</h2>
        <ul>
            <li><strong>"Clé manquante" :</strong> Vérifiez que la clé est bien saisie</li>
            <li><strong>"Quota dépassé" :</strong> Attendez la réinitialisation ou changez de modèle</li>
            <li><strong>"Erreur réseau" :</strong> Vérifiez votre connexion internet</li>
            <li><strong>"Modèle indisponible" :</strong> Essayez un autre modèle</li>
        </ul>
        """
        self._show_help_dialog("Configuration API", content)

    def _show_troubleshooting(self):
        """Show troubleshooting guide"""
        content = """
        <h1>🔧 Résolution de problèmes</h1>
        
        <h2>🚨 Problèmes fréquents</h2>
        
        <h3>❌ "Impossible de trouver le guide pour [classe]"</h3>
        <p><strong>Cause :</strong> Le fichier PDF n'est pas trouvé dans le dossier guides.</p>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Vérifiez que le fichier existe : <code>guide_pedagogique_cm2.pdf</code></li>
            <li>Respectez la nomenclature : <code>guide_pedagogique_[classe].pdf</code></li>
            <li>Pour la 6ème : <code>guide_pedagogique_6e.pdf</code> ou <code>guide_pedagogique_6eme.pdf</code></li>
            <li>Vérifiez le dossier dans <em>Préférences > Folders</em></li>
        </ul>
        
        <h3>🔑 "Clé API manquante ou invalide"</h3>
        <p><strong>Cause :</strong> Problème de configuration des clés API.</p>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Vérifiez vos clés dans <em>Préférences > AI & Models</em></li>
            <li>Cliquez sur l'œil 👁 pour révéler et vérifier</li>
            <li>Régénérez une nouvelle clé sur la plateforme</li>
            <li>Testez avec un autre fournisseur (OpenRouter ↔ Gemini)</li>
        </ul>
        
        <h3>📄 "Erreur d'extraction PDF"</h3>
        <p><strong>Cause :</strong> Le PDF est corrompu ou protégé.</p>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Vérifiez que le PDF s'ouvre normalement</li>
            <li>Essayez de le ré-enregistrer avec un autre outil</li>
            <li>Vérifiez qu'il n'est pas protégé par mot de passe</li>
            <li>Utilisez un PDF plus récent ou de meilleure qualité</li>
        </ul>
        
        <h3>🧠 "Aucune page trouvée pour le sujet"</h3>
        <p><strong>Cause :</strong> L'IA n'arrive pas à localiser le sujet dans la table des matières.</p>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Vérifiez l'orthographe du sujet</li>
            <li>Utilisez le titre exact du guide</li>
            <li>Essayez des variantes : "fractions" → "les fractions"</li>
            <li>Spécifiez les pages manuellement : <code>42-46</code></li>
        </ul>
        
        <h3>⚡ "Génération lente ou qui plante"</h3>
        <p><strong>Causes possibles :</strong></p>
        <ul>
            <li>Connexion internet lente</li>
            <li>Modèle surchargé</li>
            <li>PDF très volumineux</li>
        </ul>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Changez de modèle (essayez Gemini)</li>
            <li>Réduisez la température (plus déterministe)</li>
            <li>Spécifiez des pages précises plutôt que l'auto-détection</li>
            <li>Redémarrez l'application</li>
        </ul>
        
        <h2>💾 Problèmes de sauvegarde</h2>
        
        <h3>🚫 "Permission refusée"</h3>
        <p><strong>Cause :</strong> Problème de droits d'écriture.</p>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Changez le dossier de sortie vers Documents</li>
            <li>Vérifiez les permissions du dossier</li>
            <li>Évitez les dossiers système</li>
            <li>Créez un nouveau dossier dédié</li>
        </ul>
        
        <h3>📱 "Fichier non créé"</h3>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Vérifiez l'espace disque disponible</li>
            <li>Fermez d'autres applications utilisant le fichier</li>
            <li>Utilisez un nom de fichier plus simple</li>
            <li>Essayez un autre format (PDF → DOCX)</li>
        </ul>
        
        <h2>🌐 Problèmes réseau</h2>
        
        <h3>🔌 "Erreur de connexion"</h3>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Vérifiez votre connexion internet</li>
            <li>Désactivez temporairement VPN/proxy</li>
            <li>Vérifiez que les domaines ne sont pas bloqués :
                <ul>
                    <li>openrouter.ai</li>
                    <li>generativelanguage.googleapis.com</li>
                </ul>
            </li>
            <li>Essayez depuis un autre réseau</li>
        </ul>
        
        <h2>⚙️ Problèmes de performance</h2>
        
        <h3>🐌 Application lente</h3>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Fermez d'autres applications gourmandes</li>
            <li>Nettoyez le cache : supprimez le dossier <code>toc_cache</code></li>
            <li>Redémarrez l'application</li>
            <li>Vérifiez l'espace disque disponible</li>
        </ul>
        
        <h3>💾 Usage mémoire élevé</h3>
        <p><strong>Solutions :</strong></p>
        <ul>
            <li>Évitez d'ouvrir plusieurs gros PDFs simultanément</li>
            <li>Fermez l'onglet Preview entre les générations</li>
            <li>Redémarrez l'app périodiquement</li>
        </ul>
        
        <h2>🔄 Réinitialisation</h2>
        
        <h3>🗑️ Effacer les préférences</h3>
        <p>En cas de problème persistant :</p>
        <ol>
            <li>Fermez FicheGen</li>
            <li>Ouvrez Terminal</li>
            <li>Tapez : <code>defaults delete com.FicheGen.Pedago</code></li>
            <li>Relancez FicheGen</li>
        </ol>
        
        <h3>🗂️ Nettoyer le cache</h3>
        <p>Pour forcer une nouvelle analyse des guides :</p>
        <ul>
            <li>Supprimez le dossier <code>guides/toc_cache/</code></li>
            <li>La prochaine génération recréera le cache</li>
        </ul>
        
        <h2>📞 Obtenir de l'aide</h2>
        <p>Si le problème persiste :</p>
        <ul>
            <li>Consultez les logs dans l'onglet "Logs"</li>
            <li>Notez le message d'erreur exact</li>
            <li>Essayez avec un guide et un sujet différents</li>
            <li>Redémarrez l'application</li>
        </ul>
        """
        self._show_help_dialog("Résolution de problèmes", content)

    def _show_tips(self):
        """Show tips and tricks"""
        content = """
        <h1>💡 Conseils et astuces</h1>
        
        <h2>🎯 Optimiser la qualité des fiches</h2>
        
        <h3>📝 Sujets bien formulés</h3>
        <ul>
            <li><strong>Spécifique :</strong> "Les triangles isocèles" > "géométrie"</li>
            <li><strong>Correct :</strong> "Le cycle de l'eau" > "cycle eau"</li>
            <li><strong>Complet :</strong> "La multiplication des nombres décimaux" > "multiplication"</li>
            <li><strong>Naturel :</strong> Comme dans le guide pédagogique</li>
        </ul>
        
        <h3>⏱️ Durées réalistes</h3>
        <ul>
            <li><strong>CP-CE1 :</strong> 30-40 minutes</li>
            <li><strong>CE2-CM1 :</strong> 45-50 minutes</li>
            <li><strong>CM2-6e :</strong> 50-60 minutes</li>
            <li><strong>Matières pratiques :</strong> +10 minutes (manipulation)</li>
        </ul>
        
        <h3>📚 Matières précises</h3>
        <ul>
            <li><strong>Évitez :</strong> "Général", "Divers"</li>
            <li><strong>Préférez :</strong> "Sciences", "Mathématiques", "Français"</li>
            <li><strong>Spécialisez :</strong> "Géométrie", "Grammaire", "Histoire"</li>
        </ul>
        
        <h2>⚡ Astuces de productivité</h2>
        
        <h3>⌨️ Raccourcis indispensables</h3>
        <ul>
            <li><strong>Cmd+Entrée :</strong> Génération rapide</li>
            <li><strong>Cmd+S :</strong> Sauvegarde PDF instantanée</li>
            <li><strong>Cmd+, :</strong> Préférences rapides</li>
            <li><strong>Échap :</strong> Annulation d'urgence</li>
        </ul>
        
        <h3>🔄 Workflow optimisé</h3>
        <ol>
            <li><strong>Batch :</strong> Préparez plusieurs sujets à la suite</li>
            <li><strong>Thème :</strong> Choisissez un thème PDF par défaut</li>
            <li><strong>Évaluation :</strong> Notez immédiatement après génération</li>
            <li><strong>Organisation :</strong> Créez des sous-dossiers par matière</li>
        </ol>
        
        <h3>📁 Organisation des fichiers</h3>
        <pre>
Documents/
├── FicheGen/
│   ├── guides/
│   │   ├── guide_pedagogique_cp.pdf
│   │   ├── guide_pedagogique_ce1.pdf
│   │   └── toc_cache/ (automatique)
│   └── fiches/
│       ├── Sciences/
│       ├── Mathematiques/
│       └── Francais/
        </pre>
        
        <h2>🎨 Personnalisation avancée</h2>
        
        <h3>🖨️ Choix du thème PDF</h3>
        <ul>
            <li><strong>Usage quotidien :</strong> Normal</li>
            <li><strong>Inspection :</strong> Professionnel</li>
            <li><strong>Cours ouverts :</strong> Esthétique</li>
            <li><strong>Travail personnel :</strong> Minimal Pro</li>
            <li><strong>Document officiel :</strong> Classic Serif</li>
        </ul>
        
        <h3>✏️ Édition Markdown</h3>
        <p>Activez "✏️ Edit Markdown" pour :</p>
        <ul>
            <li><strong>Titres :</strong> <code># Titre principal</code>, <code>## Sous-titre</code></li>
            <li><strong>Listes :</strong> <code>- Point</code> ou <code>1. Numéroté</code></li>
            <li><strong>Phases :</strong> <code>### Découverte (10 min)</code></li>
            <li><strong>Emphase :</strong> <code>**gras**</code>, <code>*italique*</code></li>
        </ul>
        
        <h2>🤖 Maîtriser les modèles IA</h2>
        
        <h3>🎛️ Température</h3>
        <ul>
            <li><strong>0.0-0.3 :</strong> Très structuré, prévisible</li>
            <li><strong>0.4-0.6 :</strong> Équilibré (recommandé)</li>
            <li><strong>0.7-1.0 :</strong> Créatif, varié</li>
        </ul>
        
        <h3>🔄 Modèles par usage</h3>
        <ul>
            <li><strong>Gemini :</strong> Rapide, français excellent</li>
            <li><strong>Gemma 2 27B :</strong> Éducation, très structuré</li>
            <li><strong>DeepSeek :</strong> Créatif, approches originales</li>
            <li><strong>Mistral :</strong> Équilibré, fiable</li>
        </ul>
        
        <h3>📋 Instructions spéciales efficaces</h3>
        <ul>
            <li><strong>Adaptations :</strong> "Élèves en difficulté", "Classe nombreuse"</li>
            <li><strong>Contraintes :</strong> "Pas de matériel spécialisé", "Sans vidéo"</li>
            <li><strong>Style :</strong> "Plus d'exemples concrets", "Approche ludique"</li>
            <li><strong>Format :</strong> "Activités courtes", "Moins de théorie"</li>
        </ul>
        
        <h2>📊 Système d'évaluation</h2>
        
        <h3>⭐ Guide de notation</h3>
        <ul>
            <li><strong>5 étoiles :</strong> Fiche parfaite, utilisable directement</li>
            <li><strong>4 étoiles :</strong> Très bonne, modifications mineures</li>
            <li><strong>3 étoiles :</strong> Correcte, quelques ajustements</li>
            <li><strong>2 étoiles :</strong> Utilisable mais nécessite du travail</li>
            <li><strong>1 étoile :</strong> Inadéquate, à refaire</li>
        </ul>
        
        <h3>🔄 Amélioration continue</h3>
        <ul>
            <li>Les fiches 4-5⭐ deviennent des exemples de style</li>
            <li>L'IA apprend progressivement vos préférences</li>
            <li>Notez même les fiches imparfaites pour l'apprentissage</li>
        </ul>
        
        <h2>⚠️ Pièges à éviter</h2>
        
        <h3>🚫 Erreurs communes</h3>
        <ul>
            <li><strong>Sujet trop vague :</strong> "sciences" → précisez "le système solaire"</li>
            <li><strong>Classe incorrecte :</strong> Vérifiez que le guide correspond</li>
            <li><strong>Durée irréaliste :</strong> 20 min ou 90 min sont problématiques</li>
            <li><strong>Pages incorrectes :</strong> Vérifiez avant de confirmer</li>
        </ul>
        
        <h3>⚡ Optimisations</h3>
        <ul>
            <li><strong>Cache :</strong> Ne supprimez pas toc_cache sans raison</li>
            <li><strong>Réseau :</strong> Générez par lots pour économiser les appels API</li>
            <li><strong>Qualité :</strong> Préférez la précision à la vitesse</li>
        </ul>
        
        <h2>🎓 Cas d'usage avancés</h2>
        
        <h3>👥 Travail collaboratif</h3>
        <ul>
            <li>Exportez en DOCX pour partage/modification</li>
            <li>Standardisez les thèmes PDF par équipe</li>
            <li>Partagez les instructions spéciales communes</li>
        </ul>
        
        <h3>📈 Préparation d'inspection</h3>
        <ul>
            <li>Utilisez le thème "Professionnel"</li>
            <li>Activez les banners métadonnées</li>
            <li>Vérifiez la conformité au guide officiel</li>
            <li>Préparez plusieurs fiches d'avance</li>
        </ul>
        
        <h3>🔄 Adaptation rapide</h3>
        <ul>
            <li>Copiez/modifiez une fiche existante</li>
            <li>Changez la classe et régénérez</li>
            <li>Ajustez la durée selon le niveau</li>
            <li>Personnalisez via instructions spéciales</li>
        </ul>
        """
        self._show_help_dialog("Conseils et astuces", content)

    def _show_about(self):
        """Show about dialog"""
        content = """
        <div style="text-align: center; padding: 20px;">
            <h1>🎓 FicheGen</h1>
            <h2>Générateur intelligent de fiches pédagogiques</h2>
            
            <p style="font-size: 18px; margin: 30px 0;"><strong>Version 1.0</strong></p>
            
            <div style="background: #f5f5f5; padding: 20px; border-radius: 8px; margin: 20px 0;">
                <h3>🎯 Mission</h3>
                <p>FicheGen transforme la préparation de cours en assistant les enseignants marocains 
                dans la création automatique de fiches pédagogiques de qualité professionnelle.</p>
            </div>
            
            <h3>✨ Fonctionnalités principales</h3>
            <ul style="text-align: left; max-width: 500px; margin: 0 auto;">
                <li><strong>Analyse intelligente</strong> des guides pédagogiques</li>
                <li><strong>Génération automatique</strong> de fiches structurées</li>
                <li><strong>Multiple formats</strong> d'export (PDF, DOCX)</li>
                <li><strong>Thèmes professionnels</strong> personnalisables</li>
                <li><strong>Cache intelligent</strong> pour performance optimale</li>
                <li><strong>Système d'évaluation</strong> et d'amélioration continue</li>
            </ul>
            
            <div style="background: #e8f4fd; padding: 20px; border-radius: 8px; margin: 20px 0;">
                <h3>🤖 Technologie IA</h3>
                <p>Propulsé par des modèles d'intelligence artificielle de pointe :</p>
                <ul style="text-align: left; max-width: 400px; margin: 0 auto;">
                    <li>Google Gemini 2.5 Flash</li>
                    <li>OpenRouter (Gemma, DeepSeek, Mistral, Llama)</li>
                    <li>Fallback intelligent multi-modèles</li>
                    <li>Spécialisation par tâche</li>
                </ul>
            </div>
            
            <h3>🎨 Interface</h3>
            <p>Interface native macOS avec PyQt6, optimisée pour la productivité 
            et l'expérience utilisateur moderne.</p>
            
            <div style="background: #f0f8f0; padding: 20px; border-radius: 8px; margin: 20px 0;">
                <h3>📚 Compatibilité</h3>
                <p><strong>Programmes marocains :</strong> CP, CE1, CE2, CM1, CM2, 6e, 5e, 4e, 3e<br>
                <strong>Formats :</strong> PDF et DOCX<br>
                <strong>Système :</strong> macOS 10.14+</p>
            </div>
            
            <h3>🔒 Confidentialité</h3>
            <p>Vos données et clés API restent strictement privées et locales. 
            Aucune information n'est collectée ou transmise.</p>
            
            <div style="margin-top: 40px; font-size: 14px; color: #666;">
                <p>Développé avec passion pour l'éducation marocaine 🇲🇦</p>
                <p>© 2025 FicheGen - Tous droits réservés</p>
            </div>
        </div>
        """
        self._show_help_dialog("À propos de FicheGen", content)

    def _on_model_toggle_changed(self):
        """Handle changes to the Gemini model toggle (Pro vs Flash)"""
        use_pro = self.model_toggle.isChecked()
        self.settings.setValue("gemini_use_pro", "true" if use_pro else "false")
        
        # Update the global model setting for immediate effect using configured models
        new_model = get_configured_pro_model() if use_pro else get_configured_flash_model()
        self.settings.setValue("advanced_gemini_model", new_model)
        
        # Provide user feedback with actual model name
        model_display = f"Pro ({get_configured_pro_model()})" if use_pro else f"Flash ({get_configured_flash_model()})"
        self.append_log(f"🔄 Switched to {model_display}")

    def _on_student_textbook_toggle(self, checked):
        """Handle changes to the student textbook toggle"""
        self.settings.setValue("use_student_textbook", "true" if checked else "false")
        status = "enabled" if checked else "disabled"
        self.append_log(f"📚 Student textbook extraction {status}")

    def _on_quick_preview_changed(self, checked):
        """Handle changes to the quick preview source setting"""
        self.settings.setValue("preview_source", "true" if checked else "false")
        status = "enabled" if checked else "disabled"
        self.append_log(f"🔄 Source preview {status}")

    def append_log(self, text):
        self.log_edit.appendPlainText(text)
        bar = self.log_edit.verticalScrollBar()
        bar.setValue(bar.maximum())
        if self.log_file_handle:
            try:
                self.log_file_handle.write(f"{datetime.now().isoformat()} | {text}\n")
                self.log_file_handle.flush()
            except Exception:
                pass

    def start_generation(self):
        """Start fiche generation with comprehensive validation and worker setup."""
        # Check if already running
        if self.worker is not None and self.worker.isRunning():
            QtWidgets.QMessageBox.information(
                self, 
                "Please Wait", 
                "An operation is already running. Cancel it first or wait for completion."
            )
            return
        
        # Validate inputs
        class_level = self.class_combo.currentText()
        topic = self.topic_edit.text().strip()
        
        if not topic:
            QtWidgets.QMessageBox.warning(
                self, 
                "Missing Topic", 
                "Please enter a lesson topic."
            )
            return
        self.current_topics_list = [topic]
        
        # Check API key availability
        if not API_KEYS.get("GEMINI_API_KEY"):
            reply = QtWidgets.QMessageBox.warning(
                self,
                "API Key Missing",
                "No Gemini API key configured for fiche generation.\nConfigure your Gemini key in Preferences.",
                QtWidgets.QMessageBox.StandardButton.Yes | QtWidgets.QMessageBox.StandardButton.No,
                QtWidgets.QMessageBox.StandardButton.Yes
            )
            if reply == QtWidgets.QMessageBox.StandardButton.Yes:
                self._show_preferences()
            return

        # Get settings from preferences
        guides_dir = self.settings.value("input_dir", DEFAULT_INPUT_DIR)
        textbook_dir = self.settings.value("textbook_dir", "") or None
        output_dir = self.settings.value("output_dir", DEFAULT_OUTPUT_DIR)
        
        # Validate directories
        if not os.path.isdir(guides_dir):
            reply = QtWidgets.QMessageBox.warning(
                self, 
                "Input Folder Not Found", 
                f"The input folder does not exist:\n{guides_dir}\n\nWould you like to configure it in Preferences?",
                QtWidgets.QMessageBox.StandardButton.Yes | QtWidgets.QMessageBox.StandardButton.No,
                QtWidgets.QMessageBox.StandardButton.Yes
            )
            if reply == QtWidgets.QMessageBox.StandardButton.Yes:
                self._show_preferences()
            return
        
        try:
            os.makedirs(output_dir, exist_ok=True)
        except (OSError, PermissionError) as e:
            QtWidgets.QMessageBox.warning(
                self, 
                "Output Folder Error", 
                f"Could not create output folder:\n{e}"
            )
            return

        # Get other settings from preferences
        temperature = float(self.settings.value("temperature", "0.5"))
        use_top_examples = self.settings.value("use_top_examples", "true") == "true"
        preview_source = self.settings.value("preview_source", "false") == "true"
        special_instructions = self.settings.value("special_instructions", "")
        
        # Get settings from sidebar
        pages_override = self.pages_edit.text()
        duration_minutes = int(self.duration_spin.value())
        # Apply default subject if current is empty
        subject = self.subject_combo.currentText().strip()
        if not subject:
            subject = self.settings.value("default_subject", "").strip() or None

        # UI state
        self.generate_btn.setEnabled(False)
        self.cancel_btn.setEnabled(True)
        self.progress.setValue(0)
        self.status_label.setText("Starting generation...")
        self.log_edit.clear()
        # Reset both editor and preview
        self.preview_editor.clear()
        self.preview_edit.clear()
        self.current_content = ""
        self.current_content_type = "fiche"
        self.save_pdf_btn.setEnabled(False)
        self.save_docx_btn.setEnabled(False)
        self._set_rating_enabled(False)

        # Prepare log file if enabled
        if self.settings.value("save_logs", "false") == "true":
            try:
                os.makedirs("logs", exist_ok=True)
                fname = datetime.now().strftime("logs/run_%Y%m%d_%H%M%S.txt")
                self.log_file_handle = open(fname, "a", encoding="utf-8")
                self.append_log(f"📝 Logging to {fname}")
            except Exception as e:
                self.append_log(f"⚠️ Could not open log file: {e}")
                self.log_file_handle = None
        else:
            self.log_file_handle = None

        # Spin up worker
        # Get image generation setting
        generate_image = False
        if HAS_IMAGE_GENERATION and hasattr(self, 'generate_fiche_image_chk'):
            generate_image = self.generate_fiche_image_chk.isChecked()
        
        self.worker = GenerationWorker(
            class_level, topic, pages_override,
            temperature, guides_dir, textbook_dir, use_top_examples, duration_minutes, subject,
            preview_source, special_instructions, generate_image,
            self.use_student_textbook_chk.isChecked()
        )
        self.worker.log.connect(self.append_log)
        self.worker.progress.connect(self.progress.setValue)
        self.worker.content.connect(self.on_content_ready)
        self.worker.done.connect(self.on_done)
        self.worker.enable_buttons.connect(self.on_enable_buttons)
        self.worker.finished.connect(self.on_enable_buttons)
        self.worker.request_source_preview.connect(self.show_source_preview_dialog)
        self.worker.start()

    def start_evaluation_generation(self):
        """Start generation of evaluation/test with full validation."""
        # Check if already running
        if self.worker is not None and self.worker.isRunning():
            QtWidgets.QMessageBox.information(
                self, 
                "Please Wait", 
                "An operation is already running. Cancel it before starting a new one."
            )
            return
        
        # Check API key
        if not API_KEYS.get("GEMINI_API_KEY"):
            reply = QtWidgets.QMessageBox.warning(
                self,
                "API Key Missing",
                "Gemini API key is required for evaluations and quizzes.\n\nWould you like to open Preferences to configure it?",
                QtWidgets.QMessageBox.StandardButton.Yes | QtWidgets.QMessageBox.StandardButton.No,
                QtWidgets.QMessageBox.StandardButton.Yes
            )
            if reply == QtWidgets.QMessageBox.StandardButton.Yes:
                self._show_preferences()
            return

        # Gather inputs
        selected_class = self.eval_class_combo.currentText()
        selected_subject = self.eval_subject_combo.currentText().strip()

        # New fields for Moroccan format
        school_name = self.eval_school_name_edit.text().strip() or "Groupe Scolaire"
        academic_year = self.eval_academic_year_edit.text().strip() or "2025/2026"
        eval_number = self.eval_number_spin.value()
        semester = self.eval_semester_combo.currentText()
        max_score = 10 if self.eval_max_score_10.isChecked() else 20
        
        # Save settings for next time
        self.settings.setValue("eval_school_name", school_name)
        self.settings.setValue("eval_academic_year", academic_year)
        self.settings.setValue("eval_number", str(eval_number))
        self.settings.setValue("eval_semester", semester)
        self.settings.setValue("eval_max_score", str(max_score))

        manual_topics = [
            line.strip() 
            for line in self.eval_topics_edit.toPlainText().splitlines() 
            if line.strip()
        ]
        selected_topics = [
            item.text().strip() 
            for item in self.eval_lessons_list.selectedItems() 
            if item.text().strip()
        ]

        # Deduplicate topics while preserving order
        topics_set = []
        for topic in manual_topics + selected_topics:
            if topic and topic not in topics_set:
                topics_set.append(topic)

        if not topics_set:
            QtWidgets.QMessageBox.warning(
                self, 
                "Missing Topics", 
                "Please select or type at least one lesson topic for the evaluation."
            )
            return
        self.current_topics_list = list(topics_set)

        eval_duration = self.eval_duration_spin.value()
        question_types_text = self.eval_question_types_edit.toPlainText().strip()
        difficulty = self.eval_difficulty_combo.currentText()

        temperature = self.eval_temperature_slider.value() / 100
        
        # Use shared model toggle from footer
        use_pro = self.model_toggle.isChecked()
        model_name = get_configured_pro_model() if use_pro else get_configured_flash_model()

        formatting_options = {
            "include_tables": self.eval_include_tables_chk.isChecked(),
            "include_boxes": self.eval_include_boxes_chk.isChecked(),
            "include_matching": self.eval_include_matching_chk.isChecked(),
            "include_answer_key": self.eval_include_answer_key_chk.isChecked(),
        }

        extra_instructions = self.eval_extra_instructions_edit.toPlainText().strip()
        
        # Package evaluation metadata
        eval_metadata = {
            "school_name": school_name,
            "academic_year": academic_year,
            "eval_number": eval_number,
            "semester": semester,
            "max_score": max_score,
        }

        self._start_evaluation_worker(
            selected_class,
            selected_subject,
            topics_set,
            eval_duration,
            question_types_text,
            difficulty,
            model_name,
            temperature,
            formatting_options,
            extra_instructions,
            eval_metadata
        )


    def _start_evaluation_worker(self, class_level, subject, topics_list, duration, question_types, difficulty,
                                 model_name, temperature, formatting_options, extra_instructions, eval_metadata):
        """Start the evaluation generation worker."""
        # Get settings from preferences
        output_dir = self.settings.value("output_dir", DEFAULT_OUTPUT_DIR)
        
        try:
            os.makedirs(output_dir, exist_ok=True)
        except Exception as e:
            QtWidgets.QMessageBox.warning(self, "Output folder error", 
                f"Could not create output folder:\n{e}")
            return

        # UI state
        self.generate_btn.setEnabled(False)
        self.generate_eval_btn.setEnabled(False)
        self.cancel_btn.setEnabled(True)
        self.progress.setValue(0)
        self.status_label.setText("Starting evaluation generation...")
        self.log_edit.clear()
        # Reset both editor and preview
        self.preview_editor.clear()
        self.preview_edit.clear()
        self.current_content = ""
        self.current_content_type = "evaluation"
        self.save_pdf_btn.setEnabled(False)
        self.save_docx_btn.setEnabled(False)
        self._set_rating_enabled(False)

        # Prepare log file if enabled
        if self.settings.value("save_logs", "false") == "true":
            try:
                os.makedirs("logs", exist_ok=True)
                fname = datetime.now().strftime("logs/eval_%Y%m%d_%H%M%S.txt")
                self.log_file_handle = open(fname, "a", encoding="utf-8")
                self.append_log(f"📝 Logging to {fname}")
            except Exception as e:
                self.append_log(f"⚠️ Could not open log file: {e}")
                self.log_file_handle = None
        else:
            self.log_file_handle = None

        # Create dedicated evaluation worker
        # Get image generation settings
        generate_images = False
        num_images = 2
        if HAS_IMAGE_GENERATION and hasattr(self, 'eval_generate_images_chk'):
            generate_images = self.eval_generate_images_chk.isChecked()
            if hasattr(self, 'eval_images_count_spin'):
                num_images = self.eval_images_count_spin.value()
        
        # Get guides directory from settings
        guides_dir = self.settings.value("input_dir", DEFAULT_INPUT_DIR)
        textbook_dir = self.settings.value("textbook_dir", "")
        
        self.worker = EvaluationWorker(
            class_level=class_level,
            topics_list=topics_list,
            subject=subject,
            duration=duration,
            question_types=question_types,
            difficulty=difficulty,
            model_name=model_name,
            temperature=temperature,
            formatting_options=formatting_options,
            extra_instructions=extra_instructions,
            generate_images=generate_images,
            num_images=num_images,
            guides_dir=guides_dir,
            eval_metadata=eval_metadata,
            textbook_dir=textbook_dir,
            use_student_textbook=self.use_student_textbook_chk.isChecked()
        )
        
        # Connect signals
        self.worker.log.connect(self.append_log)
        self.worker.progress.connect(self.progress.setValue)
        self.worker.content.connect(self.on_content_ready)
        self.worker.done.connect(self.on_done)
        self.worker.enable_buttons.connect(self.on_enable_buttons)
        self.worker.finished.connect(self.on_enable_buttons)
        self.worker.request_source_preview.connect(self.show_source_preview_dialog)
        
        # Start evaluation generation
        self.worker.start()

    def show_source_preview_dialog(self, source_text: str, prompt_text: str = ""):
        """Shows a resizable modal dialog for the user to confirm the extracted source text or evaluation details."""
        dialog = QtWidgets.QDialog(self)
        
        # Check if this is an evaluation or fiche generation
        is_evaluation = isinstance(self.worker, EvaluationWorker)
        
        if is_evaluation:
            dialog.setWindowTitle("Confirm Evaluation Details & AI Prompt")
            instructions_text = "Review the evaluation details and the prompt that will be sent to AI. Click Continue to generate the evaluation:"
            first_tab_title = "📝 Evaluation Details"
        else:
            dialog.setWindowTitle("Confirm Source Text & AI Prompt")
            instructions_text = "This is the text extracted from the PDF and the prompt that will be sent to AI. Review and confirm:"
            first_tab_title = "📄 Extracted Text"
        
        dialog.setModal(True)
        dialog.resize(900, 700)  # Larger for two text areas
        
        # Layout
        layout = QtWidgets.QVBoxLayout(dialog)
        
        # Instructions
        label = QtWidgets.QLabel(instructions_text)
        label.setWordWrap(True)
        layout.addWidget(label)
        
        # Tab widget for source text and prompt
        tab_widget = QtWidgets.QTabWidget()
        
        # Source text/evaluation details tab
        source_tab = QtWidgets.QWidget()
        source_layout = QtWidgets.QVBoxLayout(source_tab)
        source_text_edit = QtWidgets.QTextEdit()
        source_text_edit.setPlainText(source_text)
        source_text_edit.setReadOnly(True)
        source_text_edit.setFont(QtGui.QFont("monospace", 10))
        source_layout.addWidget(source_text_edit)
        tab_widget.addTab(source_tab, first_tab_title)
        
        # Prompt tab (if available)
        if prompt_text:
            prompt_tab = QtWidgets.QWidget()
            prompt_layout = QtWidgets.QVBoxLayout(prompt_tab)
            
            # Prompt text area
            prompt_text_edit = QtWidgets.QTextEdit()
            prompt_text_edit.setPlainText(prompt_text)
            prompt_text_edit.setReadOnly(True)
            prompt_text_edit.setFont(QtGui.QFont("monospace", 9))
            prompt_layout.addWidget(prompt_text_edit)
            
            # Copy prompt button
            copy_layout = QtWidgets.QHBoxLayout()
            copy_prompt_btn = QtWidgets.QPushButton("📋 Copy Full Prompt")
            copy_prompt_btn.clicked.connect(lambda: self._copy_prompt_to_clipboard(prompt_text))
            copy_layout.addWidget(copy_prompt_btn)
            copy_layout.addStretch()
            
            # Manual input button for both fiches and evaluations
            manual_input_btn = QtWidgets.QPushButton("✏️ Paste Your Own Markdown")
            manual_input_btn.clicked.connect(lambda: self._show_manual_input_dialog(dialog))
            copy_layout.addWidget(manual_input_btn)
            
            prompt_layout.addLayout(copy_layout)
            tab_widget.addTab(prompt_tab, "🤖 AI Prompt")
        
        layout.addWidget(tab_widget)
        
        # Buttons
        button_layout = QtWidgets.QHBoxLayout()
        button_layout.addStretch()
        
        continue_text = "Generate Evaluation" if is_evaluation else "Continue Generation"
        continue_btn = QtWidgets.QPushButton(continue_text)
        continue_btn.setDefault(True)
        cancel_btn = QtWidgets.QPushButton("Cancel")
        
        button_layout.addWidget(continue_btn)
        button_layout.addWidget(cancel_btn)
        layout.addLayout(button_layout)
        
        # Connect buttons
        continue_btn.clicked.connect(dialog.accept)
        cancel_btn.clicked.connect(dialog.reject)
        
        # Show dialog and handle result
        result = dialog.exec() if PYQT6 else dialog.exec_()
        
        if result == (QtWidgets.QDialog.DialogCode.Accepted if PYQT6 else QtWidgets.QDialog.Accepted):
            if self.worker:
                if is_evaluation:
                    self.worker.confirm_evaluation_preview()
                else:
                    self.worker.confirm_source_preview()
        else:
            # This will also trigger the confirmation event, but the cancel_event will be set
            self.cancel_generation()
            cancel_message = "⏹️ Evaluation cancelled by user." if is_evaluation else "⏹️ Generation cancelled by user at source preview."
            self.append_log(cancel_message)

    def _copy_prompt_to_clipboard(self, prompt_text: str):
        """Copy the full prompt to clipboard and show confirmation."""
        clipboard = QtGui.QGuiApplication.clipboard()
        clipboard.setText(prompt_text)
        
        # Show brief confirmation tooltip
        QtWidgets.QToolTip.showText(
            QtGui.QCursor.pos(), 
            "✅ Prompt copied to clipboard!", 
            None, 
            QtCore.QRect(), 
            2000  # 2 seconds
        )
        self.append_log("📋 AI prompt copied to clipboard.")

    def _show_manual_input_dialog(self, parent_dialog):
        """Show dialog for user to paste their own markdown content."""
        dialog = QtWidgets.QDialog(parent_dialog)
        dialog.setWindowTitle("Paste Your Own Markdown")
        dialog.setModal(True)
        dialog.resize(800, 600)
        
        layout = QtWidgets.QVBoxLayout(dialog)
        
        # Instructions
        instructions = QtWidgets.QLabel(
            "Paste your own markdown content below. This will override the AI-generated content "
            "and be used directly for PDF generation:"
        )
        instructions.setWordWrap(True)
        layout.addWidget(instructions)
        
        # Text area for markdown input
        text_edit = QtWidgets.QTextEdit()
        text_edit.setPlaceholderText("Paste your markdown content here...")
        text_edit.setFont(QtGui.QFont("monospace", 10))
        layout.addWidget(text_edit)
        
        # Buttons
        button_layout = QtWidgets.QHBoxLayout()
        button_layout.addStretch()
        
        use_markdown_btn = QtWidgets.QPushButton("Use This Markdown")
        use_markdown_btn.setDefault(True)
        cancel_btn = QtWidgets.QPushButton("Cancel")
        
        button_layout.addWidget(use_markdown_btn)
        button_layout.addWidget(cancel_btn)
        layout.addLayout(button_layout)
        
        # Connect buttons
        use_markdown_btn.clicked.connect(dialog.accept)
        cancel_btn.clicked.connect(dialog.reject)
        
        # Show dialog
        result = dialog.exec() if PYQT6 else dialog.exec_()
        
        if result == (QtWidgets.QDialog.DialogCode.Accepted if PYQT6 else QtWidgets.QDialog.Accepted):
            markdown_content = text_edit.toPlainText().strip()
            if markdown_content:
                # Close the parent preview dialog and use the manual content
                parent_dialog.accept()
                self.append_log("✏️ Using user-provided markdown content.")
                self.on_content_ready(markdown_content)
                return
            else:
                QtWidgets.QMessageBox.warning(
                    dialog, 
                    "Empty Content", 
                    "Please paste some markdown content or cancel."
                )

    def on_preview_edit_toggled(self, checked: bool):
        # Switch between rendered preview (0) and raw editor (1)
        self.preview_stack.setCurrentIndex(1 if checked else 0)
        if not checked:
            # Leaving edit mode: sync preview from editor
            text = self.preview_editor.toPlainText()
            self.current_content = text
            self._render_preview_content(text)
        # Enable save buttons if there's content
        can_save = bool(self.get_current_markdown().strip())
        self.save_pdf_btn.setEnabled(can_save)
        self.save_docx_btn.setEnabled(can_save and HAS_DOCX)
        self._set_rating_enabled(can_save)

    def on_editor_text_changed(self):
        # Live sync current content and rendered preview if in edit mode
        text = self.preview_editor.toPlainText()
        self.current_content = text
        if self.preview_edit_toggle.isChecked():
            self._render_preview_content(text)
        can_save = bool(text.strip())
        self.save_pdf_btn.setEnabled(can_save)
        self.save_docx_btn.setEnabled(can_save and HAS_DOCX)
        self._set_rating_enabled(can_save)

    def on_content_ready(self, content):
        # Show in preview and enable Save & Rating
        self.current_content = content or ""
        sender = self.sender()
        if isinstance(sender, EvaluationWorker):
            self.current_content_type = "evaluation"
        elif isinstance(sender, QuizWorker):
            self.current_content_type = "quiz"
        elif isinstance(sender, GenerationWorker):
            self.current_content_type = "fiche"
        elif self.current_content_type not in {"fiche", "evaluation", "quiz"}:
            self.current_content_type = "fiche"
        # Populate both the preview (rendered) and editor (raw)
        self.preview_editor.blockSignals(True)
        self.preview_editor.setPlainText(self.current_content)
        self.preview_editor.blockSignals(False)
        self._render_preview_content(self.current_content)
        can_save = bool(self.current_content.strip())
        self.save_pdf_btn.setEnabled(can_save)
        self.save_docx_btn.setEnabled(can_save and HAS_DOCX)
        self._set_rating_enabled(can_save)
        # Switch to Preview tab for wow factor
        self.right_tabs.setCurrentWidget(self.preview_tab)

    def _render_preview_content(self, text: str):
        """Render markdown preview with safety fallback for very large payloads."""
        payload = text or ""
        if len(payload) > self.preview_markdown_limit:
            self.preview_edit.setPlainText(payload)
            if not self._preview_large_mode:
                self.append_log(
                    f"ℹ️ Preview switched to plain text (content too large: {len(payload)} chars)."
                )
            self._preview_large_mode = True
            return

        self._preview_large_mode = False

        try:
            self.preview_edit.setMarkdown(payload)
        except Exception:
            self.preview_edit.setPlainText(payload)

    def get_current_markdown(self) -> str:
        # If editing, source of truth is the editor; else use current_content
        return self.preview_editor.toPlainText() if self.preview_edit_toggle.isChecked() else (self.current_content or "")

    def save_current_rating(self):
        content = (self.get_current_markdown() or "").strip()
        if not content:
            QtWidgets.QMessageBox.information(self, "Nothing to rate", "Generate a fiche first.")
            return
        rating = self.rating_combo.currentIndex() + 1  # 1..5
        record = {
            "topic": self.topic_edit.text().strip(),
            "class_level": self.class_combo.currentText(),
            "rating": rating,
            "content": content,
            "timestamp": datetime.now().isoformat(timespec="seconds")
        }
        ok = save_rating_record(record)
        if ok:
            QtWidgets.QMessageBox.information(self, "Saved", f"Saved rating: {rating} ★")
            self.append_log(f"⭐ Rating saved ({rating}/5). This fiche can now be used as a style example.")
        else:
            QtWidgets.QMessageBox.warning(self, "Error", "Failed to save rating. Check write permissions.")

    def cancel_generation(self):
        """Cancel ongoing generation safely."""
        if self.worker is not None and self.worker.isRunning():
            self.worker.cancel()
            self.append_log("⏹️ Cancel requested. Stopping at the next safe checkpoint...")
            self.append_log("ℹ️ Note: If AI is currently generating, cancellation will occur after the API call completes.")
            self.cancel_btn.setEnabled(False)
            
            # Give worker time to finish gracefully (non-blocking)
            QtCore.QTimer.singleShot(100, self._check_worker_stopped)

    def _check_worker_stopped(self):
        """Periodically check if worker has stopped after cancel request."""
        if self.worker is not None:
            if self.worker.isRunning():
                # Still running, check again in 100ms
                QtCore.QTimer.singleShot(100, self._check_worker_stopped)
            else:
                # Worker finished, clean up
                self._cleanup_worker()

    def _cleanup_worker(self):
        """Clean up worker thread safely."""
        if self.worker is not None:
            try:
                # Disconnect all signals to prevent late emissions
                self.worker.log.disconnect()
                self.worker.progress.disconnect()
                self.worker.content.disconnect()
                self.worker.done.disconnect()
                self.worker.enable_buttons.disconnect()
                
                # Wait for thread to finish (should be immediate if cancelled)
                if self.worker.isRunning():
                    self.worker.wait(1000)  # Wait up to 1 second
                
                # Delete the worker
                self.worker.deleteLater()
            except (RuntimeError, AttributeError):
                # Already disconnected or deleted
                pass
            finally:
                self.worker = None

    def on_done(self, path: str):
        """Handle generation completion."""
        self.on_enable_buttons()
        QtWidgets.QMessageBox.information(
            self, 
            "Generation Complete", 
            f"Generated content:\n{path}"
        )

    def on_enable_buttons(self):
        """Re-enable UI after generation completes or fails."""
        self.generate_btn.setEnabled(True)
        self.generate_eval_btn.setEnabled(True)
        self.cancel_btn.setEnabled(False)
        
        # Close log file handle
        if self.log_file_handle:
            try:
                self.log_file_handle.flush()
                self.log_file_handle.close()
            except (IOError, OSError):
                pass
            finally:
                self.log_file_handle = None
        
        # Clean up worker after brief delay to ensure all signals processed
        QtCore.QTimer.singleShot(500, self._cleanup_worker)

    def choose_textbook_folder(self):
        dlg = QtWidgets.QFileDialog(self, "Choose textbooks folder")
        dlg.setFileMode(QtWidgets.QFileDialog.FileMode.Directory if PYQT6 else QtWidgets.QFileDialog.Directory)
        dlg.setOption(QtWidgets.QFileDialog.Option.ShowDirsOnly if PYQT6 else QtWidgets.QFileDialog.ShowDirsOnly, True)
        if dlg.exec():
            selected = dlg.selectedFiles()[0]
            self.textbook_edit.setText(selected)
            self._save_settings()

    def choose_input_folder(self):
        dlg = QtWidgets.QFileDialog(self, "Choose input folder (guides)")
        dlg.setFileMode(QtWidgets.QFileDialog.FileMode.Directory if PYQT6 else QtWidgets.QFileDialog.Directory)
        dlg.setOption(QtWidgets.QFileDialog.Option.ShowDirsOnly if PYQT6 else QtWidgets.QFileDialog.ShowDirsOnly, True)
        if dlg.exec():
            selected = dlg.selectedFiles()[0]
            self.input_edit.setText(selected)
            self._save_settings()

    def choose_output_folder(self):
        dlg = QtWidgets.QFileDialog(self, "Choose output folder")
        dlg.setFileMode(QtWidgets.QFileDialog.FileMode.Directory if PYQT6 else QtWidgets.QFileDialog.Directory)
        dlg.setOption(QtWidgets.QFileDialog.Option.ShowDirsOnly if PYQT6 else QtWidgets.QFileDialog.ShowDirsOnly, True)
        if dlg.exec():
            selected = dlg.selectedFiles()[0]
            self.output_edit.setText(selected)
            self._save_settings()

    def _is_current_content_evaluation(self):
        """Check if the current preview content should be exported as an evaluation."""
        if self.current_content_type == "evaluation":
            return True

        # Fallback heuristic for legacy/edited markdown when mode is uncertain.
        md = (self.get_current_markdown() or "").lower()
        return "## exercice" in md and ("## corrigé" in md or "## corrige" in md)

    def _export_in_progress(self) -> bool:
        return self.export_worker is not None and self.export_worker.isRunning()

    def _collect_export_context(self, is_eval: bool) -> dict:
        """Build export context from current UI state and content mode."""
        output_dir = (self.settings.value("output_dir", DEFAULT_OUTPUT_DIR) or DEFAULT_OUTPUT_DIR).strip()
        template_name = self.pdf_template_combo.currentText() or self.settings.value("default_pdf_style", list(PDF_TEMPLATES.keys())[0])
        show_meta = self.settings.value("pdf_show_meta", "false") == "true"

        if is_eval and hasattr(self, "eval_class_combo"):
            class_level = self.eval_class_combo.currentText()
        elif self.current_content_type == "quiz" and hasattr(self, "quiz_class_combo"):
            class_level = self.quiz_class_combo.currentText()
        else:
            class_level = self.class_combo.currentText()

        if is_eval and hasattr(self, "eval_subject_combo"):
            subject = self.eval_subject_combo.currentText().strip() or None
        elif self.current_content_type == "quiz" and hasattr(self, "quiz_subject_combo"):
            subject = self.quiz_subject_combo.currentText().strip() or None
        else:
            subject = self.subject_combo.currentText().strip() or None

        lesson_topic = self.topic_edit.text().strip() or "Lecon"
        topics_list = self.current_topics_list or [lesson_topic]

        return {
            "output_dir": output_dir,
            "template_name": template_name,
            "show_meta": show_meta,
            "class_level": class_level,
            "subject": subject,
            "lesson_topic": lesson_topic,
            "topics_list": topics_list,
        }

    def _start_export(self, export_format: str):
        md = self.get_current_markdown().strip()
        if not md:
            content_type = "evaluation" if self._is_current_content_evaluation() else "fiche"
            QtWidgets.QMessageBox.information(self, "Nothing to save", f"Generate a {content_type} first.")
            return

        if self._export_in_progress():
            QtWidgets.QMessageBox.information(self, "Export in progress", "Please wait for the current export to finish.")
            return

        if export_format == "docx" and not HAS_DOCX:
            QtWidgets.QMessageBox.warning(self, "Missing dependency", "Install python-docx to export DOCX:\n\npip install python-docx")
            return

        is_eval = self._is_current_content_evaluation()
        ctx = self._collect_export_context(is_eval)

        self.export_worker = ExportWorker(
            export_format=export_format,
            content_type="evaluation" if is_eval else "fiche",
            markdown=md,
            class_level=ctx["class_level"],
            output_dir=ctx["output_dir"],
            template_name=ctx["template_name"],
            subject=ctx["subject"],
            lesson_topic=ctx["lesson_topic"],
            topics_list=ctx["topics_list"],
            show_meta_banner=ctx["show_meta"],
        )
        self.export_worker.log.connect(self.append_log)
        self.export_worker.success.connect(self._on_export_success)
        self.export_worker.failed.connect(self._on_export_failed)
        self.export_worker.finished.connect(self._on_export_finished)

        self.save_pdf_btn.setEnabled(False)
        self.save_docx_btn.setEnabled(False)
        self.status_label.setText(f"Exporting {export_format.upper()}...")
        self.append_log(f"💾 Export started ({export_format.upper()}).")
        self.export_worker.start()

    def _on_export_success(self, path: str, export_format: str, content_type: str):
        label = "Evaluation" if content_type == "evaluation" else "Fiche"
        self.append_log(f"🎉 {label} {export_format.upper()} export complete.")
        QtWidgets.QMessageBox.information(self, "Saved", f"{label} {export_format.upper()} exported to:\n{path}")

    def _on_export_failed(self, message: str):
        self.append_log(f"❌ Export failed: {message}")
        QtWidgets.QMessageBox.critical(self, "Export Failed", f"Failed to export file:\n{message}\n\nCheck the log for details.")

    def _on_export_finished(self):
        self.export_worker = None
        can_save = bool(self.get_current_markdown().strip())
        self.save_pdf_btn.setEnabled(can_save)
        self.save_docx_btn.setEnabled(can_save and HAS_DOCX)
        self.status_label.setText("Ready")

    def save_current_pdf(self):
        self._start_export("pdf")

    def save_current_docx(self):
        self._start_export("docx")

    def clear_preview(self):
        self.preview_editor.clear()
        self.preview_edit.clear()
        self.preview_edit_toggle.setChecked(False)
        self.save_pdf_btn.setEnabled(False)
        self.save_docx_btn.setEnabled(False)
        self._set_rating_enabled(False)
        self.current_content = ""
        self.current_content_type = "unknown"
        self.current_topics_list = []

    def _load_settings(self):
        # Ensure API keys are loaded first
        load_api_keys_from_settings()

        # Load settings into the sidebar controls
        self.class_combo.setCurrentText(self.settings.value("class_level", "cm2"))
        self.topic_edit.setText(self.settings.value("topic", ""))
        
        # Basic settings
        pages_val = self.settings.value("pages", "")
        if pages_val:
            self.pages_edit.setText(pages_val)
        try:
            self.duration_spin.setValue(int(self.settings.value("duration", self.settings.value("default_duration", "45"))))
        except Exception:
            pass
        subj = self.settings.value("subject", self.settings.value("default_subject", ""))
        if subj is not None:
            self.subject_combo.setCurrentText(subj)
        
        # Load available lessons from toc_cache
        self._load_available_lessons()
        # Default template
        try:
            self.pdf_template_combo.setCurrentText(self.settings.value("default_pdf_style", list(PDF_TEMPLATES.keys())[0]))
        except Exception:
            pass
        
        # Load window geometry
        geometry = self.settings.value("window_geometry")
        if geometry:
            self.restoreGeometry(geometry)
        
        # Load splitter state
        splitter_state = self.settings.value("splitter_state")
        if splitter_state and getattr(self, "main_splitter", None):
            try:
                self.main_splitter.restoreState(splitter_state)
            except Exception:
                pass

    def _save_settings(self):
        # Save basic controls
        self.settings.setValue("class_level", self.class_combo.currentText())
        self.settings.setValue("topic", self.topic_edit.text())
        self.settings.setValue("pages", self.pages_edit.text())
        self.settings.setValue("duration", str(self.duration_spin.value()))
        self.settings.setValue("subject", self.subject_combo.currentText())
        
        # Save window state
        self.settings.setValue("window_geometry", self.saveGeometry())
        if getattr(self, "main_splitter", None):
            try:
                self.settings.setValue("splitter_state", self.main_splitter.saveState())
            except Exception:
                pass

    def closeEvent(self, event):
        """Handle application close with proper cleanup."""
        # Save settings first
        self._save_settings()
        
        # Cancel and clean up any running worker
        if self.worker is not None and self.worker.isRunning():
            self.worker.cancel()
            # Give worker a brief moment to exit gracefully. Avoid force-terminating threads.
            self.worker.wait(3000)
        
        # Close log file
        if self.log_file_handle:
            try:
                self.log_file_handle.flush()
                self.log_file_handle.close()
            except (IOError, OSError):
                pass
            finally:
                self.log_file_handle = None
        
        event.accept()
