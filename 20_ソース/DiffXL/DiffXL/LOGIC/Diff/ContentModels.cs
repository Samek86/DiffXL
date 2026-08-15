using System.Collections.Generic;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 画像ハイライト領域（元画像のローカル座標、ピクセル）。
    /// ImageVisualComparer は内部で縮小比較しても、ここは常に元画像 px で返す。
    /// </summary>
    public sealed class HighlightRegion
    {
        /// <summary>
        /// 左上 X（元画像ピクセル）。
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// 左上 Y（元画像ピクセル）。
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// 幅（元画像ピクセル）。
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 高さ（元画像ピクセル）。
        /// </summary>
        public int Height { get; set; }
    }

    /// <summary>
    /// セルの内容（値・背景色・ボーダー有無）。比較キーは位置ではなく内容。
    /// Address / Row / Column はメタデータのみ。
    /// </summary>
    public sealed class CellContent
    {
        /// <summary>
        /// A1 形式アドレス（メタデータ。比較キーに使わない）。
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// 行番号（1 始まり。メタデータ）。
        /// </summary>
        public int Row { get; set; }

        /// <summary>
        /// 列番号（1 始まり。メタデータ）。
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// 表示テキスト。
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 背景色（#AARRGGBB。なしは null）。
        /// </summary>
        public string BackgroundArgb { get; set; }

        /// <summary>
        /// いずれかの辺にボーダーがあるか。
        /// </summary>
        public bool HasAnyBorder { get; set; }
    }

    /// <summary>
    /// ボーダー検出などから得た表ブロック。
    /// </summary>
    public sealed class TableBlock
    {
        /// <summary>
        /// テーブル識別子（シート内で一意）。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 検出元。"ExcelTable"（xl/tables）または "Border"（罫線 flood）。
        /// </summary>
        public string DetectionSource { get; set; }

        /// <summary>
        /// シート内の出現順インデックス。
        /// </summary>
        public int OrderIndex { get; set; }

        /// <summary>
        /// 開始行（1 始まり inclusive）。
        /// </summary>
        public int RowStart { get; set; }

        /// <summary>
        /// 終了行（1 始まり inclusive）。
        /// </summary>
        public int RowEnd { get; set; }

        /// <summary>
        /// 開始列（1 始まり inclusive）。
        /// </summary>
        public int ColStart { get; set; }

        /// <summary>
        /// 終了列（1 始まり inclusive）。
        /// </summary>
        public int ColEnd { get; set; }

        /// <summary>
        /// 行 → セルの 2 次元内容。
        /// </summary>
        public IList<IList<CellContent>> Rows { get; set; } = new List<IList<CellContent>>();
    }

    /// <summary>
    /// 1 シート分の正規化内容。
    /// </summary>
    public sealed class SheetContent
    {
        /// <summary>
        /// シート名。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// テーブル外のセル（多重集合比較用）。
        /// </summary>
        public List<CellContent> LooseCells { get; set; } = new List<CellContent>();

        /// <summary>
        /// 検出された表ブロック一覧。
        /// </summary>
        public List<TableBlock> Tables { get; set; } = new List<TableBlock>();

        /// <summary>
        /// 埋め込み画像（出現順ソート済み）。
        /// </summary>
        public List<EmbeddedImage> Images { get; set; } = new List<EmbeddedImage>();

        /// <summary>
        /// 図形一覧（出現順）。
        /// </summary>
        public List<ShapeContent> Shapes { get; set; } = new List<ShapeContent>();
    }

    /// <summary>
    /// 1 ブック分の正規化内容。
    /// </summary>
    public sealed class WorkbookContent
    {
        /// <summary>
        /// 元ファイルパス。
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// シート内容一覧。
        /// </summary>
        public List<SheetContent> Sheets { get; set; } = new List<SheetContent>();
    }

    /// <summary>
    /// 図形の内容（テキスト・ラスタ・ハッシュ）。位置はメタのみ。
    /// </summary>
    public sealed class ShapeContent
    {
        /// <summary>
        /// 図形識別子。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// シート内の出現順インデックス。
        /// </summary>
        public int OrderIndex { get; set; }

        /// <summary>
        /// 図形種別（矩形・テキストボックス等の文字列表現）。
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// 図形内テキスト。
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// ラスタ化画像のキャッシュパス（無い場合は null）。
        /// </summary>
        public string RasterPath { get; set; }

        /// <summary>
        /// 内容ハッシュ（十六進など）。
        /// </summary>
        public string ContentHash { get; set; }

        /// <summary>
        /// シート上のアンカー（メタデータのみ。比較キーに使わない）。
        /// </summary>
        public AnchorRect Anchor { get; set; }
    }

    /// <summary>
    /// 系列アラインメントの 1 ステップ操作。
    /// </summary>
    public enum AlignOp
    {
        /// <summary>左右が対応（一致または内容差分）。</summary>
        Match,

        /// <summary>左側のみ（右はスキップ）。</summary>
        SkipLeft,

        /// <summary>右側のみ（左はスキップ）。</summary>
        SkipRight
    }

    /// <summary>
    /// 系列アラインメントの 1 ステップ結果。
    /// </summary>
    public sealed class AlignStep
    {
        /// <summary>
        /// 操作種別。
        /// </summary>
        public AlignOp Op { get; set; }

        /// <summary>
        /// 左系列のインデックス。SkipRight 時は -1。
        /// </summary>
        public int LeftIndex { get; set; }

        /// <summary>
        /// 右系列のインデックス。SkipLeft 時は -1。
        /// </summary>
        public int RightIndex { get; set; }
    }
}
