# Code Review

## Review Summary

**Verdict:** REQUEST CHANGES  
**Overview:** The worktree contains valuable hardening and localization work, but it also has release-blocking installer, history-integrity, export, authentication, and packaging defects.

### Verification Story

- **Tests reviewed:** Yes; changed tests were inspected before implementation, and their cross-module coverage gaps are called out below.
- **Build verified:** Yes for Debug x64 and ARM64 compilation; packaging and interactive E2E execution were not performed.
- **Security checked:** Yes; authentication/authorization, input limits, persistence, signing, secrets handling, and dependency advisories were reviewed.

## Review Scope

- **Review snapshot:** 2026-08-28, branch `windows-native`, `HEAD` `caab27c38a989536965bf6a5b183dbfdb8f5fee1`.
- **Upstream:** `origin/windows-native` points to the same commit, so there are no committed branch changes beyond that upstream.
- **Default branch:** `origin/main` is `9204c574a14939064b828c7ce0f55d8775ef57e2`, but `git merge-base HEAD origin/main` exits 1. Their roots are unrelated (`ade2ac9...` versus `afe1662...`), so a meaningful three-dot/default-branch diff cannot be produced.
- **Effective review baseline:** `HEAD`. The reviewed change set is the current worktree: **82 unstaged tracked files**, **0 staged files**, and **6 untracked files**. `git diff HEAD` reports 9,374 insertions and 4,050 deletions, excluding untracked files.
- **Untracked files reviewed:** `.opencode/plugins/graphify.js`, `Directory.Build.targets`, `docs/TEACHER_UI_UX_MOTION_REVIEW_2026-08-28.md`, `scripts/sign-binaries.ps1`, `src/FicheGen.App/Views/ILocalizablePage.cs`, and `tests/FicheGen.Infrastructure.Tests/LocalizationParityTests.cs`.
- **Intent sources:** `AGENTS.md`, `docs/WINDOWS_BLUEPRINT.md`, `docs/ARCHITECTURE.md`, README changes, tests, and the untracked UI/UX review. Tests were inspected before their corresponding implementation where practical.
- **Architecture map:** the existing `graphify-out/graph.json` was queried before broad source inspection. It confirmed the generation, authentication, history, preview, export, and test impact paths; decisive claims below were then checked against current source.

## Executive Verdict

**Verdict: Request changes**

The worktree contains useful hardening and test additions, and all currently runnable .NET tests/builds pass. It is not ready to merge: three critical defects include a non-runnable installer, destructive history undo behavior, and exports occurring after the user cancels the picker. The default cloud path, ARM64 packaging, quota enforcement, manual-edit persistence, and several UI state/accessibility paths also require correction.

**Finding count:** Critical 3 · High 9 · Medium 9 · Low/Nit 1.

## Findings

### Critical

#### C1. The documented PowerShell 5.1 installer does not parse

- **Location:** `installer/Install-MSIX.ps1:1`, `installer/Install-MSIX.ps1:99`
- **Axes:** Correctness, release engineering
- **Evidence/reasoning:** The script declares `#Requires -Version 5.1`, but executing `powershell -NoProfile -NonInteractive -File installer/Install-MSIX.ps1 ...` fails during parsing with `TerminatorExpectedAtEndOfString` at line 99. The file contains non-ASCII text without a BOM, which Windows PowerShell 5.1 can decode incorrectly.
- **Impact:** The MSIX installation path cannot start on its explicitly supported shell; certificate and package installation never run.
- **Remediation:** Save the script as UTF-8 with BOM or make it ASCII-safe, then add a Windows PowerShell 5.1 parser/execution smoke test in CI.

#### C2. Lightweight history rows break open/export and make delete-undo destructive

- **Location:** `src/FicheGen.Infrastructure/Storage/HistoryRepository.cs:245-251`; `src/FicheGen.App/ViewModels/HistoryViewModel.cs:61-70`, `591-635`, `694-703`; `src/FicheGen.App/MainWindow.xaml.cs:161-207`
- **Axes:** Correctness, data integrity, architecture, tests
- **Evidence/reasoning:** `SearchAsync` now deliberately returns `html = ''` and null `source_json`, `raw_prompt`, and `raw_response`. `HistoryItemViewModel` then derives its `PlainText` from the blank HTML instead of `model.PlainText`. Both open paths therefore have neither source JSON, plain text, nor HTML to load. More seriously, delete undo calls `SaveAsync(item.Model)` on that partial row, restoring an empty HTML document and permanently discarding the stored source/raw fields. The repository tests assert search titles only and never exercise open/export/delete-restore fidelity.
- **Impact:** History items can open as blank; exports receive incomplete data; undoing deletion can silently destroy the saved document body and metadata.
- **Remediation:** Return a dedicated summary DTO from search, hydrate the complete `HistoryItem` with `GetByIdAsync` before open/export/delete, and retain the full deleted object for undo. Add integration tests asserting open/export content and byte-for-byte field preservation across delete/restore.

#### C3. Canceling the Save picker still writes a file

- **Location:** `src/FicheGen.App/Services/ExportWorkflowService.cs:140-168`
- **Axes:** Correctness, data integrity, UX
- **Evidence/reasoning:** A normal picker cancellation returns `null`, but control falls through to the automatic Documents-folder fallback and returns a path. The caller subsequently exports there.
- **Impact:** A user who explicitly chooses Cancel can still create a file at an unexpected location.
- **Remediation:** Return `null` immediately for a successful picker invocation that yields no file. Use fallback only when the picker cannot be opened, and add tests distinguishing cancellation from picker failure.

### High

#### H1. The default onboarding configuration cannot use the cloud service

- **Location:** `src/FicheGen.Core/Storage/AppSettings.cs:42-47`; `src/FicheGen.App/Views/Controls/FirstRunDialog.xaml:178-198`; `src/FicheGen.App/Views/Controls/FirstRunDialog.xaml.cs:23-30`; `src/FicheGen.Infrastructure/Ai/Adapters/OpenAiCompatibleAdapter.cs:22-35`; `server/worker.js:55-79`; `supabase/functions/chat/index.ts:72-104`
- **Axes:** Correctness, security, UX, tests
- **Evidence/reasoning:** New installs default to `cloud`, and onboarding says it needs no key or setup and contains no sign-in step. With no user token, the adapter sends the public anonymous credential as bearer authorization, while both proxy implementations now require a valid authenticated user JWT.
- **Impact:** A first-time user can complete onboarding successfully and then have every default generation request rejected.
- **Remediation:** Require and verify account sign-in before completing cloud onboarding, or restore a deliberately bounded anonymous flow. Add an end-to-end test for “fresh install → finish onboarding → generate”.

#### H2. Cloud generation bypasses the implemented token-refresh path

- **Location:** `src/FicheGen.Infrastructure/Auth/SupabaseAuthService.cs:247-275`, `321-383`; `src/FicheGen.App/ViewModels/FicheFormViewModel.cs:562-570`; `src/FicheGen.App/ViewModels/EvaluationViewModel.cs:516-524`; `src/FicheGen.App/ViewModels/QuizViewModel.cs:539-547`; `src/FicheGen.App/ViewModels/AssistantViewModel.cs:263-273`
- **Axes:** Correctness, security, architecture, concurrency
- **Evidence/reasoning:** `GetValidTokenAsync` contains refresh synchronization and JWT-expiry handling, but generation configuration resolves `supabase_access_token` directly from `ICredentialStore`. No generation caller invokes `GetValidTokenAsync`. A failed refresh also returns the stale access token instead of clearing authentication.
- **Impact:** Cloud generation starts failing after access-token expiry and can repeatedly send an expired credential until the user signs in again.
- **Remediation:** Make the cloud secret resolver call `IAuthService.GetValidTokenAsync(ct)` and fail/sign out when refresh fails. Add tests for expired-token refresh, concurrent refresh, cancellation, and invalid refresh tokens.

#### H3. Proxy quota and request-size controls do not actually bound consumption

- **Location:** `server/worker.js:46-53`, `106-156`; `supabase/functions/chat/index.ts:106-163`
- **Axes:** Security, performance, data integrity, tests
- **Evidence/reasoning:** Both handlers only reject a quota already at zero; neither decrements or reserves quota, and there is no per-user rate limit. The worker trusts `Content-Length`, which can be absent or inaccurate, while the Supabase function has no byte-size limit. Message content and individual message size are unbounded.
- **Impact:** Any authenticated account can generate unbounded provider cost and submit oversized payloads that consume worker memory/CPU.
- **Remediation:** Atomically reserve/decrement quota before the provider call, apply per-user rate limiting, reject requests by bytes actually read, and validate message roles/content lengths. Add concurrent quota and chunked/no-content-length abuse tests.

#### H4. ARM64 packages are emitted with x64 identity and mismatched versions

- **Location:** `FicheGen.Windows.slnx:3-32`; `src/FicheGen.App/FicheGen.App.csproj:9-12`; `scripts/build-installers.ps1:17-28`, `112-128`, `166-177`; `src/FicheGen.App/Package.appxmanifest.xml:9-13`; `Directory.Build.props:10`
- **Axes:** Correctness, release architecture, tests
- **Evidence/reasoning:** The worktree adds ARM64 builds, but the packaging script copies a manifest hard-coded to `ProcessorArchitecture="x64"`. The same manifest is version `1.4.0.0`, while the script and generated `.appinstaller` default to `1.2.0.0`/`1.2.0`. A successful project build does not validate these package metadata contracts.
- **Impact:** ARM64 MSIX packages can be rejected or load the wrong architecture, and AppInstaller update/install metadata will not match the packaged identity.
- **Remediation:** Generate or transform the manifest from one version/architecture source, or use MSBuild's packaging pipeline. Validate both x64 and ARM64 packages by unpacking them and comparing manifest identity to filename/AppInstaller metadata.

#### H5. “Save” in the manual editor is not durable and is structurally lossy

- **Location:** `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:505-583`; `src/FicheGen.App/ViewModels/ResultViewModel.cs:303-324`; `src/FicheGen.Core/Documents/GeneratedDocument.cs:99-131`; `src/FicheGen.Core/Documents/FallbackMarkdownRenderer.cs:79-163`
- **Axes:** Correctness, data integrity, tests
- **Evidence/reasoning:** `ApplyManualEdits` only mutates the in-memory `ResultViewModel`; it never updates the history repository despite UI copy saying the changes were saved. Its Markdown round trip also has no representation for `KeyValueGridBlock`, so saving an unchanged document converts those blocks to paragraphs. Existing round-trip coverage tests only headings, paragraphs, and bullet lists.
- **Impact:** Edits disappear after reopening from history, and simply saving can alter document structure and export layout.
- **Remediation:** Associate the current document with its history ID and persist edits transactionally. Define a lossless editable representation for every block type (or use a structured editor), and add round-trip tests for all polymorphic blocks plus reopen-from-history behavior.

#### H6. Several advertised direct providers cannot be routed or configured correctly

- **Location:** `src/FicheGen.App/ViewModels/SettingsViewModel.cs:201-220`, `585-603`; `src/FicheGen.App/ViewModels/SettingsViewModel.Ai.cs:28-63`; `src/FicheGen.Infrastructure/Ai/LlmRouter.cs:72-80`, `132-165`
- **Axes:** Correctness, architecture, tests
- **Evidence/reasoning:** The provider catalog exposes Anthropic, DeepSeek, Groq, Mistral, OpenRouter, Together, Fireworks, Cerebras, xAI, and DoubleWord. The router maps all except Gemini/Vertex/Vercel to the OpenAI adapter, but only a subset has a real endpoint mapping; Anthropic and the remaining providers fall back to `ProxyBaseUrl`. Most provider-specific credential names also have no settings field or persistence path.
- **Impact:** Selecting an advertised provider commonly sends the wrong protocol/key to the local proxy URL or sends no credential, causing deterministic generation failures.
- **Remediation:** Define a complete provider descriptor (adapter, endpoint, credential key, model list) and derive both UI and routing from it. Hide unsupported providers. Add table-driven route/request tests for every displayed provider.

#### H7. Reduced-motion mode can leave the preview's empty state invisible

- **Location:** `src/FicheGen.App/Views/Controls/PreviewHost.xaml:47-50`, `287-288`; `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:339-378`; `src/FicheGen.App/Services/UiMotion.cs:13-22`
- **Axes:** Correctness, accessibility
- **Evidence/reasoning:** `EmptyStatePanel` starts at opacity 0 and becomes visible through a storyboard. When animations are disabled, `UpdateVisualState` stops storyboards but never applies their final opacity/transform values.
- **Impact:** Users who disable Windows animations can lose the primary empty-state guidance entirely.
- **Remediation:** Explicitly set final visual values when motion is disabled and add a reduced-motion UI regression test.

#### H8. Assistant “Generate” mode is presented but never generates

- **Location:** `src/FicheGen.App/Views/Controls/AssistantPane.xaml:125-142`; `src/FicheGen.App/ViewModels/AssistantViewModel.cs:283-360`
- **Axes:** Correctness, UX, tests
- **Evidence/reasoning:** The UI exposes Auto/Generate/Edit/Question. `SendMessageAsync` only edits when `wantsEdit`, asks a question when a document exists and mode is not Generate, or emits the “generate elsewhere first” message. There is no generation branch.
- **Impact:** A prominent user path always fails to perform the action it advertises.
- **Remediation:** Implement generation through the shared generation workflow or remove/disable the mode. Add behavior tests for every mode with and without an open document.

#### H9. A runtime SQLite dependency is reported vulnerable

- **Location:** `src/FicheGen.Infrastructure/FicheGen.Infrastructure.csproj:18-20`
- **Axes:** Security, dependency hygiene
- **Evidence/reasoning:** `dotnet list ... --vulnerable --include-transitive` reports `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 with high-severity GHSA-2m69-gcr7-jv3q/CVE-2025-6965. This worktree explicitly adds the 2.1.10 bundle, and SQLite is used at runtime by history storage. The advisory currently lists no patched NuGet version; exploitability through this application's fixed, parameterized SQL was not demonstrated.
- **Impact:** The shipped application contains a native library version associated with memory corruption risk.
- **Remediation:** Track and move to a bundle containing SQLite 3.50.2+ when available, or use a reviewed patched provider/native binary. Document temporary reachability analysis and add dependency auditing to CI.

### Medium

#### M1. PDF export cancellation waits for the uncancelable UI operation

- **Location:** `src/FicheGen.App/Services/WebView2PdfExporter.cs:43-48`, `147-162`; `src/FicheGen.App/ViewModels/ResultViewModel.cs:673-700`
- **Axes:** Correctness, responsiveness, cancellation
- **Evidence/reasoning:** `WaitAsync(ct)` can throw promptly, but its `finally` immediately awaits the same `opTcs.Task` without the token. The public export commands also pass `CancellationToken.None`.
- **Impact:** Cancellation can remain blocked until WebView initialization/printing finishes; a dispatcher shutdown could leave the wait unresolved.
- **Remediation:** Separate operation lifetime from caller cancellation, cancel/close the WebView where possible, and never await an unbounded completion in cancellation cleanup. Expose an export CTS from the ViewModel and test cancel-before-dispatch, cancel-during-navigation, and dispatcher-shutdown cases.

#### M2. An older generation can clear the busy state of a newer generation

- **Location:** `src/FicheGen.App/ViewModels/ResultViewModel.cs:248-280`; `src/FicheGen.App/ViewModels/FicheFormViewModel.cs:269-317`, `345-359`; corresponding blocks in `EvaluationViewModel.cs:264-313`, `338-352` and `QuizViewModel.cs:272-330`, `355-369`
- **Axes:** Correctness, concurrency, state consistency
- **Evidence/reasoning:** Operation IDs guard phases/chunks/results, but each page's `finally` sets the shared `ResultViewModel.IsBusy = false` without checking that its operation is still active. Separate singleton page ViewModels can overlap after navigation.
- **Impact:** A stale operation can hide progress and re-enable conflicting actions while the current operation is still running.
- **Remediation:** Give `ResultViewModel` an `EndOperation(id)` method that conditionally clears state, and route all starts/cancels through one coordinator. Add an overlapping-operation test where A completes after B starts.

#### M3. Runtime language switching refreshes only part of the visible tree and fires twice

- **Location:** `src/FicheGen.App/ViewModels/SettingsViewModel.Appearance.cs:38-52`, `191-212`; `src/FicheGen.App/MainWindow.Navigation.cs:453-516`; `src/FicheGen.App/Views/SettingsPage.xaml.cs:82-139`; `src/FicheGen.App/Views/Controls/GenerateCta.xaml.cs:39-53`; `src/FicheGen.App/Views/Controls/AssistantPane.xaml.cs:219-259`
- **Axes:** Correctness, readability, localization, performance, tests
- **Evidence/reasoning:** Selecting a language sets `Language`, whose generated partial callback calls `L10n.SetLanguage`, and then calls `L10n.SetLanguage` again directly. The shell refreshes only the current page's manual `ILocalizablePage` method; nested controls and most Settings content are not refreshed, while option catalogs retain labels created in the prior language.
- **Impact:** A single selection performs duplicate refresh work and leaves a mixed-language UI, especially in Settings, Assistant, Preview, PDF controls, and generated status text.
- **Remediation:** Make one language-change entry point, rebuild/rebind all localized collections, and give nested controls a refresh contract or recreate the page. Add runtime-switch tests for all three locales and RTL.

#### M4. The DYS preset is not applied consistently

- **Location:** `src/FicheGen.Core/Services/StylePresetService.cs:72-84`; `src/FicheGen.App/ViewModels/QuizViewModel.cs:329-346`; `src/FicheGen.App/ViewModels/ResultViewModel.cs:385-391`, `453-461`; `src/FicheGen.App/Views/Controls/PreviewHost.xaml:94-105`; `src/FicheGen.App/Services/ExportWorkflowService.cs:53`, `73`, `93-94`
- **Axes:** Correctness, accessibility, tests
- **Evidence/reasoning:** Quiz generation stores the ID `dyslexie`, but `LoadDocument` accepts already-rendered default HTML and does not re-render for the selected preset. The preview selector has no DYS option, and export calls pass `null` for the preset. `ResultViewModel` also exposes a different ID (`dys`).
- **Impact:** Enabling the DYS option may not alter the initial preview or exported document, and the active style cannot be represented consistently in the UI.
- **Remediation:** Use one canonical preset catalog/ID, render from `GeneratedDocument` plus the selected preset at load/export time, and add preview/PDF/DOCX assertions for DYS styling.

#### M5. `CreationWorkspace` leaks page instances through ViewModel subscriptions

- **Location:** `src/FicheGen.App/Views/Controls/CreationWorkspace.xaml.cs:50-74`; `src/FicheGen.App/App.xaml.cs:75-82`; `src/FicheGen.App/MainWindow.Navigation.cs:47-66`
- **Axes:** Performance, architecture, correctness
- **Evidence/reasoning:** Every workspace subscribes an anonymous closure to the singleton page ViewModel on `DataContextChanged`; the handler is never detached on unload or DataContext replacement. Frame navigation can create new pages, leaving old controls rooted by the singleton ViewModel.
- **Impact:** Repeated navigation can grow memory and dispatch updates to unloaded controls.
- **Remediation:** Store both the subscribed source and named handler, unsubscribe before replacement and on unload, then re-subscribe on load. Add a lifecycle test that verifies one notification after repeated navigation/load cycles.

#### M6. Busy forms are only pointer-disabled

- **Location:** `src/FicheGen.App/Views/Controls/CreationWorkspace.xaml.cs:55-73`
- **Axes:** Correctness, accessibility
- **Evidence/reasoning:** During generation the form uses `IsHitTestVisible = false` and reduced opacity, but descendants remain enabled and keyboard-focusable.
- **Impact:** Keyboard users can alter parameters while a generation based on the previous snapshot is running, and assistive technology does not receive a disabled state.
- **Remediation:** Disable the form semantically with `IsEnabled`, while keeping a separate Cancel control enabled and focusable. Add keyboard/UI Automation coverage.

#### M7. Accent foreground selection fails WCAG contrast for several shipped colors

- **Location:** `src/FicheGen.App/Services/AccentColorService.cs:94-106`, `142-153`; `src/FicheGen.App/ViewModels/SettingsViewModel.Appearance.cs:70-78`
- **Axes:** Accessibility, correctness
- **Evidence/reasoning:** The implementation chooses black only when relative luminance is above 0.4. WCAG contrast comparison between black and white crosses near 0.179. For the shipped green, orange, and teal accents, the current rule chooses white at approximately 3.77:1, 3.56:1, and 3.74:1 respectively, below 4.5:1 for normal text; black would pass.
- **Impact:** Accent buttons can be unreadable for low-vision users despite the blueprint's AA requirement.
- **Remediation:** Calculate both `(Llighter + 0.05)/(Ldarker + 0.05)` ratios and choose a foreground meeting the target. Add parameterized contrast tests for every palette color.

#### M8. The signing target can modify third-party binaries and silently succeed on signing failure

- **Location:** `Directory.Build.targets:2-3`; `scripts/sign-binaries.ps1:37-51`
- **Axes:** Security, release integrity, correctness
- **Evidence/reasoning:** The predicate selects every unsigned DLL/EXE, not only `FicheGen*`, because it uses `-or`. Native `signtool` exit codes are not checked, and MSBuild explicitly continues on error.
- **Impact:** Opt-in release builds may alter vendor binaries and still publish partially or wholly unsigned artifacts while appearing successful.
- **Remediation:** Restrict the allowlist to owned outputs, validate every signing exit code/signature, fail the release target on error, and timestamp production signatures. Add a packaging test that proves vendor files are untouched and all owned artifacts verify.

#### M9. Preview content changes trigger duplicate, unsequenced WebView navigations

- **Location:** `src/FicheGen.App/ViewModels/ResultViewModel.cs:138-142`; `src/FicheGen.App/Views/Controls/PreviewHost.xaml.cs:172-181`, `247-259`, `932-951`
- **Axes:** Performance, correctness, responsiveness
- **Evidence/reasoning:** Changing `CurrentHtml` also raises `PreviewHtml`, and the control handles both notifications by calling an `async void` synchronization method. Each update can therefore issue two full `NavigateToString` operations without versioning; state is marked Ready immediately after navigation is requested rather than after completion.
- **Impact:** Larger documents can flash, perform duplicate rendering work, or finish on stale content.
- **Remediation:** Observe one canonical property, serialize/version navigation requests, and transition to Ready from `NavigationCompleted`. Add a rapid successive-update test.

### Low / Nit

#### L1. Diff hygiene check fails

- **Location:** `tests/FicheGen.Core.Tests/Documents/GeneratedDocumentTests.cs:245`
- **Axes:** Readability, maintainability
- **Evidence/reasoning:** `git diff --check HEAD` exits 2 for a new blank line at EOF.
- **Impact:** Minor review noise and likely formatting-gate failure if enabled in CI.
- **Remediation:** Remove the extra blank line and add `git diff --check` to CI/pre-commit checks.

## Axis Coverage

- **Correctness and behavior:** Blocking findings cover installer execution, history hydration/undo, picker cancellation, cloud setup/token lifecycle, ARM64 packaging, manual editing, provider routing, export cancellation, generation races, and WebView sequencing.
- **Readability and simplicity:** The change improves some typed command dispatch and comments, but duplicate history hydration, duplicated localization triggers, parallel proxy implementations, and ad-hoc provider metadata increase maintenance risk.
- **Architecture:** No direct `Core -> Infrastructure/App` or `Infrastructure -> XAML` dependency reversal was found. Important seam problems remain around authentication token resolution, summary-versus-detail history models, generation ownership, and duplicate proxy implementations.
- **Security and data integrity:** Fail-closed profile checks and removal of route data from health output are positive. Blocking concerns remain around ineffective consumption controls, vulnerable SQLite native code, destructive history undo, and fail-open/broad signing.
- **Performance and responsiveness:** The bounded streaming channel and reduced history query projection are good goals, but the latter breaks behavior. Event retention, duplicate WebView navigation, duplicate localization events, and non-responsive PDF cancellation remain.
- **Tests and verification quality:** Core parser/document tests and localization parity checks are useful, but no automated tests cover the critical cross-module flows identified above. In particular, search summaries are never opened/restored, default cloud onboarding is not exercised against authentication, and packaging metadata/scripts are not validated.

## What's Done Well

- Cancellation is now rethrown through PDF/OCR paths instead of being swallowed in several places.
- Prompt construction explicitly treats PDF-derived text as untrusted reference content.
- Proxy errors no longer return provider internals to clients, and profile checks now fail closed.
- Localization resources have exact key parity across French, English, and Arabic, as confirmed by the passing parity tests.
- Both x64 and ARM64 project builds complete with zero compiler warnings.

## Verification Performed

| Command/check | Outcome |
| --- | --- |
| `graphify query "What are the architectural relationships, layer boundaries, tests, and likely impact radius of changes across FicheGen.App, FicheGen.Infrastructure, and FicheGen.Core?" --budget 3000` | Completed; 403-node result, truncated to budget. |
| `graphify query "Trace HistoryRepository.SearchAsync results through HistoryViewModel open, export, delete, and restore/undo flows. What fields are required?" --budget 5000` | Completed; confirmed history-to-preview/export impact path. |
| `graphify query "Trace GenerationResult RawPrompt RawResponse and HistoryItem persistence from generation ViewModels into HistoryRepository and later history UI." --budget 5000` | Completed; confirmed persistence call sites. |
| `graphify query "Trace cloud generation authentication from FicheFormViewModel through AiRequestConfig, LlmRouter, OpenAiCompatibleAdapter, and SupabaseAuthService token refresh." --budget 4500` | Completed; confirmed token resolver bypass. |
| `git status --short --branch`, `git branch --show-current`, `git remote -v`, `git log --oneline --decorate -15` | Inspected current branch, upstream, worktree, and recent history. |
| `git rev-parse origin/main`; `git merge-base HEAD origin/main`; root/history checks | `origin/main` resolves, but merge-base exits 1 because histories are unrelated. Local `master..HEAD` contains 12 commits. |
| `git diff --stat`, `git diff --name-status`, `git diff --numstat`, `git diff --cached ...`, `git ls-files --others --exclude-standard` | Reviewed unstaged/staged/untracked scope; no staged changes. |
| Multiple scoped `git diff --unified=... HEAD -- <paths>` inspections | Reviewed test diffs first, then Core, Infrastructure, App/UI, server, installer, docs, and project configuration changes. |
| `dotnet test tests/FicheGen.Core.Tests/FicheGen.Core.Tests.csproj --no-restore` | **Pass:** 69 passed, 0 failed, 0 skipped. |
| `dotnet test tests/FicheGen.Infrastructure.Tests/FicheGen.Infrastructure.Tests.csproj --no-restore` | **Pass:** 92 passed, 0 failed, 0 skipped. |
| `dotnet build src/FicheGen.App/FicheGen.App.csproj -c Debug -p:Platform=x64 --no-restore` | **Pass:** 0 warnings, 0 errors. |
| `dotnet build FicheGen.Windows.slnx -c Debug -p:Platform=ARM64 --no-restore` | **Pass:** all projects compiled, 0 warnings, 0 errors. This does not build/validate the MSIX package. |
| `node --check server/worker.js` | **Pass** (Node v24.19.0). |
| PowerShell AST parse of changed/new scripts | `scripts/build-installers.ps1` and `scripts/sign-binaries.ps1` pass; `installer/Install-MSIX.ps1` fails with one unterminated-string parser error. |
| `powershell -NoProfile -NonInteractive -File installer/Install-MSIX.ps1 -MsixPath missing.msix -CertPath missing.cer` | **Fail before execution:** parser error at line 99, exit 1. |
| `dotnet list FicheGen.Windows.slnx package --vulnerable --include-transitive` | Completed; found the runtime SQLite advisory plus test-only advisories noted below. |
| `dotnet list src/FicheGen.Infrastructure/FicheGen.Infrastructure.csproj package --include-transitive` | Confirmed the vulnerable SQLite native package is a top-level Infrastructure dependency and runtime transitive dependency. |
| `git diff --check HEAD` | **Fail:** blank line at EOF in `GeneratedDocumentTests.cs`; also emitted broad LF-to-CRLF conversion warnings. |
| Resource parity/count script over the three `.resw` files | 762 keys in each locale; no key-count mismatch. |
| XAML `x:Uid` to French-resource prefix scan | One UID without a resource entry: `FRD_ProviderCloud`; it is a container without a directly localized property, so no defect was asserted. |
| `Get-Command deno` | Deno unavailable; the Supabase Edge Function could not be type-checked/executed locally. |

**Not run:** interactive E2E tests (require a suitable interactive Windows desktop), actual MSIX packaging/install, certificate-store mutation, external provider calls, Narrator/High Contrast/reduced-motion visual checks, and Deno type checking.

## Open Questions / Assumptions

1. Is the cloud service intended to require a teacher account, or should a bounded anonymous/community flow remain? Current onboarding and server policy disagree.
2. Which proxy is authoritative in production: `server/worker.js` or `supabase/functions/chat/index.ts`? They already differ in request-size and Google-provider behavior.
3. Are manual edits expected to update the original history item or create a revision? The current “saved” wording implies durability, but no persistence contract is present.
4. Is release signing intended for development sideloads only? If production distribution uses this path, certificate identity, timestamping, and fail-closed verification need an explicit policy.
5. The untracked UI/UX review was used as contextual evidence only; source and tests were treated as authoritative.

## Residual Risks

- Interactive WinUI behavior was not executable in this environment, so focus order, Narrator announcements, RTL layout, high-contrast theme changes, drag/drop feedback, and actual WebView rendering still need manual validation.
- The x64/ARM64 builds compile but do not validate deployable MSIX contents or installability.
- The dependency audit also reports critical/high issues in test-only transitive packages (`System.Drawing.Common`, `Scriban.Signed`, and `System.Linq.Dynamic.Core`). The reported `System.Drawing.Common` issue applies to macOS/Linux and the affected package is in the Windows E2E project; Scriban/Dynamic LINQ appear through test tooling. Confirm reachability, then upgrade the owning test dependencies.
- No lock files are present, so transitive dependency resolution is not fully reproducible.
- Because `origin/main` has unrelated history, this review cannot establish whether the 12 commits on `windows-native` are semantically equivalent to work on the default branch; repository history should be reconciled before a normal PR merge.
