using System;
using System.Collections.Generic;
using System.IO;
using DiffXL.COMMON;
using OpenCvSharp;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 画像ペアの視覚差分結果（同一判定・マスク・ハイライト領域）。
    /// </summary>
    public sealed class ImageVisualDiff
    {
        /// <summary>
        /// 差分領域がなく同一とみなせるとき true。
        /// </summary>
        public bool IsSame { get; set; }

        /// <summary>
        /// 差分マスク画像の出力パス（互換用。未出力時は null）。
        /// </summary>
        public string MaskPath { get; set; }

        /// <summary>
        /// 元画像ピクセル座標のハイライト領域一覧（ResizeMaxSide 後の比較空間ではない）。
        /// </summary>
        public List<HighlightRegion> Regions { get; set; } = new List<HighlightRegion>();
    }

    /// <summary>
    /// 位置合わせ＋領域矩形による画像視覚比較。
    /// </summary>
    public static class ImageVisualComparer
    {
        /// <summary>
        /// 比較時の最大辺（ピクセル）。これを超える辺はアスペクト維持で縮小。
        /// </summary>
        public const int MaxSide = 1024;

        /// <summary>
        /// 連結成分として残す最小面積（ピクセル）の既定値。
        /// </summary>
        public const int MinRegionArea = 25;

        /// <summary>
        /// 設定または既定から absdiff 二値化閾値を得る。
        /// </summary>
        private static double ResolveAbsDiffThreshold()
        {
            if (AppSettings.Current != null && AppSettings.Current.Diff != null)
            {
                return AppSettings.Current.Diff.ImageAbsDiffThreshold;
            }

            return ImageDiffService.AbsDiffThreshold;
        }

        /// <summary>
        /// 設定または既定から最小領域面積を得る。
        /// </summary>
        private static int ResolveMinRegionArea()
        {
            if (AppSettings.Current != null && AppSettings.Current.Diff != null)
            {
                return AppSettings.Current.Diff.ImageMinRegionArea;
            }

            return MinRegionArea;
        }

        /// <summary>
        /// 左右画像を位置合わせしたうえで視覚差分し、領域矩形を返す。
        /// 領域が 0 件なら <see cref="ImageVisualDiff.IsSame"/> = true。
        /// </summary>
        /// <param name="leftPath">左画像パス</param>
        /// <param name="rightPath">右画像パス</param>
        /// <param name="maskDir">マスク出力ディレクトリ（null/空なら未出力）</param>
        /// <param name="maskFileName">マスクファイル名</param>
        /// <returns>視覚差分結果</returns>
        public static ImageVisualDiff Compare(
            string leftPath,
            string rightPath,
            string maskDir,
            string maskFileName)
        {
            var result = new ImageVisualDiff
            {
                IsSame = false,
                MaskPath = null,
                Regions = new List<HighlightRegion>()
            };

            if (string.IsNullOrEmpty(leftPath) || !File.Exists(leftPath)
                || string.IsNullOrEmpty(rightPath) || !File.Exists(rightPath))
            {
                result.IsSame = false;
                return result;
            }

            try
            {
                NativeBootstrap.EnsureNativeBinaries();
                double absDiffTh = ResolveAbsDiffThreshold();
                int minArea = ResolveMinRegionArea();

                using (Mat leftRaw = Cv2.ImRead(leftPath, ImreadModes.Color))
                using (Mat rightRaw = Cv2.ImRead(rightPath, ImreadModes.Color))
                {
                    if (leftRaw.Empty() || rightRaw.Empty())
                    {
                        return result;
                    }

                    using (Mat left = ImageDiffService.ResizeMaxSide(leftRaw, MaxSide))
                    using (Mat right = ImageDiffService.ResizeMaxSide(rightRaw, MaxSide))
                    using (Mat rightAligned = new Mat())
                    {
                        // 平行移動で位置合わせ（失敗時は左上揃え）
                        ImageDiffService.AlignTranslation(left, right, rightAligned);

                        using (Mat diff = new Mat())
                        using (Mat gray = new Mat())
                        using (Mat mask = new Mat())
                        using (Mat morph = new Mat())
                        {
                            Cv2.Absdiff(left, rightAligned, diff);
                            Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
                            Cv2.Threshold(
                                gray,
                                mask,
                                absDiffTh,
                                255,
                                ThresholdTypes.Binary);

                            // ノイズ除去（オープン）→ 穴埋め寄り（クローズ）
                            using (Mat kernel = Cv2.GetStructuringElement(
                                MorphShapes.Rect,
                                new Size(3, 3)))
                            {
                                Cv2.MorphologyEx(mask, morph, MorphTypes.Open, kernel);
                                Cv2.MorphologyEx(morph, morph, MorphTypes.Close, kernel);
                            }

                            // 比較は ResizeMaxSide 後の空間。UI は元画像 px で重ねるため戻す。
                            List<HighlightRegion> regions = ExtractRegions(morph, minArea);
                            result.Regions = ScaleRegionsToOriginal(
                                regions,
                                left.Width,
                                left.Height,
                                leftRaw.Width,
                                leftRaw.Height);

                            if (result.Regions.Count == 0)
                            {
                                result.IsSame = true;
                                return result;
                            }

                            // マスク画像を互換出力
                            if (!string.IsNullOrEmpty(maskDir) && !string.IsNullOrEmpty(maskFileName))
                            {
                                Directory.CreateDirectory(maskDir);
                                string maskPath = Path.Combine(maskDir, maskFileName);
                                Cv2.ImWrite(maskPath, morph);
                                result.MaskPath = maskPath;
                            }

                            result.IsSame = false;
                            return result;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ImageVisualComparer.Compare: " + ex.Message);
                result.IsSame = false;
                return result;
            }
        }

        /// <summary>
        /// 二値マスクから連結成分の外接矩形を抽出する（背景ラベル 0 を除く）。
        /// 座標は <paramref name="binaryMask"/> のピクセル空間（縮小後の比較空間）。
        /// </summary>
        private static List<HighlightRegion> ExtractRegions(Mat binaryMask, int minArea)
        {
            var regions = new List<HighlightRegion>();
            if (binaryMask == null || binaryMask.Empty())
            {
                return regions;
            }

            using (Mat labels = new Mat())
            using (Mat stats = new Mat())
            using (Mat centroids = new Mat())
            {
                int n = Cv2.ConnectedComponentsWithStats(
                    binaryMask,
                    labels,
                    stats,
                    centroids,
                    PixelConnectivity.Connectivity8);

                // ラベル 0 は背景
                for (int label = 1; label < n; label++)
                {
                    int area = stats.At<int>(label, 4); // CC_STAT_AREA
                    if (area < minArea)
                    {
                        continue;
                    }

                    int x = stats.At<int>(label, 0); // LEFT
                    int y = stats.At<int>(label, 1); // TOP
                    int w = stats.At<int>(label, 2); // WIDTH
                    int h = stats.At<int>(label, 3); // HEIGHT
                    if (w <= 0 || h <= 0)
                    {
                        continue;
                    }

                    regions.Add(new HighlightRegion
                    {
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h
                    });
                }
            }

            return regions;
        }

        /// <summary>
        /// 比較空間（ResizeMaxSide 後）の領域を元画像ピクセル座標へ変換する。
        /// 縮小していない場合は入力をそのまま返す。
        /// </summary>
        /// <param name="regions">比較空間の領域</param>
        /// <param name="compareW">比較時の幅</param>
        /// <param name="compareH">比較時の高さ</param>
        /// <param name="originalW">元画像の幅</param>
        /// <param name="originalH">元画像の高さ</param>
        /// <returns>元画像座標の領域一覧</returns>
        public static List<HighlightRegion> ScaleRegionsToOriginal(
            List<HighlightRegion> regions,
            int compareW,
            int compareH,
            int originalW,
            int originalH)
        {
            if (regions == null || regions.Count == 0)
            {
                return regions ?? new List<HighlightRegion>();
            }

            if (compareW <= 0 || compareH <= 0
                || originalW <= 0 || originalH <= 0
                || (compareW == originalW && compareH == originalH))
            {
                return regions;
            }

            double sx = originalW / (double)compareW;
            double sy = originalH / (double)compareH;
            var scaled = new List<HighlightRegion>(regions.Count);
            foreach (HighlightRegion r in regions)
            {
                if (r == null)
                {
                    continue;
                }

                int x = (int)Math.Round(r.X * sx);
                int y = (int)Math.Round(r.Y * sy);
                int w = Math.Max(1, (int)Math.Round(r.Width * sx));
                int h = Math.Max(1, (int)Math.Round(r.Height * sy));

                // 元画像境界にクリップ
                if (x < 0)
                {
                    w += x;
                    x = 0;
                }

                if (y < 0)
                {
                    h += y;
                    y = 0;
                }

                if (x + w > originalW)
                {
                    w = originalW - x;
                }

                if (y + h > originalH)
                {
                    h = originalH - y;
                }

                if (w <= 0 || h <= 0)
                {
                    continue;
                }

                scaled.Add(new HighlightRegion
                {
                    X = x,
                    Y = y,
                    Width = w,
                    Height = h
                });
            }

            return scaled;
        }
    }
}
