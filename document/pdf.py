import os
import re
from datetime import datetime
from typing import Dict, List, Any, Optional

from reportlab.lib.pagesizes import A4
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY, TA_LEFT
from reportlab.lib.units import cm
from reportlab.lib import colors

from config import PDF_TEMPLATES
from utils.helpers import safe_color

def create_pdf_styles(template):
    """Create sophisticated ReportLab styles based on template configuration"""
    styles = getSampleStyleSheet()
    
    # Get template values with defaults
    title_color = template.get('title_color', '#2E8B57')
    heading_color = template.get('heading_color', '#2F4F4F')
    accent_color = template.get('accent_color', title_color)  # Use title color if not specified
    font_family = template.get('font_family', 'Helvetica')
    title_size = template.get('title_size', 20)
    heading_size = template.get('heading_size', 16)
    body_size = template.get('body_size', 12)
    meta_size = template.get('meta_size', 11)
    line_height = template.get('line_height', 18)
    
    # Main title style - dramatic and eye-catching
    title_style_name = 'TitleStyle'
    if template.get('decorative'):
        title_style_name = 'DecorativeTitleStyle'
    
    try:
        styles.add(ParagraphStyle(
            name=title_style_name,
            fontName=f'{font_family}-Bold',
            fontSize=title_size + (2 if template.get('decorative') else 0),  # Larger for decorative
            leading=(title_size + (2 if template.get('decorative') else 0)) * 1.2,  # Add leading to prevent overlap
            alignment=TA_CENTER,
            spaceAfter=30 if template.get('decorative') else 25,
            spaceBefore=20 if template.get('decorative') else 15,
            textColor=safe_color(title_color),
            borderWidth=3 if template.get('decorative') else (2 if template.get('use_borders') else 0),
            borderColor=safe_color(title_color),
            borderPadding=(15, 15, 15, 15) if template.get('decorative') else ((12, 12, 12, 12) if template.get('use_borders') else 0),
            backColor=safe_color(template.get('background_accent', '#FFF8F0' if template.get('decorative') else '#FFFFFF')) if template.get('decorative') else None
        ))
    except Exception as e:
        print(f"❌ ERROR creating title style: {e}")
        import traceback
        traceback.print_exc()
    
    # Heading styles with hierarchy
    styles.add(ParagraphStyle(
        name='HeadingStyle',
        fontName=f'{font_family}-Bold',
        fontSize=heading_size,
        spaceAfter=template.get('section_spacing', 12),
        spaceBefore=8,
        textColor=safe_color(heading_color),
        leftIndent=0,
        borderWidth=1 if template.get('use_borders') else 0,
        borderColor=safe_color(template.get('border_color', heading_color)),
        borderPadding=6 if template.get('use_borders') else 0
    ))
    
    # Subheading style
    styles.add(ParagraphStyle(
        name='SubHeadingStyle',
        fontName=f'{font_family}-Bold',
        fontSize=heading_size - 2,
        spaceAfter=8,
        spaceBefore=6,
        textColor=safe_color(heading_color),
        leftIndent=10
    ))
    
    # Body text with proper spacing
    styles.add(ParagraphStyle(
        name='BodyStyle',
        fontName=font_family,
        fontSize=body_size,
        leading=line_height,
        alignment=TA_JUSTIFY,
        spaceAfter=6,
        spaceBefore=2,
        leftIndent=0,
        rightIndent=0
    ))
    
    # Bullet point style
    bullet_char = template.get('bullet_style', '•')
    styles.add(ParagraphStyle(
        name='BulletStyle',
        fontName=font_family,
        fontSize=body_size,
        leading=line_height,
        alignment=TA_LEFT,
        spaceAfter=4,
        spaceBefore=2,
        leftIndent=20,
        bulletIndent=10,
        bulletFontName=font_family,
        bulletColor=safe_color(heading_color)
    ))
    
    # Metadata styles - using accent color to match title
    styles.add(ParagraphStyle(
        name='MetaLabelStyle',
        fontName=f'{font_family}-Bold',
        fontSize=meta_size,
        textColor=safe_color(accent_color),  # Use accent color
        spaceAfter=2,
        alignment=TA_LEFT
    ))
    
    styles.add(ParagraphStyle(
        name='MetaValueStyle',
        fontName=font_family,
        fontSize=meta_size,
        textColor=colors.black,
        spaceAfter=4,
        alignment=TA_LEFT
    ))
    
    # Special styles for different templates
    if template.get('minimal'):
        # Ultra-minimal override
        styles['HeadingStyle'].fontSize = heading_size - 2
        styles['HeadingStyle'].spaceAfter = 16
        styles['BodyStyle'].spaceAfter = 8
        
    elif template.get('decorative'):
        # Enhanced decorative styles for Aesthetic template
        styles.add(ParagraphStyle(
            name='DecorativeBoxStyle',
            fontName=font_family,
            fontSize=body_size,
            alignment=TA_CENTER,
            borderWidth=2,
            borderColor=safe_color(title_color),
            borderPadding=10,
            backColor=safe_color(template.get('background_accent', '#F5F5F5')),
            spaceAfter=12,
            spaceBefore=12
        ))
        
        # Enhanced heading style for decorative templates
        # Create new Decorative-specific styles instead of overriding
        styles.add(ParagraphStyle(
            name='DecorativeHeadingStyle',
            fontName=f'{font_family}-Bold',
            fontSize=heading_size + 1,
            spaceAfter=template.get('section_spacing', 18),
            spaceBefore=12,
            textColor=safe_color(heading_color),
            leftIndent=0,
            borderWidth=2,
            borderColor=safe_color(template.get('border_color', heading_color)),
            borderPadding=(8, 8, 8, 8),
            backColor=safe_color(template.get('background_accent', '#FFF8F0')),
            alignment=TA_CENTER  # Center headings for dramatic effect
        ))
    
    elif template.get('use_borders'):
        # Professional template with better border alignment
        # Create new Professional-specific styles instead of overriding
        styles.add(ParagraphStyle(
            name='ProfessionalHeadingStyle',
            fontName=f'{font_family}-Bold',
            fontSize=heading_size,
            spaceAfter=template.get('section_spacing', 12),
            spaceBefore=8,
            textColor=safe_color(heading_color),
            leftIndent=0,
            borderWidth=1,
            borderColor=safe_color(template.get('border_color', heading_color)),
            borderPadding=(10, 10, 10, 10),  # Better padding for vertical centering
            backColor=safe_color(template.get('background_accent', '#F8FAFC')),
            alignment=TA_LEFT,
            leading=heading_size * 1.4  # Better line height for centering
        ))
    
    elif template.get('serif') or template.get('formal_layout'):
        # Classic Serif template with traditional academic styling
        serif_family = 'Times-Roman'  # Use standard ReportLab font name
        
        # Create serif-specific styles instead of overriding
        styles.add(ParagraphStyle(
            name='SerifTitleStyle',
            fontName='Times-Bold',  # Use standard ReportLab bold font
            fontSize=title_size,
            leading=title_size * 1.2,
            alignment=TA_CENTER,
            spaceAfter=25,
            spaceBefore=15,
            textColor=safe_color(title_color),
            borderWidth=0,  # Traditional - no borders
            borderPadding=0
        ))
        
        # Create serif heading style
        styles.add(ParagraphStyle(
            name='SerifHeadingStyle',
            fontName='Times-Bold',  # Use standard ReportLab bold font
            fontSize=heading_size,
            spaceAfter=template.get('section_spacing', 14),
            spaceBefore=10,
            textColor=safe_color(heading_color),
            leftIndent=0,
            borderWidth=0,  # Traditional style
            borderPadding=0,
            alignment=TA_LEFT
        ))
        
        # Create serif body style
        styles.add(ParagraphStyle(
            name='SerifBodyStyle',
            fontName=serif_family,
            fontSize=body_size,
            leading=line_height,
            alignment=TA_JUSTIFY,
            spaceAfter=8,  # Traditional spacing
            spaceBefore=3,
            leftIndent=0,
            rightIndent=0
        ))
        
        # Create serif bullet style
        styles.add(ParagraphStyle(
            name='SerifBulletStyle',
            fontName=serif_family,
            fontSize=body_size,
            leading=line_height,
            alignment=TA_LEFT,
            spaceAfter=6,
            spaceBefore=3,
            leftIndent=25,  # Traditional indent
            bulletIndent=15,
            bulletFontName=serif_family,
            bulletColor=safe_color(heading_color)
        ))
        
        # Also override the metadata styles for serif
        styles.add(ParagraphStyle(
            name='SerifMetaLabelStyle',
            fontName='Times-Bold',  # Use standard ReportLab bold font
            fontSize=meta_size,
            textColor=safe_color(accent_color),
            spaceAfter=2,
            alignment=TA_LEFT
        ))
        
        styles.add(ParagraphStyle(
            name='SerifMetaValueStyle',
            fontName=serif_family,
            fontSize=meta_size,
            textColor=colors.black,
            spaceAfter=4,
            alignment=TA_LEFT
        ))
        
        # Create serif sub-heading style
        styles.add(ParagraphStyle(
            name='SerifSubHeadingStyle',
            fontName='Times-Bold',  # Use standard ReportLab bold font
            fontSize=heading_size - 2,
            spaceAfter=8,
            spaceBefore=6,
            textColor=safe_color(heading_color),
            leftIndent=10
        ))
    
    return styles

def is_metadata_line(line):
    """Check if a line contains metadata that should be filtered when meta banner is shown"""
    line_lower = line.lower()
    metadata_keys = ['titre du chapitre', 'titre de la leçon', 'durée', 'classe', 'matière']
    
    # Check for lines with metadata keys followed by colon
    for key in metadata_keys:
        if key in line_lower and ':' in line:
            return True
    
    # Also check for markdown formatted metadata like ### **Titre**: Value
    if line.startswith('### ') and '**:' in line:
        content = line.lstrip('### ')
        if content.startswith('**') and '**:' in content:
            key_part = content.split('**:', 1)[0].lstrip('**').lower()
            for key in metadata_keys:
                if key in key_part:
                    return True
    
    return False

def get_template_styles(template):
    """Get the appropriate style names for a template"""
    if template.get('serif') or template.get('formal_layout'):
        return {
            'title': 'SerifTitleStyle',
            'heading': 'SerifHeadingStyle', 
            'subheading': 'SerifSubHeadingStyle',
            'body': 'SerifBodyStyle',
            'bullet': 'SerifBulletStyle'
        }
    elif template.get('decorative'):
        return {
            'title': 'DecorativeTitleStyle',
            'heading': 'DecorativeHeadingStyle',
            'subheading': 'SubHeadingStyle',
            'body': 'BodyStyle',
            'bullet': 'BulletStyle'
        }
    elif template.get('use_borders'):
        return {
            'title': 'TitleStyle',
            'heading': 'ProfessionalHeadingStyle',
            'subheading': 'SubHeadingStyle',
            'body': 'BodyStyle', 
            'bullet': 'BulletStyle'
        }
    else:
        return {
            'title': 'TitleStyle',
            'heading': 'HeadingStyle',
            'subheading': 'SubHeadingStyle',
            'body': 'BodyStyle',
            'bullet': 'BulletStyle'
        }

def extract_metadata(content):
    """Extract metadata from markdown content, handling bold formatting"""
    meta = {}
    for line in content.split('\n'):
        line = line.strip()
        
        # Handle both regular and markdown bold formatting
        if ':' in line:
            # Remove markdown bold formatting for key detection
            clean_line = re.sub(r'\*\*(.*?)\*\*', r'\1', line).lower()
            
            for key in ['titre du chapitre', 'titre de la leçon', 'durée', 'classe', 'matière']:
                if key in clean_line:
                    # Split on the first colon and get the value
                    parts = line.split(':', 1)
                    if len(parts) == 2:
                        value = parts[1].strip()
                        # Only store non-empty values
                        if value:
                            meta[key] = value
                    break
    return meta

def create_meta_banner(meta_info, styles, template):
    """Create a beautiful metadata banner with template-specific styling"""
    banner_elements = []
    
    if not meta_info:
        return banner_elements
    
    # Create enhanced metadata table
    data = []
    accent_color = template.get('accent_color', template.get('title_color', '#2E8B57'))
    
    # Key metadata fields in order with proper labels
    key_fields = [
        ('Titre du chapitre', 'titre du chapitre'),
        ('Titre de la leçon', 'titre de la leçon'), 
        ('Durée', 'durée'),
        ('Classe', 'classe'),
        ('Matière', 'matière')
    ]
    
    for display_name, key in key_fields:
        value = None
        # Find the value with case-insensitive matching
        for meta_key, meta_value in meta_info.items():
            if key.lower() in meta_key.lower():
                value = meta_value
                break
        
        if value:
            # Create formatted label and value with accent color
            label_text = f"<b><font color='{accent_color}'>{display_name}:</font></b>"
            
            # Use appropriate meta styles based on template
            if template.get('serif') or template.get('formal_layout'):
                label_style = styles.get('SerifMetaLabelStyle', styles['BodyStyle'])
                value_style = styles.get('SerifMetaValueStyle', styles['BodyStyle'])
            else:
                label_style = styles.get('MetaLabelStyle', styles['BodyStyle'])
                value_style = styles.get('MetaValueStyle', styles['BodyStyle'])
                
            data.append([Paragraph(label_text, label_style), 
                        Paragraph(value, value_style)])
    
    if data:
        # Enhanced table styling based on template - moved slightly left
        col_widths = [3.5*cm, 10*cm] if template.get('decorative') else [3*cm, 9.5*cm]
        table = Table(data, colWidths=col_widths)
        
        table_style = [
            ('FONTNAME', (0, 0), (-1, -1), template.get('font_family', 'Helvetica')),
            ('FONTSIZE', (0, 0), (-1, -1), template.get('meta_size', 11)),
            ('ALIGN', (0, 0), (-1, -1), 'LEFT'),
            ('VALIGN', (0, 0), (-1, -1), 'TOP'),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
            ('TOPPADDING', (0, 0), (-1, -1), 6),
            ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ]
        
        # Template-specific styling
        if template.get('use_borders'):
            table_style.extend([
                ('BOX', (0, 0), (-1, -1), 1, safe_color(accent_color)),
                ('INNERGRID', (0, 0), (-1, -1), 0.5, safe_color(template.get('border_color', accent_color))),
            ])
            
        if template.get('background_accent'):
            table_style.append(
                ('BACKGROUND', (0, 0), (-1, -1), safe_color(template.get('background_accent')))
            )
        
        table.setStyle(TableStyle(table_style))
        
        banner_elements.append(table)
        banner_elements.append(Spacer(1, 20))
    
    return banner_elements

def parse_markdown_to_story(content, styles, template, ui_metadata=None):
    """Convert structured markdown content to ReportLab story elements based on a strict hierarchy."""
    story = []
    
    # Get appropriate style names for this template
    template_styles = get_template_styles(template)
    
    # Extract metadata for banner if enabled
    content_meta = extract_metadata(content)
    
    # Merge content metadata with UI metadata (UI as fallback)
    meta_info = {}
    if ui_metadata:
        meta_info.update(ui_metadata)
    if content_meta:
        meta_info.update(content_meta)  # Content metadata takes precedence
    
    show_meta_banner = template.get('show_meta_banner') and meta_info
    
    # Add meta banner if enabled
    if show_meta_banner:
        story.extend(create_meta_banner(meta_info, styles, template))
    
    def format_inline_markdown(text, accent):
        """Convert minimal markdown to ReportLab-friendly inline tags"""
        formatted = re.sub(r'\*\*(.*?)\*\*', f'<b><font color="{accent}">\\1</font></b>', text)
        formatted = re.sub(r'\*(.*?)\*', r'<i>\1</i>', formatted)
        return formatted

    def is_table_line(line):
        if not line:
            return False
        pipe_count = line.count('|')
        if pipe_count < 2:
            return False
        if line.lstrip().startswith('- '):
            return False
        return True

    def is_separator_row(line):
        # Matches | --- | style separator rows
        return bool(re.fullmatch(r'\s*\|?(\s*:?-{3,}:?\s*\|)+\s*\|?\s*', line))

    # Process markdown content
    lines = content.split('\n')
    accent_color = template.get('accent_color', template.get('title_color', '#2E8B57'))
    try:
        accent_hex = colors.HexColor(accent_color)
    except Exception:
        accent_hex = colors.HexColor('#2E8B57')
    idx = 0
    total_lines = len(lines)

    while idx < total_lines:
        line = lines[idx].strip()
        idx += 1

        if not line:
            continue

        # Skip metadata lines if meta banner is shown to avoid duplication
        if show_meta_banner and is_metadata_line(line):
            continue

        # Markdown tables
        if is_table_line(line):
            table_lines = [line]
            # Collect subsequent table lines
            while idx < total_lines and is_table_line(lines[idx].strip()):
                table_lines.append(lines[idx].strip())
                idx += 1

            # Build table data, ignoring pure separator rows
            table_data = []
            for row in table_lines:
                if is_separator_row(row):
                    continue
                cells = [cell.strip() for cell in row.strip('|').split('|')]
                if cells:
                    formatted_cells = [Paragraph(format_inline_markdown(cell, accent_color), styles[template_styles['body']]) for cell in cells]
                    table_data.append(formatted_cells)

            if table_data:
                max_cols = max(len(row) for row in table_data)
                
                # Sanity check: if table has too many columns (likely malformed markdown), skip it
                if max_cols > 15:
                    print(f"⚠️ Warning: Table with {max_cols} columns detected, skipping (likely malformed markdown)")
                    continue
                
                for row in table_data:
                    while len(row) < max_cols:
                        row.append(Paragraph('', styles[template_styles['body']]))

                # Calculate reasonable column widths
                available_width = 455.71  # A4 width minus margins
                col_width = available_width / max_cols if max_cols > 0 else available_width
                
                table = Table(table_data, colWidths=[col_width] * max_cols, repeatRows=1)
                table_style = TableStyle([
                    ('BACKGROUND', (0, 0), (-1, 0), accent_hex),
                    ('TEXTCOLOR', (0, 0), (-1, 0), colors.white),
                    ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
                    ('ALIGN', (0, 0), (-1, -1), 'CENTER'),
                    ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
                    ('INNERGRID', (0, 0), (-1, -1), 0.5, accent_hex),
                    ('BOX', (0, 0), (-1, -1), 0.75, accent_hex),
                    ('LEFTPADDING', (0, 0), (-1, -1), 6),
                    ('RIGHTPADDING', (0, 0), (-1, -1), 6),
                    ('TOPPADDING', (0, 0), (-1, -1), 6),
                    ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
                ])

                # Alternate row shading for readability
                for row_idx in range(1, len(table_data)):
                    if row_idx % 2 == 1:
                        table_style.add('BACKGROUND', (0, row_idx), (-1, row_idx), colors.HexColor('#F7F7F7'))

                table.setStyle(table_style)
                story.append(Spacer(1, 6))
                story.append(table)
                story.append(Spacer(1, 12))
            continue

        # Main title (#)
        if line.startswith('# '):
            story.append(Paragraph(line.lstrip('# '), styles[template_styles['title']]))
            story.append(Spacer(1, 15))

        # Section headings (##)
        elif line.startswith('## '):
            story.append(Paragraph(line.lstrip('## '), styles[template_styles['heading']]))
            story.append(Spacer(1, 10))

        # Sub-headings / Metadata (###)
        elif line.startswith('### '):
            line_content = line.lstrip('### ')
            # Handle bolded metadata keys like '### **Titre**: Valeur'
            if line_content.startswith('**') and '**:' in line_content:
                # Split into bold key and value
                parts = line_content.split('**:', 1)
                if len(parts) == 2:
                    key = parts[0].lstrip('**')
                    value = parts[1].strip()
                    # Create a paragraph with accent-colored bold key
                    p_text = f'<b><font color="{accent_color}">{key}:</font></b> {value}'
                    p = Paragraph(p_text, styles[template_styles['subheading']])
                    story.append(p)
                else:
                    story.append(Paragraph(line_content, styles[template_styles['subheading']]))
            else:
                # Regular sub-heading
                story.append(Paragraph(line_content, styles[template_styles['subheading']]))

        # Bullet points
        elif line.startswith('- ') or line.startswith('* '):
            bullet_char = template.get('bullet_style', '•')
            bullet_text = line.lstrip('-* ')
            bullet_text = format_inline_markdown(bullet_text, accent_color)
            story.append(Paragraph(bullet_text, styles[template_styles['bullet']], bulletText=bullet_char))

        # Regular paragraphs
        else:
            formatted_line = format_inline_markdown(line, accent_color)
            story.append(Paragraph(formatted_line, styles[template_styles['body']]))
    
    return story

def generate_smart_filename(prefix, topic, class_level, output_dir, extension):
    """Generate a unique filename that doesn't overwrite existing files."""
    safe_topic = "".join(x for x in topic if x.isalnum() or x in " _-").strip()
    base_filename = f"{prefix}_{safe_topic}_{class_level}"
    
    # Check if base filename exists
    full_path = os.path.join(output_dir, f"{base_filename}.{extension}")
    if not os.path.exists(full_path):
        return f"{base_filename}.{extension}"
    
    # Add timestamp for uniqueness
    timestamp = datetime.now().strftime("%H%M%S")
    timestamped_filename = f"{base_filename}_{timestamp}.{extension}"
    full_path = os.path.join(output_dir, timestamped_filename)
    
    if not os.path.exists(full_path):
        return timestamped_filename
    
    # If timestamp still conflicts, add counter
    counter = 1
    while True:
        counter_filename = f"{base_filename}_{timestamp}_{counter}.{extension}"
        full_path = os.path.join(output_dir, counter_filename)
        if not os.path.exists(full_path):
            return counter_filename
        counter += 1

def save_fiche_to_pdf(content, lesson_topic, class_level, output_dir, queue, template_name: str | None = "Normal", subject: str | None = None):
    """Save fiche content as PDF using ReportLab"""
    try:
        os.makedirs(output_dir, exist_ok=True)
        
        # Get template configuration - ensure template_name is a string
        template_key = template_name if template_name is not None else "Normal"
        template = PDF_TEMPLATES.get(template_key, PDF_TEMPLATES["Normal"])
        
        # Create smart filename that doesn't overwrite
        filename = generate_smart_filename("Fiche", lesson_topic, class_level, output_dir, "pdf")
        full_path = os.path.join(output_dir, filename)
        
        # Create document
        margins = template.get('margins', (2*cm, 2*cm, 2*cm, 2*cm))
        doc = SimpleDocTemplate(
            full_path,
            pagesize=A4,
            rightMargin=margins[0],
            leftMargin=margins[1], 
            topMargin=margins[2],
            bottomMargin=margins[3]
        )
        
        # Create styles and story
        styles = create_pdf_styles(template)
        
        # Create enhanced metadata from UI values as fallback
        ui_metadata = {
            'titre de la leçon': lesson_topic,
            'classe': class_level.upper(),
            'matière': subject if subject else ''
        }
        
        story = parse_markdown_to_story(content, styles, template, ui_metadata)
        
        # Add main title if not already present in content
        if not any(content.strip().startswith(prefix) for prefix in ['# ', '## ']):
            template_styles = get_template_styles(template)
            title_style = template_styles['title']
            story.insert(0, Spacer(1, 12))
            story.insert(0, Paragraph(f"Fiche Pédagogique - {lesson_topic}", styles[title_style]))
        
        # Build PDF
        doc.build(story)
        
        queue.put(("log", f"💾 PDF saved: {full_path}"))
        return full_path
        
    except Exception as e:
        queue.put(("log", f"❌ PDF Save Error: {e}"))
        return None

def save_evaluation_to_pdf(content, topics_list, class_level, output_dir, queue, template_name: str | None = "Normal", subject: str | None = None):
    """Save evaluation content as PDF using ReportLab"""
    try:
        os.makedirs(output_dir, exist_ok=True)
        
        # Get template configuration
        template_key = template_name if template_name is not None else "Normal"
        template = PDF_TEMPLATES.get(template_key, PDF_TEMPLATES["Normal"])
        
        # Create smart filename for evaluation
        topics_text = "_".join(topics_list[:2])  # Use first 2 topics to keep filename reasonable
        if len(topics_list) > 2:
            topics_text += "_etc"
        filename = generate_smart_filename("Eval", topics_text, class_level, output_dir, "pdf")
        full_path = os.path.join(output_dir, filename)
        
        # Create document
        margins = template.get('margins', (2*cm, 2*cm, 2*cm, 2*cm))
        doc = SimpleDocTemplate(
            full_path,
            pagesize=A4,
            rightMargin=margins[0],
            leftMargin=margins[1], 
            topMargin=margins[2],
            bottomMargin=margins[3]
        )
        
        # Create styles and story
        styles = create_pdf_styles(template)
        
        # Create metadata for evaluation
        ui_metadata = {
            'matière': subject,
            'classe': class_level,
            'sujets': ', '.join(topics_list)
        }
        
        story = parse_markdown_to_story(content, styles, template, ui_metadata)
        doc.build(story)
        
        queue.put(("log", f"💾 Evaluation PDF saved: {full_path}"))
        return full_path
        
    except Exception as e:
        print(f"❌ ERROR in save_evaluation_to_pdf: {e}")
        import traceback
        traceback.print_exc()
        queue.put(("log", f"❌ Evaluation PDF Save Error: {e}"))
        queue.put(("log", f"Stack trace: {traceback.format_exc()}"))
        return None
