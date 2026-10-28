# Word and Excel Workspace Implementation Plan

> **For agentic workers:** Use test-driven-development for behavior and independently scoped parallel workers for Excel services and XAML. Parent integrates and verifies.

**Goal:** Scan, classify, review and safely replace Word and .xlsx dates through one clearly divided desktop workspace.
**Architecture:** ExcelDocumentService implements the three document interfaces; a format router dispatches by extension. Existing date candidates retain exact cell-relative offsets; worksheet/cell metadata travels in DocumentTextSegment. ViewModels expose file/sheet/context and readiness for redesigned WPF panels.
**Tech Stack:** .NET 8 WPF, WPF-UI, DocumentFormat.OpenXml 3.1.1, xUnit.
**Spec:** docs/superpowers/specs/2026-10-22-word-excel-workspace.md

## Constraints
Use existing packages. No edits to source documents or formulas. Preserve .xlsx extension and numeric date storage. .xls excluded. SDK C:/Users/immin/AppData/Local/Microsoft/dotnet/dotnet.exe. Workspace has no git metadata, so no commits/worktrees.

## Review Focus
- General numeric cells must not become dates.
- Formula dates remain visible but unselectable and cannot enable execution.
- Mixed Word/Excel batches route verification correctly and produce distinct correct extensions.
- Hidden sheets obey the user's scan option; worksheet filtering must not alter other selections.
- Borders/split panes/footers remain usable at 1160x780 and smaller supported window size.

## Tasks
- [x] Excel service and real workbook tests (worker): create Infrastructure/Services/ExcelDocumentService.cs; extend DocumentTextSegment and StoryType; tests Core.Tests/ExcelDocumentServiceTests.cs. Scaffold→failing scan/replace tests→implementation→focused tests.
- [x] UI XAML (worker): MainWindow.xaml, ReviewView.xaml/.cs, App.xaml resources as needed. Section borders, compact split lists, workflow steps, menu, density, file/sheet controls. Compile XAML.
- [x] Integration (parent): new App/Services/DocumentFormatRouter.cs implements scanner/replacer/verifier; App.xaml.cs DI; FileDialogService mixed extensions; Dashboard scan/import/file details; Execution preserves extension. Add integration regression tests before edits.
- [x] Review safety/context (parent): DateItemViewModel respects IsSupportedForReplacement; ReviewViewModel readiness, sheet filter and nearby-cell grid. Add regression tests for protected dates and readiness.
- [x] Verification/delivery: full solution tests, fresh reviewer, fix important findings, Release publish Publish_App_Excel, new launcher, manual render/UI verification if enabled surfaces permit.

## Ledger
Started: user authorized implementation of the described design. Agents have disjoint write scopes. Parent owns VMs, routing, integration tests, documentation and packaging.

Completed: 241/241 solution tests pass (Core 143, Word 32, App 66). Real Excel end-to-end scan/classify/review/replace/verify passed. Fresh review findings fixed with regression tests: exact worksheet identifiers, applicable number-format sections, locked cells, hidden-sheet collection filtering, populated sheet selector. Native dashboard and Excel review grid visually inspected. Published framework-dependent win-x64 Release in Publish_App_Excel; Chay_App.bat now opens it. No .xls support.

