# MiniMap ビューポート帯（青帯）をスクロール量に比例させる設計

| 項目 | 内容 |
|------|------|
| 日付 | 2026-08-15 |
| 状態 | 会話上承認済み（A + 最小高さ 16px + スクロールバー操作） |
| 対象 | MiniMap 青帯の高さ・位置・クリック／ドラッグ |
| 前提 | 内容ストリーム仮想化（2026-08-12）・スクラブ性能（2026-08-13）済み |

---

## 1. 背景と目的

### 1.1 現状

青帯の高さは内容量と無関係で、マップ高さの **20〜35%（下限 14px）** に固定されている。

```
bandH = max(14, min(bodyH * 0.2, bodyH * 0.35))
```

短いシートでも長いシートでも同じ厚さのため、スクロールバーのサムのように「今見えている範囲」が分からない。

クリックは `ratio = pointerY / bodyH` で、帯の掴み位置を持たない。帯が大きくなると帯の内側をクリックしても位置が跳ぶ。

### 1.2 目的

- 青帯の高さを **今見えている高さ / 内容全体の高さ** に比例させる
- 内容が長くて比例高さが極小になるときは **最小 16px** を守る
- 操作は **スクロールバーのサム** と同じ（掴んでドラッグ / 帯の外クリックでジャンプ）

### 1.3 成功基準

| 状況 | 期待 |
|------|------|
| 内容がビューポートに収まる（スクロールなし） | 青帯が MiniMap 本文のほぼ全高 |
| 内容がビューポートの約半分 | 青帯はマップの約半分 |
| 長大シートで比例高さが 16px 未満 | 青帯は **16px**（WPF DIP） |
| 帯の内側をドラッグ | 掴んだ相対位置を保ったまま追従（ジャンプしない） |
| 帯の外側をクリック | 帯の中心がその点へジャンプし、続けてドラッグできる |
| 本文ホイール／リサイズ | 青帯の位置と高さが追従 |
| スクラブ性能 | 2026-08-13 のフレーム統合・Realize 間引きは維持 |

---

## 2. 採用方針

**案 A — 実測の可視比率でスクロールバーサム化する。**

`ContentPane` の `ViewportHeight` / `ExtentHeight`（なければ `_layout.TotalHeight`）を唯一の比率源とする。行数推定や WPF `ScrollBar` への置き換えはしない。

比較エンジン・黄マーカー配置・スクラブ経路（`ScrubStarted` / フレーム適用 / `ScrubEnd`）の意味は変えない。

---

## 3. 数式（唯一の定義）

ヘルパー `MiniMapViewportBand`（静的・WPF 非依存）に集約する。UI は描画とイベントだけ担当する。

定数:

- `MinHeightPx = 16`
- `LabelMinBandHeightPx = 22`（帯内 % ラベルを出す最小高さ）

### 3.1 可視比率

```
VisibleFraction(viewport, extent):
  if viewport <= 0: return 1
  if extent <= viewport: return 1
  return clamp(viewport / extent, 0, 1)
```

### 3.2 帯の高さ

```
BandHeight(bodyH, visibleFraction):
  if bodyH <= 0: return 0
  raw = clamp(visibleFraction, 0, 1) * bodyH
  return min(bodyH, max(MinHeightPx, raw))
```

最小高さで実際の可視範囲より帯が太くなるのは、通常のスクロールバーと同じで許容する。

### 3.3 帯の上端

```
BandTop(bodyTop, bodyH, bandH, ratio):
  travel = max(0, bodyH - bandH)
  return bodyTop + clamp(ratio, 0, 1) * travel
```

スクロール不能（`bandH >= bodyH`）のとき travel は 0、上端は常に `bodyTop`、ratio は 0 として扱う。

### 3.4 ヒット判定

```
HitTestThumb(y, bandTop, bandH):
  return y >= bandTop && y <= bandTop + bandH
```

境界は帯の内側とする。

### 3.5 ポインタ → 比率

```
RatioFromPointer(pointerY, grabOffset, bodyTop, bodyH, bandH):
  if bodyH <= bandH: return 0
  travel = bodyH - bandH
  return clamp((pointerY - grabOffset - bodyTop) / travel, 0, 1)
```

- 帯を掴んだとき: `grabOffset = pointerY - bandTop`（ダウン時点）
- 帯の外をクリックしたとき: 帯中心をポインタへ合わせるため `grabOffset = bandH / 2`。その後同じドラッグを続ける

ページ単位の段階送りはしない。外クリックは **その位置へ即ジャンプ**。

---

## 4. 操作

| 入力 | 動作 |
|------|------|
| MouseDown（帯の内側） | `_dragging = true`、`grabOffset` を保存、その場の ratio を発行（ジャンプしない） |
| MouseDown（帯の外側） | `grabOffset = bandH/2`、`RatioFromPointer` でジャンプ、ドラッグ開始 |
| MouseMove（ドラッグ中） | `RatioFromPointer` で ratio 更新。青帯は即時。本文は既存スクラブ経路 |
| MouseUp | 最終 ratio を 1 回出して `ScrubEnded`（既存どおり） |
| 領域外ドラッグ | Y をマップ内にクランプして継続（既存どおり） |

`PointToContentRatio` の `pointerY / bodyH` は廃止し、上記に置き換える。

### 4.1 ラベル

- 帯の高さ ≥ 22px: 現状どおり帯内に `12%` を出す
- 帯の高さ < 22px: **帯内ラベルは出さない**（16px 帯に 10pt ラベルがはみ出すため）
- 下部ヒント `表示 12%` はどちらの場合も維持する

---

## 5. データ流れ

```
ContentPane.GetVisibleFraction()
  viewport = StreamScroll.ViewportHeight
  extent   = StreamScroll.ExtentHeight
             （<= 0.5 かつ layout ありなら layout.TotalHeight）
  → MiniMapViewportBand.VisibleFraction(viewport, extent)

WorkbookPane.GetContentVisibleFraction()
  → ContentHost.GetVisibleFraction()  （ホストなしは 1）

MainWindow.PushMiniMapViewport()
  ratio    = 左ペイン優先、なければ右
  fraction = 左ペイン優先、なければ右
  → MiniMap.SetContentViewport(ratio, fraction)
```

### 5.1 MiniMap API

```
SetContentViewport(double ratio, double visibleFraction)
  _visibleFraction を 0..1 に保存
  SetContentViewportRatio(ratio)  （青帯を即時更新）

SetContentViewportRatio(double ratio)
  比率だけ更新。高さ計算は直近の _visibleFraction を使う
```

初期 `_visibleFraction` は `1`（未計測＝スクロールなし扱い）。比較完了・初回スクロール通知で実測に置き換わる。一瞬全高になるのは許容する。

### 5.2 更新タイミング

| 契機 | 呼ぶ API |
|------|----------|
| 本文スクロール（ホイール・同期） | `PushMiniMapViewport`（比率と可視比率の両方） |
| ビューポートリサイズ | 同上（既存 `ScrollChanged` の ViewportHeightChange） |
| 比較完了・シート切替で MiniMap 再設定 | 同上 |
| MiniMap ドラッグ中の青帯 | `SetContentViewportRatio` のみ（高さは保持） |
| スクラブ確定（MouseUp） | 既存 `ScrubEnd` のあと `PushMiniMapViewport`（extent 変化があれば高さを補正） |

左右は共有 height map のため可視比率は一致する前提。参照は左優先。

### 5.3 変えないもの

- `NavigateRequested` / `NavigateMapped` / `ScrubStarted` / `ScrubEnded` の契約
- 黄マーカーの等間隔配置
- スクラブ中の Realize 間引き・プレースホルダ・Status 抑制
- 比較エンジン・height map の意味

---

## 6. 影響範囲

| ファイル | 変更 |
|----------|------|
| `VIEW/Controls/MiniMapViewportBand.cs` | **新規**。数式の唯一の実装 |
| `DiffXL.csproj` | 上記を Compile 追加 |
| `VIEW/Controls/MiniMapControl.xaml.cs` | 帯描画・ヒット・grabOffset・ラベル閾値 |
| `VIEW/Controls/ContentPane.xaml.cs` | `GetVisibleFraction()` |
| `VIEW/Controls/WorkbookPane.xaml.cs` | `GetContentVisibleFraction()` |
| `VIEW/MainWindow.xaml.cs` | `PushMiniMapViewport` に集約 |
| `_smoke/MiniMapViewportBandSmoke.cs` | **新規**。数式の単体確認 |

XAML 見た目（色・枠）は変えない。帯の Height だけが変わる。

---

## 7. テスト

スモーク（`csc /r:DiffXL.exe`、既存と同じ）:

| ケース | 期待 |
|--------|------|
| viewport=400, extent=400 | fraction=1, bandH=bodyH |
| viewport=200, extent=400, bodyH=400 | bandH=200 |
| viewport=20, extent=4000, bodyH=400 | raw=2 → bandH=**16** |
| viewport<=0 | fraction=1 |
| ratio=0 / 1 | 帯が本文の上端 / 下端（travel 端） |
| 帯内ポインタ、grab=帯上からの距離 | ratio がジャンプしない |
| 帯外クリック、grab=bandH/2 | 帯中心がポインタ付近 |
| bandH>=bodyH | RatioFromPointer は常に 0 |
| 既存 ContentStream / スクラブ経路 | 退行なし（ビルドが通ること） |

手動確認:

- 短いシート: 帯が太い
- `stress_suite` 長大一覧: 帯が細く、下限 16px
- 帯を掴んで上下: 相対位置がずれない
- 帯の外クリック: ジャンプしてからドラッグ継続
- ウィンドウ縦リサイズ: 帯の高さが変わる

---

## 8. リスク

| リスク | 緩和 |
|--------|------|
| 仮想化中に ExtentHeight が一時的に短い | 既存どおり layout.TotalHeight にフォールバック |
| 最小高さで帯が実際より太い | スクロールバーと同じ。位置は travel 上の ratio |
| 16px 帯に % ラベルがはみ出す | 22px 未満は帯内ラベル非表示 |
| スクラブ中に高さまで毎ムーブ再計算 | ドラッグ中は ratio のみ更新 |

---

## 9. まとめ

- 青帯 = 可視範囲。高さは `viewport / extent`、下限 **16px**
- 操作はスクロールバー: 掴みオフセット維持、外クリックは中心ジャンプ
- 数式は `MiniMapViewportBand` に閉じ、UI とスクラブ性能経路は最小差分
