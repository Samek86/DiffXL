# DiffXL UI: Phosphor Icons 積極導入 設計

**日付:** 2026-08-11  
**状態:** 承認済み（会話上）  
**対象:** DiffXL WPF UI（.NET Framework 4.8）

## 目的

MahApps.Metro.IconPacks の **Phosphor Icons** を導入し、主要 UI を「アイコン＋ラベル」中心に改善する。操作の視認性・スキャン性を高め、テキストのみのフラットな見た目を解消する。

## 決定事項

| 項目 | 決定 |
|------|------|
| アイコンパック | Phosphor Icons のみ |
| NuGet | `MahApps.Metro.IconPacks.PhosphorIcons`（全コレクションは入れない） |
| 適用範囲 | 全域（ツールバー・起動・設定・ダイアログ・WorkbookPane・ステータス／ローディング） |
| 表示形式 | アイコン＋ラベル（ラベル非表示モードは作らない） |
| 線のウェイト | Regular を基本。強調トグル ON 時は Fill 等を検討可 |
| MahApps.Metro 本体 | 導入しない（テーマは既存 CommonStyle のまま） |
| Boxicons | 導入しない |
| 単一 exe | Costura.Fody 継続。マネージ DLL は自動埋め込み想定 |

## アーキテクチャ

### 依存関係

- `PackageReference`: `MahApps.Metro.IconPacks.PhosphorIcons`（最新安定版）
- XAML 名前空間: `xmlns:iconPacks="http://metro.mahapps.com/winfx/xaml/iconpacks"`
- コントロール: `iconPacks:PackIconPhosphorIcons`（`Kind`, `Width`, `Height`, `Foreground`）

### スタイル方針

- 既存 `ToolBarButtonStyle` / `ToolBarToggleButtonStyle` / `PrimaryButtonStyle` / 既定 `Button` を維持
- Content 内に `StackPanel`（Horizontal）で `[PackIcon][TextBlock]` を置く
- アイコン色は親 `Foreground` を継承（ツールバー・Primary 白文字に追従）
- サイズ目安: ツールバー 14–16px、プライマリ 18px、見出し 20px
- アイコンとラベルの余白: 右マージン 6px

### 機能・レイアウト

- クリックハンドラ・比較ロジック・設定保存は変更しない
- レイアウト構造は最小限の変更（Content 差し替えと余白調整のみ）

## 画面別アイコン割当

実装時にパッケージの `PackIconPhosphorIconsKind` 列挙と照合して確定する。以下は意図ベースの案。

| 場所 | コントロール | 意図 | Kind 候補 |
|------|-------------|------|-----------|
| 起動 | タイトル横 | アプリ／表 | `Table` / `MicrosoftExcelLogo` |
| 起動 | 左・右 参照 | フォルダを開く | `FolderOpen` |
| 起動 | 比較開始 | 左右比較 | `GitDiff` / `ArrowsLeftRight` |
| ツールバー | シート ラベル | シート一覧 | `Rows` / `Table` |
| ツールバー | 再比較 | 再実行 | `ArrowsClockwise` |
| ツールバー | シート対応 | 対応付け | `Link` / `TreeStructure` |
| ツールバー | アンカー | 固定点 | `Anchor` / `PushPin` |
| ツールバー | 左差し替え | 左ファイル | `FileArrowLeft` 系 |
| ツールバー | 右差し替え | 右ファイル | `FileArrowRight` 系 |
| ツールバー | 差分強調 | 表示 ON/OFF | `HighlighterCircle` / `Eye` |
| ツールバー | 設定 | 歯車 | `Gear` / `GearSix` |
| ツールバー | 最初から | ホーム／戻る | `House` / `ArrowCounterClockwise` |
| WorkbookPane | 開く | 外部で開く | `ArrowSquareOut` / `Export` |
| 設定 | 差分強調 見出し | 色 | `Palette` / `PaintBrush` |
| 設定 | 表示・操作 見出し | 操作 | `Sliders` / `CursorClick` |
| 設定 | 保存 | 保存 | `FloppyDisk` / `Check` |
| 設定 | キャンセル | 閉じる | `X` |
| ダイアログ | 適用して再比較 | 確定 | `Check` |
| ダイアログ | キャンセル | 閉じる | `X` |
| ローディング | 比較中 | 待機 | `Hourglass` / `SpinnerGap` |
| ステータス | 差分 | 差分件数 | `GitDiff` |

## 対象ファイル

| ファイル | 変更内容 |
|----------|----------|
| `DiffXL.csproj` | PackageReference 追加 |
| `STYLE/CommonStyle.xaml` | 必要ならアイコン付き Content の Foreground 伝播調整 |
| `VIEW/MainWindow.xaml` | ツールバー・ローディング・ステータス |
| `VIEW/StartupPanel.xaml` | タイトル・参照・比較開始 |
| `VIEW/SettingsWindow.xaml` | 見出し・保存／キャンセル |
| `VIEW/Controls/WorkbookPane.xaml` | 開くボタン |
| `VIEW/Dialogs/SheetMapDialog.xaml` | 適用／キャンセル |
| `VIEW/Dialogs/AnchorDialog.xaml` | 適用／キャンセル |

コードビハインド（`.xaml.cs`）は原則変更不要。動的に `Content` を差し替えている箇所があれば追従する。

## 対象外（YAGNI）

- MahApps.Metro テーマ本体
- Boxicons / 他パック
- アイコンのみ表示モード
- ユーザー設定によるアイコンテーマ切替
- アニメーション付きスピナーの専用実装（静的アイコンで可）

## 検証

1. x64 Debug ビルド成功
2. 起動・比較・設定・シート対応・アンカー各画面でアイコン表示
3. ツールバー Toggle（差分強調）の ON/OFF で前景色が破綻しないこと
4. Release 単一 exe 起動時もアイコン DLL 解決（Costura）

## リスク

| リスク | 緩和 |
|--------|------|
| Kind 名が想定と異なる | ビルド時に列挙／ドキュメント照合 |
| Costura でアイコン DLL が漏れる | Release 起動確認。必要なら Unmanaged ではなく Managed 埋め込み確認 |
| ツールバー横幅不足 | MinWidth 調整、必要なら余白縮小 |
| Foreground が黒固定 | ContentPresenter 内 Style を Path/アイコンにも追従 |
