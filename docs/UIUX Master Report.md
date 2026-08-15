PROFstudio — UI/UX Master Report
From "competent but untrustworthy" to top-percentile desktop software
Report language: English.
Product UI language: French. All proposed user-facing strings are given in French, because the product ships French-only and the copy is the interface.
Reader: the developer building it.
Evidence base: verified source reading. Every diagnostic claim carries a file:line. Claims that could not be verified are quarantined in §16.
Supersedes: all prior audits. Their errors are corrected in Appendix F.

0. The thesis
Read this section even if you skip everything else. It determines every decision below.

0.1 The app's problem is not aesthetic
PROFstudio already has the things most WinUI apps get wrong. Mica and Acrylic are applied correctly. NavigationView, SettingsCard, SelectorBar, InfoBar, AutoSuggestBox, NumberBox, PersonPicture are real controls used as intended. ThemeResource brushes dominate the layout. The French copy is native, warm, and pedagogically literate. The stepped-card form pattern is genuinely a good idea.

If the problem were aesthetic, this report would be short.

The problem is that the interface misrepresents the state of the system, hides its own primary actions, and destroys work without asking. Those are not styling defects. They are integrity defects, and they are the exact three categories that separate software people tolerate from software people trust.

Concretely:

Category	Instance	Evidence
Dishonest state	Title bar shows a green dot and "connected" with zero API keys installed	MainWindow.xaml.cs:851-880 + AppSettings.cs:41
Dishonest state	Onboarding promises student data stays on the machine; prompts and extracted PDF text go to US LLM APIs	FirstRunDialog.xaml:39-40
Dishonest state	Five controls that do nothing: Language, Accent, Telemetry, Shortcut editor, Anthropic provider	§4.1
Hidden affordance	The only Export/Print controls auto-hide after 3s of pointer inactivity	PreviewHost.xaml.cs:265-306
Hidden affordance	Preview toolbar is outside tab order when hidden (IsHitTestVisible="False")	PreviewHost.xaml:269-271
Unguarded destruction	Trash icon deletes from SQLite immediately, no dialog, no undo, no accessible name, 8px from Favorite	HistoryViewModel.cs:632, HistoryPage.xaml:303,310
Everything else in this report is downstream of those three categories.

0.2 What "top 1%" actually means for a desktop tool
It does not mean more animation, more gradients, or more Fluent. Top-percentile desktop applications — the ones people describe as "it just feels right" — are distinguished almost entirely by their behaviour on the unhappy path:

The system never lies about its own state. If it can't work, it says so before you invest effort, not after.
Nothing is destroyed without a way back. Undo is a design primitive, not a feature.
Primary actions are always reachable by mouse, keyboard, and touch, without discovery.
Every failure names a cause and offers the next move, in the user's language, at the point of failure.
Every empty state teaches. Nothing is ever just blank.
The product behaves identically everywhere. No screen is a stranger.
It works on the hardware the user actually owns, not the hardware the developer owns.
PROFstudio's happy path is already good — arguably very good. .docx via OpenXML, vector PDF via WebView2, FTS5 search, auto-save to history on every generation (FicheFormViewModel.cs:315, EvaluationViewModel.cs:300, QuizViewModel.cs:328), draft recovery, French error translation via ErrorMessageTranslator. That is a strong foundation and it is why this report is worth writing.

So the work is not to redesign the app. The work is to build the unhappy path to the same standard as the happy path, and to install a design system that stops the three pages from drifting apart again.

0.3 The seven laws
Every recommendation in this document derives from one of these. When a future decision is ambiguous, resolve it with these, in this order.

L1 — Truth. The UI states only what the system can prove. Status is derived from the authority (the credential vault, the last call result), never from a default value.

L2 — Reversibility. Any action that can lose work must be undoable, confirmable, or both. Confirmation is the weaker fallback; undo is preferred.

L3 — Permanence of command. A primary action is never revealed by hover. Hover may enhance; it may never enable.

L4 — Locality of feedback. Feedback appears where the action happened. A status bar at the bottom of the window is not feedback for a button at the top of a panel.

L5 — One pattern per problem. Three pages doing the same job look and behave identically. Divergence is a defect, not a style.

L6 — Teacher lexicon. No word appears in the UI that a teacher would not use in a staff room. DPAPI, Global Provider, FutureAccessList, Serilog, TLS 1.3, jetons, température, Project ID are all currently in the UI (SettingsPage.xaml:232,234,441,575,648,685,876,913).

L7 — Value before setup. The product must demonstrate what it does before it asks for anything.

1. Diagnostic taxonomy
Rather than a flat bug list, here is the failure structure. This matters because fixing symptoms individually will not produce a top-percentile product; fixing the classes will.

1.1 Class A — State dishonesty
The interface derives status from configuration defaults rather than from reality.

RefreshAiChip() reads AppSettings.Models, which is seeded at AppSettings.cs:41 with Models["aistudio"] = "gemini-3.6-flash" out of the box. It never consults ICredentialStore. Therefore a fresh install with zero keys renders a green dot and a model name (MainWindow.xaml.cs:851-880). The chip has exactly three possible renderings — initial green, configured green, grey "à configurer" — and no error, no failure, no checking state.

This is the archetype. The same pattern recurs: Anthropic is selectable with a key field and a "test connection" button but has no adapter behind it; the Language selector writes to settings.json and is read by nothing (SettingsViewModel.cs:259,659); the Telemetry toggle governs a telemetry system that does not exist.

Class A fix principle: every status surface binds to a derived, verified property, never to a stored setting. Introduce a single IReadinessService that answers one question — can we generate right now, and if not, why not — and let the chip, the Generate button, the empty state, and Settings all bind to that one answer.

1.2 Class B — Affordance concealment
Commands exist but are not discoverable through the primary input modality.

The preview toolbar is the flagship case. Note the precise behaviour, because it is worse than "hidden by default": on generation completion, UpdateVisualState() shows the toolbar and starts a 3-second idle timer; the timer then hides it. So the user sees Export exist, then watches it disappear, then cannot find it again. A control the user has seen and lost is worse than one they never saw, because it destroys their belief that they understand the interface.

Adjacent instances: no export from the history list at all; the read-aloud button with no stop; the PDF drop zone with no way to remove an attached file.

Class B fix principle: L3. Every command that a user would go looking for is in a persistent, labelled surface. Hover-reveal is reserved for secondary affordances that duplicate a permanent one.

1.3 Class C — Unguarded destruction
HistoryViewModel.cs:632 deletes immediately. There is no dialog, no undo, no soft-delete, no trash. The trigger is an unlabelled glyph button adjacent to another unlabelled glyph button.

Note the asymmetry: the app already has a confirmation dialog pattern, used for clearing the cache (SettingsPage.xaml.cs:278) and resetting settings (:295) — both of which are less destructive than deleting a teacher's lesson plan. The guardrails were built for the developer's data and omitted for the user's.

Class C fix principle: L2. Destructive operations get soft-delete plus undo. Confirmation dialogs are the fallback for operations that cannot be soft-deleted.

1.4 Class D — Developer artefacts in user surfaces
The assistant renders Cascadia Mono, +, −, strikethrough, and the literal string @@ 14 lignes inchangées @@ (AssistantPane.xaml:30-73). This is a Git patch shown to a primary-school teacher.
Settings header renders the literal word placeholder (SettingsPage.xaml:71) — an unbound TextBlock, shipped.
Settings exposes a 7-task routing matrix, temperature sliders, Serilog log levels, WinRT FutureAccessList tokens, GCP project IDs.
Export failure surfaces a raw English .NET exception: Échec de l'export : The process cannot access the file '...' because it is being used by another process. (ResultViewModel.cs:571) — the only untranslated error in an app that otherwise has an excellent ErrorMessageTranslator.
Class D fix principle: L6. Introduce a hard rule and enforce it in review: no identifier, library name, protocol, file format internal, or provider-console concept appears in user-facing text.

1.5 Class E — Silent failure
Three failures produce no output at all:

Failure	Behaviour	Evidence
WebView2 runtime missing	EnsureCoreWebView2Async throws; caught by empty catch; preview stays blank forever with no message	PreviewHost.xaml.cs
Non-PDF dropped on drop zone	Ignored; background reverts; no message; user believes it attached	PdfDropZone.xaml.cs:65
Navigation away mid-generation	Generation continues in background on a singleton ResultViewModel; a later generation silently overwrites the result	DI singletons
Silent failure is the most expensive defect class per line of code, because the user attributes the failure to themselves.

1.6 Class F — Pattern drift
Eleven visible divergences between QuizPage and the other two generation pages, which sit adjacent in navigation and do the same job (§8). Including a duplicated status bar rendering the same text twice on the same screen (QuizPage.xaml:211 and :278).

Root cause is structural: CardBorderStyle, CaptionSecondaryStyle, and SubjectItemTemplate are byte-identical copies in three files (FichePage.xaml:15-33, EvaluationPage.xaml:14-32, QuizPage.xaml:14-32). There is no shared dictionary, so there is no gravitational centre, so pages drift.

1.7 Class G — Target-hardware mismatch
There is exactly one adaptive breakpoint: MinWindowWidth="1280". The project documentation targets 1366×768 school hardware. At Windows' recommended 125% scaling on that panel:

1366 physical px ÷ 1.25 = 1092.8 effective DIP
1092.8 < 1280  →  narrow layout, always
The three-column layout — the app's primary design — never renders on its primary target device. Every teacher on standard school hardware gets the fallback: form + preview, assistant behind a floating button. Maximised. Permanently.

This single number invalidates a large amount of layout work and must be verified on hardware immediately (§16).

2. Design system foundation
You cannot reach top percentile by fixing screens. Screens drift. You reach it by building a system that makes the right thing the easy thing, then rebuilding screens on it.

This section is the highest-leverage work in the report. Everything after it is cheaper once this exists.

2.1 File architecture
FicheGen.App/
  Styles/
    Tokens.xaml         ← primitives: spacing, radii, durations, easings, semantic brushes
    Typography.xaml     ← the type ramp, as named styles
    Controls.xaml       ← button, card, chip, field, pill styles
    Templates.xaml      ← shared DataTemplates (subject item, doc card, diff block)
    Motion.xaml         ← reusable storyboards & transition definitions
Merged once in App.xaml. After this lands, delete all page-local copies of CardBorderStyle, CaptionSecondaryStyle, SubjectItemTemplate, and the two dead styles EnhancerToggleStyle (69 lines, FichePage.xaml:98-166) and QuickStartCardStyle (60 lines, :36-95), which have zero references project-wide.

Enforcement rule for review: a <Style> or <DataTemplate> in a page's Page.Resources is a defect unless it is used exactly once in that page and nowhere else conceptually.

2.2 Spacing scale
The current code uses arbitrary values (Margin="0,0,20,20" vs "0,0,24,24", Padding="12", "6", "8,3", "16,10"). Adopt a 4pt base scale and nothing else.

XML

Copy
<x:Double x:Key="Space25">2</x:Double>
<x:Double x:Key="Space50">4</x:Double>
<x:Double x:Key="Space100">8</x:Double>
<x:Double x:Key="Space150">12</x:Double>
<x:Double x:Key="Space200">16</x:Double>
<x:Double x:Key="Space300">24</x:Double>
<x:Double x:Key="Space400">32</x:Double>
<x:Double x:Key="Space600">48</x:Double>

<Thickness x:Key="CardPadding">16</Thickness>
<Thickness x:Key="PagePadding">24,20,24,20</Thickness>
<Thickness x:Key="FieldSpacing">0,0,0,12</Thickness>
Usage law: any hard-coded margin/padding number in a page is a defect. Use {StaticResource}.

2.3 Radius and elevation
XML

Copy
<CornerRadius x:Key="RadiusControl">4</CornerRadius>   <!-- inputs, small buttons -->
<CornerRadius x:Key="RadiusCard">8</CornerRadius>      <!-- cards, panels -->
<CornerRadius x:Key="RadiusSurface">12</CornerRadius>  <!-- flyouts, dialogs, drawers -->
<CornerRadius x:Key="RadiusPill">999</CornerRadius>    <!-- chips, status pills -->
Elevation is expressed by layer brush + stroke, not by drop shadows. WinUI reads as flat-with-material; shadows on cards make it look like a web app in a chrome window. Reserve ThemeShadow for genuinely floating surfaces: the assistant drawer in overlay mode, flyouts, dialogs.

2.4 Type ramp
Fix the ramp to Fluent's semantic sizes and never set FontSize inline again.

Style key	Size / weight	Used for
TypeDisplay	28 SemiBold	Onboarding hero only
TypeTitle	20 SemiBold	Page titles
TypeSubtitle	16 SemiBold	Card / step headings
TypeBodyStrong	14 SemiBold	Field labels, emphasis
TypeBody	14 Regular	Default text
TypeCaption	12 Regular	Helper text, metadata
TypeCaptionStrong	12 SemiBold	Pill labels, chips
Additionally: remove Cascadia Mono from the entire application. Its only use is the assistant diff (AssistantPane.xaml:30-73), and that use is a defect (§7). A teacher-facing product has no legitimate need for a code font.

2.5 Semantic colour roles
The audit found 16 hard-coded hex instances across XAML. Some are legitimate (brand gradient in App.xaml, high-contrast dictionary in HistoryPage.xaml). These are not:

Location	Value	Purpose
SettingsPage.xaml:270,313,356,478,645	#1F107C10 / #107C10	"configured" success badge
SettingsPage.xaml:399,400	#1F006CFF / #0063B1	informational badge
SettingsPage.xaml:945,947,951	#C42B1C family	danger zone
FichePage.xaml:350, EvaluationPage.xaml:287	#59000000	drawer scrim
QuizPage.xaml:255	#7F000000	drawer scrim (different value, same purpose)
Replace all of them with semantic tokens that carry a High Contrast override:

XML

Copy
<ResourceDictionary.ThemeDictionaries>
  <ResourceDictionary x:Key="Default">
    <SolidColorBrush x:Key="StatusPositiveFgBrush"  Color="#6CCB5F"/>
    <SolidColorBrush x:Key="StatusPositiveBgBrush"  Color="#1F6CCB5F"/>
    <SolidColorBrush x:Key="StatusCautionFgBrush"   Color="#FCE100"/>
    <SolidColorBrush x:Key="StatusCautionBgBrush"   Color="#1FFCE100"/>
    <SolidColorBrush x:Key="StatusCriticalFgBrush"  Color="#FF99A4"/>
    <SolidColorBrush x:Key="StatusCriticalBgBrush"  Color="#1FFF99A4"/>
    <SolidColorBrush x:Key="ScrimBrush"             Color="#99000000"/>
  </ResourceDictionary>
  <ResourceDictionary x:Key="Light">
    <SolidColorBrush x:Key="StatusPositiveFgBrush"  Color="#0F7B0F"/>
    <SolidColorBrush x:Key="StatusPositiveBgBrush"  Color="#1F0F7B0F"/>
    <SolidColorBrush x:Key="StatusCautionFgBrush"   Color="#9D5D00"/>
    <SolidColorBrush x:Key="StatusCautionBgBrush"   Color="#1F9D5D00"/>
    <SolidColorBrush x:Key="StatusCriticalFgBrush"  Color="#C42B1C"/>
    <SolidColorBrush x:Key="StatusCriticalBgBrush"  Color="#1FC42B1C"/>
    <SolidColorBrush x:Key="ScrimBrush"             Color="#59000000"/>
  </ResourceDictionary>
  <ResourceDictionary x:Key="HighContrast">
    <SolidColorBrush x:Key="StatusPositiveFgBrush"  Color="{ThemeResource SystemColorWindowTextColor}"/>
    <SolidColorBrush x:Key="StatusPositiveBgBrush"  Color="{ThemeResource SystemColorWindowColor}"/>
    <SolidColorBrush x:Key="StatusCautionFgBrush"   Color="{ThemeResource SystemColorWindowTextColor}"/>
    <SolidColorBrush x:Key="StatusCautionBgBrush"   Color="{ThemeResource SystemColorWindowColor}"/>
    <SolidColorBrush x:Key="StatusCriticalFgBrush"  Color="{ThemeResource SystemColorWindowTextColor}"/>
    <SolidColorBrush x:Key="StatusCriticalBgBrush"  Color="{ThemeResource SystemColorWindowColor}"/>
    <SolidColorBrush x:Key="ScrimBrush"             Color="{ThemeResource SystemColorWindowColor}"/>
  </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>
Note the Default key is correct — it is the WinUI convention for dark. A prior audit claimed otherwise; that recommendation is withdrawn (Appendix F).

Hard rule, enforceable by grep in CI:

grep -rn '#[0-9A-Fa-f]\{6,8\}' src/**/*.xaml
must return only App.xaml brand gradients and theme dictionary definitions. Anything in a Views/ file fails the build.

2.6 Motion tokens
XML

Copy
<x:Double x:Key="DurationFast">90</x:Double>     <!-- hover, press, tooltip -->
<x:Double x:Key="DurationNormal">180</x:Double>  <!-- panel expand, chip add -->
<x:Double x:Key="DurationSlow">300</x:Double>    <!-- drawer, page transition -->
Easing: CubicEase EaseOut for entrances, EaseIn for exits, EaseInOut for state changes. Never linear.

Reduced-motion gate. The app currently starts shimmer and pulse storyboards unconditionally. Add:

Csharp

Copy
public static bool AnimationsEnabled =>
    new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
and gate every Storyboard.Begin() on it, substituting instant state changes. This is WCAG 2.3.3 and it costs an afternoon.

2.7 Component inventory
Component	Status	Action
AppCommandBar	Missing	Build. The single most important new component (§6).
DocumentCard	Ad hoc in HistoryPage	Extract, make focusable, add context menu
ChangeProposal (diff)	Exists as Git diff	Replace entirely (§7)
StatusChip (AI readiness)	Exists, lies	Rebuild on IReadinessService (§5.2)
FieldGroup (label + control + helper + error)	Missing; ad hoc	Build; solves inconsistent validation display
ChoiceChipGroup	Ad hoc in EvaluationPage	Build; replaces 3-paradigm topic input
EmptyState	Good in PreviewHost, ad hoc elsewhere	Extract; parameterise icon/title/body/actions
PdfDropZone	Exists, silent failures	Rebuild states (§6.6)
GenerateButton	Good	Keep; add readiness-blocked state
NotificationHost	Missing	Build (§9.3)
UndoToast	Missing	Build; required for Class C fix
3. Information architecture
3.1 The current model and its cost
Navigation is tool-centric: Fiche / Évaluations / Quiz / Mes documents. This mirrors the code, not the work.

A teacher's actual unit of work is a lesson, and a lesson generates a set of related artefacts: a séance plan, an exercise sheet, an evaluation, sometimes a quiz — all on the same topic, same level, same class. The current model forces them to re-enter level, subject, and topic three times, in three pages that look subtly different, producing three unrelated database rows.

Worse, ResultViewModel is a DI singleton. Generate a fiche, navigate to Évaluations, generate — the fiche result is silently overwritten. There is no warning on navigation and no prompt on window close (OnMainWindowClosed exits immediately).

3.2 Target model: document-centric, tool-secondary
Keep the navigation labels (they are clear and teachers understand them), but change what a "page" is:

NAV                         WORKSPACE
├─ 📄 Fiche séance     ─┐
├─ 📝 Évaluations      ─┼──► ONE workspace, three configurations
├─ 🎯 Quiz             ─┘    (identical shell, different step 3)
├─ 📁 Mes documents    ────► library + reuse actions
└─ ⚙ Paramètres
Concretely:

FichePage, EvaluationPage, QuizPage become one GenerationPage with a DocumentKind parameter. The shared 80% (level, subject, topic, instructions, PDF, preview, assistant, splitter, status) exists once. Only "Step 3" differs.

This eliminates Class F entirely and permanently.
It eliminates the duplicated status bar, the two FAB styles, the two scrim values, the two splitter widths, the CancelCommand/CancelGenerationCommand divergence — not by fixing eleven things, but by deleting two files.
ResultViewModel becomes transient, one per open document, held by the workspace. Navigating away preserves it.

Introduce "Créer la suite" on a completed document: from a fiche, one click pre-fills an evaluation with the same level/subject/topic. This is the single highest-value feature in this report (§14.1) and it only becomes possible once the pages are unified.

3.3 Command placement doctrine
Right now commands are scattered across five surfaces with no rule: title bar (theme, settings, profile, assistant), floating preview toolbar (all export), status bar (shortcut hints as decoration), inline page buttons (generate), and keyboard-only (Ctrl+Shift+W).

Adopt one doctrine:

Surface	Contains	Never contains
Title bar	App identity, global system state (AI readiness), window-level toggles	Document commands
Command bar (new, per workspace)	Everything you do to the current document: Generate, Export, Print, View mode, Style, Zoom, Assistant	App settings
Inline	Step-scoped actions (add topic, attach PDF, remove file)	Document-level commands
Context menu	Item-scoped actions on lists (open, duplicate, export, delete)	Anything not about that item
Status area	Transient outcome of the last operation, with an action link	Permanent hints, keyboard shortcut lists
4. Layout & adaptive strategy
4.1 The breakpoint problem, restated with numbers
One breakpoint at 1280 DIP. Target hardware is 1366×768. Standard scaling on that panel is 125%.

Device	Physical	Scale	Effective DIP	Current layout
School laptop (documented target)	1366×768	125%	1093×614	Narrow — always
School laptop, scaling forced to 100%	1366×768	100%	1366×768	Wide (three columns in 1366px — cramped)
Teacher home laptop	1920×1080	125%	1536×864	Wide
Teacher home laptop	1920×1080	150%	1280×720	Wide, exactly at threshold — will flicker
Surface Pro	2880×1920	200%	1440×960	Wide
Projector / VPI	1280×800	100%	1280×800	Wide, exactly at threshold
Two of the six configurations sit exactly on the breakpoint, which produces layout flicker when the window is resized by a pixel. And the documented primary device never sees the primary layout.

4.2 Three-tier system
COMPACT          ≤ 1000 DIP
┌──────────────────────────────────┐   Single column, tabbed.
│ [Formulaire] [Aperçu]            │   Command bar persists.
│ ┌──────────────────────────────┐ │   Assistant = full-height drawer.
│ │                              │ │
│ │      active pane             │ │
│ │                              │ │
│ └──────────────────────────────┘ │
└──────────────────────────────────┘

MEDIUM           1001–1379 DIP   ← the school laptop lives here
┌──────────────────────────────────────────────┐
│ COMMAND BAR                                  │
├────────────────┬─────────────────────────────┤
│                │                             │
│  Formulaire    │        Aperçu               │
│  (360 fixed)   │        (fill)               │
│                │                             │
└────────────────┴─────────────────────────────┘
   Assistant = right drawer over preview, 380 wide,
   toggled from command bar (not a floating FAB)

WIDE             ≥ 1380 DIP
┌───────────────────────────────────────────────────────────┐
│ COMMAND BAR                                               │
├────────────┬──────────────────────────┬───────────────────┤
│ Formulaire │        Aperçu            │    Assistant      │
│ (360)      │        (fill)            │    (360)          │
└────────────┴──────────────────────────┴───────────────────┘
Set MinWindowWidth at 1001 and 1380 — deliberately off round numbers so no common device sits on a boundary.

4.3 Minimum window size
Currently 1024×640 DIP (MainWindow.xaml.cs:39-40). At 1024 the compact tier is active, which is correct — but 640 height is too short for the form's four steps plus command bar plus generate button. Raise the minimum height to 720 and verify the compact form scrolls cleanly at 1000×720.

4.4 Density
At 1093×614 effective DIP the current padding (Padding="12" cards, 24 page margins, 16px gaps) burns roughly 20% of vertical space on whitespace. Introduce a compact density variant applied automatically below 1100 DIP:

Token	Comfortable	Compact
CardPadding	16	12
PagePadding	24,20	16,12
Field spacing	12	8
Card gap	16	12
Control height	32	28
Do not reduce font sizes. Reduce space, never legibility — teachers are frequently reading a projected screen from the back of a classroom.

5. Screen redesign — Shell
5.1 Current
┌────────────────────────────────────────────────────────────────────┐
│ 📘 PROFstudio    [●✨ Assistant IA connecté]      [💬][🎨][⚙][👤] │  ← lies
├────────────┬───────────────────────────────────────────────────────┤
│ 🔍         │                                                       │
│ 📄 Fiche   │                                                       │
│ 📝 Éval    │                  page content                         │
│ 🎯 Quiz    │                                                       │
│ 📁 Docs    │                                                       │
│ ⚙ Param    │                                                       │
├────────────┴───────────────────────────────────────────────────────┤
│ ✨ Prêt        Ctrl+G Générer • Ctrl+B Assistant • Ctrl+P PDF      │  ← noise
└────────────────────────────────────────────────────────────────────┘
5.2 The AI readiness chip — full state machine
This is Class A's flagship and deserves a real specification.

Introduce IReadinessService with a single observable property:

Csharp

Copy
public enum ReadinessState
{
    Unknown,        // startup, before first check
    NotConfigured,  // no credential in vault for the selected provider
    Checking,       // a validation call is in flight
    Ready,          // last validation succeeded
    Degraded,       // last generation failed with a retryable error (network, 5xx, 429)
    Blocked         // last generation failed with auth error (401/403) or provider unimplemented
}
Derived from: ICredentialStore presence check and the outcome of the last real call. Never from AppSettings.Models.

State	Dot	Label	Tooltip	Click action
Unknown	○ neutral, pulsing	Assistant IA — vérification…	Vérification de la configuration	—
NotConfigured	○ neutral	Assistant IA — à configurer	Aucune clé enregistrée. Cliquez pour configurer.	Open AI setup wizard
Checking	◐ accent, indeterminate ring	Assistant IA — test en cours…	Test de la connexion	—
Ready	● positive	Assistant IA — prêt	Fournisseur : {p} · Modèle : {m} · Testé {relative time}	Open AI settings
Degraded	▲ caution	Assistant IA — connexion instable	{last error}. Cliquez pour réessayer.	Retry validation
Blocked	■ critical	Assistant IA — clé refusée	{last error}. Cliquez pour corriger.	Open AI setup wizard
Three things make this top-percentile rather than merely correct:

Shape changes with state, not just colour (○ ◐ ● ▲ ■). This satisfies WCAG 1.4.1 without needing colour at all, and it reads at a glance on a projector.
The label always contains the state in words. No user has to interpret a dot.
Clicking always does the most useful thing for that state, not a fixed "open settings".
Same service drives: the Generate button's disabled reason, the preview empty state's call to action, and the Settings AI tab badge. One truth, four surfaces.

5.3 Status bar → notification area
Currently the status bar shows {Binding StatusMessage} plus a permanent shortcut string Ctrl+G Générer • Ctrl+B Assistant • Ctrl+P Exporter PDF (MainWindow.xaml:261). Two problems: the shortcut list is decoration occupying prime real estate, and errors appear here as plain text with no action — even when the error text says "Vérifiez votre clé API dans les Paramètres."

Replace with:

┌──────────────────────────────────────────────────────────────────────┐
│ ⚠  Clé d'accès refusée par le service.   [Ouvrir les Paramètres]  ✕ │
└──────────────────────────────────────────────────────────────────────┘
Rules:

Severity icon + shape (not colour alone).
Every error carries an action button. If you can write "check X in settings", you can write a button that opens X.
Auto-dismiss success after 6s; errors persist until dismissed or superseded.
AutomationProperties.LiveSetting="Assertive" for errors, "Polite" for progress/success.
The shortcut hints move to the F1 dialog (which already exists and is good) and to tooltips (which already exist and are good).
5.4 Title bar cleanup
Remove emoji-as-sole-content from Theme (MainWindow.xaml:96), Settings (:113), Profile (:125). Use FontIcon with AutomationProperties.Name. Emoji glyph names are announced inconsistently across screen readers and scale unpredictably at 200%.
The profile flyout duplicates Settings access, which is fine, but its content should be honest: Compte local — aucune donnée partagée is currently on the flyout (MainWindow.xaml:138) and is part of the trust problem (§13).
Keep the theme switcher in the title bar. It is genuinely well placed.
6. Screen redesign — the generation workspace
This is where teachers spend their time and where the largest single win lives.

6.1 Target layout (WIDE tier)
┌───────────────────────────────────────────────────────────────────────────────┐
│  Nouvelle fiche de séance                       ● Assistant IA — prêt          │
├───────────────────────────────────────────────────────────────────────────────┤
│  ✨ Générer   │  Style ▾  👤 Élève│Corrigé  −  100%  +  │  Word  PDF  Imprimer │ ← COMMAND BAR
├──────────────┬──────────────────────────────────────────┬─────────────────────┤
│              │                                          │ ✨ Assistant         │
│ 1 CLASSE     │  ┌────────────────────────────────────┐  │ ─────────────────── │
│   Niveau ▾   │  │                                    │  │                     │
│   Matière ▾  │  │        Document A4                 │  │  Que voulez-vous    │
│              │  │                                    │  │  ajuster ?          │
│ 2 SUJET      │  │        Fiche de séance             │  │                     │
│   [________] │  │        Les fractions décimales     │  │  [Simplifier]       │
│              │  │                                    │  │  [+3 exercices]     │
│ 3 CADRE      │  │        Objectifs                   │  │  [Version DYS]      │
│   30 45 55 ▾ │  │        • Identifier…               │  │  [Différencier]     │
│   [notes___] │  │                                    │  │                     │
│              │  │                                    │  │ ┌─────────────────┐ │
│ 4 APPUI      │  └────────────────────────────────────┘  │ │ Votre demande…⬆│ │
│   [📎 PDF ]  │                                          │ └─────────────────┘ │
│              │                                          │                     │
└──────────────┴──────────────────────────────────────────┴─────────────────────┘
Key changes from current:

A persistent command bar replaces the vanishing floating toolbar.
Generate lives in the command bar, not at the bottom of a scrolling form (currently a teacher must scroll to reach it).
The assistant is a peer panel, not an afterthought reached by a FAB.
6.2 The command bar specification
This is the component that fixes Class B permanently.

Group	Items	Behaviour
Primary	✨ Générer / ⏹ Annuler	Accent-styled. Swaps to Cancel with a determinate-if-possible ring during generation. Disabled when IReadinessService ≠ Ready, with tooltip explaining why + link.
View	Style ▾, Élève/Corrigé toggle, zoom − % +	Disabled (not hidden) until a document exists. Disabled-with-reason beats absent.
Output	Word, PDF, Imprimer	Text labels, not icon-only. At MEDIUM tier collapse to icons with labels in overflow; at COMPACT tier collapse into an Exporter ▾ split button.
Overflow	RTF, Copier, Lire à voix haute, Ouvrir le dossier	CommandBar secondary commands.
Right	Assistant toggle	Sticky; reflects panel visibility.
Implementation notes:

Use CommandBar with DefaultLabelPosition="Right" for the output group so labels are visible — this is the whole point.
IsSticky="True", OverflowButtonVisibility="Auto".
Keep every existing accelerator (Ctrl+Shift+E, Ctrl+Shift+W, Ctrl+P) and surface them in the tooltips, which the current toolbar already does correctly ("Exporter en PDF (Ctrl+Shift+E)").
The bar is in tab order. Always. This alone resolves three WCAG findings.
Delete FloatingToolbar and its idle timer entirely. Do not "make it always visible" — restructure, because the floating position over the document is also wrong (it occludes the top-right of the page, where headers live).

6.3 Form panel — the four steps
The stepped-card pattern is good and stays. Refinements:

Step 1 — Classe et matière. Keep. The icon+name SubjectItemTemplate is good; move it to Templates.xaml. Consider defaulting from AppSettings defaults (already exists and works) and showing that as a hint: "D'après vos préférences" with a quick "changer" affordance.

Step 2 — Sujet. Replace AutoSuggestBox with a plain TextBox (FichePage.xaml:253). The QueryIcon="Find" magnifier promises a search that does not exist; users wait for suggestions that never arrive. If you want suggestions later, populate them from the user's own history via FTS5 — that would be genuinely excellent and is cheap since the index exists.

Step 3 — Cadre. Replace the bare NumberBox (FichePage.xaml:268-273) with quick-pick chips plus custom entry:

Durée   [ 30 min ] [ 45 min ] [ 55 min ] [ 1 h 30 ]  ( ___ min )
Rationale: French school periods are 45/55 minutes and primary sessions are 30/45. Four taps cover ~90% of cases. The NumberBox remains for the rest. This is a genuine ergonomic upgrade, not a preference — but flag it for user testing rather than treating it as settled.

Step 4 — Appui documentaire (PDF). Full state rebuild — see §6.6.

Validation display. Currently a single FormInfoBar at the bottom aggregates errors ("⚠️ Veuillez vérifier le formulaire."). Move to field-level validation via the new FieldGroup component: error text appears under the offending field, in critical colour, with an icon, and focus moves to the first invalid field on submit. Keep the summary bar as a secondary aggregate for screen readers, with LiveSetting="Assertive".

6.4 Generation states
The workspace has more states than the code currently distinguishes. Specify all of them:

State	Form	Preview	Command bar	Notification
Empty	Editable	Empty state + tips	Generate enabled if ready	—
NotReady	Editable	Empty state, CTA = "Configurer l'assistant"	Generate disabled with reason	—
Validating	Editable, first error focused	unchanged	Generate enabled	Inline field errors
Generating	Read-only, dimmed 60%	Skeleton + streaming narration	Cancel	Progress, polite
Streaming	Read-only	Live content + progress bar	Cancel	Progress
Ready	Editable	Document	All enabled	Success, auto-dismiss
Failed	Editable, preserved	Empty state OR previous doc retained	Generate enabled	Error + action, assertive
Cancelled	Editable, preserved	Previous doc retained	Generate enabled	Génération annulée. polite
Exporting	Editable	Document	Export button shows ring	—
ExportFailed	Editable	Document	Enabled	Error + action
PreviewUnavailable	Editable	WebView2 error state	Export disabled with reason	Error + install link
Two of these do not exist today and are the difference between "works" and "trustworthy": NotReady (Class A) and PreviewUnavailable (Class E).

Form lock during generation is currently absent. If a teacher edits the topic while generation runs, the result won't match the form, and there's no indication. Lock it, dim it, and put the cancel affordance where their eye already is.

6.5 Preview pane
Keep: skeleton shimmer, streaming banner with pulsing dot, style presets, Élève/Corrigé toggle, zoom, export success InfoBar with "Ouvrir le fichier". These are genuinely well built and the tooltips are exemplary ("Basculer entre la version enseignant (avec corrigé) et la version élève (avec espaces de réponse)" — that is how you write a tooltip).

Fix:

WebView2 missing → explicit error state. Currently an empty catch produces a permanent blank. Specify:
┌────────────────────────────────────────────────┐
│                    ⚠                           │
│        L'aperçu ne peut pas s'afficher         │
│                                                │
│  PROFstudio a besoin du composant Microsoft    │
│  Edge WebView2 pour afficher et exporter vos   │
│  documents. Il est absent de cet ordinateur.   │
│                                                │
│      [ Installer le composant ]                │
│      [ Copier le lien pour l'informaticien ]   │
└────────────────────────────────────────────────┘
The second button matters more than the first: on a locked-down school machine the teacher cannot install anything, and giving them a copyable link to send to IT converts a dead end into a solvable ticket. This is the kind of detail that separates tiers.

Read-aloud must be stoppable. Currently a second click restarts from the beginning (PreviewHost.xaml.cs:574-577). In front of a class this is unusable. Make it a toggle: ▶ Lire / ⏹ Arrêter, plus pause if cheap. Track speechSynthesis.speaking state and reflect it in the button.

Zoom persistence. Zoom resets per generation. Persist per session; teachers on 1366×768 will set 90% once and want it to stick.

Print currently opens the Edge browser print modal (ShowPrintUI(CoreWebView2PrintDialogKind.Browser)). Functionally fine, brand-inconsistent. Low priority. If you ever change it, use CoreWebView2PrintDialogKind.System for a more native feel.

Add "Ouvrir le dossier d'export" to overflow. After exporting, teachers immediately want the file in Explorer.

6.6 PDF drop zone — full state table
Currently: idle, drag-over, accepted. Non-PDF is silently ignored (PdfDropZone.xaml.cs:65); there is no size limit, no remove button, no progress on extraction.

State	Visual	Copy
Idle	Dashed accent border, 📎 icon	Déposez un guide PDF ici / ou parcourir
Drag over (valid)	Solid accent border, filled tint	Relâchez pour ajouter
Drag over (invalid)	Solid critical border	Seuls les fichiers PDF sont acceptés
Rejected — wrong type	Border flashes critical 400ms, inline message persists 6s	Ce format n'est pas pris en charge. Déposez un fichier PDF.
Rejected — too large	idem	Ce fichier dépasse {N} Mo. Utilisez un extrait ou un chapitre.
Rejected — unreadable/encrypted	idem	Ce PDF est protégé et son texte ne peut pas être lu.
Reading	Progress ring, file name	Lecture du document…
Attached	Solid card, file icon, name, page count, ✕	{nom}.pdf · {n} pages
Attached, no text (scanned)	Caution icon on card	Ce PDF semble être une image scannée : son texte n'a pas pu être extrait.
That last state matters enormously in this domain — a large share of teachers' guides du maître are scans. Detecting zero extractable characters and saying so, instead of silently generating a fiche that ignored the attachment, is a trust-defining behaviour.

Also add: a privacy note on the drop zone itself. Le texte de ce document sera envoyé au service d'IA pour rédiger la fiche. (§13).

7. Screen redesign — the Assistant
The assistant is architecturally the most impressive part of the app (real Myers diff, apply-in-place, Ctrl+Z) and presentationally the worst.

7.1 The diff must be rebuilt
Verified current rendering (AssistantPane.xaml:30-73):

FontFamily="Cascadia Mono, Consolas" on every line
+ prefix, success-tinted background for additions
− prefix, critical-tinted background, TextDecorations="Strikethrough" for removals
Collapsed hunks rendered from ConverterParameter='@@ {0} lignes inchangées @@' — so a teacher literally sees @@ 14 lignes inchangées @@
This is a Git patch. The target user is a CP teacher.

Target design — the "before / after" card:

┌────────────────────────────────────────────────────────┐
│  Proposition de modification                           │
│  3 ajouts · 2 remplacements                            │
├────────────────────────────────────────────────────────┤
│  Exercice 2                                            │
│                                                        │
│  Version actuelle                                      │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Compléter le texte suivant avec les mots         │  │
│  │ manquants de la liste.                           │  │
│  └──────────────────────────────────────────────────┘  │
│                                                        │
│  Nouvelle proposition                                  │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Relier chaque mot à sa définition avec une       │  │
│  │ flèche.                                          │  │
│  └──────────────────────────────────────────────────┘  │
│                                                        │
│  ··· 14 lignes inchangées ···                          │
├────────────────────────────────────────────────────────┤
│  [ ✓ Appliquer ]  [ Refuser ]  [ Copier ]              │
└────────────────────────────────────────────────────────┘
Specification:

Aspect	Rule
Font	System (Segoe UI Variable). Never monospace.
Structure	Labelled blocks, not prefixed lines
Labels	Version actuelle / Nouvelle proposition — never + / −
Removed text	No strikethrough. Use a muted background + the label. Strikethrough on a full paragraph is hard to read and reads as "wrong/error", not "replaced".
Collapsed context	··· {n} lignes inchangées ···, centred, italic, muted. Clickable to expand.
Word-level diff	Within a replaced block, highlight changed words with a subtle background — this is how Word does tracked changes and every teacher already knows it
Summary	{n} ajouts · {m} remplacements · {k} suppressions at the top
Colour	Never the only signal. Labels carry the meaning.
Word-level intra-block highlighting is the piece that makes this feel professional rather than merely non-technical. You already have the Myers diff engine; running it at word granularity within a changed hunk is a small extension.

7.2 Mode selector
Four segmented modes (Auto / Rédiger / Modifier / Question) with good tooltips. The problem is that Auto makes the other three redundant for the target user, while their presence implies that choosing wrong will produce a wrong result.

Action: default to Auto, hide the segmented control behind a small ⋯ or an "Options" disclosure. Keep the modes for power users; stop making novices choose.

7.3 Suggestion chips
These are the best-designed element in the application. Simplifier le vocabulaire, Ajouter 3 questions, Différencier l'activité map exactly to real teacher requests, and one click replaces prompt-writing.

Extend rather than change. Make chips contextual to the document kind and to what's on screen:

Document kind	Chips
Fiche	Simplifier le vocabulaire · Ajouter une phase de manipulation · Version DYS · Prolongement pour les rapides · Ajouter le matériel nécessaire
Évaluation	Rééquilibrer le barème · Ajouter un exercice facile · Version différenciée · Ajouter la grille de correction
Quiz	+5 questions · Rendre plus difficile · Ajouter des Vrai/Faux · Version corrigée
And add a "Ce qui a changé" affordance after applying, so a teacher can see the last applied change without scrolling the chat.

7.4 Conversation surface
Add AutomationProperties.LiveSetting="Polite" on new assistant messages (currently only on AssistantStatusText, AssistantPane.xaml:332).
Typing indicator should be gated on reduced-motion.
Keep the Ctrl+Entrée accelerator for Apply; it is well chosen and already tooltipped.
8. Screen redesign — Documents (History)
Rename the page label from Mes documents — actually keep it; it's good. Rebuild the interaction model.

8.1 What's already right
FTS5 full-text search (not just titles), filter chips, date grouping (Aujourd'hui / Cette semaine), stat pills, favourites, correct empty state and no-results state with the searched term interpolated. Loading ProgressRing. This page has the best information architecture in the app.

8.2 Card rebuild
Current: Border + Tapped + Tag="{Binding}" inside an ItemsControl inside a ListView with SelectionMode="None" (HistoryPage.xaml:245-248). Not focusable. Two glyph buttons with no accessible names (:303, :310).

Target:

┌────────────────────────────────────────────────────────────────────────┐
│  📖  Les fractions décimales                            ⋯   ☆          │
│      Fiche · CM2 · Mathématiques · aujourd'hui 14:20                   │
│      Séance de découverte des dixièmes et centièmes, avec…             │
└────────────────────────────────────────────────────────────────────────┘
   ▲ focusable, Enter opens, Space toggles favourite, Menu key = ⋯
Changes:

Container becomes a ListViewItem (with ListView.IsItemClickEnabled) or a templated Button. Focusable, Enter-activatable, with a focus visual.
Delete moves out of the card face into an overflow ⋯ menu. A destructive action should not be a one-pixel-miss away from a benign one.
Favourite stays on the face (frequent, benign, reversible) with AutomationProperties.Name bound to state: Ajouter aux favoris / Retirer des favoris.
Overflow menu contains: Ouvrir · Dupliquer et adapter · Exporter en Word · Exporter en PDF · Imprimer · separator · Supprimer.
Same commands available via right-click context menu and via Menu key.
Point 4 resolves a real workflow gap: reprinting last year's fiche currently requires opening it, then finding the vanishing toolbar. Two clicks instead of a scavenger hunt.

8.3 Deletion — the undo pattern
Do not merely add a confirmation dialog. Confirmation dialogs train users to click through them, and they interrupt a benign, high-frequency workflow (tidying up).

Preferred design — soft delete + undo:

Delete marks the row IsDeleted = 1, DeletedAt = now and removes it from the list immediately (optimistic, zero latency).
A toast appears in the notification area: « {Titre} » supprimé. [Annuler] — persists 8 seconds.
Ctrl+Z also undoes it while the toast is live.
A background sweep hard-deletes rows older than 30 days, tied to the existing HistoryRetentionDays setting.
Add a Corbeille filter chip alongside Tout / Fiches / Évaluations / Quiz / Favoris, listing soft-deleted items with Restaurer and Supprimer définitivement.
Fallback if you want this in one afternoon: a ContentDialog copying the existing pattern from SettingsPage.xaml.cs:278, with the title text specified in §12. Ship the fallback in Sprint 1, the undo pattern in Sprint 3.

8.4 Search
Add 300ms debounce (already on the project backlog). Add search-term highlighting in results — you have FTS5, so you have match positions. Highlighting matched terms in the excerpt turns "the search works" into "the search is helping me."

8.5 Bulk actions
At MEDIUM/WIDE tiers, enable multi-select with a contextual command bar: Exporter la sélection · Ajouter aux favoris · Supprimer. Teachers batch-print at the start of a term. Low effort, high perceived competence.

9. Feedback, errors, and the unhappy path
This section is where a 7/10 app becomes a 10/10 app. It deserves as much design attention as the main screens.

9.1 The three-part error contract
Every error message in the product must answer three questions, in this order:

What happened — in the user's terms, not the system's
Why — only if actionable
What to do now — as a button, not a sentence
The existing ErrorMessageTranslator already does (1) and often (2) very well. It never does (3), because the status bar has no action slot. Fixing the status bar (§5.3) makes the existing copy dramatically more useful with almost no copy changes.

9.2 Error catalogue
Condition	Current	Target message	Action button
No key configured, user clicks Generate	Attempts call → 400 → "Connexion au service IA impossible (erreur réseau 400). Vérifiez votre connexion Internet." — diagnoses a config problem as a network problem	Prevent it. Generate is disabled in NotReady; tooltip: Configurez votre assistant IA pour générer un document.	Configurer l'assistant
401 / 403	Clé d'accès invalide ou non autorisée. Vérifiez votre clé API dans les Paramètres. (good copy, no action)	Keep copy, drop "dans les Paramètres"	Ouvrir les Paramètres
Malformed key → 400	Falls into network branch	La clé saisie n'a pas été acceptée. Vérifiez qu'elle est copiée en entier, sans espace avant ou après.	Vérifier ma clé
429 / quota	Limite d'utilisation atteinte… patienter une minute (excellent)	Keep. Add a countdown if the header provides one.	Réessayer (enabled after delay)
5xx	Le service d'intelligence artificielle est momentanément indisponible… (good)	Keep	Réessayer
Network / DNS	Impossible de contacter le service IA. Vérifiez que votre ordinateur est bien connecté à Internet. (good)	Keep	Réessayer
Timeout	Le service IA a mis trop de temps à répondre… (good)	Keep. Consider offering Réessayer avec un sujet plus court.	Réessayer
Malformed JSON from model	Le modèle d'IA a généré une réponse mal structurée… (good)	Keep	Réessayer
Export — file locked	Raw English .NET exception (ResultViewModel.cs:571)	Impossible d'enregistrer : le fichier « {nom} » est ouvert dans une autre application. Fermez-le dans Word ou votre lecteur PDF, puis réessayez.	Réessayer · Enregistrer sous…
Export — no write permission	Raw exception	PROFstudio n'a pas l'autorisation d'écrire dans ce dossier.	Choisir un autre dossier
Export — disk full	Raw exception	Espace disque insuffisant pour enregistrer le document.	—
WebView2 missing	Silent blank	§6.5 full-panel state	Installer · Copier le lien
PDF unreadable	Silent	§6.6	Choisir un autre fichier
Anthropic selected	Test button exists, no adapter	Remove from list, or disabled with Bientôt disponible	—
The pattern to internalise: the app currently has good words and no doors. Add doors.

9.3 Notification service
One service, three channels, chosen by severity and context:

Channel	Use for	Dismissal
Inline (field)	Validation	On correction
Inline (panel)	State affecting a whole panel (preview unavailable, not ready)	On state change
Notification bar (bottom)	Operation outcomes: export success/failure, generation failure, delete + undo	Auto 6s (success) / manual (error)
Dialog	Only for irreversible confirmations that cannot be soft-deleted	Explicit
Never two channels for one event. Currently export success uses an InfoBar inside the preview and the status bar carries a message — pick one.

9.4 Progress narration
Current strings are decent: Analyse du sujet et préparation de la requête… → Rédaction de la fiche pédagogique par l'IA… → Fiche « {Title} » générée en {X.X} s.

Improve to reduce perceived wait:

Narrate structure, not the system. Rédaction des objectifs… → Construction du déroulé… → Rédaction des exercices… — driven by the streaming parser as sections appear. Teachers then watch their document being built, which is the strongest anti-anxiety device available.
Show elapsed time after 10s. Rédaction en cours… 12 s. Silence past ten seconds reads as a freeze.
Cancel must be adjacent to the progress text, not only in the command bar.
Keep the {X.X} s completion time. It signals competence and lets the teacher predict future waits.
10. Accessibility programme
Not a checklist — a programme, ordered by user impact.

10.1 Keyboard operability
Current reality: zero TabIndex, zero IsTabStop, zero XYFocus* in the entire solution. History cards unreachable. Preview toolbar outside tab order when hidden.

Target focus model:

Tab cycle (WIDE):
  Title bar (AI chip → assistant toggle → theme → settings → profile)
    → Nav pane (search → 4 destinations → settings → help)
      → Command bar (Generate → view group → export group → overflow → assistant toggle)
        → Form panel (step 1 fields → … → step 4)
          → Preview (WebView2 as single stop; F6 enters content)
            → Assistant (chips → input → send)
              → Notification bar (if present: action → dismiss)
Add F6 / Shift+F6 as pane cycling — this is the standard desktop idiom for multi-pane apps and it costs almost nothing. It gives keyboard users a way to jump between form/preview/assistant without tabbing through 30 controls.

Add Escape semantics, consistently:

Assistant drawer open → close drawer
Generation running → cancel
Dialog open → dismiss
Otherwise → clear focus from field
Currently Escape is mapped to cancel/close generically; make the precedence explicit and documented.

10.2 Names and roles
Element	Current	Fix
History favourite button	glyph only	AutomationProperties.Name bound: Ajouter aux favoris / Retirer des favoris
History delete button	glyph &#xE74D;	Moves to overflow menu; menu items are self-naming
History card	Border	ListViewItem; AutomationProperties.Name = {Title}, {Type}, {Level}, {Date}
OverlayClose (Quiz)	none	Eliminated by page unification
Theme / Settings / Profile buttons	emoji or glyph	FontIcon + Name
Zoom −/+	glyphs, tooltipped	Add Name; tooltip ≠ accessible name
Style preset combo	tooltipped	Add Name="Style visuel du document"
Diff blocks	none	Name = Version actuelle / Nouvelle proposition on the containers
10.3 Live regions
Present on four surfaces (AssistantPane.xaml:332, PreviewHost.xaml:137,257, StatusBar.xaml:34) — a better-than-average starting point. Missing on every InfoBar.

Region	Setting
Generation progress	Polite (already present)
Generation completion	Polite
Generation failure	Assertive
Validation summary	Assertive on submit
Export success	Polite
Export failure	Assertive
Delete + undo toast	Assertive (it's time-limited)
Assistant new message	Polite
Readiness state change	Polite
10.4 Colour independence
Two violations, both fixed by shape + text, not by changing colours:

AI chip: shape changes per state (○ ◐ ● ▲ ■) plus state word in the label (§5.2).
Diff: labelled blocks replace +/− and tint (§7.1).
Also audit the difficulty slider's dynamic label (🌳 Intermédiaire, ⭐ Expert) — this one is already correct, since emoji + word + position all carry the value. Good instinct; generalise it.

10.5 High contrast
Replace the 16 hard-coded hexes with the semantic brushes of §2.5. Verify by running with High Contrast Black, High Contrast White, and Desert — the scrims (#59000000 / #7F000000) are the worst offenders because they will render as a translucent grey over a high-contrast surface, destroying contrast for the drawer beneath.

For scrims specifically in HC mode: use a solid SystemColorWindowColor rather than any transparency.

10.6 Text scaling and zoom
Fixed-size text containers found:

SettingsPage.xaml:864-888 — four RGPD flow cards at Width="132"
QuickStartCardStyle — Width="140", MinHeight="98" (dead style; delete)
HistoryPage.xaml:215-221 — stat pills with fixed padding
At 200% text scaling these will clip. Rule: no Width or Height on a container whose child is text. Use MinWidth + MaxWidth and let content drive. Verify per §16.

10.7 Touch and pointer
Minimum target size 40×40 DIP for anything a teacher will hit on a TBI. Current failures: zoom −/+ buttons, favourite/delete glyph buttons, close buttons in overlays. These are all in the 28–32 range.

And the headline touch defect: export is hover-gated, which on a pure-touch device means it does not exist. Resolved by the command bar.

11. Motion and craft
Motion is where "correct" becomes "expensive-feeling". The app already has good instincts (shimmer, pulsing dot, toolbar slide) but no system.

Element	Motion	Duration	Easing
Command bar buttons	Fluent default press/hover	90ms	—
Panel switch (compact tabs)	Cross-fade + 8px slide	180ms	EaseOut
Assistant drawer	Slide from right + scrim fade	300ms	EaseOut
Card expand (settings)	Height + fade	180ms	EaseInOut
Skeleton shimmer	Loop, 1.4s cycle	—	Linear
Streaming dot	Pulse 1.2s	—	EaseInOut
Chip add/remove	Scale 0.9→1 + fade	180ms	EaseOut
Notification enter	Slide up 12px + fade	180ms	EaseOut
Notification exit	Fade	90ms	EaseIn
Undo toast countdown	Linear progress hairline	8000ms	Linear
Document first paint	Fade in	300ms	EaseOut
Things that must not animate: text content changes during streaming (no per-character animation — it makes reading impossible), zoom (instant), validation errors (instant, with focus move).

All gated on UISettings.AnimationsEnabled.

One high-value polish item: when the assistant applies a change, briefly highlight the modified region in the preview (a 1.2s fading background tint on the changed block). This closes the loop between the assistant panel and the document, and it is the kind of detail that makes users say the app "feels alive". You already have the diff ranges; the WebView2 injection is a few lines.

12. Content design
The copy is the interface. This section specifies the voice and rewrites the failures.

12.1 Voice principles
Address the teacher, not the system. Votre fiche est prête. not Génération terminée.
Name the object. Votre fiche « Les fractions » beats Le document.
Never explain the mechanism. They do not need to know a model, a provider, a token, or a temperature exists.
Errors take responsibility. PROFstudio n'a pas pu… not Erreur : …
Every instruction is a button. If the text says "go to X", ship the button.
Vouvoiement, warm, never cute. The existing copy nails this. Preserve it.
Emoji as accent, never as sole meaning.
12.2 Terminology map — enforce in review
Currently in UI	Location	Replace with
Clé d'accès (API Key)	FirstRunDialog	Clé de connexion
Fournisseur IA principal (Global Provider)	SettingsPage.xaml:234	Service d'intelligence artificielle
DPAPI	:232,253,899	chiffrées par Windows
FutureAccessList WinRT	:648	autorisation d'accès au dossier
Serilog	:685,919,937	journal technique
TLS 1.3	:876	connexion chiffrée
ID du projet GCP / Région	:441-442	(move behind Advanced; do not soften — hide)
température	:575	Style de rédaction : Fidèle ↔ Créatif
jetons / max tokens	:913	Longueur maximale du document
routage / matrice de routage	:85,226,516	(move behind Advanced)
Charger les modèles (/models)	:421	Détecter les modèles disponibles
URL de base du proxy	:407	Adresse du service local
placeholder	:71	delete
@@ {n} lignes inchangées @@	AssistantPane.xaml	··· {n} lignes inchangées ···
12.3 Rewritten copy deck
Privacy — first run (replaces FirstRunDialog.xaml:39-40)

🔒 Vos documents restent chez vous
Vos fiches, évaluations et quiz sont enregistrés uniquement sur cet ordinateur. Aucun compte, aucune synchronisation.

Comment fonctionne la rédaction ?
Pour rédiger un document, PROFstudio envoie votre demande — niveau, matière, sujet, consignes, et le texte du guide PDF si vous en déposez un — au service d'intelligence artificielle que vous choisirez.
N'y indiquez pas de nom d'élève ni d'information personnelle.
Pour travailler entièrement hors ligne, choisissez l'option « Service local (Ollama) ».

Profile flyout (replaces MainWindow.xaml:138)

Vos documents sont enregistrés sur cet ordinateur

Settings privacy panel (replaces SettingsPage.xaml:867-868)

Votre saisie → envoyée au service d'IA pour la rédaction
Vos documents → enregistrés uniquement sur ce PC

Delete confirmation (fallback pattern, Sprint 1)

Supprimer ce document ?
« {Titre} » sera retiré de vos documents. Vous pourrez le récupérer dans la corbeille pendant 30 jours.
[Supprimer] [Annuler]

(If shipping hard delete in Sprint 1, change the second sentence to Cette action est définitive.)

Undo toast

« {Titre} » supprimé. [Annuler]

Generate disabled (not ready)

Tooltip: Configurez votre assistant IA pour générer un document.
Preview empty state CTA: [ Configurer l'assistant IA ]

Export — locked file

Impossible d'enregistrer : le fichier « {nom} » est ouvert dans une autre application. Fermez-le dans Word ou votre lecteur PDF, puis réessayez.
[Réessayer] [Enregistrer sous…]

WebView2 missing

L'aperçu ne peut pas s'afficher
PROFstudio a besoin du composant Microsoft Edge WebView2 pour afficher et exporter vos documents. Il n'est pas installé sur cet ordinateur.
[Installer le composant] [Copier le lien pour l'informaticien]

Evaluation suggestions, no subject chosen

Choisissez d'abord une matière pour voir des suggestions de notions.

PDF rejected

Ce format n'est pas pris en charge. Déposez un fichier PDF (guide du maître, manuel, progression).

PDF scanned / no text

Ce PDF semble être une image scannée : son texte n'a pas pu être lu. La fiche sera générée sans son contenu.

Onboarding step 3 — key testing

Success: ✅ Connexion réussie. Votre assistant est prêt.
Failure: Cette clé n'a pas été acceptée. Vérifiez qu'elle est copiée en entier, sans espace avant ou après.

Skip onboarding

Button: Découvrir sans configurer
(and it must actually persist — see §14.2)

13. Trust and privacy design
This is the report's most serious finding and it is not a bug — it is a design decision that was never made deliberately.

13.1 The gap
The UI asserts	Verified reality
🔒 100% Confidentiel — Vos cours et données d'élèves restent sur votre ordinateur (FirstRunDialog.xaml:39-40)	Level, subject, topic, free-text instructions, and extracted PDF guide text are sent over HTTPS to Google AI Studio / OpenAI / Vertex AI. The assistant additionally sends the full generated document and conversation history.
Stockage 100% local sur ce PC (MainWindow.xaml:138)	True for SQLite, settings.json, drafts, credentials (Credential Locker + DPAPI fallback). False for prompts.
Votre saisie → Reste sur ce poste (SettingsPage.xaml:867-868)	Contradicted by the GDPR diagram in the same tab, which does show cloud routing.
The local storage is genuinely well engineered. That is exactly what makes this indefensible: the product earned the right to a strong privacy claim and then overclaimed into inaccuracy.

The specific danger: the phrase "données d'élèves" invites a teacher to write student-specific context — "adapter pour Léa qui est dyslexique", "groupe de 4 élèves allophones" — into the free-text instructions field, which is then transmitted to a US processor. A DPO would read the current onboarding as a misrepresentation.

13.2 The fix, in three layers
Layer 1 — Honest copy (§12.3). One hour of work. Non-negotiable.

Layer 2 — Point-of-entry disclosure. A small, permanent, non-alarming line under the free-text instructions field and on the PDF drop zone:

ℹ Ce texte est envoyé au service d'IA pour rédiger le document.

Disclosure at the point of data entry is worth more than a paragraph in onboarding nobody reads.

Layer 3 — Active protection (the differentiating feature). Before sending, scan the instruction field for likely first names against a French given-name list. If matched:

Un prénom d'élève a peut-être été saisi
Nous avons repéré « Léa » dans vos consignes. Ce texte sera envoyé au service d'IA.
[Remplacer par « un élève »] [Envoyer tel quel] [Modifier]

This is cheap (a static name list, a regex, a dialog), it is genuinely protective, and it is the kind of thing that gets a product recommended by a conseiller pédagogique numérique. No competitor in this space does it.

Layer 4 — Make the offline path visible. Ollama support already exists and works. It is currently buried in a developer-jargon settings panel. Surface it in onboarding as a first-class option: Service local — aucune donnée ne quitte votre ordinateur (installation requise). For any teacher in an establishment with data-protection sensitivities, this is the deciding feature.

14. Beyond fixing: what moves this to top percentile
Everything above brings the app to correct. These bring it to excellent. Each is chosen because it maps to a real teacher workflow and is cheap given what already exists.

14.1 "Créer la suite" — document chaining
The workflow it serves: a teacher prepares a séance, then needs an exercise sheet on it, then an evaluation two weeks later. Today that's three visits to three near-identical pages, re-entering the same level, subject, and topic, producing three unrelated rows.

The feature: on any completed document, a command bar item:

Créer la suite ▾
  ├ Une évaluation sur cette leçon
  ├ Un quiz de révision
  ├ Une version différenciée (DYS / allophones / rapides)
  └ Une fiche de remédiation
Pre-fills the target workspace with level, subject, topic, and passes the source document as context. Store a ParentId so the library can group a lesson's artefacts.

Why it's top-percentile: it is the only feature here that changes the product's unit of work from "a document" to "a lesson". It is also only possible after §3.2 unification — which is a good argument for doing the unification.

Effort: M after unification. Impossible before it.

14.2 Demo mode — value before setup
Currently there is no sample document, no offline mode, nothing the app can show without a configured API key. And Ignorer pour l'instant doesn't persist (FirstRunDialog.xaml.cs:47), so the modal returns…on every launch. The teacher is punished for choosing to look before buying, and the punishment repeats daily.

Both halves of that are cheap to fix, and together they change the shape of the product's first five minutes.

**Fix 1 — make the skip stick.** One line. `IsFirstRunCompleted = true` on the skip path, with a persistent, dismissible entry point back into setup (a chip in the AI readiness surface, which already exists per §5.2 and already opens the wizard in `NotConfigured`). Skipping must cost the user nothing and must not be irreversible.

**Fix 2 — ship three sample documents.** Three pre-generated artefacts, stored as static JSON in app resources: a *fiche de séance* CM2 on fractions, an *évaluation* 6ᵉ on the accord du participe passé, a *quiz* CE2 on the verbs. They load into the **real** preview, with the **real** command bar, and they **really** export to Word and PDF and print. No watermark, no "sample" nag, no crippled path.

```
┌────────────────────────────────────────────────────────┐
│  Voici ce que PROFstudio prépare pour vous             │
│                                                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐              │
│  │  Fiche   │  │Évaluation│  │   Quiz   │              │
│  │   CM2    │  │    6ᵉ    │  │   CE2    │              │
│  │Fractions │  │ Grammaire│  │  Verbes  │              │
│  └──────────┘  └──────────┘  └──────────┘              │
│                                                        │
│  Ouvrez un exemple, imprimez-le, exportez-le en Word.  │
│  Aucune configuration nécessaire.                      │
│                                        [ Suivant ➔ ]   │
└────────────────────────────────────────────────────────┘
```

Why this is the highest-leverage single addition in the report, ahead even of §14.1:

The product currently asks a teacher to obtain a Google Cloud credential **before it has demonstrated a single thing**. That is an unusually steep ask — it involves a Google account, a console the teacher has never seen, and a term (*clé d'API*) that reads as belonging to someone else's profession. The teacher is being asked to pay a toll to reach an unknown destination.

With samples, the sequence inverts: the teacher prints a usable document in the first ninety seconds, then is asked for a key **in order to make one of their own**. Same request, entirely different economics — a step toward a known reward rather than a toll on an unknown one. This is L7 in §0.3, and it is the only law in that list the product currently violates outright.

Verified: **no sample or demo content exists anywhere in the codebase.** This is net-new, and it is one afternoon of JSON plus a card row in the wizard.

**Effort:** S for the skip fix, M for samples.

## 14.3 The signature moment — Élève ⇄ Corrigé

Top-percentile products have one interaction that users describe to a colleague unprompted. That is the actual distribution channel for a teaching tool: a staff room, a coffee, *"regarde ce qu'il fait."*

This app's candidate already exists and is half-built: **one artefact, two printings, zero manual editing.** The `Élève / Corrigé` toggle is the most valuable feature in the product for its audience — and it currently lives inside chrome that erases itself after three seconds (§0.1).

Promote it:

| Aspect | Specification |
|:--|:--|
| Placement | Permanent segmented control in the command bar, left-centre, always labelled in words |
| Transition | 220 ms cross-fade **in place** — answer text writes in, answer lines expand. The teacher watches the *same* document reorganise; it is never replaced. |
| Persistence | Per document, restored on reopen |
| Export | Add `Exporter les deux versions` → produces `{titre}_eleve.pdf` and `{titre}_corrige.pdf` in one action, one folder, one notification with **Ouvrir le dossier** |
| Library | `Imprimer la version élève` directly on the document card's overflow menu (§8.2), skipping the workspace entirely |
| Keyboard | `Ctrl+Shift+V` toggles |
| Motion gate | Cross-fade respects `UISettings.AnimationsEnabled`; falls back to instant swap |

The in-place cross-fade is the part that matters. A hard swap communicates "two files." A morph communicates "one document, two faces," which is both the truth and the pitch.

**Effort:** S once the command bar exists.

## 14.4 DYS and accommodation presets

`Adapter la mise en page (Police DYS)` already exists as a single unexplained checkbox. In French schools, PAP and PPRE accommodations are a legal and daily reality, and a tool that handles them well gets recommended by *conseillers pédagogiques*, which is exactly the distribution that matters.

Upgrade the checkbox into a named preset group with a live thumbnail:

```
Mise en forme adaptée
( Standard )  ( Lecture facilitée )  ( Dyslexie )  ( Gros caractères )

Dyslexie : police adaptée, interligne 1,5, texte non justifié,
           espacement des mots augmenté, fond crème.
                                              [ Aperçu ]
```

Rules: each preset states what it does in plain language (no font names as the primary label), each has a thumbnail, and the choice is per-document and persisted. `Gros caractères` should be genuinely large — this preset serves visually impaired pupils *and* the teacher projecting on a TBI.

**Effort:** S (the rendering pipeline already supports style presets).

## 14.5 The library becomes a reuse tool

Today `Mes documents` is a record of the past. The rename in the user's head — from *history* to *library* — happens the moment the list offers actions that produce new work:

| Addition | Why |
|:--|:--|
| **Dupliquer et adapter** | Prefills a workspace from an existing document's level/subject/topic/duration. Teachers repeat structure constantly: same fiche shape, next chapter. Right now every document starts from zero. Near-free once §3.2 unification lands. |
| **Imprimer (élève / corrigé)** | Reprinting last year's fiche should be one menu, not a scavenger hunt |
| **Search-term highlighting** | FTS5 gives match positions; highlighting them turns "search works" into "search is helping" |
| **`Créer la suite` from the card** | §14.1, available without opening the document |
| **Grouped by lesson** | Once `ParentId` exists (§14.1), a fiche and its evaluation and its quiz collapse into one expandable row |

That last one is the endgame: the library stops listing documents and starts listing *lessons*, which is the teacher's actual unit of work.

**Effort:** S per item, M for lesson grouping.

## 14.6 Make the preview look like paper

Currently the document renders as a themed card among other cards. It should read, instantly and unmistakably, as **a sheet of paper on a desk**:

| Element | Specification |
|:--|:--|
| Desk | `SolidBackgroundFillColorTertiaryBrush` — neutral, in both themes |
| Sheet | Pure white, `RadiusSheet` = 2 (near-square, because paper is), real drop shadow, fixed A4 aspect |
| Dark mode | **The sheet stays white.** The printout will not be themed. Offer an explicit `Aperçu sombre` toggle, defaulted off, for night preparation. |
| Page breaks | Render a visible gap and a hairline where the printed page will break |

The page-break marker deserves emphasis. Teachers print. Knowing *"this spills onto a third page"* **before** walking to the staff-room printer with thirty copies queued is worth more than several features on any backlog. It is also cheap: the export pipeline already computes pagination for PDF.

**Effort:** S for the sheet, M for break markers.

## 14.7 Offline as a state, not a failure

Teachers work on trains, in staff rooms with hostile Wi-Fi, and behind captive portals. Today, offline surfaces as a generic failure **after the full Polly budget** — up to 150 seconds of spinner before the app says anything at all.

Detect before dispatch:

```csharp
var profile = NetworkInformation.GetInternetConnectionProfile();
var online  = profile?.GetNetworkConnectivityLevel()
              == NetworkConnectivityLevel.InternetAccess;
```

If offline (and the selected provider is not local), do not spend 150 seconds failing. Show the state:

```
┌──────────────────────────────────────────────────────┐
│ 📡 Aucune connexion Internet détectée                │
│ La rédaction assistée a besoin d'une connexion.      │
│ Vos documents existants restent consultables,        │
│ imprimables et exportables.                          │
│                    [ Réessayer ]  [ Mes documents ]  │
└──────────────────────────────────────────────────────┘
```

This reframes offline from "the app is broken" to "one feature is paused" — which is **true**, because history, preview, export and print are entirely local. That accuracy is the whole value: it keeps the teacher working instead of closing the app.

Wire it into `IReadinessService` as a sixth state (`Offline`), between `NotConfigured` and `Degraded` in the §5.2 table, with the chip reading `Assistant IA — hors ligne`.

**Effort:** S.

## 14.8 Suggestions drawn from the teacher's own history

§6.3 proposed replacing the topic `AutoSuggestBox` with a plain `TextBox`, because the magnifier promises a search that does not happen. The better resolution — same cost bracket, far higher value — is to **make the promise true**:

Feed the box from the FTS5 index. Typing `frac` surfaces:

```
Les fractions décimales        Fiche · CM2 · 12 mars
Comparer des fractions         Évaluation · CM1 · 8 janv.
```

Selecting one offers `Reprendre ce sujet` (prefill) or `Créer la suite` (§14.1). The index already exists; this is a query and a template.

Ship the plain `TextBox` in Sprint 4 if the FTS5 wiring is not ready — an honest text field beats a lying magnifier. But this is the version worth building.

**Effort:** M.

---

# 15. Consolidated findings

Priority is by user harm, not by effort. `S` ≈ under a day, `M` ≈ one to three days, `L` ≈ a week or more.

| ID | Pri | Class | Surface | Finding | Evidence | Fix | Effort |
|:--|:--|:--|:--|:--|:--|:--|:--|
| **UX-01** | P0 | A | Onboarding, shell, settings | Privacy copy states student data stays local; prompts, instructions and extracted PDF text are transmitted to US LLM APIs | `FirstRunDialog.xaml:39-40`; `MainWindow.xaml:138`; `SettingsPage.xaml:867-868` | §13.2 Layers 1–2 | S |
| **UX-02** | P0 | C | Documents | Delete is instant, silent, irreversible; unnamed glyph 8 px from Favourite | `HistoryViewModel.cs:632`; `HistoryPage.xaml:303,310` | §8.3 — dialog now, undo in Sprint 3 | S→M |
| **UX-03** | P0 | B | Preview | Only Export/Print controls auto-hide after 3 s idle; hover-gated; no alternative surface; unreachable on touch | `PreviewHost.xaml:269-271`; `.cs:265-306` | §6.2 command bar | S |
| **UX-04** | P1 | E | Preview | WebView2 missing → empty catch → permanent blank, no message | `PreviewHost.xaml.cs` | §6.5 error state + copy-link | S |
| **UX-05** | P1 | A | Title bar | Green "connected" chip with zero keys installed; no error/checking/offline state; colour-only | `MainWindow.xaml.cs:851-880`; `AppSettings.cs:41` | §5.2 `IReadinessService`, 6 states, shape-coded | M |
| **UX-06** | P1 | — | Documents | Cards not focusable (`Border`+`Tapped`, `SelectionMode="None"`); icon buttons unnamed | `HistoryPage.xaml:245-248,303,310` | §8.2 | S |
| **UX-07** | P1 | G | Global | Three-column layout unreachable at 1366×768 @125 % (1093 < 1280); min height 640 exceeds usable height | single `AdaptiveTrigger`; `MainWindow.xaml.cs:39-40` | §4.2 three tiers at 1001 / 1380 | M |
| **UX-08** | P1 | — | Onboarding | `Ignorer pour l'instant` does not persist → modal every launch | `FirstRunDialog.xaml.cs:47` | §14.2 one line | S |
| **UX-09** | P1 | — | Onboarding | No in-wizard key test; no sample content exists anywhere | `FirstRunDialog.xaml`; codebase | §14.2 samples + inline test | M |
| **UX-10** | P1 | D/E | Errors | Malformed key → HTTP 400 → "Vérifiez votre connexion Internet" — sends the user to debug Wi-Fi | `ErrorMessageTranslator.cs` | §9.2 dedicated auth-400 branch | S |
| **UX-11** | P1 | — | Generation | `ResultViewModel` singleton silently overwritten on cross-page navigation | DI registration | §3.2 transient per workspace; interim notice | M |
| **UX-12** | P1 | D | Export | Raw English .NET exception on locked file | `ResultViewModel.cs:571` | §9.2 + **Enregistrer sous…** | S |
| **UX-13** | P1 | A | Settings | Anthropic selectable with key field and test button; no adapter exists | AI tab | Hide, or disable with `Bientôt disponible` | S |
| **UX-14** | P2 | D | Assistant | Git patch rendered to teachers: `Cascadia Mono`, `+`/`−`, strikethrough, literal `@@ … @@` | `AssistantPane.xaml:30-73` | §7.1 `ChangeProposal` | M |
| **UX-15** | P2 | D | Settings | 1 066 XAML / 1 334 VM lines; 6 tabs; six provider panels at once; 12 leaked dev terms | §12.2 | Simple / Advanced split; one provider card | M |
| **UX-16** | P2 | A | Global | Five placebo controls: Language, Accent, Telemetry, Shortcut editor, Anthropic | §4.1 | Hide until functional | S |
| **UX-17** | P2 | F | Quiz | Eleven divergences incl. duplicated status bar rendered twice on one screen | `QuizPage.xaml:211,278` and §8 | §3.2 page unification (or conform + shared dictionary) | S→M |
| **UX-18** | P2 | B | Preview | Read-aloud cannot be stopped; second click restarts from the beginning | `PreviewHost.xaml.cs:574-577` | §6.5 play/stop toggle | S |
| **UX-19** | P2 | E | Forms | Non-PDF drop silently ignored; no remove; no size limit; no scanned-PDF detection | `PdfDropZone.xaml.cs:65` | §6.6 nine-state table | S |
| **UX-20** | P2 | F | Évaluation | Three input paradigms synchronised through a hidden `TextBox` mirror | `EvaluationPage.xaml:170-172`; `.cs:179-205` | `ChoiceChipGroup` + real collection binding | M |
| **UX-21** | P2 | — | Évaluation | Topic suggestions hardcoded, not filtered by subject — maths teachers offered grammar | `EvaluationPage.xaml.cs:63-64` | Filter; empty-state prompt (§12.3) | S |
| **UX-22** | P2 | — | A11y | 16 hard-coded hex values override High Contrast, incl. status pills and both scrims | §2.5 | Semantic tokens + HC dictionary | S |
| **UX-23** | P2 | — | A11y | No `LiveSetting` on any `InfoBar`; validation and export outcomes silent to screen readers | `FormInfoBar`, `ExportSuccessInfoBar` | §10.3 | S |
| **UX-24** | P2 | B | Documents | No export, print or duplicate from the list | `HistoryPage.xaml` | §8.2 overflow + context menu | M |
| **UX-25** | P3 | D | Settings | Header renders the literal string `placeholder` | `SettingsPage.xaml:71` | Bind to version or delete | S |
| **UX-26** | P3 | A | Fiche | `AutoSuggestBox` with `QueryIcon="Find"` promises a search that does not exist | `FichePage.xaml:253` | §14.8 wire to FTS5, or plain `TextBox` | S / M |
| **UX-27** | P3 | — | Fiche | Duration is spinner-only; French slots are 30/45/55 | `FichePage.xaml:268-273` | §6.3 quick-pick chips — **flag for user test** | S |
| **UX-28** | P3 | — | Fiche | 129 lines of dead style, zero references project-wide | `FichePage.xaml:36-95,98-166` | Delete, or finish the feature and decide | S |
| **UX-29** | P3 | — | Shell | Status bar permanently advertises shortcuts instead of document state | `MainWindow.xaml:261` | §5.3 notification area | S |
| **UX-30** | P3 | — | Motion | No `AnimationsEnabled` gate on shimmer, pulse, or transitions | global | §2.6 | S |
| **UX-31** | P3 | — | Preview | Document renders as a card, not paper; no page-break markers | `PreviewHost.xaml` | §14.6 | M |
| **UX-32** | P3 | A | Global | No `.resw` anywhere; every string hardcoded; Language selector inert | `SettingsViewModel.cs:259,659` | Extract **after** the §12 rewrites, not before | L |
| **UX-33** | P3 | — | Global | Zero `TabIndex` / `IsTabStop` / `XYFocus*` in the solution; no region model | global | §10.1 + `F6` cycling | M |
| **UX-34** | P3 | E | Errors | Offline reported only after the full 150 s Polly budget | resilience config | §14.7 early detection | S |

**Class distribution:** A (state dishonesty) 6 · B (concealment) 5 · C (destruction) 1 · D (dev artefacts) 5 · E (silent failure) 5 · F (drift) 3 · G (hardware) 1 · a11y/other 8.

---

# 16. Quarantine — claims that must be verified on real hardware

Everything in §1–§15 carries a `file:line` or is a design proposal. The following are **inferred, not observed**. They are stated as findings elsewhere in this report because the arithmetic and the code both point one way, but none of them has been seen on a screen. Do not treat them as settled, and do not let anyone else cite them as settled.

| # | Claim | Protocol | What it decides |
|:--|:--|:--|:--|
| Q1 | **The primary layout never renders on target hardware** (UX-07). 1366 ÷ 1.25 = 1093 DIP < 1280 | Real academy laptop, or a VM configured to exactly 1366×768 @125 %. Open all three generation pages. Measure the actual `ActualWidth` of the root grid. | Whether §4.2 is a P1 restructure or a P3 tuning exercise |
| Q2 | **Fixed-width text containers clip at 200 % scaling** (§10.6) | Windows → Accessibility → Text size 200 %. Inspect `SettingsPage.xaml:864-888` (four cards at `Width="132"`), `HistoryPage.xaml:215-221` (stat pills), every step card. Screenshot every page. | Scope of the `MinWidth` conversion |
| Q3 | **Export is completely unreachable on touch**, not merely awkward | Surface or TBI, **mouse physically disconnected**, touch only. Generate a document, then attempt to export it. | Whether UX-03 is severe friction or a total dead end — this changes its priority relative to UX-01 |
| Q4 | **Narrator cannot complete the primary path** | `Win+Ctrl+Enter`. Full run: navigate → fill the form → generate → trigger an error → export → delete. Transcribe every announcement. | Confirms the §10.2/§10.3 scope and validates the announcement script in Appendix C |
| Q5 | **WebView2 absence produces a permanent silent blank** (UX-04) | Clean Windows 10 1809 VM with no Evergreen Runtime installed. Launch, generate. | Confirms severity; also tells you whether generation itself still succeeds (it should — export goes through the same component, so check both) |
| Q6 | **Duration quick-picks are an improvement** (UX-27) | Task 2 of the user test below | This is a preference claim, not an evidence claim. Do not ship it as settled design. |
| Q7 | **Teachers write pupil names into the instructions field** (§13.1) | Task 4 of the user test; and a one-question survey of any five teachers | Determines whether §13.2 Layer 3 (name detection) is a differentiator or over-engineering |
| Q8 | **Streaming narration reduces perceived wait** (§9.4) | A/B in the user test if feasible; otherwise ship and observe abandonment | Low stakes either way; the narration is cheap |

## 16.1 The gap that outweighs all eight

**No teacher has ever been observed using this application.** There are no usability sessions, no recordings, no surveys, no user-submitted bug reports anywhere in the repository. Every judgement in this report — and in every prior audit — is expert inference from source code.

Three forty-five-minute sessions would settle more than another ten thousand words of analysis, including several of mine.

**Protocol.** Three teachers: one primary (CE2–CM2), one collège, one who self-describes as *« pas à l'aise avec l'informatique »*. Fresh install. No key configured. No help offered, no matter how uncomfortable the silence gets. Think-aloud. Record screen and audio.

| # | Task (given verbatim) | What it measures |
|:--|:--|:--|
| 1 | *« Installez l'application pour pouvoir l'utiliser. »* | Onboarding, the key wall, skip persistence (UX-08, UX-09) |
| 2 | *« Créez une fiche de séance sur un sujet que vous enseignez vraiment. »* | Form comprehension, the step model, duration input (UX-27) |
| 3 | *« Imprimez la version élève. »* | **The export defect. Expect the largest failure here** (UX-03) |
| 4 | *« Rendez l'exercice 2 plus facile pour un élève en difficulté. »* | Assistant discovery, diff comprehension (UX-14), and whether a pupil's name gets typed (Q7) |
| 5 | *« Retrouvez ce document demain et réimprimez-le. »* | Library model, keyboard/touch reachability (UX-06, UX-24) |
| 6 | *« Passez l'application en mode sombre. »* | Settings navigability (UX-15) |

Record for each: time to completion, number of assists required, and — the highest-signal metric available — **every moment the participant says some version of *« c'est moi qui ai fait une bêtise ? »***. Self-blame is the reliable marker of a design failure, because users attribute interface faults to themselves with extraordinary consistency. Count those utterances. They are your defect list, ranked.

**Success bar: task 3 completed unaided by 3/3 participants.** If that fails after the Sprint 1 command bar ships, the export design is still wrong and everything downstream waits.

---

# 17. Roadmap

Sequenced so that each sprint leaves the product shippable, and so that the design system lands before the surfaces that depend on it.

## Sprint 1 — Stop the harm (≈3 days)
`UX-01` `UX-02` (dialog) `UX-03` `UX-04` `UX-12` `UX-13` `UX-25`

Nothing here is architectural. All of it is deletion, honest copy, and one new component (`AppCommandBar`).

**Exit criteria:** No irreversible data loss. No unreachable export. No silent blank preview. No false privacy claim. No dead-end provider. No literal `placeholder` in shipped UI.

## Sprint 2 — Tell the truth about state (≈5 days)
`UX-05` `IReadinessService` · `UX-08` · `UX-09` · `UX-10` · `UX-06` · `UX-16` · `UX-23` · `UX-34`

**Exit criteria:** The status surface never asserts anything it cannot prove. A first-time user reaches a printed document **before** being asked for a credential. The library is operable by keyboard and screen reader. Zero placebo controls visible anywhere.

## Sprint 3 — Install the design system (≈6 days)
`Tokens.xaml` / `Typography.xaml` / `Controls.xaml` / `Templates.xaml` / `Motion.xaml` · `StepCard` · `EmptyState` · `FieldGroup` · `StatusChip` · `UndoToast` · `UX-02` (undo) · `UX-17` · `UX-22` · `UX-28` · `UX-29` · `UX-30`

This is the sprint that stops the bleeding permanently rather than symptomatically. It is also the one most likely to be deferred under pressure. Do not defer it: every subsequent sprint is 30–40 % cheaper once it exists, and every sprint before it accrues more duplication to unwind.

**Exit criteria:** Zero duplicated style definitions. Zero hard-coded hex outside theme dictionaries (enforced by the CI grep in §18). The three generation pages are visually indistinguishable in chrome. High Contrast Black renders correctly on every surface.

## Sprint 4 — Reduce cognitive load (≈6 days)
`UX-14` `ChangeProposal` · `UX-15` Simple/Advanced · `UX-19` · `UX-20` · `UX-21` · `UX-18` · `UX-26` · `UX-27` · §12 terminology sweep

**Exit criteria:** No Git patch anywhere in the product. Settings opens on three teacher-relevant tabs. Every input has an explicit response to every input it can receive, including the wrong ones. No term from the §12.2 blacklist appears outside Advanced.

## Sprint 5 — Fit the hardware and unify the pages (≈6 days)
`UX-07` three tiers · `UX-11` · `UX-33` keyboard region model + `F6` · §3.2 page unification · `UX-24` · `UX-31` · touch pass

**Exit criteria:** Validated at 1093×614 DIP (Q1). Validated with the mouse physically disconnected (Q3). `FichePage` / `EvaluationPage` / `QuizPage` no longer exist as separate files. Class F cannot recur.

## Sprint 6 — Signature and infrastructure (≈5 days)
§14.1 `Créer la suite` · §14.3 Élève/Corrigé moment · §14.4 DYS presets · §14.6 page-break markers · §13.2 Layer 3 name detection · `UX-32` `.resw` extraction using the corrected copy

**Exit criteria:** One interaction a teacher would describe to a colleague. Localisation scaffolding in place, extracted **once**, from final strings.

## Sequencing constraints

- §3.2 unification **must** precede §14.1 `Créer la suite`. The feature is impossible before it.
- The design system (Sprint 3) **should** precede Sprint 4 and **must** precede Sprint 5, or the unified page will be built on tokens that do not exist yet.
- `UX-32` (`.resw`) **must** come last. Extracting strings before the §12 rewrites means migrating text you are about to delete.
- Q1 (the breakpoint measurement) **must** happen before Sprint 5 begins, and ideally in week one — it is a thirty-minute task that determines several days of work.

---

# 18. Definition of done

## 18.1 Per pull request — blocking

- [ ] No literal spacing value in page XAML; `{StaticResource Space*}` only
- [ ] No `FontSize` literal; typography styles only
- [ ] No hard-coded hex outside a `ThemeDictionary`
- [ ] Every interactive element has an `AutomationProperties.Name`
- [ ] Every icon-only control has **both** a tooltip and an accessible name, and they are not the same string
- [ ] No functionality reachable only via hover (L3)
- [ ] Every new surface implements: empty, loading, ready, **error**
- [ ] Every error names a cause **and** ships an action button (§9.1)
- [ ] Every destructive action is confirmed or reversible (L2)
- [ ] Every `Storyboard.Begin()` is gated on `AnimationsEnabled`
- [ ] No status is derived from a stored setting; only from a verified source (L1)
- [ ] No term from the §12.2 blacklist appears outside Advanced
- [ ] Verified at 1093×614 DIP
- [ ] Verified at 200 % text scaling
- [ ] Verified with the mouse disconnected
- [ ] Verified in High Contrast Black
- [ ] Narrator pass on the primary path of the changed surface

## 18.2 CI enforcement

Three greps, all cheap, all catching entire defect classes:

```bash
# 1 — hard-coded colour outside theme dictionaries
grep -rn '#[0-9A-Fa-f]\{6,8\}' src/**/Views/*.xaml && exit 1

# 2 — inline font sizes
grep -rn 'FontSize="[0-9]' src/**/*.xaml && exit 1

# 3 — the code font, permitted in at most one place
test "$(grep -rn 'Cascadia' src --include=*.xaml | wc -l)" -le 1 || exit 1
```

Add a fourth once §2.2 lands: a regex for `Margin="` / `Padding="` containing digits in any file under `Views/`.

## 18.3 Release-blocking

- [ ] Zero placebo controls visible
- [ ] Privacy copy matches the actual data flow, verified against a network capture
- [ ] Export reachable within five seconds by an unaided first-time user (Q3, user-test task 3)
- [ ] The full app is operable by keyboard alone
- [ ] The full app is operable by touch alone
- [ ] The three generation surfaces are indistinguishable in chrome
- [ ] Zero duplicated style definitions
- [ ] Zero dead styles
- [ ] Every string in the §12.3 copy deck is shipped verbatim or deliberately superseded

---

# 19. Closing

PROFstudio is not a badly built application with a bad interface. It is a **well-built application with an unowned interface**, and those fail in a characteristic and diagnosable way: the engineering keeps improving while the surface accumulates untended contradictions, until the product feels markedly less trustworthy than it actually is.

The evidence for that reading is concrete, and it recurs with almost comic regularity. The team wrote `ErrorMessageTranslator`, which turns HTTP 429 into *« Limite d'utilisation atteinte auprès du service IA. Veuillez patienter une minute avant de réessayer »* — better error copy than most commercial software ships — and then rendered it as inert grey text in a status bar with no button. They built real OpenXML export, and hid it behind a three-second timer. They built a genuinely private local storage layer, and described it with a sentence that is not true. They built a real Myers diff with apply-in-place and undo, and presented it as a Git patch to a CP teacher. They built a three-column layout that their own target hardware cannot display.

In every one of those cases the hard ninety-five percent was done well and the last five percent was left unowned. That five percent is what this document specifies, and it is why the report is long in proportion to the work: the fixes are individually small and collectively decisive.

The order is not negotiable. Three defects stop active harm this week — a false privacy claim, an unguarded delete, an invisible export. A readiness service and a notification system make the product stop lying about itself. A design system, which does not currently exist in any form, stops the drift from recurring. Then the page unification, which deletes two files and an entire defect class with them.

And one afternoon watching three teachers try to print a document will teach the team more than any further analysis, including this one.

---

# Appendix A — Structural debt, included only where it causes a UX defect

Architecture is out of scope except where it is the proximate cause of something in §15.

| Item | Scale | UX consequence |
|:--|:--|:--|
| `MainWindow.xaml.cs` | ~1 436 lines: navigation, theme, shortcuts, geometry, AI chip, first run, help | The chip's state logic (UX-05) is buried in a god object, which is why it was never revisited when credentials moved to the vault |
| Reflection command dispatch (`TryExecuteCommand`) | invocation by string name | **Direct cause** of the `CancelCommand` / `CancelGenerationCommand` divergence (§8) — the compiler cannot catch a rename |
| `SettingsViewModel.cs` | ~1 334 lines, six tabs, one class | Makes the Simple/Advanced split (UX-15) more expensive than it should be |
| Singleton `ResultViewModel` | shared across three pages | **Direct cause** of UX-11, and the reason §3.2 unification is worth its cost |
| `{Binding}` vs `{x:Bind}` | History compiled, generation pages reflection-based | No compile-time binding errors; the hidden `TopicsMirror` (UX-20) is a workaround for exactly this gap |
| Triplicated styles | three byte-identical copies | Any visual fix must be made three times, and demonstrably has not been (UX-17) |
| Dead styles | 129 lines, zero references | Abandoned design intent in the first file a new contributor opens (UX-28) |
| Empty `catch` on WebView2 init | one block | **Direct cause** of UX-04, the single worst silent failure in the product |
| No `.resw` | every string hardcoded | Makes the Language selector inert (UX-16) and localisation an `L` |

Nothing proposed in this report violates the project's stated architecture: the four-layer boundary (`Core → Infrastructure → App/Services → App/Presentation`), no UI or IO in `Core`, immutable records across thread boundaries, French user-facing copy with English code identifiers. Every new component named here lives in `App/Presentation`, and `IReadinessService` lives in `App/Services`.

---

# Appendix B — Component build order

Ordered by what each one unblocks, not by size.

| # | Component | Unblocks | Effort |
|:--|:--|:--|:--|
| 1 | `Tokens.xaml` + `Typography.xaml` | everything downstream | S |
| 2 | `AppCommandBar` | UX-03, UX-18, §14.3, §14.5 | S |
| 3 | `DestructiveConfirm` | UX-02 (Sprint 1 fallback) | S |
| 4 | `ErrorState` | UX-04, UX-12, §14.7 | S |
| 5 | `StatusChip` + `IReadinessService` | UX-05, settings pills, generate-disabled reason | M |
| 6 | `NotificationHost` | §5.3, all of §9 | S |
| 7 | `UndoToast` | UX-02 (real fix) | S |
| 8 | `DocumentCard` | UX-06, UX-24, §14.5 | M |
| 9 | `EmptyState` | four current sites | S |
| 10 | `FieldGroup` | §6.3 validation, all forms | M |
| 11 | `StepCard` | UX-17, §3.2 unification | M |
| 12 | `ChangeProposal` | UX-14 | M |
| 13 | `ChoiceChipGroup` | UX-20, §6.3 duration | S |
| 14 | `PdfDropZone` rebuild | UX-19 | S |

Items 1–4 are Sprint 1. Items 5–7 are Sprint 2. Items 8–11 are Sprint 3.

---

# Appendix C — Keyboard map and announcement script

## C.1 Region model

`F6` / `Shift+F6` cycle five regions. On entry, announce the region and position: *« Formulaire, étape 1 sur 4 »*.

```
① Navigation  →  ② Formulaire  →  ③ Barre de commandes
                                        ↓
                  ⑤ Assistant   ←  ④ Aperçu
```

Within a region, `Tab` moves linearly. `Escape` returns focus to the region start; `Escape` at the region start closes any overlay; `Escape` during generation cancels.

## C.2 Accelerators

| Key | Action | Status |
|:--|:--|:--|
| `Ctrl+G` | Générer | exists |
| `Escape` | Annuler / fermer | exists |
| `Ctrl+B` | Assistant | exists |
| `Ctrl+N` | Nouveau | exists |
| `Ctrl+Shift+E` / `Ctrl+Shift+W` | Exporter PDF / Word | exists |
| `Ctrl+P` | Imprimer | exists |
| `Ctrl+F` | Rechercher | exists |
| `Ctrl+Z` | Annuler la modification / la suppression | exists (extend to delete) |
| `Ctrl+1…4` | Sections | exists |
| `F1` | Raccourcis | exists |
| **`F6` / `Shift+F6`** | **Changer de zone** | **add** |
| **`Ctrl+Shift+V`** | **Basculer élève / corrigé** | **add** |
| **`Ctrl+0`** | **Zoom 100 %** | **add** |
| **`Delete`** | **Supprimer l'élément sélectionné (→ toast + annuler)** | **add** |
| **`Enter` / `Espace`** | **Ouvrir / basculer favori dans la bibliothèque** | **add** |
| **`Menu` / `Shift+F10`** | **Menu contextuel de la carte** | **add** |

The existing accelerator set is genuinely good. What is missing is not shortcuts but *structure* — a way to move between regions without tabbing through thirty controls.

## C.3 Announcement script

| Event | Announcement | Politeness |
|:--|:--|:--|
| Generation starts | `Rédaction en cours` | Polite |
| Phase change | `Rédaction des exercices` | Polite |
| Complete | `Fiche « {titre} » prête. {n} sections.` | Assertive |
| Failure | full translated message + the action button's label | Assertive |
| Validation on submit | `{n} champs à compléter. Premier champ : {label}.` | Assertive |
| Export complete | `Document exporté : {fichier}` | Polite |
| Export failure | full message + action | Assertive |
| Assistant change applied | `Modification appliquée. Annuler avec Contrôle Z.` | Polite |
| Document deleted | `Document supprimé. Récupérable pendant 30 jours. Annuler avec Contrôle Z.` | Assertive |
| Readiness change | `Assistant IA — {état}` | Polite |

---

# Appendix D — Terminology quick reference

For pasting into a review checklist. Left column must never appear in a user-facing surface outside Advanced.

`API key` · `clé d'API` · `Global Provider` · `fournisseur` · `routage` · `matrice de routage` · `température` · `jetons` · `max tokens` · `Project ID` · `Region` · `endpoint` · `URL de base` · `proxy` · `DPAPI` · `Credential Locker` · `FutureAccessList` · `WinRT` · `Serilog` · `TLS 1.3` · `/models` · `LLM` · `modèle` (as a noun the user must reason about) · `prompt` · `token` · `cache` · `base de données` · `historique` (as a page name) · `placeholder`

Canonical replacements:

| Concept | Always | Never |
|:--|:--|:--|
| The AI | `l'assistant` | `l'IA`, `le modèle`, `le fournisseur` |
| The credential | `clé de connexion` *(with `(API key)` as a parenthetical only where it must match Google's own page)* | `clé d'API` alone, `token`, `secret` |
| Producing a document | `rédiger` in prose; `Générer` on the button (established, keep it) | `générer` in prose |
| The output | `votre fiche` / `votre évaluation` / `votre quiz` | `le document`, `le résultat`, `la sortie` |
| The library | `vos documents` | `historique`, `cache` |
| The answer version | `le corrigé` | `la version enseignant`, `la solution` |
| Key storage | `protégée par le coffre-fort de Windows` | `chiffrement DPAPI via le Credential Locker` |
| Folder permission | `accès autorisé à ce dossier` | `FutureAccessList WinRT` |

---

# Appendix E — Copy deck index

Every French string proposed in this report, with its section, for a single extraction pass when `UX-32` lands.

| Surface | Section |
|:--|:--|
| Onboarding privacy card | §12.3 |
| Onboarding step 3 (samples) | §14.2 |
| Onboarding step 4 (key + skip) | §12.3, §14.2 |
| Profile flyout | §12.3 |
| Settings privacy panel | §12.3 |
| Readiness chip, six states | §5.2 |
| Generate disabled tooltip | §12.3 |
| Error catalogue, all thirteen conditions | §9.2 |
| Progress narration, five phases | §9.4 |
| Delete confirmation + undo toast | §12.3, §8.3 |
| WebView2 unavailable | §6.5, §12.3 |
| PDF drop zone, nine states | §6.6 |
| Offline state | §14.7 |
| Evaluation empty suggestions | §12.3 |
| Assistant change proposal labels | §7.1 |
| Suggestion chips, per document kind | §7.3 |
| DYS preset descriptions | §14.4 |
| Name-detection dialog | §13.2 |

---

# Appendix F — Corrections to prior audits

This report supersedes all previous audits. Their substantive errors, corrected:

| Prior claim | Verdict | Evidence |
|:--|:--|:--|
| *"The assistant diff view is one of the strongest UI surfaces in the app."* | **Wrong for this audience.** Well engineered, badly targeted. `Cascadia Mono`, `+`/`−` sigils, strikethrough and a literal `@@ … @@` are being rendered to primary-school teachers. Engineering quality and design fitness are different axes; the audit conflated them. | `AssistantPane.xaml:30-73` |
| *"Replace `Default` with `Dark` in `ThemeDictionaries`."* | **Withdrawn.** `Default` **is** the correct WinUI 3 / UWP key for the dark fallback. All three dictionaries are present and correctly keyed. Acting on this recommendation would have broken theming. | `App.xaml:12-37` |
| *"There is no way to defer API key setup."* | **Partly wrong, and the reality is worse.** `Ignorer pour l'instant` exists — but does not persist, so the dialog returns on every launch. The user *can* defer; they are simply punished for it, daily. | `FirstRunDialog.xaml:8`; `.cs:47` |
| *"The preview toolbar is hidden by default."* | **Imprecise, and the reality is worse.** It appears on generation completion, then disappears after three seconds of pointer idle. Showing a control and then removing it is more damaging than never showing it, because it destroys the user's belief that they understand the interface. | `PreviewHost.xaml.cs:265-306` |
| *"The `NumberBox` for duration is a good choice."* / *"…is poor ergonomics."* (two audits, opposite conclusions) | **Both unevidenced.** Retained as UX-27 at P3 and quarantined as Q6, to be settled by task 2 of the user test rather than by assertion. | `FichePage.xaml:268-273` |
| *"Roughly 80 % of teachers will abandon at the API key step."* | **Deleted.** No usage data of any kind exists in the repository. There is no telemetry system — indeed the telemetry toggle is one of the five placebo controls. A fabricated number in an audit is worse than no number, because it survives into planning documents. | §4.1, §16 |
| *"High Contrast support is complete."* | **Wrong.** Sixteen hard-coded hex values, including every status pill in Settings and both modal scrims, override the system palette entirely. | §2.5, UX-22 |
| *"Settings exposes five provider cards."* | **Wrong count.** Six: Gemini, OpenAI, Anthropic, Ollama, Vertex, Vercel — one of which has no adapter behind it. | `SettingsPage.xaml` |
| *"The status bar provides useful contextual feedback."* | **Wrong.** It renders a permanent, static shortcut string over prime persistent real estate, and surfaces errors as inert text with no affordance — including errors whose own copy instructs the user to navigate somewhere. | `MainWindow.xaml:261` |
| Not found by either prior audit | Privacy copy contradiction · WebView2 silent failure · read-aloud cannot be stopped · singleton result overwrite · malformed-key misdiagnosis as a network fault · the 1093 DIP breakpoint arithmetic · five placebo controls · silent PDF rejection · unfiltered evaluation suggestions · duplicated status bar on `QuizPage` · the literal `placeholder` string in Settings | §0, §4, §9, §13, §15 |