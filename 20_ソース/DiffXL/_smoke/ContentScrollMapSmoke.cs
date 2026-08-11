using System;
using System.Collections.Generic;
using DiffXL.LOGIC.Diff;

/// <summary>
/// 内容スクロールマップの単体スモーク（セル LCS + 画像 correspondence）。
/// </summary>
class Program
{
    static int Main()
    {
        int fail = 0;

        // --- テキスト挿入 ---
        var leftCells = new List<CellValue>
        {
            C(5, "S01"), C(5, "Alpha"), C(6, "S02"), C(6, "Beta"), C(7, "S03"), C(7, "Gamma")
        };
        var rightCells = new List<CellValue>
        {
            C(5, "S01"), C(5, "Alpha"), C(6, "S02"), C(6, "Beta"),
            C(7, "INS"), C(7, "Inserted"), C(8, "S03"), C(8, "Gamma")
        };
        var map = ContentScrollMap.Build("S", "S", leftCells, rightCells, (IList<ImageCorrespondence>)null);
        Console.WriteLine(map.Describe());
        Expect(ref fail, map.MapLeftToRight(5), 5, "L5->R5");
        Expect(ref fail, map.MapLeftToRight(6), 6, "L6->R6");
        Expect(ref fail, map.MapLeftToRight(7), 8, "L7->R8 S03");
        Expect(ref fail, map.MapRightToLeft(7), 6, "R7 insert hold");
        Expect(ref fail, map.MapRightToLeft(8), 7, "R8->L7");

        // --- 画像: correspondence 経由（ハッシュ一致 / 片側のみ）---
        var leftImg = new List<EmbeddedImage>
        {
            Img(4, "h1", 100, 100), Img(5, "h2L", 200, 50), Img(6, "h3L", 32, 32), Img(8, "h4", 80, 80)
        };
        var rightImg = new List<EmbeddedImage>
        {
            Img(4, "h1", 100, 100), Img(5, "h2R", 200, 50), Img(7, "h3R", 40, 40), Img(8, "h4", 80, 80)
        };
        IList<ImageCorrespondence> corr = ImageCorrespondenceService.Match(leftImg, rightImg);
        SheetAlignment al = SheetAlignmentBuilder.Build("Cat", "Cat", null, null, corr);
        ContentScrollMap map2 = al.ScrollMap;
        Console.WriteLine(map2.Describe());
        Expect(ref fail, map2.MapLeftToRight(4), 4, "img L4");
        Expect(ref fail, map2.MapLeftToRight(8), 8, "img L8");
        int holdL = map2.MapRightToLeft(7);
        int holdR = map2.MapLeftToRight(6);
        Console.WriteLine("hold R7->L" + holdL + " L6->R" + holdR);
        if (holdL >= 8)
        {
            Console.WriteLine("FAIL holdL");
            fail++;
        }

        if (holdR >= 8)
        {
            Console.WriteLine("FAIL holdR");
            fail++;
        }

        // --- 回帰: 同順 modified ペアは Equal（LeftOnly に落とさない）---
        // テキストが L6↔R20 のような飛びマッチをしても画像 L8↔R8 を落とさない
        var cellsL = new List<CellValue>
        {
            C(1, "TITLE"), C(3, "ORDER_SECTION"), C(6, "SHARED_JUMP"), C(11, "TAIL")
        };
        var cellsR = new List<CellValue>
        {
            C(1, "TITLE"), C(3, "ORDER_SECTION"), C(11, "TAIL"), C(20, "SHARED_JUMP")
        };
        var corrMod = new List<ImageCorrespondence>
        {
            Pair(ImgSpan(5, 6, "ha"), ImgSpan(5, 6, "ha"), exact: true),
            Pair(ImgSpan(8, 9, "hbL"), ImgSpan(8, 9, "hbR"), exact: false),
            LeftOnly(ImgSpan(12, 12, "onlyL")),
            RightOnly(ImgSpan(12, 12, "onlyR"))
        };
        ContentScrollMap map3 = ContentScrollMap.Build("同順", "同順", cellsL, cellsR, corrMod);
        Console.WriteLine(map3.Describe());
        Expect(ref fail, map3.MapLeftToRight(5), 5, "mod-pair L5->R5");
        Expect(ref fail, map3.MapLeftToRight(8), 8, "mod-pair L8->R8 (not hold@5)");
        Expect(ref fail, map3.MapRightToLeft(8), 8, "mod-pair R8->L8");

        Console.WriteLine(fail == 0 ? "MAP_UNIT_PASS" : "MAP_UNIT_FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }

    static EmbeddedImage ImgSpan(int rowStart, int rowEnd, string hash)
    {
        return new EmbeddedImage
        {
            AnchorRow = rowStart,
            AnchorColumn = 1,
            ContentHash = hash,
            FileName = hash + ".png",
            PixelWidth = 80,
            PixelHeight = 40,
            Anchor = new AnchorRect
            {
                RowStart = rowStart,
                RowEnd = rowEnd,
                ColStart = 1,
                ColEnd = 2
            }
        };
    }

    static ImageCorrespondence Pair(EmbeddedImage left, EmbeddedImage right, bool exact)
    {
        return new ImageCorrespondence
        {
            Left = left,
            Right = right,
            IsExactHashMatch = exact,
            DiffRatio = exact ? 0 : 0.15
        };
    }

    static ImageCorrespondence LeftOnly(EmbeddedImage left)
    {
        return new ImageCorrespondence
        {
            Left = left,
            Right = null,
            IsExactHashMatch = false,
            DiffRatio = -1
        };
    }

    static ImageCorrespondence RightOnly(EmbeddedImage right)
    {
        return new ImageCorrespondence
        {
            Left = null,
            Right = right,
            IsExactHashMatch = false,
            DiffRatio = -1
        };
    }

    static CellValue C(int row, string t)
    {
        return new CellValue { Address = "A" + row, Row = row, Column = 1, Text = t };
    }

    static EmbeddedImage Img(int row, string hash, int w, int h)
    {
        return new EmbeddedImage
        {
            AnchorRow = row,
            AnchorColumn = 1,
            ContentHash = hash,
            FileName = hash + ".png",
            PixelWidth = w,
            PixelHeight = h,
            Anchor = new AnchorRect { RowStart = row, RowEnd = row, ColStart = 1, ColEnd = 1 }
        };
    }

    static void Expect(ref int fail, int actual, int expected, string name)
    {
        if (actual != expected)
        {
            Console.WriteLine("FAIL " + name + " got=" + actual + " expect=" + expected);
            fail++;
        }
        else
        {
            Console.WriteLine("OK " + name + "=" + actual);
        }
    }
}
