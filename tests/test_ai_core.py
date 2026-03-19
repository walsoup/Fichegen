import pytest
from unittest.mock import patch, MagicMock
from core.ai import _call_model, generate_with_fallback
from google.genai import types

class MockResponse:
    def __init__(self, text="mock response", parsed=None):
        self.text = text
        self.parsed = parsed

def test_call_model_basic():
    with patch('core.ai.get_genai_client') as mock_get_client:
        mock_client = MagicMock()
        mock_get_client.return_value = mock_client
        mock_client.models.generate_content.return_value = MockResponse()
        
        response = _call_model("gemini-pro", "hello")
        
        assert response.text == "mock response"
        mock_client.models.generate_content.assert_called_once()

def test_fallback_mechanism():
    # Test that it falls back to flash if pro fails
    with patch('core.ai.get_genai_client') as mock_get_client:
        # Mock settings to ensure fallback is enabled
        with patch('PyQt6.QtCore.QSettings') as mock_settings:
            settings_instance = MagicMock()
            # enable_model_fallback=true, gemini_use_pro=true
            settings_instance.value.side_effect = lambda key, default: "true"
            mock_settings.return_value = settings_instance
            
            mock_get_client.return_value = MagicMock()
            
            # Mock _generate_with_model to fail first, then succeed
            with patch('core.ai._generate_with_model') as mock_gen_model:
                mock_gen_model.side_effect = [
                    Exception("Pro model failed"), # First call (Pro) fails
                    MockResponse("Flash response") # Second call (Flash) succeeds
                ]
                
                queue = MagicMock()
                
                # Mock API key presence
                with patch.dict('core.ai.API_KEYS', {'GEMINI_API_KEY': 'fake_key'}):
                    response = generate_with_fallback(
                        prompt="test prompt",
                        temperature=0.7,
                        queue=queue,
                        purpose="testing"
                    )
                
                assert response.text == "Flash response"
                assert mock_gen_model.call_count == 2

def test_google_search_not_enabled_fallback():
    """Test that google_search is disabled and retried when not supported by model."""
    with patch('core.ai.get_genai_client') as mock_get_client:
        mock_client = MagicMock()
        mock_get_client.return_value = mock_client
        
        # First call fails with google_search not enabled, second succeeds
        call_count = [0]
        def side_effect(*args, **kwargs):
            call_count[0] += 1
            if call_count[0] == 1:
                raise Exception("google_search is not enabled for this model")
            return MockResponse("success without search")
        
        mock_client.models.generate_content.side_effect = side_effect
        
        response = _call_model("gemini-pro", "hello", enable_google_search=True)
        
        assert response.text == "success without search"
        assert call_count[0] == 2  # Retried once

def test_rate_limit_retry():
    """Test that rate limiting triggers exponential backoff retry."""
    with patch('core.ai.get_genai_client') as mock_get_client:
        with patch('time.sleep') as mock_sleep:  # Don't actually wait
            mock_client = MagicMock()
            mock_get_client.return_value = mock_client
            
            call_count = [0]
            def side_effect(*args, **kwargs):
                call_count[0] += 1
                if call_count[0] <= 2:
                    raise Exception("Rate limit exceeded")
                return MockResponse("success after retry")
            
            mock_client.models.generate_content.side_effect = side_effect
            
            response = _call_model("gemini-pro", "hello")
            
            assert response.text == "success after retry"
            assert call_count[0] == 3  # Failed 2x, succeeded on 3rd
            assert mock_sleep.call_count == 2  # Waited twice

def test_invalid_api_key_error():
    """Test that invalid API key produces a clear error message."""
    with patch('core.ai.get_genai_client') as mock_get_client:
        mock_client = MagicMock()
        mock_get_client.return_value = mock_client
        mock_client.models.generate_content.side_effect = Exception("API key not valid. Please pass a valid API key.")
        
        with pytest.raises(RuntimeError) as exc_info:
            _call_model("gemini-pro", "hello")
        
        assert "API key" in str(exc_info.value)
        assert "Preferences" in str(exc_info.value)

def test_thinking_level_not_supported_fallback():
    """Test that thinking_level is disabled and retried when not supported."""
    with patch('core.ai.get_genai_client') as mock_get_client:
        mock_client = MagicMock()
        mock_get_client.return_value = mock_client
        
        call_count = [0]
        def side_effect(*args, **kwargs):
            call_count[0] += 1
            if call_count[0] == 1:
                raise Exception("Thinking level is not supported for this model")
            return MockResponse("success without thinking")
        
        mock_client.models.generate_content.side_effect = side_effect
        
        response = _call_model("gemini-flash", "hello", thinking_level="HIGH")
        
        assert response.text == "success without thinking"
        assert call_count[0] == 2
