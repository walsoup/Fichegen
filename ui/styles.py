"""
Minimal stylesheet for FicheGen.
On macOS, we rely primarily on native appearance for best results.
This file provides only subtle enhancements without overriding the system look.
"""

# Native macOS - no stylesheet needed, use system appearance
MACOS_NATIVE = ""

# Minimal polish - just subtle enhancements
MACOS_POLISH = """
/* Subtle polish without overriding native appearance */

/* Better group box titles */
QGroupBox {
    font-weight: 500;
    padding-top: 8px;
    margin-top: 8px;
}

QGroupBox::title {
    subcontrol-origin: margin;
    subcontrol-position: top left;
    padding: 0 4px;
}

/* Slightly more prominent generate buttons */
QPushButton#generate_btn,
QPushButton#generate_eval_btn,
QPushButton#generate_quiz_btn {
    font-weight: 600;
}

/* Better progress bar */
QProgressBar {
    border-radius: 4px;
    text-align: center;
    min-height: 8px;
    max-height: 12px;
}

/* Better status label */
QLabel#status_label {
    color: #666;
    font-size: 11px;
}

/* Subtle list widget styling */
QListWidget {
    border-radius: 6px;
}

/* Plain text edit improvements */
QPlainTextEdit {
    border-radius: 6px;
}

QTextEdit {
    border-radius: 6px;
}
"""

# Light theme - minimal customization
MACOS_LIGHT = MACOS_POLISH

# Dark theme - same minimal polish
MACOS_DARK = MACOS_POLISH

def get_stylesheet(theme="system"):
    """Get stylesheet for the given theme.
    
    On macOS, we return minimal styling to preserve native appearance.
    
    Args:
        theme: "system", "light", or "dark"
    
    Returns:
        Stylesheet string (minimal polish or empty)
    """
    # Return minimal polish - works for both light and dark
    return MACOS_POLISH
