import os
import json
from typing import Optional, List, Dict, Any
from reportlab.lib import colors
from config import RATINGS_FILE

def _clamp_temperature(value: Optional[float]) -> float:
    if value is None:
        return 0.5
    return max(0.0, min(1.0, float(value)))

def safe_color(color_value, default='#2E8B57'):
    """Safely convert a color value to a ReportLab color object, with fallback to default."""
    if color_value is None:
        color_value = default
    
    try:
        # If it's already a color object, return it
        if hasattr(color_value, 'red'):
            return color_value
        
        # Convert string to color
        if isinstance(color_value, str):
            color_value = color_value.strip()
            # Ensure it's a valid hex color
            if not color_value.startswith('#'):
                color_value = default
            return colors.toColor(color_value)
        
        # For any other type, use default
        return colors.toColor(default)
    except Exception:
        # If anything fails, return the default color
        return colors.toColor(default)

# --- Ratings persistence helpers ---
def _ensure_ratings_dir() -> bool:
    """Ensure ratings directory exists. Returns True if successful."""
    try:
        os.makedirs(os.path.dirname(RATINGS_FILE), exist_ok=True)
        return True
    except (OSError, PermissionError):
        return False

def load_ratings() -> List[Dict[str, Any]]:
    """Load ratings from JSON file with error recovery."""
    if not os.path.exists(RATINGS_FILE):
        return []
    
    try:
        with open(RATINGS_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
            # Validate it's a list
            return data if isinstance(data, list) else []
    except (FileNotFoundError, json.JSONDecodeError, UnicodeDecodeError, IOError):
        # If file is corrupted, try to recover by backing it up
        try:
            backup_path = f"{RATINGS_FILE}.backup"
            if os.path.exists(RATINGS_FILE):
                os.rename(RATINGS_FILE, backup_path)
        except (OSError, PermissionError):
            pass
        return []

def save_rating_record(record: Dict[str, Any]) -> bool:
    """Save rating record with atomic write to prevent corruption."""
    if not _ensure_ratings_dir():
        return False
    
    data = load_ratings()
    data.append(record)
    
    # Write to temporary file first (atomic operation)
    temp_file = f"{RATINGS_FILE}.tmp"
    try:
        with open(temp_file, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        
        # Atomic rename (on POSIX systems)
        os.replace(temp_file, RATINGS_FILE)
        return True
    except (IOError, OSError, PermissionError) as e:
        # Clean up temp file if it exists
        try:
            if os.path.exists(temp_file):
                os.remove(temp_file)
        except (OSError, PermissionError):
            pass
        return False

def get_top_rated_examples(n: int = 2, min_chars: int = 400) -> List[Dict[str, str]]:
    """
    Get top-rated fiche examples for prompt construction.
    
    Args:
        n: Maximum number of examples to return
        min_chars: Minimum content length to qualify
    
    Returns:
        List of dicts with keys: topic, class_level, content
    """
    data = load_ratings()
    # Sort by rating (desc) then timestamp (newest first)
    data.sort(key=lambda r: (r.get("rating", 0), r.get("timestamp", "")), reverse=True)
    
    examples = []
    for r in data:
        content = r.get("content", "")
        if len(content) >= min_chars:
            examples.append({
                "topic": r.get("topic", "Unknown"),
                "class_level": r.get("class_level", "Unknown"),
                "content": content
            })
            if len(examples) >= n:
                break
    return examples
