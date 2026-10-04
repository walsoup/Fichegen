using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace FicheGen.App.Views.Controls;

/// <summary>
/// Propriété attachée convertissant du texte Markdown simple en inlines/paragraphes pour un RichTextBlock.
/// Supporte les titres (#, ##, ###), listes à puces (- ou *), listes numérotées (1.),
/// le gras (**texte**), l'italique (*texte*), le code en ligne (`code`) et les blocs de code.
/// </summary>
public static class RichTextMarkdownHelper
{
    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.RegisterAttached(
            "MarkdownText",
            typeof(string),
            typeof(RichTextMarkdownHelper),
            new PropertyMetadata(null, OnMarkdownTextChanged));

    public static readonly DependencyProperty IsStreamingProperty =
        DependencyProperty.RegisterAttached(
            "IsStreaming",
            typeof(bool),
            typeof(RichTextMarkdownHelper),
            new PropertyMetadata(false, OnIsStreamingChanged));

    public static string? GetMarkdownText(DependencyObject obj) =>
        (string?)obj.GetValue(MarkdownTextProperty);

    public static void SetMarkdownText(DependencyObject obj, string? value) =>
        obj.SetValue(MarkdownTextProperty, value);

    public static bool GetIsStreaming(DependencyObject obj) =>
        (bool)obj.GetValue(IsStreamingProperty);

    public static void SetIsStreaming(DependencyObject obj, bool value) =>
        obj.SetValue(IsStreamingProperty, value);

    private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBlock richTextBlock)
        {
            RenderMarkdown(richTextBlock);
        }
    }

    private static void OnIsStreamingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBlock richTextBlock)
        {
            RenderMarkdown(richTextBlock);
        }
    }

    private static readonly FontFamily MonospaceFont = new("Consolas, Cascadia Code, Courier New, monospace");

    public static void RenderMarkdown(RichTextBlock richTextBlock)
    {
        var rawText = GetMarkdownText(richTextBlock);
        var isStreaming = GetIsStreaming(richTextBlock);

        richTextBlock.Blocks.Clear();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            if (isStreaming)
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                p.Inlines.Add(CreateCursorRun());
                richTextBlock.Blocks.Add(p);
            }
            return;
        }

        var lines = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inCodeBlock = false;
        var codeBlockLines = new List<string>();

        Paragraph? currentParagraph = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Blocs de code ```
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCodeBlock)
                {
                    // Fin du bloc de code
                    var codeParagraph = new Paragraph
                    {
                        FontFamily = MonospaceFont,
                        FontSize = 12,
                        Margin = new Thickness(6, 4, 6, 6)
                    };

                    for (var c = 0; c < codeBlockLines.Count; c++)
                    {
                        codeParagraph.Inlines.Add(new Run { Text = codeBlockLines[c] });
                        if (c < codeBlockLines.Count - 1)
                        {
                            codeParagraph.Inlines.Add(new LineBreak());
                        }
                    }

                    richTextBlock.Blocks.Add(codeParagraph);
                    codeBlockLines.Clear();
                    inCodeBlock = false;
                    currentParagraph = null;
                }
                else
                {
                    // Début du bloc de code
                    inCodeBlock = true;
                    codeBlockLines.Clear();
                    currentParagraph = null;
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines.Add(line);
                continue;
            }

            // Ligne vide -> fin du paragraphe actuel
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                currentParagraph = null;
                continue;
            }

            // Titre H1: # Titre
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                var heading = new Paragraph
                {
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 6, 0, 3)
                };
                AddInlines(heading.Inlines, trimmed[2..]);
                richTextBlock.Blocks.Add(heading);
                currentParagraph = null;
                continue;
            }

            // Titre H2: ## Titre
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = new Paragraph
                {
                    FontSize = 14.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 5, 0, 2)
                };
                AddInlines(heading.Inlines, trimmed[3..]);
                richTextBlock.Blocks.Add(heading);
                currentParagraph = null;
                continue;
            }

            // Titre H3+: ### Titre
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                var heading = new Paragraph
                {
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 4, 0, 2)
                };
                AddInlines(heading.Inlines, trimmed[4..]);
                richTextBlock.Blocks.Add(heading);
                currentParagraph = null;
                continue;
            }

            // Liste à puces: - item ou * item
            if ((trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)) && trimmed.Length > 2)
            {
                var bulletParagraph = new Paragraph
                {
                    Margin = new Thickness(12, 1, 0, 2)
                };

                var bulletRun = new Run
                {
                    Text = "•  ",
                    FontWeight = FontWeights.Bold
                };
                bulletParagraph.Inlines.Add(bulletRun);

                AddInlines(bulletParagraph.Inlines, trimmed[2..]);
                richTextBlock.Blocks.Add(bulletParagraph);
                currentParagraph = null;
                continue;
            }

            // Liste numérotée: 1. item, 2. item
            var numMatch = Regex.Match(trimmed, @"^(\d+\.)\s+(.*)$");
            if (numMatch.Success)
            {
                var numParagraph = new Paragraph
                {
                    Margin = new Thickness(12, 1, 0, 2)
                };

                var prefixRun = new Run
                {
                    Text = numMatch.Groups[1].Value + "  ",
                    FontWeight = FontWeights.SemiBold
                };
                numParagraph.Inlines.Add(prefixRun);

                AddInlines(numParagraph.Inlines, numMatch.Groups[2].Value);
                richTextBlock.Blocks.Add(numParagraph);
                currentParagraph = null;
                continue;
            }

            // Citation: > texte
            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                var quoteParagraph = new Paragraph
                {
                    Margin = new Thickness(12, 2, 0, 4),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                };
                AddInlines(quoteParagraph.Inlines, trimmed[2..]);
                richTextBlock.Blocks.Add(quoteParagraph);
                currentParagraph = null;
                continue;
            }

            // Paragraphe normal
            if (currentParagraph == null)
            {
                currentParagraph = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 4)
                };
                AddInlines(currentParagraph.Inlines, line);
                richTextBlock.Blocks.Add(currentParagraph);
            }
            else
            {
                currentParagraph.Inlines.Add(new LineBreak());
                AddInlines(currentParagraph.Inlines, line);
            }
        }

        // Si le bloc de code n'était pas fermé (pendant le streaming)
        if (inCodeBlock && codeBlockLines.Count > 0)
        {
            var codeParagraph = new Paragraph
            {
                FontFamily = MonospaceFont,
                FontSize = 12,
                Margin = new Thickness(6, 4, 6, 6)
            };
            for (var c = 0; c < codeBlockLines.Count; c++)
            {
                codeParagraph.Inlines.Add(new Run { Text = codeBlockLines[c] });
                if (c < codeBlockLines.Count - 1)
                {
                    codeParagraph.Inlines.Add(new LineBreak());
                }
            }
            richTextBlock.Blocks.Add(codeParagraph);
        }

        // Curseur de streaming
        if (isStreaming)
        {
            if (richTextBlock.Blocks.Count == 0)
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                p.Inlines.Add(CreateCursorRun());
                richTextBlock.Blocks.Add(p);
            }
            else if (richTextBlock.Blocks[^1] is Paragraph lastParagraph)
            {
                lastParagraph.Inlines.Add(CreateCursorRun());
            }
        }
    }

    private static Run CreateCursorRun()
    {
        var run = new Run { Text = " ▍" };
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var brush) && brush is Brush b)
        {
            run.Foreground = b;
        }
        return run;
    }

    /// <summary>
    /// Parse le texte en ligne pour le gras (**texte**), l'italique (*texte*), et le code (`code`).
    /// </summary>
    public static void AddInlines(InlineCollection inlines, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Tokenisation regex pour:
        // 1. `code`
        // 2. ***bold italic***
        // 3. **bold** ou __bold__
        // 4. *italic* ou _italic_
        var pattern = @"(`(?<codeblock>[^`]+)`)|(\*\*\*(?<bolditalic>[^\*]+)\*\*\*)|(\*\*(?<bold>[^\*]+)\*\*)|(__([^_]+)__)|(\*(?<italic>[^\*]+)\*)|(_(?<italic2>[^_]+)_)";
        var matches = Regex.Matches(text, pattern);

        var lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                inlines.Add(new Run { Text = text[lastIndex..match.Index] });
            }

            if (match.Groups["codeblock"].Success)
            {
                inlines.Add(new Run
                {
                    Text = match.Groups["codeblock"].Value,
                    FontFamily = MonospaceFont,
                    FontSize = 12
                });
            }
            else if (match.Groups["bolditalic"].Success)
            {
                var bold = new Bold();
                var italic = new Italic();
                italic.Inlines.Add(new Run { Text = match.Groups["bolditalic"].Value });
                bold.Inlines.Add(italic);
                inlines.Add(bold);
            }
            else if (match.Groups["bold"].Success || match.Groups[5].Success)
            {
                var boldVal = match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups[5].Value;
                var bold = new Bold();
                bold.Inlines.Add(new Run { Text = boldVal });
                inlines.Add(bold);
            }
            else if (match.Groups["italic"].Success || match.Groups["italic2"].Success)
            {
                var itVal = match.Groups["italic"].Success ? match.Groups["italic"].Value : match.Groups["italic2"].Value;
                var italic = new Italic();
                italic.Inlines.Add(new Run { Text = itVal });
                inlines.Add(italic);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            inlines.Add(new Run { Text = text[lastIndex..] });
        }
    }
}

