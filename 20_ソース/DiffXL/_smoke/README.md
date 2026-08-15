# DiffXL smoke harnesses

These tools reference types inside the built `DiffXL.exe` (`DiffEngine`, `ContentStreamBuilder`, `AppSettings`).

## Prerequisites

- Built `DiffXL\bin\x64\Debug\DiffXL.exe`
- Microsoft Excel is **not** required

## Logic smokes (current)

Compile with `csc /r:DiffXL.exe` and run from the Debug output directory.

- `ContentDiffSmoke` — content-based compare scenarios
- `ContentStreamSmoke` — stream align + layout
- `MiniMapViewportBandSmoke` — MiniMap thumb math
- `ImageOverlayAlignSmoke` — overlay aligner

Excel COM embed smokes (`SmokeGoto`, `SmokeEmbedGoto`, `DualEmbed`) are **retired**. Do not compile them.
