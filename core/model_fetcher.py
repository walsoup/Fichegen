import json
from typing import List, Tuple, Optional
from core.ai import get_genai_client, _generate_with_model

# Use Gemini Flash for the model analysis (fast and capable)
ANALYZER_MODEL = "gemini-2.5-flash-lite"

def fetch_available_models() -> List[str]:
    """
    Fetches available models from Gemini API.
    Returns a list of model name strings.
    """
    client = get_genai_client()
    if not client:
        return []
    
    try:
        models = list(client.models.list())
        model_names = []
        for m in models:
            name = getattr(m, 'name', '') or ''
            if name.startswith('models/'):
                name = name[7:]
            if name:
                model_names.append(name)
        return model_names
    except Exception as e:
        print(f"Error fetching models: {e}")
        return []

def find_best_models_with_ai(model_names: List[str], current_pro: str, current_flash: str) -> Tuple[Optional[str], Optional[str]]:
    """
    Uses Gemini with Google Search to analyze the model list and determine the latest Pro and Flash models.
    Returns (latest_pro_name, latest_flash_name) or (None, None) if no update needed.
    """
    if not model_names:
        return None, None
    
    # Filter to only gemini models
    gemini_models = [m for m in model_names if 'gemini' in m.lower()]
    
    if not gemini_models:
        return None, None
    
    prompt = f"""You are an AI model version analyst. Analyze this list of available Gemini models and determine:
1. The BEST/LATEST Gemini Pro model (for high-quality generation)
2. The BEST/LATEST Gemini Flash model (for fast generation)

Use Google Search to verify what the latest Gemini models are if needed.

Consider:
- Higher version numbers are better (2.5 > 2.0 > 1.5, and 3.0 > 2.5)
- "latest" suffix means the most recent stable version
- Preview/experimental versions (exp, preview) are often NEWER and should be preferred
- Models with higher date suffixes (like 0130 > 0827) are newer

Available models from API:
{json.dumps(gemini_models, indent=2)}

Current configured models:
- Pro: {current_pro}
- Flash: {current_flash}

Respond with ONLY valid JSON in this exact format:
{{"best_pro": "model-name-here", "best_flash": "model-name-here", "pro_is_newer": true/false, "flash_is_newer": true/false}}

- Set pro_is_newer to true ONLY if best_pro is definitively better/newer than "{current_pro}"
- Set flash_is_newer to true ONLY if best_flash is definitively better/newer than "{current_flash}"
- If the current model is already the best, set the corresponding is_newer to false"""

    try:
        response = _generate_with_model(
            ANALYZER_MODEL,
            prompt,
            temperature=0.4,  # Low temperature for consistent output
            # thinking_level="MEDIUM",  # Removed: gemini-2.5-flash may not support thinking
            enable_google_search=True,  # Allow searching for latest model info
        )
        
        text = (response.text or "").strip()
        
        # Try to parse JSON from the response
        # Handle markdown code blocks if present
        if "```json" in text:
            text = text.split("```json")[1].split("```")[0].strip()
        elif "```" in text:
            text = text.split("```")[1].split("```")[0].strip()
        
        data = json.loads(text)
        
        best_pro = data.get("best_pro")
        best_flash = data.get("best_flash")
        pro_is_newer = data.get("pro_is_newer", False)
        flash_is_newer = data.get("flash_is_newer", False)
        
        # Validate that the suggested models actually exist in the list
        if best_pro and best_pro not in gemini_models:
            print(f"Warning: AI suggested non-existent pro model: {best_pro}")
            pro_is_newer = False
        if best_flash and best_flash not in gemini_models:
            print(f"Warning: AI suggested non-existent flash model: {best_flash}")
            flash_is_newer = False
        
        # Only return models that are actually newer
        return (
            best_pro if pro_is_newer else None,
            best_flash if flash_is_newer else None
        )
        
    except Exception as e:
        print(f"Error using AI to analyze models: {e}")
        return None, None
