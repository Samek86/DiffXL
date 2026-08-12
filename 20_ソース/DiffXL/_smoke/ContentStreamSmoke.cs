// Content stream: document-order blocks + align (no tabs)
// Compile against built DiffXL (see other smokes) or run via msbuild + csc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DiffXL.LOGIC.Diff;

internal static class ContentStreamSmoke
{
    private static int _fails;

    private static void Expect(bool cond, string name)
    {
        if (cond)
        {
            Console.WriteLine("OK " + name);
        }
        else
        {
            Console.WriteLine("FAIL " + name);
            _fails++;
        }
    }

    private static void Main()
    {
        Console.WriteLine("ContentStreamSmoke");

        // Case1: table then image order by row
        var sheet = new SheetContent
        {
            Name = "S",
            Tables = new List<TableBlock>
            {
                new TableBlock
                {
                    Id = "T0",
                    RowStart = 10,
                    RowEnd = 12,
                    ColStart = 1,
                    ColEnd = 2,
                    Rows = new List<IList<CellContent>>
                    {
                        new List<CellContent> { Cell(10, 1, "A"), Cell(10, 2, "B") }
                    }
                }
            },
            Images = new List<EmbeddedImage>
            {
                new EmbeddedImage
                {
                    FileName = "img.png",
                    ContentHash = "abc",
                    AnchorRow = 2,
                    AnchorColumn = 1,
                    Anchor = new AnchorRect { RowStart = 2, RowEnd = 4, ColStart = 1, ColEnd = 3 }
                }
            },
            LooseCells = new List<CellContent>
            {
                Cell(1, 1, "Hello"),
                Cell(20, 1, "Tail")
            },
            Shapes = new List<ShapeContent>
            {
                new ShapeContent
                {
                    Id = "sh1",
                    Kind = "rect",
                    Text = "Note",
                    ContentHash = "s1",
                    OrderIndex = 0,
                    Anchor = new AnchorRect { RowStart = 15, RowEnd = 16, ColStart = 1, ColEnd = 2 }
                }
            }
        };

        IList<ContentStreamBlock> blocks = ContentStreamBuilder.Build(sheet);
        // Hello r1, image r2, table r10, shape r15, Tail r20 → 5
        Expect(blocks.Count == 5, "build count 5");
        Expect(blocks[0].Kind == ContentBlockKind.LooseRow && blocks[0].Row == 1, "order first Hello row1");
        Expect(blocks[1].Kind == ContentBlockKind.Image && blocks[1].Row == 2, "order image row2");
        Expect(blocks[2].Kind == ContentBlockKind.Table && blocks[2].Row == 10, "order table row10");
        Expect(blocks[3].Kind == ContentBlockKind.Shape && blocks[3].Row == 15, "order shape row15");
        Expect(blocks[4].Kind == ContentBlockKind.LooseRow && blocks[4].Row == 20, "order tail row20");

        // Case2: left/right align with extra image on right after table-equivalent position
        var left = new SheetContent
        {
            Name = "L",
            Images = new List<EmbeddedImage>
            {
                Img("h0", 1), Img("h1", 2), Img("h2", 3), Img("h3", 4),
                Img("h4", 5), Img("h5", 6), Img("h6", 7), Img("h7", 8)
            }
        };
        var right = new SheetContent
        {
            Name = "R",
            Images = new List<EmbeddedImage>
            {
                Img("h0", 1), Img("h1", 2), Img("h2", 3), Img("h3", 4),
                Img("insert", 45),
                Img("h4", 5), Img("h5", 6), Img("h6", 7), Img("h7", 8)
            }
        };

        IList<ContentStreamPair> pairs = ContentStreamBuilder.Align(
            ContentStreamBuilder.Build(left),
            ContentStreamBuilder.Build(right));
        int skipRight = pairs.Count(p => p.Op == AlignOp.SkipRight);
        int match = pairs.Count(p => p.Op == AlignOp.Match);
        Expect(pairs.Count == 9, "8vs9 pairs count 9");
        Expect(skipRight == 1, "8vs9 one SkipRight");
        Expect(match == 8, "8vs9 eight Match");

        // Case3: nearest OrderHint
        int idx = ContentStreamBuilder.FindNearestPairIndex(pairs, 5 * 1000.0);
        Expect(idx >= 0, "nearest index found");

        // Case4: real sample if present
        string root = FindRepoRoot();
        string leftX = Path.Combine(root, "30_参考資料", "samples", "content_diff_left.xlsx");
        string rightX = Path.Combine(root, "30_参考資料", "samples", "content_diff_right.xlsx");
        if (File.Exists(leftX) && File.Exists(rightX))
        {
            var engine = new DiffEngine();
            DiffResult result = engine.Compare(leftX, rightX);
            Expect(result != null && string.IsNullOrEmpty(result.ErrorMessage), "DiffEngine compare");
            Expect(result.LeftContent != null && result.RightContent != null, "contents set");
            SheetContent ls = result.LeftContent.Sheets.FirstOrDefault(s => s.Name == "S_Img8v9");
            SheetContent rs = result.RightContent.Sheets.FirstOrDefault(s => s.Name == "S_Img8v9");
            if (ls != null && rs != null)
            {
                IList<ContentStreamPair> p2 = ContentStreamBuilder.Align(
                    ContentStreamBuilder.Build(ls),
                    ContentStreamBuilder.Build(rs));
                Expect(p2.Count >= 8, "sample S_Img8v9 stream pairs >= 8");
                Expect(p2.Any(p => p.Op == AlignOp.SkipRight || p.Op == AlignOp.SkipLeft), "sample has skip");
            }
            else
            {
                Console.WriteLine("WARN sample sheets S_Img8v9 missing");
            }
        }
        else
        {
            Console.WriteLine("WARN content_diff samples not found, skip engine case");
        }

        Console.WriteLine(_fails == 0 ? "PASS ContentStreamSmoke" : "FAIL ContentStreamSmoke fails=" + _fails);
        Environment.Exit(_fails == 0 ? 0 : 1);
    }

    private static CellContent Cell(int row, int col, string text)
    {
        return new CellContent
        {
            Row = row,
            Column = col,
            Address = ((char)('A' + col - 1)).ToString() + row,
            Text = text
        };
    }

    private static EmbeddedImage Img(string hash, int row)
    {
        return new EmbeddedImage
        {
            ContentHash = hash,
            FileName = hash + ".png",
            AnchorRow = row,
            AnchorColumn = 1,
            Anchor = new AnchorRect { RowStart = row, RowEnd = row, ColStart = 1, ColEnd = 2 }
        };
    }

    private static string FindRepoRoot()
    {
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "30_参考資料")))
            {
                return dir;
            }

            string parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
            {
                break;
            }

            dir = parent;
        }

        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
