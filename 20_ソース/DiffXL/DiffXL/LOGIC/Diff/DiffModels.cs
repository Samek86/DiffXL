using System;
using System.Collections.Generic;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 差分の種類。
    /// </summary>
    public enum DiffKind
    {
        /// <summary>セルテキスト差分。</summary>
        Text,

        /// <summary>画像内容差分。</summary>
        Image,

        /// <summary>左のみに存在する画像。</summary>
        ImageOnlyLeft,

        /// <summary>右のみに存在する画像。</summary>
        ImageOnlyRight,

        /// <summary>シート構成などの構造差分。</summary>
        Structure,

        /// <summary>セル背景色差分。</summary>
        Background,

        /// <summary>テーブル行の削除（左のみ）。</summary>
        TableRowDelete,

        /// <summary>テーブル行の挿入（右のみ）。</summary>
        TableRowInsert,

        /// <summary>テーブル内セルの内容変更。</summary>
        TableCellChange,

        /// <summary>図形内容差分。</summary>
        Shape,

        /// <summary>左のみに存在する図形。</summary>
        ShapeOnlyLeft,

        /// <summary>右のみに存在する図形。</summary>
        ShapeOnlyRight
    }

    /// <summary>
    /// 1 件の差分。
    /// </summary>
    public sealed class DiffItem
    {
        /// <summary>
        /// 差分の種類。
        /// </summary>
        public DiffKind Kind { get; set; }

        /// <summary>
        /// 左シート名。
        /// </summary>
        public string SheetLeft { get; set; }

        /// <summary>
        /// 右シート名。
        /// </summary>
        public string SheetRight { get; set; }

        /// <summary>
        /// 左セル番地など（例: B12）。
        /// </summary>
        public string AddressLeft { get; set; }

        /// <summary>
        /// 右セル番地など。
        /// </summary>
        public string AddressRight { get; set; }

        /// <summary>
        /// 要約メッセージ。
        /// </summary>
        public string Summary { get; set; }

        /// <summary>
        /// 左画像のキャッシュパス。
        /// </summary>
        public string LeftImagePath { get; set; }

        /// <summary>
        /// 右画像のキャッシュパス。
        /// </summary>
        public string RightImagePath { get; set; }

        /// <summary>
        /// 差分マスク画像パス。
        /// </summary>
        public string DiffMaskPath { get; set; }

        /// <summary>
        /// 行方向の目安（MiniMap 用。大きいほど下）。
        /// </summary>
        public double OrderHint { get; set; }

        /// <summary>
        /// 画像ローカル座標のハイライト領域一覧（ピクセル）。
        /// </summary>
        public List<HighlightRegion> HighlightRegions { get; set; } = new List<HighlightRegion>();

        /// <summary>
        /// 左テーブル ID。
        /// </summary>
        public string TableIdLeft { get; set; }

        /// <summary>
        /// 右テーブル ID。
        /// </summary>
        public string TableIdRight { get; set; }

        /// <summary>
        /// 左テーブル内の行インデックス（0 始まり。不明時は null）。
        /// </summary>
        public int? RowIndexLeft { get; set; }

        /// <summary>
        /// 右テーブル内の行インデックス（0 始まり。不明時は null）。
        /// </summary>
        public int? RowIndexRight { get; set; }

        /// <summary>
        /// 左セル背景色（#AARRGGBB。なしは null）。
        /// </summary>
        public string BackgroundLeft { get; set; }

        /// <summary>
        /// 右セル背景色（#AARRGGBB。なしは null）。
        /// </summary>
        public string BackgroundRight { get; set; }

        /// <summary>内容ストリームのペア index。未割当は -1。</summary>
        public int StreamPairIndex { get; set; } = -1;
    }

    /// <summary>
    /// 1 回の比較結果。
    /// </summary>
    public sealed class DiffResult
    {
        /// <summary>
        /// 差分一覧。
        /// </summary>
        public List<DiffItem> Items { get; set; } = new List<DiffItem>();

        /// <summary>
        /// 比較に使用したシート対応。
        /// </summary>
        public List<SheetPair> SheetPairs { get; set; } = new List<SheetPair>();

        /// <summary>
        /// 左ファイルパス。
        /// </summary>
        public string LeftPath { get; set; }

        /// <summary>
        /// 右ファイルパス。
        /// </summary>
        public string RightPath { get; set; }

        /// <summary>
        /// 比較に要した時間。
        /// </summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>
        /// エラーメッセージ（致命的失敗時）。
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 比較に使用したキャッシュディレクトリ。
        /// </summary>
        public string CacheDirectory { get; set; }

        /// <summary>
        /// 縦スクロール用の内容対応マップ（シートごと）。
        /// </summary>
        public ContentScrollMapSet ScrollMaps { get; set; } = new ContentScrollMapSet();

        /// <summary>
        /// シートごとの統合対応（画像対応 + スクロールマップ）。
        /// </summary>
        public IList<SheetAlignment> Alignments { get; set; } = new List<SheetAlignment>();

        /// <summary>
        /// 左ブックの正規化内容（内容ベース比較用。未設定可）。
        /// </summary>
        public WorkbookContent LeftContent { get; set; }

        /// <summary>
        /// 右ブックの正規化内容（内容ベース比較用。未設定可）。
        /// </summary>
        public WorkbookContent RightContent { get; set; }

        /// <summary>
        /// 段階別所要時間（読込・表・画像・配置）。
        /// </summary>
        public CompareTimings Timings { get; set; } = new CompareTimings();

        /// <summary>
        /// 内容を比較済みのシートペアキー（"左\t右"）。
        /// </summary>
        public List<string> ComparedPairKeys { get; set; } = new List<string>();

        /// <summary>
        /// UI がシート遅延比較を使ったか（状態表示用）。
        /// </summary>
        public bool IsLazy { get; set; }
    }

    /// <summary>
    /// 比較の段階別ミリ秒。
    /// </summary>
    public sealed class CompareTimings
    {
        /// <summary>xlsx 抽出・モデル構築。</summary>
        public long ReadMs { get; set; }

        /// <summary>セル袋＋表 LCS。</summary>
        public long TableMs { get; set; }

        /// <summary>画像 DP＋視覚比較＋図形。</summary>
        public long ImageMs { get; set; }

        /// <summary>ストリーム Attach／マージ。</summary>
        public long LayoutMs { get; set; }

        /// <summary>初回 Realize（UI）。未計測は -1。</summary>
        public long RealizeMs { get; set; } = -1;

        /// <summary>Compare 全体。</summary>
        public long TotalMs { get; set; }
    }

    /// <summary>
    /// 左右シートの対応。
    /// </summary>
    public sealed class SheetPair
    {
        /// <summary>
        /// 左シート名。
        /// </summary>
        public string LeftSheet { get; set; }

        /// <summary>
        /// 右シート名。
        /// </summary>
        public string RightSheet { get; set; }

        /// <summary>
        /// 手動対応かどうか。
        /// </summary>
        public bool IsManual { get; set; }
    }

    /// <summary>
    /// 比較オプション。
    /// </summary>
    public sealed class CompareOptions
    {
        /// <summary>
        /// 手動シート対応（null なら同名自動）。
        /// </summary>
        public List<SheetPair> ManualSheetPairs { get; set; }

        /// <summary>
        /// 左アンカーセル（例: A10）。未指定可。
        /// </summary>
        public string AnchorLeftAddress { get; set; }

        /// <summary>
        /// 右アンカーセル。未指定可。
        /// </summary>
        public string AnchorRightAddress { get; set; }

        /// <summary>
        /// 手動画像対応ピン（null または空なら自動マッチのみ）。
        /// </summary>
        public List<ManualImagePin> ManualImagePins { get; set; }

        /// <summary>
        /// true なら FocusPair（無ければ先頭ペア）だけ内容比較する。
        /// 既定 false（全シート。ContentDiffSmoke 互換）。
        /// </summary>
        public bool LazySheets { get; set; }

        /// <summary>
        /// LazySheets 時に先に比較するペア。null なら対応の先頭。
        /// </summary>
        public SheetPair FocusPair { get; set; }
    }

    /// <summary>
    /// 左右画像の手動ピン留め（強制ペア）。
    /// </summary>
    public sealed class ManualImagePin
    {
        /// <summary>左シート名。</summary>
        public string LeftSheet { get; set; }

        /// <summary>右シート名。</summary>
        public string RightSheet { get; set; }

        /// <summary>左画像 ContentHash（優先キー）。</summary>
        public string LeftImageHash { get; set; }

        /// <summary>右画像 ContentHash（優先キー）。</summary>
        public string RightImageHash { get; set; }
    }

    /// <summary>
    /// セルの表示値。
    /// </summary>
    public sealed class CellValue
    {
        /// <summary>
        /// A1 形式アドレス。
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// 表示テキスト。
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 行番号（1 始まり）。
        /// </summary>
        public int Row { get; set; }

        /// <summary>
        /// 列番号（1 始まり）。
        /// </summary>
        public int Column { get; set; }
    }

    /// <summary>
    /// 埋め込み画像。
    /// </summary>
    public sealed class EmbeddedImage
    {
        /// <summary>
        /// パッケージ内の元パス（例: xl/media/image1.png）。
        /// </summary>
        public string PackagePath { get; set; }

        /// <summary>
        /// ファイル名。
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 抽出先フルパス。
        /// </summary>
        public string ExtractedPath { get; set; }

        /// <summary>
        /// 関連シート名（不明な場合は null）。
        /// </summary>
        public string SheetName { get; set; }

        /// <summary>
        /// 簡易コンテンツハッシュ（十六進）。
        /// </summary>
        public string ContentHash { get; set; }

        /// <summary>
        /// 画像幅（不明時は 0）。
        /// </summary>
        public int PixelWidth { get; set; }

        /// <summary>
        /// 画像高さ（不明時は 0）。
        /// </summary>
        public int PixelHeight { get; set; }

        /// <summary>
        /// ファイルサイズ（バイト。不明時は 0）。
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// シート上のアンカー行（1 始まり。drawing の from/row + 1。不明時は 0）。
        /// 互換用。実体は <see cref="Anchor"/>.RowStart と同期させる。
        /// </summary>
        public int AnchorRow { get; set; }

        /// <summary>
        /// シート上のアンカー列（1 始まり。drawing の from/col + 1。不明時は 0）。
        /// 互換用。実体は <see cref="Anchor"/>.ColStart と同期させる。
        /// </summary>
        public int AnchorColumn { get; set; }

        /// <summary>
        /// 占有セル矩形（from〜to。1 始まり inclusive）。不明時は null。
        /// </summary>
        public AnchorRect Anchor { get; set; }
    }

    /// <summary>
    /// シート対応の結果（対応ペアと片側のみシート）。
    /// </summary>
    public sealed class SheetMatchResult
    {
        /// <summary>
        /// 対応したシートペア。
        /// </summary>
        public List<SheetPair> Pairs { get; set; } = new List<SheetPair>();

        /// <summary>
        /// 左のみのシート名。
        /// </summary>
        public List<string> LeftOnlySheets { get; set; } = new List<string>();

        /// <summary>
        /// 右のみのシート名。
        /// </summary>
        public List<string> RightOnlySheets { get; set; } = new List<string>();
    }
}
