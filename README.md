# FicheGen

FicheGen is an intelligent pedagogical content generator designed for teachers. It uses Google's Gemini AI to analyze educational guides (PDFs) and generate structured lesson plans (fiches), evaluations, and quizzes.

## Features

*   **Intelligent Analysis**: Extracts and analyzes content from PDF teacher guides.
*   **Automatic Generation**: Creates structured pedagogical fiches, evaluations, and quizzes.
*   **Multiple Formats**: Exports to PDF and DOCX.
*   **Customizable**: Supports various PDF templates and formatting options.
*   **Multi-language Support**: Interface available in English and French.

## Prerequisites

*   Python 3.9+
*   Google Gemini API Key (get one from [Google AI Studio](https://ai.google.dev/))

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

## Usage

1.  Run the application:
    ```bash
    python main.py
    ```

2.  **Configuration**:
    *   Go to **Preferences** (Cmd+, or File > Preferences).
    *   Enter your **Gemini API Key** in the "AI & Models" tab.
    *   Set the **Input Guides** folder (where your PDF guides are stored).
    *   Set the **Output Folder** (where generated files will be saved).

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
