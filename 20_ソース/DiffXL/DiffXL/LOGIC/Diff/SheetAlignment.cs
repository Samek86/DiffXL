using System.Collections.Generic;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 1 シートペアの統合対応（画像対応 + 縦スクロールマップ）。
    /// </summary>
    public sealed class SheetAlignment
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
        /// 画像対応結果（ペアおよび片側のみ）。
        /// </summary>
        public IList<ImageCorrespondence> Images { get; set; }

        /// <summary>
        /// 縦スクロール用内容対応マップ（未構築時は null 可）。
        /// </summary>
        public ContentScrollMap ScrollMap { get; set; }
    }
}
