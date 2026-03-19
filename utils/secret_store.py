from __future__ import annotations

from typing import Optional

SERVICE_NAME = "FicheGen"

try:
    import keyring
except Exception:  # pragma: no cover - optional dependency runtime guard
    keyring = None


def keyring_available() -> bool:
    return keyring is not None


def get_secret(name: str) -> str:
    if not keyring_available() or not name:
        return ""
    try:
        value = keyring.get_password(SERVICE_NAME, name)
        return (value or "").strip()
    except Exception:
        return ""


def set_secret(name: str, value: str) -> bool:
    if not keyring_available() or not name:
        return False
    try:
        keyring.set_password(SERVICE_NAME, name, (value or "").strip())
        return True
    except Exception:
        return False


def delete_secret(name: str) -> bool:
    if not keyring_available() or not name:
        return False
    try:
        keyring.delete_password(SERVICE_NAME, name)
        return True
    except Exception:
        return False
