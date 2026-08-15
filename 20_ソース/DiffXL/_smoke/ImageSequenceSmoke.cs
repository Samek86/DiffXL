using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

/// <summary>
/// ImageSequenceAligner / ImageVisualComparer のスモーク。
/// 1) 8 vs 9 挿入（hash のみ）→ Match×4 SkipRight Match×4
/// 2) 同一画像・異パス → IsSame
/// 3) 一部だけ違う合成 PNG → Regions.Count &gt;= 1
/// 4) max-side &gt; 1024 の部分差 → 領域が元画像座標へ拡大されていること
/// 5) 類似度 0.20 は engine / stream とも Skip。ハッシュ同一は両方 Match。
/// </summary>
class Program
{
    static int Main()
    {
        int fail = 0;
        string dir = Path.Combine(
            Path.GetTempPath(),
            "diffxl_imgseq_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);

        try
        {
            // --- 1: 8 vs 9 挿入（位置はコストに入れない。hash で対応） ---
            {
                var left = new List<EmbeddedImage>();
                for (int i = 0; i < 8; i++)
                {
                    left.Add(Img("L" + i, "hash-" + i, row: 100 + i * 10));
                }

                var right = new List<EmbeddedImage>();
                for (int i = 0; i < 4; i++)
                {
                    right.Add(Img("R" + i, "hash-" + i, row: 1 + i));
                }

                right.Add(Img("R-insert", "hash-INSERT", row: 999));
                for (int i = 4; i < 8; i++)
                {
                    right.Add(Img("R" + (i + 1), "hash-" + i, row: 1 + i));
                }

                IList<AlignStep> steps = ImageSequenceAligner.Align(left, right);
                Console.WriteLine("case1 steps=" + steps.Count);
                for (int k = 0; k < steps.Count; k++)
                {
                    AlignStep s = steps[k];
                    Console.WriteLine(string.Format(
                        "  [{0}] {1} L={2} R={3}",
                        k, s.Op, s.LeftIndex, s.RightIndex));
                }

                var expectedOps = new[]
                {
                    AlignOp.Match, AlignOp.Match, AlignOp.Match, AlignOp.Match,
                    AlignOp.SkipRight,
                    AlignOp.Match, AlignOp.Match, AlignOp.Match, AlignOp.Match
                };
                var expectedPairs = new[]
                {
                    Tuple.Create(0, 0),
                    Tuple.Create(1, 1),
                    Tuple.Create(2, 2),
                    Tuple.Create(3, 3),
                    Tuple.Create(-1, 4),
                    Tuple.Create(4, 5),
                    Tuple.Create(5, 6),
                    Tuple.Create(6, 7),
                    Tuple.Create(7, 8)
                };

                if (steps.Count != expectedOps.Length)
                {
                    Console.WriteLine("FAIL case1 count expected=" + expectedOps.Length
                        + " actual=" + steps.Count);
                    fail++;
                }
                else
                {
                    int case1Fail = 0;
                    for (int k = 0; k < expectedOps.Length; k++)
                    {
                        if (steps[k].Op != expectedOps[k]
                            || steps[k].LeftIndex != expectedPairs[k].Item1
                            || steps[k].RightIndex != expectedPairs[k].Item2)
                        {
                            Console.WriteLine(string.Format(
                                "FAIL case1 step[{0}] expected {1} L={2} R={3} actual {4} L={5} R={6}",
                                k,
                                expectedOps[k],
                                expectedPairs[k].Item1,
                                expectedPairs[k].Item2,
                                steps[k].Op,
                                steps[k].LeftIndex,
                                steps[k].RightIndex));
                            case1Fail++;
                        }
                    }

                    if (case1Fail == 0)
                    {
                        // AnchorRow が左右で全く違うが Match できている＝位置非コスト
                        Console.WriteLine("OK case1 8vs9 insert SkipRight@R4 (position ignored)");
                    }
                    else
                    {
                        fail += case1Fail;
                    }
                }
            }

            // --- 2: 同一画像・異パス → IsSame ---
            {
                string p1 = Path.Combine(dir, "same_a.png");
                string p2 = Path.Combine(dir, "same_b.png");
                WriteSolidPng(p1, 80, 60, 30, 120, 200);
                File.Copy(p1, p2, true);

                ImageVisualDiff v = ImageVisualComparer.Compare(p1, p2, dir, "mask_same.png");
                Console.WriteLine("case2 IsSame=" + v.IsSame
                    + " regions=" + (v.Regions != null ? v.Regions.Count : -1));
                if (!v.IsSame)
                {
                    Console.WriteLine("FAIL case2 same visual different path should be IsSame");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case2 IsSame for identical bytes different path");
                }
            }

            // --- 3: 一部だけ違う → Regions.Count >= 1 ---
            {
                string leftP = Path.Combine(dir, "partial_l.png");
                string rightP = Path.Combine(dir, "partial_r.png");
                WritePartialDiffPair(leftP, rightP, 120, 90);

                ImageVisualDiff v = ImageVisualComparer.Compare(
                    leftP, rightP, dir, "mask_partial.png");
                int rc = v.Regions != null ? v.Regions.Count : 0;
                Console.WriteLine("case3 IsSame=" + v.IsSame
                    + " regions=" + rc
                    + " mask=" + (v.MaskPath ?? "(null)"));
                if (v.IsSame || rc < 1)
                {
                    Console.WriteLine("FAIL case3 expected Regions.Count >= 1");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case3 partial diff regions=" + rc);
                    foreach (HighlightRegion r in v.Regions)
                    {
                        Console.WriteLine(string.Format(
                            "  region x={0} y={1} w={2} h={3}",
                            r.X, r.Y, r.Width, r.Height));
                    }
                }

                // ComparePair 経由でも HighlightRegions が載ること
                DiffItem item = ImageDiffService.ComparePair(
                    leftP, rightP, Path.Combine(dir, "pair_mask.png"),
                    "S", "S", 0);
                if (item == null
                    || item.HighlightRegions == null
                    || item.HighlightRegions.Count < 1)
                {
                    Console.WriteLine("FAIL case3 ComparePair should carry HighlightRegions");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case3 ComparePair HighlightRegions="
                        + item.HighlightRegions.Count);
                }
            }

            // --- 4: 大画像（max side > MaxSide）→ 領域は元画像 px にスケールアップ ---
            {
                int ow = 1600;
                int oh = 1200;
                // 差分矩形（元画像座標）。縮小後は MaxSide=1024 なので side scale=1024/1600=0.64
                int rx = 1000;
                int ry = 700;
                int rw = 100;
                int rh = 80;
                string leftP = Path.Combine(dir, "large_l.png");
                string rightP = Path.Combine(dir, "large_r.png");
                WritePartialDiffPairAt(leftP, rightP, ow, oh, rx, ry, rw, rh);

                ImageVisualDiff v = ImageVisualComparer.Compare(
                    leftP, rightP, dir, "mask_large.png");
                int rc = v.Regions != null ? v.Regions.Count : 0;
                Console.WriteLine("case4 large IsSame=" + v.IsSame + " regions=" + rc);
                if (v.IsSame || rc < 1)
                {
                    Console.WriteLine("FAIL case4 expected Regions.Count >= 1 on large partial");
                    fail++;
                }
                else
                {
                    // 縮小空間のままなら X は ~640 付近。元画像へ戻せば ~1000 付近。
                    double downScale = ImageVisualComparer.MaxSide / (double)Math.Max(ow, oh);
                    int resizedXApprox = (int)Math.Round(rx * downScale);
                    HighlightRegion best = v.Regions
                        .OrderByDescending(r => r.Width * r.Height)
                        .First();
                    Console.WriteLine(string.Format(
                        "  best region x={0} y={1} w={2} h={3} (resized-space x would be ~{4})",
                        best.X, best.Y, best.Width, best.Height, resizedXApprox));

                    // 元画像座標へ拡大されていること: X が縮小空間の推定より明らかに大きい
                    if (best.X <= resizedXApprox + 40)
                    {
                        Console.WriteLine(
                            "FAIL case4 region.X looks like compare-space (not scaled to original)."
                            + " x=" + best.X + " resizedApprox=" + resizedXApprox);
                        fail++;
                    }
                    // 目標矩形付近にあること（モルフォで多少膨らむ）
                    else if (best.X < rx - 80 || best.X > rx + 80
                        || best.Y < ry - 80 || best.Y > ry + 80)
                    {
                        Console.WriteLine(string.Format(
                            "FAIL case4 region far from expected original rect ({0},{1})",
                            rx, ry));
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine("OK case4 large-image regions scaled to original px");
                    }

                    // 単位: ScaleRegionsToOriginal が compare < original で拡大すること
                    var tiny = new List<HighlightRegion>
                    {
                        new HighlightRegion { X = 64, Y = 48, Width = 10, Height = 8 }
                    };
                    List<HighlightRegion> up = ImageVisualComparer.ScaleRegionsToOriginal(
                        tiny, 1024, 768, 1600, 1200);
                    if (up == null || up.Count != 1
                        || up[0].X < 90 || up[0].Width < 14)
                    {
                        Console.WriteLine("FAIL case4 ScaleRegionsToOriginal unit check"
                            + (up != null && up.Count > 0
                                ? string.Format(" got x={0} w={1}", up[0].X, up[0].Width)
                                : " empty"));
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine(string.Format(
                            "OK case4 ScaleRegionsToOriginal unit x={0} w={1}",
                            up[0].X, up[0].Width));
                    }
                }
            }

            // --- 5: 類似度 0.20 は engine / stream とも Skip。ハッシュ同一は両方 Match ---
            {
                string pL = Path.Combine(dir, "sim20_l.png");
                string pR = Path.Combine(dir, "sim20_r.png");
                WriteSameRatioPair(pL, pR, 10, 10, 20);

                var lowL = ImgPath("sim20L", "hash-low-L", pL, 1);
                var lowR = ImgPath("sim20R", "hash-low-R", pR, 1);
                double sim = ImageSequenceAligner.ComputeSimilarity(lowL, lowR);
                Console.WriteLine("case5 sim20=" + sim.ToString("0.####"));
                if (Math.Abs(sim - 0.20) > 0.02)
                {
                    Console.WriteLine("FAIL case5 expected visual sim≈0.20 got " + sim);
                    fail++;
                }
                else
                {
                    IList<AlignStep> engineLow = ImageSequenceAligner.Align(
                        new List<EmbeddedImage> { lowL },
                        new List<EmbeddedImage> { lowR });
                    bool engineSkip = IsPairSkip(engineLow);
                    Console.WriteLine("case5 engine ops=" + FormatOps(engineLow)
                        + " skip=" + engineSkip);
                    if (!engineSkip)
                    {
                        Console.WriteLine("FAIL case5 ImageSequenceAligner should Skip sim 0.20");
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine("OK case5 engine Skip for sim 0.20");
                    }

                    IList<ContentStreamPair> streamLow = ContentStreamBuilder.Align(
                        ContentStreamBuilder.Build(SheetWith(lowL)),
                        ContentStreamBuilder.Build(SheetWith(lowR)));
                    bool streamSkip = IsStreamPairSkip(streamLow);
                    Console.WriteLine("case5 stream ops=" + FormatStreamOps(streamLow)
                        + " skip=" + streamSkip);
                    if (!streamSkip)
                    {
                        Console.WriteLine("FAIL case5 ContentStreamBuilder should Skip sim 0.20");
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine("OK case5 stream Skip for sim 0.20");
                    }
                }

                var sameL = Img("sameL", "hash-IDENT", 1);
                var sameR = Img("sameR", "hash-IDENT", 2);
                IList<AlignStep> engineSame = ImageSequenceAligner.Align(
                    new List<EmbeddedImage> { sameL },
                    new List<EmbeddedImage> { sameR });
                IList<ContentStreamPair> streamSame = ContentStreamBuilder.Align(
                    ContentStreamBuilder.Build(SheetWith(sameL)),
                    ContentStreamBuilder.Build(SheetWith(sameR)));
                bool engineMatch = engineSame.Count == 1 && engineSame[0].Op == AlignOp.Match;
                bool streamMatch = streamSame.Count == 1 && streamSame[0].Op == AlignOp.Match;
                Console.WriteLine("case5 identical-hash engine=" + FormatOps(engineSame)
                    + " stream=" + FormatStreamOps(streamSame));
                if (!engineMatch)
                {
                    Console.WriteLine("FAIL case5 ImageSequenceAligner should Match identical hash");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case5 engine Match for identical hash");
                }

                if (!streamMatch)
                {
                    Console.WriteLine("FAIL case5 ContentStreamBuilder should Match identical hash");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case5 stream Match for identical hash");
                }
            }

            // --- 6: 実行時フロアは ImageRejectDiffRatio と揃える（0.85 → 下限 0.15 なら sim 0.20 は両方 Match） ---
            {
                string pL = Path.Combine(dir, "sim20rt_l.png");
                string pR = Path.Combine(dir, "sim20rt_r.png");
                WriteSameRatioPair(pL, pR, 10, 10, 20);
                var lowL = ImgPath("sim20rtL", "hash-low-rt-L", pL, 1);
                var lowR = ImgPath("sim20rtR", "hash-low-rt-R", pR, 1);
                double prevReject = 0.45;
                if (AppSettings.Current != null && AppSettings.Current.Diff != null)
                {
                    prevReject = AppSettings.Current.Diff.ImageRejectDiffRatio;
                    AppSettings.Current.Diff.ImageRejectDiffRatio = 0.85;
                }

                try
                {
                    double floor = ImageSequenceAligner.MatchFloor;
                    Console.WriteLine("case6 floor=" + floor.ToString("0.####")
                        + " (expect 0.15)");
                    IList<AlignStep> engineLoose = ImageSequenceAligner.Align(
                        new List<EmbeddedImage> { lowL },
                        new List<EmbeddedImage> { lowR });
                    IList<ContentStreamPair> streamLoose = ContentStreamBuilder.Align(
                        ContentStreamBuilder.Build(SheetWith(lowL)),
                        ContentStreamBuilder.Build(SheetWith(lowR)));
                    bool engineMatch = engineLoose.Count == 1 && engineLoose[0].Op == AlignOp.Match;
                    bool streamMatch = streamLoose.Count == 1 && streamLoose[0].Op == AlignOp.Match;
                    Console.WriteLine("case6 reject=0.85 engine=" + FormatOps(engineLoose)
                        + " stream=" + FormatStreamOps(streamLoose));
                    if (Math.Abs(floor - 0.15) > 0.001)
                    {
                        Console.WriteLine("FAIL case6 MatchFloor should be 0.15 got " + floor);
                        fail++;
                    }
                    else if (!engineMatch || !streamMatch)
                    {
                        Console.WriteLine("FAIL case6 both should Match sim 0.20 when reject=0.85");
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine("OK case6 engine+stream same floor (Match at reject 0.85)");
                    }
                }
                finally
                {
                    if (AppSettings.Current != null && AppSettings.Current.Diff != null)
                    {
                        AppSettings.Current.Diff.ImageRejectDiffRatio = prevReject;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL exception: " + ex);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // ignore
            }
        }

        if (fail > 0)
        {
            Console.WriteLine("FAIL ImageSequenceSmoke fails=" + fail);
            return 1;
        }

        Console.WriteLine("PASS ImageSequenceSmoke");
        return 0;
    }

    static EmbeddedImage Img(string name, string hash, int row)
    {
        return new EmbeddedImage
        {
            FileName = name + ".png",
            ContentHash = hash,
            AnchorRow = row,
            AnchorColumn = 1,
            Anchor = new AnchorRect
            {
                RowStart = row,
                RowEnd = row,
                ColStart = 1,
                ColEnd = 1
            }
        };
    }

    static EmbeddedImage ImgPath(string name, string hash, string path, int row)
    {
        EmbeddedImage img = Img(name, hash, row);
        img.ExtractedPath = path;
        return img;
    }

    static SheetContent SheetWith(EmbeddedImage image)
    {
        return new SheetContent
        {
            Name = "S",
            Images = new List<EmbeddedImage> { image }
        };
    }

    static bool IsPairSkip(IList<AlignStep> steps)
    {
        if (steps == null || steps.Count == 0)
        {
            return false;
        }

        bool skipLeft = false;
        bool skipRight = false;
        foreach (AlignStep s in steps)
        {
            if (s.Op == AlignOp.Match)
            {
                return false;
            }

            if (s.Op == AlignOp.SkipLeft)
            {
                skipLeft = true;
            }

            if (s.Op == AlignOp.SkipRight)
            {
                skipRight = true;
            }
        }

        return skipLeft && skipRight;
    }

    static bool IsStreamPairSkip(IList<ContentStreamPair> pairs)
    {
        if (pairs == null || pairs.Count == 0)
        {
            return false;
        }

        bool skipLeft = false;
        bool skipRight = false;
        foreach (ContentStreamPair p in pairs)
        {
            if (p.Op == AlignOp.Match)
            {
                return false;
            }

            if (p.Op == AlignOp.SkipLeft)
            {
                skipLeft = true;
            }

            if (p.Op == AlignOp.SkipRight)
            {
                skipRight = true;
            }
        }

        return skipLeft && skipRight;
    }

    static string FormatOps(IList<AlignStep> steps)
    {
        if (steps == null)
        {
            return "(null)";
        }

        return string.Join(",", steps.Select(s => s.Op.ToString()).ToArray());
    }

    static string FormatStreamOps(IList<ContentStreamPair> pairs)
    {
        if (pairs == null)
        {
            return "(null)";
        }

        return string.Join(",", pairs.Select(p => p.Op.ToString()).ToArray());
    }

    /// <summary>
    /// 左は全面同一色、右は先頭 sameCount 画素だけ同じ色。残りは別色。
    /// </summary>
    static void WriteSameRatioPair(string leftPath, string rightPath, int w, int h, int sameCount)
    {
        using (var left = new Bitmap(w, h))
        using (var right = new Bitmap(w, h))
        {
            Color same = Color.FromArgb(255, 40, 40, 180);
            Color diff = Color.FromArgb(255, 240, 220, 20);
            int n = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    left.SetPixel(x, y, same);
                    right.SetPixel(x, y, n < sameCount ? same : diff);
                    n++;
                }
            }

            left.Save(leftPath, ImageFormat.Png);
            right.Save(rightPath, ImageFormat.Png);
        }
    }

    static void WriteSolidPng(string path, int w, int h, int r, int g, int b)
    {
        using (var bmp = new Bitmap(w, h))
        {
            using (var gfx = Graphics.FromImage(bmp))
            {
                gfx.Clear(Color.FromArgb(255, r, g, b));
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }

    static void WritePartialDiffPair(string leftPath, string rightPath, int w, int h)
    {
        WritePartialDiffPairAt(leftPath, rightPath, w, h, w / 2, h / 3, 28, 22);
    }

    /// <summary>
    /// 右画像だけ指定矩形に黄色い差分を描いたペアを保存する。
    /// </summary>
    static void WritePartialDiffPairAt(
        string leftPath,
        string rightPath,
        int w,
        int h,
        int rectX,
        int rectY,
        int rectW,
        int rectH)
    {
        using (var left = new Bitmap(w, h))
        using (var right = new Bitmap(w, h))
        {
            using (var gl = Graphics.FromImage(left))
            using (var gr = Graphics.FromImage(right))
            {
                gl.Clear(Color.FromArgb(255, 40, 40, 180));
                gr.Clear(Color.FromArgb(255, 40, 40, 180));
                gr.FillRectangle(
                    new SolidBrush(Color.FromArgb(255, 240, 220, 20)),
                    new Rectangle(rectX, rectY, rectW, rectH));
            }

            left.Save(leftPath, ImageFormat.Png);
            right.Save(rightPath, ImageFormat.Png);
        }
    }
}
