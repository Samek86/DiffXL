# 内容ベース比較（Excel 廃止）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Excel COM 埋め込みを廃止し、`.xlsx` の内容（セル値・背景色・ボーダー検出テーブル・画像・図形）を位置非依存で比較し、自前内容ビューで差分を分かりやすく表示する。

**Architecture:** OOXML 抽出 → `WorkbookContent` 正規化 → シート対応 → セル多重集合／テーブル行 LCS／画像・図形の系列 DP アラインメント → `DiffResult` → WPF 内容ビュー（MiniMap は現在シートのみ、画像ハイライトは赤 3px＋黄 50%）。比較キーにセル番地・画像アンカー位置は使わない。

**Tech Stack:** C# / .NET Framework 4.8 / WPF / OpenCvSharp x64 / System.IO.Compression（OOXML）/ YamlDotNet / Python openpyxl（サンプル生成）

**Spec:** `docs/superpowers/specs/2026-08-12-content-based-diff-design.md`

## Global Constraints

- 対象形式: **`.xlsx` のみ**
- Excel インストール・COM **不要**（埋め込み完全廃止）
- OpenCV: **x64**（`NativeBootstrap`）
- 設定・ログ・キャッシュ: `%AppData%\Roaming\DiffXL\`
- ソース: クラス・メソッド・フィールド直上に **日本語コメント必須**
- 配布: 原則単一 `DiffXL.exe`（x64）
- 比較キー: **内容**（位置・パスはメタデータのみ）
- 画像ハイライト既定: 枠 **赤 3px**、塗り **黄 α=0.5**、表示トグル必須
- MiniMap: **現在シートの差分のみ**（全シート横断禁止）
- 検証: 既存どおり `_smoke` コンソール exe を主とする（COM なしでエンジン検証）
- 新規ロジックは pure C# でスモーク可能に保つ

---

## 現状ギャップ

| # | 現状 | 完成形 |
|---|------|--------|
| G1 | セルはアドレス一致比較 | 多重集合（位置無視） |
| G2 | 背景色なし | 背景色比較 |
| G3 | テーブル概念なし | border 検出＋行 LCS |
| G4 | 画像は位置込み対応寄り | 出現順 DP＋見た目（位置無視） |
| G5 | スクショずれに弱い | 位置合わせ＋領域矩形 |
| G6 | Excel 埋め込み必須 | 内容ビューのみ |
| G7 | MiniMap 全シート俯瞰あり | 現在シートのみ |
| G8 | ハイライトがオーバーレイ黄中心 | 画像は赤枠3px＋黄50% |

---

## ファイル構成

| パス | 責務 |
|------|------|
| `LOGIC/Diff/DiffModels.cs` | DiffKind 拡張、HighlightRegion、CellContent、結果モデル |
| `LOGIC/Diff/ContentModels.cs` | **新規** WorkbookContent / SheetContent / TableBlock / ShapeContent |
| `LOGIC/Diff/XlsxPackageReader.cs` | 背景色・border・図形抽出を拡張 |
| `LOGIC/Diff/TableDetector.cs` | **新規** border → TableBlock |
| `LOGIC/Diff/CellBagComparer.cs` | **新規** テーブル外セル多重集合 |
| `LOGIC/Diff/SequenceAligner.cs` | **新規** 汎用 DP（Match/SkipL/SkipR） |
| `LOGIC/Diff/TableRowAligner.cs` | **新規** 行 LCS |
| `LOGIC/Diff/TableCompareService.cs` | **新規** テーブル系列＋行 diff → DiffItem |
| `LOGIC/Diff/ImageSequenceAligner.cs` | **新規** 画像順序 DP（既存 Correspondence を置換） |
| `LOGIC/Diff/ImageVisualComparer.cs` | **新規** 整列＋領域矩形（ImageDiffService 拡張でも可） |
| `LOGIC/Diff/ShapeCompareService.cs` | **新規** 図形順序＋内容 |
| `LOGIC/Diff/TextDiffService.cs` | アドレス比較から撤退 or 薄く残して Bag に委譲 |
| `LOGIC/Diff/DiffEngine.cs` | 新パイプライン統括 |
| `LOGIC/Diff/SheetMatcher.cs` | 維持（同名＋手動＋片側） |
| `VIEW/Controls/ContentPane.xaml(.cs)` | **新規** 左右内容ホスト |
| `VIEW/Controls/TableDiffGrid.xaml(.cs)` | **新規** 行追加削除が見える表 |
| `VIEW/Controls/ImagePairView.xaml(.cs)` | **新規** ペア＋ハイライト |
| `VIEW/Controls/MiniMapControl.*` | 現在シートのみに改修 |
| `VIEW/MainWindow.*` | Excel ホスト切断、内容 UI 接続 |
| `VIEW/StartupPanel.*` | Excel チェック削除 |
| `VIEW/Dialogs/SheetMapDialog.*` | 異名手動ペア維持・強化 |
| `COMMON/AppSettings.cs` | ハイライト色・画像閾値 |
| `LOGIC/Excel/*` / `ExcelHostControl` 等 | 段階的削除 |
| `30_参考資料/samples/_gen/create_content_diff_samples.py` | **新規** シナリオ xlsx |
| `_smoke/ContentDiffSmoke.cs` | **新規** エンジン必須シナリオ |
| `10_管理資料/要件定義.md` | 方針を内容比較へ更新（最終タスク） |

---

## 共有型（全タスク共通・Task 1 で確定）

```csharp
// DiffKind 追加（既存 Text/Image/ImageOnlyLeft/ImageOnlyRight/Structure に加えて）
Background,
TableRowDelete,
TableRowInsert,
TableCellChange,
Shape,
ShapeOnlyLeft,
ShapeOnlyRight

// 画像ハイライト領域（画像ローカル座標、ピクセル）
public sealed class HighlightRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

// DiffItem 追加プロパティ
// List<HighlightRegion> HighlightRegions
// string TableIdLeft / TableIdRight
// int? RowIndexLeft / RowIndexRight
// string BackgroundLeft / BackgroundRight  // #AARRGGBB or null

// CellContent（CellValue 拡張または置換）
public sealed class CellContent
{
    public string Address { get; set; }      // メタ
    public int Row { get; set; }
    public int Column { get; set; }
    public string Text { get; set; }
    public string BackgroundArgb { get; set; } // null=なし
    public bool HasAnyBorder { get; set; }
}

public sealed class TableBlock
{
    public string Id { get; set; }
    public int OrderIndex { get; set; }
    public int RowStart { get; set; }
    public int RowEnd { get; set; }
    public int ColStart { get; set; }
    public int ColEnd { get; set; }
    public IList<IList<CellContent>> Rows { get; set; } // 行→セル
}

public sealed class SheetContent
{
    public string Name { get; set; }
    public List<CellContent> LooseCells { get; set; }  // テーブル外
    public List<TableBlock> Tables { get; set; }
    public List<EmbeddedImage> Images { get; set; }    // 出現順ソート済み
    public List<ShapeContent> Shapes { get; set; }
}

public sealed class WorkbookContent
{
    public string Path { get; set; }
    public List<SheetContent> Sheets { get; set; }
}

public sealed class ShapeContent
{
    public string Id { get; set; }
    public int OrderIndex { get; set; }
    public string Kind { get; set; }
    public string Text { get; set; }
    public string RasterPath { get; set; }
    public string ContentHash { get; set; }
    public AnchorRect Anchor { get; set; } // メタのみ
}

// 系列アラインメント結果
public enum AlignOp { Match, SkipLeft, SkipRight }
public sealed class AlignStep
{
    public AlignOp Op { get; set; }
    public int LeftIndex { get; set; }  // SkipRight 時は -1
    public int RightIndex { get; set; } // SkipLeft 時は -1
}
```

---

### Task 1: 内容モデルと DiffKind 拡張

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentModels.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs`
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`（Compile 追加）
- Test: `20_ソース/DiffXL/DiffXL/_smoke/ContentModelsSmoke.cs`（または bin 側 `_smoke` 方針に合わせる）

**Interfaces:**
- Produces: 上記共有型すべて。`DiffResult` に `WorkbookContent LeftContent` / `RightContent` を追加可能にする

- [ ] **Step 1: ContentModels.cs を追加し csproj に Compile を足す**

日本語コメント付きで `CellContent`, `TableBlock`, `SheetContent`, `WorkbookContent`, `ShapeContent`, `HighlightRegion`, `AlignOp`, `AlignStep` を定義する。

- [ ] **Step 2: DiffKind と DiffItem を拡張**

```csharp
// DiffKind に Background, TableRowDelete, TableRowInsert, TableCellChange,
// Shape, ShapeOnlyLeft, ShapeOnlyRight を追加
// DiffItem に HighlightRegions, TableIdLeft/Right, RowIndexLeft/Right,
// BackgroundLeft/Right を追加
// DiffResult に LeftContent / RightContent（WorkbookContent）を追加
```

既存の `Text` / `Image*` / `Structure` は残す（破壊的リネーム禁止）。

- [ ] **Step 3: ビルド確認**

Run: `msbuild 20_ソース\DiffXL\DiffXL.sln /p:Configuration=Debug /p:Platform=x64`
Expected: 成功（警告のみ可）

- [ ] **Step 4: Commit**

```bash
git add 20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentModels.cs 20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs 20_ソース/DiffXL/DiffXL/DiffXL.csproj
git commit -m "feat: add content models and extended DiffKind for content-based diff"
```

---

### Task 2: 抽出拡張（背景色・border）

**Files:**
- Modify: `LOGIC/Diff/XlsxPackageReader.cs`
- Modify: `LOGIC/Diff/DiffModels.cs`（CellValue に Bg/border を足すか CellContent へ移行）
- Test: `_smoke/ContentExtractSmoke.cs` + 最小 xlsx

**Interfaces:**
- Consumes: ZIP 内 `xl/styles.xml`, `xl/worksheets/sheet*.xml`
- Produces:
  - `IEnumerable<CellContent> EnumerateCellContents(string sheetName)`
  - 各セルに `Text`, `BackgroundArgb`, `HasAnyBorder`, `Address`, `Row`, `Column`

- [ ] **Step 1: styles.xml から fill を読み、セル style index → ARGB を解決する**

```csharp
// 方針:
// - cellXfs / fills / fgColor theme/rgb を解決
// - 解決不能・塗りなしは BackgroundArgb = null
// - theme は簡易表（主要 theme 色）または rgb のみ対応で開始
```

- [ ] **Step 2: セルの border 有無を styles の borderId から HasAnyBorder に落とす**

四辺のいずれかが `style != none` なら true。

- [ ] **Step 3: EnumerateCellContents を公開し、既存 EnumerateCells は Text 互換ラッパに**

- [ ] **Step 4: スモーク xlsx を手置きまたは Python で生成し、背景付きセルが読めることを確認**

Run: `ContentExtractSmoke.exe path\to\sample.xlsx`
Expected: 特定セルの Text と BackgroundArgb がコンソールに出る

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: extract cell background and border flags from xlsx"
```

---

### Task 3: TableDetector

**Files:**
- Create: `LOGIC/Diff/TableDetector.cs`
- Modify: `DiffXL.csproj`
- Test: `_smoke/TableDetectorSmoke.cs`

**Interfaces:**
- Consumes: `IList<CellContent> cells`（同一シート）
- Produces: `TableDetectResult { List<TableBlock> Tables; List<CellContent> LooseCells; }`

```csharp
public static class TableDetector
{
    public static TableDetectResult Detect(IList<CellContent> cells)
    {
        // 1. HasAnyBorder==true のセルを格子点として収集
        // 2. 4 近傍（上下左右の隣接セル）で連結成分
        // 3. 成分の bounding box を TableBlock にする（min 2x2 または 2 行以上など閾値）
        // 4. ボックス内の全セル（border なし含む）を Rows に行列配置
        // 5. ボックス外の非空セル → LooseCells
        // 6. OrderIndex = RowStart, ColStart でソート
    }
}
```

- [ ] **Step 1: 失敗するスモークを書く**

```csharp
// 3x3 に border フラグ付きセル + 外に Hello
// → Tables.Count==1, LooseCells に Hello
```

- [ ] **Step 2: TableDetector を実装してスモーク PASS**

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: detect table blocks from cell borders"
```

---

### Task 4: SequenceAligner（汎用 DP）

**Files:**
- Create: `LOGIC/Diff/SequenceAligner.cs`
- Test: `_smoke/SequenceAlignerSmoke.cs`

**Interfaces:**

```csharp
public static class SequenceAligner
{
    /// <summary>
    /// leftCount x rightCount の類似度（0..1）と閾値で編集アラインメント。
    /// matchReward = similarity, skipCost 固定（例 0.4）。
    /// similarity &lt; threshold の Match は不可。
    /// </summary>
    public static IList<AlignStep> Align(
        int leftCount,
        int rightCount,
        Func<int, int, double> similarity,
        double matchThreshold,
        double skipCost = 0.4);
}
```

- [ ] **Step 1: スモーク — 8 vs 9 で index 4 のみ右スキップ**

```csharp
// sim(i,j)=1 if i==j for j<4; sim(i,j)=1 if i==j-1 for j>=5; else 0
// 期待: Match x4, SkipRight, Match x4
```

- [ ] **Step 2: 実装して PASS**

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: add generic sequence aligner for insert/skip matching"
```

---

### Task 5: セル多重集合比較

**Files:**
- Create: `LOGIC/Diff/CellBagComparer.cs`
- Modify or deprecate path: `LOGIC/Diff/TextDiffService.cs`（Bag に委譲可）
- Test: `_smoke/CellBagSmoke.cs`

**Interfaces:**

```csharp
public static class CellBagComparer
{
    public static IList<DiffItem> Compare(
        IEnumerable<CellContent> left,
        IEnumerable<CellContent> right,
        SheetPair pair);
}
```

規則:
- キー = `Text + "\0" + (BackgroundArgb ?? "")` の多重集合マッチ
- 完全一致ペアは Diff なし
- 同じ Text で Bg のみ違う → 可能な範囲で `Background` 差分 1 件（Text 同士を貪欲に寄せる）
- 余りは Text 片側差分
- Address は代表メタとして DiffItem に載せるがマッチに使わない

- [ ] **Step 1: スモーク**

| 入力 | 期待 |
|------|------|
| A1 Hello / A2 Hello | 0 件 |
| Hello 赤 / Hello 白 | Background 1 |
| Hello,World / World,Hello | 0 件 |
| Hello×2 / Hello×1 | Text 片側 1 |

- [ ] **Step 2: 実装して PASS**

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: position-agnostic cell bag comparison"
```

---

### Task 6: テーブル行 LCS と比較サービス

**Files:**
- Create: `LOGIC/Diff/TableRowAligner.cs`
- Create: `LOGIC/Diff/TableCompareService.cs`
- Test: `_smoke/TableRowDiffSmoke.cs`

**Interfaces:**

```csharp
public static class TableRowAligner
{
    // 行キー = タブ結合した各セル Text（Bg はセル変更検出で別扱い）
    public static IList<AlignStep> AlignRows(
        IList<IList<CellContent>> leftRows,
        IList<IList<CellContent>> rightRows);
}

public static class TableCompareService
{
    public static IList<DiffItem> Compare(
        IList<TableBlock> leftTables,
        IList<TableBlock> rightTables,
        SheetPair pair);
}
```

`Compare` 手順:
1. テーブル間: 行内容の粗類似で `SequenceAligner`（または行集合 Jaccard）
2. Match テーブル内: `AlignRows`
3. SkipLeft 行 → `TableRowDelete`、SkipRight → `TableRowInsert`
4. Match 行内: 列は min 長で zip、Text/Bg 差 → `TableCellChange`

- [ ] **Step 1: スモーク 12345 vs 1245**

```csharp
// left rows keys "1","2","3","4","5" / right "1","2","4","5"
// → ちょうど 1 件 TableRowDelete（中身 "3"）、Insert 0
```

- [ ] **Step 2: 行内 1 セル変更スモーク**

- [ ] **Step 3: 実装して PASS**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: table sequence and row LCS diff"
```

---

### Task 7: 画像系列アラインメント＋視覚比較（領域）

**Files:**
- Create: `LOGIC/Diff/ImageSequenceAligner.cs`
- Create: `LOGIC/Diff/ImageVisualComparer.cs`（または `ImageDiffService` 拡張）
- Modify: `LOGIC/Diff/ImageDiffService.cs`（位置合わせ API）
- Test: `_smoke/ImageSequenceSmoke.cs`

**Interfaces:**

```csharp
public static class ImageSequenceAligner
{
    public static IList<AlignStep> Align(
        IList<EmbeddedImage> left,
        IList<EmbeddedImage> right);
    // similarity: hash 一致=1.0、否则 TryGetDiffRatio から 1-ratio
    // threshold: 1 - RejectDiffRatio 相当
}

public sealed class ImageVisualDiff
{
    public bool IsSame { get; set; }
    public string MaskPath { get; set; }
    public List<HighlightRegion> Regions { get; set; }
}

public static class ImageVisualComparer
{
    public static ImageVisualDiff Compare(
        string leftPath,
        string rightPath,
        string maskDir,
        string maskFileName);
}
```

視覚比較手順:
1. 読み込み・最大辺を制限してリサイズ
2. `FindTransformECC` または位相相関で平行移動整列（失敗時は左上揃え）
3. absdiff → 閾値 → morphology → connectedComponents
4. 面積 ≥ 最小閾値の外接矩形を `HighlightRegion` に
5. 矩形が 0 なら IsSame=true

UI 描画用に Regions を必ず DiffItem に載せる（マスク画像も互換で残す）。

- [ ] **Step 1: 8 vs 9 挿入スモーク（類似度モック or 実ファイル）**

- [ ] **Step 2: 同一画像・異パスで IsSame**

- [ ] **Step 3: 一部だけ違う合成 PNG で Regions.Count >= 1**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: image sequence alignment and regional visual diff"
```

---

### Task 8: 図形抽出と比較（最小）

**Files:**
- Modify: `LOGIC/Diff/XlsxPackageReader.cs`（sp / pic 以外の drawing）
- Create: `LOGIC/Diff/ShapeCompareService.cs`
- Test: `_smoke/ShapeDiffSmoke.cs`

**Interfaces:**
- `IList<ShapeContent> ExtractShapes(string sheetName, string cacheDir)`
- 図形内テキストがあれば `Text`、なければ簡易ラスタ or XML 正規化ハッシュ
- `ShapeCompareService.Compare` は画像と同様 SequenceAligner

初期スコープ: テキスト付きシェイプを優先。ラスタ化が重い場合は Text+Kind+サイズハッシュで Match し、差があれば Shape Diff。

- [ ] **Step 1: 抽出→順序リスト→片側 1 件のスモーク**

- [ ] **Step 2: Commit**

```bash
git commit -m "feat: extract and compare shapes by content sequence"
```

---

### Task 9: DiffEngine 新パイプライン

**Files:**
- Modify: `LOGIC/Diff/DiffEngine.cs`
- Modify: `LOGIC/CompareSession.cs`（必要なら）
- Test: `_smoke/ContentDiffSmoke.cs`（設計書 §7 シナリオを一括）

**Interfaces:**
- `DiffResult Compare(left, right, options, progress)` シグネチャ維持
- 内部:
  1. Open 左右 Reader
  2. SheetMatcher
  3. 各シート SheetContent 構築（Detect tables, sort images/shapes）
  4. ペアごと CellBag + Table + Image + Shape
  5. 片側シート Structure
  6. result.LeftContent / RightContent セット
  7. 旧 ContentScrollMap / Excel 向け Alignment は **生成しない** か空で残す

- [ ] **Step 1: ContentDiffSmoke に必須シナリオを列挙（exit code ≠0 で失敗）**

シナリオ（設計書 7.1）:
1. 位置無視 Hello  
2. 背景差  
3. 表 12345 vs 1245  
4. 表セル 1 変更  
5. 画像同見た目異位置  
6. 画像 8 vs 9  
7. 画像部分差 → Regions  
8. シート片側・同名  

- [ ] **Step 2: DiffEngine を新パイプラインに接続**

- [ ] **Step 3: サンプル生成スクリプト `create_content_diff_samples.py` で xlsx を用意**

- [ ] **Step 4: ContentDiffSmoke PASS**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: rewire DiffEngine to content-based pipeline"
```

---

### Task 10: 設定（ハイライト・画像閾値）

**Files:**
- Modify: `COMMON/AppSettings.cs`
- Modify: `VIEW/SettingsWindow.xaml(.cs)`
- Modify: `LOGIC/Diff/DiffHighlightStyle.cs`（画像枠用プロパティ）

**Interfaces:**

```csharp
// 既定
ImageHighlightBorderColor = "#FFFF0000"
ImageHighlightBorderThickness = 3
ImageHighlightFillColor = "#80FFFF00"  // 黄 50%
HighlightEnabled default true
// 既存の差分色設定と并存。画像領域は上記を優先
```

- [ ] **Step 1: YAML 読み書きと既定値**

- [ ] **Step 2: 設定 UI に項目追加（最小: 色2つ＋α＋枠幅）**

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: settings for image highlight style and thresholds"
```

---

### Task 11: Content UI 骨格（Excel ホスト切断）

**Files:**
- Create: `VIEW/Controls/ContentPane.xaml`, `ContentPane.xaml.cs`
- Modify: `VIEW/MainWindow.xaml`, `MainWindow.xaml.cs`
- Modify: `VIEW/StartupPanel.xaml.cs`
- Modify: `VIEW/Controls/WorkbookPane.xaml(.cs)`（ContentPane に置換または中身入替）
- Modify: `App.xaml.cs`（Excel 起動チェック削除）

**Interfaces:**
- `ContentPane.Load(SheetContent sheet, IList<DiffItem> sheetDiffs, bool isLeft)`
- タブ: セル / テーブル / 画像 / 図形（中身はプレースホルダでも可）
- 起動: `.xlsx` 選択 → DiffEngine → 左右 ContentPane にシート 0 を表示
- **ExcelAvailability でアプリ終了しない**

- [ ] **Step 1: Startup から Excel 必須チェックを削除**

- [ ] **Step 2: MainWindow から ExcelHost 埋め込みを外し ContentPane を置く**

- [ ] **Step 3: 比較後に LeftContent/RightContent をバインド**

- [ ] **Step 4: Debug 起動で Excel なし PC でも起動・比較結果テキストが見える**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: replace Excel embed with content pane shell"
```

---

### Task 12: テーブル差分グリッド UI

**Files:**
- Create: `VIEW/Controls/TableDiffGrid.xaml(.cs)`
- Modify: `ContentPane` テーブルタブ

**Interfaces:**
- 入力: 左右 `TableBlock` + そのテーブルの DiffItem 群 or 行 AlignStep
- Delete 行: 左表示・右空白、背景で削除と分かる
- Insert 行: 右表示・左空白
- Change セル: 黄/設定色で強調

- [ ] **Step 1: 12345 vs 1245 が UI 上で 3 行目欠落と分かることを手動確認**

- [ ] **Step 2: Commit**

```bash
git commit -m "feat: table diff grid with insert/delete row display"
```

---

### Task 13: 画像ペアビュー＋ハイライト描画

**Files:**
- Create: `VIEW/Controls/ImagePairView.xaml(.cs)`
- Modify: ツールバー ハイライトトグル
- Modify: `DiffHighlightController` または新規 `ImageHighlightRenderer`

**Interfaces:**
- ペア行リスト（AlignStep 順）
- 各 Match: 左右 Image + Regions を **赤 3px 枠＋黄 50% 塗り**で Canvas 重ね
- Skip: 片側のみ
- `HighlightVisible` false で枠・塗り非表示（画像本体は残す）

- [ ] **Step 1: 部分差サンプルで枠が見える**

- [ ] **Step 2: トグル OFF で消える・ON で戻る（再比較不要）**

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: image pair view with red border and yellow highlight toggle"
```

---

### Task 14: 差分リスト・シート切替・MiniMap（現在シートのみ）

**Files:**
- Modify: `VIEW/Controls/MiniMapControl.xaml.cs`
- Modify: `VIEW/MainWindow.xaml.cs`
- Modify: `VIEW/Dialogs/SheetMapDialog.*`

**Interfaces:**
- 差分リスト既定フィルタ: 現在のシートペア名
- シートコンボ変更 → 左右 ContentPane 更新 → MiniMap を **そのシートの Items だけ**で再構築
- 全シートを1本の MiniMap に積むコードパスを削除
- SheetMapDialog: 同名リセット、異名ペア、片側明示（既存強化）
- メインで左右シート独立選択 → 「この組み合わせで比較」で ManualSheetPairs 更新して再比較

- [ ] **Step 1: 2 シート以上サンプルで、シート B 選択時 MiniMap にシート A の差分が出ない**

- [ ] **Step 2: 片側のみシートが Structure と片側表示になる**

- [ ] **Step 3: 異名手動ペアで比較できる**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: per-sheet minimap and manual sheet pairing UX"
```

---

### Task 15: Excel COM 層の削除と掃除

**Files:**
- Remove or exclude from csproj: `LOGIC/Excel/*`（Win32 埋め込み一式）
- Remove: `VIEW/Controls/ExcelHostControl.cs`
- Remove: `SyncGapOverlay` 等 Excel 専用 UI（未使用なら）
- Remove: `AnchorDialog`（位置アンカー）
- Modify: `CompareSession`, `AutoLiveTest`（COM 前提を切断または削除）
- Modify: `40_リリース/README.md`, 起動メッセージ

**Interfaces:**
- ビルドが Excel interop / COM 参照なしで通る
- デッドコード・未使用 using を除去

- [ ] **Step 1: csproj から Excel 関連 Compile を外し、参照エラーを Content 経路に置換**

- [ ] **Step 2: Release|x64 ビルド成功**

- [ ] **Step 3: ContentDiffSmoke + 手動起動確認**

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor: remove Excel COM embedding and dependency"
```

---

### Task 16: サンプル・文書・要件の同期

**Files:**
- `30_参考資料/samples/_gen/create_content_diff_samples.py`
- `30_参考資料/samples/README.md`
- `10_管理資料/要件定義.md`（ビュー最重要を内容比較に変更）
- `10_管理資料/計画/00_全体ロードマップ.md`（計画 02 の位置づけ更新）
- `docs/superpowers/specs/2026-08-12-content-based-diff-design.md`（状態: 承認済み）

- [x] **Step 1: 設計書の必須シナリオをカバーする xlsx を生成スクリプト化**

- [x] **Step 2: 要件定義の §1.3 / V-01 系を内容ビュー方針に改訂**

- [x] **Step 3: ロードマップに本計画への参照を追記**

- [x] **Step 4: Commit**

```bash
git commit -m "docs: align requirements and samples with content-based diff"
```

---

## 実装順序と依存

```
T1 モデル
 → T2 抽出 → T3 TableDetector
 → T4 SequenceAligner → T5 CellBag / T6 Table / T7 Image / T8 Shape
 → T9 DiffEngine + ContentDiffSmoke
 → T10 Settings
 → T11 Content UI shell
 → T12 Table UI / T13 Image UI
 → T14 MiniMap + Sheet UX
 → T15 COM 削除
 → T16 文書
```

T5〜T8 は T4 後に並行可能。T12/T13 は T11 後に並行可能。

---

## Spec カバレッジ（セルフチェック）

| 設計要件 | タスク |
|----------|--------|
| Excel 廃止 | T11, T15 |
| セル位置無視・多重集合 | T5, T9 |
| 背景色 | T2, T5 |
| テーブル border 検出 | T3 |
| 行削除 12345 vs 1245 | T6, T12 |
| 画像位置無視・系列 8vs9 | T4, T7 |
| スクショずれ・領域ハイライト | T7, T13 |
| 赤3px＋黄50%・ON/OFF | T10, T13 |
| 図形 | T8 |
| 同名／片側／異名シート | T9, T14 |
| MiniMap 現在シートのみ | T14 |
| 行高列幅無視 | 全抽出・UI で非対応（明示的に作らない） |
| 必須シナリオテスト | T9, T16 |

---

## リスクメモ（実装者向け）

- theme 色の完全再現はしない。rgb 直指定＋主要 theme で十分。
- border 検出失敗表は LooseCells に落ちる（手動範囲指定はスコープ外）。
- ECC 位置合わせ失敗時は平行移動ゼロで差分（過検出時は面積閾値を上げる）。
- 旧 auto-live COM テストは T15 で破棄し、ContentDiffSmoke を正とする。

---

## 完了の定義

- `ContentDiffSmoke` が設計書 7.1 の全シナリオ PASS
- Excel 未インストール環境でアプリ起動・比較・ハイライトトグル・シート切替・MiniMap が現在シートのみ
- Release x64 ビルド成功、COM プロジェクト参照なし
- 要件定義が内容比較方針と矛盾しない
