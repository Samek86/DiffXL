using System.Collections.Generic;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// シートペアの統合対応（画像対応 + 縦スクロールマップ）を構築する。
    /// 画像マッチは呼び出し側で完了済みの <see cref="ImageCorrespondence"/> を受け取り、再マッチしない。
    /// </summary>
    public static class SheetAlignmentBuilder
    {
        /// <summary>
        /// 左右セルと画像対応から <see cref="SheetAlignment"/> を構築する。
        /// <see cref="SheetAlignment.ScrollMap"/> は占有レンジ付きの内容対応マップ。
        /// </summary>
        public static SheetAlignment Build(
            string leftSheet,
            string rightSheet,
            IList<CellValue> leftCells,
            IList<CellValue> rightCells,
            IList<ImageCorrespondence> images)
        {
            ContentScrollMap scrollMap = ContentScrollMap.Build(
                leftSheet,
                rightSheet,
                leftCells,
                rightCells,
                images);

            return new SheetAlignment
            {
                LeftSheet = leftSheet,
                RightSheet = rightSheet,
                Images = images,
                ScrollMap = scrollMap
            };
        }
    }
}
