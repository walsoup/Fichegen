import os
import threading
from typing import Optional, Dict, List, Any
from google import genai
from google.genai import types

from config import (
    API_KEYS, IMAGE_MODEL, STYLE_TEMPLATES, COMPLEXITY_LEVELS
)

# Image generation using Google Imagen 4
_IMAGE_CLIENT = None
_IMAGE_CLIENT_KEY = None
_IMAGE_CLIENT_LOCK = threading.Lock()

def _get_image_client(api_key: Optional[str] = None) -> Optional[genai.Client]:
    """Return a cached google-genai client for image-related calls."""
    global _IMAGE_CLIENT, _IMAGE_CLIENT_KEY
    
    key = (api_key or os.getenv("GEMINI_API_KEY") or API_KEYS.get("GEMINI_API_KEY") or "").strip()
    if not key:
        return None
    with _IMAGE_CLIENT_LOCK:
        if _IMAGE_CLIENT is None or _IMAGE_CLIENT_KEY != key:
            _IMAGE_CLIENT = genai.Client(api_key=key)
            _IMAGE_CLIENT_KEY = key
    return _IMAGE_CLIENT

def _extract_function_args(response) -> Optional[Dict[str, Any]]:
    """Pull function call arguments out of a Gemini response."""
    if not response or not getattr(response, "candidates", None):
        return None
    for candidate in response.candidates:
        content = getattr(candidate, "content", None)
        if not content:
            continue
        for part in getattr(content, "parts", []) or []:
            fn_call = getattr(part, "function_call", None)
            if fn_call and getattr(fn_call, "args", None):
                return fn_call.args
    return None

def _draft_image_plan(client: genai.Client, original_prompt: str, class_level: str, style: str) -> Dict[str, Any]:
    """Use a reasoning model with function calling to plan the illustration."""
    plan_function = types.FunctionDeclaration(
        name="propose_image_plan",
        description="Prépare un plan détaillé pour une illustration pédagogique.",
        parameters=types.Schema(
            type=types.Type.OBJECT,
            properties={
                "prompt_summary": types.Schema(type=types.Type.STRING),
                "scene_layout": types.Schema(type=types.Type.STRING),
                "foreground_elements": types.Schema(type=types.Type.ARRAY, items=types.Schema(type=types.Type.STRING)),
                "background_elements": types.Schema(type=types.Type.ARRAY, items=types.Schema(type=types.Type.STRING), nullable=True),
                "text_overlays": types.Schema(type=types.Type.ARRAY, items=types.Schema(type=types.Type.STRING), nullable=True),
                "labels": types.Schema(type=types.Type.ARRAY, items=types.Schema(type=types.Type.STRING), nullable=True),
                "color_palette": types.Schema(type=types.Type.STRING, nullable=True),
                "lighting": types.Schema(type=types.Type.STRING, nullable=True),
                "notes": types.Schema(type=types.Type.STRING, nullable=True),
            },
            required=["prompt_summary", "scene_layout", "foreground_elements"],
        ),
    )
    level_label = (class_level or "primaire").upper()
    plan_tool = types.Tool(function_declarations=[plan_function])
    plan_prompt = f"""Tu es directeur artistique pour une illustration éducative destinée à des élèves de {level_label}.
Analyse la demande ci-dessous et prépare un plan précis en utilisant la fonction `propose_image_plan`.

Demande de l'enseignant:
{original_prompt}

Consigne:
- Identifie l'élément principal à mettre en avant.
- Décris l'agencement (avant-plan, arrière-plan, disposition).
- Décris les éléments textuels ou étiquettes à ajouter si utiles.
- Recommande une palette de couleurs sobre et cohérente.
- Mentionne des notes pédagogiques si nécessaire (ex: montrer une flèche, un encart explicatif).
"""
    try:
        # Use flash for image planning (faster and cheaper, planning doesn't need Pro)
        response = client.models.generate_content(
            model="gemini-2.5-flash", contents=[plan_prompt],
            config=types.GenerateContentConfig(tools=[plan_tool], temperature=0.2)
        )
    except Exception as exc:
        print(f"Error generating image plan: {exc}")
        return {}
    plan_args = _extract_function_args(response)
    if isinstance(plan_args, dict):
        return plan_args
    fallback_summary = (getattr(response, "text", "") or "").strip()
    if fallback_summary:
        return {"prompt_summary": fallback_summary, "scene_layout": "mise en scène frontale", "foreground_elements": [fallback_summary]}
    return {}

def _compose_image_prompt(original_prompt: str, plan: Dict[str, Any], class_level: str, style: str, aspect_ratio: str) -> str:
    """Merge user intent, pedagogical constraints, and AI plan into a single prompt."""
    style_guidance = STYLE_TEMPLATES.get(style, STYLE_TEMPLATES["diagram"])
    level_label = (class_level or "primaire").lower()
    complexity = COMPLEXITY_LEVELS.get(level_label, "clair et pédagogique")
    lines = [
        f"Educational illustration prompt for {(class_level or 'primary').upper()} students.",
        f"Target complexity: {complexity}.",
        f"Overall style guidance: {style_guidance}.",
        f"Requested aspect ratio: {aspect_ratio}.",
        "Render with clean lines, high legibility and classroom-friendly aesthetics.",
    ]
    summary = plan.get("prompt_summary")
    if summary:
        lines.append(f"Scene objective: {summary}.")
    layout = plan.get("scene_layout")
    if layout:
        lines.append(f"Layout: {layout}.")
    foreground = plan.get("foreground_elements") or []
    if foreground:
        lines.append("Foreground elements:")
        for element in foreground:
            lines.append(f"- {element}")
    background = plan.get("background_elements") or []
    if background:
        lines.append("Background elements:")
        for element in background:
            lines.append(f"- {element}")
    labels = plan.get("labels") or []
    if labels:
        lines.append("Labels or annotations to include:")
        for label in labels:
            lines.append(f"- {label}")
    overlays = plan.get("text_overlays") or []
    if overlays:
        lines.append("Text overlays:")
        for overlay in overlays:
            lines.append(f"- {overlay}")
    color_palette = plan.get("color_palette")
    if color_palette:
        lines.append(f"Suggested color palette: {color_palette}.")
    lighting = plan.get("lighting")
    if lighting:
        lines.append(f"Lighting guidance: {lighting}.")
    notes = plan.get("notes")
    if notes:
        lines.append(f"Additional notes: {notes}.")
    lines.append("Avoid photorealism, keep lines crisp and text perfectly legible.")
    lines.append("Teacher request summary: " + original_prompt.strip())
    return "\n".join(lines)

def generate_illustration(prompt: str, class_level: str = "ce2", style: str = "diagram", aspect_ratio: str = "16:9", api_key: Optional[str] = None) -> Optional[bytes]:
    """Generate a simple educational illustration using Imagen API."""
    client = _get_image_client(api_key)
    if client is None:
        print("⚠️ Missing Gemini API key for image generation")
        return None
    style_key = style if style in STYLE_TEMPLATES else "diagram"
    base_prompt = prompt.strip()
    plan = _draft_image_plan(client, base_prompt, class_level or "primaire", style_key)
    final_prompt = _compose_image_prompt(base_prompt, plan, class_level or "primaire", style_key, aspect_ratio)
    image_config = None
    try:
        image_config = types.ImageConfig(aspect_ratio=aspect_ratio)
    except Exception:
        pass
    try:
        response = client.models.generate_content(
            model=IMAGE_MODEL, contents=[final_prompt],
            config=types.GenerateContentConfig(temperature=0.2, response_modalities=["IMAGE"], image_config=image_config)
        )
    except Exception as exc:
        print(f"⚠️ Image generation error: {exc}")
        return None
    if not response or not getattr(response, "candidates", None):
        return None
    for candidate in response.candidates:
        content = getattr(candidate, "content", None)
        if not content:
            continue
        for part in getattr(content, "parts", []) or []:
            inline = getattr(part, "inline_data", None)
            if not inline or inline.data is None:
                continue
            data = inline.data
            if isinstance(data, bytes):
                return data
            try:
                import base64
                return base64.b64decode(data)
            except Exception:
                continue
    return None

def generate_fiche_illustration(lesson_topic: str, class_level: str, context: Optional[str] = None, api_key: Optional[str] = None) -> Optional[bytes]:
    """Generate a single illustration for a pedagogical fiche."""
    context_text = f"\nContext: {context}" if context else ""
    prompt = f"""Create a simple educational illustration for a lesson about: {lesson_topic}
{context_text}

This illustration will accompany a pedagogical fiche for {class_level.upper()} students.

Requirements:
- Simple, clean, educational style
- Supports the lesson concept visually
- Appropriate for classroom use
- Professional but approachable
- Minimalist design
- Can include simple labels if helpful
- White background"""
    style = "coloring" if class_level.lower() in ["cp", "ce1"] else "diagram"
    return generate_illustration(prompt=prompt, class_level=class_level, style=style, aspect_ratio="16:9", api_key=api_key)

def generate_evaluation_illustrations(topics: List[str], class_level: str, num_images: int = 2, api_key: Optional[str] = None) -> List[Dict[str, Any]]:
    """Generate illustrations for an evaluation based on topics."""
    illustrations = []
    is_young = class_level.lower() in ["cp", "ce1"]
    for topic in topics[:num_images]:
        if is_young:
            prompt = f"""Create a simple coloring page about: {topic}
The image should be:
- Black and white line drawing only
- Thick, clear outlines perfect for coloring
- Age-appropriate for {class_level.upper()} students
- Simple shapes and minimal details
- No shading, no gradients
- Large areas suitable for crayons or markers
- Educational and fun"""
            image_data = generate_illustration(prompt=prompt, class_level=class_level, style="coloring", aspect_ratio="1:1", api_key=api_key)
            description = f"Coloriage: {topic}"
        else:
            prompt = f"""Create a clear educational diagram showing: {topic}
Requirements:
- Clean, simple illustration style
- Educational textbook quality
- Clear labels with arrows or lines
- Minimal colors (2-3 colors maximum)
- Easy to understand for {class_level.upper()} students
- Hand-drawn diagram aesthetic
- White background"""
            image_data = generate_illustration(prompt=prompt, class_level=class_level, style="diagram", aspect_ratio="16:9", api_key=api_key)
            description = f"Illustration: {topic}"
        if image_data:
            illustrations.append({"topic": topic, "image_data": image_data, "description": description, "type": "coloring" if is_young else "diagram"})
    return illustrations

def save_image_to_file(image_data: bytes, output_path: str) -> bool:
    """Save generated image bytes to a file."""
    try:
        from PIL import Image
        from io import BytesIO
        image = Image.open(BytesIO(image_data))
        os.makedirs(os.path.dirname(output_path) if os.path.dirname(output_path) else ".", exist_ok=True)
        image.save(output_path, "PNG")
        return True
    except Exception as e:
        print(f"Error saving image to {output_path}: {e}")
        return False

def image_to_base64(image_data: bytes) -> str:
    """Convert image bytes to base64 string for embedding in markdown."""
    import base64
    return base64.b64encode(image_data).decode('utf-8')
