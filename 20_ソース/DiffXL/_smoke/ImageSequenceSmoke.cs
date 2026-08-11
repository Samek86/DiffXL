using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// ImageSequenceAligner / ImageVisualComparer のスモーク。
/// 1) 8 vs 9 挿入（hash のみ）→ Match×4 SkipRight Match×4
/// 2) 同一画像・異パス → IsSame
/// 3) 一部だけ違う合成 PNG → Regions.Count &gt;= 1
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
        using (var left = new Bitmap(w, h))
        using (var right = new Bitmap(w, h))
        {
            using (var gl = Graphics.FromImage(left))
            using (var gr = Graphics.FromImage(right))
            {
                gl.Clear(Color.FromArgb(255, 40, 40, 180));
                gr.Clear(Color.FromArgb(255, 40, 40, 180));
                // 右だけ黄色い矩形を追加（明確な領域差）
                gr.FillRectangle(
                    new SolidBrush(Color.FromArgb(255, 240, 220, 20)),
                    new Rectangle(w / 2, h / 3, 28, 22));
            }

            left.Save(leftPath, ImageFormat.Png);
            right.Save(rightPath, ImageFormat.Png);
        }
    }
}
