import os
import json
import re
from typing import List, Dict, Any, Optional
import pdfplumber

from src.config import (
    TOC_CACHE_DIR, TABLE_OF_CONTENTS_PAGES, API_KEYS,
    get_configured_toc_prompt, get_configured_gemini_toc_model,
    get_configured_gemini_offset_model, get_configured_gemma_syntax_model,
    get_configured_page_finding_prompt, GEMINI_TOC_MODEL
)
from src.core.ai import _generate_with_model

def get_cached_toc(guide_path: str, guides_dir: Optional[str] = None) -> Optional[List[Dict[str, Any]]]:
    """
    Load parsed ToC from JSON cache if available and valid.
    
    Args:
        guide_path: Path to the PDF guide file
        guides_dir: Optional guides directory for cache location
    
    Returns:
        List of ToC entries or None if cache miss/invalid
    """
    if not os.path.exists(guide_path):
        return None
    
    # Determine cache directory
    cache_dir = os.path.join(guides_dir, "toc_cache") if guides_dir else TOC_CACHE_DIR
    
    try:
        os.makedirs(cache_dir, exist_ok=True)
    except (OSError, PermissionError):
        return None
    
    cache_file = os.path.join(cache_dir, os.path.basename(guide_path) + ".json")
    
    if not os.path.exists(cache_file):
        return None
    
    # Check if cache is newer than source PDF (optional staleness check)
    try:
        cache_mtime = os.path.getmtime(cache_file)
        pdf_mtime = os.path.getmtime(guide_path)
        if cache_mtime < pdf_mtime:
            # Cache is stale, remove it
            try:
                os.remove(cache_file)
            except (OSError, PermissionError):
                pass
            return None
    except OSError:
        pass
    
    try:
        with open(cache_file, "r", encoding="utf-8") as f:
            data = json.load(f)
            # Validate structure
            if isinstance(data, list) and all(isinstance(item, dict) for item in data):
                return data
            return None
    except (json.JSONDecodeError, IOError, UnicodeDecodeError):
        # Corrupted cache, try to remove it
        try:
            os.remove(cache_file)
        except (OSError, PermissionError):
            pass
        return None

def save_toc_to_cache(guide_path: str, toc_data: List[Dict[str, Any]], guides_dir: Optional[str] = None) -> bool:
    """
    Save parsed ToC to JSON cache with atomic write.
    
    Args:
        guide_path: Path to the PDF guide file
        toc_data: List of ToC entries to cache
        guides_dir: Optional guides directory for cache location
    
    Returns:
        True if save succeeded, False otherwise
    """
    if not toc_data or not isinstance(toc_data, list):
        return False
    
    cache_dir = os.path.join(guides_dir, "toc_cache") if guides_dir else TOC_CACHE_DIR
    
    try:
        os.makedirs(cache_dir, exist_ok=True)
    except (OSError, PermissionError):
        return False
    
    cache_file = os.path.join(cache_dir, os.path.basename(guide_path) + ".json")
    temp_file = f"{cache_file}.tmp"
    
    try:
        # Atomic write via temp file
        with open(temp_file, "w", encoding="utf-8") as f:
            json.dump(toc_data, f, ensure_ascii=False, indent=2)
        os.replace(temp_file, cache_file)
        return True
    except (IOError, OSError, PermissionError):
        # Clean up temp file on failure
        try:
            if os.path.exists(temp_file):
                os.remove(temp_file)
        except (OSError, PermissionError):
            pass
        return False

def parse_full_toc_with_ai(toc_text: str, queue):
    """Uses an AI model to parse a raw ToC string into a structured list of topics and pages."""
    queue.put(("log", "🧠 No cache found. Parsing full ToC with AI... (one-time operation per guide)"))
    
    # Get the configured prompt template
    prompt_template = get_configured_toc_prompt()
    prompt = prompt_template.format(toc_text=toc_text)

    try:
        if not API_KEYS.get("GEMINI_API_KEY"):
            queue.put(("log", "❌ Gemini API key needed for ToC parsing. Please add it to keys.txt or .env"))
            return None

        response = _generate_with_model(
            get_configured_gemini_toc_model(),
            prompt,
            temperature=0.0,
        )
        resp = (response.text or "") if response else ""
        
        # Clean the response to ensure it's valid JSON
        json_str = resp.strip()
        if json_str.startswith("```json"):
            json_str = json_str[7:]
        if json_str.endswith("```"):
            json_str = json_str[:-3]
        
        parsed_json = json.loads(json_str)
        if isinstance(parsed_json, list):
            queue.put(("log", f"✅ AI parsed {len(parsed_json)} ToC entries."))
            return parsed_json
        return None
    except Exception as e:
        queue.put(("log", f"❌ AI ToC Parsing Error: {e}"))
        return None

def detect_page_offset(pdf_path: str, queue) -> int:
    """
    Detect an offset between logical page numbers (printed on pages) and physical PDF indices.
    Smarter heuristic: scan header/footer text on several pages after the ToC and look for
    explicit page numbers (e.g., "Page 5", "p. 5", roman numerals, or bare numbers).
    Compute the consensus offset = physical_index - printed_number.

    Falls back to a lightweight AI check only if the heuristic yields no signal.
    """
    def roman_to_int(s: str) -> int | None:
        s = s.upper()
        romans = {"I":1, "V":5, "X":10, "L":50, "C":100, "D":500, "M":1000}
        total = 0
        prev = 0
        for ch in reversed(s):
            if ch not in romans: return None
            val = romans[ch]
            if val < prev:
                total -= val
            else:
                total += val
                prev = val
        return total if total > 0 else None

    try:
        with pdfplumber.open(pdf_path) as pdf:
            n = len(pdf.pages)
            if n == 0:
                return 0

            start_idx = min(TABLE_OF_CONTENTS_PAGES, n - 1)
            end_idx = min(start_idx + 15, n)  # inspect up to 15 pages after ToC

            import re
            delta_counts: dict[int, int] = {}
            hits = 0

            for i in range(start_idx, end_idx):
                try:
                    page = pdf.pages[i]
                    h = page.height
                    w = page.width
                    # Focus on header (top 12%) and footer (bottom 12%) regions
                    header = page.within_bbox((0, 0, w, h * 0.12)).extract_text() or ""
                    footer = page.within_bbox((0, h * 0.88, w, h)).extract_text() or ""
                    text = f"{header}\n{footer}".strip()
                    if not text:
                        # fallback to full page if nothing in header/footer
                        text = page.extract_text() or ""
                    if not text:
                        continue

                    physical = i + 1  # 1-based physical index

                    # Prefer explicit formats first
                    candidates: list[int] = []

                    for m in re.finditer(r"(?i)\b(?:page|p\.?|pag\.)\s*([0-9]{1,4})\b", text):
                        try:
                            candidates.append(int(m.group(1)))
                        except Exception:
                            pass

                    # Roman numerals in explicit formats
                    for m in re.finditer(r"(?i)\b(?:page|p\.?|pag\.)\s*([ivxlcdm]{1,7})\b", text):
                        val = roman_to_int(m.group(1))
                        if val:
                            candidates.append(val)

                    # Bare numbers on isolated lines (short)
                    lines = [ln.strip() for ln in text.splitlines() if ln.strip()]
                    for ln in lines:
                        # very short line likely to be a page number
                        if 1 <= len(ln) <= 4:
                            if re.fullmatch(r"[0-9]{1,4}", ln):
                                try:
                                    candidates.append(int(ln))
                                except Exception:
                                    pass
                            elif re.fullmatch(r"(?i)[ivxlcdm]{1,7}", ln):
                                val = roman_to_int(ln)
                                if val:
                                    candidates.append(val)

                    # Filter and compute deltas
                    for printed in candidates:
                        if not (0 < printed < 2000):
                            continue
                        delta = physical - printed
                        if -50 <= delta <= 200:  # sanity bounds
                            delta_counts[delta] = delta_counts.get(delta, 0) + 1
                            hits += 1
                except Exception:
                    continue

            if hits:
                # Choose the mode delta; break ties preferring smaller |delta|
                best_delta = None
                best_count = -1
                for d, c in delta_counts.items():
                    if c > best_count or (c == best_count and best_delta is not None and abs(d) < abs(best_delta)):
                        best_delta = d
                        best_count = c

                if best_delta is not None:
                    queue.put(("log", f"📄 Detected page offset by heuristic: {best_delta:+d} (from {hits} hits)"))
                    return int(best_delta)

            # Heuristic failed: optional AI fallback if available
            if not API_KEYS.get("GEMINI_API_KEY"):
                queue.put(("log", "⚠️ No clear page labels found; assuming no offset."))
                return 0

            # Build a compact prompt from a few pages
            sample_pages = []
            for j in range(start_idx, min(start_idx + 5, n)):
                pg = pdf.pages[j]
                txt = (pg.extract_text() or "").strip()
                if txt:
                    sample_pages.append({"pdf_page_number": j + 1, "text": txt[:800]})

            if not sample_pages:
                queue.put(("log", "⚠️ No content pages found for offset detection. Assuming no offset."))
                return 0

            pages_text = ""
            for info in sample_pages:
                pages_text += f"\n--- PDF Page {info['pdf_page_number']} ---\n{info['text']}\n"

            prompt = f"""You are a page numbering expert. Detect the offset between logical page numbers and physical PDF page positions.

The offset = PDF page number (position in file) - Logical page number (printed on page).

Here are sample pages (text content may include headers/footers with page numbers):
{pages_text}

Return ONLY the integer offset. If unsure, return 0."""

            try:
                response = _generate_with_model(
                    get_configured_gemini_offset_model(),
                    prompt,
                    temperature=0.0,
                )
                resp = (response.text or "") if response else ""
            except Exception:
                queue.put(("log", "⚠️ Failed to create Gemini model for offset detection. Assuming no offset."))
                return 0
            try:
                offset = int(resp.strip())
                if -50 <= offset <= 200:
                    queue.put(("log", f"📄 AI detected page offset: {offset:+d}"))
                    return offset
                else:
                    queue.put(("log", f"⚠️ AI returned unreasonable offset ({offset}). Assuming no offset."))
                    return 0
            except Exception:
                queue.put(("log", f"⚠️ AI returned non-numeric offset: '{resp}'. Assuming no offset."))
                return 0
    except Exception as e:
        queue.put(("log", f"⚠️ Error detecting page offset: {e}. Assuming no offset."))
        return 0

def find_pages_from_cached_toc(cached_toc: list, lesson_topic: str, queue, page_offset: int = 0) -> str | None:
    """Finds the page range for a topic from a structured/cached ToC without an AI call.
    Applies the detected page_offset to convert logical (printed) numbers to physical PDF indices.
    Handles weird resets by falling back to a short fixed range.
    """
    # Normalize topic for better matching
    normalized_topic = lesson_topic.lower().strip()
    
    # Find the entry for the lesson - try exact/substring match first
    found_entry = None
    found_index = -1
    for i, entry in enumerate(cached_toc):
        toc_topic = str(entry.get("topic", "")).lower()
        if normalized_topic in toc_topic or toc_topic in normalized_topic:
            found_entry = entry
            found_index = i
            break
    
    # If no exact/substring match found, try fuzzy matching with key words
    if not found_entry and len(cached_toc) > 0:
        # Extract key words from the lesson topic (words > 3 chars)
        query_words = set(w for w in normalized_topic.split() if len(w) > 3)
        
        if query_words:
            # Find the best match by counting matching words
            best_match = None
            best_match_count = 0
            best_index = -1
            
            for i, entry in enumerate(cached_toc):
                toc_topic = str(entry.get("topic", "")).lower()
                toc_words = set(w for w in toc_topic.split() if len(w) > 3)
                
                # Count matching words
                matching = len(query_words & toc_words)
                if matching > best_match_count:
                    best_match_count = matching
                    best_match = entry
                    best_index = i
            
            if best_match_count > 0:  # At least one word matched
                found_entry = best_match
                found_index = best_index
                queue.put(("log", f"ℹ️ Fuzzy matched '{lesson_topic}' with ToC entry '{found_entry.get('topic', '')}'"))
    
    if not found_entry:
        queue.put(("log", f"❌ Could not find '{lesson_topic}' in the cached ToC."))
        return None
    logical_start = found_entry.get("page")
    if not isinstance(logical_start, int):
        queue.put(("log", f"❌ Invalid page number for '{lesson_topic}' in cache."))
        return None
    # Find the start page of the *next* lesson to determine the end page
    logical_end = None
    if found_index + 1 < len(cached_toc):
        next_entry = cached_toc[found_index + 1]
        next_page = next_entry.get("page")
        if isinstance(next_page, int) and next_page > logical_start:
            logical_end = next_page - 1
    # If it's the last item or pages are weird, assume it's 3-4 pages long
    if logical_end is None or logical_end < logical_start:
        logical_end = logical_start + 3
        queue.put(("log", f"⚠️ Could not determine end page. Assuming a range of {logical_start}-{logical_end}."))
    # Convert logical pages to physical by applying the detected offset
    pdf_start = (logical_start or 1) + (page_offset or 0)
    pdf_end_candidate = (logical_end or (logical_start + 3)) + (page_offset or 0)
    if pdf_start < 1:
        queue.put(("log", f"⚠️ Computed start {pdf_start} < 1 after offset. Clamping to 1."))
        pdf_start = 1
    if pdf_end_candidate <= pdf_start:
        # Non-monotonic or weird labels; assume a short 3-page span
        pdf_end = pdf_start + 3
        queue.put(("log", f"⚠️ ToC pages non-monotonic around '{lesson_topic}'. Assuming {pdf_start}-{pdf_end}."))
    else:
        pdf_end = pdf_end_candidate
    if pdf_end < pdf_start:
        pdf_end = pdf_start
    page_range = f"{pdf_start}-{pdf_end}"
    if page_offset:
        queue.put(("log", f"ℹ️ Using ToC with offset {page_offset:+d}: PDF pages {pdf_start}-{pdf_end} for '{lesson_topic}'."))
    else:
        queue.put(("log", f"ℹ️ Using ToC (no offset): PDF pages {pdf_start}-{pdf_end} for '{lesson_topic}'."))
    return page_range

def correct_lesson_topic_syntax(lesson_topic: str, queue, toc_json: str | None = None) -> str:
    """Uses Gemma to correct spelling, grammar, capitalization, and punctuation in lesson topics."""
    try:
        if not API_KEYS.get("GEMINI_API_KEY"):
            queue.put(("log", "⚠️ Gemini API key needed for syntax correction. Using original topic."))
            return lesson_topic

        toc_context = ""
        if toc_json:
            toc_context = f"""
Here is the JSON table of contents from the book. The corrected topic MUST match one of the "topic" values in this JSON.
---
{toc_json}
---
"""

        prompt = f"""You are an expert French language proofreader and editor. Your task is to correct any spelling, grammar, capitalization, or punctuation errors in lesson topics for French educational materials.

INPUT: "{lesson_topic}"

Please correct any errors and return ONLY the corrected version. Keep the meaning identical, but fix:
- Spelling mistakes
- Missing or incorrect accents (é, è, à, ç, etc.)
- Capitalization (proper nouns, first letters)
- Missing or extra punctuation
- Grammar errors

If a table of contents is provided, find the closest matching topic and return it exactly as it appears in the JSON.

{toc_context}

Examples:
- "le cycle de leau" → "Le cycle de l'eau"
- "les volcans," → "Les volcans"
- "la revolution francaise" → "La Révolution française"

Return ONLY the corrected topic, nothing else."""
        response = _generate_with_model(
            get_configured_gemma_syntax_model(),
            prompt,
            temperature=0.1,
        )
        resp = (response.text or "") if response else ""
        corrected_topic = resp.strip()
        
        if corrected_topic and corrected_topic != lesson_topic:
            queue.put(("log", f"🔧 Topic corrected: '{lesson_topic}' → '{corrected_topic}'"))
            return corrected_topic
        else:
            return lesson_topic
            
    except Exception as e:
        queue.put(("log", f"⚠️ Error in syntax correction: {e}. Using original topic."))
        return lesson_topic

def extract_table_of_contents(pdf_path, queue):
    toc_text = ""
    try:
        with pdfplumber.open(pdf_path) as pdf:
            num_pages_to_scan = min(TABLE_OF_CONTENTS_PAGES, len(pdf.pages))
            for i in range(num_pages_to_scan):
                page = pdf.pages[i]
                text = page.extract_text()
                if text:
                    toc_text += text + "\n\n"
            queue.put(("log", "✅ Table of contents extracted."))
            return toc_text
    except Exception as e:
        queue.put(("log", f"❌ PDF Error: {e}"))
        return None

def get_pages_from_toc(toc_text, lesson_topic, queue):
    queue.put(("log", f"🧠 Finding pages for '{lesson_topic}' using Gemini model..."))
    
    # Get the configured prompt template
    prompt_template = get_configured_page_finding_prompt()
    prompt = prompt_template.format(lesson_topic=lesson_topic, toc_text=toc_text)

    try:
        # Always use the Gemini TOC model for page finding regardless of user selection
        if not API_KEYS.get("GEMINI_API_KEY"):
            queue.put(("log", "❌ Gemini API key needed for page-finding. Please add it to keys.txt or .env"))
            return None

        response = _generate_with_model(
            get_configured_gemini_toc_model(),
            prompt,
            temperature=0.1,
        )
        resp = (response.text or "") if response else ""
        
        if resp and resp.strip():
            queue.put(("log", f"✅ page-finding: used Gemini {GEMINI_TOC_MODEL}"))
            queue.put(("log", f"🤖 Response: '{resp}'"))
            return resp.strip()
        else:
            queue.put(("log", f"❌ page-finding: No response from Gemini {GEMINI_TOC_MODEL}."))
            return None
    except Exception as e:
        queue.put(("log", f"❌ page-finding failed with Gemini: {e}"))
        return None

def parse_page_numbers(page_str, queue):
    pages = []
    page_str = ''.join(re.findall(r'[\d,-]', page_str or ""))
    if not page_str:
        queue.put(("log", "❌ Parser Error: Program response contained no numbers."))
        return []
    try:
        for part in page_str.split(','):
            part = part.strip()
            if not part:
                continue
            if '-' in part:
                start, end = map(int, part.split('-'))
                if start <= end:
                    pages.extend(range(start, end + 1))
            else:
                pages.append(int(part))
        queue.put(("log", f"✅ Parsed page numbers: {pages}"))
        return pages
    except ValueError:
        queue.put(("log", f"❌ Parser Error: Couldn't understand '{page_str}'."))
        return []

def find_guide_file(class_level, guides_dir, queue):
    class_level = class_level.lower()
    possible_filenames = [f"guide_pedagogique_{class_level}.pdf"]
    if class_level == "6e":
        possible_filenames.append("guide_pedagogique_6eme.pdf")
    for filename in possible_filenames:
        filepath = os.path.join(guides_dir, filename)
        if os.path.exists(filepath):
            queue.put(("log", f"✅ Found guide: {filepath}"))
            return filepath
    queue.put(("log", f"❌ Error: Could not find guide for '{class_level}' in {guides_dir}."))
    return None


def find_textbook_file(class_level, textbook_dir, queue):
    """Find student textbook PDF for the given class level."""
    if not textbook_dir or not os.path.exists(textbook_dir):
        queue.put(("log", "ℹ️ No textbook folder specified or folder doesn't exist."))
        return None
    
    class_level = class_level.lower()
    possible_filenames = [
        f"livre_{class_level}.pdf",
        f"manuel_{class_level}.pdf", 
        f"textbook_{class_level}.pdf",
        f"{class_level}.pdf"
    ]
    if class_level == "6e":
        possible_filenames.extend([
            "livre_6eme.pdf", "manuel_6eme.pdf", "textbook_6eme.pdf", "6eme.pdf"
        ])
    
    for filename in possible_filenames:
        filepath = os.path.join(textbook_dir, filename)
        if os.path.exists(filepath):
            queue.put(("log", f"✅ Found student textbook: {filepath}"))
            return filepath
    
    queue.put(("log", f"ℹ️ No textbook found for '{class_level}' in {textbook_dir}."))
    return None


def extract_lesson_text(pdf_path, page_numbers, queue, cancel_event=None):
    lesson_text = ""
    try:
        with pdfplumber.open(pdf_path) as pdf:
            for page_num in page_numbers:
                if cancel_event and cancel_event.is_set():
                    queue.put(("log", "⏹️ Cancelled during PDF extraction."))
                    return None
                if 1 <= page_num <= len(pdf.pages):
                    page = pdf.pages[page_num - 1]
                    text = page.extract_text()
                    if text:
                        lesson_text += f"\n\n--- TEXT FROM PAGE {page_num} ---\n\n{text}"
                else:
                    queue.put(("log", f"⚠️ Warning: Page {page_num} is out of bounds."))
        queue.put(("log", "✅ Lesson text extracted."))
        return lesson_text
    except Exception as e:
        queue.put(("log", f"❌ PDF Extraction Error: {e}"))
        return None
