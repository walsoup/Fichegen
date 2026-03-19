from __future__ import annotations

import os
import re
import base64
from io import BytesIO
from datetime import datetime
from typing import Dict, List, Any, Optional
from xml.sax.saxutils import escape

from reportlab.lib.pagesizes import A4
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, Image, XPreformatted, KeepInFrame, HRFlowable
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY, TA_LEFT
from reportlab.lib.units import cm, inch
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

    # Code Block Style
    styles.add(ParagraphStyle(
        name='CodeBlockStyle',
        fontName='Courier',
        fontSize=body_size - 1,
        leading=line_height,
        leftIndent=20,
        rightIndent=20,
        backColor=colors.whitesmoke,
        borderPadding=10,
        spaceAfter=12,
        spaceBefore=12,
        textColor=colors.black
    ))

    # Blockquote Style
    styles.add(ParagraphStyle(
        name='BlockquoteStyle',
        fontName=font_family,
        fontSize=body_size,
        leading=line_height,
        leftIndent=20,
        rightIndent=12,
        spaceAfter=12,
        spaceBefore=12,
        textColor=colors.HexColor('#1F2937'),
        borderWidth=1,
        borderColor=safe_color(accent_color),
        borderPadding=10,
        backColor=safe_color(template.get('background_accent', '#F8FAFC'))
    ))

    styles.add(ParagraphStyle(
        name='NumberedStyle',
        fontName=font_family,
        fontSize=body_size,
        leading=line_height,
        alignment=TA_LEFT,
        spaceAfter=4,
        spaceBefore=2,
        leftIndent=24,
        bulletIndent=8,
        bulletFontName=f'{font_family}-Bold'
    ))

    styles.add(ParagraphStyle(
        name='ExerciseHeadingStyle',
        fontName=f'{font_family}-Bold',
        fontSize=heading_size - 1,
        spaceAfter=10,
        spaceBefore=12,
        textColor=safe_color(template.get('title_color', heading_color)),
        borderWidth=1,
        borderColor=safe_color(accent_color),
        borderPadding=8,
        backColor=safe_color(template.get('background_accent', '#F8FAFC'))
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

    # Dedicated fiche layout styles (used by the redesigned fiche renderer)
    styles.add(ParagraphStyle(
        name='FicheHeroTitle',
        fontName=f'{font_family}-Bold',
        fontSize=title_size + 1,
        leading=(title_size + 1) * 1.15,
        alignment=TA_CENTER,
        textColor=colors.white,
        spaceAfter=0,
    ))

    styles.add(ParagraphStyle(
        name='FicheSummary',
        fontName=f'{font_family}-Bold',
        fontSize=meta_size,
        leading=meta_size * 1.4,
        alignment=TA_CENTER,
        textColor=safe_color(heading_color),
        spaceAfter=0,
    ))

    styles.add(ParagraphStyle(
        name='FicheSectionHeading',
        fontName=f'{font_family}-Bold',
        fontSize=heading_size - 1,
        leading=(heading_size - 1) * 1.2,
        textColor=colors.white,
        spaceAfter=0,
    ))

    styles.add(ParagraphStyle(
        name='FicheSubHeading',
        fontName=f'{font_family}-Bold',
        fontSize=heading_size - 3,
        leading=(heading_size - 3) * 1.2,
        textColor=safe_color(heading_color),
        spaceAfter=4,
        spaceBefore=8,
    ))

    styles.add(ParagraphStyle(
        name='FicheBody',
        fontName=font_family,
        fontSize=body_size,
        leading=line_height,
        alignment=TA_LEFT,
        textColor=colors.black,
        spaceAfter=5,
    ))

    styles.add(ParagraphStyle(
        name='FicheBullet',
        fontName=font_family,
        fontSize=body_size,
        leading=line_height,
        alignment=TA_LEFT,
        leftIndent=16,
        bulletIndent=4,
        spaceAfter=3,
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
        # Bold
        formatted = re.sub(r'\*\*(.*?)\*\*', f'<b><font color="{accent}">\\1</font></b>', text)
        # Italic
        formatted = re.sub(r'\*(.*?)\*', r'<i>\1</i>', formatted)
        # Links: [text](url) -> <link href="url" color="blue">text</link>
        formatted = re.sub(r'\[(.*?)\]\((.*?)\)', f'<link href="\\2" color="blue">\\1</link>', formatted)
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
        
        if not line:
            idx += 1
            continue

        # Skip metadata lines if meta banner is shown to avoid duplication
        if show_meta_banner and is_metadata_line(line):
            idx += 1
            continue

        if line == '---':
            story.append(Spacer(1, 4))
            story.append(HRFlowable(width='100%', thickness=1, color=accent_hex, spaceBefore=4, spaceAfter=8))
            idx += 1
            continue

        # 1. Handle Code Blocks
        if line.startswith('```'):
            idx += 1
            code_lines = []
            while idx < total_lines:
                curr_line = lines[idx] # Don't strip to preserve indentation
                if curr_line.strip().startswith('```'):
                    idx += 1
                    break
                code_lines.append(curr_line)
                idx += 1
            
            if code_lines:
                code_text = '\n'.join(code_lines)
                story.append(XPreformatted(code_text, styles['CodeBlockStyle']))
                story.append(Spacer(1, 10))
            continue

        # 2. Handle Blockquotes
        if line.startswith('> '):
            quote_lines = []
            # Collect consecutive blockquote lines
            while idx < total_lines:
                curr_line = lines[idx].strip()
                if not curr_line.startswith('> '):
                    break
                quote_lines.append(curr_line.lstrip('> ').strip())
                idx += 1
            
            if quote_lines:
                quote_text = ' '.join(quote_lines)
                formatted_quote = format_inline_markdown(quote_text, accent_color)
                story.append(Paragraph(formatted_quote, styles['BlockquoteStyle']))
                story.append(Spacer(1, 10))
            continue

        # 3. Handle Images
        # Format: ![Alt text](path/to/image.png)
        img_match = re.match(r'!\[(.*?)\]\((.*?)\)', line)
        if img_match:
            alt_text = img_match.group(1)
            img_path = img_match.group(2)
            
            try:
                available_width = 460
                if img_path.startswith('data:image/') and ';base64,' in img_path:
                    _, encoded = img_path.split(';base64,', 1)
                    img_elem = Image(BytesIO(base64.b64decode(encoded)))
                elif os.path.exists(img_path):
                    img_elem = Image(img_path)
                else:
                    raise FileNotFoundError(img_path)

                img_width = img_elem.drawWidth
                img_height = img_elem.drawHeight
                
                if img_width > available_width:
                    factor = available_width / img_width
                    img_elem.drawWidth = available_width
                    img_elem.drawHeight = img_height * factor
                
                story.append(img_elem)
                story.append(Spacer(1, 6))
                
                if alt_text:
                    story.append(Paragraph(alt_text, styles['MetaValueStyle']))
                
                story.append(Spacer(1, 12))
            except FileNotFoundError:
                story.append(Paragraph(f"[Image not found: {img_path}]", styles['BodyStyle']))
            except Exception as e:
                story.append(Paragraph(f"[Image Error: {e}]", styles['BodyStyle']))
            
            idx += 1
            continue

        # 4. Handle Markdown Tables
        if is_table_line(line):
            table_lines = [line]
            idx += 1
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

        # 5. Handle Headings
        # Main title (#)
        if line.startswith('# '):
            story.append(Paragraph(line.lstrip('# '), styles[template_styles['title']]))
            story.append(Spacer(1, 15))
            idx += 1

        # Section headings (##)
        elif line.startswith('## '):
            heading_text = line.lstrip('## ')
            heading_style = 'ExerciseHeadingStyle' if heading_text.lower().startswith(('exercice', 'partie', 'illustrations')) else template_styles['heading']
            story.append(Paragraph(heading_text, styles[heading_style]))
            story.append(Spacer(1, 10))
            idx += 1

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
            idx += 1

        # 6. Handle Bullet points
        elif line.startswith('- ') or line.startswith('* '):
            bullet_char = template.get('bullet_style', '•')
            bullet_text = line.lstrip('-* ')
            if bullet_text.startswith('[ ] '):
                bullet_char = '☐'
                bullet_text = bullet_text[4:]
            elif bullet_text.startswith('[x] '):
                bullet_char = '☑'
                bullet_text = bullet_text[4:]
            bullet_text = format_inline_markdown(bullet_text, accent_color)
            story.append(Paragraph(bullet_text, styles[template_styles['bullet']], bulletText=bullet_char))
            idx += 1

        elif re.match(r'^\d+\.\s+', line):
            match = re.match(r'^(\d+)\.\s+(.*)$', line)
            bullet_num = match.group(1)
            numbered_text = format_inline_markdown(match.group(2), accent_color)
            story.append(Paragraph(numbered_text, styles['NumberedStyle'], bulletText=f"{bullet_num}."))
            idx += 1

        # 7. Regular paragraphs
        else:
            formatted_line = format_inline_markdown(line, accent_color)
            story.append(Paragraph(formatted_line, styles[template_styles['body']]))
            idx += 1
    
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


def _format_inline_reportlab(text: str, accent_color: str) -> str:
    safe = escape(text or "")
    safe = re.sub(r'\*\*(.+?)\*\*', rf'<b><font color="{accent_color}">\1</font></b>', safe)
    safe = re.sub(r'\*(.+?)\*', r'<i>\1</i>', safe)
    return safe


def _parse_fiche_markdown(content: str) -> Dict[str, Any]:
    parsed: Dict[str, Any] = {
        "title": "Fiche pédagogique",
        "summary": "",
        "metadata": [],
        "sections": [],
    }

    current_section: Optional[Dict[str, Any]] = None
    current_subsection: Optional[Dict[str, Any]] = None
    metadata_re = re.compile(r'^###\s+\*\*([^*]+)\*\*\s*:\s*(.+)$')
    metadata_bullet_re = re.compile(r'^-\s+\*\*([^*]+)\*\*\s*:\s*(.+)$')

    for raw_line in content.splitlines():
        line = raw_line.strip()
        if not line:
            continue

        if line.startswith('# '):
            parsed["title"] = line[2:].strip() or parsed["title"]
            continue

        if line.startswith('> '):
            if not parsed["summary"]:
                parsed["summary"] = line[2:].strip().replace('**', '')
            elif current_subsection is not None:
                current_subsection["lines"].append(line)
            elif current_section is not None:
                current_section["lines"].append(line)
            continue

        meta_match = metadata_re.match(line)
        if meta_match:
            parsed["metadata"].append((meta_match.group(1).strip(), meta_match.group(2).strip()))
            continue

        bullet_meta_match = metadata_bullet_re.match(line)
        if bullet_meta_match:
            parsed["metadata"].append((bullet_meta_match.group(1).strip(), bullet_meta_match.group(2).strip()))
            if current_subsection is not None:
                current_subsection["lines"].append(line)
            elif current_section is not None:
                current_section["lines"].append(line)
            continue

        if line.startswith('## '):
            current_section = {"title": line[3:].strip(), "lines": [], "subsections": []}
            parsed["sections"].append(current_section)
            current_subsection = None
            continue

        if line.startswith('### '):
            if current_section is None:
                current_section = {"title": "Contenu", "lines": [], "subsections": []}
                parsed["sections"].append(current_section)
            current_subsection = {"title": line[4:].strip(), "lines": []}
            current_section["subsections"].append(current_subsection)
            continue

        if current_subsection is not None:
            current_subsection["lines"].append(line)
        elif current_section is not None:
            current_section["lines"].append(line)

    return parsed


def _build_redesigned_fiche_story(content: str, styles, template, ui_metadata=None):
    accent_color = template.get('accent_color', template.get('title_color', '#2E8B57'))
    title_color = safe_color(template.get('title_color', '#2E8B57'))
    section_color = safe_color(template.get('heading_color', '#2F4F4F'))
    soft_bg = safe_color(template.get('background_accent', '#F5F7FA'))
    parsed = _parse_fiche_markdown(content)

    # Distinct layout systems per template family
    if template.get('minimal'):
        layout_mode = 'minimal'
    elif template.get('serif') or template.get('formal_layout'):
        layout_mode = 'manuscript'
    elif template.get('use_borders'):
        layout_mode = 'inspector'
    elif template.get('decorative') or template.get('warm_styling'):
        layout_mode = 'atelier'
    else:
        layout_mode = 'timeline'

    hero_fill = title_color
    section_fill = section_color
    if layout_mode == 'atelier':
        hero_fill = safe_color(template.get('gradient_colors', ['#355C7D'])[0])
        section_fill = safe_color(template.get('gradient_colors', ['#355C7D', '#F08A4B'])[-1])
    elif layout_mode == 'minimal':
        hero_fill = safe_color('#2E3D45')
        section_fill = safe_color('#5D7A86')
    elif layout_mode == 'manuscript':
        hero_fill = safe_color('#2B2A28')
        section_fill = safe_color('#7F5C3C')

    if ui_metadata:
        fallback_items = [
            ('Titre de la leçon', ui_metadata.get('titre de la leçon', '')),
            ('Classe', ui_metadata.get('classe', '')),
            ('Matière', ui_metadata.get('matière', '')),
        ]
        known = {k.lower() for k, _ in parsed['metadata']}
        for key, value in fallback_items:
            if value and key.lower() not in known:
                parsed['metadata'].append((key, value))

    story: List[Any] = []

    if layout_mode == 'minimal':
        story.append(Paragraph(escape(parsed['title']), styles['SubHeadingStyle']))
        story.append(HRFlowable(width='100%', thickness=1, color=hero_fill, spaceBefore=2, spaceAfter=8))
    elif layout_mode == 'manuscript':
        story.append(Paragraph(escape(parsed['title']), styles.get('SerifTitleStyle', styles['TitleStyle'])))
        story.append(HRFlowable(width='75%', thickness=1, color=hero_fill, spaceBefore=1, spaceAfter=10))
    else:
        hero = Table([[Paragraph(escape(parsed['title']), styles['FicheHeroTitle'])]], colWidths=[16 * cm])
        hero.setStyle(TableStyle([
            ('BACKGROUND', (0, 0), (-1, -1), hero_fill),
            ('BOX', (0, 0), (-1, -1), 0.8, hero_fill),
            ('LEFTPADDING', (0, 0), (-1, -1), 14),
            ('RIGHTPADDING', (0, 0), (-1, -1), 14),
            ('TOPPADDING', (0, 0), (-1, -1), 12),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 12),
        ]))
        story.append(hero)
        story.append(Spacer(1, 10))

    if parsed['summary']:
        story.append(Paragraph(_format_inline_reportlab(parsed['summary'], accent_color), styles['FicheSummary']))
        story.append(Spacer(1, 8))

    if parsed['metadata']:
        if layout_mode == 'minimal':
            compact = ' · '.join([f"{label}: {value}" for label, value in parsed['metadata']])
            story.append(Paragraph(_format_inline_reportlab(compact, accent_color), styles['FicheBody']))
            story.append(Spacer(1, 10))
        elif layout_mode == 'manuscript':
            story.append(Paragraph("Repères de séance", styles.get('SerifSubHeadingStyle', styles['SubHeadingStyle'])))
            for label, value in parsed['metadata']:
                story.append(Paragraph(f"<b>{escape(label)}:</b> {escape(value)}", styles.get('SerifBodyStyle', styles['FicheBody'])))
            story.append(Spacer(1, 10))
        else:
            meta_rows = []
            for label, value in parsed['metadata']:
                meta_rows.append([
                    Paragraph(f"<b><font color='{accent_color}'>{escape(label)}:</font></b>", styles['FicheBody']),
                    Paragraph(escape(value), styles['FicheBody'])
                ])
            meta_table = Table(meta_rows, colWidths=[4.6 * cm, 11.4 * cm])
            meta_style = [
                ('LEFTPADDING', (0, 0), (-1, -1), 8),
                ('RIGHTPADDING', (0, 0), (-1, -1), 8),
                ('TOPPADDING', (0, 0), (-1, -1), 6),
                ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
                ('VALIGN', (0, 0), (-1, -1), 'TOP'),
            ]
            if layout_mode == 'inspector':
                meta_style.extend([
                    ('BACKGROUND', (0, 0), (-1, -1), colors.white),
                    ('BOX', (0, 0), (-1, -1), 1.0, section_color),
                    ('INNERGRID', (0, 0), (-1, -1), 0.8, section_color),
                ])
            else:
                meta_style.extend([
                    ('BACKGROUND', (0, 0), (-1, -1), soft_bg),
                    ('BOX', (0, 0), (-1, -1), 0.6, section_color),
                    ('INNERGRID', (0, 0), (-1, -1), 0.3, section_color),
                ])
            meta_table.setStyle(TableStyle(meta_style))
            story.append(meta_table)
            story.append(Spacer(1, 12))

    for section in parsed['sections']:
        if layout_mode == 'minimal':
            story.append(Paragraph(escape(section['title'].upper()), styles['SubHeadingStyle']))
            story.append(HRFlowable(width='100%', thickness=0.7, color=section_fill, spaceBefore=1, spaceAfter=6))
        elif layout_mode == 'manuscript':
            story.append(Paragraph(escape(section['title']), styles.get('SerifHeadingStyle', styles['HeadingStyle'])))
        else:
            section_banner = Table([[Paragraph(escape(section['title']), styles['FicheSectionHeading'])]], colWidths=[16 * cm])
            banner_style = [
                ('BACKGROUND', (0, 0), (-1, -1), section_fill),
                ('LEFTPADDING', (0, 0), (-1, -1), 10),
                ('RIGHTPADDING', (0, 0), (-1, -1), 10),
                ('TOPPADDING', (0, 0), (-1, -1), 6),
                ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
            ]
            if layout_mode == 'inspector':
                banner_style.append(('BOX', (0, 0), (-1, -1), 1, hero_fill))
            section_banner.setStyle(TableStyle(banner_style))
            story.append(section_banner)
            story.append(Spacer(1, 6))

        for line in section['lines']:
            bullet_char = '•'
            if layout_mode == 'timeline':
                bullet_char = '▸'
            elif layout_mode == 'atelier':
                bullet_char = '◆'
            elif layout_mode == 'minimal':
                bullet_char = '-'
            elif layout_mode == 'manuscript':
                bullet_char = '•'
            if line.startswith('- '):
                text = _format_inline_reportlab(line[2:].strip(), accent_color)
                story.append(Paragraph(text, styles['FicheBullet'], bulletText=bullet_char))
            elif re.match(r'^\d+\.\s+', line):
                m = re.match(r'^(\d+)\.\s+(.*)$', line)
                story.append(Paragraph(_format_inline_reportlab(m.group(2), accent_color), styles['FicheBullet'], bulletText=f"{m.group(1)}."))
            elif line.startswith('> '):
                story.append(Paragraph(f"<i>{_format_inline_reportlab(line[2:].strip(), accent_color)}</i>", styles['FicheBody']))
            else:
                story.append(Paragraph(_format_inline_reportlab(line, accent_color), styles['FicheBody']))

        for subsection in section['subsections']:
            subsection_style = styles['FicheSubHeading']
            if layout_mode == 'manuscript':
                subsection_style = styles.get('SerifSubHeadingStyle', styles['FicheSubHeading'])
            story.append(Paragraph(escape(subsection['title']), subsection_style))
            for line in subsection['lines']:
                bullet_char = '•'
                if layout_mode == 'timeline':
                    bullet_char = '▸'
                elif layout_mode == 'atelier':
                    bullet_char = '◆'
                elif layout_mode == 'minimal':
                    bullet_char = '-'
                if line.startswith('- '):
                    text = _format_inline_reportlab(line[2:].strip(), accent_color)
                    story.append(Paragraph(text, styles['FicheBullet'], bulletText=bullet_char))
                elif re.match(r'^\d+\.\s+', line):
                    m = re.match(r'^(\d+)\.\s+(.*)$', line)
                    story.append(Paragraph(_format_inline_reportlab(m.group(2), accent_color), styles['FicheBullet'], bulletText=f"{m.group(1)}."))
                elif line.startswith('> '):
                    story.append(Paragraph(f"<i>{_format_inline_reportlab(line[2:].strip(), accent_color)}</i>", styles['FicheBody']))
                else:
                    story.append(Paragraph(_format_inline_reportlab(line, accent_color), styles['FicheBody']))

        story.append(Spacer(1, 10))

    return story

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
        
        # Create styles and redesigned fiche story
        styles = create_pdf_styles(template)
        
        # Create enhanced metadata from UI values as fallback
        ui_metadata = {
            'titre de la leçon': lesson_topic,
            'classe': class_level.upper(),
            'matière': subject if subject else ''
        }

        story = _build_redesigned_fiche_story(content, styles, template, ui_metadata)
        
        # Build PDF
        doc.build(story)
        
        queue.put(("log", f"💾 PDF saved: {full_path}"))
        return full_path
        
    except Exception as e:
        queue.put(("log", f"❌ PDF Save Error: {e}"))
        return None


def _clean_md_inline(text: str) -> str:
    return (text or "").replace("**", "").replace("*", "").strip()


def _extract_markdown_images(content: str) -> List[Dict[str, str]]:
    images: List[Dict[str, str]] = []
    for raw_line in (content or "").splitlines():
        line = raw_line.strip()
        m = re.match(r'!\[(.*?)\]\((.*?)\)', line)
        if m:
            images.append({"alt": m.group(1).strip(), "src": m.group(2).strip()})
    return images


def _parse_reimagined_evaluation(content: str) -> Dict[str, Any]:
    parsed: Dict[str, Any] = {
        "school": "Groupe Scolaire",
        "session": "",
        "level_subject": "",
        "duration_total": "",
        "consignes": [],
        "exercises": [],
        "correction": [],
    }

    lines = (content or "").splitlines()
    idx = 0
    current_ex = None
    current_q = None
    in_consigne = False
    in_correction = False

    while idx < len(lines):
        raw = lines[idx]
        line = raw.strip()
        if not line:
            idx += 1
            continue

        if line.startswith('> '):
            text = _clean_md_inline(line[2:])
            if text.lower().startswith("session"):
                parsed["session"] = text.split(':', 1)[1].strip() if ':' in text else text
            elif text.lower().startswith("niveau"):
                parsed["level_subject"] = text
            elif text.lower().startswith("durée") or text.lower().startswith("duree"):
                parsed["duration_total"] = text
            elif not parsed["school"] or parsed["school"] == "Groupe Scolaire":
                parsed["school"] = text
            idx += 1
            continue

        if line.startswith('## Consignes générales'):
            in_consigne = True
            in_correction = False
            idx += 1
            continue

        if re.match(r'^##\s+Exercice\s+\d+\s*-\s*', line, flags=re.IGNORECASE):
            in_consigne = False
            in_correction = False
            title = re.sub(r'^##\s+Exercice\s+\d+\s*-\s*', '', line, flags=re.IGNORECASE).strip()
            current_ex = {"title": title or "Exercice", "points": "", "instruction": "", "questions": []}
            parsed["exercises"].append(current_ex)
            current_q = None
            idx += 1
            continue

        if line.startswith('## Corrigé') or line.startswith('## Corrige'):
            in_consigne = False
            in_correction = True
            current_ex = None
            current_q = None
            idx += 1
            continue

        if in_consigne and re.match(r'^\d+\.\s+', line):
            parsed["consignes"].append(_clean_md_inline(re.sub(r'^\d+\.\s+', '', line)))
            idx += 1
            continue

        if current_ex is not None:
            if line.startswith('**Points:**'):
                current_ex["points"] = _clean_md_inline(line.split(':', 1)[1] if ':' in line else line)
                idx += 1
                continue
            if line.startswith('**Consigne:**'):
                current_ex["instruction"] = _clean_md_inline(line.split(':', 1)[1] if ':' in line else line)
                idx += 1
                continue
            if line.startswith('### Q'):
                current_q = {"header": _clean_md_inline(line.lstrip('#').strip()), "prompt": ""}
                current_ex["questions"].append(current_q)
                idx += 1
                continue
            if current_q is not None and not line.startswith('Réponse:') and not line.startswith('---'):
                if not current_q["prompt"]:
                    current_q["prompt"] = _clean_md_inline(line)
                idx += 1
                continue

        if in_correction and line.startswith('- '):
            parsed["correction"].append(_clean_md_inline(line[2:]))
            idx += 1
            continue

        idx += 1

    return parsed


def _build_reimagined_evaluation_story(content: str, styles, template, ui_metadata=None) -> List[Any]:
    parsed = _parse_reimagined_evaluation(content)
    images = _extract_markdown_images(content)

    title_color = safe_color(template.get('title_color', '#0E3A5D'))
    accent = safe_color(template.get('accent_color', '#0E3A5D'))
    soft_bg = safe_color(template.get('background_accent', '#EEF4FA'))

    eval_title = ParagraphStyle(
        name='EvalTitleStyle',
        parent=styles['TitleStyle'],
        fontSize=22,
        leading=26,
        alignment=TA_CENTER,
        textColor=colors.white,
        spaceBefore=0,
        spaceAfter=0,
    )
    eval_meta = ParagraphStyle(
        name='EvalMetaStyle',
        parent=styles['BodyStyle'],
        fontSize=10,
        leading=13,
        alignment=TA_LEFT,
        textColor=colors.HexColor('#1A2A39'),
    )
    eval_heading = ParagraphStyle(
        name='EvalHeadingStyle',
        parent=styles['HeadingStyle'],
        fontSize=14,
        leading=17,
        textColor=colors.white,
        spaceBefore=0,
        spaceAfter=0,
    )
    eval_question = ParagraphStyle(
        name='EvalQuestionStyle',
        parent=styles['BodyStyle'],
        fontSize=11,
        leading=15,
        leftIndent=0,
        alignment=TA_LEFT,
    )

    story: List[Any] = []

    hero = Table([[Paragraph("EPREUVE D'EVALUATION", eval_title)]], colWidths=[16 * cm])
    hero.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, -1), title_color),
        ('BOX', (0, 0), (-1, -1), 1, title_color),
        ('TOPPADDING', (0, 0), (-1, -1), 12),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 12),
        ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ('RIGHTPADDING', (0, 0), (-1, -1), 8),
    ]))
    story.append(hero)
    story.append(Spacer(1, 8))

    info_rows = [
        [Paragraph("<b>Etablissement</b>", eval_meta), Paragraph(escape(parsed.get('school') or ''), eval_meta)],
        [Paragraph("<b>Session</b>", eval_meta), Paragraph(escape(parsed.get('session') or ''), eval_meta)],
        [Paragraph("<b>Niveau / Matière</b>", eval_meta), Paragraph(escape(parsed.get('level_subject') or ''), eval_meta)],
        [Paragraph("<b>Durée / Barème</b>", eval_meta), Paragraph(escape(parsed.get('duration_total') or ''), eval_meta)],
    ]
    info_table = Table(info_rows, colWidths=[4.8 * cm, 11.2 * cm])
    info_table.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, -1), soft_bg),
        ('BOX', (0, 0), (-1, -1), 0.8, accent),
        ('INNERGRID', (0, 0), (-1, -1), 0.4, accent),
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('TOPPADDING', (0, 0), (-1, -1), 5),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
        ('LEFTPADDING', (0, 0), (-1, -1), 6),
        ('RIGHTPADDING', (0, 0), (-1, -1), 6),
    ]))
    story.append(info_table)
    story.append(Spacer(1, 8))

    identity = Table([
        ["Nom et prénom", "_____________________________________", "Classe", "____________"],
        ["Date", "__________________", "Note", "________ / ________"],
    ], colWidths=[3.2 * cm, 6.2 * cm, 2.6 * cm, 4.0 * cm])
    identity.setStyle(TableStyle([
        ('BOX', (0, 0), (-1, -1), 0.8, accent),
        ('INNERGRID', (0, 0), (-1, -1), 0.4, accent),
        ('FONTNAME', (0, 0), (-1, -1), 'Helvetica'),
        ('FONTSIZE', (0, 0), (-1, -1), 10),
        ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
        ('TOPPADDING', (0, 0), (-1, -1), 6),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
        ('LEFTPADDING', (0, 0), (-1, -1), 6),
        ('RIGHTPADDING', (0, 0), (-1, -1), 6),
    ]))
    story.append(identity)
    story.append(Spacer(1, 10))

    consignes = parsed.get("consignes") or []
    if consignes:
        consigne_header = Table([[Paragraph("Consignes Générales", eval_heading)]], colWidths=[16 * cm])
        consigne_header.setStyle(TableStyle([
            ('BACKGROUND', (0, 0), (-1, -1), accent),
            ('TOPPADDING', (0, 0), (-1, -1), 5),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
            ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ]))
        story.append(consigne_header)
        story.append(Spacer(1, 4))
        for idx, rule in enumerate(consignes, start=1):
            story.append(Paragraph(f"{idx}. {escape(rule)}", eval_question))
        story.append(Spacer(1, 8))

    for ex_idx, ex in enumerate(parsed.get("exercises") or [], start=1):
        ex_title = ex.get('title') or f"Exercice {ex_idx}"
        ex_points = ex.get('points') or ""
        ex_instr = ex.get('instruction') or ""

        ex_header = Table(
            [[Paragraph(f"Exercice {ex_idx} - {escape(ex_title)}", eval_heading), Paragraph(f"{escape(ex_points)} pts", eval_heading)]],
            colWidths=[13.2 * cm, 2.8 * cm],
        )
        ex_header.setStyle(TableStyle([
            ('BACKGROUND', (0, 0), (-1, -1), title_color),
            ('ALIGN', (1, 0), (1, 0), 'CENTER'),
            ('TOPPADDING', (0, 0), (-1, -1), 6),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
            ('LEFTPADDING', (0, 0), (-1, -1), 8),
            ('RIGHTPADDING', (0, 0), (-1, -1), 8),
        ]))
        story.append(ex_header)
        if ex_instr:
            story.append(Spacer(1, 3))
            story.append(Paragraph(f"<i>{escape(ex_instr)}</i>", eval_question))
        story.append(Spacer(1, 5))

        for q_idx, question in enumerate(ex.get("questions") or [], start=1):
            q_header = question.get("header") or f"Q{ex_idx}.{q_idx}"
            q_prompt = question.get("prompt") or ""
            story.append(Paragraph(f"<b>{escape(q_header)}</b>", eval_question))
            if q_prompt:
                story.append(Paragraph(escape(q_prompt), eval_question))

            answer_zone = Table([
                [""],
                [""],
            ], colWidths=[16 * cm], rowHeights=[0.8 * cm, 0.8 * cm])
            answer_zone.setStyle(TableStyle([
                ('BOX', (0, 0), (-1, -1), 0.6, colors.HexColor('#8AA0B4')),
                ('INNERGRID', (0, 0), (-1, -1), 0.3, colors.HexColor('#C5D1DB')),
            ]))
            story.append(answer_zone)
            story.append(Spacer(1, 6))

    if images:
        img_header = Table([[Paragraph("Illustrations", eval_heading)]], colWidths=[16 * cm])
        img_header.setStyle(TableStyle([
            ('BACKGROUND', (0, 0), (-1, -1), accent),
            ('TOPPADDING', (0, 0), (-1, -1), 5),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
            ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ]))
        story.append(Spacer(1, 4))
        story.append(img_header)
        story.append(Spacer(1, 6))

        for img in images:
            src = img.get("src") or ""
            alt = img.get("alt") or "Illustration"
            try:
                if src.startswith('data:image/') and ';base64,' in src:
                    _, encoded = src.split(';base64,', 1)
                    img_elem = Image(BytesIO(base64.b64decode(encoded)))
                elif os.path.exists(src):
                    img_elem = Image(src)
                else:
                    continue

                max_w = 14.5 * cm
                if img_elem.drawWidth > max_w:
                    factor = max_w / float(img_elem.drawWidth)
                    img_elem.drawWidth = max_w
                    img_elem.drawHeight = img_elem.drawHeight * factor

                story.append(img_elem)
                story.append(Spacer(1, 3))
                story.append(Paragraph(escape(alt), eval_meta))
                story.append(Spacer(1, 8))
            except Exception:
                continue

    corrections = parsed.get("correction") or []
    if corrections:
        story.append(Spacer(1, 6))
        corr_header = Table([[Paragraph("Corrigé Enseignant", eval_heading)]], colWidths=[16 * cm])
        corr_header.setStyle(TableStyle([
            ('BACKGROUND', (0, 0), (-1, -1), colors.HexColor('#3B4956')),
            ('TOPPADDING', (0, 0), (-1, -1), 5),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
            ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ]))
        story.append(corr_header)
        story.append(Spacer(1, 4))
        for item in corrections:
            story.append(Paragraph(f"- {escape(item)}", eval_question))

    return story

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
        
        story = _build_reimagined_evaluation_story(content, styles, template, ui_metadata)
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
