import os
import re
from typing import Optional
from docx import Document
from docx.shared import Pt, RGBColor, Cm
from docx.enum.style import WD_STYLE_TYPE
from docx.enum.text import WD_PARAGRAPH_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

from document.pdf import generate_smart_filename
from config import PDF_TEMPLATES

HAS_DOCX = True

# --- Converter Logic ---

FIELD_RE = re.compile(r"\{\{FIELD:([^|}]+)(?:\|([^}]+))?\}\}")
TABLE_START_RE = re.compile(r"\{\{TABLE:([^}]+)\}\}")
TABLE_END = "{{ENDTABLE}}"
CELL_RE = re.compile(r"\{\{CELL:([^:}]+):(\d+):(\d+)\}\}")

def parse_attrs(attr_string):
    """Parse attribute string like 'label=Nom|lines=1|value=foo'"""
    res = {}
    if not attr_string:
        return res
    parts = attr_string.split("|")
    for p in parts:
        if "=" in p:
            k, v = p.split("=", 1)
            res[k.strip()] = v.strip()
    return res

def add_underline_paragraph(doc, lines=1, value=None):
    """Add a paragraph with underline or value text"""
    if value:
        p = doc.add_paragraph(value)
        return p
    
    # Create underline using bottom border
    p = doc.add_paragraph()
    p.add_run("_" * 50)  # Fallback: underscore line
    return p

def add_heading(doc, line):
    """Add heading based on markdown level"""
    if line.startswith("# "):
        level = 0
        text = line.lstrip("# ").strip()
    elif line.startswith("## "):
        level = 1
        text = line.lstrip("## ").strip()
    else:
        level = 2
        text = line.lstrip("### ").strip()
    
    h = doc.add_heading(text, level=level)
    return h

def add_bullet(doc, text):
    """Add bullet point"""
    p = doc.add_paragraph(text.strip(), style='List Bullet')
    return p

def process_markdown_formatting(paragraph, text):
    """Process inline markdown (bold, italic) and add to paragraph"""
    # Split by bold and italic markers
    parts = re.split(r'(\*\*[^*]+\*\*|\*[^*]+\*)', text)
    
    for part in parts:
        if part.startswith('**') and part.endswith('**'):
            # Bold
            run = paragraph.add_run(part[2:-2])
            run.bold = True
        elif part.startswith('*') and part.endswith('*'):
            # Italic
            run = paragraph.add_run(part[1:-1])
            run.italic = True
        else:
            # Regular text
            paragraph.add_run(part)

def convert_markdown_to_docx(md_text, output_path):
    """
    Convert evaluation markdown with field markers to DOCX.
    
    Args:
        md_text: Markdown text with {{FIELD:...}} and {{TABLE:...}} markers
        output_path: Path to save the DOCX file
    """
    doc = Document()
    
    # Set default font
    style = doc.styles['Normal']
    style.font.name = 'Calibri'
    style.font.size = Pt(11)
    
    lines = md_text.splitlines()
    i = 0
    n = len(lines)
    
    while i < n:
        line = lines[i].rstrip()
        
        # Skip YAML frontmatter
        if i == 0 and line.strip() == '---':
            i += 1
            while i < n and lines[i].strip() != '---':
                i += 1
            i += 1
            continue
        
        # Headings
        if line.startswith("#"):
            add_heading(doc, line)
            i += 1
            continue
        
        # Table start
        if line.strip().startswith("{{TABLE:"):
            m = TABLE_START_RE.match(line.strip())
            if not m:
                i += 1
                continue
            
            table_info = m.group(1)
            attrs = parse_attrs(table_info)
            cols = int(attrs.get("cols", "2"))
            rows = int(attrs.get("rows", "1"))
            
            # Collect cell markers and content
            cell_text = {}
            j = i + 1
            while j < n and lines[j].strip() != TABLE_END:
                # Find cell markers
                for cm in CELL_RE.finditer(lines[j]):
                    tabid, r, c = cm.group(1), int(cm.group(2)), int(cm.group(3))
                    after = lines[j][cm.end():].strip()
                    cell_text[(r, c)] = after
                j += 1
            
            # Create DOCX table
            table = doc.add_table(rows=rows + 1, cols=cols)
            table.style = 'Light Grid Accent 1'
            
            # Find header row from markdown table syntax
            k = i + 1
            header_line = None
            while k < j:
                if '|' in lines[k] and not lines[k].strip().startswith('{{'):
                    header_line = lines[k]
                    break
                k += 1
            
            if header_line:
                # Parse header
                parts = [p.strip() for p in header_line.strip().strip("|").split("|")]
                for ci, text in enumerate(parts):
                    if ci < cols:
                        table.cell(0, ci).text = text
            
            # Fill cell placeholders
            for r in range(1, rows + 1):
                for c in range(1, cols + 1):
                    text = cell_text.get((r, c), "")
                    if r <= rows and c <= cols:
                        table.cell(r, c - 1).text = text
            
            i = j + 1
            continue
        
        # Field markers inline
        fm = FIELD_RE.search(line)
        if fm:
            before = line[:fm.start()].strip()
            fid = fm.group(1)
            attrs = parse_attrs(fm.group(2))
            lines_count = int(attrs.get("lines", "1"))
            value = attrs.get("value", None)
            
            p = doc.add_paragraph()
            if before:
                process_markdown_formatting(p, before + " ")
            
            # Insert underline or value
            if value:
                p.add_run(value)
            else:
                p.add_run("_" * 50)
            
            # Add remaining text
            after = line[fm.end():].strip()
            if after:
                p.add_run(" " + after)
            
            i += 1
            continue
        
        # Bullet points
        if line.strip().startswith("- "):
            add_bullet(doc, line.strip()[2:])
            i += 1
            continue
        
        # Horizontal rule
        if line.strip() == "---":
            doc.add_paragraph()  # Just add spacing
            i += 1
            continue
        
        # Empty line
        if line.strip() == "":
            doc.add_paragraph()
            i += 1
            continue
        
        # Regular paragraph
        p = doc.add_paragraph()
        process_markdown_formatting(p, line)
        i += 1
    
    # Save document
    doc.save(output_path)
    return output_path

# --- Export Functions ---

def save_fiche_to_docx(content, lesson_topic, class_level, output_dir, queue, template_name: Optional[str] = "Normal"):
    if not HAS_DOCX:
        queue.put(("log", "❌ DOCX export requires python-docx. Run: pip install python-docx"))
        return None

    os.makedirs(output_dir, exist_ok=True)
    
    # Create smart filename that doesn't overwrite
    filename = generate_smart_filename("Fiche", lesson_topic, class_level, output_dir, "docx")
    full_path = os.path.join(output_dir, filename)

    try:
        doc = Document()
        template_key = template_name if template_name is not None else "Normal"
        template = PDF_TEMPLATES.get(template_key, PDF_TEMPLATES.get("Normal", {}))

        # Build a Word-safe palette from the selected template
        def _hex_to_rgb(hex_color, fallback):
            try:
                value = (hex_color or "").strip().lstrip('#')
                if len(value) == 6:
                    return RGBColor(int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16))
            except Exception:
                pass
            return fallback

        def _hex_six(hex_color, fallback_hex):
            value = (hex_color or "").strip().lstrip('#')
            if len(value) == 6:
                return value.upper()
            return fallback_hex

        title_rgb = _hex_to_rgb(template.get("title_color", "#244A6E"), RGBColor(36, 74, 110))
        heading_rgb = _hex_to_rgb(template.get("heading_color", "#3B627D"), RGBColor(59, 98, 125))
        accent_rgb = _hex_to_rgb(template.get("accent_color", "#1F4B78"), RGBColor(31, 75, 120))
        title_fill = _hex_six(template.get("title_color", "#244A6E"), "244A6E")
        heading_fill = _hex_six(template.get("heading_color", "#3B627D"), "3B627D")

        section = doc.sections[0]
        section.top_margin = Cm(2.2)
        section.bottom_margin = Cm(2.2)
        section.left_margin = Cm(2.2)
        section.right_margin = Cm(2.2)

        base_style = doc.styles['Normal']
        base_style.font.name = 'Calibri'
        base_style.font.size = Pt(11)

        def ensure_paragraph_style(name, base='Normal', size=11, bold=False, color=None, alignment=None, space_before=0, space_after=0):
            if name in doc.styles:
                st = doc.styles[name]
            else:
                st = doc.styles.add_style(name, WD_STYLE_TYPE.PARAGRAPH)
            st.base_style = doc.styles[base]
            st.font.name = 'Calibri'
            st.font.size = Pt(size)
            st.font.bold = bold
            if color is not None:
                st.font.color.rgb = color
            st.paragraph_format.space_before = Pt(space_before)
            st.paragraph_format.space_after = Pt(space_after)
            if alignment is not None:
                st.paragraph_format.alignment = alignment
            return st

        ensure_paragraph_style('FicheTitle', size=20, bold=True, alignment=WD_PARAGRAPH_ALIGNMENT.CENTER, space_before=0, space_after=0)
        ensure_paragraph_style('FicheSummary', size=10, bold=True, color=accent_rgb, alignment=WD_PARAGRAPH_ALIGNMENT.CENTER, space_before=4, space_after=6)
        ensure_paragraph_style('FicheSection', size=13, bold=True, color=RGBColor(255, 255, 255), space_before=0, space_after=0)
        ensure_paragraph_style('FicheSubSection', size=12, bold=True, color=heading_rgb, space_before=6, space_after=3)
        ensure_paragraph_style('FicheBody', size=11, space_before=0, space_after=4)

        lines = [line.rstrip() for line in content.splitlines()]
        metadata_re = re.compile(r'^###\s+\*\*([^*]+)\*\*\s*:\s*(.+)$')
        metadata_bullet_re = re.compile(r'^-\s+\*\*([^*]+)\*\*\s*:\s*(.+)$')

        title = f"Fiche pédagogique - {lesson_topic}"
        summary = ""
        metadata = []
        content_lines = []

        for line in lines:
            stripped = line.strip()
            if not stripped:
                content_lines.append("")
                continue
            if stripped.startswith('# '):
                title = stripped[2:].strip() or title
                continue
            if stripped.startswith('> ') and not summary:
                summary = stripped[2:].strip()
                continue
            meta_match = metadata_re.match(stripped)
            if meta_match:
                metadata.append((meta_match.group(1).strip(), meta_match.group(2).strip()))
                continue
            bullet_meta_match = metadata_bullet_re.match(stripped)
            if bullet_meta_match:
                metadata.append((bullet_meta_match.group(1).strip(), bullet_meta_match.group(2).strip()))
                continue
            content_lines.append(stripped)

        header_table = doc.add_table(rows=1, cols=1)
        header_table.autofit = True
        header_cell = header_table.cell(0, 0)
        header_para = header_cell.paragraphs[0]
        header_para.style = 'FicheTitle'
        process_markdown_formatting(header_para, title)
        header_shading = OxmlElement('w:shd')
        header_shading.set(qn('w:fill'), title_fill)
        header_cell._tc.get_or_add_tcPr().append(header_shading)
        header_cell.width = Cm(16)

        if summary:
            p = doc.add_paragraph(style='FicheSummary')
            process_markdown_formatting(p, summary)

        if not metadata:
            metadata = [
                ("Titre de la leçon", lesson_topic),
                ("Classe", class_level.upper()),
            ]

        meta_table = doc.add_table(rows=len(metadata), cols=2)
        meta_table.style = 'Light Grid Accent 1'
        for idx, (label, value) in enumerate(metadata):
            left = meta_table.cell(idx, 0)
            right = meta_table.cell(idx, 1)
            lp = left.paragraphs[0]
            rp = right.paragraphs[0]
            lp.style = 'FicheBody'
            rp.style = 'FicheBody'
            left_run = lp.add_run(f"{label}:")
            left_run.bold = True
            left_run.font.color.rgb = accent_rgb
            process_markdown_formatting(rp, value)

        doc.add_paragraph()

        for line in content_lines:
            if not line:
                doc.add_paragraph()
                continue

            if line.startswith('## '):
                banner = doc.add_table(rows=1, cols=1)
                banner.style = 'Table Grid'
                cell = banner.cell(0, 0)
                p = cell.paragraphs[0]
                p.style = 'FicheSection'
                process_markdown_formatting(p, line[3:].strip())
                shading = OxmlElement('w:shd')
                shading.set(qn('w:fill'), heading_fill)
                cell._tc.get_or_add_tcPr().append(shading)
                continue

            if line.startswith('### '):
                p = doc.add_paragraph(style='FicheSubSection')
                process_markdown_formatting(p, line[4:].strip())
                continue

            if line.startswith('- ') or line.startswith('* '):
                p = doc.add_paragraph(style='List Bullet')
                process_markdown_formatting(p, line[2:].strip())
                continue

            if re.match(r'^\d+\.\s+', line):
                text = re.sub(r'^\d+\.\s+', '', line)
                p = doc.add_paragraph(style='List Number')
                process_markdown_formatting(p, text)
                continue

            if line.startswith('> '):
                p = doc.add_paragraph(style='FicheBody')
                run = p.add_run(line[2:].strip())
                run.italic = True
                continue

            p = doc.add_paragraph(style='FicheBody')
            process_markdown_formatting(p, line)

        doc.save(full_path)
        queue.put(("log", f"💾 DOCX saved: {full_path}"))
        return full_path
    except Exception as e:
        queue.put(("log", f"❌ DOCX Save Error: {e}"))
        import traceback
        queue.put(("log", f"Traceback: {traceback.format_exc()}"))
        return None

def save_evaluation_to_docx(content, topics_list, class_level, output_dir, queue):
    """
    Save evaluation content as DOCX using the special converter.
    This preserves {{FIELD:...}}, {{TABLE:...}}, and {{CELL:...}} markers
    for editable evaluations in Word.
    """
    if not HAS_DOCX:
        queue.put(("log", "❌ DOCX export requires python-docx. Run: pip install python-docx"))
        return None

    os.makedirs(output_dir, exist_ok=True)
    
    # Create smart filename for evaluation
    topics_text = "_".join(topics_list[:2])  # Use first 2 topics
    if len(topics_list) > 2:
        topics_text += "_etc"
    filename = generate_smart_filename("Eval", topics_text, class_level, output_dir, "docx")
    full_path = os.path.join(output_dir, filename)

    try:
        # Use the special converter that handles field markers
        convert_markdown_to_docx(content, full_path)
        queue.put(("log", f"💾 Evaluation DOCX saved: {full_path}"))
        queue.put(("log", "ℹ️  Field markers ({{FIELD:...}}, {{TABLE:...}}) preserved for editing in Word"))
        return full_path
    except Exception as e:
        queue.put(("log", f"❌ Evaluation DOCX Save Error: {e}"))
        import traceback
        queue.put(("log", f"Traceback: {traceback.format_exc()}"))
        return None
