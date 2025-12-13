import json
from typing import Optional, List, Dict, Any
from functools import lru_cache
from google import genai
from google.genai import types
from PyQt6 import QtCore

from config import (
    API_KEYS, _GENAI_CLIENT, _GENAI_CLIENT_KEY, _GENAI_CLIENT_LOCK,
    get_configured_gemini_model, get_configured_flash_model,
    _clamp_temperature
)

def get_genai_client() -> Optional[genai.Client]:
    """Return a cached google-genai client configured with the current API key."""
    api_key = API_KEYS.get("GEMINI_API_KEY")
    if not api_key:
        return None

    global _GENAI_CLIENT, _GENAI_CLIENT_KEY
    with _GENAI_CLIENT_LOCK:
        if _GENAI_CLIENT is None or _GENAI_CLIENT_KEY != api_key:
            _GENAI_CLIENT = genai.Client(api_key=api_key)
            _GENAI_CLIENT_KEY = api_key
    return _GENAI_CLIENT

def get_ai_client(api_provider):
    """Get Gemini client - OpenRouter has been removed."""
    try:
        client = get_genai_client()
    except Exception as exc:
        return None, f"Failed to initialise Gemini client: {exc}"

    if client is None:
        return None, "Gemini API key not found in environment or keys.txt"

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
):
    """Shared helper to invoke Gemini models with consistent config handling."""

    client = get_genai_client()
    if client is None:
        raise RuntimeError("Missing Gemini API key")

    config_kwargs: Dict[str, Any] = {"temperature": _clamp_temperature(temperature)}
    if response_schema is not None:
        config_kwargs["response_schema"] = response_schema
    if response_mime_type is not None:
        config_kwargs["response_mime_type"] = response_mime_type
    if tools is not None:
        config_kwargs["tools"] = tools
    if tool_config is not None:
        config_kwargs["tool_config"] = tool_config

    config = types.GenerateContentConfig(**config_kwargs)
    return client.models.generate_content(
        model=model_name,
        contents=contents,
        config=config,
    )

def _generate_with_model(
    model_name: str,
    prompt: str,
    temperature: float,
    *,
    response_schema: Optional[types.Schema] = None,
    response_mime_type: Optional[str] = None,
    tools: Optional[List[types.Tool]] = None,
    tool_config: Optional[types.ToolConfig] = None,
):
    return _call_model(
        model_name,
        prompt,
        temperature=temperature,
        response_schema=response_schema,
        response_mime_type=response_mime_type,
        tools=tools,
        tool_config=tool_config,
    )

def _generate_with_gemini(
    prompt: str,
    temperature: float,
    *,
    response_schema: Optional[types.Schema] = None,
    response_mime_type: Optional[str] = None,
    tools: Optional[List[types.Tool]] = None,
    tool_config: Optional[types.ToolConfig] = None,
):
    return _generate_with_model(
        get_configured_gemini_model(),
        prompt,
        temperature,
        response_schema=response_schema,
        response_mime_type=response_mime_type,
        tools=tools,
        tool_config=tool_config,
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
    purpose: for logs ("page-finding", "fiche-generation", "evaluation-generation", etc.)
    Returns the generated text or None if failed.
    """
    settings = QtCore.QSettings("FicheGen", "Pedago")
    enable_fallback = settings.value("enable_model_fallback", "true") == "true"
    use_pro = settings.value("gemini_use_pro", "true") == "true"
    
    if not API_KEYS.get("GEMINI_API_KEY"):
        queue.put(("log", "❌ No Gemini API key configured"))
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

def _render_fiche_markdown(data: Dict[str, Any]) -> str:
    """Convert fiche JSON into the Markdown format expected by the UI."""
    if not isinstance(data, dict):
        return ""

    lines: List[str] = []
    metadata = data.get("metadata", {}) or {}

    title = data.get("title") or metadata.get("lesson_title") or "Fiche pédagogique"
    lines.append(f"# {title.strip()}")

    def _meta(label: str, key: str):
        value = metadata.get(key)
        if value:
            lines.append(f"### **{label}**: {value}")

    _meta("Titre du chapitre", "chapter_title")
    _meta("Titre de la leçon", "lesson_title")
    duration = metadata.get("duration_minutes")
    if duration:
        lines.append(f"### **Durée**: {duration} min")
    _meta("Classe", "class_level")
    _meta("Matière", "subject")

    objectives = data.get("objectives") or []
    if objectives:
        lines.append("")
        lines.append("## Objectifs")
        for obj in objectives:
            lines.append(f"- {obj}")

    phases = data.get("phases") or []
    if phases:
        lines.append("")
        lines.append("## Déroulement de la séance")
        for phase in phases:
            if not isinstance(phase, dict):
                continue
            name = phase.get("name") or "Phase"
            duration_minutes = phase.get("duration_minutes")
            header = f"### {name}" + (f" ({duration_minutes} min)" if duration_minutes else "")
            lines.append(header)

            goal = phase.get("goal")
            if goal:
                lines.append(f"*Objectif :* {goal}")

            teacher_steps = phase.get("teacher_steps") or []
            if teacher_steps:
                lines.append("**Actions de l'enseignant :**")
                for step in teacher_steps:
                    lines.append(f"- {step}")

            student_steps = phase.get("student_steps") or []
            if student_steps:
                lines.append("**Actions des élèves :**")
                for step in student_steps:
                    lines.append(f"- {step}")

            diff = phase.get("differentiation")
            if diff:
                lines.append(f"*Différenciation :* {diff}")


    evaluation = data.get("evaluation") or {}
    if evaluation:
        lines.append("")
        lines.append("## Évaluation")
        strategy = evaluation.get("strategy")
        if strategy:
            lines.append(strategy)
        questions = evaluation.get("questions") or []
        for question in questions:
            lines.append(f"- {question}")

        answer_key = evaluation.get("answer_key") or []
        if answer_key:
            lines.append("")
            lines.append("### Corrigé")
            for answer in answer_key:
                lines.append(f"- {answer}")

    reminders = data.get("reminders")
    if reminders:
        lines.append("")
        lines.append("## Remarques et rappels")
        lines.append(reminders)

    conclusion = data.get("conclusion")
    if conclusion:
        lines.append("")
        lines.append("## Conclusion")
        lines.append(conclusion)

    return "\n".join(lines).strip()

def _render_evaluation_markdown(data: Dict[str, Any]) -> str:
    """Convert evaluation JSON into Markdown matching French school format."""
    if not isinstance(data, dict):
        return ""

    lines: List[str] = []
    header = data.get("header", {}) or {}
    
    # School name as main title
    school_name = data.get("school_name") or "Groupe Scolaire"
    lines.append(f"# {school_name}")
    lines.append("")
    
    # Student information fields
    lines.append("**Nom et prénom :** _________________________________________________")
    lines.append("")
    
    class_level = header.get("class_level", "CM1")
    lines.append(f"**Niveau :** {class_level}")
    lines.append("")
    
    academic_year = header.get("academic_year")
    if academic_year:
        lines.append(f"**Année scolaire :** {academic_year}")
    else:
        lines.append("**Année scolaire :** _________________________")
    lines.append("")
    lines.append("")
    
    # Session label (e.g., "1er contrôle du 1er semestre")
    session_label = header.get("session_label")
    if not session_label:
        eval_num = header.get("evaluation_number", 1)
        semester = header.get("semester", "1")
        num_word = "1er" if eval_num == 1 else f"{eval_num}e"
        sem_word = "1er" if semester == "1" else f"{semester}e"
        session_label = f"{num_word} contrôle du {sem_word} semestre"
    
    lines.append(f"**{session_label}**")
    lines.append("")
    
    # Duration and score
    duration = header.get("duration_minutes", 45)
    max_score = header.get("max_score", 20)
    lines.append(f"**Durée :** {duration} min")
    lines.append("")
    lines.append(f"**Note :** _________ / {int(max_score)}")
    lines.append("")
    lines.append("")
    lines.append("---")
    lines.append("")
    
    # Exercises
    exercises = data.get("exercises") or []
    for idx, exercise in enumerate(exercises, start=1):
        if not isinstance(exercise, dict):
            continue
            
        title = exercise.get("title") or f"Exercice {idx}"
        instructions = exercise.get("instructions") or ""
        points = exercise.get("points")
        
        # Exercise header with points
        if points is not None:
            if points == int(points):
                lines.append(f"## Exercice {idx} — {instructions} ({int(points)}pts)")
            else:
                lines.append(f"## Exercice {idx} — {instructions} ({points}pts)")
        else:
            lines.append(f"## Exercice {idx} — {instructions}")
        lines.append("")
        
        # Questions for this exercise
        questions = exercise.get("questions") or []
        for question in questions:
            if not isinstance(question, dict):
                continue
            prompt = question.get("prompt")
            if prompt:
                lines.append(prompt)
                lines.append("")
        
        lines.append("")
        lines.append("---")
        lines.append("")
    
    # Answer key (optional)
    answer_key = data.get("answer_key")
    if answer_key:
        lines.append("## Corrigé")
        lines.append("")
        for answer in answer_key:
            if isinstance(answer, dict):
                ref = answer.get("reference") or "Question"
                value = answer.get("answer") or ""
                lines.append(f"- **{ref}** : {value}")
            else:
                lines.append(f"- {answer}")
        lines.append("")
    
    return "\n".join(lines).strip()
