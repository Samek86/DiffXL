# Content Stream Performance Implementation Plan

> **For agentic workers:** Inline execution in the approving session.

**Goal:** Make long sheets (≈1000 table rows) render and scroll smoothly via shared layout + stream virtualization with table-row expansion.

**Architecture:** Expand aligned tables into header+row stream pairs; share one `ContentStreamLayout` (pairs + height map) across left/right panes; render only viewport items with top/bottom spacers.

**Tech Stack:** WPF (.NET Framework 4.8), existing DiffXL content stream.

## Global Constraints

- Do not change comparison semantics (position-agnostic content diff).
- Keep MiniMap ratio scroll and pair jump working via height map offsets.
- Prefer estimates over full-tree `UpdateLayout`.

## Tasks

### Task 1: Expand + shared layout in ContentStreamBuilder

- Add `TableHeader` / `TableRow` kinds
- `AlignAndExpand` / `GetOrBuildLayout` / `ContentStreamLayout`
- Smoke: expand 1000-row table → ~1001 pairs, not 1

### Task 2: Virtualizing ContentPane

- Spacer host; realize viewport only
- Use shared layout; remove SyncPairHeights full measure
- Lightweight table row UI (no nested ItemsControl)

### Task 3: Wire navigation + verify

- ScrollToPairIndex via offset
- DiffItem → row index for table diffs
- Build + ContentStreamSmoke + stress path sanity
