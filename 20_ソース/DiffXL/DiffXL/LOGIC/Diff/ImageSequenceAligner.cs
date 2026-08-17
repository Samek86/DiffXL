using System;
using System.Collections.Generic;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 埋め込み画像の出現順系列を DP で対応付ける。
    /// 類似度は ContentHash 一致または見た目差分比率から算出し、アンカー位置はコストに含めない。
    /// </summary>
    public static class ImageSequenceAligner
    {
        /// <summary>
        /// SkipLeft / SkipRight 1 回あたりのコスト。
        /// </summary>
        private const double SkipCost = 0.4;

        /// <summary>
        /// Match に必要な最小類似度（1 - ImageRejectDiffRatio。未設定時は 0.55）。
        /// ContentStreamBuilder の画像 Match も同じフロアを使う。
        /// </summary>
        public static double MatchFloor
        {
            get
            {
                double reject = 0.45;
                if (AppSettings.Current != null && AppSettings.Current.Diff != null)
                {
                    reject = AppSettings.Current.Diff.ImageRejectDiffRatio;
                }

                return 1.0 - reject;
            }
        }

        /// <summary>
        /// 左右の画像系列をアラインし、Match / SkipLeft / SkipRight のステップ列を返す。
        /// hash 一致は類似度 1.0。否则は <see cref="ImageDiffService.TryGetDiffRatio"/> から 1-ratio。
        /// </summary>
        /// <param name="left">左画像系列（出現順）</param>
        /// <param name="right">右画像系列（出現順）</param>
        /// <returns>先頭から末尾への AlignStep 列</returns>
        public static IList<AlignStep> Align(
            IList<EmbeddedImage> left,
            IList<EmbeddedImage> right)
        {
            IList<EmbeddedImage> leftList = left ?? Array.Empty<EmbeddedImage>();
            IList<EmbeddedImage> rightList = right ?? Array.Empty<EmbeddedImage>();

            int n = leftList.Count;
            int m = rightList.Count;

            return SequenceAligner.Align(
                n,
                m,
                (i, j) => ImageSimilarity(leftList[i], rightList[j]),
                MatchFloor,
                SkipCost);
        }

        /// <summary>
        /// 2 画像の類似度（0..1）。位置情報は使わない。
        /// ContentStreamBuilder のブロック対応でも同じ基準を使う。
        /// </summary>
        public static double ComputeSimilarity(EmbeddedImage left, EmbeddedImage right)
        {
            if (left == null || right == null)
            {
                return 0.0;
            }

            // ContentHash 一致は見た目同一とみなす
            if (!string.IsNullOrEmpty(left.ContentHash)
                && !string.IsNullOrEmpty(right.ContentHash)
                && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            string lp = left.ExtractedPath;
            string rp = right.ExtractedPath;
            if (string.IsNullOrEmpty(lp) || string.IsNullOrEmpty(rp))
            {
                // パス未抽出時はファイル名一致のみ
                if (!string.IsNullOrEmpty(left.FileName)
                    && string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    return 0.7;
                }

                return 0.0;
            }

            double? ratio = ImageDiffService.TryGetDiffRatio(
                lp, rp, left.ContentHash, right.ContentHash);
            if (!ratio.HasValue)
            {
                if (!string.IsNullOrEmpty(left.FileName)
                    && string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    return 0.7;
                }

                return 0.0;
            }

            double sim = 1.0 - ratio.Value;
            if (sim < 0.0)
            {
                return 0.0;
            }

            if (sim > 1.0)
            {
                return 1.0;
            }

            return sim;
        }

        /// <summary>
        /// 2 画像の類似度（0..1）。位置情報は使わない。
        /// </summary>
        private static double ImageSimilarity(EmbeddedImage left, EmbeddedImage right)
        {
            return ComputeSimilarity(left, right);
        }
    }
}
