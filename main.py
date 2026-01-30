import sys
import os

# Add the project root to the python path so imports work correctly
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

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
