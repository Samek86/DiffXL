using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DiffXL.LOGIC.Diff;
using OpenCvSharp;

/// <summary>
/// 画像最適対応（Hungarian）のスモーク。
/// L: A同一, B改訂, C左のみ / R: A同一, B改訂, D右のみ
/// 期待: Pair(A-A), Pair(B-B), LeftOnly(C), RightOnly(D)。B が D と誤ペアしないこと。
/// </summary>
class Program
{
    static int Main()
    {
        Console.WriteLine("PairMaxDiffRatio=" + ImageCorrespondenceService.PairMaxDiffRatio
            + " RejectDiffRatio=" + ImageCorrespondenceService.RejectDiffRatio
            + " (ImageDiffService.PairMax=" + ImageDiffService.PairMaxDiffRatio + ")");

        string dir = Path.Combine(Path.GetTempPath(), "diffxl_imgmatch_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        int fail = 0;

        try
        {
            // A: 同一（左右とも赤べた）
            string aPath = Path.Combine(dir, "A.png");
            WriteSolid(aPath, 64, 64, 0, 0, 220);

            // B: 改訂（青ベース + 緑矩形の位置が左右で少し違う）
            string bL = Path.Combine(dir, "B_left.png");
            string bR = Path.Combine(dir, "B_right.png");
            WriteRevisedPair(bL, bR, 80, 60);

            // C: 左のみ（黄 + 黒ストライプ — 他と大きく異なる）
            string cPath = Path.Combine(dir, "C.png");
            WriteStripes(cPath, 48, 48, 0, 200, 200, 0, 0, 0);

            // D: 右のみ（マゼンタ市松 — B や C と誤ペアしない）
            string dPath = Path.Combine(dir, "D.png");
            WriteChecker(dPath, 48, 48, 200, 0, 200, 255, 255, 255);

            string hashA = FileHash(aPath);
            string hashBL = FileHash(bL);
            string hashBR = FileHash(bR);
            string hashC = FileHash(cPath);
            string hashD = FileHash(dPath);

            Console.WriteLine("hash A=" + hashA.Substring(0, 8)
                + " BL=" + hashBL.Substring(0, 8)
                + " BR=" + hashBR.Substring(0, 8)
                + " C=" + hashC.Substring(0, 8)
                + " D=" + hashD.Substring(0, 8));

            // 事前に差分比率をログ
            LogRatio("A-A", aPath, aPath);
            LogRatio("B-B", bL, bR);
            LogRatio("B-D", bL, dPath);
            LogRatio("C-D", cPath, dPath);
            LogRatio("C-B", cPath, bR);
            LogRatio("A-D", aPath, dPath);

            var left = new List<EmbeddedImage>
            {
                Img("A", aPath, hashA, 10, 64, 64),
                Img("B", bL, hashBL, 20, 80, 60),
                Img("C", cPath, hashC, 30, 48, 48),
            };
            var right = new List<EmbeddedImage>
            {
                Img("A", aPath, hashA, 10, 64, 64),
                Img("B", bR, hashBR, 20, 80, 60),
                Img("D", dPath, hashD, 40, 48, 48),
            };

            IList<ImageCorrespondence> result = ImageCorrespondenceService.Match(left, right);
            Console.WriteLine("correspondences=" + result.Count);
            foreach (var c in result)
            {
                string ln = c.Left != null ? Tag(c.Left) : "-";
                string rn = c.Right != null ? Tag(c.Right) : "-";
                Console.WriteLine("  " + ln + " <-> " + rn
                    + " paired=" + c.IsPaired
                    + " exact=" + c.IsExactHashMatch
                    + " ratio=" + c.DiffRatio.ToString("0.####")
                    + " leftOnly=" + c.IsLeftOnly
                    + " rightOnly=" + c.IsRightOnly);
            }

            // 期待: 4 件（A-A, B-B, C only, D only）
            if (result.Count != 4)
            {
                Console.WriteLine("FAIL count expected 4 got " + result.Count);
                fail++;
            }

            var pairAA = result.FirstOrDefault(c =>
                c.IsPaired && Tag(c.Left) == "A" && Tag(c.Right) == "A");
            if (pairAA == null)
            {
                Console.WriteLine("FAIL missing Pair(A-A)");
                fail++;
            }
            else if (!pairAA.IsExactHashMatch || pairAA.DiffRatio != 0)
            {
                Console.WriteLine("FAIL A-A should be exact hash DiffRatio=0");
                fail++;
            }
            else
            {
                Console.WriteLine("OK Pair(A-A) exact");
            }

            var pairBB = result.FirstOrDefault(c =>
                c.IsPaired && Tag(c.Left) == "B" && Tag(c.Right) == "B");
            if (pairBB == null)
            {
                Console.WriteLine("FAIL missing Pair(B-B)");
                fail++;
            }
            else if (pairBB.IsExactHashMatch)
            {
                Console.WriteLine("FAIL B-B should NOT be exact hash (revised)");
                fail++;
            }
            else if (pairBB.DiffRatio < 0 || pairBB.DiffRatio > ImageCorrespondenceService.PairMaxDiffRatio)
            {
                Console.WriteLine("FAIL B-B DiffRatio out of range: " + pairBB.DiffRatio);
                fail++;
            }
            else
            {
                Console.WriteLine("OK Pair(B-B) revised ratio=" + pairBB.DiffRatio.ToString("0.####"));
            }

            var leftOnlyC = result.FirstOrDefault(c => c.IsLeftOnly && Tag(c.Left) == "C");
            if (leftOnlyC == null)
            {
                Console.WriteLine("FAIL missing LeftOnly(C)");
                fail++;
            }
            else if (leftOnlyC.DiffRatio != -1)
            {
                Console.WriteLine("FAIL LeftOnly DiffRatio should be -1");
                fail++;
            }
            else
            {
                Console.WriteLine("OK LeftOnly(C)");
            }

            var rightOnlyD = result.FirstOrDefault(c => c.IsRightOnly && Tag(c.Right) == "D");
            if (rightOnlyD == null)
            {
                Console.WriteLine("FAIL missing RightOnly(D)");
                fail++;
            }
            else
            {
                Console.WriteLine("OK RightOnly(D)");
            }

            // B が D と誤ペアしていない
            bool badBD = result.Any(c =>
                c.IsPaired
                && ((Tag(c.Left) == "B" && Tag(c.Right) == "D")
                    || (Tag(c.Left) == "C" && Tag(c.Right) == "D")
                    || (Tag(c.Left) == "B" && Tag(c.Right) == "A")));
            if (badBD)
            {
                Console.WriteLine("FAIL unexpected bad pairing involving B/C/D");
                fail++;
            }
            else
            {
                Console.WriteLine("OK no B-D / C-D / B-A mis-pair");
            }

            // 空入力
            var empty = ImageCorrespondenceService.Match(null, null);
            if (empty == null || empty.Count != 0)
            {
                Console.WriteLine("FAIL empty match should be empty list");
                fail++;
            }
            else
            {
                Console.WriteLine("OK empty inputs");
            }

            // 右のみ 1 枚
            var rightOnly = ImageCorrespondenceService.Match(
                new List<EmbeddedImage>(),
                new List<EmbeddedImage> { Img("D", dPath, hashD, 1, 48, 48) });
            if (rightOnly.Count != 1 || !rightOnly[0].IsRightOnly)
            {
                Console.WriteLine("FAIL right-only single");
                fail++;
            }
            else
            {
                Console.WriteLine("OK single RightOnly");
            }

            // 手動ピン: 通常は誤ペアしない C↔D を強制
            var pinCD = new List<ManualImagePin>
            {
                new ManualImagePin
                {
                    LeftSheet = "S",
                    RightSheet = "S",
                    LeftImageHash = hashC,
                    RightImageHash = hashD
                }
            };
            IList<ImageCorrespondence> pinned = ImageCorrespondenceService.Match(left, right, pinCD);
            Console.WriteLine("pinned correspondences=" + pinned.Count);
            foreach (var c in pinned)
            {
                string ln = c.Left != null ? Tag(c.Left) : "-";
                string rn = c.Right != null ? Tag(c.Right) : "-";
                Console.WriteLine("  pin " + ln + " <-> " + rn
                    + " paired=" + c.IsPaired
                    + " ratio=" + c.DiffRatio.ToString("0.####"));
            }

            var forceCD = pinned.FirstOrDefault(c =>
                c.IsPaired && Tag(c.Left) == "C" && Tag(c.Right) == "D");
            if (forceCD == null)
            {
                Console.WriteLine("FAIL pin did not force Pair(C-D)");
                fail++;
            }
            else if (forceCD.DiffRatio != 0.0)
            {
                Console.WriteLine("FAIL pin pair DiffRatio should be 0 got " + forceCD.DiffRatio);
                fail++;
            }
            else
            {
                Console.WriteLine("OK force pin Pair(C-D) cost=0");
            }

            // ピン済みは自由集合から除外 → C only / D only が出ない
            bool leftoverC = pinned.Any(c => c.IsLeftOnly && Tag(c.Left) == "C");
            bool leftoverD = pinned.Any(c => c.IsRightOnly && Tag(c.Right) == "D");
            if (leftoverC || leftoverD)
            {
                Console.WriteLine("FAIL pin excluded poorly: leftoverC=" + leftoverC + " leftoverD=" + leftoverD);
                fail++;
            }
            else
            {
                Console.WriteLine("OK pin excludes C/D from free set");
            }

            // Map にピンが反映されること（不自然ペア C row30 ↔ D row40）
            SheetAlignment al = SheetAlignmentBuilder.Build("S", "S", null, null, pinned);
            if (al == null || al.Images == null || !al.Images.Any(c =>
                c.IsPaired && Tag(c.Left) == "C" && Tag(c.Right) == "D"))
            {
                Console.WriteLine("FAIL SheetAlignment missing pin pair C-D");
                fail++;
            }
            else
            {
                Console.WriteLine("OK SheetAlignment carries pin pair C-D");
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

        if (fail == 0)
        {
            Console.WriteLine("IMAGE_MATCH_SMOKE_PASS");
            return 0;
        }

        Console.WriteLine("IMAGE_MATCH_SMOKE_FAIL count=" + fail);
        return 1;
    }

    static EmbeddedImage Img(string tag, string path, string hash, int row, int w, int h)
    {
        return new EmbeddedImage
        {
            FileName = tag + ".png",
            ExtractedPath = path,
            ContentHash = hash,
            AnchorRow = row,
            AnchorColumn = 1,
            PixelWidth = w,
            PixelHeight = h,
            Anchor = new AnchorRect
            {
                RowStart = row,
                RowEnd = row,
                ColStart = 1,
                ColEnd = 1
            }
        };
    }

    static string Tag(EmbeddedImage img)
    {
        if (img == null || string.IsNullOrEmpty(img.FileName))
        {
            return "?";
        }

        return Path.GetFileNameWithoutExtension(img.FileName);
    }

    static void LogRatio(string name, string a, string b)
    {
        double? r = ImageDiffService.TryGetDiffRatio(a, b);
        Console.WriteLine("ratio " + name + "=" + (r.HasValue ? r.Value.ToString("0.####") : "null"));
    }

    static string FileHash(string path)
    {
        using (var fs = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(fs);
            return BitConverter.ToString(h).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    static void WriteSolid(string path, int w, int h, byte b, byte g, byte r)
    {
        using (var mat = new Mat(h, w, MatType.CV_8UC3, new Scalar(b, g, r)))
        {
            Cv2.ImWrite(path, mat);
        }
    }

    static void WriteRevisedPair(string leftPath, string rightPath, int w, int h)
    {
        using (var left = new Mat(h, w, MatType.CV_8UC3, new Scalar(180, 40, 40)))
        using (var right = left.Clone())
        {
            // 左: 緑矩形 左寄り
            Cv2.Rectangle(left, new Rect(8, 10, 24, 20), new Scalar(40, 200, 40), -1);
            // 右: 緑矩形 右寄り + 小さな黄点（改訂）
            Cv2.Rectangle(right, new Rect(w - 32, 10, 24, 20), new Scalar(40, 200, 40), -1);
            Cv2.Circle(right, new Point(w / 2, h - 12), 4, new Scalar(0, 220, 220), -1);
            Cv2.ImWrite(leftPath, left);
            Cv2.ImWrite(rightPath, right);
        }
    }

    static void WriteStripes(string path, int w, int h, byte b1, byte g1, byte r1, byte b2, byte g2, byte r2)
    {
        using (var mat = new Mat(h, w, MatType.CV_8UC3))
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool band = ((x / 6) % 2) == 0;
                    mat.Set(y, x, band
                        ? new Vec3b(b1, g1, r1)
                        : new Vec3b(b2, g2, r2));
                }
            }

            Cv2.ImWrite(path, mat);
        }
    }

    static void WriteChecker(string path, int w, int h, byte b1, byte g1, byte r1, byte b2, byte g2, byte r2)
    {
        using (var mat = new Mat(h, w, MatType.CV_8UC3))
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool cell = (((x / 8) + (y / 8)) % 2) == 0;
                    mat.Set(y, x, cell
                        ? new Vec3b(b1, g1, r1)
                        : new Vec3b(b2, g2, r2));
                }
            }

            Cv2.ImWrite(path, mat);
        }
    }
}
