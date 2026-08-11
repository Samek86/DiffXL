# 02 Excel COM ハイブリッドビュー Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** インストール済みデスクトップ Excel の全バージョンで、ローカル `.xlsx` を左右ペインに読み取り専用で埋め込み表示し、1px ずれのないビューモードを実現する。

**Architecture:** `ExcelAppManager` が Excel COM インスタンス寿命を管理し、`ExcelWorkbookHost`（HWnd ホスト）が WPF 内に Excel ウィンドウを載せる。バージョンは固定せず、ProgID `Excel.Application` で起動可能なものを使う。差分オーバーレイ用に可視範囲・ウィンドウ座標を後続計画へ公開する。

**Tech Stack:** .NET Framework 4.8、WPF、`Microsoft.Office.Interop.Excel` または動的 COM（`Type.GetTypeFromProgID`）、Win32 `SetParent` / `MoveWindow`

## Global Constraints

- **x64** プロセス（Office も x64 推奨。x86 Office のみの環境は起動時に検出して明示エラー）
- 対象ファイル **`.xlsx` のみ**
- **ローカル xlsx を開ける Excel の全バージョン**を対象
- 表示は Excel 本体（行高・列幅・フォント・図形・画像、1px ずれ不可）
- 編集用途は対象外（読み取り専用）
- 失敗時は黙って空表示せず、エラーを出す
- AppData ログ必須、日本語コメント必須

**Depends on:** 計画 01（AppPaths, Log）

---

## File Map

| パス | 責務 |
|------|------|
| `LOGIC/Excel/ExcelAvailability.cs` | Excel インストール／ビットネス検出 |
| `LOGIC/Excel/ExcelAppManager.cs` | Application COM の生成・終了 |
| `LOGIC/Excel/ExcelWorkbookSession.cs` | 1 ブックの Open／Close／シート切替 |
| `VIEW/Controls/ExcelHostControl.xaml(.cs)` | HwndHost で Excel を埋め込み |
| `VIEW/Controls/WorkbookPane.xaml(.cs)` | ファイルパス表示＋ホストの左右1枚分 |
| `LOGIC/Excel/Win32.cs` | SetParent / MoveWindow / スタイル変更 |

---

### Task 1: Excel 利用可否検出

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Excel/ExcelAvailability.cs`

**Interfaces:**
- Produces:
  - `ExcelAvailability.IsExcelInstalled()` → bool
  - `ExcelAvailability.TryGetExcelProgId(out string progId)`
  - `ExcelAvailability.GetDiagnosticMessage()` → ユーザー向け日本語メッセージ

- [x] **Step 1: ProgID で検出を実装する**

```csharp
/// <summary>
/// デスクトップ Excel が COM で利用可能か調べる。
/// </summary>
public static class ExcelAvailability
{
    /// <summary>
    /// Excel.Application が登録されているか。
    /// </summary>
    public static bool IsExcelInstalled()
    {
        return Type.GetTypeFromProgID("Excel.Application") != null;
    }

    /// <summary>
    /// ユーザー向けの診断メッセージを返す。
    /// </summary>
    public static string GetDiagnosticMessage()
    {
        if (!IsExcelInstalled())
        {
            return "Microsoft Excel（デスクトップ版）が見つかりません。DiffXL の表示には Excel が必要です。";
        }
        return "Excel を利用できます。";
    }
}
```

- [x] **Step 2: 起動時ログ**

```csharp
if (!ExcelAvailability.IsExcelInstalled())
    Log.Error(ExcelAvailability.GetDiagnosticMessage());
else
    Log.Info(ExcelAvailability.GetDiagnosticMessage());
```

- [x] **Step 3: 手動確認** — Excel あり／なし（可能なら）でメッセージが変わること

- [x] **Step 4: Commit**

```bash
git commit -m "feat: detect desktop Excel availability via ProgID"
```

---

### Task 2: Win32 ヘルパと HwndHost 骨組み

**Files:**
- Create: `LOGIC/Excel/Win32.cs`
- Create: `VIEW/Controls/ExcelHostControl.cs`（HwndHost 派生）

**Interfaces:**
- Produces:
  - `Win32.SetParent` / `MoveWindow` / `SetWindowLong` 等
  - `ExcelHostControl` : `HwndHost` — `Attach(IntPtr excelHwnd)` / `Detach()`

- [x] **Step 1: P/Invoke 定義**

```csharp
/// <summary>
/// Excel ウィンドウ埋め込み用 Win32 API。
/// </summary>
internal static class Win32
{
    public const int GWL_STYLE = -16;
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
}
```

- [x] **Step 2: ExcelHostControl を実装（空 HWND でもリサイズに追随）**

`BuildWindowCore` で子ウィンドウ用ホストを作り、後から Excel の HWND を `SetParent` する設計。

- [x] **Step 3: 仮の色付きパネルで MainWindow に 2 つ並べ、リサイズで領域が動くことを確認**

- [x] **Step 4: Commit**

```bash
git commit -m "feat: add Win32 helpers and Excel HwndHost control shell"
```

---

### Task 3: ExcelAppManager / WorkbookSession

**Files:**
- Create: `LOGIC/Excel/ExcelAppManager.cs`
- Create: `LOGIC/Excel/ExcelWorkbookSession.cs`

**Interfaces:**
- Produces:
  - `ExcelAppManager.Acquire()` → COM Application（共有 or 左右各1 — 初期は **左右で Application を分ける** 方が安定しやすい。メモリ許容で 2 インスタンス）
  - `ExcelWorkbookSession.OpenReadOnly(string path)`
  - `Close()` / `Dispose()` で COM 解放
  - `ActivateSheet(string name)`
  - `IntPtr GetMainWindowHandle()` — 埋め込み用

- [x] **Step 1: 読み取り専用で Open する**

方針:

```csharp
// 動的 COM 例（Interop アセンブリ版でも可）
dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application"));
app.Visible = true;          // 埋め込み前は true が必要な場合あり。埋め込み後に調整
app.DisplayAlerts = false;
app.ScreenUpdating = true;
// Workbooks.Open(Filename, ReadOnly: true, ...)
```

- [x] **Step 2: Open 後にウィンドウキャプション／HWND を取得**

`app.Hwnd`（Excel 2013+ で利用可能なことが多い）または `FindWindow` フォールバック。バージョン差はログに残す。

- [x] **Step 3: 終了処理を必ず実装**

```
Workbook.Close(false) → Workbooks 解放 → Quit → RCW Marshal.ReleaseComObject → GC
```

異常終了でも orphan Excel を残さないよう `try/finally`。

- [x] **Step 4: コンソール相当の検証コード（一時ボタン）で Open/Close を 3 回繰り返し、タスクマネージャに EXCEL.EXE が残らないこと**

- [x] **Step 5: Commit**

```bash
git commit -m "feat: Excel COM session open readonly and cleanup"
```

---

### Task 4: 左右ペインへの埋め込み表示

**Files:**
- Create: `VIEW/Controls/WorkbookPane.xaml(.cs)`
- Modify: `VIEW/MainWindow.xaml(.cs)`

**Interfaces:**
- Produces:
  - `WorkbookPane.OpenFile(string path)` / `CloseFile()`
  - `WorkbookPane.IsOpen`
  - イベント `OpenFailed(string message)`

- [x] **Step 1: WorkbookPane UI**

上部にパス表示、本体に `ExcelHostControl`。

- [x] **Step 2: OpenFile フロー**

1. 拡張子が `.xlsx` でなければエラー  
2. `ExcelAvailability` 確認  
3. `ExcelWorkbookSession.OpenReadOnly`  
4. HWND を `ExcelHostControl.Attach`  
5. ホストサイズに `MoveWindow`  

- [x] **Step 3: MainWindow に左右 2 ペイン**

ファイル選択ダイアログ（フィルタ: `Excel ブック (*.xlsx)|*.xlsx`）で左右それぞれ Open。

- [x] **Step 4: 目視確認（受け入れ）**

| 確認 | 期待 |
|------|------|
| 行高・列幅 | Excel 単独起動と同じ |
| フォント | 同じ |
| 図形・画像 | 欠けない・ずれない |
| ズーム | Excel 側表示に従う |

- [x] **Step 5: Commit**

```bash
git commit -m "feat: embed left/right Excel workbooks in WPF panes"
```

---

### Task 5: シート一覧・切替 API

**Files:**
- Modify: `ExcelWorkbookSession.cs`
- Modify: `WorkbookPane.xaml(.cs)`

**Interfaces:**
- Produces:
  - `IReadOnlyList<string> GetSheetNames()`
  - `void ActivateSheet(string name)`
  - ペイン上の ComboBox でシート切替

- [x] **Step 1: Worksheets を列挙して名前リストを返す**

- [x] **Step 2: ComboBox 選択で Activate**

- [x] **Step 3: 複数シートブックで切替確認**

- [x] **Step 4: Commit**

```bash
git commit -m "feat: sheet list and activation on Excel host pane"
```

---

### Task 6: 座標・可視範囲の公開（計画 04・05 向け）

**Files:**
- Create: `LOGIC/Excel/ExcelViewMetrics.cs`
- Modify: `ExcelWorkbookSession.cs`

**Interfaces:**
- Produces:
  - `ExcelViewMetrics`（ウィンドウ矩形、可能なら VisibleRange アドレス）
  - `TryGetViewMetrics(out ExcelViewMetrics m)`

- [x] **Step 1: 最低限ホスト上の Excel HWND のスクリーン座標を返す**

```csharp
/// <summary>
/// 差分オーバーレイ位置合わせ用の表示メトリクス。
/// </summary>
public sealed class ExcelViewMetrics
{
    public Rect ScreenBounds { get; set; }
    public string VisibleRangeAddress { get; set; }
}
```

- [x] **Step 2: 取得失敗時は null/false を返しログに理由を残す（バージョン差を許容）**

- [x] **Step 3: Commit**

```bash
git commit -m "feat: expose Excel view metrics for overlay alignment"
```

---

### Task 7: エラー UX と受け入れ

- [x] **Step 1: 次の失敗をユーザーに日本語で見せる**

| 失敗 | メッセージ方針 |
|------|----------------|
| Excel 未インストール | デスクトップ Excel が必要 |
| x86/x64 不一致 | ビットネス不一致を案内 |
| ファイルが xlsx でない | xlsx のみ |
| Open 失敗（ロック等） | パスと COM エラー要約 |
| 埋め込み失敗 | ログ参照を促す |

- [x] **Step 2: 計画 00 進捗更新、Excel 複数バージョンがあれば可能な範囲で確認**

---

## リスク対応メモ

- **Office Click-to-Run / Microsoft 365** も ProgID 経由で同一コードパスを使う（バージョン固定しない）。
- **埋め込みが特定バージョンで不安定**な場合のフォールバック（計画内オプション）: 一時的に Excel を別ウィンドウ表示しつつスクリーン座標でオーバーレイ — ただし本線は埋め込み。
- **2 インスタンス**のライセンス／パフォーマンス問題が出たら、1 Application + 2 Window の調査をログ付きで行う（仕様変更時は要件定義を更新）。

---

## Spec Coverage

| 要件 | Task |
|------|------|
| Excel 本体描画・1px | Task 4 |
| 全バージョン（ローカル xlsx 可） | Task 1, 3（ProgID） |
| 左右分割 | Task 4 |
| 読み取り専用 | Task 3 |
| xlsx のみ | Task 4 |
| 後続オーバーレイ用メトリクス | Task 6 |

---

## 改訂履歴

| 版 | 日付 | 内容 |
|----|------|------|
| 1.0 | 2026-08-11 | 初版 |
