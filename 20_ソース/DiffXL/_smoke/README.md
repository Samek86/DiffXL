# DiffXL smoke harnesses (shipped code)

These tools call the **real** `DiffXL.exe` types (`DiffEngine`, `ExcelWorkbookSession`, `AppSettings`), not reimplementations.

## Prerequisites

- Built `DiffXL\bin\x64\Debug\DiffXL.exe`
- Excel desktop installed (x64)

## SmokeGoto

```powershell
# After Debug build + csc (see goal evidence scripts)
.\SmokeGoto.exe path\to\left.xlsx path\to\right.xlsx
```

Exercises `DiffEngine.Compare` and `TryGotoRow` / `TrySetScroll` / `TryGetScroll`.

## SmokeEmbedGoto / DualEmbed

WPF hosts that `Attach` Excel into `ExcelHostControl` then jump rows (proves MiniMap path after embed).

## SettingsSmoke

Persists highlight color/opacity via `AppSettings` YAML under `%AppData%\Roaming\DiffXL`.
