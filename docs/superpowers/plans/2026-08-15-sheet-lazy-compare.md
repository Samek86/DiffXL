# Sheet-Lazy Compare + Phase Timings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or subagent-driven-development.

**Goal:** 比較の段階時間をログし、初回はフォーカスシートだけ読んで比較する。他シートは切替時に追加比較する。

**Architecture:** `Compare()` の既定は全シート（ContentDiffSmoke 互換）。UI は `LazySheets=true` で先頭（または選択）ペアだけ `BuildWorkbookContent`。未読シートは名前だけのスタブ。切替時 `CompareSheetPair` がスタブを実データに差し替え Items をマージする。

**Tech Stack:** .NET 4.8 / 既存 `_smoke` + csc

## Global Constraints

- `engine.Compare(left, right)` 無引数は従来どおり全シート比較。
- UI 初回だけ遅延。再比較ボタンは現在ペアをやり直し。
- 内容ベース意味は変えない。
- 段階時間は `Log.Info` と `DiffResult.Timings` と状態行。

---

### Task 1: Timings + lazy Compare API

**Files:** DiffModels, DiffEngine, DiffResultLinker(no change if already skips missing), SheetLazyCompareSmoke

- `CompareTimings`: ReadMs, TableMs, ImageMs, LayoutMs, TotalMs
- `CompareOptions.LazySheets`, `FocusPair`
- `DiffResult.Timings`, `ComparedPairKeys` (`List<string>` `"L\tR"`)
- `BuildWorkbookContent(..., IList<string> sheetNames)` — 指定シートだけセル/図形。他シートは名前スタブ。画像は指定シートの media だけ抽出を試みる。
- Compare ループは Compared 対象ペアだけ。Attach は既存。
- ログ: `比較段階: 読込= Xms 表= Yms 画像= Zms 配置= Wms 合計= Tms`

Smoke: content_diff で LazySheets+FocusPair=S_Cells → LeftContent に全シート名、中身セルがあるのは S_Cells のみ。Timings.ReadMs>=0, TotalMs>0。Compare() 無オプションは全シート中身。

### Task 2: CompareSheetPair merge

**Files:** DiffEngine

`public void CompareSheetPair(DiffResult result, string leftPath, string rightPath, SheetPair pair, CompareOptions options, IProgress<string> progress)`

- 既存 cache ディレクトリを再利用
- そのペアだけ読んで比較、Items から同ペア非 Structure を除去して追加
- ComparedPairKeys に追加
- Timings をその回の段階で上書き

### Task 3: UI lazy + 切替時 Ensure

**Files:** MainWindow, ContentPane (first Realize ログ)

- RunCompareOnlyAsync: `options.LazySheets = true`（明示全比較フラグが無い限り）
- FocusPair = コンボ選択 or 先頭ペア
- PairSheetCombo_SelectionChanged / シート切替: 未比較なら loading して CompareSheetPair → Bind
- Status: `比較完了 320 ms (読込80 表40 画像150 配置30) / このシートのみ`
- ContentPane.Load 後の初回 RealizeViewport で `表示Realize= Nms` を Log.Info

### Task 4: Verify

- ContentDiffSmoke（全シート）PASS
- SheetLazyCompareSmoke PASS
- 既存 run-logic-smokes.ps1 に SheetLazyCompareSmoke 追加
