# MiniMap Scrub Performance Implementation Plan

**Goal:** Smooth MiniMap scrub with real-time position; placeholders OK during fast drag (option A).

**Architecture:** Frame-coalesce ratio applies; Scrub mode throttles full row realize and uses placeholders; ScrubEnd forces full realize.

**Files:** MiniMapControl, MainWindow, ContentPane, WorkbookPane.
