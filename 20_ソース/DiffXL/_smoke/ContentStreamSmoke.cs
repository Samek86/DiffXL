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

        // Case8: 行番号が違っても近い検証メモ同士は Match（B17↔B19 相当）
        {
            var leftSheet = new SheetContent
            {
                Name = "SC",
                LooseCells = new List<CellContent>
                {
                    Cell(1, 1, "SC_テキスト挿入"),
                    Cell(17, 2, "検証: L10(S03)→R12 / R8 挿入区間は左ホールド(≤7)。")
                }
            };
            var rightSheet = new SheetContent
            {
                Name = "SC",
                LooseCells = new List<CellContent>
                {
                    Cell(1, 1, "SC_テキスト挿入"),
                    Cell(19, 2, "検証: 挿入行(R8-9)スクロール中は左が S02 付近でホールド。")
                }
            };
            double noteSim = ContentStreamBuilder.BlockSimilarity(
                ContentStreamBuilder.Build(leftSheet).First(b => b.Row == 17),
                ContentStreamBuilder.Build(rightSheet).First(b => b.Row == 19));
            Expect(noteSim >= ContentStreamBuilder.MatchThreshold, "verify-note rows similar across row numbers");
            IList<ContentStreamPair> notePairs = ContentStreamBuilder.Align(
                ContentStreamBuilder.Build(leftSheet),
                ContentStreamBuilder.Build(rightSheet));
            Expect(notePairs.Count == 2 && notePairs.All(p => p.Op == AlignOp.Match), "verify-notes paired without gaps");
            Expect(
                notePairs.Any(p => p.Op == AlignOp.Match
                    && p.Left != null && p.Right != null
                    && p.Left.Row == 17 && p.Right.Row == 19),
                "B17-style paired with B19-style");
        }

        // Case7: 見た目が似ているがハッシュ違いの画像はストリーム上で Match（S_ImgPartial 相当）
        {
            string rootImg = FindRepoRoot();
            string leftPartial = Path.Combine(rootImg, "30_参考資料", "samples", "content_diff_left.xlsx");
            string rightPartial = Path.Combine(rootImg, "30_参考資料", "samples", "content_diff_right.xlsx");
            if (File.Exists(leftPartial) && File.Exists(rightPartial))
            {
                DiffResult pr = new DiffEngine().Compare(leftPartial, rightPartial);
                SheetContent pls = pr != null && pr.LeftContent != null
                    ? pr.LeftContent.Sheets.FirstOrDefault(s => s.Name == "S_ImgPartial")
                    : null;
                SheetContent prs = pr != null && pr.RightContent != null
                    ? pr.RightContent.Sheets.FirstOrDefault(s => s.Name == "S_ImgPartial")
                    : null;
                if (pls != null && prs != null
                    && pls.Images != null && pls.Images.Count > 0
                    && prs.Images != null && prs.Images.Count > 0)
                {
                    double imgSim = ContentStreamBuilder.BlockSimilarity(
                        ContentStreamBuilder.Build(pls).First(b => b.Kind == ContentBlockKind.Image),
                        ContentStreamBuilder.Build(prs).First(b => b.Kind == ContentBlockKind.Image));
                    Expect(imgSim >= ContentStreamBuilder.MatchThreshold, "S_ImgPartial visual image matchable");
                    IList<ContentStreamPair> imgPairs = ContentStreamBuilder.Align(
                        ContentStreamBuilder.Build(pls),
                        ContentStreamBuilder.Build(prs));
                    Expect(
                        imgPairs.Any(p => p.Op == AlignOp.Match
                            && p.Left != null && p.Right != null
                            && p.Left.Kind == ContentBlockKind.Image
                            && p.Right.Kind == ContentBlockKind.Image),
                        "S_ImgPartial images paired in stream");
                    Expect(
                        !imgPairs.Any(p =>
                            (p.Left != null && p.Left.Kind == ContentBlockKind.Image && p.Op != AlignOp.Match)
                            || (p.Right != null && p.Right.Kind == ContentBlockKind.Image && p.Op != AlignOp.Match)),
                        "S_ImgPartial no image-only gaps");
                }
                else
                {
                    Console.WriteLine("WARN S_ImgPartial sheet/images missing");
                }
            }
            else
            {
                Console.WriteLine("WARN content_diff samples missing for image case");
            }
        }

        // Case5: テーブル行の一部変更でもストリーム上でテーブル同士を Match する
        // （売上サマリ相当: ヘッダ+6行中 3 行完全一致 → Jaccard≈0.33、UI 閾値 0.55 だと落ちていた）
        {
            TableBlock leftT = SalesTable(
                "T0",
                new[]
                {
                    "No\t年月\t部門\t数量\t売上\t担当\t備考",
                    "1\t2026-01\t東日本\t100\t3000\t佐藤\tOK",
                    "2\t2026-02\t西日本\t90\t2700\t鈴木\t",
                    "3\t2026-03\t中部\t110\t3300\t田中\t計画",
                    "4\t2026-04\t東日本\t95\t2850\t佐藤\t",
                    "5\t2026-05\t西日本\t120\t3600\t鈴木\t大型"
                });
            TableBlock rightT = SalesTable(
                "T0",
                new[]
                {
                    "No\t年月\t部門\t数量\t売上\t担当\t備考",
                    "1\t2026-01\t東日本\t100\t3000\t佐藤\tOK",
                    "2\t2026-02\t西日本\t98\t2950\t鈴木\t修正",
                    "3\t2026-03\t中部\t110\t3300\t田中\t計画",
                    "4\t2026-04\t東日本\t95\t3000\t高橋\t",
                    "5\t2026-05\t西日本\t125\t3750\t鈴木\t大型増"
                });

            var leftSheet = new SheetContent
            {
                Name = "売上サマリ",
                Tables = new List<TableBlock> { leftT },
                LooseCells = new List<CellContent>
                {
                    Cell(1, 1, "月次売上（テキスト差分・MiniMap 用）"),
                    Cell(10, 1, "ANCHOR_SALES"),
                    Cell(10, 2, "共通アンカー")
                }
            };
            var rightSheet = new SheetContent
            {
                Name = "売上サマリ",
                Tables = new List<TableBlock> { rightT },
                LooseCells = new List<CellContent>
                {
                    Cell(1, 1, "月次売上（テキスト差分・MiniMap 用）"),
                    Cell(10, 1, "ANCHOR_SALES"),
                    Cell(10, 2, "共通アンカー（右メモあり）"),
                    Cell(10, 3, "right note")
                }
            };

            double tableSim = ContentStreamBuilder.BlockSimilarity(
                ContentStreamBuilder.Build(leftSheet).First(b => b.Kind == ContentBlockKind.Table),
                ContentStreamBuilder.Build(rightSheet).First(b => b.Kind == ContentBlockKind.Table));
            Expect(tableSim >= ContentStreamBuilder.MatchThreshold, "partial table change is matchable");

            double row10Sim = ContentStreamBuilder.BlockSimilarity(
                ContentStreamBuilder.Build(leftSheet).First(b => b.Kind == ContentBlockKind.LooseRow && b.Row == 10),
                ContentStreamBuilder.Build(rightSheet).First(b => b.Kind == ContentBlockKind.LooseRow && b.Row == 10));
            Expect(row10Sim >= ContentStreamBuilder.MatchThreshold, "loose row with extra cell is matchable");

            IList<ContentStreamPair> salesPairs = ContentStreamBuilder.Align(
                ContentStreamBuilder.Build(leftSheet),
                ContentStreamBuilder.Build(rightSheet));
            Expect(salesPairs.Count == 3, "sales stream pairs count 3");
            Expect(salesPairs.All(p => p.Op == AlignOp.Match), "sales all blocks Match (no false gaps)");
            Expect(
                salesPairs[1].Left != null
                && salesPairs[1].Right != null
                && salesPairs[1].Left.Kind == ContentBlockKind.Table
                && salesPairs[1].Right.Kind == ContentBlockKind.Table,
                "sales tables paired at stream index 1");
            Expect(
                salesPairs[2].Left != null
                && salesPairs[2].Right != null
                && salesPairs[2].Left.Kind == ContentBlockKind.LooseRow
                && salesPairs[2].Right.Kind == ContentBlockKind.LooseRow
                && salesPairs[2].Left.Row == 10
                && salesPairs[2].Right.Row == 10,
                "sales loose row10 paired");
        }

        // Case6: 行 ID は同じで状態列だけ違うテーブル（製品カタログ相当）も Match
        {
            TableBlock leftT = CatalogTable(
                "T0",
                new[]
                {
                    new[] { "", "画像ID", "説明", "状態", "プレビュー" },
                    new[] { "", "BIG-A", "FHD 同一 PNG", "同一", "" },
                    new[] { "", "BIG-B", "QHD PNG", "基準", "" },
                    new[] { "", "BIG-C", "4K 左のみ", "左のみ", "" },
                    new[] { "", "BIG-D", "4K 右のみ", "—", "" }
                });
            TableBlock rightT = CatalogTable(
                "T0",
                new[]
                {
                    new[] { "", "画像ID", "説明", "状態", "プレビュー" },
                    new[] { "", "BIG-A", "FHD 同一 PNG", "同一", "" },
                    new[] { "", "BIG-B", "QHD PNG", "内容差分", "" },
                    new[] { "", "BIG-C", "4K 左のみ", "—", "" },
                    new[] { "", "BIG-D", "4K 右のみ", "右のみ", "" }
                });
            var leftSheet = new SheetContent
            {
                Name = "製品カタログ",
                Tables = new List<TableBlock> { leftT },
                LooseCells = new List<CellContent> { Cell(1, 2, "製品カタログ（大画像比較）") }
            };
            var rightSheet = new SheetContent
            {
                Name = "製品カタログ",
                Tables = new List<TableBlock> { rightT },
                LooseCells = new List<CellContent> { Cell(1, 2, "製品カタログ（大画像比較）") }
            };
            double catSim = ContentStreamBuilder.BlockSimilarity(
                ContentStreamBuilder.Build(leftSheet).First(b => b.Kind == ContentBlockKind.Table),
                ContentStreamBuilder.Build(rightSheet).First(b => b.Kind == ContentBlockKind.Table));
            Expect(catSim >= ContentStreamBuilder.MatchThreshold, "catalog soft table is matchable");
            IList<ContentStreamPair> catPairs = ContentStreamBuilder.Align(
                ContentStreamBuilder.Build(leftSheet),
                ContentStreamBuilder.Build(rightSheet));
            Expect(catPairs.All(p => p.Op == AlignOp.Match), "catalog stream all Match");
            Expect(
                catPairs.Any(p => p.Op == AlignOp.Match
                    && p.Left != null && p.Right != null
                    && p.Left.Kind == ContentBlockKind.Table),
                "catalog tables paired");
        }

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

        // Case: 長大テーブルを行展開 → ヘッダ + 行数 のペアになる
        {
            const int n = 200;
            var leftRows = new List<IList<CellContent>>();
            var rightRows = new List<IList<CellContent>>();
            for (int i = 0; i < n; i++)
            {
                string v = "R" + i;
                // 右は 50 行目だけ差分
                string rv = i == 50 ? "R50x" : v;
                leftRows.Add(new List<CellContent> { Cell(i + 1, 1, v) });
                rightRows.Add(new List<CellContent> { Cell(i + 1, 1, rv) });
            }

            var leftLong = new SheetContent
            {
                Name = "Long",
                Tables = new List<TableBlock>
                {
                    new TableBlock
                    {
                        Id = "Tlong",
                        RowStart = 1,
                        RowEnd = n,
                        ColStart = 1,
                        ColEnd = 1,
                        Rows = leftRows
                    }
                }
            };
            var rightLong = new SheetContent
            {
                Name = "Long",
                Tables = new List<TableBlock>
                {
                    new TableBlock
                    {
                        Id = "Tlong",
                        RowStart = 1,
                        RowEnd = n,
                        ColStart = 1,
                        ColEnd = 1,
                        Rows = rightRows
                    }
                }
            };

            ContentStreamLayout layout = ContentStreamBuilder.BuildLayout(leftLong, rightLong);
            Expect(layout != null, "layout not null");
            // 差分行が Match なら n+1、SkipLeft+SkipRight なら n+2
            Expect(layout.Count >= n + 1 && layout.Count <= n + 2,
                "expand header+rows count=" + layout.Count);
            Expect(layout.Pairs[0].Left != null && layout.Pairs[0].Left.Kind == ContentBlockKind.TableHeader,
                "first is TableHeader");
            Expect(layout.Pairs[1].Left != null && layout.Pairs[1].Left.Kind == ContentBlockKind.TableRow,
                "second is TableRow");
            int tableRowPairs = 0;
            for (int i = 0; i < layout.Count; i++)
            {
                ContentStreamPair p = layout.Pairs[i];
                if (p != null && ((p.Left != null && p.Left.Kind == ContentBlockKind.TableRow)
                    || (p.Right != null && p.Right.Kind == ContentBlockKind.TableRow)))
                {
                    tableRowPairs++;
                }
            }

            Expect(tableRowPairs >= n, "table row pairs >= " + n + " got " + tableRowPairs);
            Expect(layout.TotalHeight > n * 20, "total height scales with rows");
            Expect(layout.IndexAtOffset(0) == 0, "index at 0");
            Expect(layout.IndexAtOffset(layout.TotalHeight - 1) == layout.Count - 1, "index near end is last");

            ContentStreamLayout again = ContentStreamBuilder.GetOrBuildLayout(leftLong, rightLong);
            Expect(ReferenceEquals(layout, again) == false || again.Count == layout.Count,
                "GetOrBuildLayout returns usable layout");
            // キャッシュ: 同一参照で再取得
            ContentStreamLayout cached = ContentStreamBuilder.GetOrBuildLayout(leftLong, rightLong);
            Expect(ReferenceEquals(again, cached), "layout cache hit");
            ContentStreamBuilder.ClearLayoutCache();
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

    /// <summary>
    /// タブ区切り行テキストから TableBlock を作る（行3開始）。
    /// </summary>
    private static TableBlock SalesTable(string id, string[] rowTexts)
    {
        var rows = new List<IList<CellContent>>();
        for (int i = 0; i < rowTexts.Length; i++)
        {
            string[] parts = rowTexts[i].Split('\t');
            var cells = new List<CellContent>();
            for (int c = 0; c < parts.Length; c++)
            {
                cells.Add(Cell(3 + i, c + 1, parts[c]));
            }

            rows.Add(cells);
        }

        return new TableBlock
        {
            Id = id,
            RowStart = 3,
            RowEnd = 3 + rowTexts.Length - 1,
            ColStart = 1,
            ColEnd = 7,
            Rows = rows
        };
    }

    private static TableBlock CatalogTable(string id, string[][] rowParts)
    {
        var rows = new List<IList<CellContent>>();
        for (int i = 0; i < rowParts.Length; i++)
        {
            var cells = new List<CellContent>();
            for (int c = 0; c < rowParts[i].Length; c++)
            {
                cells.Add(Cell(3 + i, c + 1, rowParts[i][c]));
            }

            rows.Add(cells);
        }

        return new TableBlock
        {
            Id = id,
            RowStart = 3,
            RowEnd = 3 + rowParts.Length - 1,
            ColStart = 1,
            ColEnd = 5,
            Rows = rows
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
