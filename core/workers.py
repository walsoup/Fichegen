import os
import json
import threading
import time
from datetime import datetime
from typing import Optional, List, Dict, Any

from PyQt6 import QtCore

from config import (
    DEFAULT_INPUT_DIR, DEFAULT_OUTPUT_DIR, API_KEYS,
    get_configured_fiche_prompt, get_configured_flash_model,
    get_configured_pro_model, HAS_IMAGE_GENERATION, HAS_DOCX,
    get_configured_page_finding_prompt, GEMINI_TOC_MODEL
)
from core.ai import (
    generate_with_fallback, _fiche_response_schema, _parse_structured_response,
    _render_fiche_markdown, _evaluation_response_schema, _render_evaluation_markdown,
    _generate_with_model
)
from core.toc import (
    find_guide_file, find_textbook_file, extract_table_of_contents,
    get_cached_toc, parse_full_toc_with_ai, save_toc_to_cache,
    detect_page_offset, correct_lesson_topic_syntax, parse_page_numbers,
    find_pages_from_cached_toc, get_pages_from_toc, extract_lesson_text
)
from core.image_gen import (
    generate_fiche_illustration, generate_evaluation_illustrations, image_to_base64
)
from utils.helpers import get_top_rated_examples
from core.model_fetcher import fetch_available_models, find_best_models_with_ai

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
        elif msg_type == "enable_button":
            self.worker.enable_buttons.emit()
        elif msg_type == "request_source_preview":
            try:
                if len(item) == 3:  # source_text and prompt provided
                    self.worker.request_source_preview.emit(str(item[1]), str(item[2]))
                else:  # backwards compatibility
                    self.worker.request_source_preview.emit(str(payload), "")
            except Exception:
                pass

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
        lesson_text = extract_lesson_text(guide_path, pages, queue, cancel_event=worker_ref.cancel_event)
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
                textbook_text = extract_lesson_text(textbook_path, pages, queue, cancel_event=worker_ref.cancel_event)
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
            worker_ref.source_preview_confirmed.wait()
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
                settings_obj = QtCore.QSettings("FicheGen", "Pedago")
                api_key = settings_obj.value("gemini_api_key", "")
                
                if not api_key:
                    queue.put(("log", "⚠️ No API key found - skipping image generation"))
                else:
                    # Generate a single illustration for the fiche
                    image_data = generate_fiche_illustration(
                        lesson_topic=lesson_topic,
                        class_level=class_level,
                        context=combined_text[:1500],  # Pass some context
                        api_key=api_key
                    )
                    
                    if image_data:
                        queue.put(("log", "✅ Illustration generated"))
                        
                        # Embed image in the fiche content
                        try:
                            base64_img = image_to_base64(image_data)
                            image_markdown = f"\n\n---\n\n## 📸 Illustration\n\n![Illustration](data:image/png;base64,{base64_img})\n\n"
                            final_fiche_content += image_markdown
                            queue.put(("log", "✅ Image embedded in fiche"))
                        except Exception as e:
                            queue.put(("log", f"⚠️ Failed to embed image: {e}"))
                    else:
                        queue.put(("log", "⚠️ No image was generated"))
                        
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
            for topic in self.topics_list:
                if self.cancel_event.is_set():
                    return
                    
                queue.put(("log", f"📖 Processing topic: {topic}"))
                
                # Use the same extraction logic as fiche generation
                try:
                    if not guide_path or not cached_toc:
                        queue.put(("log", f"⚠️ Skipping text extraction for '{topic}' (no guide or ToC available)"))
                        continue
                    
                    # Find pages for this topic
                    page_range = find_pages_from_cached_toc(cached_toc, topic, queue, page_offset)
                    if page_range:
                        # Parse page numbers and extract text
                        page_numbers = parse_page_numbers(page_range, queue)
                        if page_numbers:
                            lesson_text = extract_lesson_text(guide_path, page_numbers, queue, self.cancel_event)
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
                    for topic in self.topics_list:
                        if self.cancel_event.is_set():
                            return
                        
                        try:
                            page_range = find_pages_from_cached_toc(cached_toc, topic, queue, page_offset)
                            if page_range:
                                page_numbers = parse_page_numbers(page_range, queue)
                                if page_numbers:
                                    textbook_text = extract_lesson_text(textbook_path, page_numbers, queue, self.cancel_event)
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
                
            if response:
                parsed = _parse_structured_response(response)
                if parsed:
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
                    queue.put(("log", f"🎨 Generating {self.num_images} illustration(s)..."))
                    queue.put(("progress", 92))
                    
                    try:
                        settings = QtCore.QSettings("FicheGen", "Pedago")
                        api_key = settings.value("gemini_api_key", "")
                        
                        if not api_key:
                            queue.put(("log", "⚠️ No API key found - skipping image generation"))
                        else:
                            # Generate images appropriate for the grade level
                            generated_images = generate_evaluation_illustrations(
                                topics=self.topics_list,
                                class_level=self.class_level,
                                num_images=self.num_images,
                                api_key=api_key
                            )
                            
                            if self.cancel_event.is_set():
                                queue.put(("log", "⏹️ Cancelled during image generation."))
                                return
                            
                            if generated_images:
                                queue.put(("log", f"✅ Generated {len(generated_images)} image(s)"))
                                
                                # Embed images in the evaluation content
                                images_markdown = "\n\n---\n\n## 📸 Illustrations\n\n"
                                for idx, img_data in enumerate(generated_images, 1):
                                    try:
                                        base64_img = image_to_base64(img_data)
                                        images_markdown += f"![Illustration {idx}](data:image/png;base64,{base64_img})\n\n"
                                    except Exception as e:
                                        queue.put(("log", f"⚠️ Failed to embed image {idx}: {e}"))
                                
                                # Append images to the evaluation content
                                evaluation_content += images_markdown
                                queue.put(("log", "✅ Images embedded in evaluation"))
                            else:
                                queue.put(("log", "⚠️ No images were generated"))
                                
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
        """
        Build a pedagogically sound evaluation prompt with comprehensive guidance.
        """
        settings = QtCore.QSettings("FicheGen", "Pedago")
        use_top_examples = settings.value("use_top_examples", "true") == "true"

        topics_text = ", ".join(self.topics_list)

        # Build examples block (tone/style), reused from fiche generation
        try:
            examples_block = build_examples_block(use_top_examples)
        except Exception:
            examples_block = ""  # Fallback silently if anything goes wrong

        # ============================================================================
        # PEDAGOGICAL FOUNDATIONS & COGNITIVE LEVELS
        # ============================================================================
        
        # Map class level to cognitive expectations (Bloom's Taxonomy adapted for primary)
        cognitive_map = {
            "cp": "Se rappeler (nommer, identifier, reconnaître)",
            "ce1": "Comprendre (expliquer, décrire, donner des exemples)",
            "ce2": "Appliquer (utiliser, résoudre, calculer)",
            "cm1": "Analyser (comparer, catégoriser, distinguer)",
            "cm2": "Évaluer et créer (argumenter, proposer, synthétiser)",
            "6e": "Analyser et évaluer (justifier, critiquer, défendre)"
        }
        cognitive_level = cognitive_map.get(self.class_level.lower(), "Comprendre et appliquer")
        
        # Special considerations for early grades (limited writing skills)
        is_early_grade = self.class_level.lower() in ["cp", "ce1"]
        if is_early_grade:
            early_grade_note = """

⚠️ ADAPTATION CP/CE1 - ÉCRITURE LIMITÉE
Les élèves de CP et CE1 ont des capacités d'écriture limitées. PRIVILÉGIE:
- Exercices visuels: relier, entourer, colorier, cocher
- Questions à réponse unique (un mot, un nombre)
- Exercices de mise en relation (colonne A → colonne B)
- "Relie" (connect-the-dots) avec symboles ou images
- QCM avec cases à cocher
- Compléter avec une banque de mots donnée
- Coller/dessiner (si pertinent)

ÉVITE:
- Phrases complètes à rédiger
- Justifications longues
- Questions ouvertes nécessitant plusieurs lignes
- Production écrite extensive

ASTUCE: Maximum 1-2 mots par réponse attendue, sauf exception justifiée."""
        else:
            early_grade_note = ""
        
        # ============================================================================
        # SOURCE MATERIAL INTEGRATION
        # ============================================================================
        
        if extracted_content.strip():
            content_section = f"""

📚 MATÉRIEL SOURCE (extraits des guides pédagogiques) :
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
{extracted_content[:3000]}{"..." if len(extracted_content) > 3000 else ""}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

CONSIGNE : Utilise ce matériel pour créer des questions PRÉCISES et CONTEXTUALISÉES.
- Extrais les concepts clés, vocabulaire spécifique, et exemples concrets
- Assure-toi que chaque question est vérifiable dans le contenu ci-dessus
- Évite les questions trop génériques ou hors-sujet"""
        else:
            content_section = f"""

⚠️ AUCUN MATÉRIEL SOURCE DISPONIBLE
Tu dois créer l'évaluation en te basant sur :
- Les programmes officiels français pour le niveau {self.class_level}
- Ta connaissance des compétences attendues à ce niveau
- Le référentiel de {self.subject or "cette matière"}"""
        
        # ============================================================================
        # PDF FORMATTING CONTROLS (AI has full control over presentation)
        # ============================================================================
        
        wants_tables = bool(self.formatting_options.get("include_tables"))
        wants_boxes = bool(self.formatting_options.get("include_boxes"))
        wants_matching = bool(self.formatting_options.get("include_matching"))
        wants_answers = bool(self.formatting_options.get("include_answer_key"))
        
        # Comprehensive formatting guide with examples
        formatting_guide = """

═══════════════════════════════════════════════════════════════════════════════
📐 CONTRÔLES DE FORMATAGE PDF - Tu as le contrôle total sur la présentation
═══════════════════════════════════════════════════════════════════════════════

TITRES & HIÉRARCHIE:
  # Titre principal (niveau 1) : Titre de l'évaluation
  ## Section principale (niveau 2) : Parties, Corrigé
  ### Sous-section (niveau 3) : Exercices individuels, sous-parties

EMPHASE & MISE EN ÉVIDENCE:
  **Texte en gras** : Mots-clés, consignes importantes, titres d'exercices
  *Texte en italique* : Exemples, notes, indications optionnelles
  
LISTES & ÉNUMÉRATIONS:
  - Liste simple avec tirets (pour options, étapes)
  1. Liste numérotée (pour questions séquentielles)
  
ESPACEMENT & CLARTÉ:
  - Ligne vide entre chaque exercice pour aérer
  - Deux lignes vides entre grandes parties
  - Espaces de réponse: _______________ (13+ underscores)
  - Petits espaces: _____ (5 underscores)"""

        if wants_tables:
            formatting_guide += """

TABLEAUX MARKDOWN (structure rigoureuse):
  | Entête 1 | Entête 2 | Entête 3 |
  | -------- | -------- | -------- |
  | Cellule  | Cellule  | Cellule  |
  
  RÈGLES IMPÉRATIVES:
  ✓ Ligne séparatrice obligatoire (| --- | --- |)
  ✓ Espaces de réponse dans cellules: ________
  ✓ Alignement uniforme des pipes |
  ✗ PAS de backticks autour du tableau
  ✗ PAS de légende ou titre au-dessus
  
  EXEMPLE COMPLET:
  | N° | Question | Réponse |
  | -- | -------- | ------- |
  | 1  | 5 + 3 =  | _____   |
  | 2  | 9 - 4 =  | _____   |"""

        if wants_boxes:
            formatting_guide += """

ENCADRÉS & CALLOUTS (pour instructions critiques):
  > **📌 Consigne importante**
  > Lis attentivement avant de commencer.
  > Vérifie tes réponses à la fin.
  
  > **💡 Astuce**
  > Commence par les questions les plus faciles.
  
  > **⚠️ Attention**
  > N'oublie pas d'indiquer les unités (cm, g, etc.)"""

        if wants_matching:
            formatting_guide += """

EXERCICES DE MISE EN RELATION / "RELIE":
  Option 1 - Structure en deux colonnes:
  
  **Colonne A** (Définitions)        **Colonne B** (Termes)
  1. Organe de la respiration        a) Cœur
  2. Organe de la circulation        b) Poumon
  3. Organe de la digestion          c) Estomac
  
  Réponses: 1-___ | 2-___ | 3-___
  
  Option 2 - Format visuel avec points:
  
  Relie chaque animal à son habitat:
  
  1. Poisson  •              • a) Forêt
  2. Oiseau   •              • b) Océan  
  3. Écureuil •              • c) Ciel
  
  
  RÈGLES:
  ✓ Équilibre des colonnes (même nombre d'items)
  ✓ Ordre mélangé (ne pas mettre 1-a, 2-b, 3-c)
  ✓ Espaces de réponse clairs"""

        formatting_guide += """

ESPACES DE RÉPONSE (adapter selon le type):
  - Réponse courte (1 mot): _____
  - Réponse moyenne (phrase): _______________
  - Calcul/nombre: _____ (avec unité si nécessaire)
  - Cases à cocher: ☐ Option A  ☐ Option B
  - Ligne complète: _________________________________________________

BARÈME & NOTATION:
  Indique les points APRÈS chaque question/exercice:
  **Exercice 1** (3 pts)
  Question a) (1 pt)
  Question b) (2 pts)
  
═══════════════════════════════════════════════════════════════════════════════
"""

        # ============================================================================
        # QUALITY CRITERIA & COMMON PITFALLS
        # ============================================================================
        
        quality_criteria = f"""

═══════════════════════════════════════════════════════════════════════════════
✨ CRITÈRES DE QUALITÉ PÉDAGOGIQUE
═══════════════════════════════════════════════════════════════════════════════

PROGRESSION COGNITIVE (Bloom):
  Niveau ciblé pour {self.class_level}: {cognitive_level}
  
  Partie 1 (30-40% des points): Connaissances de base
    → Se rappeler: définitions, faits, vocabulaire
    → Questions: QCM, vrai/faux, compléter les blancs
    → Exemple: "Quel est le nom de l'organe qui pompe le sang?"
  
  Partie 2 (40-50% des points): Application & Compréhension
    → Appliquer: résoudre, calculer, utiliser
    → Questions: problèmes, exercices pratiques, schémas à compléter
    → Exemple: "Calcule le périmètre d'un rectangle de 5cm × 3cm"
  
  Partie 3 (15-20% des points): Analyse & Réflexion
    → Analyser: comparer, expliquer, justifier
    → Questions: pourquoi, comment, quelle différence
    → Exemple: "Explique pourquoi les plantes ont besoin de lumière"

ADAPTATION AU NIVEAU {self.class_level.upper()}:
  ✓ Vocabulaire simple et précis (évite jargon technique excessif)
  ✓ Phrases courtes (max 15-20 mots par consigne)
  ✓ Consignes à l'impératif (Calcule, Écris, Complète, Relie)
  ✓ Un seul verbe d'action par question
  ✓ Contextes familiers et concrets (vie quotidienne, école, famille)

ÉQUILIBRE & VARIÉTÉ:
  ✓ Minimum 3 types de questions différents
  ✓ Mélange de questions fermées (QCM) et ouvertes (justifications)
  ✓ Alternance entre rappel et réflexion
  ✓ Au moins une question visuelle/schéma si pertinent
  ✓ Progression du plus simple au plus complexe

CLARTÉ DES CONSIGNES:
  ✓ "Réponds" → "Écris ta réponse" (plus explicite)
  ✓ "Donne un exemple" → "Donne UN exemple tiré de la leçon"
  ✓ Indique le format attendu: (en 2-3 lignes), (un seul mot), (un nombre)
  ✓ Précise les unités: (en cm), (en grammes), (en minutes)

BARÈME COHÉRENT:
  ✓ Total exactement 20 points
  ✓ Points proportionnels à la difficulté
  ✓ Questions simples: 0.5-1 pt
  ✓ Questions moyennes: 1.5-2 pts
  ✓ Questions complexes: 3-4 pts
  ✓ Barème partiel pour questions à étapes

═══════════════════════════════════════════════════════════════════════════════
⚠️  PIÈGES À ÉVITER ABSOLUMENT
═══════════════════════════════════════════════════════════════════════════════

❌ Questions ambiguës: "Parle de la photosynthèse" 
   ✓ Précis: "Explique en 2 phrases comment les plantes fabriquent leur nourriture"

❌ Plusieurs questions en une: "Nomme et explique les trois états de l'eau"
   ✓ Séparé: Question 1: Nomme... | Question 2: Explique...

❌ Négations doubles: "Laquelle n'est pas incorrecte?"
   ✓ Simple: "Laquelle est correcte?"

❌ Indices dans les questions suivantes:
   Q1: "Combien font 5+3?" Q2: "Si 5+3=8, alors..."
   ✓ Questions indépendantes

❌ QCM avec une seule option correcte évidente
   ✓ Distracteurs plausibles basés sur erreurs communes

❌ Vocabulaire trop complexe pour le niveau
   ✓ Adapter: "habitat" plutôt que "niche écologique" en CE1

❌ Questions nécessitant connaissances extérieures non enseignées
   ✓ Rester dans le périmètre des sujets: {topics_text}

❌ Barème incohérent (question difficile = 1pt, facile = 4pts)
   ✓ Proportionnel à l'effort cognitif requis

═══════════════════════════════════════════════════════════════════════════════
"""

        # ============================================================================
        # STRUCTURE TEMPLATE WITH EXAMPLES
        # ============================================================================
        
        structure_template = f"""

═══════════════════════════════════════════════════════════════════════════════
📋 STRUCTURE DE L'ÉVALUATION (à respecter strictement)
═══════════════════════════════════════════════════════════════════════════════

# Évaluation – {self.subject or "Matière"} – {self.class_level.upper()}

### 📊 Informations
**Nom**: ____________________________  **Prénom**: ____________________________  
**Classe**: {self.class_level.upper()}  **Date**: _______________

**Sujets évalués**: {topics_text}  
**Durée**: {self.duration} minutes  
**Total**: _____ / 20 points

### 📝 Consignes générales
{">" if wants_boxes else ""} Lis chaque question attentivement avant de répondre  
{">" if wants_boxes else ""} Écris lisiblement avec un stylo bleu ou noir  
{">" if wants_boxes else ""} Gère bien ton temps: {self.duration} minutes pour toute l'évaluation  
{">" if wants_boxes else ""} N'oublie pas de vérifier tes réponses à la fin

---

## Partie 1 : Connaissances (≈ 6-8 points)

*Cette partie évalue ta maîtrise des notions de base.*

**Exercice 1 – [Nom de l'exercice]** (X pts)

[Questions de rappel: QCM, vrai/faux, vocabulaire, définitions]

---

## Partie 2 : Application (≈ 8-10 points)

*Cette partie évalue ta capacité à utiliser tes connaissances.*

**Exercice 2 – [Nom de l'exercice]** (X pts)

[Exercices pratiques: calculs, problèmes, schémas, situations concrètes]

---

## Partie 3 : Analyse et Réflexion (≈ 2-4 points)

*Cette partie évalue ta compréhension approfondie.*

**Exercice 3 – [Nom de l'exercice]** (X pts)

[Questions ouvertes: explications, comparaisons, justifications]

---

## 📊 Barème récapitulatif

| Partie | Points | Note obtenue |
| ------ | ------ | ------------ |
| Partie 1: Connaissances | ___ / X | ___ |
| Partie 2: Application | ___ / X | ___ |
| Partie 3: Réflexion | ___ / X | ___ |
| **TOTAL** | **___ / 20** | **___** |

{"---\n\n## ✅ Corrigé\n\n[Réponses détaillées avec barème pour chaque question]" if wants_answers else ""}

═══════════════════════════════════════════════════════════════════════════════
"""

        # ============================================================================
        # CONCRETE EXAMPLES BY QUESTION TYPE
        # ============================================================================
        
        examples_section = """

═══════════════════════════════════════════════════════════════════════════════
💡 EXEMPLES CONCRETS PAR TYPE DE QUESTION
═══════════════════════════════════════════════════════════════════════════════

**QCM (Choix Multiple)**
Question: Le cœur est un organe du système:
☐ a) Digestif  
☐ b) Respiratoire  
☐ c) Circulatoire ✓  
☐ d) Nerveux

*Astuce: 3-4 options, une seule correcte, distracteurs plausibles*

---

**Vrai / Faux avec justification**
1. Les plantes respirent uniquement la nuit. ☐ Vrai  ☐ Faux
   Justifie ta réponse: _________________________________________________

*Astuce: Ajoute justification pour éviter le hasard*

---

**Compléter les blancs**
Complete la phrase avec les mots suivants: [poumons • oxygène • respiration]

La _____________ permet d'apporter de l'_____________ à notre corps grâce aux _____________.

*Astuce: Donne la banque de mots, évite ambiguïté*

---

**Question ouverte courte**
Explique en 2-3 phrases pourquoi nous devons boire de l'eau chaque jour.

__________________________________________________________________
__________________________________________________________________
__________________________________________________________________

*Astuce: Indique longueur attendue et nombre de lignes*

---

**Problème avec étapes**
Un rectangle a une longueur de 8 cm et une largeur de 5 cm.

a) Calcule son périmètre. (1.5 pt)
   Calcul: _________________________________
   Réponse: ____________ cm

b) Calcule son aire. (1.5 pt)
   Calcul: _________________________________
   Réponse: ____________ cm²

*Astuce: Divise en sous-questions, demande calculs + réponse*

---

**Schéma à compléter/légender**
[Indiquer: dessiner ou fournir schéma à compléter]

Légende le schéma du système solaire en plaçant: Soleil, Terre, Lune

[Si tu génères un schéma textuel simple:]
```
    ( Soleil )
         |
    ( _____ )  ← Terre
         |
    ( _____ )  ← Lune
```

*Astuce: Schémas simples en texte ASCII ou description claire*

═══════════════════════════════════════════════════════════════════════════════
"""

        # ============================================================================
        # TONE BLOCK FROM FICHE EXAMPLES
        # ============================================================================
        
        tone_block = f"""

═══════════════════════════════════════════════════════════════════════════════
🎨 EXEMPLES DE STYLE À IMITER (ton direct et pratique)
═══════════════════════════════════════════════════════════════════════════════

{examples_block if examples_block else "Adopte un ton clair, direct, bienveillant et professionnel."}

═══════════════════════════════════════════════════════════════════════════════
"""

        # ============================================================================
        # FINAL PROMPT ASSEMBLY
        # ============================================================================
        
        extra_guidance = (self.extra_instructions or "").strip()
        extra_block = f"\n\n🎯 INSTRUCTIONS SPÉCIALES DE L'ENSEIGNANT:\n{extra_guidance}\n" if extra_guidance else ""
        
        # Extract metadata
        school_name = self.eval_metadata.get("school_name", "Groupe Scolaire")
        academic_year = self.eval_metadata.get("academic_year", "2025/2026")
        eval_number = self.eval_metadata.get("eval_number", 1)
        semester = self.eval_metadata.get("semester", "1")
        max_score = self.eval_metadata.get("max_score", 10)
        
        # Generate session label
        num_word = "1er" if eval_number == 1 else f"{eval_number}e"
        sem_word = "1er" if semester == "1" else f"{semester}e"
        session_label = f"{num_word} contrôle du {sem_word} semestre"
        
        prompt = f"""Tu es un expert en pédagogie française spécialisé dans l'évaluation scolaire au primaire.

Tu dois créer une ÉVALUATION au format EXACT des écoles marocaines françaises pour:
- **Niveau**: {self.class_level.upper()}
- **Matière**: {self.subject or "Sciences"}
- **Sujets**: {topics_text}
- **Durée**: {self.duration} minutes
- **Barème total**: {max_score} points

═══════════════════════════════════════════════════════════════════════════════
📋 FORMAT REQUIS (ABSOLUMENT RESPECTER)
═══════════════════════════════════════════════════════════════════════════════

**ENTÊTE** (fournis exactement ces informations dans le JSON):
- Nom de l'école: "{school_name}"
- Année scolaire: "{academic_year}"
- Session: "{session_label}"
- Durée: {self.duration} min
- Note: ___ / {max_score}

**EXERCICES** (format numéroté strict):

Exercice 1 — [Consigne complète de l'exercice] : (Xpts)

[Questions ou contenu de l'exercice avec espaces de réponse clairs]

---

Exercice 2 — [Consigne] : (Xpts)

[Contenu...]

---

═══════════════════════════════════════════════════════════════════════════════
🎯 TYPES D'EXERCICES RECOMMANDÉS (varier obligatoirement)
═══════════════════════════════════════════════════════════════════════════════

1. **Tableaux à compléter** (ex: classer des maladies contagieuses/non contagieuses)
   Format markdown:
   | Catégorie A | Catégorie B |
   | ----------- | ----------- |
   | __________ | __________ |
   
2. **Relier/Matching** (ex: relier mots et définitions)
   Format:
   - **Mot 1** — __________
   - **Mot 2** — __________
   
3. **Compléter un texte** avec banque de mots
   Format:
   Mots à utiliser: **mot1, mot2, mot3**
   
   Texte: "Pour rester en bonne santé, il faut avoir une bonne __________ (hygiène)..."
   
4. **Questions courtes / production**
   Format:
   Imagine un menu pour une journée:
   - Petit-déjeuner: __________
   - Déjeuner: __________

═══════════════════════════════════════════════════════════════════════════════
🎓 DIRECTIVES PÉDAGOGIQUES
═══════════════════════════════════════════════════════════════════════════════

{early_grade_note}
{content_section}
{quality_criteria}

**BARÈME**: Répartis les points équitablement entre 3-5 exercices pour un total de 10 pts (ou 20 si demandé).

**CLARTÉ**: Chaque exercice doit avoir:
- Un numéro (Exercice 1, 2, 3...)
- Une consigne complète et claire
- Le nombre de points entre parenthèses: (2pts), (3pts), etc.

**PROGRESSION**: Du plus simple (connaissances) au plus complexe (application/réflexion).

{extra_block}

═══════════════════════════════════════════════════════════════════════════════
📤 SORTIE JSON REQUISE
═══════════════════════════════════════════════════════════════════════════════

Réponds UNIQUEMENT avec du JSON valide (AUCUN texte avant/après, AUCUN bloc de code):

{{
  "school_name": "{school_name}",
  "header": {{
    "class_level": "{self.class_level.upper()}",
    "academic_year": "{academic_year}",
    "evaluation_number": {eval_number},
    "semester": "{semester}",
    "session_label": "{session_label}",
    "duration_minutes": {self.duration},
    "max_score": {max_score},
    "subject": "{self.subject or 'Sciences'}"
  }},
  "exercises": [
    {{
      "title": "Exercice 1",
      "instructions": "Classe les maladies suivantes dans le tableau :",
      "points": 3,
      "questions": [
        {{
          "prompt": "| Maladies contagieuses | Maladies non contagieuses |\\n| --------------------- | ------------------------- |\\n| __________ | __________ |",
          "answer_type": "tableau",
          "expected_answer": "Contagieuses: rougeole, rhume, covid-19. Non contagieuses: asthme, diabète, cancer"
        }}
      ]
    }},
    {{
      "title": "Exercice 2",
      "instructions": "Relie chaque mot à sa définition :",
      "points": 2,
      "questions": [
        {{
          "prompt": "- **Vaccin** — __________\\n- **Maladie** — __________",
          "answer_type": "matching",
          "expected_answer": "Vaccin: produit qui protège. Maladie: dysfonctionnement du corps"
        }}
      ]
    }}
  ],
  "answer_key": [
    "Exercice 1: Contagieuses: rougeole, rhume, covid-19. Non contagieuses: asthme, diabète, cancer",
    "Exercice 2: Vaccin = produit protecteur, Maladie = dysfonctionnement"
  ]
}}

IMPORTANT: 
- Génère 3-5 exercices variés et adaptés au niveau {self.class_level.upper()} sur les sujets: {topics_text}
- Le total des points DOIT être exactement {max_score} points
- Utilise EXACTEMENT les valeurs d'entête fournies ci-dessus
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
            
            if guide_path:
                cached_toc = get_cached_toc(guide_path, self.guides_dir)
                if cached_toc:
                    page_offset = detect_page_offset(guide_path, queue)
                    page_range = find_pages_from_cached_toc(cached_toc, self.topic, queue, page_offset)
                    
                    if page_range:
                        page_numbers = parse_page_numbers(page_range, queue)
                        if page_numbers:
                            extracted_text = extract_lesson_text(guide_path, page_numbers, queue, self.cancel_event)
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
                        textbook_text = extract_lesson_text(textbook_path, page_numbers, queue, self.cancel_event)
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
                "quiz-generation"
            )
            
            queue.put(("progress", 90))
            
            if self.cancel_event.is_set():
                return
            
            if response:
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
        """Build the prompt for quiz generation."""
        
        # Determine question format instructions
        format_instructions = {
            "Mixed (MCQ + Short Answer)": "Mélange de QCM (avec 4 options) et de questions à réponse courte",
            "Multiple Choice Only": "Uniquement des QCM avec 4 options (A, B, C, D)",
            "Short Answer Only": "Uniquement des questions à réponse courte (1-2 phrases)",
            "True/False + MCQ": "Questions Vrai/Faux et QCM",
            "Fill in the Blanks": "Texte à trous avec espaces à compléter"
        }.get(self.quiz_format, "Questions variées")
        
        difficulty_map = {
            "Adapted to class level": f"Adapté au niveau {self.class_level}",
            "Easy": "Facile - questions de compréhension basique",
            "Medium": "Moyen - questions de compréhension et application",
            "Hard": "Difficile - questions d'analyse et réflexion"
        }
        difficulty_text = difficulty_map.get(self.difficulty, self.difficulty)
        
        content_section = ""
        if extracted_content:
            content_section = f"""
## Contenu source du manuel:
{extracted_content[:4000]}  
"""
        
        answer_section = ""
        if self.include_answers:
            answer_section = """
## Corrigé
À la fin, fournis un corrigé clair avec toutes les réponses correctes.
"""
        
        extra = ""
        if self.extra_instructions:
            extra = f"\n\nInstructions supplémentaires: {self.extra_instructions}"
        
        prompt = f"""Tu es un enseignant expérimenté. Crée un quiz pédagogique pour une classe de {self.class_level}.

# Informations du quiz
- **Sujet**: {self.topic}
- **Matière**: {self.subject or "Non spécifiée"}
- **Nombre de questions**: {self.num_questions}
- **Format**: {format_instructions}
- **Difficulté**: {difficulty_text}
- **Durée**: {self.duration} minutes

{content_section}

# Format de sortie (Markdown)

## Quiz: {self.topic}
**Classe**: {self.class_level} | **Durée**: {self.duration} min

### Questions

(Numérote chaque question de 1 à {self.num_questions})
(Pour les QCM, utilise A, B, C, D)
(Pour Vrai/Faux, indique clairement les options)

{answer_section}

# Consignes importantes
1. Les questions doivent être claires et adaptées au niveau {self.class_level}
2. Varie les types de questions si le format le permet
3. Assure-toi que les questions testent la compréhension du sujet
4. Les QCM doivent avoir une seule bonne réponse évidente
5. Utilise un langage simple et des exemples concrets{extra}
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

class ModelUpdateWorker(QtCore.QThread):
    """
    Background worker to check for newer Gemini models using Gemma-3-27b analysis.
    Emits signals with old and new model names for user confirmation.
    """
    # Signal: (old_pro, new_pro, old_flash, new_flash)
    models_found = QtCore.pyqtSignal(str, str, str, str)
    
    def __init__(self, current_pro: str = "", current_flash: str = ""):
        super().__init__()
        self.current_pro = current_pro
        self.current_flash = current_flash
    
    def run(self):
        try:
            # Short delay to let the app start up fully before querying network
            time.sleep(3)
            
            # Check if API key is available
            if not API_KEYS.get("GEMINI_API_KEY"):
                return

            model_names = fetch_available_models()
            if not model_names:
                return
                
            # Use Gemma to intelligently determine the best models
            new_pro, new_flash = find_best_models_with_ai(
                model_names, 
                self.current_pro, 
                self.current_flash
            )
            
            # Only emit if at least one model has an update
            if new_pro or new_flash:
                self.models_found.emit(
                    self.current_pro,
                    new_pro or "",
                    self.current_flash,
                    new_flash or ""
                )
        except Exception as e:
            print(f"Model update check failed: {e}")



