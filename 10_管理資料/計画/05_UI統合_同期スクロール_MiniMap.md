# 05 UI 統合・同期スクロール・MiniMap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HTML プロトタイプ相当の画面遷移とメイン比較 UX（同期スクロール、MiniMap、シート対応、アンカー、再比較、片側差し替え）を WPF で統合し、実データで一連の比較操作を完了できるようにする。

**Architecture:** `MainWindow` がシェル。`StartupView` / ダイアログ群を切り替え。`CompareSession` が左右パス・DiffResult・Excel セッションを保持。同期スクロールは Excel COM のスクロール位置（または Window.ScrollRow/Column）をポーリング／イベントで双方向同期。MiniMap は DiffResult の OrderHint から構築。

**Tech Stack:** WPF、計画 02–04 の成果物、STYLE 共通スタイル

## Global Constraints

- 画面は `10_管理資料/画面プロトタイプ/` の情報設計に準拠
- STYLE フォルダの共通スタイルを使用
- `.xlsx` のみ、Excel 全バージョン（利用可能なもの）、差分トグルは 04 済みを接続
- 日本語コメント、ログ、AppData

**Depends on:** 計画 02, 03, 04（基盤 01 含む）

---

## File Map

| パス | 責務 |
|------|------|
| `LOGIC/CompareSession.cs` | 1 比較セッション状態 |
| `VIEW/MainWindow.xaml(.cs)` | シェル・ツールバー |
| `VIEW/StartupPanel.xaml(.cs)` | SCR-01 |
| `VIEW/Dialogs/SheetMapDialog.xaml(.cs)` | SCR-03 |
| `VIEW/Dialogs/AnchorDialog.xaml(.cs)` | SCR-04 |
| `VIEW/Dialogs/ReplaceFileDialog.xaml(.cs)` | SCR-06 |
| `VIEW/SettingsWindow.xaml(.cs)` | SCR-05（04 と統合） |
| `VIEW/Controls/MiniMapControl.xaml(.cs)` | MiniMap |
| `LOGIC/Excel/ScrollSyncService.cs` | 同期スクロール |
| `STYLE/*.xaml` | 見た目統一 |

---

### Task 1: CompareSession 状態オブジェクト

**Files:**
- Create: `LOGIC/CompareSession.cs`

**Interfaces:**
- Produces:

```csharp
/// <summary>
/// 現在の比較セッション。
/// </summary>
public sealed class CompareSession
{
    public string LeftPath { get; set; }
    public string RightPath { get; set; }
    public DiffResult LastResult { get; set; }
    public CompareOptions Options { get; set; } = new CompareOptions();
    public bool IsBusy { get; set; }
}
```

- [x] **Step 1: 実装と MainWindow から保持**
- [x] **Step 2: Commit**

```bash
git commit -m "feat: CompareSession state holder"
```

---

### Task 2: SCR-01 起動／ファイル選択

**Files:**
- Create: `VIEW/StartupPanel.xaml(.cs)`
- Modify: `MainWindow`

**Interfaces:**
- Produces: 左右パス選択、比較開始ボタン、`.xlsx` フィルタ

- [x] **Step 1: レイアウトをプロトタイプに合わせて作成**

- [x] **Step 2: 両方選択後のみ「比較開始」有効**

- [x] **Step 3: 比較開始で Excel Open + DiffEngine.Compare + Overlay.Apply + メイン画面へ**

- [x] **Step 4: ローディングインジケータ（IsBusy）**

- [x] **Step 5: Commit**

```bash
git commit -m "feat: startup file pick and start compare flow"
```

---

### Task 3: メイン画面ツールバーとステータスバー

**Files:**
- Modify: `MainWindow.xaml(.cs)`

**Interfaces:**
- ボタン: 再比較、差分強調トグル（04）、シート対応、アンカー、設定、左右差し替え、閉じる

- [x] **Step 1: ツールバー配置（STYLE 利用）**

- [x] **Step 2: ステータスに差分件数（Text / Image / Only）**

```csharp
statusDiff.Text = string.Format(
    "差分 {0} 件（テキスト {1} / 画像 {2} / 片側のみ {3}）",
    total, textCount, imageCount, onlyCount);
```

- [x] **Step 3: Commit**

```bash
git commit -m "feat: main toolbar and diff status summary"
```

---

### Task 4: ScrollSyncService（同期スクロール）

**Files:**
- Create: `LOGIC/Excel/ScrollSyncService.cs`

**Interfaces:**
- Produces:
  - `void Attach(ExcelWorkbookSession left, ExcelWorkbookSession right)`
  - `void Detach()`
  - `bool Enabled { get; set; }`（設定 `Ui.SyncScroll`）

- [x] **Step 1: 実装方針をコードに固定**

```
タイマー 50–100ms で左右の ScrollRow / ScrollColumn（Window）を取得
アクティブ側の変化を検知したら非アクティブ側へ設定
再入防止フラグ _syncing
```

COM が取れないバージョンでは `Log.Debug` し、同期を無効化してもアプリは落とさない。

- [x] **Step 2: 左右スクロールで追従することを目視確認**

- [x] **Step 3: 設定の SyncScroll OFF で止まること**

- [x] **Step 4: Commit**

```bash
git commit -m "feat: bidirectional Excel scroll sync service"
```

---

### Task 5: MiniMapControl

**Files:**
- Create: `VIEW/Controls/MiniMapControl.xaml(.cs)`

**Interfaces:**
- Produces:
  - `void SetDiffs(IEnumerable<DiffItem> items)`
  - イベント `NavigateRequested(double ratio)` または `DiffItem`
  - クリック／ドラッグで本体側スクロール位置を移動（ScrollSync 経由）

- [x] **Step 1: 縦長 Canvas に差分マーカー（設定色）を描画**

- [x] **Step 2: クリックで対応 DiffItem 付近へスクロール／選択**

- [x] **Step 3: 差分トグル OFF 時も MiniMap マーカーは残すか消すかを設定可能に — 既定は「残す」（位置ナビ優先）**

- [x] **Step 4: Commit**

```bash
git commit -m "feat: MiniMap for diff overview and navigation"
```

---

### Task 6: シート対応ダイアログ（SCR-03）

**Files:**
- Create: `VIEW/Dialogs/SheetMapDialog.xaml(.cs)`

**Interfaces:**
- Produces: `List<SheetPair> ResultPairs`、適用で再比較

- [x] **Step 1: 左右シート一覧と対応付け UI**

- [x] **Step 2: OK で `session.Options.ManualSheetPairs` 更新 → DiffEngine.Compare → Overlay 更新**

- [x] **Step 3: Commit**

```bash
git commit -m "feat: manual sheet mapping dialog and recompare"
```

---

### Task 7: アンカーダイアログ（SCR-04）

**Files:**
- Create: `VIEW/Dialogs/AnchorDialog.xaml(.cs)`

**Interfaces:**
- Produces: `AnchorLeftAddress` / `AnchorRightAddress`

- [x] **Step 1: 左右の開始セル入力（例: A10）**

- [x] **Step 2: 適用で再比較**

- [x] **Step 3: Commit**

```bash
git commit -m "feat: anchor dialog for mid-sheet recompare"
```

---

### Task 8: 片側差し替え（SCR-06）と再比較

**Files:**
- Create: `VIEW/Dialogs/ReplaceFileDialog.xaml(.cs)` または OpenFileDialog 直接

- [x] **Step 1: 左または右のパス変更 → 当該 Excel セッションだけ開き直し → 再比較**

- [x] **Step 2: ツールバー「再比較」はパス維持で DiffEngine 再実行**

- [x] **Step 3: Commit**

```bash
git commit -m "feat: replace one side file and recompare"
```

---

### Task 9: 設定画面の残項目と閉じるフロー

**Files:**
- Modify: Settings（04 の色に加え SyncScroll、ログレベル）

- [x] **Step 1: SyncScroll チェックボックスを YAML 連動**

- [x] **Step 2: 「ファイルを閉じて最初から」で COM 解放 + SCR-01 へ**

- [x] **Step 3: Commit**

```bash
git commit -m "feat: settings sync-scroll and close-to-startup flow"
```

---

### Task 10: 受け入れ（E2E）

- [x] **Step 1: プロトタイプ README の操作フローを実アプリで実施**

| 操作 | 期待 |
|------|------|
| 起動 → 2 ファイル → 比較 | 左右 Excel 表示 + 差分 |
| 差分トグル | 強調 ON/OFF |
| 同期スクロール | 左右追従 |
| MiniMap | ジャンプ |
| シート対応 | 再比較 |
| アンカー | 再比較範囲変化 |
| 片側差し替え | 再比較 |
| 設定色変更 | 即時反映 |
| 閉じる | Excel プロセス残留なし |

- [x] **Step 2: 計画 00 進捗更新**

---

## Spec Coverage

| 要件 | Task |
|------|------|
| SCR-01〜06 | Task 2,6,7,8,9 |
| N-01 同期スクロール | Task 4 |
| N-02/03 MiniMap | Task 5 |
| F-01〜04 | Task 2,8 |
| C-02,C-04 UI | Task 6,7 |
| V-07 トグル接続 | Task 3（04 成果） |

---

## 内容ベース同期スクロール（後続計画）

本計画（05）の同期スクロールは行番号／基本 COM 同期を含む。  
**画像最適対応・内容マップによる完璧同期** は後続計画で完了:

- ポインタ: [`07_内容同期スクロール_画像完全対応.md`](./07_内容同期スクロール_画像完全対応.md)
- 実装本体: [`docs/superpowers/plans/2026-08-11-perfect-content-scroll-image-alignment.md`](../../docs/superpowers/plans/2026-08-11-perfect-content-scroll-image-alignment.md)
- 受け入れ: `PERFECT_SCROLL_PASS` + content_scroll / full_feature 双方 `AUTO_LIVE_PASS`（TC-CS-01〜12）

## 改訂履歴

| 版 | 日付 | 内容 |
|----|------|------|
| 1.0 | 2026-08-11 | 初版 |
| 1.1 | 2026-08-11 | 07 内容同期スクロール完了へのリンク追記 |
