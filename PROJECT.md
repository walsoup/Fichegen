# Project: PROFstudio

## Architecture
PROFstudio is a native WinUI 3 desktop application (.NET 8 / C# 12) for French teachers.
Architecture follows 4 strict layers:
- `FicheGen.Core` (net8.0, zero UI/IO, System.* + STJ only)
- `FicheGen.Infrastructure` (net8.0-windows10.0.19041.0, Core interfaces implementation)
- `FicheGen.App` / Services (UI-thread aware application orchestrators)
- `FicheGen.App` (WinUI 3 XAML pages, ViewModels, WebView2 hosts)

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | PDF Export | Implement off-screen `CoreWebView2.PrintToPdfAsync()` in `WebView2PdfExporter.cs` per Blueprint §4.C.3 | M1 | survey |
| 2 | VM Concurrency & Cancellation | `CanExecute` guards & cancel-previous `CancellationTokenSource` in `FicheForm`, `Evaluation`, `Quiz`, `Assistant` VMs | M1 | survey |
| 3 | LLM Resilience & Adapters | Polly pipeline in `LlmClient.GenerateStreamAsync`, HTTP status check before stream parsing, UTF-8 chunk decoder in `PlainTextStreamParser`, Vertex `?alt=sse`, OpenAI stream flag fix, `StreamingChannelPipeline` try/finally & throttle interval | M1 | survey |
| 4 | Data Security & Immutability | `ImmutableDictionary` for `SecretsProtected` in `AppSettings`, read-only block collections in `GeneratedDocument`, secret redaction in DTO `ToString()`, atomic file write flush in `SettingsStore` | M1 | survey |
| 5 | Search Debounce | 300ms debounce timer, CTS disposal, cancellation token propagation, `IDisposable` in `HistoryViewModel` | M2 | survey |
| 6 | Security Inputs | `PasswordBox` controls with show/hide toggle for API keys in `SettingsPage.xaml` | M2 | survey |
| 7 | Clipboard Byte Offsets | Explicit CRLF headers & UTF-8 byte length calculation with French accent tests in `ClipboardPackageBuilder` | M2 | survey |
| 8 | UX Polish & Accessibility | `ContentDialog` deletion confirmation, empty states primary CTA, `AutomationProperties.Name` on buttons/chips, form validation InfoBar | M2 | survey |
| 9 | Credential & DPAPI Security | Purge corrupt `.dpapi` files in `DpapiCredentialStore`, clear fallback in `CredentialLockerStore`, implement `SecretRedactingPolicy.Enrich`, sanitize FTS5 queries & 5s busy timeout in `HistoryRepository` | M2 | survey |
| 10 | MainWindow Refactoring | Decompose 1375-line `MainWindow.xaml.cs` into 6 services/helpers and 3 partial classes | M3 | survey |
| 11 | Assistant Toggle | Implement `ToggleAssistantVisibility(bool)` body and `AssistantVisibilityChangedMessage` | M3 | survey |
| 12 | Duplicate Navigation Removal | Consolidate initial navigation into `NavigationService.EnsureInitialNavigation()` | M3 | survey |
| 13 | Reflection Command Removal | Replace `TryExecuteCommand`/`TryInvokeMethod` with `WeakReferenceMessenger` typed messages | M3 | survey |
| 14 | Settings Decomposition | Split `SettingsViewModel` into 8 partial classes and `SettingsPage.xaml` into 6 `UserControl`s with `x:Load="False"` | M3 | survey |
| 15 | Memory Leaks & Cleanup | Unsubscribe `AssistantViewModel` event handlers, stop `DispatcherQueueTimer` instances in ViewModels, seal uninherited classes | M3 | survey |
| 16 | Unit Test Coverage & Standardization | Increase `FicheGen.Core` coverage to ≥85%, add missing tests (`FallbackMarkdownRenderer`, streaming parsers, resilience, WireMock Vertex, CF_HTML), convert xUnit `Assert` to FluentAssertions, enforce `Method_State_ExpectedResult` naming | M4 | survey |
| 17 | E2E Test Pass & Adversarial Hardening | Pass 100% E2E test suite (Tiers 1-4) and complete Tier 5 white-box adversarial coverage hardening | M5 | survey |

## Code Layout
```
src/
├── FicheGen.Core/              # Zero UI, zero IO, System.* + STJ only
│   ├── Ai/                     # Prompt builders, LLM requests/configs
│   ├── Documents/              # GeneratedDocument polymorphic block schema
│   ├── Storage/                # AppSettings, HistoryEntry models
│   ├── Toc/                    # TocParser (3-tier regex)
│   └── Services/               # Core domain services
├── FicheGen.Infrastructure/    # Implementation of Core interfaces
│   ├── Ai/                     # LlmClient, LlmRouter, 4 Adapters, Streaming pipelines, Polly resilience
│   ├── Storage/                # HistoryRepository (SQLite FTS5), SettingsStore
│   ├── Security/               # DpapiCredentialStore, CredentialLockerStore
│   ├── Pdf/                    # PdfGuideService (PdfPig 1-indexed)
│   ├── Export/                 # ClipboardPackageBuilder, RtfDocumentWriter, DocxExporter
│   └── Diagnostics/            # SecretRedactingPolicy
└── FicheGen.App/               # WinUI 3 XAML, ViewModels, WebView2 host
    ├── ViewModels/             # CommunityToolkit.Mvvm ViewModels
    │   └── Settings/           # Tab-specific partial classes for SettingsViewModel
    ├── Views/                  # XAML pages (FichePage, SettingsPage, HistoryPage, etc.)
    │   └── Controls/ Settings/ # Modular UserControl tabs for SettingsPage
    ├── Services/               # WebView2PdfExporter, NavigationService, WebView2Manager, ThemeService, etc.
    └── Messages/               # WeakReferenceMessenger message records
tests/
├── FicheGen.Core.Tests/        # Core unit tests (Target ≥ 85%)
└── FicheGen.Infrastructure.Tests/ # Infrastructure unit tests (WireMock.Net, SQLite, Export)
```

## Interface Contracts

### NavigationService Contract
- `void NavigateTo(Type pageType, object parameter)`
- `void EnsureInitialNavigation(NavigationView navView, Frame frame, string defaultTag)`

### WeakReferenceMessenger Message Records
- `record TriggerGenerationMessage()`
- `record CancelGenerationMessage()`
- `record CreateNewFicheMessage()`
- `record ExportDocumentMessage()`
- `record TogglePreviewPaneMessage()`
- `record PrintDocumentMessage()`
- `record UndoLastActionMessage()`
- `record GlobalSearchQueryMessage(string Query)`
- `record AssistantVisibilityChangedMessage(bool IsVisible)`

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M0 | E2E Testing Track | Design and build comprehensive opaque-box E2E test suite (Tiers 1-4) & publish `TEST_READY.md` | none | PLANNED |
| M1 | Phase 1: P0/P1 Critical Bugs | PDF export stub fix, VM race condition & cancellation guards, LLM resilience & adapters fixes, data security & immutability | none | PLANNED |
| M2 | Phase 2: UX Polish & Security | History search 300ms debounce, PasswordBox API keys, Clipboard CF_HTML UTF-8 byte offsets, UX loading/empty/dialog states, DPAPI/FTS5 security | M1 | PLANNED |
| M3 | Phase 3: Architecture & Maintainability | Decompose `MainWindow.xaml.cs` (6 services), implement assistant toggle, remove reflection command dispatching, decompose `SettingsViewModel` & `SettingsPage.xaml`, fix VM memory leaks | M1 | PLANNED |
| M4 | Phase 4: Test Coverage & Standardization | Increase Core coverage to ≥85%, add missing infrastructure tests, convert to FluentAssertions, standardize test method naming | M1, M2, M3 | PLANNED |
| M5 | Final Milestone: E2E Test Pass & Adversarial Hardening | Pass 100% of E2E test suite (Tiers 1-4) and complete Tier 5 white-box adversarial coverage hardening | M0, M1, M2, M3, M4 | PLANNED |
