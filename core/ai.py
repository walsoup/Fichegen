import json
import time
import hashlib
from typing import Optional, List, Dict, Any
from functools import lru_cache
from google import genai
from google.genai import types
from PyQt6 import QtCore

from config import (
    API_KEYS, _GENAI_CLIENT, _GENAI_CLIENT_KEY, _GENAI_CLIENT_LOCK,
    get_configured_gemini_model, get_configured_flash_model,
    _clamp_temperature, has_gemini_access, get_vertex_ai_config
)

def _api_key_route_label(api_key_name: str) -> str:
    use_vertex, project, location = get_vertex_ai_config()
    if use_vertex and project:
        return f"Vertex AI ({project}/{location})"
    if api_key_name == "GEMINI_API_KEY":
        return "Gemini key"
    return api_key_name


def get_genai_client(api_key_name: str = "GEMINI_API_KEY") -> Optional[genai.Client]:
    """Return a cached google-genai client configured with API key or Vertex AI."""
    api_key = API_KEYS.get(api_key_name)
    use_vertex, project, location = get_vertex_ai_config()
    if not api_key and not (use_vertex and project):
        return None

    client_cache_key = (
        f"vertex:{project}:{location}"
        if (use_vertex and project)
        else f"key:{hashlib.sha256((api_key or '').encode('utf-8')).hexdigest()[:16]}"
    )

    global _GENAI_CLIENT, _GENAI_CLIENT_KEY
    with _GENAI_CLIENT_LOCK:
        if _GENAI_CLIENT is None or _GENAI_CLIENT_KEY != client_cache_key:
            if use_vertex and project:
                _GENAI_CLIENT = genai.Client(
                    vertexai=True,
                    project=project,
                    location=location,
                )
            else:
                _GENAI_CLIENT = genai.Client(api_key=api_key)
            _GENAI_CLIENT_KEY = client_cache_key
    return _GENAI_CLIENT

def get_ai_client(api_provider):
    """Get Gemini client - OpenRouter has been removed."""
    try:
        client = get_genai_client("GEMINI_API_KEY")
    except Exception as exc:
        return None, f"Failed to initialise Gemini client: {exc}"

    if client is None:
        return None, "Gemini access is not configured (add API key or Vertex AI project settings)"

    return client, None

def _call_model(
    model_name: str,
    contents,
    *,
    temperature: Optional[float] = 0.5,
    response_schema: Optional[types.Schema] = None,
    response_mime_type: Optional[str] = None,
    tools: Optional[List[types.Tool]] = None,
    tool_config: Optional[types.ToolConfig] = None,
    thinking_level: Optional[str] = None,
    enable_google_search: bool = False,
    enable_url_context: bool = False,
    api_key_name: str = "GEMINI_API_KEY",
    ):
    """
    Shared helper to invoke Gemini models with consistent config handling.
    
    """

    client = get_genai_client(api_key_name)
    if client is None:
        raise RuntimeError("Missing Gemini access (API key or Vertex AI configuration)")

    config_kwargs: Dict[str, Any] = {"temperature": _clamp_temperature(temperature)}
    
    if response_schema is not None:
        config_kwargs["response_schema"] = response_schema
    if response_mime_type is not None:
        config_kwargs["response_mime_type"] = response_mime_type
    
    # Sanitize config based on model capabilities
    model_lower = model_name.lower()
    
    # Gemma models generally do not support tools/search/thinking
    if "gemma" in model_lower:
        thinking_level = None
        enable_google_search = False
        enable_url_context = False

    # Build tools list - combine provided tools with search/url tools
    all_tools = list(tools) if tools else []
    
    if enable_google_search:
        all_tools.append(types.Tool(google_search=types.GoogleSearch()))
    
    if enable_url_context:
        all_tools.append(types.Tool(url_context=types.UrlContext()))
    
    if all_tools:
        config_kwargs["tools"] = all_tools
    
    if tool_config is not None:
        config_kwargs["tool_config"] = tool_config
    
    # Add thinking config if specified
    if thinking_level:
        config_kwargs["thinking_config"] = types.ThinkingConfig(
            thinking_level=thinking_level
        )

    config = types.GenerateContentConfig(**config_kwargs)
    
    def _is_invalid_api_key_error(message: str) -> bool:
        return any(
            token in message for token in (
                "api key not valid",
                "invalid api key",
                "permission denied",
                "unauthorized",
                "authentication",
            )
        )

    def _is_transient_error(message: str) -> bool:
        return any(
            token in message for token in (
                "rate limit",
                "quota",
                "429",
                "temporarily unavailable",
                "service unavailable",
                "deadline exceeded",
                "timed out",
                "timeout",
                "connection reset",
                "network",
            )
        )

    max_retries = 2
    backoff = 1.0

    for attempt in range(max_retries + 1):
        try:
            config = types.GenerateContentConfig(**config_kwargs)
            return client.models.generate_content(
                model=model_name,
                contents=contents,
                config=config,
            )
        except Exception as e:
            message = str(e).lower()

            # Normalize auth errors for clearer UX in callers.
            if _is_invalid_api_key_error(message):
                raise RuntimeError(
                    "Gemini API key is invalid or unauthorized. "
                    "Update it in Preferences and try again."
                ) from e

            # Capability fallback: disable thinking and retry once.
            if "thinking_config" in config_kwargs and "thinking level is not supported" in message:
                config_kwargs.pop("thinking_config", None)
                continue

            # Capability fallback: remove Google Search tool and retry once.
            if "tools" in config_kwargs and (
                "google_search is not enabled" in message or "google search is not enabled" in message
            ):
                filtered_tools = []
                for tool in config_kwargs.get("tools", []):
                    try:
                        if getattr(tool, "google_search", None) is not None:
                            continue
                    except Exception:
                        pass
                    filtered_tools.append(tool)

                if len(filtered_tools) != len(config_kwargs.get("tools", [])):
                    if filtered_tools:
                        config_kwargs["tools"] = filtered_tools
                    else:
                        config_kwargs.pop("tools", None)
                    continue

            # Transient failures: short exponential backoff retries.
            if attempt < max_retries and _is_transient_error(message):
                time.sleep(backoff)
                backoff *= 2
                continue

            raise

def _generate_with_model(
    model_name: str,
    prompt: str,
    temperature: float,
    *,
    response_schema: Optional[types.Schema] = None,
    response_mime_type: Optional[str] = None,
    tools: Optional[List[types.Tool]] = None,
    tool_config: Optional[types.ToolConfig] = None,
    thinking_level: Optional[str] = None,
    enable_google_search: bool = False,
    enable_url_context: bool = False,
    api_key_name: str = "GEMINI_API_KEY",
):
    return _call_model(
        model_name,
        prompt,
        temperature=temperature,
        response_schema=response_schema,
        response_mime_type=response_mime_type,
        tools=tools,
        tool_config=tool_config,
        thinking_level=thinking_level,
        enable_google_search=enable_google_search,
        enable_url_context=enable_url_context,
        api_key_name=api_key_name,
    )

def _generate_with_gemini(
    prompt: str,
    temperature: float,
    *,
    response_schema: Optional[types.Schema] = None,
    response_mime_type: Optional[str] = None,
    tools: Optional[List[types.Tool]] = None,
    tool_config: Optional[types.ToolConfig] = None,
    thinking_level: Optional[str] = "HIGH",  # Default to HIGH for main content gen
    enable_google_search: bool = False,       # Default to True for main content gen
    enable_url_context: bool = False,         # Default to True for main content gen
    api_key_name: str = "GEMINI_API_KEY",
):
    return _generate_with_model(
        get_configured_gemini_model(),
        prompt,
        temperature,
        response_schema=response_schema,
        response_mime_type=response_mime_type,
        tools=tools,
        tool_config=tool_config,
        thinking_level=thinking_level,
        enable_google_search=enable_google_search,
        enable_url_context=enable_url_context,
        api_key_name=api_key_name,
    )

def generate_with_fallback(
    prompt: str,
    temperature: float,
    queue,
    purpose: str,
    *,
    response_schema: Optional[types.Schema] = None,
    response_mime_type: Optional[str] = None,
    tools: Optional[List[types.Tool]] = None,
    tool_config: Optional[types.ToolConfig] = None,
):
    """
    Generate content using Gemini API with the configured model.
    If Pro model fails and fallback is enabled, automatically retries with Flash model.
    
    Args:
        purpose: for logs ("page-finding", "fiche-generation", "evaluation-generation", etc.)
        thinking_level: "NONE", "LOW", "MEDIUM", "HIGH" for thinking mode
        enable_google_search: Allow model to search Google
        enable_url_context: Allow model to fetch and read URLs
        
    Returns the generated response or None if failed.
    """
    settings = QtCore.QSettings("FicheGen", "Pedago")
    enable_fallback = settings.value("enable_model_fallback", "true") == "true"
    use_pro = settings.value("gemini_use_pro", "true") == "true"

    api_key_name = "GEMINI_API_KEY"
    queue.put(("log", f"🔑 API route: {_api_key_route_label(api_key_name)}"))
    if not has_gemini_access(api_key_name):
        queue.put(("log", f"❌ No Gemini access configured for {purpose}"))
        return None
    
    # Try primary model first
    model_name = get_configured_gemini_model()
    queue.put(("log", f"🤖 Using Gemini model: {model_name}"))
    
    try:
        response = _generate_with_model(
            model_name,
            prompt,
            temperature,
            response_schema=response_schema,
            response_mime_type=response_mime_type,
            tools=tools,
            tool_config=tool_config,
            api_key_name=api_key_name,
        )

        response_text = (response.text or "") if response else ""
        if response and (response_text.strip() or getattr(response, "parsed", None)):
            queue.put(("log", f"✅ {purpose}: generation complete"))
            return response
        else:
            raise ValueError("Empty response from model")
            
    except Exception as e:
        queue.put(("log", f"⚠️ {purpose} failed with {model_name}: {e}"))
        
        # If using Pro and fallback is enabled, try Flash
        if use_pro and enable_fallback:
            flash_model = get_configured_flash_model()
            queue.put(("log", f"🔄 Falling back to Flash model: {flash_model}"))
            
            try:
                response = _generate_with_model(
                    flash_model,
                    prompt,
                    temperature,
                    response_schema=response_schema,
                    response_mime_type=response_mime_type,
                    tools=tools,
                    tool_config=tool_config,
                    api_key_name=api_key_name,
                )
                
                response_text = (response.text or "") if response else ""
                if response and (response_text.strip() or getattr(response, "parsed", None)):
                    queue.put(("log", f"✅ {purpose}: generation complete (using fallback model)"))
                    return response
                else:
                    queue.put(("log", f"❌ {purpose}: empty response from fallback model"))
                    return None
                    
            except Exception as fallback_e:
                queue.put(("log", f"❌ {purpose} fallback also failed: {fallback_e}"))
                return None
        else:
            queue.put(("log", f"❌ {purpose} failed (fallback disabled or already using Flash)"))
            return None

def _parse_structured_response(response) -> Optional[Any]:
    """Extract parsed data from a structured Gemini response."""
    if response is None:
        return None

    parsed = getattr(response, "parsed", None)
    if parsed is not None:
        return parsed

    text = (getattr(response, "text", "") or "").strip()
    if not text:
        return None

    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return None

@lru_cache(maxsize=1)
def _fiche_response_schema() -> types.Schema:
    """Schema guiding Gemini to emit consistent fiche JSON."""
    return types.Schema(
        type=types.Type.OBJECT,
        properties={
            "title": types.Schema(type=types.Type.STRING),
            "metadata": types.Schema(
                type=types.Type.OBJECT,
                properties={
                    "chapter_title": types.Schema(type=types.Type.STRING, nullable=True),
                    "lesson_title": types.Schema(type=types.Type.STRING),
                    "duration_minutes": types.Schema(type=types.Type.INTEGER),
                    "class_level": types.Schema(type=types.Type.STRING),
                    "subject": types.Schema(type=types.Type.STRING, nullable=True),
                    "materials": types.Schema(
                        type=types.Type.ARRAY,
                        items=types.Schema(type=types.Type.STRING),
                        nullable=True,
                    ),
                },
                required=["lesson_title", "duration_minutes", "class_level"],
            ),
            "objectives": types.Schema(
                type=types.Type.ARRAY,
                items=types.Schema(type=types.Type.STRING),
            ),
            "phases": types.Schema(
                type=types.Type.ARRAY,
                items=types.Schema(
                    type=types.Type.OBJECT,
                    properties={
                        "name": types.Schema(type=types.Type.STRING),
                        "goal": types.Schema(type=types.Type.STRING, nullable=True),
                        "duration_minutes": types.Schema(type=types.Type.INTEGER, nullable=True),
                        "teacher_steps": types.Schema(
                            type=types.Type.ARRAY,
                            items=types.Schema(type=types.Type.STRING),
                            nullable=True,
                        ),
                        "student_steps": types.Schema(
                            type=types.Type.ARRAY,
                            items=types.Schema(type=types.Type.STRING),
                            nullable=True,
                        ),
                        "materials": types.Schema(
                            type=types.Type.ARRAY,
                            items=types.Schema(type=types.Type.STRING),
                            nullable=True,
                        ),
                        "differentiation": types.Schema(type=types.Type.STRING, nullable=True),
                    },
                    required=["name"],
                ),
            ),
            "evaluation": types.Schema(
                type=types.Type.OBJECT,
                properties={
                    "strategy": types.Schema(type=types.Type.STRING),
                    "questions": types.Schema(
                        type=types.Type.ARRAY,
                        items=types.Schema(type=types.Type.STRING),
                        nullable=True,
                    ),
                    "answer_key": types.Schema(
                        type=types.Type.ARRAY,
                        items=types.Schema(type=types.Type.STRING),
                        nullable=True,
                    ),
                },
                required=["strategy"],
            ),
            "reminders": types.Schema(type=types.Type.STRING, nullable=True),
            "conclusion": types.Schema(type=types.Type.STRING, nullable=True),
        },
        required=["title", "metadata", "objectives", "phases", "evaluation"],
    )

@lru_cache(maxsize=1)
def _evaluation_response_schema() -> types.Schema:
    """Schema guiding Gemini to emit consistent evaluation JSON matching French school format."""
    return types.Schema(
        type=types.Type.OBJECT,
        properties={
            "school_name": types.Schema(type=types.Type.STRING, nullable=True),
            "header": types.Schema(
                type=types.Type.OBJECT,
                properties={
                    "class_level": types.Schema(type=types.Type.STRING),
                    "academic_year": types.Schema(type=types.Type.STRING, nullable=True),
                    "evaluation_number": types.Schema(type=types.Type.INTEGER, nullable=True),
                    "semester": types.Schema(type=types.Type.STRING, nullable=True),
                    "session_label": types.Schema(type=types.Type.STRING, nullable=True),
                    "duration_minutes": types.Schema(type=types.Type.INTEGER),
                    "max_score": types.Schema(type=types.Type.NUMBER),
                    "subject": types.Schema(type=types.Type.STRING, nullable=True),
                },
                required=["class_level", "duration_minutes", "max_score"],
            ),
            "exercises": types.Schema(
                type=types.Type.ARRAY,
                items=types.Schema(
                    type=types.Type.OBJECT,
                    properties={
                        "title": types.Schema(type=types.Type.STRING),
                        "instructions": types.Schema(type=types.Type.STRING),
                        "points": types.Schema(type=types.Type.NUMBER),
                        "questions": types.Schema(
                            type=types.Type.ARRAY,
                            items=types.Schema(
                                type=types.Type.OBJECT,
                                properties={
                                    "prompt": types.Schema(type=types.Type.STRING),
                                    "answer_type": types.Schema(type=types.Type.STRING, nullable=True),
                                    "expected_answer": types.Schema(type=types.Type.STRING, nullable=True),
                                },
                                required=["prompt"],
                            ),
                        ),
                    },
                    required=["title", "instructions", "points", "questions"],
                ),
            ),
            "answer_key": types.Schema(
                type=types.Type.ARRAY,
                items=types.Schema(type=types.Type.STRING),
                nullable=True,
            ),
        },
        required=["header", "exercises"],
    )

@lru_cache(maxsize=1)
def _quiz_response_schema() -> types.Schema:
    """Schema guiding Gemini to emit consistent quiz JSON."""
    return types.Schema(
        type=types.Type.OBJECT,
        properties={
            "title": types.Schema(type=types.Type.STRING),
            "class_level": types.Schema(type=types.Type.STRING),
            "topic": types.Schema(type=types.Type.STRING),
            "subject": types.Schema(type=types.Type.STRING, nullable=True),
            "duration_minutes": types.Schema(type=types.Type.INTEGER),
            "instructions": types.Schema(
                type=types.Type.ARRAY,
                items=types.Schema(type=types.Type.STRING),
                nullable=True,
            ),
            "questions": types.Schema(
                type=types.Type.ARRAY,
                items=types.Schema(
                    type=types.Type.OBJECT,
                    properties={
                        "number": types.Schema(type=types.Type.INTEGER),
                        "type": types.Schema(type=types.Type.STRING),
                        "prompt": types.Schema(type=types.Type.STRING),
                        "options": types.Schema(
                            type=types.Type.ARRAY,
                            items=types.Schema(type=types.Type.STRING),
                            nullable=True,
                        ),
                        "expected_answer": types.Schema(type=types.Type.STRING, nullable=True),
                    },
                    required=["number", "type", "prompt"],
                ),
            ),
            "answer_key": types.Schema(
                type=types.Type.ARRAY,
                items=types.Schema(type=types.Type.STRING),
                nullable=True,
            ),
        },
        required=["title", "class_level", "topic", "duration_minutes", "questions"],
    )

def _render_fiche_markdown(data: Dict[str, Any]) -> str:
    """Render fiche JSON into a production-style teaching orchestration format."""
    if not isinstance(data, dict):
        return ""

    def _clean_list(values: Any) -> List[str]:
        if not values:
            return []
        if isinstance(values, str):
            text = values.strip()
            return [text] if text else []
        cleaned: List[str] = []
        for value in values:
            if isinstance(value, str) and value.strip():
                cleaned.append(value.strip())
        return cleaned

    metadata = data.get("metadata", {}) or {}
    title = (data.get("title") or metadata.get("lesson_title") or "Fiche pédagogique").strip()
    class_level = metadata.get("class_level")
    subject = metadata.get("subject")
    duration = metadata.get("duration_minutes")
    chapter_title = metadata.get("chapter_title")
    lesson_title = metadata.get("lesson_title")
    materials = _clean_list(metadata.get("materials"))
    objectives = _clean_list(data.get("objectives"))
    phases = data.get("phases") or []

    lines: List[str] = [f"# {title}"]

    summary_parts: List[str] = []
    if class_level:
        summary_parts.append(f"Classe {class_level}")
    if subject:
        summary_parts.append(subject)
    if duration:
        summary_parts.append(f"{duration} min")
    if summary_parts:
        lines.append("")
        lines.append(f"> **Fiche pédagogique** · {' · '.join(summary_parts)}")

    if objectives:
        lines.append("")
        lines.append("## Objectifs")
        for idx, obj in enumerate(objectives, start=1):
            lines.append(f"{idx}. {obj}")

    if phases:
        lines.append("")
        lines.append("## Plan de seance")
        for idx, phase in enumerate(phases, start=1):
            if not isinstance(phase, dict):
                continue
            phase_name = phase.get("name") or f"Phase {idx}"
            phase_goal = (phase.get("goal") or "").strip()
            phase_duration = phase.get("duration_minutes")
            duration_text = f"{phase_duration} min" if phase_duration else "à adapter"
            if phase_goal:
                lines.append(f"- **Bloc {idx} ({duration_text})**: {phase_name} - {phase_goal}")
            else:
                lines.append(f"- **Bloc {idx} ({duration_text})**: {phase_name}")

        lines.append("")
        lines.append("## Deroulement detaille")
        for idx, phase in enumerate(phases, start=1):
            if not isinstance(phase, dict):
                continue
            name = phase.get("name") or f"Phase {idx}"
            phase_duration = phase.get("duration_minutes")
            header = f"### Bloc {idx} - {name}"
            if phase_duration:
                header += f" ({phase_duration} min)"
            lines.append(header)

            goal = (phase.get("goal") or "").strip()
            if goal:
                lines.append(f"**Objectif de la phase**: {goal}")

            teacher_steps = _clean_list(phase.get("teacher_steps"))
            if teacher_steps:
                lines.append("**Actions de l'enseignant**")
                for step in teacher_steps:
                    lines.append(f"- {step}")

            student_steps = _clean_list(phase.get("student_steps"))
            if student_steps:
                lines.append("**Actions des eleves**")
                for step in student_steps:
                    lines.append(f"- {step}")

            phase_materials = _clean_list(phase.get("materials"))
            if phase_materials:
                lines.append(f"**Supports**: {', '.join(phase_materials)}")

            diff = (phase.get("differentiation") or "").strip()
            if diff:
                lines.append(f"**Differenciation**: {diff}")

            lines.append("")

    evaluation = data.get("evaluation") or {}
    if evaluation:
        lines.append("## Evaluation")
        strategy = (evaluation.get("strategy") or "").strip()
        if strategy:
            lines.append(f"**Modalite**: {strategy}")

        questions = _clean_list(evaluation.get("questions"))
        if questions:
            lines.append("### Questions")
            for idx, question in enumerate(questions, start=1):
                lines.append(f"{idx}. {question}")

        answer_key = _clean_list(evaluation.get("answer_key"))
        if answer_key:
            lines.append("### Elements de correction")
            for idx, answer in enumerate(answer_key, start=1):
                lines.append(f"{idx}. {answer}")
        lines.append("")

    reminders = (data.get("reminders") or "").strip()
    if reminders:
        lines.append("## Remarques")
        lines.append(f"- {reminders}")
        lines.append("")

    conclusion = (data.get("conclusion") or "").strip()
    if conclusion:
        lines.append("## Conclusion")
        lines.append(conclusion)

    return "\n".join(line for line in lines if line is not None).strip()

def _render_evaluation_markdown(data: Dict[str, Any]) -> str:
    """Convert evaluation JSON into a dedicated exam-sheet markdown format."""
    if not isinstance(data, dict):
        return ""

    lines: List[str] = []
    header = data.get("header", {}) or {}

    def _clean_text(value: Any, fallback: str = "") -> str:
        text = str(value or "").strip()
        return text if text else fallback

    def _score_text(value: Any) -> str:
        try:
            number = float(value)
            if number.is_integer():
                return str(int(number))
            return f"{number:.1f}".rstrip("0").rstrip(".")
        except Exception:
            return str(value if value is not None else "")

    school_name = data.get("school_name") or "Groupe Scolaire"
    class_level = _clean_text(header.get("class_level"), "CM1")
    academic_year = _clean_text(header.get("academic_year"), "2025/2026")
    subject = _clean_text(header.get("subject"), "Matière")
    duration = _clean_text(header.get("duration_minutes"), "45")
    max_score = _score_text(header.get("max_score", 20))

    session_label = header.get("session_label")
    if not session_label:
        eval_num = header.get("evaluation_number", 1)
        semester = header.get("semester", "1")
        num_word = "1er" if eval_num == 1 else f"{eval_num}e"
        sem_word = "1er" if semester == "1" else f"{semester}e"
        session_label = f"{num_word} contrôle du {sem_word} semestre"

    lines.append("# EPREUVE D'EVALUATION")
    lines.append("")
    lines.append(f"> **{school_name}**")
    lines.append(f"> **Session**: {session_label}")
    lines.append(f"> **Niveau**: {class_level}  |  **Matière**: {subject}")
    lines.append(f"> **Durée**: {duration} min  |  **Total**: {max_score} points")
    lines.append("")

    lines.append("## Cadre élève")
    lines.append("- Nom et prénom: ________________________________________________")
    lines.append("- Date: ____________________")
    lines.append("- Classe: __________________")
    lines.append("- Note finale: ________ / " + max_score)
    lines.append("")

    lines.append("## Consignes générales")
    lines.append(f"1. Lis chaque consigne avec attention et respecte la durée ({duration} min).")
    lines.append("2. Soigne la présentation et justifie quand la consigne l'exige.")
    lines.append("3. Réponds directement dans les espaces prévus.")
    lines.append("")

    exercises = data.get("exercises") or []
    if exercises:
        lines.append("## Barème de l'épreuve")
        lines.append("| Exercice | Points |")
        lines.append("|:---------|------:|")
        for idx, exercise in enumerate(exercises, start=1):
            if not isinstance(exercise, dict):
                continue
            points = _score_text(exercise.get("points"))
            label = _clean_text(exercise.get("title"), f"Exercice {idx}")
            lines.append(f"| {label} | {points} |")
        lines.append(f"| **Total** | **{max_score}** |")
        lines.append("")

    lines.append("---")
    lines.append("")

    # Exercises block: fully rebuilt layout
    for idx, exercise in enumerate(exercises, start=1):
        if not isinstance(exercise, dict):
            continue

        title = _clean_text(exercise.get("title"), f"Exercice {idx}")
        instructions = _clean_text(exercise.get("instructions"))
        points = _score_text(exercise.get("points"))

        lines.append(f"## Exercice {idx} - {title}")
        lines.append(f"**Points:** {points}")
        if instructions:
            lines.append(f"**Consigne:** {instructions}")
        lines.append("")

        questions = exercise.get("questions") or []
        for question_index, question in enumerate(questions, start=1):
            if not isinstance(question, dict):
                continue
            prompt = _clean_text(question.get("prompt"))
            if prompt:
                answer_type = _clean_text(question.get("answer_type"), "réponse ouverte")
                lines.append(f"### Q{idx}.{question_index} [{answer_type}]")
                lines.append(prompt)
                lines.append("")
                lines.append("Réponse: __________________________________________________________")
                lines.append("____________________________________________________________")
                lines.append("")

        lines.append("")
        lines.append("---")
        lines.append("")

    # Answer key (optional)
    answer_key = data.get("answer_key")
    if answer_key:
        lines.append("## Corrigé enseignant")
        lines.append("")
        for answer in answer_key:
            if isinstance(answer, dict):
                ref = _clean_text(answer.get("reference"), "Question")
                value = _clean_text(answer.get("answer"))
                lines.append(f"- **{ref}** : {value}")
            else:
                lines.append(f"- {answer}")
        lines.append("")

    return "\n".join(lines).strip()

def _render_quiz_markdown(data: Dict[str, Any]) -> str:
    """Convert quiz JSON into Markdown for preview/export."""
    if not isinstance(data, dict):
        return ""

    lines: List[str] = []
    title = data.get("title") or f"Quiz: {data.get('topic', 'Sujet')}"
    class_level = data.get("class_level", "")
    duration = data.get("duration_minutes", "")
    subject = data.get("subject") or ""
    topic = data.get("topic") or ""

    lines.append(f"# {title}")
    lines.append("")
    meta = []
    if class_level:
        meta.append(f"**Classe**: {class_level}")
    if subject:
        meta.append(f"**Matière**: {subject}")
    if topic:
        meta.append(f"**Sujet**: {topic}")
    if duration:
        meta.append(f"**Durée**: {duration} min")
    if meta:
        lines.append(" | ".join(meta))
        lines.append("")

    instructions = data.get("instructions") or []
    if instructions:
        lines.append("## Consignes")
        for item in instructions:
            lines.append(f"- {item}")
        lines.append("")

    lines.append("## Questions")
    lines.append("")
    for q in data.get("questions", []):
        if not isinstance(q, dict):
            continue
        number = q.get("number", "?")
        q_type = q.get("type", "question")
        prompt = q.get("prompt", "")
        lines.append(f"### Question {number} ({q_type})")
        lines.append(prompt)
        options = q.get("options") or []
        if options:
            for idx, opt in enumerate(options):
                letter = chr(ord('A') + idx)
                lines.append(f"- {letter}. {opt}")
        lines.append("")

    answer_key = data.get("answer_key") or []
    if answer_key:
        lines.append("## Corrigé")
        lines.append("")
        for answer in answer_key:
            lines.append(f"- {answer}")

    return "\n".join(lines).strip()
