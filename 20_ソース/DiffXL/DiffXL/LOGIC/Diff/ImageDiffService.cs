using System;
using System.Globalization;
using System.IO;
using DiffXL.COMMON;
using OpenCvSharp;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// OpenCV による画像ペア比較。
    /// </summary>
    public static class ImageDiffService
    {
        /// <summary>
        /// 差分とみなす最小差分画素比率（ComparePair で「同一」扱いの上限）。
        /// </summary>
        public const double DiffRatioThreshold = 0.001;

        /// <summary>
        /// 二値化閾値（absdiff 後）。
        /// </summary>
        public const double AbsDiffThreshold = 15.0;

        /// <summary>
        /// 画像対応で「改訂同一」とみなす最大差分比率（ImageCorrespondenceService と同一）。
        /// </summary>
        public const double PairMaxDiffRatio = ImageCorrespondenceService.PairMaxDiffRatio;

        /// <summary>
        /// 画像対応で割当禁止とする差分比率（ImageCorrespondenceService と同一）。
        /// </summary>
        public const double RejectDiffRatio = ImageCorrespondenceService.RejectDiffRatio;

        /// <summary>
        /// 差分画素比率を返す（0=同一, 1=全画素差）。読み込み失敗時は null。
        /// スクロール内容対応の類似判定にも使う。
        /// </summary>
        public static double? TryGetDiffRatio(string leftPath, string rightPath)
        {
            if (string.IsNullOrEmpty(leftPath) || !File.Exists(leftPath)
                || string.IsNullOrEmpty(rightPath) || !File.Exists(rightPath))
            {
                return null;
            }

            try
            {
                NativeBootstrap.EnsureNativeBinaries();
                using (Mat left = Cv2.ImRead(leftPath, ImreadModes.Color))
                using (Mat right = Cv2.ImRead(rightPath, ImreadModes.Color))
                {
                    if (left.Empty() || right.Empty())
                    {
                        return null;
                    }

                    int width = Math.Max(left.Width, right.Width);
                    int height = Math.Max(left.Height, right.Height);
                    // 類似判定は縮小して高速化
                    int tw = Math.Min(width, 320);
                    int th = Math.Min(height, 240);
                    using (Mat leftResized = new Mat())
                    using (Mat rightResized = new Mat())
                    using (Mat diff = new Mat())
                    using (Mat gray = new Mat())
                    using (Mat mask = new Mat())
                    {
                        Cv2.Resize(left, leftResized, new Size(tw, th), 0, 0, InterpolationFlags.Linear);
                        Cv2.Resize(right, rightResized, new Size(tw, th), 0, 0, InterpolationFlags.Linear);
                        Cv2.Absdiff(leftResized, rightResized, diff);
                        Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
                        Cv2.Threshold(gray, mask, AbsDiffThreshold, 255, ThresholdTypes.Binary);
                        int nonZero = Cv2.CountNonZero(mask);
                        return nonZero / (double)(tw * th);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryGetDiffRatio: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 左右画像を比較し、差分があれば DiffItem を返す。同一なら null。
        /// </summary>
        /// <param name="leftPath">左画像</param>
        /// <param name="rightPath">右画像</param>
        /// <param name="outMaskPath">マスク出力先</param>
        /// <param name="sheetLeft">左シート</param>
        /// <param name="sheetRight">右シート</param>
        /// <param name="orderHint">並び</param>
        /// <returns>差分アイテムまたは null</returns>
        public static DiffItem ComparePair(
            string leftPath,
            string rightPath,
            string outMaskPath,
            string sheetLeft,
            string sheetRight,
            double orderHint)
        {
            if (string.IsNullOrEmpty(leftPath) || !File.Exists(leftPath))
            {
                throw new FileNotFoundException("左画像がありません。", leftPath);
            }

            if (string.IsNullOrEmpty(rightPath) || !File.Exists(rightPath))
            {
                throw new FileNotFoundException("右画像がありません。", rightPath);
            }

            NativeBootstrap.EnsureNativeBinaries();

            using (Mat left = Cv2.ImRead(leftPath, ImreadModes.Color))
            using (Mat right = Cv2.ImRead(rightPath, ImreadModes.Color))
            {
                if (left.Empty() || right.Empty())
                {
                    Log.Error("画像の読み込みに失敗: " + leftPath + " / " + rightPath);
                    return new DiffItem
                    {
                        Kind = DiffKind.Image,
                        SheetLeft = sheetLeft,
                        SheetRight = sheetRight,
                        LeftImagePath = leftPath,
                        RightImagePath = rightPath,
                        Summary = "画像を読み込めませんでした",
                        OrderHint = orderHint
                    };
                }

                int width = Math.Max(left.Width, right.Width);
                int height = Math.Max(left.Height, right.Height);
                using (Mat leftResized = new Mat())
                using (Mat rightResized = new Mat())
                {
                    Cv2.Resize(left, leftResized, new Size(width, height), 0, 0, InterpolationFlags.Linear);
                    Cv2.Resize(right, rightResized, new Size(width, height), 0, 0, InterpolationFlags.Linear);

                    using (Mat diff = new Mat())
                    using (Mat gray = new Mat())
                    using (Mat mask = new Mat())
                    {
                        Cv2.Absdiff(leftResized, rightResized, diff);
                        Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
                        Cv2.Threshold(gray, mask, AbsDiffThreshold, 255, ThresholdTypes.Binary);

                        int nonZero = Cv2.CountNonZero(mask);
                        double ratio = nonZero / (double)(width * height);
                        if (ratio < DiffRatioThreshold)
                        {
                            return null;
                        }

                        if (!string.IsNullOrEmpty(outMaskPath))
                        {
                            string dir = Path.GetDirectoryName(outMaskPath);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            Cv2.ImWrite(outMaskPath, mask);
                        }

                        return new DiffItem
                        {
                            Kind = DiffKind.Image,
                            SheetLeft = sheetLeft,
                            SheetRight = sheetRight,
                            LeftImagePath = leftPath,
                            RightImagePath = rightPath,
                            DiffMaskPath = outMaskPath,
                            Summary = string.Format(
                                CultureInfo.InvariantCulture,
                                "画像差分 {0} ↔ {1} (diff≈{2:P2})",
                                Path.GetFileName(leftPath),
                                Path.GetFileName(rightPath),
                                ratio),
                            OrderHint = orderHint
                        };
                    }
                }
            }
        }
    }
}
