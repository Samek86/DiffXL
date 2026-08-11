# MiniMap 画面認識テスト結果（Orca computer-use）

| 項目 | 内容 |
|------|------|
| 実施日時 | 2026-08-11 09:20–09:27 JST |
| 計画書 | [../テスト計画_MiniMap完全動作_20260811.md](../テスト計画_MiniMap完全動作_20260811.md) |
| 操作 | **Orca computer-use**（`orca computer get-app-state` / `click` / `scroll`） |
| 対象 | DiffXL pid 経由（`pid:6212`）。名称 `DiffXL` は VS の「DiffXL - Microsoft Visual Studio」と誤マッチするため **PID 指定必須** |
| サンプル | full_feature_left / right |

## 集計

| 区分 | 結果 |
|------|------|
| 画面認識＋実クリック系列 | **PASS（コア）** |
| 差分強調トグル | **PASS** |
| スクロール追従 | **PARTIAL**（操作は送付、ラベル変化は限定的） |
| auto-live MINIMAP | 併走（`auto-live-report.txt`） |

## ケース結果

| ID | 結果 | 証拠・所見 |
|----|------|------------|
| TC-MM-01 概観 | **PASS** | `03_diffxl_orca.png`：帯 1–10、黄マーカー、青帯「表紙 · 行1」、差分 69 件 |
| TC-MM-02 帯順序 | **PASS** | ツリー: 1.表紙 … 4.長い一覧 … 10.右のみメモ（SheetPairs 順） |
| TC-MM-03 マーカー | **PASS** | 画面認識: 長い一覧帯に黄マーカー密集 |
| TC-MM-04 初期 VP | **PASS** | `表紙 · 行1` |
| TC-MM-10/11 長い一覧クリック | **PASS** | y=350 → ステータス `MiniMap → 長い一覧!行 79` **(L:OK R:OK)**、VP `長い一覧 · 行63` |
| TC-MM-13 売上付近 | **PASS** | y=230 → `MiniMap → 売上サマリ!行 17` **(L:OK R:OK)** |
| TC-MM-13 表紙 | **PASS** | y=100 → `MiniMap → 表紙!行 3` **(L:OK R:OK)**、VP `表紙 · 行1` |
| TC-MM-13 レイアウト | **PASS** | y=400 → `MiniMap → レイアウト確認!行 12` **(L:OK R:OK)** |
| TC-MM-14 ステータス | **PASS** | 全クリックで `MiniMap → シート!行 N / … (L:OK R:OK)` |
| TC-MM-20 スクロール | **PARTIAL** | scroll 送信後 VP ラベルはレイアウト確認のまま（Excel 埋め込みのホイール応答は別経路の可能性） |
| TC-MM-30 強調 | **PASS** | element 14 トグル → OFF → ON をツリーで確認 |

## 画面認識メモ（vision）

### 比較直後（`03_diffxl_orca.png`）

- 左右 Excel 埋め込み、表紙表示
- 右端 MiniMap: シート帯ラベル、黄差分マーカー、青ビューポート帯「表紙 · 行1」
- フッタ: 差分 69 件（テキスト 63 / 画像 2 / 構造 4）

### クリック後の決定的証拠

| 操作 | ステータス（アクセシビリティ） |
|------|--------------------------------|
| MiniMap y≈350 | `MiniMap → 長い一覧!行 79 / テキスト差分 C79: … (L:OK R:OK)` |
| MiniMap y≈230 | `MiniMap → 売上サマリ!行 17 / … (L:OK R:OK)` |
| MiniMap y≈100 | `MiniMap → 表紙!行 3 / … (L:OK R:OK)` |
| MiniMap y≈400 | `MiniMap → レイアウト確認!行 12 / … (L:OK R:OK)` |

→ **シート切替 + 行ジャンプ + 左右 COM Goto が実クリック経路で成立**。

## キャリブレーション所見

- ウィンドウ座標は Orca `coordinateSpace: window`（幅 1360 時 MiniMap 中心 x≈1320）。
- 帯の Y はおおよそ上から 表紙→売上→製品→長い一覧→…。y=160 は表紙帯に当たりやすい。
- 完璧なマーカー単位クリックには、帯ごとのピクセル計測 or マーカー色検出の自動化を追加推奨。

## 成果物

- `screenshots/*.png` … Orca キャプチャ
- `vision-notes/*.txt` … ツリー抜粋
- `orca-click-*.json` … 各クリック生レスポンス
- `run-log.txt`
- `auto-live-report.txt`
- `app-log-excerpt.txt`

## 結論

**MiniMap のコア目標（画面を見ながらのクリックでシート横断ジャンプ）は達成。**  
D1–D6, D8, D10 は本セッションで強い証拠あり。D7（ホイール追従）は追加チューニング推奨。
