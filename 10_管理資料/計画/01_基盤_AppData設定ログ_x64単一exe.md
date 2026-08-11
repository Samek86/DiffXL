# 01 基盤（AppData / 設定 / ログ / x64 / 単一 exe）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** DiffXL の実行基盤として、x64 固定ビルド、AppData 配下のデータ配置、YAML 設定、日次ログ、マネージ DLL の単一 exe 化を実現する。

**Architecture:** `AppPaths` が `%AppData%\Roaming\DiffXL` を一括解決し、`Log` / `AppSettings` がそこに読み書きする。Release では Costura.Fody でマネージ依存を埋め込み、ネイティブ用ディレクトリを事前作成する。

**Tech Stack:** .NET Framework 4.8、WPF、YamlDotNet、Costura.Fody、MSBuild x64

## Global Constraints

- Windows **x64 のみ**
- 設定・ログ・キャッシュ・native → **`%AppData%\Roaming\DiffXL`**
- 設定形式: **YAML**（`settings.yaml`）
- ログ: **1日1ファイル**、`Log.Info` / `Debug` / `Error` / `Exception`
- 配布: 原則 **exe のみ**（マネージ DLL は埋め込み）
- 日本語コメント必須
- 差分色既定: 黄・不透明度 0.5（設定モデルに含める）

---

## File Map

| パス | 責務 |
|------|------|
| `20_ソース/DiffXL/DiffXL/DiffXL.csproj` | x64、PackageReference、Costura |
| `20_ソース/DiffXL/DiffXL/COMMON/AppPaths.cs` | AppData パス解決・ディレクトリ作成 |
| `20_ソース/DiffXL/DiffXL/COMMON/Log.cs` | ファイルログ |
| `20_ソース/DiffXL/DiffXL/COMMON/AppSettings.cs` | YAML 読み書き |
| `20_ソース/DiffXL/DiffXL/COMMON/Common.cs` | アプリ共通定数 |
| `20_ソース/DiffXL/DiffXL/App.xaml.cs` | 起動時初期化 |
| `20_ソース/DiffXL/DiffXL/FodyWeavers.xml` | Costura 設定 |

---

### Task 1: プロジェクトを x64 固定にする

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`
- Modify: `20_ソース/DiffXL/DiffXL.sln`（Platform に x64 を追加）

**Interfaces:**
- Produces: 構成 `Debug|x64` / `Release|x64`。`PlatformTarget=x64`

- [x] **Step 1: csproj の Platform 既定と PropertyGroup を x64 にする**

`DiffXL.csproj` で次を満たす:

```xml
<Platform Condition=" '$(Platform)' == '' ">x64</Platform>
```

`Debug|x64` / `Release|x64` の PropertyGroup を追加し、`PlatformTarget` を `x64` にする。`AnyCPU` は残してもビルド既定では使わない（ドキュメント上は対象外）。

- [x] **Step 2: ソリューション構成を x64 にする**

Visual Studio / 手動編集で `DiffXL.sln` に `x64` を追加し、プロジェクトを `x64` にマップする。

- [x] **Step 3: ビルド確認**

Run:

```powershell
cd "C:\JUN\WORK\DiffXL\20_ソース\DiffXL"
msbuild DiffXL.sln /p:Configuration=Debug /p:Platform=x64
```

Expected: 成功。`bin\x64\Debug\DiffXL.exe`（または csproj の OutputPath に従ったパス）が生成される。

- [x] **Step 4: Commit**

```bash
git add 20_ソース/DiffXL/DiffXL/DiffXL.csproj 20_ソース/DiffXL/DiffXL.sln
git commit -m "build: lock DiffXL platform to x64"
```

---

### Task 2: AppPaths（AppData ルート）を実装する

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/COMMON/AppPaths.cs`
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`（Compile 追加が必要な場合）
- Modify: `20_ソース/DiffXL/DiffXL/COMMON/Common.cs`

**Interfaces:**
- Produces:
  - `AppPaths.Root` → `%AppData%\Roaming\DiffXL`
  - `AppPaths.SettingsFile` → `...\settings.yaml`
  - `AppPaths.LogsDir` / `CacheDir` / `NativeDir`
  - `AppPaths.EnsureDirectories()` — 全ディレクトリ作成
  - `AppPaths.TodayLogFile` → `...\logs\DiffXL_yyyyMMdd.log`

- [x] **Step 1: AppPaths を実装する**

```csharp
/// <summary>
/// DiffXL が利用する AppData 配下のパスを解決する。
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// アプリ名（Roaming 配下フォルダ名）。
    /// </summary>
    public const string AppFolderName = "DiffXL";

    /// <summary>
    /// %AppData%\Roaming\DiffXL
    /// </summary>
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string SettingsFile => Path.Combine(Root, "settings.yaml");
    public static string LogsDir => Path.Combine(Root, "logs");
    public static string CacheDir => Path.Combine(Root, "cache");
    public static string NativeDir => Path.Combine(Root, "native");

    /// <summary>
    /// 当日分のログファイルパスを返す。
    /// </summary>
    public static string TodayLogFile =>
        Path.Combine(LogsDir, "DiffXL_" + DateTime.Now.ToString("yyyyMMdd") + ".log");

    /// <summary>
    /// 必要なディレクトリをすべて作成する。
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(NativeDir);
    }
}
```

- [x] **Step 2: 起動時に EnsureDirectories を呼ぶ骨組みを App に入れる**

`App.xaml.cs` の `OnStartup` で:

```csharp
AppPaths.EnsureDirectories();
```

（Log / Settings 初期化は Task 3・4 で接続）

- [x] **Step 3: 手動確認**

アプリを起動し、エクスプローラで `%AppData%\Roaming\DiffXL` に `logs` / `cache` / `native` ができることを確認する。

- [x] **Step 4: Commit**

```bash
git add 20_ソース/DiffXL/DiffXL/COMMON/AppPaths.cs 20_ソース/DiffXL/DiffXL/App.xaml.cs
git commit -m "feat: add AppPaths under AppData Roaming DiffXL"
```

---

### Task 3: Log.cs を実装する

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/COMMON/Log.cs`

**Interfaces:**
- Produces:
  - `Log.Info(string)` / `Log.Debug(string)` / `Log.Error(string)` / `Log.Exception(Exception)`
  - 出力先: `AppPaths.TodayLogFile`
  - スレッドセーフ（簡易 lock）

- [x] **Step 1: Log クラスを実装する**

```csharp
/// <summary>
/// ファイルへ 1 日 1 ログで出力する。
/// </summary>
public static class Log
{
    private static readonly object Sync = new object();

    public static void Info(string message) => Write("INFO", message);
    public static void Debug(string message) => Write("DEBUG", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Exception(Exception ex)
    {
        if (ex == null) { Write("ERROR", "(null exception)"); return; }
        Write("ERROR", ex.ToString());
    }

    private static void Write(string level, string message)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                       + " [" + level + "] " + message + Environment.NewLine;
            lock (Sync)
            {
                File.AppendAllText(AppPaths.TodayLogFile, line, Encoding.UTF8);
            }
        }
        catch
        {
            // ログ失敗でアプリを落とさない
        }
    }
}
```

- [x] **Step 2: 起動時に 1 行書く**

`App.xaml.cs`:

```csharp
Log.Info("DiffXL 起動");
```

- [x] **Step 3: 確認**

起動後 `%AppData%\Roaming\DiffXL\logs\DiffXL_yyyyMMdd.log` に行が増えること。

- [x] **Step 4: Commit**

```bash
git add 20_ソース/DiffXL/DiffXL/COMMON/Log.cs 20_ソース/DiffXL/DiffXL/App.xaml.cs
git commit -m "feat: implement daily file logging to AppData"
```

---

### Task 4: AppSettings（YAML）を実装する

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/COMMON/AppSettings.cs`
- Package: YamlDotNet（既存 18.1.0 を利用）

**Interfaces:**
- Produces:
  - `AppSettings.Current`（メモリ上の設定）
  - `AppSettings.Load()` / `Save()`
  - モデルに `DiffHighlightColor` / `DiffHighlightOpacity` / `DiffHighlightEnabled` を含む

- [x] **Step 1: 設定モデルと読み書きを実装する**

```csharp
/// <summary>
/// ユーザー設定のルートモデル。
/// </summary>
public class SettingsModel
{
    /// <summary>差分強調関連。</summary>
    public DiffSettings Diff { get; set; } = new DiffSettings();
    /// <summary>UI 関連。</summary>
    public UiSettings Ui { get; set; } = new UiSettings();
    /// <summary>ログレベル。</summary>
    public LogSettings Log { get; set; } = new LogSettings();
}

public class DiffSettings
{
    /// <summary>差分強調の初期表示。</summary>
    public bool HighlightEnabled { get; set; } = true;
    /// <summary>差分色 RGB（例: #FFFF00）。</summary>
    public string HighlightColor { get; set; } = "#FFFF00";
    /// <summary>不透明度 0.0〜1.0。既定 0.5。</summary>
    public double HighlightOpacity { get; set; } = 0.5;
}

public class UiSettings
{
    public bool SyncScroll { get; set; } = true;
    public bool RememberWindowBounds { get; set; } = true;
}

public class LogSettings
{
    public string Level { get; set; } = "Info";
}

/// <summary>
/// YAML で設定を読み書きする。
/// </summary>
public static class AppSettings
{
    public static SettingsModel Current { get; private set; } = new SettingsModel();

    public static void Load()
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.SettingsFile))
        {
            Current = new SettingsModel();
            Save();
            return;
        }
        var yaml = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        Current = deserializer.Deserialize<SettingsModel>(yaml) ?? new SettingsModel();
        if (Current.Diff == null) Current.Diff = new DiffSettings();
        // 不透明度の安全域
        if (Current.Diff.HighlightOpacity < 0) Current.Diff.HighlightOpacity = 0;
        if (Current.Diff.HighlightOpacity > 1) Current.Diff.HighlightOpacity = 1;
    }

    public static void Save()
    {
        AppPaths.EnsureDirectories();
        var serializer = new SerializerBuilder().Build();
        File.WriteAllText(AppPaths.SettingsFile, serializer.Serialize(Current), Encoding.UTF8);
    }
}
```

（YamlDotNet の using: `YamlDotNet.Serialization`）

- [x] **Step 2: 起動時 Load**

```csharp
AppSettings.Load();
Log.Info("設定を読み込み: " + AppPaths.SettingsFile);
```

- [x] **Step 3: 確認**

初回起動後 `settings.yaml` に `highlightOpacity: 0.5` と `highlightColor: '#FFFF00'` 相当が書かれること。

- [x] **Step 4: Commit**

```bash
git add 20_ソース/DiffXL/DiffXL/COMMON/AppSettings.cs 20_ソース/DiffXL/DiffXL/App.xaml.cs
git commit -m "feat: YAML settings with default yellow 50% highlight"
```

---

### Task 5: Costura.Fody でマネージ DLL を埋め込む

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`
- Create: `20_ソース/DiffXL/DiffXL/FodyWeavers.xml`

**Interfaces:**
- Produces: Release ビルド出力が実質 `DiffXL.exe`（+ 必要最小限）中心になる

- [x] **Step 1: パッケージ追加**

```xml
<PackageReference Include="Costura.Fody" Version="5.7.0">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
<PackageReference Include="Fody" Version="6.8.2">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

- [x] **Step 2: FodyWeavers.xml**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Weavers xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:noNamespaceSchemaLocation="FodyWeavers.xsd">
  <Costura />
</Weavers>
```

- [x] **Step 3: Release|x64 ビルドして出力を確認**

Run:

```powershell
msbuild DiffXL.sln /p:Configuration=Release /p:Platform=x64
```

Expected: 出力フォルダに `YamlDotNet.dll` が **並ばない**（Costura により exe 内）。`DiffXL.exe` が単体起動できる。

- [x] **Step 4: Commit**

```bash
git add 20_ソース/DiffXL/DiffXL/DiffXL.csproj 20_ソース/DiffXL/DiffXL/FodyWeavers.xml
git commit -m "build: embed managed dependencies with Costura.Fody"
```

---

### Task 6: ネイティブ展開用の骨組み（OpenCV 用）

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/COMMON/NativeBootstrap.cs`
- Modify: `App.xaml.cs`

**Interfaces:**
- Produces:
  - `NativeBootstrap.EnsureNativeBinaries()` — `AppPaths.NativeDir` を用意し、将来のリソース展開ポイントにする
  - 現状はディレクトリ作成 + ログのみでよい（実 DLL 埋め込みは計画 03 / 06）

- [x] **Step 1: 実装**

```csharp
/// <summary>
/// ネイティブ DLL を AppData\native へ展開するための入口。
/// </summary>
public static class NativeBootstrap
{
    /// <summary>
    /// ネイティブ配置先を準備する。未展開リソースがあればコピーする。
    /// </summary>
    public static void EnsureNativeBinaries()
    {
        AppPaths.EnsureDirectories();
        // 計画 03 で OpenCV 等の埋め込みリソース展開を追加する
        Log.Debug("Native dir ready: " + AppPaths.NativeDir);
    }
}
```

- [x] **Step 2: 起動順を固定**

```
EnsureDirectories → EnsureNativeBinaries → Log 初期メッセージ → AppSettings.Load
```

- [x] **Step 3: Commit**

```bash
git add 20_ソース/DiffXL/DiffXL/COMMON/NativeBootstrap.cs 20_ソース/DiffXL/DiffXL/App.xaml.cs
git commit -m "feat: native bootstrap placeholder for AppData native folder"
```

---

### Task 7: 基盤の受け入れ確認

- [x] **Step 1: チェックリスト実行**

| # | 確認 | 期待 |
|---|------|------|
| 1 | Debug\|x64 ビルド | 成功 |
| 2 | Release\|x64 ビルド | 成功・YamlDotNet.dll 非出力（または Costura 後に不要） |
| 3 | 起動 | クラッシュしない |
| 4 | AppData | `settings.yaml` / `logs\DiffXL_*.log` / `cache` / `native` |
| 5 | settings 既定 | opacity 0.5, color yellow 系 |
| 6 | ログ | `DiffXL 起動` が残る |

- [x] **Step 2: 計画 00 の進捗表で 01 を完了にする**

---

## Spec Coverage（自己レビュー）

| 要件 | Task |
|------|------|
| x64 のみ | Task 1 |
| AppData 集約 | Task 2 |
| ログ 1 日 1 ファイル API | Task 3 |
| YAML 設定・差分色既定 | Task 4 |
| 単一 exe（マネージ） | Task 5 |
| native 展開先 | Task 6 |

---

## 改訂履歴

| 版 | 日付 | 内容 |
|----|------|------|
| 1.0 | 2026-08-11 | 初版 |
