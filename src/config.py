import os
import threading
import json
from PyQt6 import QtCore
from reportlab.lib.units import cm
import json

# Base directory
BASE_DIR = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ICON_PATH = os.path.join(BASE_DIR, "icon.png")

try:
    import docx
    HAS_DOCX = True
except ImportError:
    HAS_DOCX = False

HAS_IMAGE_GENERATION = True

# Constants
DEFAULT_INPUT_DIR = "guides"
DEFAULT_OUTPUT_DIR = "fiches"
TOC_CACHE_DIR = "toc_cache"
RATINGS_FILE = os.path.join("data", "ratings.json")
TABLE_OF_CONTENTS_PAGES = 5
CLASS_LEVELS = [
    "cp", "ce1", "ce2", "cm1", "cm2", "6e",
    "7e", "8e", "9e"
]

# Default model names
DEFAULT_PRO_MODEL = "gemini-2.5-pro"
DEFAULT_FLASH_MODEL = "gemini-2.5-flash"
GEMINI_MODEL = DEFAULT_PRO_MODEL
GEMINI_TOC_MODEL = DEFAULT_FLASH_MODEL
GEMINI_OFFSET_MODEL = "gemini-2.5-flash-lite"
GEMMA_SYNTAX_MODEL = "gemma-3-27b-it"

# Image Generation Constants
IMAGE_MODEL = "gemini-2.5-flash-image"
STYLE_TEMPLATES = {
    "coloring": "Simple black and white line drawing suitable for coloring, clean outlines, no shading, minimal details, children's coloring book style",
    "diagram": "Clean educational diagram with simple lines, minimal colors, textbook illustration style, hand-drawn aesthetic",
    "minimalist": "Minimalist illustration with simple shapes and lines, flat design, limited color palette, clean and professional",
    "sketch": "Simple pencil sketch style, light lines, educational illustration, notebook doodle aesthetic",
}

COMPLEXITY_LEVELS = {
    "cp": "extremely simple, very few elements, large shapes, suitable for 6-year-olds",
    "ce1": "simple with basic details, clear shapes, suitable for 7-year-olds",
    "ce2": "moderate detail, recognizable elements, suitable for 8-9 year-olds",
    "cm1": "good detail level, educational accuracy, suitable for 9-10 year-olds",
    "cm2": "detailed and informative, clear diagrams, suitable for 10-11 year-olds",
    "6e": "detailed educational illustrations, diagram quality, suitable for 11-12 year-olds",
}

# API Keys Management
API_KEYS = {}
_GENAI_CLIENT = None
_GENAI_CLIENT_KEY = None
_GENAI_CLIENT_LOCK = threading.Lock()

def _store_api_key(value: str):
    """Persist key in memory and refresh the cached client when it changes."""
    global _GENAI_CLIENT, _GENAI_CLIENT_KEY
    previous = API_KEYS.get("GEMINI_API_KEY")
    API_KEYS["GEMINI_API_KEY"] = value
    if value and value != previous:
        with _GENAI_CLIENT_LOCK:
            _GENAI_CLIENT = None
            _GENAI_CLIENT_KEY = None

def load_api_keys_from_settings() -> bool:
    """
    Load Gemini API key from QSettings with fallback chain.
    Priority: QSettings > Environment > keys.txt
    Returns: True if key was loaded successfully, False otherwise
    """
    settings = QtCore.QSettings("FicheGen", "Pedago")
    
    # Try QSettings first
    gemini_key = settings.value("gemini_api_key", "").strip()
    if gemini_key and len(gemini_key) > 10:
        _store_api_key(gemini_key)
        return True
    
    # Fallback to environment
    env_gemini = os.getenv("GEMINI_API_KEY", "").strip()
    if env_gemini and len(env_gemini) > 10:
        _store_api_key(env_gemini)
        return True
    
    # Final fallback to keys.txt
    try:
        keys_path = os.path.join(BASE_DIR, "keys.txt")
        if os.path.exists(keys_path):
            with open(keys_path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line or line.startswith('#'):
                        continue
                    if '=' in line:
                        key, value = line.split('=', 1)
                        key, value = key.strip(), value.strip()
                        if key == "GEMINI_API_KEY" and value and len(value) > 10:
                            _store_api_key(value)
                            return True
    except (FileNotFoundError, IOError, UnicodeDecodeError):
        pass
    
    return False

# Translations
TRANSLATIONS = {
    "en": {
        "app_title": "FicheGen",
        "fiches_tab": "Fiches",
        "evaluations_tab": "Evaluations",
        "quizzes_tab": "Quizzes",
        "preview_tab": "Preview",
        "log_tab": "Log",
        "generate_fiche": "📄 Generate Fiche",
        "generate_eval": "📝 Generate Evaluation",
        "generate_quiz": "🎯 Generate Quiz",
        "cancel": "⏹ Cancel",
        "class": "Class",
        "topic": "Topic",
        "subject": "Subject",
        "duration": "Duration",
        "pages_optional": "Pages (optional)",
        "temperature": "Temperature",
        "output_folder": "Output folder",
        "pdf_template": "PDF Template",
        "special_instructions": "Special Instructions",
        "use_top_examples": "Use top-rated fiches as style examples",
        "save_logs": "Save logs to file during generation",
        "preview_source": "Preview source text",
        "browse": "Browse…",
        "preferences": "Preferences",
        "help": "Help",
        "export_pdf": "Export PDF",
        "export_docx": "Export DOCX",
        "rate_fiche": "Rate Fiche",
        "edit_markdown": "Edit Markdown",
        "view_preview": "View Preview",
        "input_folder": "Input folder",
        "textbooks_folder": "Textbooks folder",
        "gemini_key": "Gemini Key",
        "api_keys": "API Keys",
        "get_free_key": "Get your free Gemini API key",
        "generation_settings": "Generation Settings",
        "ai_model": "AI & Model",
        "gemini_model": "Gemini Model",
        "formatting_prefs": "Formatting Preferences",
        "include_tables": "Include tabular questions (Markdown tables)",
        "include_boxes": "Highlight key instructions with callout boxes",
        "include_matching": "Add at least one matching / connect-the-dots activity",
        "include_connect_dots": "Add a relier / connect-the-dots style exercise",
        "include_answer_key": "Append an answer key section",
        "difficulty": "Difficulty",
        "question_types": "Question Types",
        "extra_instructions": "Additional instructions for AI (optional)...",
        "select_lessons": "Select lesson topics",
        "manual_topics": "Or type topics manually (one per line)",
        "eval_settings": "Evaluation Settings",
        "advanced": "Advanced",
        "appearance": "Appearance",
        "compact_sidebar": "Compact sidebar spacing",
        "show_pdf_meta": "Show PDF meta banner (title, classe, durée)",
        "defaults": "Defaults",
        "default_duration": "Default Duration (min)",
        "default_pdf_style": "Default PDF Style",
        "default_subject": "Default Subject",
        "folders": "Folders",
        "input_guides": "Input Guides",
        "student_books": "Student Books",
        "ai_model_config": "AI Model Configuration",
        "main_gemini_model": "Main Gemini Model",
        "toc_extraction_model": "ToC Extraction Model",
        "offset_detection_model": "Offset Detection Model",
        "syntax_correction_model": "Syntax Correction Model",
        "advanced_prompt_config": "Advanced Prompt Configuration",
        "enable_prompt_editing": "Enable advanced prompt editing",
        "progress": "Progress",
        "language": "Language",
        "choose_language": "Interface Language",
    },
    "fr": {
        "app_title": "FicheGen",
        "fiches_tab": "Fiches",
        "evaluations_tab": "Évaluations",
        "quizzes_tab": "Quiz",
        "preview_tab": "Aperçu",
        "log_tab": "Journal",
        "generate_fiche": "📄 Générer une Fiche",
        "generate_eval": "📝 Générer une Évaluation",
        "generate_quiz": "🎯 Générer un Quiz",
        "cancel": "⏹ Annuler",
        "class": "Classe",
        "topic": "Sujet",
        "subject": "Matière",
        "duration": "Durée",
        "pages_optional": "Pages (optionnel)",
        "temperature": "Température",
        "output_folder": "Dossier de sortie",
        "pdf_template": "Modèle PDF",
        "special_instructions": "Instructions spéciales",
        "use_top_examples": "Utiliser les fiches les mieux notées comme exemples",
        "save_logs": "Enregistrer les journaux dans un fichier",
        "preview_source": "Aperçu du texte source",
        "browse": "Parcourir…",
        "preferences": "Préférences",
        "help": "Aide",
        "export_pdf": "Exporter en PDF",
        "export_docx": "Exporter en DOCX",
        "rate_fiche": "Noter la Fiche",
        "edit_markdown": "Modifier le Markdown",
        "view_preview": "Voir l'Aperçu",
        "input_folder": "Dossier d'entrée",
        "textbooks_folder": "Dossier des manuels",
        "gemini_key": "Clé Gemini",
        "api_keys": "Clés API",
        "get_free_key": "Obtenez votre clé API Gemini gratuite",
        "generation_settings": "Paramètres de génération",
        "ai_model": "IA & Modèle",
        "gemini_model": "Modèle Gemini",
        "formatting_prefs": "Préférences de formatage",
        "include_tables": "Inclure des questions en tableau (tableaux Markdown)",
        "include_boxes": "Mettre en évidence les instructions avec des encadrés",
        "include_matching": "Ajouter au moins une activité de correspondance",
        "include_connect_dots": "Ajouter un exercice de type relier",
        "include_answer_key": "Ajouter un corrigé",
        "difficulty": "Difficulté",
        "question_types": "Types de questions",
        "extra_instructions": "Instructions supplémentaires pour l'IA (optionnel)...",
        "select_lessons": "Sélectionner les sujets de leçon",
        "manual_topics": "Ou saisir les sujets manuellement (un par ligne)",
        "eval_settings": "Paramètres d'évaluation",
        "advanced": "Avancé",
        "appearance": "Apparence",
        "compact_sidebar": "Espacement compact de la barre latérale",
        "show_pdf_meta": "Afficher la bannière de métadonnées PDF",
        "defaults": "Par défaut",
        "default_duration": "Durée par défaut (min)",
        "default_pdf_style": "Style PDF par défaut",
        "default_subject": "Matière par défaut",
        "folders": "Dossiers",
        "input_guides": "Guides d'entrée",
        "student_books": "Manuels des élèves",
        "ai_model_config": "Configuration du modèle IA",
        "main_gemini_model": "Modèle Gemini principal",
        "toc_extraction_model": "Modèle d'extraction de table des matières",
        "offset_detection_model": "Modèle de détection de décalage",
        "syntax_correction_model": "Modèle de correction de syntaxe",
        "advanced_prompt_config": "Configuration avancée des prompts",
        "enable_prompt_editing": "Activer l'édition avancée des prompts",
        "progress": "Progression",
        "language": "Langue",
        "choose_language": "Langue de l'interface",
    }
}

CURRENT_LANGUAGE = "en"

def tr(key):
    """Translate a UI string based on current language"""
    return TRANSLATIONS.get(CURRENT_LANGUAGE, TRANSLATIONS["en"]).get(key, key)

def set_language(lang_code):
    """Set the current UI language"""
    global CURRENT_LANGUAGE
    if lang_code in TRANSLATIONS:
        CURRENT_LANGUAGE = lang_code

# Model Configuration Getters
def get_configured_pro_model():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    return settings.value("custom_pro_model", DEFAULT_PRO_MODEL)

def get_configured_flash_model():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    return settings.value("custom_flash_model", DEFAULT_FLASH_MODEL)

def get_configured_gemini_model():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    use_pro = settings.value("gemini_use_pro", "true") == "true"
    if use_pro:
        return get_configured_pro_model()
    else:
        return get_configured_flash_model()

def get_configured_gemini_toc_model():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    return settings.value("advanced_gemini_toc_model", GEMINI_TOC_MODEL)

def get_configured_gemini_offset_model():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    return settings.value("advanced_gemini_offset_model", GEMINI_OFFSET_MODEL)

def get_configured_gemma_syntax_model():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    return settings.value("advanced_gemma_syntax_model", GEMMA_SYNTAX_MODEL)

def save_rating_record(record):
    """Save a rating record to the ratings file"""
    try:
        os.makedirs(os.path.dirname(RATINGS_FILE), exist_ok=True)
        
        ratings = []
        if os.path.exists(RATINGS_FILE):
            try:
                with open(RATINGS_FILE, 'r', encoding='utf-8') as f:
                    ratings = json.load(f)
            except json.JSONDecodeError:
                pass
        
        ratings.append(record)
        
        with open(RATINGS_FILE, 'w', encoding='utf-8') as f:
            json.dump(ratings, f, indent=2, ensure_ascii=False)
            
        return True
    except Exception as e:
        print(f"Error saving rating: {e}")
        return False

def _clamp_temperature(val):
    """Ensure temperature is between 0.0 and 1.0"""
    try:
        f = float(val)
        return max(0.0, min(1.0, f))
    except (ValueError, TypeError):
        return 0.7

# Prompts
DEFAULT_TOC_PROMPT = """You are an expert index parser. Your task is to convert a raw table of contents from a PDF into a structured JSON array.

The user will provide the raw text of a table of contents. You must identify each lesson/chapter title and its corresponding starting page number.

**Output Format:**
- Respond with a valid JSON array `[]`.
- Each object in the array must have two keys: `"topic"` (the full title of the lesson) and `"page"` (the integer page number).
- Do NOT include section titles or anything that isn't a lesson with a page number.
- Be precise. The topic names must be exact.

**Example:**
If the input is:
"Sciences Naturelles
Le cycle de l'eau ......................... 42
Les volcans ............................... 48
Histoire
La Révolution Française ................... 55"

Your output must be:
[
  {{
    "topic": "Le cycle de l'eau",
    "page": 42
  }},
  {{
    "topic": "Les volcans",
    "page": 48
  }},
  {{
    "topic": "La Révolution Française",
    "page": 55
  }}
]

Here is the table of contents to parse:
---
{toc_text}
---
Respond with ONLY the JSON array. Do not add any introductory text or code fences."""

DEFAULT_PAGE_FINDING_PROMPT = """You are an index analysis bot. Your task is to find the page numbers for a specific lesson topic from a book's table of contents.
The lesson topic is: "{lesson_topic}"
Here is the text of the table of contents:
---
{toc_text}
---
Analyze the table of contents and find the page or range of pages corresponding to the lesson topic.
Respond with ONLY the page numbers.
- If it's a range of pages (most common), find the start page for "{lesson_topic}" and the start page for the next lesson, then subtract one. Respond with a dash (e.g., "42-46").
- If it's a single page, respond with the number (e.g., "42").
Do NOT add any other words, sentences, or explanations. Just the numbers."""

DEFAULT_FICHE_PROMPT = """Tu es un assistant expert pour les enseignants du primaire au Maroc.

MISSION:
Crée une fiche pédagogique complète et bien structurée pour la leçon "{lesson_topic}" pour la classe de {class_level}.
{subject_line}

MATÉRIEL SOURCE (texte du manuel):
---
{lesson_text}
---

STRUCTURE À RESPECTER SCRUPULEUSEMENT (utilise ce format comme plan de travail):
---
{fiche_structure}
---

EXEMPLES DE STYLE À IMITER (pour le ton et la formulation, pas le format):
---
{examples_block}
---
{instructions_block}
**INSTRUCTIONS DÉTAILLÉES:**
1.  **Structure interne:** Utilise la `STRUCTURE À RESPECTER` comme plan pour remplir les champs JSON (objectives → liste, phases → étapes chronologiques, etc.).
2.  **Métadonnées:** Renseigne précisément les informations clés (titre du chapitre si disponible, durée réaliste, classe, matière et matériel nécessaire).
3.  **Contenu:** Base-toi sur le MATÉRIEL SOURCE pour alimenter chaque section. Analyse-le rigoureusement et reste fidèle au programme.
4.  **Ton:** Adopte le style des EXEMPLES (voix de l'enseignant, phrases actionnables, bienveillance professionnelle).
5.  **Timing:** Calibre chaque phase pour respecter environ {duree} minutes, avec {active} minutes d'activité effective. Commence par une mise en route orale.
6.  **Clarté:** Les consignes doivent être directes, sans phrases d'introduction inutiles. Prévois une conclusion récapitulative prête à être recopiée.
7.  **Format JSON:** Réponds exclusivement avec un objet JSON contenant les champs `title`, `metadata`, `objectives`, `phases`, `evaluation`, `reminders` (optionnel) et `conclusion` (optionnel). Chaque liste (`objectives`, `teacher_steps`, `student_steps`, `evaluation.questions`) doit contenir des phrases courtes en Markdown simple.
"""

def get_configured_toc_prompt():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    custom_prompt = settings.value("advanced_toc_prompt", "").strip()
    return custom_prompt if custom_prompt else DEFAULT_TOC_PROMPT

def get_configured_page_finding_prompt():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    custom_prompt = settings.value("advanced_page_finding_prompt", "").strip()
    return custom_prompt if custom_prompt else DEFAULT_PAGE_FINDING_PROMPT

def get_configured_fiche_prompt():
    settings = QtCore.QSettings("FicheGen", "Pedago")
    custom_prompt = settings.value("advanced_fiche_prompt", "").strip()
    return custom_prompt if custom_prompt else DEFAULT_FICHE_PROMPT

# PDF Templates
PDF_TEMPLATES = {
    "Normal": {
        "title_color": "#2E8B57",
        "heading_color": "#2F4F4F",
        "accent_color": "#2E8B57",
        "font_family": "Helvetica",
        "title_size": 24,
        "heading_size": 18,
        "body_size": 12,
        "meta_size": 11,
        "line_height": 16,
        "margins": (2*cm, 2.5*cm, 2*cm, 2.5*cm),
        "show_meta_banner": True,
        "header_style": "elegant_line",
        "bullet_style": "•",
        "section_spacing": 12,
        "background_accent": "#F0F8F5"
    },
    "Professional": {
        "title_color": "#1E3A8A",
        "heading_color": "#1F2937",
        "accent_color": "#1E3A8A",
        "font_family": "Helvetica",
        "title_size": 26,
        "heading_size": 20,
        "body_size": 12,
        "meta_size": 11,
        "line_height": 18,
        "margins": (2.5*cm, 3*cm, 2.5*cm, 2.5*cm),
        "show_meta_banner": True,
        "header_style": "corporate_box",
        "bullet_style": "▸",
        "section_spacing": 16,
        "background_accent": "#F8FAFC",
        "border_color": "#3B82F6",
        "use_borders": True,
        "decorative_elements": True
    },
    "Coral": {
        "title_color": "#FF6B35",
        "heading_color": "#8B4513",
        "accent_color": "#FF6B35",
        "font_family": "Helvetica",
        "title_size": 22,
        "heading_size": 17,
        "body_size": 11,
        "meta_size": 10,
        "line_height": 15,
        "margins": (2*cm, 2.5*cm, 2*cm, 2*cm),
        "show_meta_banner": True,
        "header_style": "organic_wave",
        "bullet_style": "◦",
        "section_spacing": 10,
        "background_accent": "#FFF5F0",
        "warm_styling": True
    },
    "Aesthetic": {
        "title_color": "#FF8C00",
        "heading_color": "#FF6600",
        "accent_color": "#FF8C00",
        "font_family": "Helvetica",
        "title_size": 28,
        "heading_size": 20,
        "body_size": 12,
        "meta_size": 11,
        "line_height": 18,
        "margins": (2.5*cm, 3.5*cm, 2.5*cm, 2.5*cm),
        "show_meta_banner": True,
        "header_style": "modern_gradient",
        "bullet_style": "◆",
        "section_spacing": 18,
        "background_accent": "#FFF8F0",
        "gradient_colors": ["#FF8C00", "#FFA500"],
        "decorative": True,
        "shadow_effects": True
    },
    "Minimal Pro": {
        "title_color": "#374151",
        "heading_color": "#4B5563",
        "accent_color": "#374151",
        "font_family": "Helvetica",
        "title_size": 20,
        "heading_size": 16,
        "body_size": 11,
        "meta_size": 10,
        "line_height": 14,
        "margins": (3.5*cm, 4*cm, 3.5*cm, 3*cm),
        "show_meta_banner": False,
        "header_style": "minimal_line",
        "bullet_style": "—",
        "section_spacing": 20,
        "minimal": True,
        "extra_whitespace": True
    },
    "Classic Serif": {
        "title_color": "#0F172A",
        "heading_color": "#1E293B",
        "accent_color": "#0F172A",
        "font_family": "Times-Roman",
        "title_size": 24,
        "heading_size": 19,
        "body_size": 12,
        "meta_size": 11,
        "line_height": 16,
        "margins": (2.5*cm, 3*cm, 2.5*cm, 2.5*cm),
        "show_meta_banner": True,
        "header_style": "classic_underline",
        "bullet_style": "•",
        "section_spacing": 14,
        "background_accent": "#F8FAFC",
        "serif": True,
        "traditional_spacing": True,
        "formal_layout": True
    }
}
