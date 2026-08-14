using System;
using System.Collections.Generic;
using System.IO;
using DiffXL.COMMON;
using OpenCvSharp;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 画像オーバーレイ Window 用の位置合わせ結果。
    /// 左右は同一キャンバスサイズの PNG バイト列（BGR→エンコード）で返す。
    /// </summary>
    public sealed class ImageOverlayAlignResult
    {
        /// <summary>OpenCV で有意な位置合わせができたとき true。</summary>
        public bool Aligned { get; set; }

        /// <summary>使用手法（PhaseCorrelate / MatchTemplate / TopLeft）。</summary>
        public string Method { get; set; }

        /// <summary>右→左へ合わせる平行移動量 X（px）。</summary>
        public double ShiftX { get; set; }

        /// <summary>右→左へ合わせる平行移動量 Y（px）。</summary>
        public double ShiftY { get; set; }

        /// <summary>信頼度（位相相関 response または matchTemplate スコア）。</summary>
        public double Confidence { get; set; }

        /// <summary>キャンバス幅（px）。</summary>
        public int Width { get; set; }

        /// <summary>キャンバス高さ（px）。</summary>
        public int Height { get; set; }

        /// <summary>左画像 PNG。</summary>
        public byte[] LeftPng { get; set; }

        /// <summary>位置合わせ後の右画像 PNG（左と同じサイズ）。</summary>
        public byte[] RightPng { get; set; }

        /// <summary>エラーメッセージ（失敗時）。</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// ImageCompare の 16×16 総当り位置合わせを置き換える OpenCV 実装。
    /// 位相相関 → テンプレートマッチ → 左上揃えの順で試す。
    /// </summary>
    public static class ImageOverlayAligner
    {
        /// <summary>位相相関の最低応答。</summary>
        public const double PhaseCorrelateMinResponse = ImageDiffService.PhaseCorrelateMinResponse;

        /// <summary>matchTemplate（CCOEFF_NORMED）の最低スコア。</summary>
        public const double MatchTemplateMinScore = 0.55;

        /// <summary>テンプレート一辺（px）。</summary>
        private const int TemplateSide = 64;

        /// <summary>テンプレート探索の最大候補数。</summary>
        private const int MaxTemplateCandidates = 12;

        /// <summary>
        /// 左右画像パスから位置合わせし、同一キャンバスの PNG を返す。
        /// </summary>
        /// <param name="leftPath">左画像</param>
        /// <param name="rightPath">右画像</param>
        /// <returns>結果（失敗時も ErrorMessage 付きで返す）</returns>
        public static ImageOverlayAlignResult Align(string leftPath, string rightPath)
        {
            var result = new ImageOverlayAlignResult
            {
                Aligned = false,
                Method = "TopLeft",
                Confidence = 0.0
            };

            if (string.IsNullOrEmpty(leftPath) || !File.Exists(leftPath))
            {
                result.ErrorMessage = "左画像がありません。";
                return result;
            }

            if (string.IsNullOrEmpty(rightPath) || !File.Exists(rightPath))
            {
                result.ErrorMessage = "右画像がありません。";
                return result;
            }

            try
            {
                NativeBootstrap.EnsureNativeBinaries();

                using (Mat leftRaw = Cv2.ImRead(leftPath, ImreadModes.Color))
                using (Mat rightRaw = Cv2.ImRead(rightPath, ImreadModes.Color))
                {
                    if (leftRaw.Empty() || rightRaw.Empty())
                    {
                        result.ErrorMessage = "画像の読み込みに失敗しました。";
                        return result;
                    }

                    // 表示用は元解像度を優先（極端に大きい場合のみ縮小）
                    const int maxSide = 4096;
                    using (Mat left = ImageDiffService.ResizeMaxSide(leftRaw, maxSide))
                    using (Mat right = ImageDiffService.ResizeMaxSide(rightRaw, maxSide))
                    {
                        int cw = Math.Max(left.Width, right.Width);
                        int ch = Math.Max(left.Height, right.Height);
                        result.Width = cw;
                        result.Height = ch;

                        using (Mat leftCanvas = new Mat(ch, cw, left.Type(), Scalar.All(0)))
                        using (Mat rightCanvas = new Mat(ch, cw, right.Type(), Scalar.All(0)))
                        using (Mat alignedRight = new Mat())
                        {
                            PasteTopLeft(left, leftCanvas);
                            PasteTopLeft(right, rightCanvas);

                            double shiftX = 0.0;
                            double shiftY = 0.0;
                            double confidence = 0.0;
                            string method = "TopLeft";
                            bool aligned = false;

                            Point2d phaseShift;
                            double phaseResponse;
                            if (TryPhaseCorrelate(leftCanvas, rightCanvas, out phaseShift, out phaseResponse)
                                && phaseResponse >= PhaseCorrelateMinResponse)
                            {
                                // 位相相関の符号は実装・版で揺れうるため、± を absdiff で採用
                                Point2d chosen = ChooseBetterShift(leftCanvas, rightCanvas, phaseShift);
                                shiftX = chosen.X;
                                shiftY = chosen.Y;
                                confidence = phaseResponse;
                                method = "PhaseCorrelate";
                                aligned = true;
                            }
                            else
                            {
                                Point2d tplShift;
                                double tplScore;
                                if (TryMatchTemplateShift(leftCanvas, rightCanvas, out tplShift, out tplScore)
                                    && tplScore >= MatchTemplateMinScore)
                                {
                                    shiftX = tplShift.X;
                                    shiftY = tplShift.Y;
                                    confidence = tplScore;
                                    method = "MatchTemplate";
                                    aligned = true;
                                }
                            }

                            if (aligned)
                            {
                                using (Mat warp = Mat.Eye(2, 3, MatType.CV_64FC1))
                                {
                                    warp.Set<double>(0, 2, shiftX);
                                    warp.Set<double>(1, 2, shiftY);
                                    Cv2.WarpAffine(
                                        rightCanvas,
                                        alignedRight,
                                        warp,
                                        new Size(cw, ch),
                                        InterpolationFlags.Linear,
                                        BorderTypes.Constant,
                                        Scalar.All(0));
                                }
                            }
                            else
                            {
                                rightCanvas.CopyTo(alignedRight);
                            }

                            result.Aligned = aligned;
                            result.Method = method;
                            result.ShiftX = shiftX;
                            result.ShiftY = shiftY;
                            result.Confidence = confidence;
                            result.LeftPng = EncodePng(leftCanvas);
                            result.RightPng = EncodePng(alignedRight);
                            return result;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ImageOverlayAligner.Align: " + ex.Message);
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// src を dst の左上へコピーする（はみ出しはクリップ）。
        /// </summary>
        private static void PasteTopLeft(Mat src, Mat dst)
        {
            int cw = Math.Min(src.Width, dst.Width);
            int ch = Math.Min(src.Height, dst.Height);
            using (Mat srcRoi = new Mat(src, new Rect(0, 0, cw, ch)))
            using (Mat dstRoi = new Mat(dst, new Rect(0, 0, cw, ch)))
            {
                srcRoi.CopyTo(dstRoi);
            }
        }

        /// <summary>
        /// shift と -shift のどちらを右画像に適用すると左に近いかを absdiff で選ぶ。
        /// </summary>
        private static Point2d ChooseBetterShift(Mat leftBgr, Mat rightBgr, Point2d shift)
        {
            double errPos = MeasureAlignError(leftBgr, rightBgr, shift.X, shift.Y);
            double errNeg = MeasureAlignError(leftBgr, rightBgr, -shift.X, -shift.Y);
            if (errNeg < errPos)
            {
                return new Point2d(-shift.X, -shift.Y);
            }

            return shift;
        }

        /// <summary>
        /// 右を (dx,dy) だけ Warp したときの左との平均絶対差（低いほど良い）。
        /// </summary>
        private static double MeasureAlignError(Mat leftBgr, Mat rightBgr, double dx, double dy)
        {
            using (Mat warped = new Mat())
            using (Mat diff = new Mat())
            using (Mat gray = new Mat())
            using (Mat warp = Mat.Eye(2, 3, MatType.CV_64FC1))
            {
                warp.Set<double>(0, 2, dx);
                warp.Set<double>(1, 2, dy);
                Cv2.WarpAffine(
                    rightBgr,
                    warped,
                    warp,
                    leftBgr.Size(),
                    InterpolationFlags.Linear,
                    BorderTypes.Constant,
                    Scalar.All(0));
                Cv2.Absdiff(leftBgr, warped, diff);
                Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
                Scalar mean = Cv2.Mean(gray);
                return mean.Val0;
            }
        }

        /// <summary>
        /// 位相相関で平行移動を推定する。
        /// </summary>
        private static bool TryPhaseCorrelate(
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
                using (Mat window = new Mat())
                {
                    Cv2.CvtColor(leftBgr, leftGray, ColorConversionCodes.BGR2GRAY);
                    Cv2.CvtColor(rightBgr, rightGray, ColorConversionCodes.BGR2GRAY);
                    leftGray.ConvertTo(leftF, MatType.CV_32FC1);
                    rightGray.ConvertTo(rightF, MatType.CV_32FC1);
                    Cv2.CreateHanningWindow(window, leftF.Size(), MatType.CV_32FC1);
                    shift = Cv2.PhaseCorrelate(leftF, rightF, window, out response);
                    return !double.IsNaN(shift.X) && !double.IsNaN(shift.Y)
                        && !double.IsInfinity(shift.X) && !double.IsInfinity(shift.Y);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ImageOverlayAligner.TryPhaseCorrelate: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 左画像の特徴的パッチを右画像で matchTemplate し、平行移動を推定する。
        /// ImageCompare の「一致部分探索」を OpenCV で高速・堅牢化した版。
        /// </summary>
        private static bool TryMatchTemplateShift(
            Mat leftBgr,
            Mat rightBgr,
            out Point2d shift,
            out double bestScore)
        {
            shift = new Point2d(0, 0);
            bestScore = 0.0;

            try
            {
                using (Mat leftGray = new Mat())
                using (Mat rightGray = new Mat())
                {
                    Cv2.CvtColor(leftBgr, leftGray, ColorConversionCodes.BGR2GRAY);
                    Cv2.CvtColor(rightBgr, rightGray, ColorConversionCodes.BGR2GRAY);

                    int side = TemplateSide;
                    if (leftGray.Width < side * 2 || leftGray.Height < side * 2
                        || rightGray.Width < side * 2 || rightGray.Height < side * 2)
                    {
                        side = Math.Min(32, Math.Min(leftGray.Width, leftGray.Height) / 2);
                    }

                    if (side < 16)
                    {
                        return false;
                    }

                    List<Point> candidates = CollectTexturedPatches(leftGray, side, MaxTemplateCandidates);
                    if (candidates.Count == 0)
                    {
                        return false;
                    }

                    // 高スコア候補のシフトを集め、中央値で誤マッチを抑える
                    var scoredShifts = new List<KeyValuePair<double, Point2d>>();

                    foreach (Point origin in candidates)
                    {
                        if (origin.X + side > leftGray.Width || origin.Y + side > leftGray.Height)
                        {
                            continue;
                        }

                        using (Mat templ = new Mat(leftGray, new Rect(origin.X, origin.Y, side, side)))
                        {
                            if (templ.Width >= rightGray.Width || templ.Height >= rightGray.Height)
                            {
                                continue;
                            }

                            using (Mat resultMap = new Mat())
                            {
                                Cv2.MatchTemplate(rightGray, templ, resultMap, TemplateMatchModes.CCoeffNormed);
                                double minVal, maxVal;
                                Point minLoc, maxLoc;
                                Cv2.MinMaxLoc(resultMap, out minVal, out maxVal, out minLoc, out maxLoc);
                                if (maxVal < MatchTemplateMinScore)
                                {
                                    continue;
                                }

                                // 右上の一致位置 → 左パッチ原点へ合わせる平行移動
                                var s = new Point2d(origin.X - maxLoc.X, origin.Y - maxLoc.Y);
                                scoredShifts.Add(new KeyValuePair<double, Point2d>(maxVal, s));
                            }
                        }
                    }

                    if (scoredShifts.Count == 0)
                    {
                        return false;
                    }

                    scoredShifts.Sort((a, b) => b.Key.CompareTo(a.Key));
                    // 上位スコアの中央値（外れ値に強い）
                    int take = Math.Min(scoredShifts.Count, Math.Max(3, scoredShifts.Count / 2));
                    var xs = new List<double>(take);
                    var ys = new List<double>(take);
                    double scoreSum = 0.0;
                    for (int i = 0; i < take; i++)
                    {
                        xs.Add(scoredShifts[i].Value.X);
                        ys.Add(scoredShifts[i].Value.Y);
                        scoreSum += scoredShifts[i].Key;
                    }

                    xs.Sort();
                    ys.Sort();
                    shift = new Point2d(xs[xs.Count / 2], ys[ys.Count / 2]);
                    bestScore = scoreSum / take;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ImageOverlayAligner.TryMatchTemplateShift: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 分散が高い（模様がある）パッチ原点をグリッドから収集する。
        /// </summary>
        private static List<Point> CollectTexturedPatches(Mat gray, int side, int maxCount)
        {
            var scored = new List<KeyValuePair<double, Point>>();
            int step = Math.Max(side / 2, 16);
            int maxX = gray.Width - side;
            int maxY = gray.Height - side;
            if (maxX < 0 || maxY < 0)
            {
                return new List<Point>();
            }

            for (int y = 0; y <= maxY; y += step)
            {
                for (int x = 0; x <= maxX; x += step)
                {
                    using (Mat roi = new Mat(gray, new Rect(x, y, side, side)))
                    using (Mat mean = new Mat())
                    using (Mat stddev = new Mat())
                    {
                        Cv2.MeanStdDev(roi, mean, stddev);
                        double s = stddev.At<double>(0);
                        // 平坦（一色）パッチは捨てる
                        if (s < 8.0)
                        {
                            continue;
                        }

                        scored.Add(new KeyValuePair<double, Point>(s, new Point(x, y)));
                    }
                }
            }

            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var list = new List<Point>();
            int n = Math.Min(maxCount, scored.Count);
            for (int i = 0; i < n; i++)
            {
                list.Add(scored[i].Value);
            }

            return list;
        }

        /// <summary>
        /// Mat を PNG バイト列にエンコードする。
        /// </summary>
        private static byte[] EncodePng(Mat bgr)
        {
            byte[] bytes;
            Cv2.ImEncode(".png", bgr, out bytes);
            return bytes;
        }
    }
}
