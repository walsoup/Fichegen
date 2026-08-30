# Teacher-Centered UI, UX, and Motion Review

**Application:** PROFstudio  
**Platform:** WinUI 3 desktop application  
**Review date:** 2026-08-28  
**Review type:** Full, read-only source review  
**Primary audience:** Teachers with little or no technical knowledge  
**Overall verdict:** Block

## Executive Summary

PROFstudio has a solid visual foundation and a generally understandable document-creation structure. The numbered creation flow, persistent generation action, progressive disclosure of advanced AI settings, and preview-specific states all support the intended teacher audience.

The main risks are not visual styling problems. They are gaps between what the interface promises and what the application actually does, along with inconsistent feedback during important operations. For a non-technical teacher, these gaps can make the application appear unreliable even when the underlying feature is functioning as implemented.

The highest-impact problems are:

1. Canceling a Save dialog may still create an exported file.
2. Assistant modes advertise document-generation behavior that is not implemented.
3. First-run onboarding can finish with a cloud provider that is not ready to use.
4. Generation, export, and history failures can be invisible or misleading.
5. Multiple generation operations can overlap and corrupt the visible busy or cancellation state.
6. Reduced-motion mode can leave the preview empty state invisible.
7. Several custom interactive surfaces lack complete keyboard, focus, and screen-reader behavior.
8. Localization is incomplete despite advertising French, English, and Arabic support.

These issues should be resolved before investing heavily in decorative animation or additional visual polish. Teachers need predictable outcomes, plain-language recovery, and confidence that clicking Cancel, Generate, Save, or Retry has one clear result.

## Audience Lens

This review assumes that the typical user:

- Is comfortable with ordinary desktop applications but not AI terminology.
- Does not know what an API key, provider, endpoint, model, token, or local server is.
- Wants to create teaching material quickly while working beside a browser, PDF, or grade book.
- May interpret an unexplained failure as personal error rather than a system problem.
- Benefits from familiar controls, explicit labels, visible progress, and clear next actions.
- Needs the application to preserve work and context when an operation fails.
- May use keyboard navigation, touch, high contrast, larger text, or reduced motion.

The review therefore prioritizes trust, task completion, error recovery, and accessibility over novelty or visual density.

## Scope

The review covered the application shell, creation workflows, preview, assistant, history, first-run experience, account flow, settings, export behavior, localization, keyboard interaction, motion, and perceived responsiveness.

Evidence included:

- Application XAML and code-behind.
- Relevant ViewModels and UI-facing services.
- Localization resources for French, English, and Arabic.
- Existing screenshots under `screens/`.
- Existing tests related to localization and application behavior.
- Repository guidance in `AGENTS.md` and `docs/WINDOWS_BLUEPRINT.md`.

No application code was modified during the review.

## Severity Definitions

| Severity | Meaning |
| --- | --- |
| Critical | The application violates an explicit user decision or risks an unexpected external effect. |
| High | A core task can fail, mislead the user, lose trust, or become inaccessible. |
| Medium | The issue meaningfully harms comprehension, efficiency, consistency, or recovery. |
| Low | The issue is primarily comfort or polish and does not normally block task completion. |

## Findings Overview

| ID | Severity | Area | Finding |
| --- | --- | --- | --- |
| F01 | Critical | Export | Canceling the Save picker may still create a file. |
| F02 | High | Assistant | Generate and Auto modes promise behavior they do not perform. |
| F03 | High | Onboarding | First-run can complete with an unusable cloud configuration. |
| F04 | High | Feedback | Important operational failures are not reliably visible. |
| F05 | High | Async state | Generation ownership is fragmented and permits races. |
| F06 | High | Recovery | Failed or canceled generation removes the visible preview. |
| F07 | High | Accessibility | Reduced-motion mode can make empty preview content invisible. |
| F08 | High | History | Searching can remove focus while the teacher is typing. |
| F09 | High | Interaction | Busy forms look disabled but remain keyboard-editable. |
| F10 | High | Performance | Preview changes can trigger duplicate WebView navigations. |
| F11 | High | Accessibility | Custom interactive surfaces lack native semantics or complete focus behavior. |
| F12 | High | Localization | French, English, and Arabic support is not end-to-end. |
| F13 | Medium | Validation | Generate is disabled before it can explain missing information. |
| F14 | Medium | Responsive layout | Minimum width prevents useful Windows Snap layouts. |
| F15 | Medium | Discoverability | Primary preview actions can move behind horizontal scrolling. |
| F16 | Medium | Accessibility | Settings controls lack explicit accessible label relationships. |
| F17 | Medium | Focus | Custom overlays do not consistently contain or restore focus. |
| F18 | Medium | Context | The shared preview does not clearly identify its document type. |
| F19 | Medium | Account flow | Authentication errors are remote from fields and actions remain repeatable while busy. |
| F20 | Medium | Keyboard | Shortcut documentation conflicts with implemented commands. |
| F21 | Medium | Progress | The generation step tracker does not reflect actual progress. |
| F22 | Medium | Export | Export lacks meaningful progress, cancellation, and execution gating. |
| F23 | Medium | Assistant | Diff actions do not visibly resolve the proposed change. |
| F24 | Medium | History | History rendering weakens virtualization and duplicates refresh work. |
| F25 | Medium | Onboarding | First-run step changes are not announced or focused. |
| F26 | Medium | PDF input | PDF picker and drag feedback have recovery gaps. |
| F27 | Low | Ergonomics | Frequently used controls are smaller than comfortable touch targets. |

## Detailed Findings

### F01. Canceling the Save picker may still create a file

**Severity:** Critical  
**Area:** Export and user trust  
**Evidence:** `src/FicheGen.App/Services/ExportWorkflowService.cs:140-168`

When the Save picker returns `null`, the workflow can fall through to an automatic Documents-folder fallback and create a file anyway.

For a teacher, Cancel has one expected meaning: do not save. Creating a file after cancellation violates an explicit decision and makes later success feedback confusing. It can also leave the teacher unsure where the file was placed.

**Recommendation:**

- Return immediately when the user cancels the picker.
- Use a fallback only when the picker cannot be launched, not when the user declines it.
- Explain the fallback location before writing a file there.
- Ensure success feedback names or opens the actual destination.

### F02. Assistant Generate and Auto modes promise unsupported behavior

**Severity:** High  
**Area:** Feature comprehension and trust  
**Evidence:**

- `src/FicheGen.App/Strings/fr-FR/Resources.resw:846-865`
- `src/FicheGen.App/ViewModels/AssistantViewModel.cs:283-360`

The interface describes a Generate mode that creates a new teaching document and an Auto mode that detects generation intent. The implementation only edits or answers questions about an existing document. Other requests return a no-document response.

This mismatch is especially damaging for a non-technical user because the interface teaches a workflow that cannot succeed. The teacher is likely to retry with different wording rather than understand that the mode is not implemented.

**Recommendation:**

- Implement assistant-driven document creation, or remove the unsupported modes.
- If the assistant is limited to the current document, describe it in those terms.
- Disable unavailable actions before interaction and explain what is required.
- Avoid using Auto unless the system can reliably explain what action it selected.

### F03. First-run can complete with an unusable cloud configuration

**Severity:** High  
**Area:** Onboarding and activation  
**Evidence:**

- `src/FicheGen.App/Views/Controls/FirstRunDialog.xaml:178-198`
- `src/FicheGen.App/Views/Controls/FirstRunDialog.xaml.cs:23-30`
- `src/FicheGen.App/Services/ReadinessService.cs:113-121`
- `supabase/functions/chat/index.ts:72-103`

Cloud is preselected and described as requiring no setup, but readiness requires a Supabase access token and the server rejects unauthenticated calls.

The first-run experience can therefore end with a configuration that appears complete but cannot generate content. This creates a poor first impression and requires the teacher to diagnose a technical authentication condition.

**Recommendation:**

- Include sign-in within onboarding when cloud is selected.
- Prevent completion until the selected provider is ready.
- Offer a clearly labeled path to continue without cloud.
- Use plain language such as "Sign in to use online generation" rather than provider terminology.
- Confirm readiness with a short success state before closing onboarding.

### F04. Important operational failures are not reliably visible

**Severity:** High  
**Area:** Feedback and error recovery  
**Evidence:**

- `src/FicheGen.App/ViewModels/FicheFormViewModel.cs:340-350`
- `src/FicheGen.App/ViewModels/EvaluationViewModel.cs:336-346`
- `src/FicheGen.App/ViewModels/QuizViewModel.cs:353-363`
- `src/FicheGen.App/ViewModels/ResultViewModel.cs:673-729`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml:223-244`
- `src/FicheGen.App/ViewModels/HistoryViewModel.cs:317-342`
- `src/FicheGen.App/ViewModels/HistoryViewModel.cs:464-477`

Generation failures update ViewModel status strings that the generator pages do not visibly bind. Export failures use a similar status path while the preview emphasizes success. History failures can appear as an ordinary empty state.

For non-technical users, silence after an operation usually looks like a frozen or broken application. Showing an empty history when loading failed can also imply that saved work was lost.

**Recommendation:**

- Provide one consistent, visible operation status surface.
- Distinguish error, cancellation, warning, and success visually and semantically.
- State what happened in plain language.
- Provide a concrete next action such as Retry, Open settings, or Choose another folder.
- Keep history load errors separate from a genuine empty history.
- Announce important state changes to assistive technology.

### F05. Generation ownership is fragmented and permits races

**Severity:** High  
**Area:** Cancellation, navigation, and async state  
**Evidence:**

- `src/FicheGen.App/MainWindow.Navigation.cs:47-70`
- `src/FicheGen.App/ViewModels/FicheFormViewModel.cs:268-276`
- `src/FicheGen.App/ViewModels/EvaluationViewModel.cs:263-271`
- `src/FicheGen.App/ViewModels/QuizViewModel.cs:271-279`
- `src/FicheGen.App/ViewModels/ResultViewModel.cs:250-260`
- `src/FicheGen.App/ViewModels/FicheFormViewModel.cs:352-360`
- `src/FicheGen.App/ViewModels/EvaluationViewModel.cs:348-356`
- `src/FicheGen.App/ViewModels/QuizViewModel.cs:365-373`
- `src/FicheGen.App/MainWindow.Accelerators.cs:60-65`

Each generator owns a separate cancellation token. Navigation does not cancel active work, replacing a result does not stop the older request, and an older operation can clear the shared busy state used by a newer operation.

A teacher can start generation, navigate elsewhere, start another task, and receive inconsistent progress or cancellation behavior. This is difficult to explain and can produce a strong impression that the application is unstable.

**Recommendation:**

- Use one application-level generation coordinator.
- Define whether navigation cancels, backgrounds, or blocks active generation.
- Cancel replaced operations explicitly.
- Allow only the current operation to update shared progress and completion state.
- Make Escape target the active operation regardless of which page is visible.

### F06. Failed or canceled generation removes the visible preview

**Severity:** High  
**Area:** Recovery and preservation of context  
**Evidence:**

- `src/FicheGen.App/ViewModels/ResultViewModel.cs:250-257`
- `src/FicheGen.App/ViewModels/FicheFormViewModel.cs:340-350`
- `src/FicheGen.App/Views/Controls/CreationWorkspace.xaml:33-38`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:182-196`

Starting a new operation clears the current HTML. If the operation fails or is canceled, the preview can switch to an empty state even though the previous document still exists in application state.

The previous document is useful work and an important source of reassurance. Removing it before replacement succeeds makes cancellation feel destructive.

**Recommendation:**

- Keep the previous rendered document visible until a replacement succeeds.
- Overlay non-blocking progress rather than replacing the entire preview.
- Clearly state when the previous version is still displayed.
- Offer Retry after failure.
- Treat cancellation as a return to the prior stable state, not as an empty result.

### F07. Reduced-motion mode can make empty preview content invisible

**Severity:** High  
**Area:** Accessibility and motion  
**Evidence:**

- `src/FicheGen.App/Views/Controls/PreviewHost.xaml:287-297`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:368-378`

The empty-preview content begins at zero opacity. When animations are disabled, the storyboard is stopped without restoring the final opacity and transform values.

Users who disable animation can therefore lose essential guidance. Reduced motion must remove movement, not remove content.

**Recommendation:**

- Explicitly apply every animation's final visual state when animation is disabled.
- Test reduced motion as a separate behavior, not only as a skipped storyboard.
- Add regression coverage for visibility with animations disabled.

### F08. History search can remove focus while the teacher is typing

**Severity:** High  
**Area:** Search interaction and focus stability  
**Evidence:**

- `src/FicheGen.App/Views/HistoryPage.xaml:143-170`
- `src/FicheGen.App/ViewModels/HistoryViewModel.cs:348-375`
- `src/FicheGen.App/ViewModels/HistoryViewModel.cs:417-425`

The search and filter area collapses whenever history enters its loading state. A debounced search starts loading after a short pause, which can make the focused search box disappear while the teacher is entering a query.

This creates a disruptive layout jump and may interrupt typing. Search controls should remain stable while only the result set refreshes.

**Recommendation:**

- Keep search and filters visible during refresh.
- Show progress within the results region.
- Preserve focus, caret position, and typed text.
- Avoid disabling filtering unless interaction would corrupt state.

### F09. Busy forms look disabled but remain keyboard-editable

**Severity:** High  
**Area:** Interaction state and keyboard behavior  
**Evidence:**

- `src/FicheGen.App/Views/Controls/CreationWorkspace.xaml.cs:55-73`
- `src/FicheGen.App/MainWindow.Accelerators.cs:36-72`

During generation, the form is faded and pointer interaction is disabled, but controls remain enabled for keyboard interaction. Escape attempts to cancel before respecting editable-field focus.

Visual state and actual behavior do not match. A keyboard user can edit a form that appears locked, and pressing Escape while editing may cancel generation unexpectedly.

**Recommendation:**

- Disable the form semantically with `IsEnabled`, not only pointer hit testing.
- Keep the explicit Cancel action enabled and focusable.
- Resolve editable-control Escape behavior before invoking global cancellation.
- Ensure the disabled state is conveyed by UI Automation.

### F10. Preview changes can trigger duplicate WebView navigations

**Severity:** High  
**Area:** Perceived performance and visual stability  
**Evidence:**

- `src/FicheGen.App/ViewModels/ResultViewModel.cs:138-142`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:172-181`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:462-488`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:932-951`

`CurrentHtml` also raises `PreviewHtml`, and the view handles both changes in the same way. Each update can perform a full WebView navigation without sequencing or version protection. Style changes can add further work.

The likely user-visible consequences are blank flashes, delayed updates, stale completion races, and inconsistent ready states. These effects were not runtime-verified, but the duplicate work is present in source.

**Recommendation:**

- Choose one canonical preview-content notification.
- Version or cancel render requests so stale completions cannot win.
- Mark the preview ready only after successful navigation completion.
- Apply safe style-only changes without full document replacement where possible.

### F11. Custom interactive surfaces lack complete native semantics and focus behavior

**Severity:** High  
**Area:** Accessibility  
**Evidence:**

- `src/FicheGen.App/Views/HistoryPage.xaml:262-271`
- `src/FicheGen.App/Views/HistoryPage.xaml.cs:77-119`
- `src/FicheGen.App/Views/Controls/PdfDropZone.xaml:11-27`
- `src/FicheGen.App/Views/HistoryPage.xaml:394-398`
- `src/FicheGen.App/App.xaml:68-118`

History rows and the PDF drop zone use focusable containers with manual pointer and keyboard handling rather than controls that naturally expose an Invoke pattern. The undo toast has an unnamed icon-only dismiss button. The shared custom button template does not define explicit focus states.

These patterns increase the risk that controls are unclear to keyboard and screen-reader users. They also make focus behavior less predictable than standard WinUI controls.

**Recommendation:**

- Prefer `Button`, `ListViewItem`, or another native actionable control.
- Give every icon-only action a localized accessible name.
- Ensure visible focus indicators are present in all themes.
- Verify role, name, value, state, and action through UI Automation.
- Avoid reproducing native interaction behavior manually unless necessary.

### F12. Localization is not end-to-end

**Severity:** High  
**Area:** Localization and RTL  
**Evidence:**

- `src/FicheGen.App/Views/SettingsPage.xaml.cs:82-139`
- `src/FicheGen.App/Views/FichePage.xaml.cs:28-67`
- `src/FicheGen.App/ViewModels/SettingsViewModel.cs:324-333`
- `src/FicheGen.App/Services/ErrorMessageTranslator.cs:65`
- `src/FicheGen.App/Services/ErrorMessageTranslator.cs:116`
- `src/FicheGen.Core/Documents/HtmlRenderer.cs:167-184`

Runtime language refresh covers only part of Settings. Subject and topic catalogs and several statuses remain in French. Some Arabic error strings contain replacement characters. Generated documents always declare French as their language.

The application advertises three languages, so partial translation is more confusing than a clearly limited language scope. Arabic also requires correct directionality and document language metadata.

**Recommendation:**

- Move all visible strings and selectable option data into resources.
- Refresh or recreate all visible controls after a language change.
- Replace corrupted Arabic literals and add validation for resource encoding.
- Derive generated-document `lang` and `dir` from the selected locale or content.
- Test full workflows in Arabic, including dialogs, errors, exports, and preview content.

### F13. Generate is disabled before it can explain missing information

**Severity:** Medium  
**Area:** Form validation  
**Evidence:**

- `src/FicheGen.App/Views/FichePage.xaml.cs:251-288`
- `src/FicheGen.App/Views/EvaluationPage.xaml.cs:377-395`
- `src/FicheGen.App/Views/QuizPage.xaml.cs:222-239`
- `src/FicheGen.App/Strings/fr-FR/Resources.resw:510-514`

Generate becomes disabled when required fields are incomplete, while the submit-attempt path contains the logic that focuses and explains the first missing field. The tooltip only describes the generation action.

A disabled button without a visible reason forces the teacher to inspect the entire form and infer what is missing.

**Recommendation:**

- Keep Generate available until submission, then show inline validation and focus the first invalid field; or
- Keep it disabled but display the exact unmet requirement beside the action.
- Do not rely on a tooltip as the only explanation.

### F14. Minimum width prevents useful Windows Snap layouts

**Severity:** Medium  
**Area:** Responsive layout and multitasking  
**Evidence:**

- `src/FicheGen.App/MainWindow.xaml.cs:35-39`
- `src/FicheGen.App/MainWindow.TitleBar.cs:224-236`
- `src/FicheGen.App/Views/Controls/CreationWorkspace.xaml:15-18`

The application enforces a 1024-DIP minimum while the creation workspace also preserves substantial form and preview minimum widths.

Teachers commonly work beside source material, a browser, a PDF, or a student system. Preventing a practical half-screen layout weakens a likely everyday workflow.

**Recommendation:**

- Add a narrow single-pane or list-detail layout.
- Allow the teacher to switch between Form and Preview at narrow widths.
- Preserve the generation action and status when panes collapse.
- Lower the minimum width only after the narrow layout is usable.

### F15. Primary preview actions can move behind horizontal scrolling

**Severity:** Medium  
**Area:** Action discoverability  
**Evidence:** `src/FicheGen.App/Views/Controls/PreviewHost.xaml:75-218`

The preview toolbar has a minimum width of 680 DIPs and places export and print actions near the trailing edge, while the preview pane can be narrower.

Teachers may not discover horizontal scrolling in a toolbar and may conclude that export is unavailable.

**Recommendation:**

- Keep one primary export action permanently visible.
- Move secondary formats and less-used commands into an overflow menu.
- Use responsive priority rules instead of a horizontally scrolling command strip.

### F16. Settings controls lack explicit accessible label relationships

**Severity:** Medium  
**Area:** Accessibility  
**Evidence:**

- `src/FicheGen.App/Views/SettingsPage.xaml:176-225`
- `src/FicheGen.App/Views/SettingsPage.xaml:341-355`
- `src/FicheGen.App/Views/SettingsPage.xaml:683-721`

Theme, accent, language, provider, and creativity controls are visually described by surrounding text but do not consistently use a control header, accessible name, or explicit label relationship.

Visual proximity does not guarantee that assistive technology announces the purpose of a focused control.

**Recommendation:**

- Associate each control with a localized visible label.
- Verify that the announced text includes purpose and current value.
- Avoid duplicating excessively verbose card descriptions in every announcement.

### F17. Custom overlays do not consistently contain or restore focus

**Severity:** Medium  
**Area:** Keyboard navigation and modal behavior  
**Evidence:**

- `src/FicheGen.App/Views/Controls/CommandPalette.xaml.cs:38-70`
- `src/FicheGen.App/Views/Controls/CreationWorkspace.xaml.cs:247-260`
- `src/FicheGen.App/MainWindow.Accelerators.cs:49-57`

The command palette focuses its query when opened but does not restore prior focus when closed. The assistant overlay does not consistently synchronize its visible state with the shell toggle, contain focus, or close through Escape from every opening path.

An overlay that visually covers the interface but allows focus to move behind it is disorienting, particularly for keyboard and screen-reader users.

**Recommendation:**

- Route all open and close actions through one state transition.
- Save the invoking element and restore focus on close.
- Focus the primary field or heading on open.
- Contain Tab navigation while an overlay is modal.
- Ensure Escape closes the visible overlay regardless of how it was opened.

### F18. The shared preview does not clearly identify its document type

**Severity:** Medium  
**Area:** Context and mental model  
**Evidence:**

- `src/FicheGen.App/App.xaml.cs:75-82`
- `src/FicheGen.App/Views/Controls/CreationWorkspace.xaml:65`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml:107-115`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml:313-367`
- `src/FicheGen.App/ViewModels/ResultViewModel.cs:163-169`

One result ViewModel is shared across lesson-plan, evaluation, and quiz workflows. The preview does not prominently identify which workflow produced the visible content, and empty-state wording refers generically to a lesson plan.

When switching sections, a teacher may be unsure whether the visible document belongs to the current workflow or a previous one.

**Recommendation:**

- Display the document type and title in the preview header.
- Make empty-state instructions specific to the active workflow.
- Clarify the student/correction toggle using an action label or explicit current state.
- Define whether changing workflows preserves, replaces, or archives the current preview.

### F19. Authentication feedback is remote from fields and actions remain repeatable while busy

**Severity:** Medium  
**Area:** Account flow and error recovery  
**Evidence:**

- `src/FicheGen.App/Views/Controls/AccountDialog.xaml:148-155`
- `src/FicheGen.App/ViewModels/AccountViewModel.cs:25`
- `src/FicheGen.App/ViewModels/AccountViewModel.cs:110-149`
- `src/FicheGen.App/ViewModels/AccountViewModel.cs:153-229`

Validation and service errors appear in a generic text block instead of beside the affected field. Commands can remain executable during a request, and raw exception text may reach the user.

Technical error wording and repeated submissions make sign-in harder to recover from.

**Recommendation:**

- Show field-specific validation beside the relevant field.
- Focus the first invalid field after submission.
- Disable conflicting actions while the request is active.
- Keep the action label visible with an adjacent progress indicator.
- Translate and sanitize service failures into plain language.

### F20. Shortcut documentation conflicts with implemented commands

**Severity:** Medium  
**Area:** Keyboard efficiency and trust  
**Evidence:**

- `src/FicheGen.App/ViewModels/SettingsViewModel.Storage.cs:62-69`
- `src/FicheGen.App/MainWindow.xaml:14-36`
- `src/FicheGen.App/MainWindow.xaml.cs:488-505`
- `src/FicheGen.App/Strings/fr-FR/Resources.resw:441-445`
- `src/FicheGen.App/MainWindow.Accelerators.cs:224-234`

Settings and help advertise shortcuts that differ from the implemented accelerators. One help entry describes preview toggling for a shortcut that exports Word.

Incorrect shortcuts teach users that keyboard commands are unreliable.

**Recommendation:**

- Define shortcuts once and generate help text from that source.
- Include shortcut text on the relevant menu or tooltip.
- Add tests that compare displayed shortcuts with accelerator definitions.

### F21. The generation step tracker does not reflect actual progress

**Severity:** Medium  
**Area:** Progress feedback  
**Evidence:**

- `src/FicheGen.App/ViewModels/ResultViewModel.cs:243-275`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml:404-452`

The ViewModel tracks a generation phase, but the four visible milestones are static and the progress bar remains indeterminate.

A step tracker implies measured advancement. Static milestones can make generation appear stalled or misleadingly precise.

**Recommendation:**

- Bind each milestone to current and completed states; or
- Remove the step tracker and show one honest phase message.
- Avoid percentages or stages that the application cannot determine reliably.

### F22. Export lacks meaningful progress, cancellation, and execution gating

**Severity:** Medium  
**Area:** Long-running operation behavior  
**Evidence:**

- `src/FicheGen.App/ViewModels/ResultViewModel.cs:673-700`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:182-197`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:354-361`
- `src/FicheGen.App/Services/WebView2PdfExporter.cs:48-64`
- `src/FicheGen.App/Services/WebView2PdfExporter.cs:111-117`
- `src/FicheGen.App/Services/WebView2PdfExporter.cs:147-162`

Export uses `CancellationToken.None`, preview actions can remain active while work is running, and PDF cancellation may wait for the underlying operation to finish. Some filesystem work occurs inside the UI-dispatched callback.

The teacher can initiate conflicting actions or see the interface pause without understanding why.

**Recommendation:**

- Provide one visible export state with destination and format.
- Disable conflicting export actions while active.
- Allow cancellation where the underlying technology supports it.
- Keep UI-affined WebView work on the dispatcher and move file work off it.
- Avoid reporting cancellation until the operation has actually stopped or its result will be ignored safely.

### F23. Assistant diff actions do not visibly resolve the proposed change

**Severity:** Medium  
**Area:** Assistant interaction feedback  
**Evidence:**

- `src/FicheGen.App/Views/Controls/AssistantPane.xaml:215-220`
- `src/FicheGen.App/Views/Controls/AssistantPane.xaml.cs:370-379`
- `src/FicheGen.App/Views/Controls/IAssistantHostPage.cs:138-161`
- `src/FicheGen.App/ViewModels/AssistantViewModel.cs:414-425`

Reject changes state that the template does not use, and the host does not wire the rejection event. Apply can become a silent no-op on repeated activation because its resolved state is not represented visibly.

The teacher cannot confidently tell whether a suggestion was accepted, ignored, or is still pending.

**Recommendation:**

- Replace action buttons with a clear Applied or Ignored state after resolution.
- Disable actions immediately after activation.
- Announce the result to assistive technology.
- Move focus to a stable next target.
- Ensure Apply and Ignore use the same state model and host wiring.

### F24. History rendering weakens virtualization and duplicates refresh work

**Severity:** Medium  
**Area:** Responsiveness and scrolling  
**Evidence:**

- `src/FicheGen.App/Views/HistoryPage.xaml:225-260`
- `src/FicheGen.App/ViewModels/HistoryViewModel.cs:439-443`
- `src/FicheGen.App/ViewModels/HistoryViewModel.cs:355-359`
- `src/FicheGen.App/Views/HistoryPage.xaml.cs:63-74`
- `src/FicheGen.App/ViewModels/HistoryViewModel.cs:370-395`

A `ListView` is nested within an outer `ScrollViewer`, with an `ItemsControl` per group. Up to 200 records are loaded. Filter changes and explicit search can trigger duplicate or competing refresh paths.

This structure increases layout work, weakens effective virtualization, and can make search results feel less responsive as history grows.

**Recommendation:**

- Use one virtualized grouped `ListView` as the scroll owner.
- Keep one cancellable refresh path.
- Cancel pending debounce work when search is explicitly submitted.
- Ensure stale queries cannot replace newer results.

### F25. First-run step changes are not announced or focused

**Severity:** Medium  
**Area:** Onboarding accessibility  
**Evidence:**

- `src/FicheGen.App/Views/Controls/FirstRunDialog.xaml.cs:42-58`
- `src/FicheGen.App/Views/Controls/FirstRunDialog.xaml:21-28`

Moving between onboarding steps collapses one panel and reveals another, but focus does not move and the progress text is not exposed as a live update.

Screen-reader and keyboard users may not know that the content changed or where to continue.

**Recommendation:**

- Focus the new step heading or first interactive control.
- Announce "Step N of 3" with a polite live region.
- Keep Back and Continue placement stable.
- Ensure step titles describe the user's task rather than technical configuration.

### F26. PDF picker and drag feedback have recovery gaps

**Severity:** Medium  
**Area:** File input  
**Evidence:**

- `src/FicheGen.App/Views/Controls/PdfDropZone.xaml.cs:110-120`
- `src/FicheGen.App/Views/Controls/PdfDropZone.xaml.cs:185-210`

Drag-over advertises a Copy operation without first confirming that the payload contains an acceptable PDF. Picker exceptions are logged without a visible user-facing error.

The interface may indicate that a drop is valid and then fail without guidance.

**Recommendation:**

- Show an allowed drag operation only for valid PDF storage items.
- Explain invalid file type, unreadable file, and picker failure separately.
- Preserve the prior valid selection when replacement fails.
- Offer a visible Retry or Choose another file action.

### F27. Frequently used controls are smaller than comfortable touch targets

**Severity:** Low  
**Area:** Ergonomics  
**Evidence:**

- `src/FicheGen.App/App.xaml:131-140`
- `src/FicheGen.App/Views/Controls/PreviewHost.xaml:17-26`
- `src/FicheGen.App/Views/HistoryPage.xaml:15-20`

Choice chips, toolbar controls, and history filters use minimum heights between 26 and 34 DIPs.

These sizes are compact for mouse use but less forgiving for touch, motor impairments, and hurried classroom interaction.

**Recommendation:**

- Target approximately 40 to 44 DIPs for frequently used actions.
- Preserve compact visual styling through internal icon and text sizing rather than reducing the hit area.
- Prioritize Generate, Cancel, Export, filter, and dismiss actions.

## Cross-Cutting Themes

### 1. Preserve the last stable state

Several flows replace stable content with a loading or empty state too early. A safer model is:

1. Keep the current document or result visible.
2. Indicate that an update is in progress.
3. Replace the content only after success.
4. Return to the prior stable state on cancellation.
5. Keep failure visible until acknowledged or retried.

This pattern reduces anxiety and makes experimentation safer for teachers.

### 2. Use one operation model

Generation, export, history refresh, assistant actions, and account requests expose different busy, cancellation, and error conventions. A shared interaction contract should define:

- What starts an operation.
- Which actions remain available.
- Where progress appears.
- Whether cancellation is possible.
- What happens on navigation.
- How success and failure are announced.
- Which previous state is preserved.

The implementation does not need one universal class, but the user experience should feel like one system.

### 3. Prefer native interaction semantics

Custom focusable borders and manually simulated buttons increase accessibility and maintenance risk. Native WinUI controls should be the default for clickable rows, drop zones, dismiss actions, menus, and modal surfaces.

Custom visuals can be applied without discarding the built-in keyboard, focus, state, and UI Automation behavior.

### 4. Avoid teaching technical concepts during core tasks

Provider, token, model, and endpoint choices should remain in advanced settings. The primary workflow should communicate outcomes:

- Online generation is ready.
- Sign-in is required.
- The local service is unavailable.
- Generation could not finish.
- Try again or open settings.

The teacher should not need to understand the architecture to recover.

### 5. Motion should clarify state, not carry state

The application generally uses restrained transform and opacity animation, which is appropriate. However, essential content must not depend on animation completion. Every animated state needs a correct static equivalent for reduced motion and interrupted transitions.

## Strengths

The review found several strong foundations worth preserving:

- Creation workflows use clear numbered sections and a persistent primary action.
- Advanced AI configuration is progressively disclosed rather than placed in the default workflow.
- The preview includes purpose-built empty, loading, success, and missing-runtime treatments.
- Generation changes its main action to a spinner-backed Cancel state with updated accessible text.
- Invalid generation attempts can focus the first missing field.
- History deletion includes confirmation and an undo period.
- Semantic resources exist for light, dark, and high-contrast themes.
- The motion service checks the Windows animation preference.
- Entrance animation uses opacity and transforms rather than layout-heavy properties.
- Streaming updates are bounded and batched.
- PDF input supports pointer, keyboard, and drag interaction paths.
- Localization parity tests cover the three declared resource sets.
- Native controls provide a good baseline for hover, pressed, disabled, and keyboard behavior where they are used directly.

## Recommended Delivery Plan

### Phase 1: Restore trust and prevent surprising outcomes

1. Fix Save-picker cancellation so Cancel never creates a file.
2. Make Assistant modes match implemented capabilities.
3. Make first-run end with a verified usable provider.
4. Surface generation, export, history, and account failures consistently.
5. Preserve the previous preview during replacement, cancellation, and failure.

### Phase 2: Stabilize operations and navigation

1. Introduce a coordinated generation lifecycle.
2. Define navigation behavior during generation.
3. Gate conflicting export and assistant actions.
4. Remove duplicate WebView navigation paths.
5. Consolidate history refresh and restore effective virtualization.

### Phase 3: Complete accessibility and keyboard behavior

1. Fix reduced-motion final states.
2. Replace pseudo-controls with native actionable controls.
3. Add accessible names and label relationships.
4. Contain and restore focus for overlays.
5. Announce onboarding, operation, and assistant state changes.
6. Verify keyboard-only operation, Narrator, high contrast, and 200% scaling.

### Phase 4: Improve teacher workflow clarity

1. Explain disabled or invalid Generate states directly.
2. Make preview context and document type explicit.
3. Replace static pseudo-progress with honest progress messaging.
4. Generate shortcut help from implemented commands.
5. Simplify account and provider language.

### Phase 5: Complete localization and responsive polish

1. Localize all visible strings and option data.
2. Correct Arabic resources and generated-document directionality.
3. Add a narrow creation layout for Windows Snap.
4. Make preview actions responsive without horizontal toolbar scrolling.
5. Increase frequently used hit targets.

## Prioritized Top Ten

| Priority | Work item | Primary benefit |
| --- | --- | --- |
| 1 | Honor Save-picker cancellation | Prevents explicit user intent from being violated. |
| 2 | Coordinate generation and cancellation globally | Eliminates contradictory busy and cancellation states. |
| 3 | Preserve the previous preview and expose failures | Protects work and improves recovery. |
| 4 | Make first-run produce a ready configuration | Prevents immediate first-use failure. |
| 5 | Make Assistant capabilities truthful | Restores trust in feature labels and modes. |
| 6 | Fix reduced motion and custom control semantics | Removes accessibility blockers. |
| 7 | Stabilize search, overlay focus, and keyboard behavior | Makes interaction predictable without a mouse. |
| 8 | Remove duplicate preview and history work | Improves responsiveness and visual stability. |
| 9 | Complete localization and RTL behavior | Makes advertised language support credible. |
| 10 | Add narrow layouts and responsive actions | Supports common teacher multitasking workflows. |

## Considered but Not Recommended

| Candidate | Reason not recommended |
| --- | --- |
| Add more decorative animation | Current risks concern state correctness and feedback. Additional motion would not solve them and could obscure transitions. |
| Replace the numbered creation flow | The existing structure is clear and familiar. Improving validation and responsive behavior offers more value than redesigning it. |
| Expose more AI configuration in onboarding | The primary audience should not need provider knowledge. Readiness should be automated or expressed through outcomes. |
| Increase shadows or visual depth broadly | The current visual system is not the main usability problem. Systemic feedback and accessibility work should come first. |
| Convert every operation to a modal dialog | Most progress and errors should remain contextual and non-blocking. Modal dialogs should be reserved for decisions requiring immediate attention. |

## Verification Performed

- Reviewed repository instructions and the Windows blueprint.
- Inspected application XAML, code-behind, ViewModels, UI services, localization resources, and relevant tests.
- Reviewed shell navigation, creation pages, preview, history, settings, account, first-run, assistant, export, PDF input, and motion paths.
- Searched for animation, focus, loading, cancellation, navigation, dialog, scrolling, and WebView patterns.
- Cross-checked French, English, and Arabic resource coverage.
- Inspected existing screenshots as supporting context only.
- Confirmed the review was read-only and did not modify application code.

## Not Runtime-Verified

The following require an interactive WinUI environment and should not be considered visually proven by source review alone:

- Actual focus-ring appearance.
- Narrator announcements and UI Automation control patterns.
- High Contrast rendering.
- Arabic RTL layout across complete workflows.
- Behavior at 200% text scaling.
- Narrow-window and Windows Snap behavior.
- Contrast ratios against final rendered surfaces.
- Animation smoothness and frame pacing at 60 Hz or 120 Hz.
- WebView blank flashes or paint timing.
- Touch, pen, and drag visual feedback.
- Reduced-motion behavior on a configured Windows system.

The existing screenshots appear older than parts of the current source, so they were not treated as definitive evidence of current runtime appearance.

## Suggested Acceptance Checks

Before changing the verdict, verify at minimum:

1. Cancel every Save picker and confirm that no file is created.
2. Start generation, navigate, start another generation, and confirm that only the intended operation remains active.
3. Cancel and fail regeneration while an older preview exists and confirm that the older document remains visible.
4. Trigger provider, network, export, history, and authentication failures and confirm that each has plain-language recovery.
5. Run the application with Windows animations disabled and confirm that all content remains visible.
6. Complete all primary flows using only the keyboard.
7. Verify custom actions and state changes with Narrator.
8. Use Windows Snap and larger text while creating and previewing a document.
9. Complete first-run and generate a document without opening advanced settings.
10. Run the complete workflow in French, English, and Arabic, including exported output.

## Final Verdict

**Block**

The application has a credible visual and structural foundation, but the current interface contains several high-impact trust, accessibility, feedback, and state-management issues. The critical Save-cancellation behavior and the high-severity workflow mismatches should be addressed before the interface is considered ready for non-technical teachers.

No application code was changed as part of this review.
