import os
import json
import re
import threading
import time
import random
from datetime import datetime
from typing import Optional, List, Dict, Any

from PyQt6 import QtCore

from config import (
    DEFAULT_INPUT_DIR, DEFAULT_OUTPUT_DIR, API_KEYS,
    get_configured_fiche_prompt, get_configured_flash_model,
    get_configured_pro_model, HAS_IMAGE_GENERATION, HAS_DOCX,
    get_configured_page_finding_prompt, GEMINI_TOC_MODEL,
    load_api_keys_from_settings
)
from core.ai import (
    generate_with_fallback, _fiche_response_schema, _parse_structured_response,
    _render_fiche_markdown, _evaluation_response_schema, _render_evaluation_markdown,
    _quiz_response_schema, _render_quiz_markdown, _generate_with_model
)
from core.toc import (
    find_guide_file, find_textbook_file, extract_table_of_contents,
    get_cached_toc, parse_full_toc_with_ai, save_toc_to_cache,
    detect_page_offset, correct_lesson_topic_syntax, parse_page_numbers,
    find_pages_from_cached_toc, get_pages_from_toc, extract_lesson_text,
    PdfExtractionSession
)
from core.image_gen import (
    generate_illustration, image_to_base64
)
from core.updater import check_update_status, update_and_build, REPO_URL, REPO_BRANCH
from utils.helpers import get_top_rated_examples


def _resolve_image_api_key() -> str:
    """Resolve API key for image generation via centralized config loading."""
    key = (API_KEYS.get("GEMINI_API_KEY") or "").strip()
    if key:
        return key

    # Ensure in-memory keys are synced from the configured secure source policy.
    load_api_keys_from_settings()
    return (API_KEYS.get("GEMINI_API_KEY") or "").strip()


_IMG_TAG_RE = re.compile(r'<generateimage\s*:\s*(["\'])(.*?)\1\s*>', re.IGNORECASE | re.DOTALL)
_EXERCISE_HEADER_RE = re.compile(r'^##\s+Exercice\s+(\d+)\s+-', re.IGNORECASE)


def _collect_image_prompts_from_markdown(markdown_text: str, max_prompts: int = 5) -> List[str]:
    """Extract inline image directives from markdown: <generateimage:"prompt">."""
    prompts: List[str] = []
    if not markdown_text:
        return prompts
    for match in _IMG_TAG_RE.finditer(markdown_text):
        prompt = (match.group(2) or "").strip()
        if prompt:
            prompts.append(prompt)
        if len(prompts) >= max_prompts:
            break
    return prompts


def _replace_image_tags_with_images(markdown_text: str, class_level: str, api_key: str, queue, max_images: int = 5) -> tuple[str, int]:
    """Generate and replace inline image directives with embedded markdown images."""
    if not markdown_text or not api_key:
        return markdown_text, 0

    is_young = (class_level or "").lower() in ["cp", "ce1"]
    style = "coloring" if is_young else "diagram"
    aspect_ratio = "1:1" if is_young else "16:9"
    generated_count = 0

    def _repl(match):
        nonlocal generated_count
        prompt = (match.group(2) or "").strip()
        if not prompt or generated_count >= max_images:
            return match.group(0)
        try:
            image_data = generate_illustration(
                prompt=prompt,
                class_level=class_level,
                style=style,
                aspect_ratio=aspect_ratio,
                api_key=api_key,
            )
            if not image_data:
                queue.put(("log", f"⚠️ No image generated for directive: {prompt[:80]}"))
                return f"\n\n*⚠️ Image not generated for prompt:* {prompt}\n\n"

            generated_count += 1
            base64_img = image_to_base64(image_data)
            return (
                "\n\n"
                f"![Illustration {generated_count}](data:image/png;base64,{base64_img})\n"
                f"*Prompt image: {prompt}*\n\n"
            )
        except Exception as exc:
            queue.put(("log", f"⚠️ Image directive error: {exc}"))
            return f"\n\n*⚠️ Image generation error for prompt:* {prompt}\n\n"

    updated = _IMG_TAG_RE.sub(_repl, markdown_text)
    return updated, generated_count


def _clean_eval_text(value: Any) -> str:
    text = str(value or "").strip()
    return " ".join(text.split())


def _extract_exercise_image_jobs(parsed_evaluation: Dict[str, Any], num_images: int) -> List[Dict[str, Any]]:
    """Build deterministic image jobs tied to concrete exercises from structured eval JSON."""
    if not isinstance(parsed_evaluation, dict) or num_images <= 0:
        return []

    jobs: List[Dict[str, Any]] = []
    exercises = parsed_evaluation.get("exercises") or []
    visual_keywords = (
        "observe", "dessine", "schéma", "schema", "figure", "image",
        "illustre", "associe", "relie", "complète", "complete", "diagramme",
        "label", "étiquette", "etiquette"
    )

    for idx, exercise in enumerate(exercises, start=1):
        if not isinstance(exercise, dict):
            continue

        title = _clean_eval_text(exercise.get("title") or f"Exercice {idx}")
        instructions = _clean_eval_text(exercise.get("instructions"))
        questions = exercise.get("questions") or []
        question_prompts: List[str] = []
        for question in questions:
            if not isinstance(question, dict):
                continue
            q_prompt = _clean_eval_text(question.get("prompt"))
            if q_prompt:
                question_prompts.append(q_prompt)

        # Rank exercises with a simple deterministic heuristic favoring visual wording.
        joined = f"{title} {instructions} {' '.join(question_prompts)}".lower()
        keyword_score = sum(1 for kw in visual_keywords if kw in joined)
        richness_score = min(len(question_prompts), 4)
        total_score = (keyword_score * 2) + richness_score

        if not instructions and not question_prompts:
            continue

        jobs.append({
            "exercise_index": idx,
            "title": title,
            "instructions": instructions,
            "question_prompts": question_prompts,
            "score": total_score,
        })

    if not jobs:
        return []

    # Select highest-signal exercises, then restore exercise order for predictable output.
    ranked = sorted(jobs, key=lambda job: (-job["score"], job["exercise_index"]))
    selected = ranked[:max(1, num_images)]
    selected.sort(key=lambda job: job["exercise_index"])
    return selected


def _build_exercise_prompt(job: Dict[str, Any], class_level: str, subject: str) -> str:
    """Compose a specific image prompt from one exercise payload."""
    exercise_idx = job.get("exercise_index")
    title = job.get("title") or f"Exercice {exercise_idx}"
    instructions = job.get("instructions") or ""
    question_prompts = job.get("question_prompts") or []

    lines = [
        f"Illustration pédagogique pour l'exercice {exercise_idx} ({title}).",
        f"Niveau: {(class_level or '').upper() or 'PRIMAIRE'}.",
        f"Matière: {subject or 'Général'}.",
        "Objectif: créer un visuel directement exploitable pour résoudre l'exercice.",
        "Style: simple, clair, scolaire, fond blanc, lisibilité maximale.",
        "N'ajoute aucune marque, logo ou texte décoratif inutile.",
    ]

    if instructions:
        lines.append(f"Consigne de l'exercice: {instructions}")

    if question_prompts:
        lines.append("Questions à soutenir visuellement:")
        for q in question_prompts[:4]:
            lines.append(f"- {q}")

    lines.append("Le visuel doit correspondre explicitement à cet exercice et à aucune autre activité.")
    return "\n".join(lines)


def _embed_images_under_exercises(markdown_text: str, image_blocks: Dict[int, str]) -> str:
    """Insert generated image markdown inside each matching exercise section."""
    if not markdown_text or not image_blocks:
        return markdown_text

    lines = markdown_text.splitlines()
    exercise_headers: List[tuple[int, int]] = []
    for line_idx, line in enumerate(lines):
        match = _EXERCISE_HEADER_RE.match(line.strip())
        if match:
            exercise_headers.append((line_idx, int(match.group(1))))

    if not exercise_headers:
        return markdown_text

    insertions: List[tuple[int, List[str]]] = []
    for header_idx, (line_idx, exercise_number) in enumerate(exercise_headers):
        block = image_blocks.get(exercise_number)
        if not block:
            continue

        next_header_idx = exercise_headers[header_idx + 1][0] if header_idx + 1 < len(exercise_headers) else len(lines)
        insert_at = next_header_idx

        for probe_idx in range(line_idx + 1, next_header_idx):
            if lines[probe_idx].strip().startswith("### Q"):
                insert_at = probe_idx
                break

        block_lines = ["", block.rstrip(), ""]
        insertions.append((insert_at, block_lines))

    if not insertions:
        return markdown_text

    # Apply from bottom to top to keep insertion indexes stable.
    for insert_at, block_lines in sorted(insertions, key=lambda item: item[0], reverse=True):
        lines[insert_at:insert_at] = block_lines

    return "\n".join(lines)

# --- QThread worker that bridges queue events to Qt signals ---
class QueueProxy:
    def __init__(self, worker):
        self.worker = worker

    def put(self, item):
        try:
            msg_type = item[0]
            payload = item[1] if len(item) > 1 else None
        except Exception:
            return
        if msg_type == "log":
            self.worker.log.emit(str(payload))
        elif msg_type == "progress":
            try:
                self.worker.progress.emit(int(payload))
            except Exception:
                pass
        elif msg_type == "done":
            self.worker.done.emit(str(payload))
        elif msg_type == "content":
            self.worker.content.emit(str(payload))
        elif msg_type in {"enable_button", "enable_buttons"}:
            self.worker.enable_buttons.emit()
        elif msg_type == "request_source_preview":
            try:
                if len(item) == 3:  # source_text and prompt provided
                    self.worker.request_source_preview.emit(str(item[1]), str(item[2]))
                else:  # backwards compatibility
                    self.worker.request_source_preview.emit(str(payload), "")
            except Exception:
                pass


class ExportWorker(QtCore.QThread):
    """Background worker for PDF/DOCX exports to keep the UI responsive."""

    log = QtCore.pyqtSignal(str)
    success = QtCore.pyqtSignal(str, str, str)  # path, format, content_type
    failed = QtCore.pyqtSignal(str)

    def __init__(
        self,
        *,
        export_format: str,
        content_type: str,
        markdown: str,
        class_level: str,
        output_dir: str,
        template_name: str,
        subject: Optional[str],
        lesson_topic: str,
        topics_list: Optional[List[str]],
        show_meta_banner: bool,
    ):
        super().__init__()
        self.export_format = (export_format or "").lower()
        self.content_type = content_type or "fiche"
        self.markdown = markdown or ""
        self.class_level = class_level or ""
        self.output_dir = output_dir or DEFAULT_OUTPUT_DIR
        self.template_name = template_name or "Normal"
        self.subject = subject
        self.lesson_topic = lesson_topic or "Lecon"
        self.topics_list = topics_list or ["Unknown"]
        self.show_meta_banner = bool(show_meta_banner)

    def run(self):
        class _Queue:
            def __init__(self, emitter):
                self.emitter = emitter

            def put(self, item):
                try:
                    kind, payload = item
                    if kind == "log":
                        self.emitter.emit(str(payload))
                except Exception:
                    pass

        q = _Queue(self.log)

        try:
            if self.export_format == "pdf":
                from document.pdf import save_fiche_to_pdf, save_evaluation_to_pdf

                if self.content_type == "evaluation":
                    path = save_evaluation_to_pdf(
                        self.markdown,
                        self.topics_list,
                        self.class_level,
                        self.output_dir,
                        q,
                        self.template_name,
                        self.subject,
                        self.show_meta_banner,
                    )
                else:
                    path = save_fiche_to_pdf(
                        self.markdown,
                        self.lesson_topic,
                        self.class_level,
                        self.output_dir,
                        q,
                        self.template_name,
                        self.subject,
                        self.show_meta_banner,
                    )
            elif self.export_format == "docx":
                from document.docx import save_fiche_to_docx, save_evaluation_to_docx

                if self.content_type == "evaluation":
                    path = save_evaluation_to_docx(
                        self.markdown,
                        self.topics_list,
                        self.class_level,
                        self.output_dir,
                        q,
                    )
                else:
                    path = save_fiche_to_docx(
                        self.markdown,
                        self.lesson_topic,
                        self.class_level,
                        self.output_dir,
                        q,
                        template_name=self.template_name,
                    )
            else:
                self.failed.emit(f"Unsupported export format: {self.export_format}")
                return

            if path:
                self.success.emit(path, self.export_format, self.content_type)
            else:
                self.failed.emit(f"Failed to export {self.export_format.upper()} file.")
        except Exception as exc:
            self.failed.emit(str(exc))


class UpdateWorker(QtCore.QThread):
    """Background worker that checks for updates and optionally rebuilds from GitHub source."""

    log = QtCore.pyqtSignal(str)
    progress = QtCore.pyqtSignal(int)
    checked = QtCore.pyqtSignal(bool, str, str, str)  # available, local_sha, remote_sha, message
    success = QtCore.pyqtSignal(str)
    failed = QtCore.pyqtSignal(str)

    def __init__(
        self,
        *,
        mode: str,
        repo_dir: str,
        repo_url: str = REPO_URL,
        branch: str = REPO_BRANCH,
    ):
        super().__init__()
        self.mode = (mode or "check").lower()
        self.repo_dir = repo_dir
        self.repo_url = repo_url
        self.branch = branch

    def run(self):
        try:
            if self.mode == "check":
                self.progress.emit(15)
                available, local_sha, remote_sha, message = check_update_status(
                    self.repo_dir,
                    repo_url=self.repo_url,
                    branch=self.branch,
                    log=lambda line: self.log.emit(str(line)),
                )
                self.progress.emit(100)
                self.checked.emit(available, local_sha or "", remote_sha or "", message)
                return

            if self.mode == "update":
                self.progress.emit(5)
                self.log.emit("🔍 Checking update source...")
                app_path = update_and_build(
                    self.repo_dir,
                    repo_url=self.repo_url,
                    branch=self.branch,
                    log=lambda line: self.log.emit(str(line)),
                )
                self.progress.emit(100)
                self.success.emit(app_path)
                return

            self.failed.emit(f"Unsupported updater mode: {self.mode}")
        except Exception as exc:
            self.failed.emit(str(exc))

def build_examples_block(use_top_rated: bool):
    builtin_example = """
## EXEMPLE DE STYLE (référence de style et pas de format)
## EXEMPLE DE STYLE n1
Titre du chapitre : La santé de l'être humain
Titre de la leçon : Les 5 sens
Durée :
Classe : CP
(Red Ink)
Objectifs :
Faire connaître aux élèves nos cinq principaux organes sensoriels : les yeux, les oreilles, le nez, la langue et la peau et explorer leurs différentes fonctions : la vue, l'ouïe, l'odorat, le goût et le toucher.
(Red Ink)
Déroulement :
Découverte générale :
Dans un petit sac je met un parfum
je demande aux élèves :
"Comment peut-on savoir ce qu'il y a dans le sac ?"
je laisse les enfants proposer :
(Red Ink) regarder, sentir, écouter, goûter, toucher.
J'explique aux élèves que pour découvrir le monde, notre corps utilise 5 organes des sens.
j'associe rapidement chaque sens à son organe sur le tableau.
Activité de découverte :

je demande aux élèves de prendre leurs livre p. 8 et 9.
je lis la consigne et j'explique qu'ils doivent observer les images et découvrir le sens utilisé sur chaque image.
je passe vérifier les réponses de chacun, puis on corrige.
Amener les élèves à donner un nom à chaque sens.
Ouïe - Vue - Odorat - Toucher - Goût
Ecrire le même nom pour le 2ème ex.
Conclusion :
Nous avons 5 sens pour découvrir le monde :
la vue : On voit grâce aux yeux.
l'ouïe : On entend grâce aux oreilles.
l'odorat : On sent les odeurs grâce au nez.
le goût : On goûte grâce à la langue.
le toucher : On touche les objets grâce avec la peau et les mains.
(Red Ink)
Donner un exercice d'approfondissement à faire à la maison.
## EXEMPLE DE STYLE n2
Page 1
Titre du chapitre : la santé de l'être humain
Titre de la leçon : (Red Ink) le toucher
Durée :
Classe : CE1
(Red Ink)
Objectifs :
Identifier l'organe du toucher.
Découvrir le rôle du toucher et
comprendre comment la peau nous informe sur notre environnement.
(Red Ink)
Déroulement :
Découverte
je fais un rappel sur les cinq sens :
Les élèves doivent connaître les cinq sens : le toucher, le goût, la vue, l'ouïe et l'odorat, on les écrivant au tableau et les lier à l'organe correspondant.
je fais le point sur le toucher comme titre de leçon.
mentionner que "le sens du toucher est partout sur notre peau, mais surtout sur nos mains et nos pieds car ils touchent beaucoup de chose et sont très sensibles."
Activités de découverte
je demande aux élèves de prendre leurs livre p. 8 et 9. observer l'image et je pose la question "Que remarquez-vous?"
j'ai noté les remarques sur le tableau, ils révèleront que c'est une "silhouette".
j'invite les élèves à réaliser l'exercice et passe à une correction collectif. Correction : les mains et les pieds.
Le 2ème exercice, les élèves sont menés à le réaliser en autonomie, ensuite je vérifie les réponses.
la fille a utilisé un gant pour éviter de se brûler la main par le glaçon.
la fille sent qu'elle tient un petit paquet dans les bras, l'image
tient un grand paquet dans le bras aussi.
Conclusion :
Le toucher est le sens qui permet le contact avec l'environnement.
La peau est l'organe du toucher, elle recouvre tout le corps et transmet les sensation du toucher au cerveau.
Les mains et les pieds sont les parties les plus sensibles.
Les types de sensations sont : la douceur, la douleur, la pression, le froid, le chaud etc...
Exercices d'approfondissements
Proposer aux élèves d'effectuer les exercices 3 et 4 à la maison.
""".strip()

    parts = [builtin_example]

    if use_top_rated:
        top = get_top_rated_examples(n=2)
        for i, ex in enumerate(top, start=1):
            parts.append(f"""
## EXEMPLE TOP-RATED #{i} — {ex.get('class_level','').upper()} — {ex.get('topic','')}
{ex.get('content','').strip()}
""".strip())

    return "\n\n".join(parts).strip()

def generate_fiche_from_text(lesson_text, lesson_topic, class_level, queue, temperature: float, use_top_rated_examples: bool, duration_minutes: int, subject: str, special_instructions: str, cancel_event=None):
    if cancel_event and cancel_event.is_set():
        queue.put(("log", "⏹️ Cancelled before generation step."))
        return None

    queue.put(("log", "Génération de la fiche..."))

    examples_block = build_examples_block(use_top_rated_examples)

    # Build structure dynamically with subject and duration
    duree = max(10, int(duration_minutes or 45))
    active = max(5, duree - 15)
    matiere_line = f"   - Matière: {subject}" if subject else "   - Matière: (déduire si pertinent)"
    
    # Add special instructions to the prompt if provided
    instructions_block = ""
    if special_instructions:
        instructions_block = f"""
**INSTRUCTIONS SPÉCIALES:**
---
{special_instructions}
---
"""

    fiche_structure = f"""
**Titre du chapitre** : (à déduire du manuel)
**Titre de la leçon** : {lesson_topic}
**Durée** : {duree} min
**Classe** : {class_level}
**Matière** : {subject if subject else "(à déduire si pertinent)"}
(ne rien ajouter de plus)
## Objectifs
- Identifier ...
- Décrire ...
- (Ajouter si nécessaire)

## Déroulement de la séance

### Introduction (5-10 min)
Je commence la séance par une petite question/rappel pour éveiller la curiosité des élèves.  
Je présente le titre de la leçon et j’annonce ce que nous allons apprendre aujourd’hui.

### Activité de découverte (15-20 min)
Je demande aux élèves de prendre leur manuel p. X.  
Nous observons ensemble les images / le texte.  
Je pose des questions simples : "Que voyez-vous ? Que remarquez-vous ?"  
Je note les réponses des élèves au tableau.  
Je les guide vers la découverte de la notion de la leçon.  
Les élèves réalisent les exercices indiqués.  
Je circule dans la classe pour vérifier et aider.  
On corrige collectivement.

### Synthèse et structuration (10-15 min)
Nous reprenons les points essentiels.  
Je formule avec les élèves la règle ou la conclusion.  
Les élèves recopient la conclusion dans leurs cahiers.

## Évaluation
Je propose un court exercice (oral ou écrit) pour vérifier que chacun a compris.  

## Remarques et conclusion
Rappeler aux élèves l’idée principale de la leçon.  
**Conclusion à recopier :** (un paragraphe de 3-5 lignes, claire, à noter dans le cahier)

""".strip()

    # Get the configured prompt template
    prompt_template = get_configured_fiche_prompt()
    
    # Prepare variables for the prompt template
    subject_line = f"Matière: {subject}" if subject else ""
    
    # Format the configured prompt with all variables
    prompt = prompt_template.format(
        lesson_topic=lesson_topic,
        class_level=class_level,
        subject_line=subject_line,
        lesson_text=lesson_text,
        fiche_structure=fiche_structure,
        examples_block=examples_block,
        instructions_block=instructions_block,
        duree=duree,
        active=active
    )

    schema_guidance = (
        "\nFORMAT DE SORTIE STRUCTURÉ:\n"
        "- Réponds exclusivement avec un objet JSON valide (aucun texte avant ou après).\n"
        "- Remplis les champs: title, metadata, objectives, phases, evaluation, reminders (optionnel), conclusion (optionnel).\n"
        "- metadata doit contenir lesson_title, duration_minutes, class_level et, si possible, chapter_title, subject, materials.\n"
        "- Chaque élément dans phases doit préciser teacher_steps (liste), student_steps (liste) et duration_minutes.\n"
        "- evaluation.strategy décrit la consigne générale; questions et answer_key listent des formulations concises.\n"
        "- Si une question/activité nécessite un visuel, ajoute un tag inline exact: <generateimage:\"prompt précis de l'image\">.\n"
        "- N'ajoute ce tag que quand il apporte une vraie valeur pédagogique (max 3 tags).\n"
    )

    prompt = f"{prompt}\n\n{schema_guidance}"

    response = generate_with_fallback(
        prompt,
        temperature=max(0.0, min(1.0, temperature or 0.5)),
        queue=queue,
        purpose="fiche-generation",
        response_schema=_fiche_response_schema(),
        response_mime_type="application/json",
    )

    if not response:
        return None

    data = _parse_structured_response(response)
    if data:
        markdown = _render_fiche_markdown(data)
    else:
        markdown = (response.text or "").strip()

    if markdown:
        queue.put(("log", "✅ Fiche générée."))
        return markdown

    queue.put(("log", "❌ Fiche: réponse vide après transformation"))
    return None

def pipeline_run(class_level, lesson_topic, queue, pages_override: str, temperature: float, guides_dir: str, textbook_dir: str, use_top_rated_examples: bool, duration_minutes: int, subject: str, preview_source: bool, worker_ref, generate_image: bool = False, use_student_textbook: bool = False):
    try:
        queue.put(("progress", 10)); queue.put(("log", "🚀 Lancement du processus..."))

        # Early cancel
        if worker_ref.cancel_event.is_set():
            queue.put(("log", "⏹️ Cancelled before initialization."))
            return

        queue.put(("log", f"✅ Input: {guides_dir}"))

        queue.put(("progress", 20))
        guide_path = find_guide_file(class_level, guides_dir, queue)
        if not guide_path:
            return
        if worker_ref.cancel_event.is_set():
            queue.put(("log", "⏹️ Cancelled after finding guide."))
            return

        # --- ToC and Syntax Correction Logic ---
        queue.put(("progress", 30))
        raw_toc_text = extract_table_of_contents(guide_path, queue)
        if not raw_toc_text:
            return
        if worker_ref.cancel_event.is_set():
            queue.put(("log", "⏹️ Cancelled after extracting ToC."))
            return

        # Try to get a structured ToC (cache or AI) to help with syntax correction
        cached_toc = get_cached_toc(guide_path, guides_dir)
        if not cached_toc:
            queue.put(("log", "⏳ No ToC cache, parsing with AI..."))
            cached_toc = parse_full_toc_with_ai(raw_toc_text, queue)
            if isinstance(cached_toc, list) and cached_toc:
                save_toc_to_cache(guide_path, cached_toc, guides_dir)
        
        toc_json_for_correction = json.dumps(cached_toc, ensure_ascii=False, indent=2) if cached_toc else None

        # Correct lesson topic syntax using Gemma, now with ToC context
        corrected_topic = correct_lesson_topic_syntax(lesson_topic, queue, toc_json=toc_json_for_correction)
        lesson_topic = corrected_topic  # Use corrected version for all subsequent operations

        # Manual pages override vs AI ToC + page-finding with fallback
        pages: list[int] = []
        pages_override = (pages_override or "").strip()
        if pages_override:
            queue.put(("log", f"⏭️ Pages choisies manuellement: {pages_override}"))
            pages = parse_page_numbers(pages_override, queue)
            if not pages:
                return
            queue.put(("progress", 60))
        else:
            page_numbers_str: str = None
            pages_source: str = None

            # Detect page offset once per guide
            page_offset = detect_page_offset(guide_path, queue)

            # Use the already-loaded cached_toc
            if cached_toc:
                queue.put(("log", "✅ Using pre-loaded ToC."))
                pr = find_pages_from_cached_toc(cached_toc, lesson_topic, queue, page_offset)
                if pr:
                    page_numbers_str = pr
                    pages_source = "cache_structured"

            # If still nothing, fall back to direct page finding on raw text
            if not page_numbers_str:
                queue.put(("progress", 50))
                page_numbers_str = get_pages_from_toc(raw_toc_text, lesson_topic, queue)
                if not page_numbers_str:
                    return
                if worker_ref.cancel_event.is_set():
                    queue.put(("log", "⏹️ Cancelled after page-finding."))
                    return
                pages_source = "direct_toc"

            queue.put(("progress", 60))
            pages = parse_page_numbers(page_numbers_str, queue)
            # Apply offset only if pages came from direct TOC page-finding (logical labels)
            if pages and pages_source == "direct_toc" and page_offset:
                pages = [p + page_offset for p in pages]
                queue.put(("log", f"↔️ Applied offset {page_offset:+d} to direct TOC pages -> {pages}"))
            if not pages:
                return

        queue.put(("progress", 75))
        with PdfExtractionSession(guide_path, queue) as guide_session:
            lesson_text = extract_lesson_text(
                guide_path,
                pages,
                queue,
                cancel_event=worker_ref.cancel_event,
                session=guide_session,
            )
        if not lesson_text:
            return
        if worker_ref.cancel_event.is_set():
            queue.put(("log", "⏹️ Cancelled after extraction."))
            return

        # Optional: student textbook extraction (can be enabled via UI checkbox)
        combined_text = lesson_text
        if use_student_textbook and textbook_dir:
            textbook_text = ""
            textbook_path = find_textbook_file(class_level, textbook_dir, queue)
            if textbook_path:
                queue.put(("log", "📖 Extracting context from student textbook..."))
                with PdfExtractionSession(textbook_path, queue) as textbook_session:
                    textbook_text = extract_lesson_text(
                        textbook_path,
                        pages,
                        queue,
                        cancel_event=worker_ref.cancel_event,
                        session=textbook_session,
                    )
                if textbook_text:
                    combined_text += f"\n\n=== CONTEXTE SUPPLÉMENTAIRE DU MANUEL ÉLÈVE ===\n\n{textbook_text}"
                    queue.put(("log", "🔗 Combined teacher guide and student textbook content."))
                else:
                    queue.put(("log", "⚠️ Could not extract textbook content."))
            if worker_ref.cancel_event.is_set():
                queue.put(("log", "⏹️ Cancelled during textbook extraction."))
                return
        elif not use_student_textbook:
            queue.put(("log", "ℹ️ Using guide only (student textbook extraction disabled)."))
        else:
            queue.put(("log", "ℹ️ No textbook folder specified."))

        # Preview gating
        if preview_source:
            queue.put(("log", "✋ User confirmation required for source text."))
            
            # Generate the prompt that would be sent to AI
            queue.put(("log", "🔧 Generating preview prompt..."))
            examples_block = build_examples_block(use_top_rated_examples)
            duree = max(10, int(duration_minutes or 45))
            
            fiche_structure = f"""
**Titre du chapitre** : (à déduire du manuel)
**Titre de la leçon** : {lesson_topic}
**Durée** : {duree} min
**Classe** : {class_level}
**Matière** : {subject if subject else "(à déduire si pertinent)"}

## Objectifs
- Identifier ...
- Décrire ...
- (Ajouter si nécessaire)

## Déroulement de la séance

### Introduction (5-10 min)
Je commence la séance par une petite question/rappel pour éveiller la curiosité des élèves.  
Je présente le titre de la leçon et j'annonce ce que nous allons apprendre aujourd'hui.

### Activité de découverte (15-20 min)
Je demande aux élèves de prendre leur manuel p. X.  
Nous observons ensemble les images / le texte.  
Je pose des questions simples : "Que voyez-vous ? Que remarquez-vous ?"  
Je note les réponses des élèves au tableau.  
Je les guide vers la découverte de la notion de la leçon.  
Les élèves réalisent les exercices indiqués.  
Je circule dans la classe pour vérifier et aider.  
On corrige collectivement.

### Synthèse et structuration (10-15 min)
Nous reprenons les points essentiels.  
Je formule avec les élèves la règle ou la conclusion.  
Les élèves recopient la conclusion dans leurs cahiers.

## Évaluation
Je propose un court exercice (oral ou écrit) pour vérifier que chacun a compris.  

## Remarques et conclusion
Rappeler aux élèves l'idée principale de la leçon.  
**Conclusion à recopier :** (un paragraphe de 3-5 lignes, claire, à noter dans le cahier)

""".strip()

            # Build the full prompt
            instructions_block = ""
            if worker_ref.special_instructions:
                instructions_block = f"""
**INSTRUCTIONS SPÉCIALES:**
---
{worker_ref.special_instructions}
---
"""
            
            prompt_template = get_configured_fiche_prompt()
            subject_line = f"Matière: {subject}" if subject else ""
            active = max(5, duree - 15)
            
            full_prompt = prompt_template.format(
                lesson_topic=lesson_topic,
                class_level=class_level,
                subject_line=subject_line,
                lesson_text=combined_text,
                fiche_structure=fiche_structure,
                examples_block=examples_block,
                instructions_block=instructions_block,
                duree=duree,
                active=active
            )
            
            queue.put(("request_source_preview", combined_text, full_prompt))
            while not worker_ref.cancel_event.is_set():
                if worker_ref.source_preview_confirmed.wait(timeout=0.2):
                    break
            if worker_ref.cancel_event.is_set():
                queue.put(("log", "⏹️ Cancelled by user during source preview."))
                return

        queue.put(("progress", 90))
        final_fiche_content = generate_fiche_from_text(
            combined_text, lesson_topic, class_level, queue,
            temperature, use_top_rated_examples,
            duration_minutes=duration_minutes, subject=subject,
            special_instructions=worker_ref.special_instructions,
            cancel_event=worker_ref.cancel_event
        )
        if not final_fiche_content:
            return
        if worker_ref.cancel_event.is_set():
            queue.put(("log", "⏹️ Cancelled after generation step."))
            return

        # Generate image if requested
        if generate_image and HAS_IMAGE_GENERATION:
            queue.put(("log", "🎨 Generating illustration..."))
            queue.put(("progress", 95))
            
            try:
                api_key = _resolve_image_api_key()
                
                if not api_key:
                    queue.put(("log", "⚠️ No API key found in keychain/settings/env - skipping image generation"))
                else:
                    # First pass: execute inline model directives like <generateimage:"...">
                    directive_prompts = _collect_image_prompts_from_markdown(final_fiche_content, max_prompts=5)
                    if directive_prompts:
                        queue.put(("log", f"🧩 Found {len(directive_prompts)} image directive(s) in fiche."))
                        final_fiche_content, generated_count = _replace_image_tags_with_images(
                            final_fiche_content,
                            class_level=class_level,
                            api_key=api_key,
                            queue=queue,
                            max_images=5,
                        )
                        if generated_count > 0:
                            queue.put(("log", f"✅ Generated and embedded {generated_count} directive image(s)."))
                        else:
                            queue.put(("log", "⚠️ Directive images requested but none were generated."))
                    else:
                        queue.put(("log", "ℹ️ No <generateimage:...> directive found; no illustration generated (model-controlled mode)."))
                        
            except Exception as e:
                queue.put(("log", f"⚠️ Image generation error: {e}"))
                # Continue without image - don't fail the entire fiche

        # Emit content for preview
        queue.put(("content", final_fiche_content))
        queue.put(("progress", 100))
        queue.put(("log", "👀 Aperçu prêt dans l'onglet Preview. Évaluez la fiche ou enregistrez en PDF/DOCX."))

    except Exception as e:
        queue.put(("log", f"💥 CRITICAL WORKER ERROR: {e}"))
    finally:
        queue.put(("enable_button", None))

class EvaluationWorker(QtCore.QThread):
    """Worker thread for generating evaluations/tests based on lesson topics."""
    log = QtCore.pyqtSignal(str)
    progress = QtCore.pyqtSignal(int)
    content = QtCore.pyqtSignal(str)
    done = QtCore.pyqtSignal(str)
    enable_buttons = QtCore.pyqtSignal()
    request_source_preview = QtCore.pyqtSignal(str, str)  # topics_summary, prompt

    def __init__(self, class_level, topics_list, subject, duration, question_types, difficulty, model_name, temperature, formatting_options=None, extra_instructions="", generate_images=False, num_images=2, guides_dir=None, eval_metadata=None, textbook_dir=None, use_student_textbook=False):
        super().__init__()
        self.class_level = class_level
        self.topics_list = topics_list
        self.subject = subject
        self.duration = duration
        self.question_types = question_types
        self.difficulty = difficulty
        self.model_name = model_name
        self.temperature = temperature
        self.cancel_event = threading.Event()
        self.confirmed = False  # Add confirmation state
        self.formatting_options = formatting_options or {}
        self.extra_instructions = extra_instructions or ""
        self.generate_images = generate_images
        self.num_images = num_images
        self.guides_dir = guides_dir or DEFAULT_INPUT_DIR
        self.eval_metadata = eval_metadata or {}
        self.textbook_dir = textbook_dir
        self.use_student_textbook = use_student_textbook

    def cancel(self):
        self.cancel_event.set()

    def confirm_evaluation_preview(self):
        """Called when user confirms the evaluation prompt preview."""
        self.confirmed = True

    def run(self):
        """Generate evaluation based on lesson topics."""
        try:
            queue = QueueProxy(self)
            
            # Log start
            queue.put(("log", f"📝 Starting evaluation generation..."))
            queue.put(("log", f"📚 Topics: {', '.join(self.topics_list)}"))
            queue.put(("log", f"🎯 Class: {self.class_level} | Subject: {self.subject}"))
            queue.put(("log", f"⏱️ Duration: {self.duration} min | Difficulty: {self.difficulty}"))
            queue.put(("progress", 10))
            
            if self.cancel_event.is_set():
                return
            
            # Extract guide text for the selected topics (like fiche generation does)
            extracted_texts = []
            guide_path = None
            cached_toc = None
            page_offset = 0
            
            # First pass: Find guide and ToC once (same for all topics in a class)
            queue.put(("log", f"📚 Searching for guide for class {self.class_level}..."))
            guide_path = find_guide_file(self.class_level, self.guides_dir, queue)
            if not guide_path:
                queue.put(("log", f"❌ No guide found for {self.class_level}. Cannot extract content."))
                queue.put(("log", f"⚠️ Will generate evaluation based on topic names only."))
            else:
                # Get cached ToC or extract it
                cached_toc = get_cached_toc(guide_path, self.guides_dir)
                if not cached_toc:
                    queue.put(("log", f"🧠 Parsing table of contents from {os.path.basename(guide_path)}..."))
                    toc_text = extract_table_of_contents(guide_path, queue)
                    if toc_text:
                        cached_toc = parse_full_toc_with_ai(toc_text, queue)
                        if cached_toc:
                            save_toc_to_cache(guide_path, cached_toc, self.guides_dir)
                            queue.put(("log", f"✅ Cached {len(cached_toc)} topics from ToC"))
                    else:
                        queue.put(("log", f"❌ Could not extract ToC text from PDF"))
                else:
                    queue.put(("log", f"✅ Using cached ToC with {len(cached_toc)} topics"))
                
                # Detect page offset if we have a guide and ToC
                if cached_toc:
                    page_offset = detect_page_offset(guide_path, queue)
            
            # Second pass: Extract content for each topic
            if guide_path and cached_toc:
                with PdfExtractionSession(guide_path, queue) as guide_session:
                    for topic in self.topics_list:
                        if self.cancel_event.is_set():
                            return

                        queue.put(("log", f"📖 Processing topic: {topic}"))

                        try:
                            # Find pages for this topic
                            page_range = find_pages_from_cached_toc(cached_toc, topic, queue, page_offset)
                            if page_range:
                                # Parse page numbers and extract text
                                page_numbers = parse_page_numbers(page_range, queue)
                                if page_numbers:
                                    lesson_text = extract_lesson_text(
                                        guide_path,
                                        page_numbers,
                                        queue,
                                        self.cancel_event,
                                        session=guide_session,
                                    )
                                    if lesson_text and lesson_text.strip():
                                        extracted_texts.append(f"=== {topic} ===\n{lesson_text}")
                                        queue.put(("log", f"✅ Extracted {len(lesson_text)} characters for '{topic}'"))
                                    else:
                                        queue.put(("log", f"⚠️ No text found on pages {page_range} for '{topic}'"))
                                else:
                                    queue.put(("log", f"⚠️ Could not parse page numbers: {page_range}"))
                            else:
                                queue.put(("log", f"⚠️ Could not find pages for '{topic}' in ToC"))

                        except Exception as e:
                            queue.put(("log", f"❌ Error extracting content for '{topic}': {e}"))
                            import traceback
                            queue.put(("log", f"Traceback: {traceback.format_exc()[:200]}"))  # Log first 200 chars of traceback
            else:
                for topic in self.topics_list:
                    queue.put(("log", f"⚠️ Skipping text extraction for '{topic}' (no guide or ToC available)"))
            
            # Combine all extracted texts
            if extracted_texts:
                combined_text = "\n\n".join(extracted_texts)
                queue.put(("log", f"📚 Successfully extracted content for {len(extracted_texts)} topics"))
            else:
                combined_text = "No content could be extracted from the guides for the selected topics."
                queue.put(("log", "⚠️ No content extracted from guides, will generate based on topic names only"))
            
            # Optional: student textbook extraction
            if self.use_student_textbook and self.textbook_dir and guide_path and cached_toc:
                textbook_path = find_textbook_file(self.class_level, self.textbook_dir, queue)
                if textbook_path:
                    queue.put(("log", "📖 Extracting context from student textbook..."))
                    textbook_texts = []

                    # Extract same topics from textbook
                    with PdfExtractionSession(textbook_path, queue) as textbook_session:
                        for topic in self.topics_list:
                            if self.cancel_event.is_set():
                                return

                            try:
                                page_range = find_pages_from_cached_toc(cached_toc, topic, queue, page_offset)
                                if page_range:
                                    page_numbers = parse_page_numbers(page_range, queue)
                                    if page_numbers:
                                        textbook_text = extract_lesson_text(
                                            textbook_path,
                                            page_numbers,
                                            queue,
                                            self.cancel_event,
                                            session=textbook_session,
                                        )
                                        if textbook_text and textbook_text.strip():
                                            textbook_texts.append(f"=== {topic} (Student Book) ===\n{textbook_text}")
                            except Exception as e:
                                queue.put(("log", f"⚠️ Could not extract '{topic}' from textbook: {e}"))
                    
                    if textbook_texts:
                        combined_text += f"\n\n=== CONTEXTE SUPPLÉMENTAIRE DU MANUEL ÉLÈVE ===\n\n" + "\n\n".join(textbook_texts)
                        queue.put(("log", f"🔗 Combined teacher guide and student textbook content ({len(textbook_texts)} topics from textbook)."))
                    else:
                        queue.put(("log", "⚠️ Could not extract textbook content."))
                        
                if self.cancel_event.is_set():
                    queue.put(("log", "⏹️ Cancelled after textbook extraction."))
                    return
                    
            elif not self.use_student_textbook:
                queue.put(("log", "ℹ️ Using guide only (student textbook extraction disabled)."))
            
            queue.put(("progress", 20))
            
            if self.cancel_event.is_set():
                return
                
            # Build evaluation prompt with extracted content
            evaluation_prompt = self._build_evaluation_prompt(combined_text)
            queue.put(("progress", 30))
            
            if self.cancel_event.is_set():
                return
            
            # Show preview to user and wait for confirmation
            queue.put(("request_source_preview", combined_text, evaluation_prompt))
            queue.put(("log", "⏸️ Waiting for user confirmation..."))
            
            # Wait for user confirmation
            while not self.confirmed and not self.cancel_event.is_set():
                self.msleep(100)  # Sleep for 100ms
            
            if self.cancel_event.is_set():
                queue.put(("log", "⏹️ Cancelled before AI generation."))
                return
                
            queue.put(("log", f"🤖 Using Gemini model: {self.model_name}"))
            queue.put(("progress", 40))
            
            if self.cancel_event.is_set():
                queue.put(("log", "⏹️ Cancelled before AI generation."))
                return
                
            # Generate evaluation content using Gemini
            queue.put(("log", "🚀 Sending request to Gemini API (this may take 30-60 seconds)..."))
            evaluation_content = None

            response = generate_with_fallback(
                evaluation_prompt,
                self.temperature,
                queue,
                "evaluation-generation",
                response_schema=_evaluation_response_schema(),
                response_mime_type="application/json",
            )
                
            queue.put(("progress", 90))
            
            if self.cancel_event.is_set():
                queue.put(("log", "⏹️ Cancelled after AI generation."))
                return
                
            parsed_evaluation: Optional[Dict[str, Any]] = None
            if response:
                parsed = _parse_structured_response(response)
                if isinstance(parsed, dict):
                    parsed_evaluation = parsed
                    evaluation_content = _render_evaluation_markdown(parsed)
                else:
                    evaluation_content = (response.text or "").strip()

            if evaluation_content:
                queue.put(("log", "✅ Evaluation generated successfully!"))
                
                if self.cancel_event.is_set():
                    queue.put(("log", "⏹️ Cancelled before image generation."))
                    return
                
                # Generate images if requested and available
                if self.generate_images and HAS_IMAGE_GENERATION:
                    queue.put(("log", f"🎨 Generating up to {self.num_images} exercise-linked illustration(s)..."))
                    queue.put(("progress", 92))
                    
                    try:
                        api_key = _resolve_image_api_key()
                        
                        if not api_key:
                            queue.put(("log", "⚠️ No API key found in keychain/settings/env - skipping image generation"))
                        else:
                            if not parsed_evaluation:
                                queue.put(("log", "⚠️ Structured evaluation missing, cannot reliably link images to exercises."))
                            else:
                                image_jobs = _extract_exercise_image_jobs(parsed_evaluation, self.num_images)
                                if not image_jobs:
                                    queue.put(("log", "⚠️ No eligible exercise content found for image generation."))
                                else:
                                    queue.put(("log", f"🧩 Prepared {len(image_jobs)} image job(s) from specific exercises."))

                                    is_young = (self.class_level or "").lower() in ["cp", "ce1"]
                                    style = "coloring" if is_young else "diagram"
                                    aspect_ratio = "1:1" if is_young else "16:9"

                                    image_blocks: Dict[int, str] = {}
                                    for ordinal, job in enumerate(image_jobs, start=1):
                                        if self.cancel_event.is_set():
                                            queue.put(("log", "⏹️ Cancelled during image generation."))
                                            return

                                        ex_idx = int(job.get("exercise_index", ordinal))
                                        queue.put(("log", f"🖼️ Generating illustration {ordinal}/{len(image_jobs)} for Exercice {ex_idx}..."))

                                        prompt = _build_exercise_prompt(job, self.class_level, self.subject)
                                        image_bytes = generate_illustration(
                                            prompt=prompt,
                                            class_level=self.class_level,
                                            style=style,
                                            aspect_ratio=aspect_ratio,
                                            api_key=api_key,
                                        )
                                        if not image_bytes:
                                            queue.put(("log", f"⚠️ No image generated for Exercice {ex_idx}."))
                                            continue

                                        base64_img = image_to_base64(image_bytes)
                                        caption = f"Illustration ciblée pour Exercice {ex_idx}: {job.get('title') or ''}".strip()
                                        image_blocks[ex_idx] = (
                                            f"![Illustration Exercice {ex_idx}](data:image/png;base64,{base64_img})\n"
                                            f"*{caption}*"
                                        )

                                    if image_blocks:
                                        evaluation_content = _embed_images_under_exercises(evaluation_content, image_blocks)
                                        queue.put(("log", f"✅ Embedded {len(image_blocks)} exercise-linked image(s)."))
                                    else:
                                        queue.put(("log", "⚠️ No images were generated for the selected exercises."))
                            
                            if self.cancel_event.is_set():
                                queue.put(("log", "⏹️ Cancelled during image generation."))
                                return
                                
                    except Exception as e:
                        queue.put(("log", f"⚠️ Image generation error: {e}"))
                        # Continue without images - don't fail the entire evaluation
                    
                    queue.put(("progress", 95))
                
                if self.cancel_event.is_set():
                    queue.put(("log", "⏹️ Cancelled before sending final content."))
                    return
                
                queue.put(("content", evaluation_content))
                queue.put(("done", f"Evaluation for {', '.join(self.topics_list)}"))
            else:
                queue.put(("log", "❌ Failed to generate evaluation content"))
                
            queue.put(("progress", 100))
            
        except Exception as e:
            if self.cancel_event.is_set():
                queue.put(("log", "⏹️ Generation cancelled by user."))
            else:
                queue.put(("log", f"❌ Evaluation Generation Error: {e}"))
                import traceback
                queue.put(("log", f"Stack trace: {traceback.format_exc()}"))
        finally:
            queue.put(("enable_buttons", None))

    def _build_evaluation_prompt(self, extracted_content: str = "") -> str:
        """Build a high-structure, exam-first prompt for evaluation generation."""
        topics_text = ", ".join(self.topics_list)
        school_name = self.eval_metadata.get("school_name", "Groupe Scolaire")
        academic_year = self.eval_metadata.get("academic_year", "2025/2026")
        eval_number = int(self.eval_metadata.get("eval_number", 1) or 1)
        semester = str(self.eval_metadata.get("semester", "1"))
        max_score = int(self.eval_metadata.get("max_score", 10) or 10)

        num_word = "1er" if eval_number == 1 else f"{eval_number}e"
        sem_word = "1er" if semester == "1" else f"{semester}e"
        session_label = f"{num_word} contrôle du {sem_word} semestre"

        force_types = []
        if self.formatting_options.get("include_tables"):
            force_types.append("table")
        if self.formatting_options.get("include_matching"):
            force_types.append("matching")
        if self.question_types:
            force_types.append(self.question_types)
        forced_types_text = ", ".join(force_types) if force_types else "aucun type forcé"

        source_text = (extracted_content or "").strip()
        source_block = (
            f"EXTRAIT DE RÉFÉRENCE (prioritaire):\n---\n{source_text[:5000]}\n---"
            if source_text
            else "Aucun extrait source fourni. Génère une évaluation fidèle au programme attendu du niveau."
        )

        answer_key_rule = (
            "Inclure un corrigé final synthétique mais complet."
            if self.formatting_options.get("include_answer_key")
            else "Ne pas inclure de corrigé détaillé; expected_answer doit rester bref."
        )

        extra_block = (self.extra_instructions or "").strip()
        if extra_block:
            extra_block = f"\nCONSIGNES SUPPLÉMENTAIRES ENSEIGNANT:\n{extra_block}\n"

        prompt = f"""Tu es un concepteur d'épreuves scolaires francophones. Ta mission: produire une évaluation exploitable immédiatement, rigoureuse, claire, et variée.

CONTEXTE FIXE:
- Établissement: {school_name}
- Niveau: {self.class_level.upper()}
- Matière: {self.subject or "Sciences"}
- Sujets à couvrir: {topics_text}
- Durée totale: {int(self.duration)} minutes
- Barème total imposé: {max_score} points
- Session: {session_label}

{source_block}
{extra_block}
CONTRAINTES PÉDAGOGIQUES OBLIGATOIRES:
1. Générer 4 à 6 exercices, progressifs (facile -> moyen -> transfert).
2. Varier explicitement les modalités: compréhension, application, raisonnement, production.
3. Formulations courtes, consignes nettes, adaptées à {self.class_level.upper()}.
4. Aucun exercice redondant; chaque exercice doit tester une compétence distincte.
5. Si QCM, distracteurs plausibles (pas absurdes).
6. Types à forcer si possible: {forced_types_text}.
7. {answer_key_rule}

CONTRAINTES DE STRUCTURE JSON:
- Retourner uniquement un objet JSON valide (aucun texte hors JSON).
- Utiliser exactement les champs du schéma: school_name, header, exercises, answer_key.
- header doit inclure:
  class_level="{self.class_level.upper()}", academic_year="{academic_year}", evaluation_number={eval_number}, semester="{semester}", session_label="{session_label}", duration_minutes={int(self.duration)}, max_score={max_score}, subject="{self.subject or 'Sciences'}".
- exercises: liste d'objets avec title, instructions, points, questions.
- questions: objets avec prompt, answer_type, expected_answer.
- La somme des points des exercices doit être EXACTEMENT {max_score}.

QUALITÉ ATTENDUE:
- Exigences réalistes de copie d'élève.
- Énoncés auto-suffisants (sans devoir lire le manuel).
- Cohérence interne entre consignes, barème et corrigé.
"""
        return prompt

class QuizWorker(QtCore.QThread):
    """Worker thread for generating quick quizzes based on a single topic."""
    log = QtCore.pyqtSignal(str)
    progress = QtCore.pyqtSignal(int)
    content = QtCore.pyqtSignal(str)
    done = QtCore.pyqtSignal(str)
    enable_buttons = QtCore.pyqtSignal()

    def __init__(self, class_level, topic, subject, quiz_type, quiz_format, difficulty,
                 duration, num_questions, include_answers, extra_instructions, temperature,
                 guides_dir, textbook_dir, use_student_textbook):
        super().__init__()
        self.class_level = class_level
        self.topic = topic
        self.subject = subject
        self.quiz_type = quiz_type
        self.quiz_format = quiz_format
        self.difficulty = difficulty
        self.duration = duration
        self.num_questions = num_questions
        self.include_answers = include_answers
        self.extra_instructions = extra_instructions
        self.temperature = temperature
        self.guides_dir = guides_dir
        self.textbook_dir = textbook_dir
        self.use_student_textbook = use_student_textbook
        self.cancel_event = threading.Event()

    def cancel(self):
        self.cancel_event.set()

    def run(self):
        """Generate a quiz based on the topic."""
        try:
            queue = QueueProxy(self)
            
            queue.put(("log", f"🎯 Starting quiz generation..."))
            queue.put(("log", f"📚 Topic: {self.topic}"))
            queue.put(("log", f"🎯 Class: {self.class_level} | Format: {self.quiz_format}"))
            queue.put(("progress", 10))
            
            if self.cancel_event.is_set():
                return
            
            # Try to extract content from guide
            extracted_text = ""
            guide_path = find_guide_file(self.class_level, self.guides_dir, queue)
            page_numbers = []
            
            if guide_path:
                cached_toc = get_cached_toc(guide_path, self.guides_dir)
                if cached_toc:
                    page_offset = detect_page_offset(guide_path, queue)
                    page_range = find_pages_from_cached_toc(cached_toc, self.topic, queue, page_offset)
                    
                    if page_range:
                        page_numbers = parse_page_numbers(page_range, queue)
                        if page_numbers:
                            with PdfExtractionSession(guide_path, queue) as guide_session:
                                extracted_text = extract_lesson_text(
                                    guide_path,
                                    page_numbers,
                                    queue,
                                    self.cancel_event,
                                    session=guide_session,
                                )
                            if extracted_text:
                                queue.put(("log", f"✅ Extracted {len(extracted_text)} characters from guide"))
            
            queue.put(("progress", 30))
            
            if self.cancel_event.is_set():
                return
            
            # Optional: extract from student textbook
            if self.use_student_textbook and self.textbook_dir and extracted_text:
                textbook_path = find_textbook_file(self.class_level, self.textbook_dir, queue)
                if textbook_path:
                    queue.put(("log", "📖 Extracting from student textbook..."))
                    # Use same pages as guide
                    if page_numbers:
                        with PdfExtractionSession(textbook_path, queue) as textbook_session:
                            textbook_text = extract_lesson_text(
                                textbook_path,
                                page_numbers,
                                queue,
                                self.cancel_event,
                                session=textbook_session,
                            )
                        if textbook_text:
                            extracted_text += f"\n\n=== MANUEL ÉLÈVE ===\n{textbook_text}"
                            queue.put(("log", "🔗 Added textbook content"))
            
            queue.put(("progress", 40))
            
            if self.cancel_event.is_set():
                return
            
            # Build quiz prompt
            prompt = self._build_quiz_prompt(extracted_text)
            
            queue.put(("log", f"🤖 Generating quiz with {self.num_questions} questions..."))
            queue.put(("progress", 50))
            
            # Generate with fallback
            response = generate_with_fallback(
                prompt,
                self.temperature,
                queue,
                "quiz-generation",
                response_schema=_quiz_response_schema(),
                response_mime_type="application/json",
            )
            
            queue.put(("progress", 90))
            
            if self.cancel_event.is_set():
                return
            
            if response:
                parsed = _parse_structured_response(response)
                if parsed:
                    quiz_content = _render_quiz_markdown(parsed)
                else:
                    quiz_content = (response.text or "").strip()
                if quiz_content:
                    queue.put(("log", "✅ Quiz generated successfully!"))
                    queue.put(("content", quiz_content))
                    queue.put(("done", f"Quiz: {self.topic}"))
                else:
                    queue.put(("log", "❌ Empty response from AI"))
            else:
                queue.put(("log", "❌ Failed to generate quiz"))
            
            queue.put(("progress", 100))
            
        except Exception as e:
            if self.cancel_event.is_set():
                queue.put(("log", "⏹️ Quiz generation cancelled"))
            else:
                queue.put(("log", f"❌ Quiz Generation Error: {e}"))
                import traceback
                queue.put(("log", f"Stack trace: {traceback.format_exc()}"))
        finally:
            queue.put(("enable_buttons", None))

    def _build_quiz_prompt(self, extracted_content: str = "") -> str:
        """Build a deeply varied quiz prompt and request structured JSON output."""
        seed = int(time.time() * 1000) % 100000
        rng = random.Random(seed)

        format_map = {
            "Mixed (MCQ + Short Answer)": ["mcq", "short-answer", "true-false", "fill-blank"],
            "Multiple Choice Only": ["mcq"],
            "Short Answer Only": ["short-answer", "scenario"],
            "True/False + MCQ": ["true-false", "mcq"],
            "Fill in the Blanks": ["fill-blank", "word-bank"],
        }
        allowed_types = format_map.get(self.quiz_format, ["mcq", "short-answer"])

        challenge_modes = [
            "Rapid-fire quiz avec questions brèves et variées",
            "Mini enquête avec indices contextuels",
            "Quiz à paliers (facile -> moyen -> défi)",
            "Quiz situationnel basé sur des mini-cas concrets",
        ]
        mode = rng.choice(challenge_modes)

        source_block = (
            f"SOURCE:\n---\n{extracted_content[:3200]}\n---"
            if extracted_content
            else "Pas d'extrait source: s'appuyer sur les acquis attendus du niveau."
        )

        correction_rule = "Inclure un answer_key exploitable." if self.include_answers else "answer_key très bref (peut être vide)."
        extra = (self.extra_instructions or "").strip()
        extra_block = f"\nCONTRAINTE PROF: {extra}\n" if extra else ""

        prompt = f"""Tu es un concepteur de quiz innovants pour enseignants du primaire/collège.

PARAMÈTRES:
- Classe: {self.class_level}
- Sujet principal: {self.topic}
- Matière: {self.subject or 'Générale'}
- Durée: {self.duration} min
- Nombre de questions: {self.num_questions}
- Niveau de difficulté: {self.difficulty}
- Mode de quiz: {mode}
- Graine de variation: {seed}
- Types autorisés: {', '.join(allowed_types)}

{source_block}

OBJECTIF ANTI-RÉPÉTITION:
1. Générer des questions avec débuts de phrase différents.
2. Éviter les formulations scolaires stéréotypées répétées.
3. Alterner les mécanismes cognitifs (repérer, comparer, appliquer, justifier).
4. Créer des distracteurs plausibles pour QCM.
5. Introduire au moins 2 contextes concrets de vie quotidienne.

SORTIE JSON STRICTE:
- title (str)
- class_level (str)
- topic (str)
- subject (str)
- duration_minutes (int)
- instructions (list[str])
- questions (list[object])
  - chaque question: number (int), type (str), prompt (str), options (list[str], optional), expected_answer (str, optional)
- answer_key (list[str], optional)

RÈGLES SUPPLÉMENTAIRES:
- Respecter exactement {self.num_questions} questions.
- Utiliser seulement les types autorisés.
- Numéroter proprement de 1 à {self.num_questions}.
- {correction_rule}
{extra_block}

Réponds uniquement avec un JSON valide.
"""
        return prompt

class GenerationWorker(QtCore.QThread):
    log = QtCore.pyqtSignal(str)
    progress = QtCore.pyqtSignal(int)
    content = QtCore.pyqtSignal(str)
    done = QtCore.pyqtSignal(str)
    enable_buttons = QtCore.pyqtSignal()
    request_source_preview = QtCore.pyqtSignal(str, str)  # source_text, prompt

    def __init__(self, class_level, lesson_topic, pages_override, temperature, guides_dir, textbook_dir, use_top_rated_examples, duration_minutes: int, subject: str, preview_source: bool, special_instructions: str, generate_image: bool = False, use_student_textbook: bool = False):
        super().__init__()
        self.class_level = class_level
        self.lesson_topic = lesson_topic
        self.pages_override = pages_override
        self.temperature = temperature
        self.guides_dir = guides_dir
        self.textbook_dir = textbook_dir
        self.use_top_rated_examples = use_top_rated_examples
        self.duration_minutes = duration_minutes
        self.subject = subject
        self.preview_source = preview_source
        self.special_instructions = special_instructions
        self.cancel_event = threading.Event()
        self.source_preview_confirmed = threading.Event()
        self.generate_image = generate_image
        self.use_student_textbook = use_student_textbook

    def confirm_source_preview(self):
        self.source_preview_confirmed.set()

    def cancel(self):
        self.cancel_event.set()
        # If we are waiting for user confirmation, unblock the worker thread
        self.source_preview_confirmed.set()

    def run(self):
        q = QueueProxy(self)
        pipeline_run(
            self.class_level,
            self.lesson_topic,
            q,
            self.pages_override,
            self.temperature,
            self.guides_dir,
            self.textbook_dir,
            self.use_top_rated_examples,
            self.duration_minutes,
            self.subject,
            self.preview_source,
            self,
            self.generate_image,
            self.use_student_textbook
        )

