# PROFstudio Agent Guide

## Start Here

Before substantial work:

1. Apply the global skill, Graphify, and delegation gates.
2. Read the files you will change, their tests, and one nearby example.
3. Read `docs/WINDOWS_BLUEPRINT.md` before architectural changes or when intended behavior is unclear. Source code and tests remain authoritative for current behavior.

In the first progress update, name the skills loaded, whether Graphify was queried, and any subagents delegated. This makes orchestration observable.

## Skill Routing

Load every skill that materially changes the workflow; avoid decorative loading.

| Work | Required skill |
|:---|:---|
| WinUI/XAML, theming, layout, controls, accessibility | `winui-design` |
| Bug, failing test, build break, unexplained behavior | `diagnosing-bugs` or `debugging-and-error-recovery` |
| Logic or behavior change | `test-driven-development` |
| Multi-file implementation | `incremental-implementation` |
| Architecture or module boundaries | `codebase-design`; consult the blueprint |
| Secrets, auth, external input, storage, network integrations | `security-and-hardening` |
| Performance or responsiveness | `performance-optimization` |
| Review request | `code-review` or `code-review-and-quality` |
| Agent/OpenCode instruction changes | `writing-for-agents` and `customize-opencode` |

Use a specialized subagent when its expertise materially improves the result. Run independent investigations concurrently. Keep tightly coupled edits in the main agent, and use `explore` only for broad discovery rather than reading a known file.

## Product And Stack

PROFstudio is a native WinUI 3 desktop application for French teachers. It generates lesson plans, evaluations, and quizzes through multiple LLM providers, with PDF guide integration, WebView2 preview, local history, and multi-format export.

- Solution: `FicheGen.Windows.slnx`
- Runtime: .NET 8; C# 12 by default
- UI: WinUI 3 / Windows App SDK 1.6
- MVVM: CommunityToolkit.Mvvm source generators
- Targets: Windows 10 1809+ and Windows 11; x64 and ARM64

## Architecture Boundaries

Dependency direction:

```text
FicheGen.App -> FicheGen.Infrastructure -> FicheGen.Core
      |                                      ^
      +--------------------------------------+
```

- `FicheGen.Core`: domain models, contracts, and pure logic. No UI or I/O APIs. Keep dependencies minimal; current package references are the source of truth.
- `FicheGen.Infrastructure`: implements Core contracts for HTTP, PDF, storage, credentials, export, and diagnostics. No XAML or dispatcher dependencies.
- `FicheGen.App/Services`: UI-aware, view-free application workflows and platform adapters.
- `FicheGen.App`: XAML, controls, pages, ViewModels, and composition root.
- ViewModels call services; they do not access HTTP, SQLite, PDF APIs, or the file system directly.
- Data crossing thread boundaries must be immutable.
- `WebView2PdfExporter` intentionally lives in App behind `IDocumentPdfExporter` because WebView2 is UI-affined.

## Implementation Conventions

- Use nullable annotations and existing C# 12 patterns.
- Make non-extensible classes `sealed`.
- Async APIs accept and propagate `CancellationToken` unless the framework fixes the signature.
- Outside ViewModels and UI-affined code, use `ConfigureAwait(false)`.
- Move CPU-bound work off the UI thread; keep UI-bound work on the dispatcher.
- Use `[ObservableProperty]`, `[RelayCommand]`, and other source-generated MVVM features. Do not add reflection-based command dispatch.
- Use English for identifiers, comments, and logs. Put user-facing text in `.resw`; update `fr-FR`, `en-US`, and `ar-SA` together.
- Preserve established local style instead of applying unrelated cleanup.

## High-Risk Invariants

- PdfPig page numbers are 1-based. Preserve `physicalPage = printedPage + offset` semantics.
- Initialize WebView2 with `EnsureCoreWebView2Async` before accessing `CoreWebView2`.
- Exchange preview data with `PostWebMessageAsJson` and virtual-host mapping, not a Base64 HTML bridge.
- Place WebView2 in a star-sized or fixed-height row, never an auto-sized viewport row.
- Initialize WinRT file pickers with the owning window handle.
- CF_HTML offsets are UTF-8 byte offsets, not character indexes.
- Keep credentials out of settings, exports, diagnostics, and logs. DPAPI data is user-and-machine bound.
- Preserve `privateNetworkClientServer` for packaged Ollama loopback access.
- Custom controls that wrap WebView2 must observe `DataContext` and property changes and must forward toolbar commands when handlers are absent.

## Verification

Use the narrowest relevant checks first, then broaden:

```powershell
dotnet test tests/FicheGen.Core.Tests/FicheGen.Core.Tests.csproj
dotnet test tests/FicheGen.Infrastructure.Tests/FicheGen.Infrastructure.Tests.csproj
dotnet build src/FicheGen.App/FicheGen.App.csproj -c Debug -p:Platform=x64
```

- Tests use xUnit. Follow the assertion style already used in the touched test file; do not rewrite unrelated tests.
- Name tests `Method_State_ExpectedResult`.
- Use WireMock.Net for HTTP/SSE adapter behavior and Verify.Xunit for stable renderer or prompt snapshots.
- Add or update tests for behavior changes and public code.
- E2E tests under `tests/FicheGen.E2E.Tests` require a suitable interactive Windows environment; run them only when relevant and report if the environment prevents execution.
- Report exactly what ran and any checks that could not run.

## Repository Safety

- The worktree may contain concurrent user changes. Inspect diffs before editing and never revert unrelated work.
- Do not commit, amend, push, or alter Git configuration unless explicitly requested.
- Treat `docs/WINDOWS_BLUEPRINT.md` as design intent, not permission to overwrite newer source behavior.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For architecture, dependencies, data flow, impact radius, or unfamiliar implementation, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
