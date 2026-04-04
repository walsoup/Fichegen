from unittest.mock import patch

import config


class _DummySettings:
    def __init__(self, values):
        self._values = values

    def value(self, key, default=None):
        return self._values.get(key, default)

    def remove(self, key):
        self._values.pop(key, None)


def test_load_keys_ignores_env_when_legacy_disabled():
    config.API_KEYS.clear()
    settings = _DummySettings({"security_allow_legacy_keys": "false"})

    with patch("config.QtCore.QSettings", return_value=settings), \
         patch("config.get_secret", return_value=""), \
         patch("config.os.getenv", return_value="env-key-1234567890"):
        loaded = config.load_api_keys_from_settings()

    assert loaded is False
    assert config.API_KEYS.get("GEMINI_API_KEY") in (None, "")


def test_load_keys_uses_env_when_legacy_enabled():
    config.API_KEYS.clear()
    settings = _DummySettings({"security_allow_legacy_keys": "true"})

    def _getenv(key, default=""):
        if key == "GEMINI_API_KEY":
            return "env-key-1234567890"
        return default

    with patch("config.QtCore.QSettings", return_value=settings), \
         patch("config.get_secret", return_value=""), \
         patch("config.os.getenv", side_effect=_getenv):
        loaded = config.load_api_keys_from_settings()

    assert loaded is True
    assert config.API_KEYS.get("GEMINI_API_KEY") == "env-key-1234567890"


def test_has_gemini_access_true_when_vertex_configured_without_api_key():
    config.API_KEYS.clear()
    settings = _DummySettings({})

    with patch("config.QtCore.QSettings", return_value=settings), \
         patch("config.os.getenv") as mock_getenv:
        def _getenv(key, default=""):
            if key == "GOOGLE_CLOUD_PROJECT":
                return "demo-project"
            if key == "GOOGLE_CLOUD_LOCATION":
                return "europe-west1"
            if key == "GOOGLE_GENAI_USE_VERTEXAI":
                return "true"
            return default
        mock_getenv.side_effect = _getenv
        assert config.has_gemini_access("GEMINI_API_KEY") is True


def test_has_gemini_access_false_without_api_key_or_vertex():
    config.API_KEYS.clear()
    settings = _DummySettings({})

    with patch("config.QtCore.QSettings", return_value=settings), \
         patch("config.os.getenv", return_value=""):
        assert config.has_gemini_access("GEMINI_API_KEY") is False
