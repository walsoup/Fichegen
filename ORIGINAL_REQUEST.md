# Original User Request

## 2026-08-06T02:30:57Z

<USER_REQUEST>
Fix all identified issues across Phase 1 (P0/P1 Critical Bugs), Phase 2 (UX Polish), Phase 3 (Architecture & Maintainability), and Phase 4 (Test Coverage) in PROFstudio as documented in project_review.md.

Working directory: c:/Users/walid/work/goofy-goodall
Integrity mode: development

## Requirements

### R1. Phase 1 Critical Bugs & System Robustness (P0 & P1)
- **PDF Export**: Replace `WebView2PdfExporter` stub with complete `CoreWebView2.PrintToPdfAsync()` implementation following `WINDOWS_BLUEPRINT.md` §4.C.3.
- **VM Race Conditions & Cancellation**: Implement `CanExecute` guard for active generations and cancel-previous pattern (`CancellationTokenSource`) across `FicheFormViewModel`, `EvaluationViewModel`, `QuizViewModel`, and `AssistantViewModel`.
- **LLM Resilience & Adapters**: Wrap streaming in Polly resilience pipelines in `LlmClient`, check HTTP success status before parsing error streams, fix UTF-8 string chunk boundary decoding in `PlainTextStreamParser`, add Vertex `?alt=sse` parameter, and ensure `StreamingChannelPipeline` closes channel on exception.
- **Data Security & Immutability**: Replace mutable `Dictionary` with `ImmutableDictionary` for `SecretsProtected` in `AppSettings`, make `GeneratedDocument` blocks read-only/immutable collections, redact API keys in record `ToString()`, and implement atomic file writes in `SettingsStore`.

### R2. Maintainability & Code Quality (Phase 3 & Known Issues)
- **MainWindow Refactoring**: Decompose 1,375-line `MainWindow.xaml.cs` into modular services/helpers (`NavigationService`, `WebView2Manager`, `AssistantPanelHelper`, `PreviewService`, `ThemeService`, `NotificationService`).
- **Missing Assistant Toggle**: Implement `ToggleAssistantVisibility(bool)` in `MainWindow.xaml.cs`.
- **Settings Refactoring**: Split 64KB `SettingsViewModel.cs` into tab-specific partial classes and decompose `SettingsPage.xaml` into modular `UserControl`s.
- **Command Dispatching**: Replace reflection-based `TryExecuteCommand`/`TryInvokeMethod` in `MainWindow` with `WeakReferenceMessenger`.
- **Memory & Resource Cleanup**: Fix event handler memory leaks (e.g. `AssistantViewModel` unsubscribing), stop/dispose `DispatcherQueueTimer` instances, and seal internal/uninherited classes.

### R3. UX Polish & Security (Phase 2)
- **Search Debounce**: Implement 300ms debounce timer for History search FTS5 queries in `HistoryViewModel`.
- **Security Inputs**: Replace plain `TextBox` controls for API key inputs on Settings page with `PasswordBox`.
- **Clipboard Byte Offsets**: Fix CF_HTML byte offset calculation in `ClipboardExporter` to use UTF-8 byte length instead of string length.
- **Feedback & Validation**: Add loading indicators, empty states, deletion confirmation dialogs, and basic form validation across views.

### R4. Test Coverage & Verification (Phase 4)
- **Core Coverage**: Increase `FicheGen.Core` test coverage to >= 85%.
- **Infrastructure & Adapter Tests**: Add unit tests for `FallbackMarkdownRenderer`, streaming pipelines, SSE parser, resilience retry logic, and export builders.
- **Test Standardization**: Convert test assertions to FluentAssertions and follow `Method_State_ExpectedResult` naming convention.

## Acceptance Criteria

### Automated Build & Test Verification
- [ ] Project compiles without errors or warnings: `dotnet build src/FicheGen.App/FicheGen.App.csproj -c Debug -p:Platform=x64`
- [ ] Core unit tests pass cleanly: `dotnet test tests/FicheGen.Core.Tests/FicheGen.Core.Tests.csproj`
- [ ] Infrastructure unit tests pass cleanly: `dotnet test tests/FicheGen.Infrastructure.Tests/FicheGen.Infrastructure.Tests.csproj`

### Objective Integrity & Functionality Checks
- [ ] `WebView2PdfExporter.cs` invokes `PrintToPdfAsync()` and exports valid PDF files.
- [ ] No reflection-based command invocation (`TryExecuteCommand`/`TryInvokeMethod`) remains in `MainWindow.xaml.cs`.
- [ ] `ToggleAssistantVisibility` in `MainWindow.xaml.cs` toggles assistant panel visibility.
- [ ] `SecretsProtected` in `AppSettings` uses `ImmutableDictionary<string, string>`.
- [ ] Record DTOs (`AiRequestConfig`, `RouteSelection`) override `ToString()` to redact API keys.
- [ ] `SettingsStore.cs` uses temporary file + atomic replace for saves.
- [ ] All unit test assertions use FluentAssertions.
</USER_REQUEST>
