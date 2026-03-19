# FicheGen

FicheGen is an intelligent pedagogical content generator designed for teachers. It uses Google's Gemini AI to analyze educational guides (PDFs) and generate structured lesson plans (fiches), evaluations, and quizzes.

## Features

*   **Intelligent Analysis**: Extracts and analyzes content from PDF teacher guides.
*   **Automatic Generation**: Creates structured pedagogical fiches, evaluations, and quizzes.
*   **Multiple Formats**: Exports to PDF and DOCX.
*   **Customizable**: Supports various PDF templates and formatting options.
*   **Multi-language Support**: Interface available in English and French.
*   **Provider Visibility Toggle**: OpenRouter references can be shown/hidden from the UI in Preferences.
*   **Built-in Updater**: Manual update manager (Help/About) can fetch latest source from GitHub and rebuild the macOS app.

## Prerequisites

*   Python 3.9+
*   Google Gemini API Key (get one from [Google AI Studio](https://ai.google.dev/))
*   macOS 12 users: install the pinned dependencies from `requirements.txt` so Qt stays on a compatible release line

## Installation

1.  Clone the repository:
    ```bash
    git clone https://github.com/yourusername/fichegen.git
    cd fichegen
    ```

2.  Install the required dependencies:
    ```bash
    pip install -r requirements.txt
    ```

    If you already installed a newer `PyQt6`, reinstall the pinned version:
    ```bash
    pip install --upgrade --force-reinstall "PyQt6>=6.7,<6.8"
    ```

## Usage

1.  Run the application:
    ```bash
    python main.py
    ```

2.  **Configuration**:
    *   Go to **Preferences** (Cmd+, or File > Preferences).
    *   Enter your **Gemini API Key** in the "AI & Models" tab.
    *   Optionally enable OpenRouter UI references from the same tab if you want provider-related guidance visible.
    *   Set the **Input Guides** folder (where your PDF guides are stored).
    *   Set the **Output Folder** (where generated files will be saved).
    *   Optional: enable/disable automatic update checks in **Preferences > General**.

## Updates

FicheGen now includes a user-friendly update flow:

1. Open **Help > Check for Updates…** or use the **Check for Updates…** button in **About**.
2. Click **Check Now**.
3. If an update is available, click **Build & Update**.
4. The updater pulls from `https://github.com/walsoup/Fichegen`, installs dependencies, rebuilds the app, and replaces `/Applications/FicheGen.app` automatically.

Notes:

* Updates are always manual to avoid surprise installs.
* Automatic mode only checks availability at startup (can be turned off).
* If needed, macOS will ask for admin privileges during installation to `/Applications`.

## Security Notes

*   On macOS, API keys are stored in Keychain via the Python `keyring` backend.
*   Legacy plaintext settings and `keys.txt` values are still read for compatibility and migrated when possible.

3.  **Generating Content**:
    *   Select the **Class Level** and **Subject**.
    *   Enter a **Lesson Topic** (e.g., "Le cycle de l'eau").
    *   Click **Generate Fiche**.

## File Structure

*   `core/`: Core logic for AI interaction and processing.
*   `document/`: PDF and DOCX generation logic.
*   `ui/`: PyQt6 user interface.
*   `utils/`: Helper functions.
*   `guides/`: Default directory for input PDF guides.
*   `fiches/`: Default directory for output files.

## License

[License Name]
