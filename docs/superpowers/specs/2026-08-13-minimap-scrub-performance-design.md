# MiniMap スクラブ性能改善 設計

| 項目 | 内容 |
|------|------|
| 日付 | 2026-08-13 |
| 状態 | 承認済み（A: プレースホルダ許容）・実装済み |
| 対象 | MiniMap ドラッグ／クリックによる内容スクロール |
| 前提 | 内容ストリーム仮想化（2026-08-12）済み |

---

## 1. 背景と目的

### 1.1 現状

長大シートでは本文スクロールは改善されたが、**MiniMap をドラッグして位置を動かす操作（スクラブ）** が滑らかでない。

原因は「マウス移動のたびに左右ペインの本格スクロール＋仮想化再構築＋差分探索」を同期実行していることであり、WPF の物理限界ではない。

### 1.2 目的

- MiniMap スクラブ中も **見た目（青帯・左右本文のスクロール位置）はリアルタイムに近い**
- 操作が **引っかからず追従** する
- 差分意味・左右同期の正しさは維持する

### 1.3 成功基準（stress_suite 長大一覧）

| 指標 | 目標 |
|------|------|
| MiniMap ドラッグ中の青帯 | ポインタに **フレーム単位で追従**（体感遅延 < 1 フレーム） |
| 左右本文のスクロール位置 | ドラッグ中も **継続的に動く**（止まったままにしない） |
| ドラッグ中の UI 応答 | マウスが「粘る」感覚が消える |
| マウスアップ直後 | 本文内容が正しい位置で完全表示・左右比率一致 |
| 本文スクロール → MiniMap 青帯 | 既存どおり追従（退行なし） |

「リアルタイム」の定義（本設計）:

- **位置の更新** … 毎フレーム（約 16ms）必ず反映 → 必須
- **行内容のフル描画** … 可能なら毎フレーム。重いときは **プレースホルダ／薄い描画** でもよく、位置は合わせる
- **差分ハイライト枠・ステータス文言** … リアルタイム必須ではない（間引き可）

---

## 2. 現状経路（ボトルネック）

```
MouseMove (高頻度)
  → MiniMap.RaiseNavigate
      → UpdateViewportVisuals / UpdateHintText
      → NavigateRequested(ratio, nearestDiffItem)
  → MainWindow.OnMiniMapNavigate
      → Left/Right SetContentScrollRatio
          → ScrollToVerticalOffset
          → RealizeViewport  （範囲変化時に CreatePairElement × 多数）
          → CaptureMeasuredHeights (BeginInvoke)
      → FindContentPairIndex(item)  （最大 O(ペア数)）
      → HighlightPairIndex × 左右
      → StatusText 更新
```

| # | コスト | スクラブに必要か |
|---|--------|------------------|
| A | 左右 `RealizeViewport` で行 UI 生成 | 位置表示には **毎イベント不要** |
| B | `FindContentPairIndex` + 選択枠 | **ドラッグ中不要**（クリック確定時で可） |
| C | `StatusText` 毎ムーブ | **不要** |
| D | MiniMap 青帯更新 | **必要（軽い）** |
| E | `ScrollToVerticalOffset` | **位置リアルタイムに必要（軽い）** |
| F | `CaptureMeasuredHeights` 連続 | スクラブ中は **有害**（extent 揺れ） |

---

## 3. 方針の核

### 原則

1. **位置（ratio / offset）は常に最新** — 見た目の「どこを見ているか」を優先
2. **重い仕事はフレームに 1 回、またはモードを分ける**
3. **ドラッグ中モード** と **確定（MouseUp）モード** を明示する
4. プレースホルダを使う場合でも **高さマップは共有のまま** なので左右ずれを増やさない

### 採用しない方針

| 案 | 不採用理由 |
|----|------------|
| ドラッグ中は本文を一切動かさない | 「見た目リアルタイム」に反する |
| ドラッグ完了まで BeginInvoke でまとめるだけ | 位置が遅れて見える |
| 固定 50ms デバウンスのみ | 低速ドラッグでカクつき、高速で遅延が目立つ |

---

## 4. アプローチ比較

### 案 A — フレーム統合（推奨・第 1 段）

MouseMove では **目標 ratio だけ保持**。`CompositionTarget.Rendering`（または DispatcherPriority.Render）で **1 フレーム 1 回** 左右スクロール＋軽量 Realize を適用。

| 項目 | 評価 |
|------|------|
| 見た目 | 60fps 相当で位置更新 → リアルタイム感を維持 |
| 負荷 | MouseMove 回数（100+/s）→ 最大 60/s に上限 |
| 実装量 | 小〜中 |
| リスク | 低 |

### 案 B — スクラブ軽量モード（推奨・第 2 段、A と併用）

ドラッグ中フラグ `_scrubbing` 時:

- スクロール offset は毎フレーム更新
- Realize は **間引き**（例: 3 フレームに 1 回、または index が N 行以上動いたとき）
- 間引き中の未生成行は **固定高プレースホルダ**（グレー帯）で extent を維持
- MouseUp で **強制フル Realize** + ハイライト

| 項目 | 評価 |
|------|------|
| 見た目 | 位置は常に正しい。内容は一瞬プレースホルダの可能性 |
| 負荷 | 高速スクラブ時に大きく下がる |
| 実装量 | 中 |
| リスク | 中（プレースホルダの見た目合意） |

### 案 C — 本文はサムネ的静的ビットマップ

シート全体を縮小画像にして MiniMap 連動だけ先に動かす。

| 項目 | 評価 |
|------|------|
| 不採用（本フェーズ） | 生成コスト・メモリ・差分色の再実装が大きい。A+B で足りる想定 |

### 推奨

**案 A を必須実装、案 B を同一リリース内の第 2 段として入れる。**  
プレースホルダは「薄い行（背景のみ・テキストなし）」とし、通常行と高さを height map で一致させる。

---

## 5. 詳細設計

### 5.1 状態

```
MiniMapScrubController（MainWindow 内 private でも可）
  _targetRatio: double
  _pending: bool
  _scrubbing: bool          // Mouse キャプチャ中
  _lastAppliedRatio: double
  _lastRealizeRatio / _lastRealizeFrame
```

- MiniMap は `NavigateRequested` の前に **ScrubStarted / ScrubMoved / ScrubEnded** を出すか、  
  既存の down/move/up から MainWindow が `_scrubbing` を立てる。

### 5.2 イベント方針

| イベント | MiniMap（青帯） | 目標 ratio | 本文 offset | Realize | 差分ハイライト | Status |
|----------|-----------------|------------|-------------|---------|----------------|--------|
| MouseDown | 即時 | 更新 | 同フレーム適用 | する | クリック時のみ可 | 任意 |
| MouseMove | **即時** | 更新のみ | **次 Rendering で適用** | 間引き | **しない** | **しない** |
| MouseUp | 即時 | 最終値 | 即時 | **強制フル** | する | する |

青帯は軽量なので **Move でも即時更新を維持**（リアルタイム感の主観に効く）。

### 5.3 フレーム適用（案 A）

```
OnNavigate(ratio):
  _targetRatio = ratio
  MiniMap.SetContentViewportRatio(ratio)  // 青帯は即時
  if (!_pending):
    _pending = true
    CompositionTarget.Rendering += OnFrame

OnFrame:
  apply _targetRatio → Left/Right SetContentScrollRatio(scrubMode: true)
  if !_scrubbing or shouldRealize:
    Realize (通常 or 軽量)
  if !_scrubbing:
    CompositionTarget.Rendering -= OnFrame
    _pending = false
```

同一フレーム内で ratio が何度更新されても **最後の値だけ** 使う。

### 5.4 スクラブ軽量 Realize（案 B）

`ContentPane.SetVerticalScrollRatio(ratio, ScrubOptions options)`

| options | 動作 |
|---------|------|
| `Normal` | 現状どおり Realize + 高さ補正可 |
| `Scrub` | offset 更新必須。Realize は条件付き。`CaptureMeasuredHeights` **抑制** |
| `ScrubEnd` | 強制 Realize。高さ補正再開。ハイライト可 |

**Realize を走らせる条件（Scrub 時）:**

- 前回 Realize から 2〜3 フレーム以上経過、**または**
- 先頭可視 index が前回から ±(buffer/2) 以上変化

**Realize をスキップするフレーム:**

- `ScrollToVerticalOffset` のみ（スペーサと既存要素で extent 維持）
- 画面外に出た要素の破棄はスキップしてよい（メモリはバッファ内に限定済み）
- 新規に必要だが未生成の index は **プレースホルダ Border**（`MinHeight = layout.GetHeight(i)`、背景 `#F3F4F6`）

**MouseUp / ScrubEnd:**

- プレースホルダを破棄し通常 `CreatePairElement`
- `RealizeViewport(force: true)` 左右
- 必要なら `HighlightPairIndex` / Status 更新

### 5.5 ドラッグ中に切る処理（必須・低コスト）

`OnMiniMapNavigate` からドラッグ中は外す:

1. `FindContentPairIndex` / `HighlightPairIndex`
2. `StatusText` の毎ムーブ更新（Up 時 1 回、または 100ms に 1 回まで）

クリック（Down→Up でほぼ移動なし）では Up 時にハイライトを実行。

### 5.6 左右同期

- 目標 ratio は **1 つ**（左右同じ値）
- `_syncingContentScroll` はフレーム適用中も維持
- 本文側の `VerticalScrollRatioChanged` がフレーム適用中に再入しないよう既存フラグを流用

### 5.7 MiniMap 側の軽量化（任意・第 3 段）

- ドラッグ中 `UpdateHintText` を間引き
- `SetContentViewportRatio` は青帯 Geometry のみ更新（既に近い）

本命は本文 Realize 側なので、第 1・2 段で十分な見込み。

---

## 6. 見た目の合意

| 状態 | ユーザーに見えるもの |
|------|----------------------|
| 通常スクロール | 現状どおりフル行 |
| MiniMap 高速ドラッグ中 | 青帯・本文スクロール位置は追従。一部行が短い灰色の帯になることが **あってもよい** |
| ドラッグ終了 | 直ちにフル行に置き換わる（1 フレーム以内を目標） |
| 低速ドラッグ | ほぼ常にフル行（Realize 間引きが発火しにくい） |

プレースホルダを嫌う場合は案 B のプレースホルダを止め、**Realize 間引きだけ**にする（そのフレームは古い行が画面に残るが offset は進む）。  
本設計の推奨は **プレースホルダあり**（位置と内容の不一致が少ない）。

---

## 7. 影響範囲

| ファイル | 変更 |
|----------|------|
| `MiniMapControl.xaml.cs` | Scrub 開始/終了の通知、Move 時は ratio 先行 |
| `MainWindow.xaml.cs` | フレーム統合、`OnMiniMapNavigate` 分割、ハイライト/Status 間引き |
| `ContentPane.xaml.cs` | `Scrub` / `ScrubEnd` モード、プレースホルダ、高さ補正抑制 |
| `WorkbookPane.xaml.cs` | `SetContentScrollRatio` のオプション中継（必要なら） |

比較エンジン・ストリーム展開・height map の意味は変更しない。

---

## 8. 実装フェーズ

### Phase 1（即効・見た目リアルタイム維持）

1. MouseMove の ratio をフレーム統合
2. ドラッグ中の Find/Highlight/Status 停止
3. スクラブ中 `CaptureMeasuredHeights` 抑制

### Phase 2（高速ドラッグ耐性）

1. `Scrub` モードの Realize 間引き
2. プレースホルダ行
3. `ScrubEnd` でフル Realize

### Phase 3（必要なら）

1. MiniMap hint 間引き
2. Realize の Children.Clear 廃止（差分 add/remove）

Phase 1 だけでも体感は大きく改善する想定。Phase 2 で stress 級の高速ドラッグに耐える。

---

## 9. テスト観点

| ケース | 確認 |
|--------|------|
| stress 長大一覧で MiniMap 上下ドラッグ | 青帯・本文が滑らかに追従 |
| ドラッグ中に左右位置がずれない | 同一 ratio |
| MouseUp 後 | フル行表示、ハイライトが必要なら付く |
| 本文ホイールスクロール | MiniMap 青帯が追従（退行なし） |
| 差分マーカークリック | 該当付近へジャンプ（Up 時ハイライト） |
| content_diff / stream_align 系 | 既存ライブ検証が落ちない |

計測（任意）:

- ドラッグ 2 秒間の `RealizeViewport` 呼び出し回数（改善前後）
- フレーム適用の平均間隔

---

## 10. リスクと緩和

| リスク | 緩和 |
|--------|------|
| プレースホルダが目立つ | 低速時はフル Realize；色を通常行に近づける |
| MouseUp で一瞬ジャンプ | Scrub 中も height map を変えず offset のみ |
| Rendering イベントの付け外し漏れ | ScrubEnd と Unloaded で必ず解除 |
| 既存 auto-live が「毎ムーブ Status」に依存 | Status は Up 時更新で十分か確認し、テストを合わせる |

---

## 11. まとめ

- **限界ではない。経路の過剰同期が原因。**
- **位置は毎フレーム最新、重い描画と探索は間引く。**
- 推奨実装: **フレーム統合（A）+ スクラブ軽量 Realize / プレースホルダ（B）。**
- 見た目のリアルタイム感は **青帯＋スクロール位置** で担保し、行の中身は可能なら追従、厳しければ短い灰色帯でつなぐ。
