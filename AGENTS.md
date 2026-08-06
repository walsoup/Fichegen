# PROFstudio for Windows — Agent Instructions

## Subagent Policy

**When invoking subagents, NEVER use `Model: "inherit"`.** Always select one of:

| Model | Use When |
|:---|:---|
| `flash_lite` | Trivial lookups, file reads, simple searches |
| `flash` | Research, code review, targeted refactors, writing tests |
| `pro` | Complex architectural changes, multi-file refactors, debugging subtle issues |

## Project Overview

PROFstudio is a **native WinUI 3 desktop application** (.NET 8 / C# 12) for French teachers. It generates pedagogical documents (lesson plans, evaluations, quizzes) using LLM APIs, with PDF guide textbook integration, WebView2 preview, multi-format export, and an interactive AI assistant.

- **Solution**: `FicheGen.Windows.slnx`
- **Target**: Windows 10 (1809+) and Windows 11, x64 and ARM64
- **Language**: C# 12 / .NET 8 LTS
- **UI Framework**: WinUI 3 (Windows App SDK 1.6)
- **MVVM**: CommunityToolkit.Mvvm (source generators)
- **Blueprint**: `docs/WINDOWS_BLUEPRINT.md` — the authoritative design document (949 lines). Read it before making architectural decisions. For any in-depth details about the system's intended design, architecture, or edge cases, you MUST consult this blueprint.

## Architecture — 4 Strict Layers

```
FicheGen.Core            →  net8.0 (plain). Zero UI, zero I/O. Only System.* + STJ.
FicheGen.Infrastructure  →  net8.0-windows10.0.19041.0. Implements Core interfaces.
FicheGen.App/Services    →  Application orchestrators. UI-thread-aware, view-free.
FicheGen.App             →  XAML pages, ViewModels, WebView2 hosts, converters.
```

### Layer Rules — NEVER Violate

1. **Core** must NEVER reference Infrastructure, App, or any UI/IO namespace. It references only `System.*` and `System.Text.Json`. If you need to add a dependency to Core, think twice — it probably belongs in Infrastructure.
2. **Infrastructure** must NEVER reference `DispatcherQueue`, XAML types, or any UI framework. The one exception is `WebView2PdfExporter` which lives in App (not Infrastructure) behind `IDocumentPdfExporter`.
3. **ViewModels** must NEVER directly touch `HttpClient`, `PdfDocument`, files, or SQLite. They call Application Services only.
4. **Everything crossing a thread boundary must be an immutable record** (`AiRequestConfig`, `FicheParameters`, `LessonContext`, etc.). No mutable objects shared between threads.

## Code Conventions

### C# Style
- Use C# 12 features: records, `required` properties, primary constructors, raw string literals, collection expressions where appropriate
- Nullable reference types are **enabled globally** (`Directory.Build.props`)
- Use `ConfigureAwait(false)` on all awaits outside ViewModels
- CPU-bound work goes in `Task.Run`, never on the UI thread
- All async methods must accept and propagate `CancellationToken`
- Use `sealed` on classes that aren't designed for inheritance

### MVVM
- Use CommunityToolkit.Mvvm source generators: `[ObservableProperty]`, `[RelayCommand]`, `ObservableValidator`
- No reflection-based MVVM — source-gen only
- ViewModels live in `FicheGen.App/ViewModels/`

### Naming
- French for user-facing strings (`.resw` resources, UI labels, prompt templates)
- English for all code identifiers, comments, and log messages
- Namespaces match folder paths

### Immutable Records for DTOs
```csharp
// Good — immutable, cross-thread safe
public sealed record AiRequestConfig(string GlobalProvider, ...);

// Bad — mutable class shared across threads
public class AiRequestConfig { public string GlobalProvider { get; set; } }
```

## Key Subsystems to Know

| Subsystem | Entry Point | Notes |
|:---|:---|:---|
| LLM routing | `LlmRouter` → `IProviderAdapter` × 4 | Gemini, Vertex, OpenAI-compatible, Vercel |
| Generation pipeline | `GenerationOrchestrator` | Coordinates PDF → prompt → LLM → parse → render |
| Document model | `GeneratedDocument` (Core) | Polymorphic block schema with `[JsonDerivedType]` |
| PDF extraction | `PdfGuideService` (PdfPig) | 1-based pages. `physicalPage = printedPage + offset` |
| ToC parsing | `TocParser` (3-tier regex) | Tier 1: dot leaders, Tier 2: column-separated, Tier 3: leading page |
| Preview | `PreviewHost` (WebView2) | Virtual host mapping, `PostWebMessageAsJson`, CSP |
| History | `HistoryRepository` (SQLite + FTS5) | WAL mode, BM25 ranking, triggers for FTS sync |
| Credentials | `CredentialLockerStore` → `DpapiCredentialStore` fallback | Secrets NEVER written to `settings.json` |
| Streaming | `SseStreamParser` → `Channel<string>` → batched UI | 50ms flush interval, capacity 256 |
| Resilience | Polly v8 pipelines | Retry 3×, jittered backoff, circuit breaker, Retry-After |

## Known Issues — Fix Before Adding Features

These are known defects. If you encounter them during work, fix them:

1. **`WebView2PdfExporter` is a stub** — it writes raw HTML to the output path instead of calling `CoreWebView2.PrintToPdfAsync()`. See `WINDOWS_BLUEPRINT.md` §4.C.3 for the correct implementation.
2. **`MainWindow.xaml.cs` is 1,375 lines** — god-object code-behind. Needs decomposition into helper classes or partial classes.
3. **`SettingsViewModel.cs` is 1,334 lines** — should be split into tab-specific partial classes.
4. **`ToggleAssistantVisibility(bool)` in `MainWindow.xaml.cs` is empty** — method body is `{ }`.
5. **Duplicate navigation logic** — initial page navigation is copy-pasted between `OnMainWindowLoaded` and `OnMainWindowActivated`.
6. **Reflection-based command dispatching** — `MainWindow` uses `TryExecuteCommand`/`TryInvokeMethod` to invoke commands on child VMs. Replace with an interface or messaging.

## Testing

- **Framework**: xUnit + FluentAssertions + Verify.Xunit + WireMock.Net
- **Test projects**: `tests/FicheGen.Core.Tests/`, `tests/FicheGen.Infrastructure.Tests/`
- Use **FluentAssertions** consistently (not xUnit `Assert`)
- Use **WireMock.Net** for HTTP/SSE simulation in adapter tests
- Use **Verify.Xunit** for golden-file snapshot tests (prompt builders, renderers)
- Test method naming: `Method_State_ExpectedResult`
- All new public code must have tests. Target ≥ 85% line coverage on Core.

## Build & Run

```powershell
# Build
dotnet build src/FicheGen.App/FicheGen.App.csproj -c Debug -p:Platform=x64

# Run tests
dotnet test tests/FicheGen.Core.Tests/FicheGen.Core.Tests.csproj
dotnet test tests/FicheGen.Infrastructure.Tests/FicheGen.Infrastructure.Tests.csproj

# Run the app
dotnet run --project src/FicheGen.App/FicheGen.App.csproj -c Debug -p:Platform=x64
```

## Platform Gotchas — Read Before Touching These Areas

1. **PdfPig pages are 1-indexed** (`GetPage(1)` = first page). This is intentional and aligns with printed page numbers. Do NOT introduce 0-indexing.
2. **No Base64 bridge for WebView2.** Use `PostWebMessageAsJson` + virtual host mapping. Never port the macOS `atob(decodeURIComponent(...))` pattern.
3. **MSIX loopback for Ollama** — `localhost:11434` requires `privateNetworkClientServer` capability in the manifest.
4. **File pickers need the window handle** — `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` or they throw.
5. **`EnsureCoreWebView2Async` before any `CoreWebView2` access.** Handle `ProcessFailed` (recreate once, then InfoBar).
6. **CF_HTML clipboard requires byte-offset headers** (`StartHTML:`, `EndHTML:`) — Word silently drops malformed payloads.
7. **DPAPI blobs are machine+user bound** — settings export must exclude `secretsProtected`.
8. **Secrets never in logs** — `SecretRedactingPolicy` strips API keys. Maintain this when adding new secret fields.
