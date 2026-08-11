using System.Windows;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// 差分オーバーレイ位置合わせ用の表示メトリクス。
    /// </summary>
    public sealed class ExcelViewMetrics
    {
        /// <summary>
        /// Excel ウィンドウのスクリーン座標境界。
        /// </summary>
        public Rect ScreenBounds { get; set; }

        /// <summary>
        /// 可視セル範囲のアドレス（取得できる場合）。
        /// </summary>
        public string VisibleRangeAddress { get; set; }

        /// <summary>
        /// アクティブシート名。
        /// </summary>
        public string ActiveSheetName { get; set; }

        /// <summary>
        /// ウィンドウのスクロール行（1 始まり、取得できる場合）。
        /// </summary>
        public int? ScrollRow { get; set; }

        /// <summary>
        /// ウィンドウのスクロール列（1 始まり、取得できる場合）。
        /// </summary>
        public int? ScrollColumn { get; set; }
    }
}
