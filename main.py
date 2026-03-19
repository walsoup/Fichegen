import sys
import os
import platform
from importlib import metadata

# Add the project root to the python path so imports work correctly
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

def _parse_version(value: str) -> tuple[int, ...]:
    parts = []
    for chunk in (value or "").split('.'):
        if chunk.isdigit():
            parts.append(int(chunk))
        else:
            break
    return tuple(parts)


def _check_macos_qt_compatibility() -> tuple[bool, str]:
    if platform.system() != "Darwin":
        return True, ""

    mac_version = _parse_version(platform.mac_ver()[0])
    if not mac_version:
        return True, ""

    try:
        pyqt_version = metadata.version("PyQt6")
    except metadata.PackageNotFoundError:
        return False, "PyQt6 is not installed. Run `pip install -r requirements.txt`."

    if mac_version < (13, 0) and _parse_version(pyqt_version) >= (6, 8):
        return False, (
            f"Installed PyQt6 {pyqt_version} requires macOS 13 or later. "
            "This Mac is running macOS "
            f"{platform.mac_ver()[0]}.\n\n"
            "Fix:\n"
            "  pip install --upgrade --force-reinstall \"PyQt6>=6.7,<6.8\"\n"
            "or reinstall all dependencies with:\n"
            "  pip install --upgrade --force-reinstall -r requirements.txt"
        )

    return True, ""


is_compatible, compatibility_message = _check_macos_qt_compatibility()
if not is_compatible:
    print(compatibility_message)
    sys.exit(1)

from PyQt6 import QtWidgets, QtGui
from ui.main_window import MainWindow
from config import ICON_PATH

def main():
    app = QtWidgets.QApplication(sys.argv)
    app.setApplicationName("FicheGen")
    app.setOrganizationName("FicheGen")
    app.setOrganizationDomain("fichegen.com")

    # Set application icon
    if os.path.exists(ICON_PATH):
        app.setWindowIcon(QtGui.QIcon(ICON_PATH))

    window = MainWindow()
    window.show()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
