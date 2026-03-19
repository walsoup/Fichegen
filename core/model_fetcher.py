import re
from typing import List, Tuple, Optional

from core.ai import get_genai_client

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
        return sorted(set(model_names))
    except Exception as e:
        print(f"Error fetching models: {e}")
        return []

def _parse_version(model_name: str) -> tuple[int, int]:
    match = re.search(r'gemini-(\d+)(?:\.(\d+))?', model_name.lower())
    if not match:
        return (0, 0)
    return (int(match.group(1)), int(match.group(2) or 0))


def _parse_date_tokens(model_name: str) -> tuple[int, ...]:
    lower = model_name.lower()
    match = re.search(r'(20\d{2})[-_]?([01]\d)[-_]?([0-3]\d)', lower)
    if match:
        return tuple(int(part) for part in match.groups())
    return tuple(int(part) for part in re.findall(r'(?<!\d)(\d{4})(?!\d)', lower))


def _is_candidate_for_role(model_name: str, role: str) -> bool:
    lower = model_name.lower()
    if 'gemini' not in lower:
        return False
    if any(blocked in lower for blocked in ('embedding', 'aqa', 'tts', 'transcribe', 'image', 'vision', 'live')):
        return False
    if role == 'pro':
        return 'pro' in lower and 'flash' not in lower
    if role == 'flash':
        return 'flash' in lower and 'flash-image' not in lower
    return False


def _model_rank(model_name: str, role: str) -> tuple:
    lower = model_name.lower()
    stability_score = 40
    if 'latest' in lower:
        stability_score = 60
    elif 'stable' in lower:
        stability_score = 50
    elif 'preview' in lower:
        stability_score = 30
    elif 'exp' in lower or 'experimental' in lower:
        stability_score = 20

    quality_score = 10 if 'lite' not in lower else -15
    if 'thinking' in lower:
        quality_score += 2

    role_bonus = 0
    if role == 'pro' and lower.endswith('-pro'):
        role_bonus = 3
    elif role == 'flash' and lower.endswith('-flash'):
        role_bonus = 3

    return (
        _parse_version(lower),
        stability_score,
        _parse_date_tokens(lower),
        quality_score,
        role_bonus,
        len(model_name),
    )


def _pick_best_model(model_names: List[str], role: str) -> Optional[str]:
    candidates = [name for name in model_names if _is_candidate_for_role(name, role)]
    if not candidates:
        return None
    return max(candidates, key=lambda name: _model_rank(name, role))


def _is_model_newer(candidate: Optional[str], current: str, role: str) -> bool:
    if not candidate or not current or candidate == current:
        return False
    return _model_rank(candidate, role) > _model_rank(current, role)


def find_best_models_with_ai(model_names: List[str], current_pro: str, current_flash: str) -> Tuple[Optional[str], Optional[str]]:
    """
    Deterministically analyze available Gemini models and determine newer Pro and Flash variants.
    """
    if not model_names:
        return None, None

    gemini_models = [m for m in model_names if 'gemini' in m.lower()]
    if not gemini_models:
        return None, None

    best_pro = _pick_best_model(gemini_models, 'pro')
    best_flash = _pick_best_model(gemini_models, 'flash')
    return (
        best_pro if _is_model_newer(best_pro, current_pro, 'pro') else None,
        best_flash if _is_model_newer(best_flash, current_flash, 'flash') else None,
    )
