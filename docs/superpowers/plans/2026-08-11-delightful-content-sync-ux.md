# DiffXL 内容同期 UX 完成（素敵な操作感）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 内容ベース縦同期・セル位置横同期を「正しく動く」から「触って気持ちよく・常に意図が分かる」レベルまで引き上げ、ギャップ可視化・即時追従・MiniMap 統合・状態表示・手動修正・人手操作試験を含む製品級 UX を完成させる。

**Architecture:** 同期の頭脳を `SyncSessionState`（現在セグメント・ホールド理由・対応行）に集約し、入力（ホイール／キー／MiniMap）は **イベント駆動で即マップ適用**、ポーリングはフォールバックに降格する。ギャップは Excel 本体に空白行を挿入せず、WPF オーバーレイ（`SyncGapOverlay`）とステータスバー（`SyncStatusBar`）で「挿入区間／ホールド」を可視化する。MiniMap は `ContentScrollMap` の論理座標に載せ替え、左右の青帯を内容対応で描く。

**Tech Stack:** C# / .NET Framework 4.8 / WPF / Excel COM / 既存 `ContentScrollMap`・`SheetAlignment`・`ImageCorrespondence` / Phosphor Icons（MahApps IconPacks）

## Global Constraints

- 対象形式は `.xlsx` のみ
- OpenCV / ネイティブは **x64**
- 設定・ログは `%AppData%\Roaming\DiffXL\`
- 横スクロールは **列番号 1:1**（内容マップ不使用）
- 縦スクロールは **内容対応マップのみ**（行番号強制同期は禁止）
- 片側のみ内容は **ギャップ（相手ホールド）**、再一致で同期（C-07 / C-08 / N-01）
- Excel ブックに **行を挿入して空白を作らない**（読み取り専用比較を壊さない）
- 既存 `content_scroll` / `full_feature` の数値 smoke（`PERFECT_SCROLL_PASS` / `AUTO_LIVE_PASS`）を壊さない
- 新規 UI 文言は **日本語**、ツールバー密度を現状以上に散らかさない
- アニメーションは **120–220ms**、無効化設定 `Ui.ReduceMotion` を用意
- 同期 OFF（`Ui.SyncScroll=false`）時はオーバーレイ／追従をすべて停止

## 前提（すでに実装済み — 本計画では壊さない）

| 資産 | 場所 |
|------|------|
| 画像最適対応 | `ImageCorrespondenceService` |
| 占有矩形 | `AnchorRect` / drawing from–to |
| 内容マップ | `ContentScrollMap` / `SheetAlignmentBuilder` |
| 同期サービス | `ScrollSyncService`（現状 150ms ポーリング） |
| 専用サンプル | `content_scroll_*.xlsx` + `content_scroll_expected.json` |

## UX 完成の定義（DoD）

次をすべて満たしたら「素敵なアプリケーション」の同期 UX 完了とする。

1. **分かる:** ギャップ中は左右どちらかに「挿入／片側のみ」バッジ＋半透明オーバーレイが出る  
2. **滑らか:** 通常ホイール追従の体感遅延 **≤ 50ms**（イベント駆動）。再同期ジャンプは **短イージング or トースト 1 行**  
3. **一致した脳:** MiniMap クリック・青帯・本文が **同一 ContentScrollMap** を使う  
4. **状態が見える:** フッターに `同期ON · 内容対応 · ギャップ中(右+3行)` 等が常時表示  
5. **直せる:** 画像対応の手動ピン留め UI があり、再比較なしでマップに反映（または軽量再構築）  
6. **壊れない:** COM 失敗時に赤バナー「同期一時停止」＋再試行  
7. **検証:** 人手シナリオ 12 本 + 自動（マップ／ライブ／UI 状態文字列）が PASS  
8. **回帰:** full_feature + content_scroll の既存 PASS 維持  

---

## ファイル構成（本計画で触る単位）

| パス | 責任 |
|------|------|
| `LOGIC/Excel/SyncSessionState.cs` | **新規** 同期の現在状態（モード・セグメント・メッセージ） |
| `LOGIC/Excel/ScrollSyncService.cs` | イベント駆動適用・状態発行・失敗 UI 連携 |
| `LOGIC/Diff/ContentScrollMap.cs` | `DescribeAt(row)` / セグメント種別の公開クエリ |
| `VIEW/Controls/SyncGapOverlay.xaml(.cs)` | **新規** 左右ペイン上のギャップ可視化 |
| `VIEW/Controls/SyncStatusBar.xaml(.cs)` | **新規** または MainWindow フッタ拡張 |
| `VIEW/Controls/WorkbookPane.xaml(.cs)` | ホイール後に即 `ScrollSync.ApplyFrom(side)` |
| `VIEW/Controls/MiniMapControl.xaml(.cs)` | 内容マップ座標系・左右帯 |
| `VIEW/Dialogs/ImageLinkDialog.xaml(.cs)` | **新規** 画像対応の手動ピン |
| `VIEW/MainWindow.xaml(.cs)` | 配線・トースト・バナー |
| `COMMON/AppSettings.cs` | `Ui.ReduceMotion`, `Ui.ShowSyncGapOverlay`, `Ui.SyncPollFallbackMs` |
| `VIEW/SettingsWindow.xaml(.cs)` | 上記設定 UI |
| `10_管理資料/テスト/UXシナリオ_内容同期.md` | **新規** 人手試験手順 |
| `10_管理資料/テスト/run-ux-sync-test.ps1` | **新規** 自動＋エビデンス |
| `_smoke/SyncUxSmoke.cs` | **新規** 状態文字列・DescribeAt の COM なし試験 |

---

## 共有モデル（全タスクが参照）

```csharp
// SyncSessionState.cs
public enum SyncDriveSide { None, Left, Right, Both, External }

public enum SyncSegmentKind
{
    Equal,          // 内容 1:1
    LeftOnly,       // 左のみ（右ホールド）
    RightOnly,      // 右のみ（左ホールド）
    Identity,       // マップなしフォールバック
    Disabled,       // SyncScroll OFF
    Unavailable     // COM 失敗で停止
}

public sealed class SyncSessionState
{
    public bool Enabled { get; set; }
    public SyncSegmentKind SegmentKind { get; set; }
    public SyncDriveSide DriveSide { get; set; }
    public int LeftRow { get; set; }
    public int RightRow { get; set; }
    public int LeftCol { get; set; }
    public int RightCol { get; set; }
    public string LeftSheet { get; set; }
    public string RightSheet { get; set; }
    /// <summary>ユーザー向け 1 行（フッター用）。例: 「同期ON · 右のみ画像 · 左は行7で待機」</summary>
    public string StatusLine { get; set; }
    /// <summary>ギャップ時の短い理由（オーバーレイ用）。</summary>
    public string GapCaption { get; set; }
    public bool IsInGap { get { return SegmentKind == SyncSegmentKind.LeftOnly || SegmentKind == SyncSegmentKind.RightOnly; } }
    public DateTime UtcUpdated { get; set; }
}

// ContentScrollMap に追加する公開 API
public sealed class ScrollMapProbe
{
    public SyncSegmentKind Kind { get; set; }
    public int MappedRow { get; set; }
    public int HoldRow { get; set; }
    public int SegmentStart { get; set; }
    public int SegmentEnd { get; set; }
}
// ContentScrollMap.ProbeFromLeft(int leftRow) / ProbeFromRight(int rightRow)
```

**設定デフォルト:**

| キー | 既定 | 意味 |
|------|------|------|
| `Ui.SyncScroll` | true | 既存 |
| `Ui.ShowSyncGapOverlay` | true | ギャップ半透明帯 |
| `Ui.ReduceMotion` | false | 再同期アニメ OFF |
| `Ui.SyncPollFallbackMs` | 250 | イベント駆動の保険ポーリング（150→250 に緩め、主経路はイベント） |
| `Ui.ShowSyncToastOnJump` | true | 再同期ジャンプ時の短い通知 |

---

### Task 1: SyncSessionState とマップ照会 API

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Excel/SyncSessionState.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentScrollMap.cs`（`ProbeFromLeft` / `ProbeFromRight`）
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Excel/ScrollSyncService.cs`（状態保持・`StateChanged` イベント）
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`
- Test: `20_ソース/DiffXL/_smoke/SyncUxSmoke.cs`

**Interfaces:**
- Consumes: 既存 `ContentScrollMap.MapLeftToRight` / `MapRightToLeft`、内部セグメント
- Produces:
```csharp
public event Action<SyncSessionState> StateChanged;
public SyncSessionState CurrentState { get; }
public ScrollMapProbe ProbeFromLeft(int leftRow);
public ScrollMapProbe ProbeFromRight(int rightRow);
// ScrollSyncService:
public void ApplyDrivenByLeft(int leftRow, int leftCol);
public void ApplyDrivenByRight(int rightRow, int rightCol);
```

- [ ] **Step 1: SyncUxSmoke に期待を書く（content_scroll SC_画像ギャップ）**

```csharp
// Map 構築後
var p = map.ProbeFromRight(9); // right-only 帯
Assert(p.Kind == SyncSegmentKind.RightOnly);
Assert(p.MappedRow <= 7); // 左ホールド
Assert(!string.IsNullOrEmpty(BuildStatusLine(...))); // 「右のみ」を含む
```

- [ ] **Step 2: 実行して FAIL（Probe 未実装）を確認**

Run:
```powershell
# ビルド後
.\SyncUxSmoke.exe --sheet SC_画像ギャップ
```
Expected: FAIL missing ProbeFromRight

- [ ] **Step 3: `ContentScrollMap` にセグメント走査の Probe を実装**

```csharp
public ScrollMapProbe ProbeFromRight(int rightRow)
{
    // 既存 MapRightToLeft と同じセグメント探索を流用し Kind/範囲を返す
}
```

- [ ] **Step 4: `ScrollSyncService` が Apply のたびに `SyncSessionState` を組み立て `StateChanged` 発火**

StatusLine 例:
- Equal: `同期ON · 内容対応 · L{0} ↔ R{1}`
- RightOnly: `同期ON · 右のみの内容 · 左は行{0}で待機`
- LeftOnly: `同期ON · 左のみの内容 · 右は行{0}で待機`
- Disabled: `同期OFF`
- Unavailable: `同期停止 · Excelスクロールを取得できません`

- [ ] **Step 5: Smoke PASS を確認**

```powershell
.\SyncUxSmoke.exe
# Expected: SYNC_UX_SMOKE_PASS
```

---

### Task 2: イベント駆動の即時同期（ポーリングを保険に）

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Excel/ScrollSyncService.cs`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/WorkbookPane.xaml.cs`
- Modify: `20_ソース/DiffXL/DiffXL/COMMON/AppSettings.cs`（`SyncPollFallbackMs`）
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs`（配線）
- Test: `20_ソース/DiffXL/_smoke/SyncUxSmoke.cs`（ApplyDriven のマップ結果）+ 手動ホイール手順

**Interfaces:**
- Consumes: Task 1 の `ApplyDrivenByLeft/Right`
- Produces: ホイール／キー操作後 **同一 UI スレッド内**で相手側 `TrySetScroll` 完了
- Polling: 既定 250ms。**位置が Apply と一致しているときだけ**フォールバック修正。主経路はイベント

- [ ] **Step 1: WorkbookPane にイベントを追加**

```csharp
public event Action<WorkbookPane, int /*row*/, int /*col*/, bool /*horizontal*/> ScrollInteracted;
// TryScrollByWheelDelta の成功後に発火（verify 後の実 ScrollRow/Col）
```

- [ ] **Step 2: MainWindow で Left/Right を購読**

```csharp
LeftPane.ScrollInteracted += (p, r, c, h) => {
  if (h) _scrollSync.ApplyDrivenByLeft(r, c); // 横も列は 1:1 で右へ
  else _scrollSync.ApplyDrivenByLeft(r, c);
};
```

- [ ] **Step 3: `ApplyDrivenByLeft` 実装**

```csharp
public void ApplyDrivenByLeft(int leftRow, int leftCol)
{
    if (!Enabled) { Publish disabled; return; }
    _syncing = true;
    try {
        int rightRow = MapLeftToRight(leftRow);
        int rightCol = leftCol; // 横 1:1
        _right.TrySetScroll(rightRow, rightCol);
        PublishState(drive: Left, leftRow, rightRow, leftCol, rightCol);
        _last* = ...;
    } finally { _syncing = false; }
}
```

- [ ] **Step 4: タイマー Tick を「保険」に変更**

- 変化検知時のみ Apply  
- Interval = `AppSettings.Current.Ui.SyncPollFallbackMs`（既定 250）  
- 連続失敗時 `SegmentKind=Unavailable` を Publish（Task 5 のバナーが購読）

- [ ] **Step 5: 手動確認チェックリストをレポートに書く**

```text
1. content_scroll を開く → SC_画像ギャップ
2. 右を 1 ノッチずつホイール → 左が「次のノッチ前」に追従（体感即時）
3. 右のみ帯で左が止まり、StatusLine が「右のみ」を含む
```

---

### Task 3: ギャップ・オーバーレイ（空白の代わりに「分かる」）

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/VIEW/Controls/SyncGapOverlay.xaml`
- Create: `20_ソース/DiffXL/DiffXL/VIEW/Controls/SyncGapOverlay.xaml.cs`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/WorkbookPane.xaml`（Grid に Overlay 重ね）
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs`（StateChanged → Overlay）
- Modify: `COMMON/AppSettings.cs` + `SettingsWindow`（`ShowSyncGapOverlay`）
- Test: UI 自動は状態フラグ、目視は `UXシナリオ_内容同期.md` TC-UX-03

**Interfaces:**
```csharp
public partial class SyncGapOverlay : UserControl
{
    public void Apply(SyncSessionState state, bool isLeftPane);
    // isLeftPane=true かつ RightOnly → 左に「待機」オーバーレイ
    // isLeftPane=false かつ LeftOnly → 右に「待機」オーバーレイ
    // Equal / Disabled → Visibility Collapsed
}
```

**見た目仕様（固定）:**

| 項目 | 値 |
|------|-----|
| 背景 | `#99000000`（黒 60%） |
| 中央カード | 白 92% / 角丸 8 / 最大幅 280 |
| タイトル | 「比較相手にない内容」または「こちらで待機中」 |
| 本文 | `state.GapCaption` |
| アイコン | Phosphor `Pause` / `ImageBroken` |
| ReduceMotion | フェードなし即表示 |

- [ ] **Step 1: XAML スケルトン + `Apply` が Collapsed/Visible を切り替えるユニット的呼び出しを MainWindow からテスト可能に**

- [ ] **Step 2: WorkbookPane の Excel ホスト上に `Panel.ZIndex` 高い Overlay を載せる（マウスは `IsHitTestVisible=false`）**

重要: **Excel 操作を阻害しない**（クリック透過）。

- [ ] **Step 3: StateChanged で左右 Overlay を更新**

```csharp
LeftGapOverlay.Apply(state, isLeftPane: true);
RightGapOverlay.Apply(state, isLeftPane: false);
```

- [ ] **Step 4: 設定 OFF で非表示**

- [ ] **Step 5: content_scroll で右のみ帯に入り、左にカードが見えることをスクショ保存**

Path: `10_管理資料/テスト/エビデンス_ux_sync_<timestamp>/01_gap_overlay.png`

---

### Task 4: 再同期ジャンプの通知とオプション・イージング

**Files:**
- Modify: `ScrollSyncService.cs`（ジャンプ検出）
- Modify: `MainWindow.xaml(.cs)`（トースト `SyncToast` Border）
- Modify: `AppSettings` / Settings（`ShowSyncToastOnJump`, `ReduceMotion`）
- Test: SyncUxSmoke で「行差 ≥ 3 かつ Equal に復帰」→ トースト文言生成を検証

**Interfaces:**
```csharp
// ScrollSyncService が Publish 時
// 前回が Gap で今回 Equal、かつ |Δrow| >= 3 → JumpHint = "同じ内容で再同期: 右 12 ↔ 左 8"
public string JumpHint { get; set; } // SyncSessionState に追加可
```

**挙動:**

1. ギャップ→Equal で相手行が 3 以上飛ぶ → `JumpHint` セット  
2. MainWindow が下部トーストを **1800ms** 表示（`Ui.ShowSyncToastOnJump`）  
3. `ReduceMotion=false` のときのみ、相手側 ScrollRow を **中間 1 ステップ**（from→mid→to、各 50ms）で近似イージング。Excel COM の制約上、本格アニメはしない  
4. `ReduceMotion=true` なら即 to + トーストのみ  

- [ ] **Step 1: JumpHint 生成ロジックを pure メソッドに切り出し Smoke**

```csharp
public static string BuildJumpHint(SyncSegmentKind prev, SyncSegmentKind next, int oldR, int newR, bool fromRight)
{
    if (prev is LeftOnly or RightOnly && next == Equal && Math.Abs(newR - oldR) >= 3)
        return string.Format("同じ内容で再同期しました（{0}行 → {1}行）", oldR, newR);
    return null;
}
```

- [ ] **Step 2–4: サービス組み込み・トースト UI・設定**

- [ ] **Step 5: 手動 — 右のみ帯を抜け same_B へ。トーストが見え、左右が L8↔R12**

---

### Task 5: 同期ステータスバーと COM 失敗バナー

**Files:**
- Modify: `MainWindow.xaml` フッタ領域
- Modify: `MainWindow.xaml.cs`
- Modify: `ScrollSyncService.cs`（Unavailable 時イベント）
- Test: 自動は StatusLine 文字列、失敗はモック困難なためログ＋手動 TC-UX-07

**Interfaces:**
- フッタ左: 既存 `FooterText`（ファイル状態）
- フッタ中央〜右: `SyncStatusText`（`state.StatusLine` をバインド的に更新）
- 失敗時: ツールバー下に `SyncErrorBanner`（赤系、`再試行` ボタン → `_scrollSync` の failCount リセット + Attach し直し）

- [ ] **Step 1: XAML に `SyncStatusText` と `SyncErrorBanner` を追加**

```xml
<Border x:Name="SyncErrorBanner" Visibility="Collapsed" Background="#33FF4444" Padding="8,4">
  <DockPanel>
    <Button x:Name="BtnSyncRetry" Content="再試行" DockPanel.Dock="Right" Click="BtnSyncRetry_Click"/>
    <TextBlock x:Name="SyncErrorText" Text="同期を一時停止しました。Excel のスクロール位置を取得できません。"/>
  </DockPanel>
</Border>
```

- [ ] **Step 2: StateChanged で `SyncStatusText.Text = state.StatusLine`**

- [ ] **Step 3: Unavailable で Banner Visible**

- [ ] **Step 4: 再試行で `_scrollUnavailable=false`, `_failCount=0`, timer 再開**

- [ ] **Step 5: 通常操作で StatusLine がホイールに追随して変わることを確認**

---

### Task 6: MiniMap を ContentScrollMap 座標系に統合

**Files:**
- Modify: `VIEW/Controls/MiniMapControl.xaml.cs`
- Modify: `MainWindow.xaml.cs`（`OnMiniMapNavigate` / `UpdateMiniMapViewportFromScroll`）
- Modify: `ScrollSyncService.ScrollBothToRow` は既にマップ使用 — ナビは **左右別行** を渡す API を使う
- Test: content_scroll で MiniMap クリック後 `Lsr/Rsr` が Map と一致（±2）

**Interfaces:**
```csharp
// MiniMapControl
public void SetAlignment(SheetAlignment alignment); // ScrollMap + sheet names
public void SetViewportMapped(int leftRow, int rightRow); // 青帯を左右で別位置に

// Navigate イベントを拡張（破壊的変更を避けるなら optional）
public event Action<double ratio, int suggestedLeftRow, int suggestedRightRow> NavigateMapped;
```

**ルール:**

1. MiniMap 縦位置 → 論理比率 `t ∈ [0,1]`  
2. 左の最大内容行 `Lmax`、右 `Rmax` を Alignment から推定  
3. `leftRow = 1 + t*(Lmax-1)`、`rightRow = MapLeftToRight(leftRow)`（または逆）  
4. `ScrollBothToRows(leftRow, rightRow)` を呼ぶ（同一行番号禁止）  
5. 青帯: 左マップ位置と右マップ位置を **2 本** または 1 本＋ラベル `L{n}/R{m}`

- [ ] **Step 1: `SetViewportMapped` でラベルが `L7 · R9` 形式になる**

- [ ] **Step 2: クリックハンドラを Map 経由に変更**

- [ ] **Step 3: auto-live の MINIMAP 検証を content_scroll で「左右行が Map 整合」に強化**

```text
expect: Math.Abs(MapLeftToRight(Lsr) - Rsr) <= 2
```

- [ ] **Step 4: full_feature でも回帰（従来 ±3 行を維持）**

- [ ] **Step 5: 目視 — ギャップ帯をクリックしても左右が無理に同じ行に張り付かない**

---

### Task 7: 画像対応の手動ピン留め UI

**Files:**
- Create: `VIEW/Dialogs/ImageLinkDialog.xaml`
- Create: `VIEW/Dialogs/ImageLinkDialog.xaml.cs`
- Modify: `MainWindow.xaml`（ツールバーに「画像対応」ボタン — またはアンカーメニュー近傍）
- Modify: `LOGIC/Diff/SheetAlignmentBuilder.cs` / `DiffResult` に `ManualImagePins` を載せる
- Modify: `DiffEngine` または MainWindow 側でピン適用後に `SheetAlignment` 再構築
- Test: pin 後に `Map` がピンを優先することを Smoke

**Interfaces:**
```csharp
public sealed class ManualImagePin
{
    public string LeftSheet { get; set; }
    public string RightSheet { get; set; }
    public string LeftImageHash { get; set; }
    public string RightImageHash { get; set; }
    // または ExtractedPath 相対名
}

// ImageCorrespondenceService.Match に optional pins
public static IList<ImageCorrespondence> Match(
    IList<EmbeddedImage> left,
    IList<EmbeddedImage> right,
    IList<ManualImagePin> pins = null);
// pins はコスト 0 の強制ペア。残りの Hungarian はピン済みを除外
```

**ダイアログ UX:**

1. 現在シートの左右画像をサムネ一覧（最大 64px）  
2. 左クリック選択 → 右クリックでペア  
3. 「自動に戻す」でピン削除  
4. OK で `Options.ManualImagePins` 保存 → **マップ再構築のみ**（全文再比較は任意。既定は Alignment 再構築のみで高速）

- [ ] **Step 1: ManualImagePin モデルと Match のピン除外**

- [ ] **Step 2: ダイアログ UI**

- [ ] **Step 3: MainWindow 配線**

- [ ] **Step 4: Smoke — 強制ピンで L12↔R5 のような不自然ペアも Map に出る（テスト用）**

- [ ] **Step 5: 手動 — SC_同順異内容で誤認がないことを確認し、ピン操作が分かることを確認**

---

### Task 8: 設定画面・キーボード・アクセシビリティの仕上げ

**Files:**
- Modify: `SettingsWindow.xaml(.cs)`
- Modify: `AppSettings.cs` / YAML 保存
- Modify: `MainWindow.xaml.cs`（`PreviewKeyDown` で PageDown 等も ScrollInteracted 相当）
- Test: 設定のラウンドトリップ（保存→再読込）

**設定ページ「同期」セクション:**

| UI | 設定キー |
|----|----------|
| 同期スクロール | `Ui.SyncScroll`（既存） |
| ギャップ表示 | `Ui.ShowSyncGapOverlay` |
| 再同期メッセージ | `Ui.ShowSyncToastOnJump` |
| 動きを減らす | `Ui.ReduceMotion` |
| 保険ポーリング (ms) | `Ui.SyncPollFallbackMs`（100–1000） |

- [ ] **Step 1: YAML にキー追加・既定値**

- [ ] **Step 2: SettingsWindow バインド**

- [ ] **Step 3: PageUp/PageDown/Ctrl+矢印 でも ApplyDriven が走るよう WorkbookPane or MainWindow でフック**

```csharp
// キー操作後に TryGetScroll → ApplyDriven
```

- [ ] **Step 4: ツールチップをツールバー Sync 状態アイコンに（任意の小アイコン）**

- [ ] **Step 5: 設定変更が即反映（ダイアログ閉じる時に Overlay/Timer 更新）**

---

### Task 9: UX シナリオ試験ハーネスとドキュメント

**Files:**
- Create: `10_管理資料/テスト/UXシナリオ_内容同期.md`
- Create: `10_管理資料/テスト/run-ux-sync-test.ps1`
- Modify: `MainWindow` auto-live に `CONTENT_SCROLL` 時 **StatusLine 非空** と **JumpHint 経路** の軽い検証を追加
- Modify: `10_管理資料/テスト/テストケース一覧.md`（TC-UX-01..12）
- Modify: `10_管理資料/計画/08_内容同期UX完成.md`（ポインタ）

**人手シナリオ（必須 12）:**

| ID | 操作 | 合格条件 |
|----|------|----------|
| TC-UX-01 | 通常域をゆっくりホイール | 左右が体感同時、Status が Equal 文言 |
| TC-UX-02 | 右のみ帯を通過 | 左オーバーレイ「待機」、左行が固定 |
| TC-UX-03 | 帯を抜け same_B | トースト or 再同期文言、L↔R 内容一致 |
| TC-UX-04 | 左のみ帯 | 右が待機オーバーレイ |
| TC-UX-05 | 横スクロール | 列一致、ギャップ表示は出ない |
| TC-UX-06 | MiniMap クリック | 左右行が Map 整合、青帯ラベル L/R |
| TC-UX-07 | SyncScroll OFF | 追従なし、オーバーレイ消える |
| TC-UX-08 | 高速フリック 20 ノッチ | 最終位置が Map 整合（途中の波打ちは許容） |
| TC-UX-09 | 画像ピン変更 | マップが変わりホールド位置が変わる |
| TC-UX-10 | シート切替 | Status が新シート名、誤った旧ギャップが残らない |
| TC-UX-11 | ReduceMotion ON | トーストのみで中間ステップなし |
| TC-UX-12 | full_feature 製品カタログ | 旧挙動より「固まった」が説明付きで許容 |

**自動 `run-ux-sync-test.ps1`:**

```powershell
# 1. msbuild Debug|x64
# 2. SyncUxSmoke.exe → SYNC_UX_SMOKE_PASS
# 3. ContentScrollPerfectSmoke.exe → PERFECT_SCROLL_PASS
# 4. DiffXL --auto-live-test content_scroll → AUTO_LIVE_PASS + Status 検証ログ
# 5. DiffXL --auto-live-test full_feature → AUTO_LIVE_PASS
# 6. エビデンスフォルダに report コピー
```

- [ ] **Step 1: UXシナリオ MD を本文どおり作成**

- [ ] **Step 2: SyncUxSmoke 完成（Probe + StatusLine + JumpHint）**

- [ ] **Step 3: run-ux-sync-test.ps1**

- [ ] **Step 4: 人手 12 本を実施し、エビデンスに `ux-checklist.md`（各 PASS/FAIL）を残す**

- [ ] **Step 5: DoD チェックリストを計画ポインタに「完了日」付きで記入**

---

### Task 10: パフォーマンス・エッジケース・研磨

**Files:**
- Modify: `ScrollSyncService.cs`（連打時の coalece: 16ms 以内の Apply は最後のみ）
- Modify: `ImageCorrespondenceService.cs`（ピン済み除外の確認のみ、重い再計算はしない）
- Modify: `SyncGapOverlay`（DPI / リサイズでカード中央維持）
- Test: 連続 Apply 100 回で例外なし Smoke

**エッジケース:**

| ID | ケース | 期待 |
|----|--------|------|
| E1 | マップ identity | Overlay 出さない、Status「行番号同期」 |
| E2 | 比較中 IsBusy | Apply を無視またはキューしない |
| E3 | 左右シート不一致 | Status「シート未対応」、同期しない |
| E4 | 画像 0 | テキストのみ、ギャップはテキスト挿入のみ |
| E5 | ウィンドウ最小化→復帰 | Banner/State 再 Publish |
| E6 | 高 DPI 150% | Overlay 文字切れなし |

- [ ] **Step 1: Apply の coalescing（DispatcherTimer 16ms one-shot）**

```csharp
// 連続ホイールで COM を食いすぎない。最後の行だけ Apply
```

- [ ] **Step 2: IsBusy / シート不一致ガード**

- [ ] **Step 3: E1–E4 を SyncUxSmoke に追加**

- [ ] **Step 4: リサイズ後 Overlay レイアウト確認**

- [ ] **Step 5: 全体 `run-ux-sync-test.ps1` 緑**

---

## 実装順序と依存

```text
Task1 State+Probe
  └─ Task2 イベント駆動 Apply
       ├─ Task3 Gap Overlay
       ├─ Task4 Jump toast
       └─ Task5 Status banner
            └─ Task6 MiniMap mapped
                 └─ Task7 Image pin UI
                      └─ Task8 Settings + keys
                           └─ Task9 UX harness + 人手
                                └─ Task10 polish
```

推定: **4–6 日**（人手試験・スクショ込み）。Task 3–5 は Task2 後に並列化可（エージェントは直列推奨）。

---

## 批判項目との対応表

| 批判 | 解消タスク |
|------|------------|
| 空白が続かない／固まったに見える | T3 Overlay + T5 Status |
| 再同期ワープ | T4 JumpHint + 簡易イージング |
| 150ms 遅延 | T2 イベント駆動 |
| ホイールがマップ非対応 | T2 ScrollInteracted |
| 同時操作が雑 | T2 駆動側明示 + coalescing T10 |
| 説明 UI ゼロ | T3+T5 |
| MiniMap 別脳 | T6 |
| 占有範囲 UX 弱い | 既存 Anchor + Overlay 文言で補足（EMU 推定は YAGNI、必要なら別 plan） |
| COM 静死 | T5 Banner + 再試行 |
| 手動修正不可 | T7 |
| テストがラボのみ | T9 人手 12 + 自動 |
| 横は dummy smart | 仕様維持（列 1:1）。説明を Status に「横:列一致」 |

---

## やらないこと（YAGNI・別計画）

- Excel に実際の空白行を挿入する（ブック破壊）  
- ピクセル単位のスムース慣性同期（COM 限界）  
- 横方向の内容ベース列マップ（要件外）  
- AI による意味対応  
- ウェブ版 UI  

---

## Self-Review

| チェック | 結果 |
|----------|------|
| 批判 7 条件すべてにタスク | T1–T9 で対応 |
| 既存 smoke 破壊しない | DoD + T9 回帰 |
| プレースホルダ | なし（文言・ms・パス固定） |
| 型名一貫 | `SyncSessionState` / `ScrollMapProbe` / `ManualImagePin` |
| ファイル責務 | Overlay / Status / Service / MiniMap / Dialog 分離 |

---

## 実行コマンド早見

```powershell
# ビルド
& "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "20_ソース\DiffXL\DiffXL.sln" /p:Configuration=Debug /p:Platform=x64

# UX 自動一括
powershell -File "10_管理資料\テスト\run-ux-sync-test.ps1"

# 単体
.\20_ソース\DiffXL\DiffXL\bin\x64\Debug\SyncUxSmoke.exe
.\20_ソース\DiffXL\DiffXL\bin\x64\Debug\ContentScrollPerfectSmoke.exe
```
