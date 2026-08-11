using System;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 左右画像の 1 対応結果（ペアまたは片側のみ）。
    /// </summary>
    public sealed class ImageCorrespondence
    {
        /// <summary>
        /// 左画像。右のみの場合は null。
        /// </summary>
        public EmbeddedImage Left { get; set; }

        /// <summary>
        /// 右画像。左のみの場合は null。
        /// </summary>
        public EmbeddedImage Right { get; set; }

        /// <summary>
        /// 差分比率（0=同一, 1=全差）。片側のみは -1。
        /// </summary>
        public double DiffRatio { get; set; }

        /// <summary>
        /// コンテンツハッシュが完全一致したペアか。
        /// </summary>
        public bool IsExactHashMatch { get; set; }

        /// <summary>
        /// 左右がペアになっているか。
        /// </summary>
        public bool IsPaired
        {
            get { return Left != null && Right != null; }
        }

        /// <summary>
        /// 左のみか。
        /// </summary>
        public bool IsLeftOnly
        {
            get { return Left != null && Right == null; }
        }

        /// <summary>
        /// 右のみか。
        /// </summary>
        public bool IsRightOnly
        {
            get { return Left == null && Right != null; }
        }
    }
}
