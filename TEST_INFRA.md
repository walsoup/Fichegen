# E2E Test Infra: PROFstudio

## Test Philosophy
- Opaque-box, requirement-driven. No dependency on implementation design.
- Methodology: Category-Partition + BVA + Pairwise + Workload Testing.
- Framework: FlaUI / WinAppDriver via xUnit (e.g. `tests/FicheGen.E2E.Tests`) for native WinUI 3 application testing.

## Feature Inventory
| # | Feature | Source (requirement) | Tier 1 | Tier 2 | Tier 3 |
|---|---------|---------------------|:------:|:------:|:------:|
| 1 | Document Generation | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 2 | Generation Cancellation | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 3 | Export to PDF | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 4 | Export to Clipboard (HTML/Word) | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 5 | Settings & API Keys Input | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 6 | History Search & Retrieval | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 7 | Assistant Chat & Toggle | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ |
| 8 | UX Dialogs & Empty States | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |

## Test Architecture
- **Test runner**: `dotnet test tests/FicheGen.E2E.Tests/FicheGen.E2E.Tests.csproj`
- **Test case format**: xUnit tests using FlaUI for UI automation to verify expected states.
- **Directory layout**:
  ```
  tests/FicheGen.E2E.Tests/
  ├── Infrastructure/    # App lifecycle, FlaUI setup
  ├── Tier1_Features/
  ├── Tier2_Boundaries/
  ├── Tier3_Pairwise/
  └── Tier4_Scenarios/
  ```

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Full end-to-end lesson generation | Generation, History, Assistant, PDF Export | High |
| 2 | Settings configuration & cancel | Settings, Security Inputs, Dialogs | Medium |
| 3 | History search and clipboard export | History Search, Clipboard, Preview | Medium |
| 4 | Aborted generation during chat | Cancellation, Assistant Chat, Generation | High |
| 5 | First launch experience | Empty states, Dialogs | Low |

## Coverage Thresholds
- Tier 1: ≥5 per feature (Total ~40 tests)
- Tier 2: ≥5 per feature (where boundaries exist) (Total ~40 tests)
- Tier 3: pairwise coverage of major feature interactions (Total ~28 tests)
- Tier 4: ≥5 realistic application scenarios (Total 5 tests)
