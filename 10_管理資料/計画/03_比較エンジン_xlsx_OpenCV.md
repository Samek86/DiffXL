# 03 比較エンジン（xlsx / OpenCV）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 2 つの `.xlsx` からシート対応・テキスト差分・埋め込み画像差分（OpenCV x64）を検出し、UI 非依存の `DiffResult` を返す比較エンジンを構築する。

**Architecture:** `XlsxPackageReader` が ZIP としてブックを読み、セル値と media を抽出。`SheetMatcher` が同名／手動対応を扱い、`TextDiffService` と `ImageDiffService`（OpenCV）が差分を生成。`DiffEngine` がパイプラインを統括する。

**Tech Stack:** .NET Framework 4.8、System.IO.Compression、OpenCvSharp4（x64 ランタイム）または同等 OSS、YamlDotNet は設定参照のみ

## Global Constraints

- **`.xlsx` のみ**
- OpenCV **x64**。ネイティブは `%AppData%\Roaming\DiffXL\native` 展開を前提
- OSS 最大限利用。必要ならソース改修して `third_party/` 管理
- キャッシュは `AppPaths.CacheDir`
- ログは `Log.*`
- 日本語コメント必須
- UI 色は扱わない（色は計画 04）。エンジンは **差分の種類と位置・画像マスク** を返す

**Depends on:** 計画 01  
**並行可:** 計画 02

---

## File Map

| パス | 責務 |
|------|------|
| `LOGIC/Diff/DiffModels.cs` | DiffResult, DiffItem, DiffKind 等 |
| `LOGIC/Diff/XlsxPackageReader.cs` | ZIP 展開的読み取り・共有文字列・画像 |
| `LOGIC/Diff/SheetMatcher.cs` | シート対応 |
| `LOGIC/Diff/TextDiffService.cs` | セルテキスト差分 |
| `LOGIC/Diff/ImageDiffService.cs` | OpenCV 画像差分 |
| `LOGIC/Diff/DiffEngine.cs` | オーケストレーション |
| `LOGIC/Diff/AnchorOptions.cs` | 手動アンカー |
| `COMMON/NativeBootstrap.cs` | OpenCV DLL 展開（01 の拡張） |

---

### Task 1: 差分モデルを定義する

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs`

**Interfaces:**
- Produces: 下記型（後続 Task はこれを変更せずに利用）

```csharp
/// <summary>
/// 差分の種類。
/// </summary>
public enum DiffKind
{
    Text,
    Image,
    ImageOnlyLeft,
    ImageOnlyRight,
    Structure
}

/// <summary>
/// 1 件の差分。
/// </summary>
public sealed class DiffItem
{
    public DiffKind Kind { get; set; }
    public string SheetLeft { get; set; }
    public string SheetRight { get; set; }
    /// <summary>セル番地など（例: B12）。</summary>
    public string AddressLeft { get; set; }
    public string AddressRight { get; set; }
    public string Summary { get; set; }
    /// <summary>画像差分時のマスクや抽出ファイルパス（キャッシュ）。</summary>
    public string LeftImagePath { get; set; }
    public string RightImagePath { get; set; }
    public string DiffMaskPath { get; set; }
    /// <summary>行方向の目安（MiniMap 用 0..1 でも可）。</summary>
    public double OrderHint { get; set; }
}

/// <summary>
/// 1 回の比較結果。
/// </summary>
public sealed class DiffResult
{
    public List<DiffItem> Items { get; set; } = new List<DiffItem>();
    public List<SheetPair> SheetPairs { get; set; } = new List<SheetPair>();
    public string LeftPath { get; set; }
    public string RightPath { get; set; }
    public TimeSpan Elapsed { get; set; }
}

/// <summary>
/// 左右シートの対応。
/// </summary>
public sealed class SheetPair
{
    public string LeftSheet { get; set; }
    public string RightSheet { get; set; }
    public bool IsManual { get; set; }
}

/// <summary>
/// 比較オプション。
/// </summary>
public sealed class CompareOptions
{
    public List<SheetPair> ManualSheetPairs { get; set; }
    public string AnchorLeftAddress { get; set; }
    public string AnchorRightAddress { get; set; }
}
```

- [x] **Step 1: ファイル作成・プロジェクトに Compile 追加**
- [x] **Step 2: Commit**

```bash
git commit -m "feat: add DiffResult models for compare engine"
```

---

### Task 2: XlsxPackageReader（セルと画像）

**Files:**
- Create: `LOGIC/Diff/XlsxPackageReader.cs`

**Interfaces:**
- Produces:
  - `XlsxPackageReader.Open(string path)`
  - `IReadOnlyList<string> GetSheetNames()`
  - `IEnumerable<CellValue> EnumerateCells(string sheetName)`
  - `IReadOnlyList<EmbeddedImage> ExtractImages(string sheetName, string cacheDir)`
  - `Dispose` で一時展開を掃除（cache は Compare 単位のサブフォルダ推奨）

- [x] **Step 1: ZIP として Open**

```csharp
using (var zip = ZipFile.OpenRead(path))
{
    // xl/workbook.xml, xl/sharedStrings.xml, xl/worksheets/sheetN.xml, xl/media/*
}
```

- [x] **Step 2: 共有文字列とシート上のセル値（A1 形式）を列挙**

初期スコープ: 表示文字列が取れること。数式はキャッシュ値（`<v>`）優先。

- [x] **Step 3: `xl/media/*` を `cache\{compareId}\media\{side}\` に抽出**

シートと画像の対応は drawing XML が取れる範囲で紐付け。紐付け困難な場合はファイル単位比較にフォールバックし、ログに `WARN`。

- [x] **Step 4: サンプル xlsx でシート名・セル・画像枚数をログ出力して確認**

- [x] **Step 5: Commit**

```bash
git commit -m "feat: read xlsx cells and extract media via ZIP"
```

---

### Task 3: SheetMatcher

**Files:**
- Create: `LOGIC/Diff/SheetMatcher.cs`

**Interfaces:**
- Produces: `List<SheetPair> Match(IList<string> left, IList<string> right, List<SheetPair> manualOrNull)`

- [x] **Step 1: 既定は同名同士。manual があればそれで上書き**

```csharp
// 同名
foreach (var name in left.Intersect(right))
    pairs.Add(new SheetPair { LeftSheet = name, RightSheet = name, IsManual = false });
```

- [x] **Step 2: 片側にしかないシートは Structure 差分候補として名前を返す（Engine 側で DiffItem 化）**

- [x] **Step 3: Commit**

```bash
git commit -m "feat: match sheets by name or manual pairs"
```

---

### Task 4: TextDiffService

**Files:**
- Create: `LOGIC/Diff/TextDiffService.cs`

**Interfaces:**
- Produces: `IList<DiffItem> Compare(IEnumerable<CellValue> left, IEnumerable<CellValue> right, SheetPair pair, CompareOptions opt)`

- [x] **Step 1: アドレスをキーに Dictionary 化し、値不一致を Text 差分にする**

- [x] **Step 2: アンカー指定がある場合、その行以降（または指定セル以降）のみ比較する簡易実装**

行番号パース: `B12` → row 12。不完全ならアンカー無視＋ログ。

- [x] **Step 3: 同一テキスト出現を「再同期の手がかり」として OrderHint を安定化（初期は出現順で十分）**

- [x] **Step 4: 1 セルだけ違うサンプルで DiffItem が 1 件になることを確認**

- [x] **Step 5: Commit**

```bash
git commit -m "feat: text cell diff with optional anchor"
```

---

### Task 5: OpenCV x64 導入と Native 展開

**Files:**
- Modify: `DiffXL.csproj`（OpenCvSharp4 / runtime.win-x64 等）
- Modify: `COMMON/NativeBootstrap.cs`
- Document: ライセンス表記を `30_参考資料/licenses/` に置く手順

**Interfaces:**
- Produces: 実行時に OpenCV ネイティブがロードできる状態

- [x] **Step 1: NuGet で OpenCvSharp4 と Windows x64 ランタイムを追加**

例:

```xml
<PackageReference Include="OpenCvSharp4" Version="4.10.0.20240616" />
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.10.0.20240616" />
```

（バージョンは実装時点の安定版でよい。**x64 のみ**を使う）

- [x] **Step 2: NativeBootstrap で runtime を AppData\native へコピーする戦略を決めて実装**

方針のいずれか（実装時に1つに固定）:

1. Costura 対象外の native を埋め込みリソース化し、初回に `NativeDir` へ展開  
2. ビルド後イベントで `native` フォルダを用意し、起動時に AppData へ同期  

単一 exe 目標のため **1 を推奨**。マネージ OpenCvSharp は Costura、native は AppData。

- [x] **Step 3: `PATH` または `OpenCvSharp` の設定で `NativeDir` を参照**

```csharp
// 起動時
Environment.SetEnvironmentVariable(
    "PATH",
    AppPaths.NativeDir + ";" + Environment.GetEnvironmentVariable("PATH"));
```

- [x] **Step 4: 小さな Mat を new して Dispose する煙テストを起動時 Debug のみ実行**

- [x] **Step 5: Commit**

```bash
git commit -m "feat: integrate OpenCvSharp x64 with AppData native deploy"
```

---

### Task 6: ImageDiffService

**Files:**
- Create: `LOGIC/Diff/ImageDiffService.cs`

**Interfaces:**
- Produces:
  - `DiffItem ComparePair(string leftPath, string rightPath, string outMaskPath)`
  - 片側のみ → `ImageOnlyLeft` / `ImageOnlyRight`
  - 差分マスク画像を cache に保存（黄色塗りは UI 側。ここでは差分画素マスク）

- [x] **Step 1: サイズが違う場合はリサイズ or 最大共通領域で absdiff**

```csharp
using (var a = Cv2.ImRead(leftPath))
using (var b = Cv2.ImRead(rightPath))
{
    // absdiff → threshold → 差分あり判定
}
```

- [x] **Step 2: 差分がほぼ 0 なら Items に追加しない**

- [x] **Step 3: 片側のみ画像はスキップして次ペアへ、という Engine 側ポリシーに合わせ、Service は 1 ペア比較に集中**

- [x] **Step 4: 部分変更サンプルで mask ファイルが出力されること**

- [x] **Step 5: Commit**

```bash
git commit -m "feat: OpenCV image pair diff and mask output"
```

---

### Task 7: DiffEngine オーケストレーション

**Files:**
- Create: `LOGIC/Diff/DiffEngine.cs`

**Interfaces:**
- Produces:
  - `DiffResult Compare(string leftPath, string rightPath, CompareOptions options = null)`
  - 進捗: `IProgress<string>` またはイベント `ProgressChanged`

- [x] **Step 1: パイプライン実装**

```
1. 拡張子チェック (.xlsx)
2. cache サブフォルダ作成
3. 左右 XlsxPackageReader.Open
4. SheetMatcher
5. 各 SheetPair:
   - TextDiffService
   - 画像リスト対応（ファイル名/順序/ハッシュ。片側のみは Only*）
   - ImageDiffService
6. DiffResult 返却 + Log.Info 件数
```

- [x] **Step 2: UI なしで呼べるよう public にする**

一時的に MainWindow のデバッグボタン「比較テスト」から呼び出し、ステータスに件数表示でも可。

- [x] **Step 3: 要件の差分パターン表を 1 つずつ通す**

| パターン | 期待 |
|----------|------|
| テキスト微変更 | Text ≥1 |
| 画像部分変更 | Image ≥1 + mask |
| 画像追加 | ImageOnly* |
| シート不一致 | Structure または未対応シート情報 |

- [x] **Step 4: Commit**

```bash
git commit -m "feat: DiffEngine pipeline for text and image compare"
```

---

### Task 8: 受け入れ

- [x] **Step 1: チェックリスト**

| # | 確認 | 期待 |
|---|------|------|
| 1 | 非 xlsx | 例外またはエラー結果 |
| 2 | 同一ファイル同士 | Items 空または 0 件 |
| 3 | AppData\cache | 一時 media / mask が残る（または方針どおり削除） |
| 4 | native | OpenCV ロード成功 |
| 5 | ログ | 比較開始・終了・件数 |

- [x] **Step 2: 計画 00 進捗更新**

---

## Spec Coverage

| 要件 | Task |
|------|------|
| C-01〜C-09 のエンジン側 | Task 3–7 |
| OpenCV x64 | Task 5–6 |
| xlsx ZIP 画像 | Task 2 |
| アンカー | Task 4, 7 |
| キャッシュ AppData | Task 2, 7 |

---

## 改訂履歴

| 版 | 日付 | 内容 |
|----|------|------|
| 1.0 | 2026-08-11 | 初版 |
