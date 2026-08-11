# 内容ベース同期スクロール・画像完全対応 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 描画・スクロール同期の前にシート内の全画像を類似度で最適対応し、各画像が占有するセル範囲（開始〜終了）を正確に把握したうえで、横はセル位置・縦は内容対応で完璧に同期する。

**Architecture:** 比較パイプラインを「抽出 → 幾何（from/to）→ 画像最適マッチ → テキスト LCS → ContentScrollMap → 表示/ScrollSync」の一方向データ流に統一する。画像対応は `ImageCorrespondenceService` が左右全画像の類似度行列を作り、貪欲ではなく **Hungarian（二部グラフ最小コスト割当）** で 1:1 対応を確定する。占有範囲は OOXML drawing の `from`/`to` から `AnchorRect` を構築し、スクロールマップと差分強調の双方が同一の `SheetAlignment` を参照する。

**Tech Stack:** C# / .NET Framework 4.8 / WPF / Excel COM / OpenCvSharp / OpenXML (ZIP+XML) / Python openpyxl（テストデータ生成）

## Global Constraints

- 対象形式は `.xlsx` のみ（既存 `Common.ExcelExtension`）
- OpenCV は **x64** のみ（`NativeBootstrap`）
- 設定・ログは `%AppData%\Roaming\DiffXL\`（既存方針）
- 同期スクロールは設定 `Ui.SyncScroll` で OFF 可能
- 横スクロールは **常に列番号 1:1**（内容マップを使わない）
- 縦スクロールは **内容対応マップのみ**（同一行番号での強制同期は禁止）
- 片側のみ画像はギャップ（相手側ホールド）、次の高類似画像で再同期（要件 C-07 / C-08 / N-01）
- 既存 auto-live-test・full_feature サンプルの回帰を壊さない
- 新規ロジックはユニット検証可能な pure C# を優先（COM なしでマップ・マッチを検証）

---

## 現状ギャップ（この計画で埋めるもの）

| # | 現状 | 完成形 |
|---|------|--------|
| G1 | 画像対応が貪欲（ハッシュ→名前→寸法→順番） | 全画像の類似度行列 + 最適 1:1 割当 |
| G2 | スクロール用と差分用で対応ロジックが二重 | **単一** `ImageCorrespondence` を両方で共有 |
| G3 | 画像位置は `from` の開始セルのみ | `from`〜`to` の矩形 + ピクセル占有高さ（行数） |
| G4 | 縦マップが開始行ランドマークのみ | 画像は **占有行レンジ** をギャップ幅に反映 |
| G5 | 専用テストデータが弱い | `content_scroll_*` 専用 xlsx + 期待 JSON + 自動検証 |
| G6 | ライブ検証が部分的 | マップ単体 → DiffEngine → Excel 実スクロールの三段 |

---

## ファイル構成（作成・変更）

| パス | 役割 |
|------|------|
| `LOGIC/Diff/AnchorRect.cs` | 画像・アンカーのセル矩形（RowStart/End, ColStart/End） |
| `LOGIC/Diff/ImageCorrespondence.cs` | 1 画像ペアの対応結果（類似度・片側のみ） |
| `LOGIC/Diff/ImageCorrespondenceService.cs` | 類似度行列 + 最適割当 |
| `LOGIC/Diff/SheetAlignment.cs` | 1 シートペアのテキスト+画像統合ランドマークと行マップ |
| `LOGIC/Diff/ContentScrollMap.cs` | **改修**: `SheetAlignment` から構築（独自画像マッチ削除） |
| `LOGIC/Diff/DiffModels.cs` | `EmbeddedImage` に End 行/列、`DiffResult` に `Alignments` |
| `LOGIC/Diff/XlsxPackageReader.cs` | drawing の `to` も読む |
| `LOGIC/Diff/DiffEngine.cs` | 対応サービス呼び出し順を固定、結果を `DiffResult` に載せる |
| `LOGIC/Diff/ImageDiffService.cs` | `TryGetDiffRatio` を公式 API として維持・閾値定数化 |
| `LOGIC/Excel/ScrollSyncService.cs` | 縦は `SheetAlignment` / `ContentScrollMap` のみ |
| `VIEW/MainWindow.xaml.cs` | 比較後に Alignment 適用、auto-live 検証強化 |
| `30_参考資料/samples/_gen/create_content_scroll_samples.py` | **新規** テストデータ生成 |
| `30_参考資料/samples/content_scroll_*.xlsx` | 生成物 |
| `30_参考資料/samples/content_scroll_expected.json` | 期待マップ・対応表 |
| `_smoke/ContentScrollPerfectSmoke.cs` | COM なし単体検証 exe |
| `10_管理資料/テスト/テスト計画_内容同期スクロール完全.md` | 人手・自動テスト手順書 |

---

## データモデル（全タスク共通）

```csharp
// AnchorRect.cs
public sealed class AnchorRect
{
    public int RowStart { get; set; }  // 1-based inclusive
    public int RowEnd { get; set; }    // 1-based inclusive (>= RowStart)
    public int ColStart { get; set; }
    public int ColEnd { get; set; }
    public int RowSpan { get { return Math.Max(1, RowEnd - RowStart + 1); } }
}

// EmbeddedImage 追加プロパティ
// AnchorRow/Column は互換のため残し、実体は Anchor に集約してもよい
public AnchorRect Anchor { get; set; }

// ImageCorrespondence.cs
public sealed class ImageCorrespondence
{
    public EmbeddedImage Left { get; set; }   // null = 右のみ
    public EmbeddedImage Right { get; set; }  // null = 左のみ
    public double DiffRatio { get; set; }     // 0=同一, 1=全差, 片側のみは -1
    public bool IsExactHashMatch { get; set; }
    public bool IsPaired { get { return Left != null && Right != null; } }
    public bool IsLeftOnly { get { return Left != null && Right == null; } }
    public bool IsRightOnly { get { return Left == null && Right != null; } }
}

// SheetAlignment.cs（1 シートペア）
public sealed class SheetAlignment
{
    public string LeftSheet { get; set; }
    public string RightSheet { get; set; }
    public IList<ImageCorrespondence> Images { get; set; }
    public ContentScrollMap ScrollMap { get; set; }
}
```

**類似度閾値（固定・設定化は後続で可）:**

| 定数 | 値 | 意味 |
|------|-----|------|
| `ExactHash` | ハッシュ一致 | DiffRatio=0, ペア確定 |
| `PairMaxDiffRatio` | `0.55` | これ以下なら「改訂同一画像」としてペア候補 |
| `RejectDiffRatio` | `0.85` | これ以上はコスト無限大（割当不可） |
| 割当コスト | `DiffRatio` | 小さいほど良い。未ペアはコスト 1.0 の仮想スロットで表現せず、閾値超はマッチ禁止 |

---

### Task 1: 画像アンカー矩形（from〜to）の完全抽出

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/AnchorRect.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs`（`EmbeddedImage.Anchor`）
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/XlsxPackageReader.cs`（`ExtractAnchorsFromDrawing`）
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`
- Test: `20_ソース/DiffXL/_smoke/AnchorRectSmoke.cs`

**Interfaces:**
- Produces: `EmbeddedImage.Anchor`（`RowStart/RowEnd/ColStart/ColEnd` すべて 1-based inclusive）
- Produces: `XlsxPackageReader.ExtractImages` が `Anchor` を埋める
- Consumes: drawing XML の `from` / `to`（twoCellAnchor）。oneCellAnchor は Start=End。absoluteAnchor は行 0 のまま（マップ非対象）

- [ ] **Step 1: 失敗するスモークを書く**

```csharp
// AnchorRectSmoke.cs — full_feature の製品カタログ画像
// 期待: 各画像 Anchor.RowStart >= 1 かつ RowEnd >= RowStart
// 少なくとも 1 枚は twoCell で RowEnd > RowStart または ColEnd > ColStart（サンプル側で保証）
using (var r = XlsxPackageReader.Open(leftPath))
{
    var imgs = r.ExtractImages("製品カタログ", cache);
    Assert(imgs.All(i => i.Anchor != null && i.Anchor.RowStart >= 1));
    Assert(imgs.All(i => i.Anchor.RowEnd >= i.Anchor.RowStart));
}
```

- [ ] **Step 2: スモークを実行し、現状（End 未設定）で失敗することを確認**

Run:
```powershell
# ビルド後 csc で Smoke をリンクして実行
.\AnchorRectSmoke.exe
```
Expected: FAIL（`RowEnd==0` または `Anchor==null`）

- [ ] **Step 3: `DrawingMediaAnchor` に End を追加し `to` をパース**

```csharp
// ExtractAnchorsFromDrawing 内
// from: row0, col0
// to:   row1, col1  （twoCellAnchor のみ。無ければ from と同じ）
// Excel 1-based: RowStart = row0+1, RowEnd = row1+1（to は exclusive な場合がある点に注意）
// OOXML: to/row は「終了セルの 0-based index」で inclusive 扱いが一般的。
// 実装では max(from,to) を取り inclusive に正規化する。
```

- [ ] **Step 4: `EmbeddedImage` へコピーし、互換の `AnchorRow`/`AnchorColumn` は `RowStart`/`ColStart` と同期**

- [ ] **Step 5: スモーク PASS を確認してコミット**

```bash
git add 20_ソース/DiffXL/DiffXL/LOGIC/Diff/AnchorRect.cs \
        20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs \
        20_ソース/DiffXL/DiffXL/LOGIC/Diff/XlsxPackageReader.cs \
        20_ソース/DiffXL/DiffXL/DiffXL.csproj \
        20_ソース/DiffXL/_smoke/AnchorRectSmoke.cs
git commit -m "feat: parse image anchor from-to cell rects from drawings"
```

---

### Task 2: 画像最適対応サービス（全画像事前マッチ）

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ImageCorrespondence.cs`
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ImageCorrespondenceService.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ImageDiffService.cs`（閾値定数を public）
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`
- Test: `20_ソース/DiffXL/_smoke/ImageMatchSmoke.cs`

**Interfaces:**
- Consumes: `IList<EmbeddedImage> left`, `IList<EmbeddedImage> right`, `ImageDiffService.TryGetDiffRatio`
- Produces:
```csharp
public static class ImageCorrespondenceService
{
    public const double PairMaxDiffRatio = 0.55;
    public const double RejectDiffRatio = 0.85;

    /// <summary>左右画像を 1:1 最適対応する。順序は左 Anchor.RowStart 昇順。</summary>
    public static IList<ImageCorrespondence> Match(
        IList<EmbeddedImage> left,
        IList<EmbeddedImage> right);
}
```

**アルゴリズム（実装固定）:**

1. `n=left.Count`, `m=right.Count`
2. コスト行列 `cost[n,m]` を埋める:
   - ハッシュ一致 → `0`
   - それ以外 `TryGetDiffRatio` → 値
   - 読み込み失敗 → `1.0`
   - `ratio > RejectDiffRatio` → `+∞`（割当禁止）
3. **Hungarian algorithm**（または Kuhn-Munkres）で最小コスト完全マッチ（長方形はダミー列/行で正方化）
4. コスト `+∞` の割当は破棄 → 片側のみ
5. ハッシュ一致・有限コストのみ `IsPaired=true`
6. 結果リスト: ペア + LeftOnly + RightOnly。ソートキーは `Left?.Anchor.RowStart ?? int.MaxValue`, `Right?.Anchor.RowStart ?? int.MaxValue`

- [ ] **Step 1: 合成画像 3 枚ずつの失敗テスト**

```csharp
// L: A同一, B改訂, C左のみ
// R: A同一, B改訂, D右のみ
// 期待: 3 correspondence — Pair(A-A), Pair(B-B), LeftOnly(C), RightOnly(D) を含む
// B-B は寸法が同じでも中身が近いもの同士（A と誤ペアしない）
```

- [ ] **Step 2: 実行して FAIL を確認（サービス未実装）**

- [ ] **Step 3: Hungarian + `TryGetDiffRatio` で実装**

Hungarian は外部 NuGet に頼らず、`ImageCorrespondenceService` 内に 80 行程度の実装を置く（依存追加を避ける）。

- [ ] **Step 4: Smoke PASS**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: optimal image correspondence via similarity matrix"
```

---

### Task 3: DiffEngine を単一対応パイプラインに統合

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffEngine.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs`（`DiffResult.Alignments`）
- Test: `20_ソース/DiffXL/_smoke/DiffEngineAlignmentSmoke.cs`

**Interfaces:**
- Produces: `DiffResult.Alignments : IList<SheetAlignment>`
- Produces: 画像 `DiffItem` は **必ず** `ImageCorrespondence` から生成（独自 `CompareImages` 貪欲ロジック削除）
- Order: 各 `SheetPair` で  
  `cells` → `images` → `ImageCorrespondenceService.Match` → テキスト差分 → `SheetAlignment` 構築 → `ContentScrollMap`

- [ ] **Step 1: 回帰スモーク — full_feature で画像関連件数を固定期待**

```text
期待（製品カタログ）:
  Image（内容差） >= 1  （IMG-B）
  ImageOnlyLeft   == 1  （IMG-C）※現状 0 になっているバグをここで直す
  ImageOnlyRight  == 1  （IMG-D）
  ハッシュ同一 IMG-A/E は DiffItem に出さない
```

- [ ] **Step 2: `CompareImages` を `Match` 結果の foreach に置換**

```csharp
foreach (var c in ImageCorrespondenceService.Match(leftImages, rightImages))
{
    if (c.IsExactHashMatch) continue;
    if (c.IsLeftOnly) { items.Add(ImageOnlyLeft(...)); continue; }
    if (c.IsRightOnly) { items.Add(ImageOnlyRight(...)); continue; }
    // paired: ImageDiffService.ComparePair → null なら差分なし
}
```

- [ ] **Step 3: `SheetAlignment` を `result.Alignments` に追加**

- [ ] **Step 4: Smoke + 既存 auto-live の COMPARE 件数ログ確認**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: unify image matching in DiffEngine via correspondence service"
```

---

### Task 4: 占有レンジ対応の ContentScrollMap / SheetAlignment

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/SheetAlignmentBuilder.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentScrollMap.cs`（画像独自 Align 削除、Builder から構築）
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Excel/ScrollSyncService.cs`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs`
- Test: `20_ソース/DiffXL/_smoke/ContentScrollPerfectSmoke.cs`

**Interfaces:**
```csharp
public static class SheetAlignmentBuilder
{
    public static SheetAlignment Build(
        string leftSheet,
        string rightSheet,
        IList<CellValue> leftCells,
        IList<CellValue> rightCells,
        IList<ImageCorrespondence> images);
}
```

**縦マップ規則（完成形）:**

| ランドマーク | 左 | 右 | セグメント |
|--------------|----|----|------------|
| ペア画像 | `Left.Anchor.RowStart..RowEnd` | `Right.Anchor.RowStart..RowEnd` | Equal（行スパンは **短い方に合わせ、長い方は相手ホールド付き**） |
| 左のみ画像 | `RowStart..RowEnd` | hold | LeftOnly（相手は直前 Equal の右行でホールド） |
| 右のみ画像 | hold | `RowStart..RowEnd` | RightOnly |
| テキスト一致 | セル行 | セル行 | Equal 1 行 |
| 弱いトークン | 短い数字のみ等 | — | ランドマークにしない |

横: `ScrollSyncService` は列を常に 1:1。縦のみ `ScrollMap.MapLeftToRight` / `MapRightToLeft`。

- [ ] **Step 1: PerfectSmoke に「右のみ画像区間で左がホールド」を書く**

```csharp
// 専用サンプル content_scroll_left/right を使う（Task 5 で生成。先に最小合成データでも可）
// 右 ScrollRow が右のみ画像の RowStart のとき MapRightToLeft < 次ペア画像の RowStart
```

- [ ] **Step 2: FAIL 確認**

- [ ] **Step 3: Builder + Map 実装。ペア画像は RowSpan を考慮**

```text
例: 左画像 span=2 (rows 10-11), 右画像 span=4 (rows 12-15) がペア
→ Equal で 2 行分 1:1 したあと、右の残り 2 行は RightOnly（左ホールド）
```

- [ ] **Step 4: Smoke PASS + MainWindow が `result.Alignments` から `SetContentMaps`**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: content scroll map from image rects and unified alignment"
```

---

### Task 5: 専用テストデータの作成

**Files:**
- Create: `30_参考資料/samples/_gen/create_content_scroll_samples.py`
- Create: `30_参考資料/samples/_gen/media_content_scroll/`（生成 PNG）
- Create: `30_参考資料/samples/content_scroll_left.xlsx`
- Create: `30_参考資料/samples/content_scroll_right.xlsx`
- Create: `30_参考資料/samples/content_scroll_expected.json`
- Modify: `30_参考資料/samples/README.md`

**Interfaces:**
- 生成コマンド:
```powershell
python 30_参考資料/samples/_gen/create_content_scroll_samples.py
```
- 出力 JSON スキーマは Step 3 に固定

- [ ] **Step 1: シート設計をスクリプトの docstring に固定**

| シート名 | 目的 |
|----------|------|
| `SC_画像ギャップ` | 左 2 枚・右 3 枚。右の真ん中だけ別物 → 縦ギャップ同期 |
| `SC_テキスト挿入` | 右にだけ 2 行挿入。S01..S05 の ID で再連結 |
| `SC_大画像span` | 画像が複数行にまたがる（行高を高くし twoCell で to を遠く） |
| `SC_横同期` | 縦は同一内容、列だけ広く → 横 1:1 の確認用 |
| `SC_同順異内容` | 順番は同じだが 2 枚目が大きく異なる → 誤ペア禁止 |

**`SC_画像ギャップ` レイアウト（必須・ユーザー例そのもの）:**

```text
Left rows:
  3:  TEXT "SECTION_A"
  5:  IMAGE same_A   (hash shared)     span rows 5-6
  8:  IMAGE same_B   (hash shared)     span rows 8-9

Right rows:
  3:  TEXT "SECTION_A"
  5:  IMAGE same_A                     span rows 5-6
  8:  IMAGE only_right_X  (unique)     span rows 8-10   ← 右のみ
  12: IMAGE same_B                     span rows 12-13

期待スクロール:
  L5 ↔ R5   (same_A)
  R8-10 のあいだ → 左は L6 付近でホールド（same_A 終端）
  L8 ↔ R12  (same_B で再同期)
```

- [ ] **Step 2: openpyxl で画像・行高・テキストを生成**

```python
# 重要: 画像は openpyxl Image でアンカーを TwoCellAnchor 相当に
# 行高を 60〜90 にして「見た目でも span が分かる」ようにする
# メディアは media_content_scroll/ に PNG を書き出してからブックへ埋め込む
```

- [ ] **Step 3: `content_scroll_expected.json` を同時出力**

```json
{
  "version": 1,
  "sheets": {
    "SC_画像ギャップ": {
      "imagePairs": [
        { "leftRowStart": 5, "rightRowStart": 5, "kind": "exact" },
        { "leftRowStart": 8, "rightRowStart": 12, "kind": "exact" }
      ],
      "leftOnly": [],
      "rightOnly": [ { "rightRowStart": 8, "kind": "rightOnly" } ],
      "scrollSamples": [
        { "from": "L", "row": 5,  "expectOther": 5 },
        { "from": "R", "row": 9,  "expectOtherMax": 7 },
        { "from": "L", "row": 8,  "expectOther": 12 },
        { "from": "R", "row": 12, "expectOther": 8 }
      ]
    },
    "SC_テキスト挿入": {
      "scrollSamples": [
        { "from": "L", "row": 10, "expectOther": 12, "note": "S03 after 2 insert rows" },
        { "from": "R", "row": 8,  "expectOtherMax": 7, "note": "insert zone holds left" }
      ]
    }
  }
}
```

- [ ] **Step 4: スクリプト実行し xlsx/json が生成されることを確認**

```powershell
python 30_参考資料/samples/_gen/create_content_scroll_samples.py
Test-Path 30_参考資料/samples/content_scroll_left.xlsx
Test-Path 30_参考資料/samples/content_scroll_expected.json
```

- [ ] **Step 5: README に推奨ペアとして追記して Commit**

```bash
git commit -m "test: add content-scroll dedicated sample workbooks and expected JSON"
```

---

### Task 6: 自動テストハーネス（三段）

**Files:**
- Create: `20_ソース/DiffXL/_smoke/ContentScrollPerfectSmoke.cs`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs`（`VerifyContentScrollSyncAsync` を expected.json 駆動に）
- Create: `10_管理資料/テスト/run-content-scroll-test.ps1`
- Create: `10_管理資料/テスト/テスト計画_内容同期スクロール完全.md`

**三段テスト:**

| 段 | 名前 | COM | 入力 | 合否 |
|----|------|-----|------|------|
| T1 | Map/Match 単体 | 不要 | xlsx を ZIP として読む + expected.json | DiffEngine + Map の数値が一致 |
| T2 | DiffEngine 統合 | 不要 | content_scroll_*.xlsx | 画像件数・対応 kind |
| T3 | Excel ライブ | 必要 | 同 + DiffXL.exe --auto-live-test | ScrollRow 実測が expect の ±2 |

- [ ] **Step 1: T1 スモーク実装**

```csharp
// ContentScrollPerfectSmoke.cs
// 1) DiffEngine.Compare(left,right)
// 2) expected.json を読む
// 3) 各 sheet の Alignments.ScrollMap で scrollSamples を検証
// 4) imagePairs の RowStart が Correspondence と一致
// exit 0/1
```

- [ ] **Step 2: 実行して（Task4-5 完了後）PASS させる**

```powershell
cd 20_ソース\DiffXL\DiffXL\bin\x64\Debug
.\ContentScrollPerfectSmoke.exe `
  --left ..\..\..\..\..\30_参考資料\samples\content_scroll_left.xlsx `
  --right ..\..\..\..\..\30_参考資料\samples\content_scroll_right.xlsx `
  --expected ..\..\..\..\..\30_参考資料\samples\content_scroll_expected.json
```
Expected: `PERFECT_SCROLL_PASS`

- [ ] **Step 3: T3 を MainWindow auto-live に組み込む**

引数例:
```text
DiffXL.exe --auto-live-test
  --left  ...\content_scroll_left.xlsx
  --right ...\content_scroll_right.xlsx
  --report ...\_latest_content_scroll_perfect.txt
```

検証項目（自動）:
1. 比較成功、`Alignments.Count >= 4`
2. シート `SC_画像ギャップ` を選択
3. 右を only 画像 `RowStart` へ `TrySetScroll` → 左実測 `<= expectOtherMax`
4. 右を same_B へ → 左右が expect の ±2
5. 横: 左 col=5 に合わせ右 col=5
6. `FAILURES=0` / `AUTO_LIVE_PASS`

- [ ] **Step 4: `run-content-scroll-test.ps1` で T1→T3 を一括**

```powershell
# 10_管理資料/テスト/run-content-scroll-test.ps1
# 1. msbuild Debug|x64
# 2. ContentScrollPerfectSmoke.exe
# 3. DiffXL.exe --auto-live-test ...
# 4. レポートを エビデンス_content_scroll_yyyyMMdd_HHmmss/ にコピー
```

- [ ] **Step 5: テスト計画 MD を書き、手動チェックリストも含めて Commit**

```bash
git commit -m "test: perfect content-scroll harness and evidence script"
```

---

### Task 7: 回帰・エッジケース・ドキュメント締め

**Files:**
- Modify: `10_管理資料/テスト/テストケース一覧.md`
- Modify: `10_管理資料/計画/05_UI統合_同期スクロール_MiniMap.md`（完了条件追記）または本 plan へのリンク
- Test: full_feature + content_scroll の両方で auto-live

**エッジケース（コードとテストの両方で扱う）:**

| ID | ケース | 期待 |
|----|--------|------|
| E1 | 画像 0 枚 | テキストのみ LCS。identity に落ちない（内容があれば content） |
| E2 | 左右枚数 0 vs N | すべて RightOnly / LeftOnly。スクロールは相手 hold=1 付近 |
| E3 | 全画像ハッシュ一致 | DiffItem 画像 0。マップは全ペア Equal |
| E4 | 全画像が Reject 超 | すべて片側のみ。順番では無理にペアしない |
| E5 | 同一ハッシュが左右に 2 組 | 行順をコスト微調整（RowStart 差）で安定割当 |
| E6 | absoluteAnchor のみ | Anchor 無効 → 画像をマップに使わない（テキストのみ） |
| E7 | SyncScroll OFF | 左右独立。マップは構築するが適用しない |
| E8 | シート切替 | アクティブ `SheetAlignment` が切り替わる |

- [ ] **Step 1: E1–E4 を PerfectSmoke の合成ケースまたは expected 拡張でカバー**

- [ ] **Step 2: full_feature auto-live が依然 PASS**

```powershell
.\DiffXL.exe --auto-live-test `
  --left  ...\full_feature_left.xlsx `
  --right ...\full_feature_right.xlsx `
  --report ...\_latest_full_feature_regression.txt
```
Expected: `AUTO_LIVE_PASS`

- [ ] **Step 3: content_scroll auto-live PASS**

- [ ] **Step 4: テストケース一覧に TC-CS-01 〜 TC-CS-12 を追記**

| ID | 操作 | 期待 |
|----|------|------|
| TC-CS-01 | content_scroll で比較 | Alignments 生成、件数ログ |
| TC-CS-02 | SC_画像ギャップで右を only 画像へ | 左ホールド |
| TC-CS-03 | 続けて same_B へ | 左右再同期 |
| TC-CS-04 | SC_テキスト挿入で S03 行へ | 左右が異なる行番号で内容一致 |
| TC-CS-05 | SC_大画像span で画像下端へ | スパン中は同一ペア内マッピング |
| TC-CS-06 | 横スクロール col 変化 | 列のみ 1:1、行は維持 |
| TC-CS-07 | SyncScroll OFF | 非追従 |
| TC-CS-08 | MiniMap ジャンプ | マップ経由の左右行 |
| TC-CS-09 | 再比較 | Alignment 再構築 |
| TC-CS-10 | full_feature 製品カタログ | IMG-C/D が Only、誤ペアなし |
| TC-CS-11 | シートコンボ切替 | マップ切替 |
| TC-CS-12 | 期待 JSON と Map 数値一致 | PerfectSmoke PASS |

- [ ] **Step 5: Commit**

```bash
git commit -m "docs: content-scroll perfect alignment test matrix and regression gates"
```

---

## テストデータ詳細仕様

### 生成ファイル一覧

| ファイル | 説明 |
|----------|------|
| `content_scroll_left.xlsx` | 左ブック |
| `content_scroll_right.xlsx` | 右ブック |
| `content_scroll_expected.json` | 機械検証用期待値 |
| `_gen/media_content_scroll/same_a.png` | 左右同一 |
| `_gen/media_content_scroll/same_b.png` | 左右同一（2 枚目） |
| `_gen/media_content_scroll/mod_b.png` | same_b の軽微改訂（SC_同順異内容用） |
| `_gen/media_content_scroll/only_left.png` | 左のみ |
| `_gen/media_content_scroll/only_right.png` | 右のみ（中央挿入用） |
| `_gen/media_content_scroll/decoy.png` | 誤ペア誘発用（色・サイズ違い） |

### 画像ピクセル仕様（誤ペア防止）

| ID | サイズ | 内容 |
|----|--------|------|
| same_A | 320×120 | 青背景 + 文字 A |
| same_B | 320×120 | 緑背景 + 文字 B |
| only_right_X | 200×200 | 赤背景 + X（面積比で decoy と差） |
| only_left_Y | 180×90 | 黄背景 + Y |
| decoy | 400×80 | 紫（Reject しやすい） |

### 手動目視チェック（T3 補助）

1. DiffXL で content_scroll を開く  
2. `SC_画像ギャップ` を選択  
3. 右ペインをゆっくり縦スクロール  
4. **only_right が画面に入っている間、左は same_A 付近で止まり、同じ内容が並ばないこと**  
5. same_B が右に来た瞬間、左も same_B にジャンプすること  
6. 横スクロールで列見出しが左右同じ列に揃うこと  

エビデンス: スクリーンショット 3 枚（hold 中 / 再同期後 / 横同期）を  
`10_管理資料/テスト/エビデンス_content_scroll_<timestamp>/screenshots/` に保存。

---

## 実装順序と依存関係

```text
Task1 AnchorRect
  └─► Task2 ImageCorrespondenceService
        └─► Task3 DiffEngine 統合
              └─► Task4 SheetAlignment + ContentScrollMap
                    ├─► Task5 テストデータ（Task4 と並行可。expected は Task4 後に微調整）
                    └─► Task6 自動ハーネス
                          └─► Task7 回帰・ドキュメント
```

推定工数（目安）: Task1–2 で 0.5–1 日、Task3–4 で 1 日、Task5–6 で 0.5–1 日、Task7 で 0.5 日。合計 **約 3 日**。

---

## 完了の定義（DoD）

次をすべて満たしたら「完璧な実装」完了とする。

1. **事前マッチ:** 表示前に `ImageCorrespondenceService.Match` が全画像を処理し、ログに対応表を出す  
2. **最適性:** 貪欲寸法マッチによる IMG-C↔IMG-D 誤ペアが full_feature で **0**  
3. **矩形:** すべての twoCell 画像で `RowEnd>=RowStart` かつ expected の span と一致  
4. **縦同期:** `SC_画像ギャップ` で右のみ区間中、左 ScrollRow が次ペア未満  
5. **再同期:** same_B で左右が expected 行 ±2  
6. **横同期:** 列は常に一致  
7. **三段テスト:** T1/T2/T3 すべて PASS、レポート `AUTO_LIVE_PASS` / `PERFECT_SCROLL_PASS`  
8. **回帰:** full_feature auto-live PASS  

---

## リスクと緩和

| リスク | 緩和 |
|--------|------|
| Hungarian が重い（画像 100 枚超） | まずハッシュで確定除去。残りのみ行列。通常シートは数十枚以下 |
| OpenCV 失敗環境 | ハッシュのみでマッチ、類似度行列は 1.0 扱い。アプリは落とさない |
| openpyxl の anchor が oneCell になる | 生成スクリプトで行高・アンカーを明示。Task1 スモークで from/to を検証 |
| MiniMap が行番号前提 | Task4 後も MiniMap は「左行」基準のままでよい。必要なら別 plan |
| 既存 CompareImages 削除で件数変化 | Task3 の期待件数を先に更新し、auto-live のハード失敗条件を合わせる |

---

## Self-Review（計画自己レビュー）

| チェック | 結果 |
|----------|------|
| 要件 C-07 片側画像スキップ | Task2/3/4 でカバー |
| 要件 C-08 テキスト再連結 | Task4 テキスト LCS |
| 要件 N-01 同期スクロール | Task4 + ScrollSync |
| 事前に全画像比較 | Task2 行列 + Task3 パイプライン順序 |
| セル from〜to | Task1 |
| テストデータ | Task5 専用ブック + JSON |
| テスト方法 | Task6 三段 + Task7 マトリクス |
| プレースホルダ | なし（閾値・パス・期待値を固定） |
| 型名の一貫性 | `ImageCorrespondence` / `SheetAlignment` / `AnchorRect` で統一 |

---

## 実行時のコマンド早見

```powershell
# ビルド
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "20_ソース\DiffXL\DiffXL.sln" /p:Configuration=Debug /p:Platform=x64

# テストデータ再生成
python "30_参考資料\samples\_gen\create_content_scroll_samples.py"

# T1/T2
.\20_ソース\DiffXL\DiffXL\bin\x64\Debug\ContentScrollPerfectSmoke.exe

# T3 一括
powershell -File "10_管理資料\テスト\run-content-scroll-test.ps1"
```
