using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 2 つの .xlsx を内容ベースで比較し DiffResult を返す。
    /// Excel COM は不要。セル多重集合・表行 LCS・画像系列 DP・図形系列を統括する。
    /// </summary>
    public sealed class DiffEngine
    {
        /// <summary>
        /// 左右ファイルを比較する。
        /// </summary>
        /// <param name="leftPath">左 xlsx</param>
        /// <param name="rightPath">右 xlsx</param>
        /// <param name="options">オプション（null 可）</param>
        /// <param name="progress">進捗（null 可）</param>
        /// <returns>比較結果（LeftContent / RightContent 付き）</returns>
        public DiffResult Compare(
            string leftPath,
            string rightPath,
            CompareOptions options = null,
            IProgress<string> progress = null)
        {
            var sw = Stopwatch.StartNew();
            var result = new DiffResult
            {
                LeftPath = leftPath,
                RightPath = rightPath
            };

            try
            {
                ValidateXlsx(leftPath, "左");
                ValidateXlsx(rightPath, "右");
                Report(progress, "キャッシュを準備しています...");

                // 大画像比較の連打で AppData\cache が肥大化しないよう整理
                try
                {
                    int purged = AppPaths.PurgeCompareCache(
                        keepNewest: 2,
                        maxAge: TimeSpan.FromMinutes(20),
                        maxTotalBytes: 400L * 1024 * 1024);
                    if (purged > 0)
                    {
                        Log.Info("比較キャッシュ整理: 削除 " + purged + " 件");
                    }
                }
                catch (Exception purgeEx)
                {
                    Log.Debug("cache purge warn: " + purgeEx.Message);
                }

                string compareId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                    + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string cacheRoot = Path.Combine(AppPaths.CacheDir, compareId);
                string leftMediaDir = Path.Combine(cacheRoot, "media", "left");
                string rightMediaDir = Path.Combine(cacheRoot, "media", "right");
                string maskDir = Path.Combine(cacheRoot, "masks");
                Directory.CreateDirectory(leftMediaDir);
                Directory.CreateDirectory(rightMediaDir);
                Directory.CreateDirectory(maskDir);
                result.CacheDirectory = cacheRoot;

                // 旧 ContentScrollMap / Excel 向け Alignment は生成しない（空のまま）
                result.ScrollMaps = new ContentScrollMapSet();
                result.Alignments = new List<SheetAlignment>();

                Report(progress, "ブックを読み込んでいます...");
                using (XlsxPackageReader leftReader = XlsxPackageReader.Open(leftPath))
                using (XlsxPackageReader rightReader = XlsxPackageReader.Open(rightPath))
                {
                    IReadOnlyList<string> leftSheets = leftReader.GetSheetNames();
                    IReadOnlyList<string> rightSheets = rightReader.GetSheetNames();
                    SheetMatchResult match = SheetMatcher.Match(
                        leftSheets.ToList(),
                        rightSheets.ToList(),
                        options != null ? options.ManualSheetPairs : null);
                    result.SheetPairs = match.Pairs;

                    // 片側のみシート → Structure
                    foreach (string name in match.LeftOnlySheets)
                    {
                        result.Items.Add(new DiffItem
                        {
                            Kind = DiffKind.Structure,
                            SheetLeft = name,
                            Summary = "左のみのシート: " + name,
                            OrderHint = 0
                        });
                    }

                    foreach (string name in match.RightOnlySheets)
                    {
                        result.Items.Add(new DiffItem
                        {
                            Kind = DiffKind.Structure,
                            SheetRight = name,
                            Summary = "右のみのシート: " + name,
                            OrderHint = 0
                        });
                    }

                    // 全シートの SheetContent を構築
                    Report(progress, "内容モデルを構築しています...");
                    WorkbookContent leftContent = BuildWorkbookContent(
                        leftReader, leftPath, leftMediaDir);
                    WorkbookContent rightContent = BuildWorkbookContent(
                        rightReader, rightPath, rightMediaDir);
                    result.LeftContent = leftContent;
                    result.RightContent = rightContent;

                    Dictionary<string, SheetContent> leftByName = IndexSheets(leftContent);
                    Dictionary<string, SheetContent> rightByName = IndexSheets(rightContent);

                    int pairIndex = 0;
                    foreach (SheetPair pair in match.Pairs)
                    {
                        pairIndex++;
                        Report(progress, string.Format(
                            CultureInfo.InvariantCulture,
                            "シート比較中 ({0}/{1}): {2} ↔ {3}",
                            pairIndex,
                            match.Pairs.Count,
                            pair.LeftSheet,
                            pair.RightSheet));

                        SheetContent leftSheet = FindSheet(leftByName, pair.LeftSheet);
                        SheetContent rightSheet = FindSheet(rightByName, pair.RightSheet);
                        if (leftSheet == null)
                        {
                            leftSheet = new SheetContent { Name = pair.LeftSheet };
                        }

                        if (rightSheet == null)
                        {
                            rightSheet = new SheetContent { Name = pair.RightSheet };
                        }

                        double baseHint = pairIndex * 1000.0;

                        // 1) テーブル外セル（多重集合・位置無視）
                        foreach (DiffItem item in CellBagComparer.Compare(
                            leftSheet.LooseCells, rightSheet.LooseCells, pair))
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            item.OrderHint = baseHint + Math.Max(0, item.OrderHint);
                            result.Items.Add(item);
                        }

                        // 2) テーブル系列＋行 LCS
                        foreach (DiffItem item in TableCompareService.Compare(
                            leftSheet.Tables, rightSheet.Tables, pair))
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            item.OrderHint = baseHint + 200 + Math.Max(0, item.OrderHint);
                            result.Items.Add(item);
                        }

                        // 3) 画像出現順 DP + 視覚差分
                        AddImageDiffItems(
                            leftSheet.Images,
                            rightSheet.Images,
                            pair,
                            maskDir,
                            result.Items,
                            pairIndex);

                        // 4) 図形系列
                        foreach (DiffItem item in ShapeCompareService.Compare(
                            leftSheet.Shapes, rightSheet.Shapes, pair))
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            item.OrderHint = baseHint + 700 + Math.Max(0, item.OrderHint);
                            result.Items.Add(item);
                        }
                    }
                }

                // 安定した並び
                result.Items = result.Items
                    .OrderBy(i => i.OrderHint)
                    .ThenBy(i => i.Kind)
                    .ThenBy(i => i.AddressLeft ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 展開済みストリームへ付着し、同一 pair の片側 Text を 1 件にまとめる
                DiffResultLinker.AttachExpandedLayouts(result);
                DiffResultLinker.MergeOneSidedTextsOnSamePair(result);

                sw.Stop();
                result.Elapsed = sw.Elapsed;
                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "比較完了: 差分 {0} 件 / {1} ms / cache={2}",
                    result.Items.Count,
                    sw.ElapsedMilliseconds,
                    result.CacheDirectory));
                Report(progress, "比較完了");
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.Elapsed = sw.Elapsed;
                result.ErrorMessage = ex.Message;
                Log.Exception(ex);
                Report(progress, "比較失敗: " + ex.Message);
                return result;
            }
        }

        /// <summary>
        /// Reader から全シートの SheetContent を構築する。
        /// EnumerateCellContents → TableDetector、画像・図形は出現順（行→列）でソート。
        /// </summary>
        private static WorkbookContent BuildWorkbookContent(
            XlsxPackageReader reader,
            string path,
            string mediaDir)
        {
            var wb = new WorkbookContent
            {
                Path = path,
                Sheets = new List<SheetContent>()
            };

            if (reader == null)
            {
                return wb;
            }

            Directory.CreateDirectory(mediaDir);

            // 画像は一度抽出しシートごとに振り分ける
            List<EmbeddedImage> allImages;
            try
            {
                allImages = reader.ExtractImages(null, mediaDir).ToList();
            }
            catch (Exception ex)
            {
                Log.Debug("ExtractImages 失敗: " + ex.Message);
                allImages = new List<EmbeddedImage>();
            }

            IReadOnlyList<string> sheetNames = reader.GetSheetNames();
            for (int si = 0; si < sheetNames.Count; si++)
            {
                string sheetName = sheetNames[si];
                List<CellContent> cells;
                try
                {
                    cells = reader.EnumerateCellContents(sheetName).ToList();
                }
                catch (Exception ex)
                {
                    Log.Debug("EnumerateCellContents 失敗 [" + sheetName + "]: " + ex.Message);
                    cells = new List<CellContent>();
                }

                IList<string> definedRefs = null;
                try
                {
                    definedRefs = reader.GetDefinedTableRefs(sheetName);
                }
                catch (Exception ex)
                {
                    Log.Debug("GetDefinedTableRefs 失敗 [" + sheetName + "]: " + ex.Message);
                    definedRefs = null;
                }

                TableDetectResult detect = TableDetector.Detect(cells, definedRefs);

                List<EmbeddedImage> sheetImages = allImages
                    .Where(i => i != null
                        && string.Equals(i.SheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i.AnchorRow > 0 ? i.AnchorRow : int.MaxValue)
                    .ThenBy(i => i.AnchorColumn > 0 ? i.AnchorColumn : int.MaxValue)
                    .ThenBy(i => i.FileName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // シート紐付けが無い画像は先頭シートへ（ブック単位フォールバック）
                if (si == 0)
                {
                    List<EmbeddedImage> unmapped = allImages
                        .Where(i => i != null && string.IsNullOrEmpty(i.SheetName))
                        .OrderBy(i => i.FileName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (unmapped.Count > 0)
                    {
                        sheetImages.AddRange(unmapped);
                    }
                }

                IList<ShapeContent> shapes;
                try
                {
                    shapes = reader.ExtractShapes(sheetName, mediaDir);
                }
                catch (Exception ex)
                {
                    Log.Debug("ExtractShapes 失敗 [" + sheetName + "]: " + ex.Message);
                    shapes = new List<ShapeContent>();
                }

                wb.Sheets.Add(new SheetContent
                {
                    Name = sheetName,
                    LooseCells = detect.LooseCells ?? new List<CellContent>(),
                    Tables = detect.Tables ?? new List<TableBlock>(),
                    Images = sheetImages,
                    Shapes = shapes != null ? shapes.ToList() : new List<ShapeContent>()
                });
            }

            return wb;
        }

        /// <summary>
        /// シート名 → SheetContent の辞書（大文字小文字無視）。
        /// </summary>
        private static Dictionary<string, SheetContent> IndexSheets(WorkbookContent wb)
        {
            var map = new Dictionary<string, SheetContent>(StringComparer.OrdinalIgnoreCase);
            if (wb == null || wb.Sheets == null)
            {
                return map;
            }

            foreach (SheetContent s in wb.Sheets)
            {
                if (s == null || string.IsNullOrEmpty(s.Name))
                {
                    continue;
                }

                if (!map.ContainsKey(s.Name))
                {
                    map[s.Name] = s;
                }
            }

            return map;
        }

        /// <summary>
        /// 辞書からシートを取得する（無ければ null）。
        /// </summary>
        private static SheetContent FindSheet(
            Dictionary<string, SheetContent> map,
            string name)
        {
            if (map == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            SheetContent s;
            return map.TryGetValue(name, out s) ? s : null;
        }

        /// <summary>
        /// 画像系列をアラインし、Match は視覚比較、Skip は ImageOnly* として items に追加する。
        /// </summary>
        private static void AddImageDiffItems(
            IList<EmbeddedImage> leftImages,
            IList<EmbeddedImage> rightImages,
            SheetPair pair,
            string maskDir,
            List<DiffItem> items,
            int pairIndex)
        {
            if (items == null)
            {
                return;
            }

            pair = pair ?? new SheetPair();
            IList<EmbeddedImage> left = leftImages ?? Array.Empty<EmbeddedImage>();
            IList<EmbeddedImage> right = rightImages ?? Array.Empty<EmbeddedImage>();

            if (left.Count == 0 && right.Count == 0)
            {
                return;
            }

            IList<AlignStep> steps;
            try
            {
                steps = ImageSequenceAligner.Align(left, right);
            }
            catch (Exception ex)
            {
                Log.Debug("ImageSequenceAligner 失敗: " + ex.Message);
                return;
            }

            int index = 0;
            foreach (AlignStep step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                if (step.Op == AlignOp.SkipLeft)
                {
                    EmbeddedImage li = left[step.LeftIndex];
                    items.Add(new DiffItem
                    {
                        Kind = DiffKind.ImageOnlyLeft,
                        SheetLeft = pair.LeftSheet,
                        SheetRight = pair.RightSheet,
                        LeftImagePath = li != null ? li.ExtractedPath : null,
                        Summary = "左のみの画像: "
                            + (li != null ? li.FileName : "?")
                            + FormatDim(li),
                        OrderHint = pairIndex * 1000 + 500 + index
                    });
                }
                else if (step.Op == AlignOp.SkipRight)
                {
                    EmbeddedImage ri = right[step.RightIndex];
                    items.Add(new DiffItem
                    {
                        Kind = DiffKind.ImageOnlyRight,
                        SheetLeft = pair.LeftSheet,
                        SheetRight = pair.RightSheet,
                        RightImagePath = ri != null ? ri.ExtractedPath : null,
                        Summary = "右のみの画像: "
                            + (ri != null ? ri.FileName : "?")
                            + FormatDim(ri),
                        OrderHint = pairIndex * 1000 + 500 + index
                    });
                }
                else if (step.Op == AlignOp.Match)
                {
                    EmbeddedImage li = left[step.LeftIndex];
                    EmbeddedImage ri = right[step.RightIndex];
                    CompareMatchedImages(li, ri, pair, maskDir, items, pairIndex, index);
                }

                index++;
            }
        }

        /// <summary>
        /// 対応済み画像ペアを視覚比較する。ハッシュ一致はスキップ。差分があれば Regions 付き Image。
        /// </summary>
        private static void CompareMatchedImages(
            EmbeddedImage li,
            EmbeddedImage ri,
            SheetPair pair,
            string maskDir,
            List<DiffItem> items,
            int pairIndex,
            int indexHint)
        {
            if (li == null || ri == null)
            {
                return;
            }

            // ContentHash 完全一致は見た目同一
            if (!string.IsNullOrEmpty(li.ContentHash)
                && !string.IsNullOrEmpty(ri.ContentHash)
                && string.Equals(li.ContentHash, ri.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string maskName = string.Format(
                CultureInfo.InvariantCulture,
                "p{0}_img_{1}.png",
                pairIndex,
                indexHint);

            try
            {
                ImageVisualDiff visual = ImageVisualComparer.Compare(
                    li.ExtractedPath,
                    ri.ExtractedPath,
                    maskDir,
                    maskName);

                if (visual != null && visual.IsSame)
                {
                    return;
                }

                var regions = visual != null && visual.Regions != null
                    ? visual.Regions
                    : new List<HighlightRegion>();

                // 領域 0 かつ IsSame でない場合も、比較失敗として Image を出す
                items.Add(new DiffItem
                {
                    Kind = DiffKind.Image,
                    SheetLeft = pair.LeftSheet,
                    SheetRight = pair.RightSheet,
                    LeftImagePath = li.ExtractedPath,
                    RightImagePath = ri.ExtractedPath,
                    DiffMaskPath = visual != null ? visual.MaskPath : null,
                    HighlightRegions = regions,
                    Summary = string.Format(
                        CultureInfo.InvariantCulture,
                        "画像差分: {0} ↔ {1} (regions={2})",
                        li.FileName ?? "?",
                        ri.FileName ?? "?",
                        regions.Count),
                    OrderHint = pairIndex * 1000 + 500 + indexHint
                });
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                items.Add(new DiffItem
                {
                    Kind = DiffKind.Image,
                    SheetLeft = pair.LeftSheet,
                    SheetRight = pair.RightSheet,
                    LeftImagePath = li.ExtractedPath,
                    RightImagePath = ri.ExtractedPath,
                    Summary = "画像比較エラー: " + ex.Message,
                    OrderHint = pairIndex * 1000 + 500 + indexHint
                });
            }
        }

        private static string FormatDim(EmbeddedImage img)
        {
            if (img == null || img.PixelWidth <= 0 || img.PixelHeight <= 0)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                " ({0}x{1})",
                img.PixelWidth,
                img.PixelHeight);
        }

        /// <summary>
        /// xlsx パスを検証する。
        /// </summary>
        private static void ValidateXlsx(string path, string side)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(side + "ファイルが未選択です。");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(side + "ファイルが見つかりません。", path);
            }

            if (!string.Equals(Path.GetExtension(path), Common.ExcelExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(side + "ファイルは .xlsx のみ対応です。");
            }
        }

        /// <summary>
        /// 進捗通知。
        /// </summary>
        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null)
            {
                progress.Report(message);
            }

            Log.Debug(message);
        }
    }
}
