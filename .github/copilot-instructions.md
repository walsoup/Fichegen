# FicheGen Copilot Instructions

## Project Architecture
- **Monolithic Design**: The core application logic, UI, and rendering reside in `main.py`. Helper logic for DOCX conversion is in `md_to_docx_converter.py`.
- **UI Framework**: Uses **PyQt6**. Ensure all UI updates happen on the main thread. Long-running operations (AI calls, PDF processing) must run in background threads (using `QThread` and queues).
- **AI Integration**: Uses `google.genai` SDK.
  - **Models**: Configurable via `get_configured_*_model()` functions. Defaults include `gemini-2.5-pro` (logic) and `gemini-2.5-flash` (fast tasks).
  - **Image Gen**: Uses `gemini-2.5-flash-image` via a dedicated inline module in `main.py`.
- **Document Generation**:
  - **PDF**: Uses `reportlab.platypus`. Styles are defined in `PDF_TEMPLATES` and `create_pdf_styles`.
  - **DOCX**: Uses `python-docx`. Markdown-to-DOCX conversion logic handles custom markers like `{{FIELD:...}}` and `{{TABLE:...}}`.

## Coding Conventions
- **Localization**: All user-facing strings must use the `tr("key")` function. Add new keys to the `TRANSLATIONS` dictionary in `main.py` (supports 'en' and 'fr').
- **Optional Dependencies**: Wrap imports and logic for non-critical features (like Image Generation or DOCX export) in `try...except` blocks to allow the app to run with reduced functionality.
- **PDF Styling**: Do not hardcode styles in render functions. Add new visual themes to `PDF_TEMPLATES` dictionary.
- **Prompt Engineering**: Keep prompts in the global scope or configuration functions (e.g., `DEFAULT_FICHE_PROMPT`). Use f-strings for dynamic injection.

## Key Workflows
- **Pipeline Execution**: The `pipeline_run` function in `main.py` orchestrates the generation flow:
  1. PDF Text Extraction (`extract_lesson_text`)
  2. AI Content Generation (`generate_with_fallback`)
  3. Image Generation (optional)
  4. Document Rendering (`save_fiche_to_pdf` / `save_fiche_to_docx`)
- **ToC Caching**: Table of Contents data is cached in `toc_cache/` to avoid re-parsing PDFs. Use `get_cached_toc` before attempting extraction.

## Specific Implementation Details
- **Markdown Parsing**: The app parses AI-generated Markdown to create ReportLab stories. See `parse_markdown_to_story`.
- **Metadata**: PDF metadata (Title, Class, Duration) is extracted from the generated content and displayed via `create_meta_banner`.
- **Settings**: API keys and preferences are loaded via `load_api_keys_from_settings`.
