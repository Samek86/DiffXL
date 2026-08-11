# Phosphor Icons UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce MahApps.Metro.IconPacks Phosphor Icons across DiffXL UI with icon+label content on all primary controls.

**Architecture:** Add only `MahApps.Metro.IconPacks.PhosphorIcons` NuGet package. Place `PackIconPhosphorIcons` inside button contents via StackPanel. Keep existing styles; fix Foreground inheritance if needed. For dynamic toggle caption, update a named TextBlock instead of replacing entire Content.

**Tech Stack:** WPF, .NET Framework 4.8, MahApps.Metro.IconPacks.PhosphorIcons 6.2.1, Costura.Fody

## Global Constraints

- Phosphor Icons only (no Boxicons, no full IconPacks meta package)
- Icon + label (no icon-only toolbar mode)
- No MahApps.Metro theme package
- Costura single-exe embedding must keep working
- Do not change compare/session logic except caption update for highlight toggle
- Source roots under `20_ソース/DiffXL/DiffXL/`

---

### Task 1: Package + highlight toggle code-behind safe pattern

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj` (PackageReference — may already be present)
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml` (toolbar toggle structure)
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs` (`UpdateHighlightToggleCaption`)

**Interfaces:**
- Produces: `BtnHighlightToggleLabel` (TextBlock) inside toggle Content; `UpdateHighlightToggleCaption` sets `.Text` only

- [ ] Add package if missing: `MahApps.Metro.IconPacks.PhosphorIcons` 6.2.1
- [ ] Toggle Content = StackPanel with PackIcon + named TextBlock `BtnHighlightToggleLabel`
- [ ] Change `UpdateHighlightToggleCaption` to set label Text only
- [ ] Build x64 Debug

### Task 2: MainWindow + StartupPanel icons

**Files:**
- Modify: `VIEW/MainWindow.xaml`
- Modify: `VIEW/StartupPanel.xaml`

- [ ] Add `xmlns:iconPacks` to both
- [ ] Icon+label all toolbar buttons, sheet label, loading, status diff
- [ ] Icon+label startup title, browse, compare
- [ ] Build

### Task 3: Settings, dialogs, WorkbookPane

**Files:**
- Modify: `VIEW/SettingsWindow.xaml`
- Modify: `VIEW/Dialogs/SheetMapDialog.xaml`
- Modify: `VIEW/Dialogs/AnchorDialog.xaml`
- Modify: `VIEW/Controls/WorkbookPane.xaml`
- Modify: `STYLE/CommonStyle.xaml` only if Foreground inheritance breaks icons

- [ ] Icon section headers and OK/Cancel/Save buttons
- [ ] WorkbookPane Open button icon
- [ ] Build and smoke-run if possible

### Task 4: Verify

- [ ] msbuild x64 Debug succeeds
- [ ] Kind names compile (no XAML parse errors)
- [ ] Document any Kind renames if enum differed from design
