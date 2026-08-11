using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DiffXL.COMMON;
using OpenCvSharp;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// OpenCV による画像ペア比較・位置合わせ。
    /// </summary>
    public static class ImageDiffService
    {
        /// <summary>
        /// 差分とみなす最小差分画素比率（ComparePair で「同一」扱いの上限・互換用）。
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
        /// 位相相関の応答がこの値未満なら位置合わせ失敗とみなす。
        /// </summary>
        public const double PhaseCorrelateMinResponse = 0.05;

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
        /// 最大辺が maxSide を超える場合にアスペクト比を維持して縮小した複製を返す。
        /// 縮小不要時も複製を返す（呼び出し側が Dispose）。
        /// </summary>
        /// <param name="src">入力画像</param>
        /// <param name="maxSide">最大辺（ピクセル）</param>
        /// <returns>リサイズ後の Mat（呼び出し側 Dispose）</returns>
        public static Mat ResizeMaxSide(Mat src, int maxSide)
        {
            if (src == null || src.Empty())
            {
                return new Mat();
            }

            int w = src.Width;
            int h = src.Height;
            int side = Math.Max(w, h);
            if (maxSide <= 0 || side <= maxSide)
            {
                return src.Clone();
            }

            double scale = maxSide / (double)side;
            int nw = Math.Max(1, (int)Math.Round(w * scale));
            int nh = Math.Max(1, (int)Math.Round(h * scale));
            var dst = new Mat();
            Cv2.Resize(src, dst, new Size(nw, nh), 0, 0, InterpolationFlags.Linear);
            return dst;
        }

        /// <summary>
        /// 右画像を左画像の座標系へ平行移動で位置合わせする。
        /// 位相相関を試し、失敗時はキャンバスを左サイズに合わせて左上揃え。
        /// </summary>
        /// <param name="leftBgr">左 BGR</param>
        /// <param name="rightBgr">右 BGR</param>
        /// <param name="alignedRight">出力（左と同じサイズの BGR）</param>
        /// <returns>位相相関で位置合わせできたとき true、左上揃えフォールバック時 false</returns>
        public static bool AlignTranslation(Mat leftBgr, Mat rightBgr, Mat alignedRight)
        {
            if (leftBgr == null || leftBgr.Empty() || rightBgr == null || rightBgr.Empty()
                || alignedRight == null)
            {
                return false;
            }

            int tw = leftBgr.Width;
            int th = leftBgr.Height;

            // 同一キャンバス上で比較するため、まず左上揃えで右をパディング／クロップ
            using (Mat rightPadded = new Mat(th, tw, leftBgr.Type(), Scalar.All(0)))
            {
                int cw = Math.Min(tw, rightBgr.Width);
                int ch = Math.Min(th, rightBgr.Height);
                using (Mat srcRoi = new Mat(rightBgr, new Rect(0, 0, cw, ch)))
                using (Mat dstRoi = new Mat(rightPadded, new Rect(0, 0, cw, ch)))
                {
                    srcRoi.CopyTo(dstRoi);
                }

                Point2d shift;
                double response;
                bool ok = TryPhaseCorrelateShift(leftBgr, rightPadded, out shift, out response);
                if (!ok || response < PhaseCorrelateMinResponse)
                {
                    rightPadded.CopyTo(alignedRight);
                    return false;
                }

                // 右を shift だけ平行移動（位相相関の (dx,dy) は src2→src1 方向）
                using (Mat warp = Mat.Eye(2, 3, MatType.CV_64FC1))
                {
                    warp.Set<double>(0, 2, shift.X);
                    warp.Set<double>(1, 2, shift.Y);
                    Cv2.WarpAffine(
                        rightPadded,
                        alignedRight,
                        warp,
                        new Size(tw, th),
                        InterpolationFlags.Linear,
                        BorderTypes.Constant,
                        Scalar.All(0));
                }

                return true;
            }
        }

        /// <summary>
        /// 位相相関で平行移動量を推定する。
        /// </summary>
        private static bool TryPhaseCorrelateShift(
            Mat leftBgr,
            Mat rightBgr,
            out Point2d shift,
            out double response)
        {
            shift = new Point2d(0, 0);
            response = 0.0;

            try
            {
                using (Mat leftGray = new Mat())
                using (Mat rightGray = new Mat())
                using (Mat leftF = new Mat())
                using (Mat rightF = new Mat())
                {
                    Cv2.CvtColor(leftBgr, leftGray, ColorConversionCodes.BGR2GRAY);
                    Cv2.CvtColor(rightBgr, rightGray, ColorConversionCodes.BGR2GRAY);
                    leftGray.ConvertTo(leftF, MatType.CV_32FC1);
                    rightGray.ConvertTo(rightF, MatType.CV_32FC1);
                    shift = Cv2.PhaseCorrelate(leftF, rightF, null, out response);
                    return !double.IsNaN(shift.X) && !double.IsNaN(shift.Y)
                        && !double.IsInfinity(shift.X) && !double.IsInfinity(shift.Y);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryPhaseCorrelateShift: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 左右画像を比較し、差分があれば DiffItem を返す。同一なら null。
        /// 位置合わせ後の領域矩形を <see cref="DiffItem.HighlightRegions"/> に載せる。
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

            string maskDir = null;
            string maskFile = null;
            if (!string.IsNullOrEmpty(outMaskPath))
            {
                maskDir = Path.GetDirectoryName(outMaskPath);
                maskFile = Path.GetFileName(outMaskPath);
            }

            ImageVisualDiff visual = ImageVisualComparer.Compare(
                leftPath,
                rightPath,
                maskDir,
                maskFile);

            if (visual != null && visual.IsSame)
            {
                return null;
            }

            var regions = visual != null && visual.Regions != null
                ? visual.Regions
                : new List<HighlightRegion>();

            string maskPath = visual != null ? visual.MaskPath : outMaskPath;

            return new DiffItem
            {
                Kind = DiffKind.Image,
                SheetLeft = sheetLeft,
                SheetRight = sheetRight,
                LeftImagePath = leftPath,
                RightImagePath = rightPath,
                DiffMaskPath = maskPath,
                HighlightRegions = regions,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "画像差分 {0} ↔ {1} (regions={2})",
                    Path.GetFileName(leftPath),
                    Path.GetFileName(rightPath),
                    regions.Count),
                OrderHint = orderHint
            };
        }
    }
}
