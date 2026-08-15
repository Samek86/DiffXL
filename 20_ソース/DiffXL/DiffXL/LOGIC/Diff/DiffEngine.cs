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
                bool lazy = options != null && options.LazySheets;
                result.IsLazy = lazy;
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

                    List<SheetPair> pairsToCompare = ResolvePairsToCompare(match.Pairs, options, lazy);
                    List<string> leftNeed = CollectSheetNames(pairsToCompare, left: true);
                    List<string> rightNeed = CollectSheetNames(pairsToCompare, left: false);

                    Report(progress, lazy
                        ? "内容モデルを構築しています（このシート）..."
                        : "内容モデルを構築しています...");
                    var swRead = Stopwatch.StartNew();
                    WorkbookContent leftContent = BuildWorkbookContent(
                        leftReader, leftPath, leftMediaDir, leftSheets, leftNeed);
                    WorkbookContent rightContent = BuildWorkbookContent(
                        rightReader, rightPath, rightMediaDir, rightSheets, rightNeed);
                    swRead.Stop();
                    result.LeftContent = leftContent;
                    result.RightContent = rightContent;
                    result.Timings.ReadMs = swRead.ElapsedMilliseconds;

                    Dictionary<string, SheetContent> leftByName = IndexSheets(leftContent);
                    Dictionary<string, SheetContent> rightByName = IndexSheets(rightContent);

                    var swTable = Stopwatch.StartNew();
                    var swImage = Stopwatch.StartNew();
                    long tableAcc = 0;
                    long imageAcc = 0;

                    int pairIndex = 0;
                    foreach (SheetPair pair in match.Pairs)
                    {
                        pairIndex++;
                        if (!ContainsPair(pairsToCompare, pair))
                        {
                            continue;
                        }

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

                        long pairTable;
                        long pairImage;
                        AppendPairContentDiffs(
                            result,
                            leftSheet,
                            rightSheet,
                            pair,
                            maskDir,
                            pairIndex,
                            out pairTable,
                            out pairImage);
                        tableAcc += pairTable;
                        imageAcc += pairImage;
                        RememberCompared(result, pair);
                    }

                    result.Timings.TableMs = tableAcc;
                    result.Timings.ImageMs = imageAcc;
                }

                // 安定した並び
                result.Items = result.Items
                    .OrderBy(i => i.OrderHint)
                    .ThenBy(i => i.Kind)
                    .ThenBy(i => i.AddressLeft ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 展開済みストリームへ付着し、同一 pair の片側 Text を 1 件にまとめる
                var swLayout = Stopwatch.StartNew();
                DiffResultLinker.AttachExpandedLayouts(result);
                DiffResultLinker.MergeOneSidedTextsOnSamePair(result);
                swLayout.Stop();
                result.Timings.LayoutMs = swLayout.ElapsedMilliseconds;

                sw.Stop();
                result.Elapsed = sw.Elapsed;
                result.Timings.TotalMs = sw.ElapsedMilliseconds;
                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "比較段階: 読込={0}ms 表={1}ms 画像={2}ms 配置={3}ms 合計={4}ms / 差分 {5} 件 / cache={6}",
                    result.Timings.ReadMs,
                    result.Timings.TableMs,
                    result.Timings.ImageMs,
                    result.Timings.LayoutMs,
                    result.Timings.TotalMs,
                    result.Items.Count,
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
        /// シートペアキー（"左\t右"）。
        /// </summary>
        public static string MakePairKey(string leftSheet, string rightSheet)
        {
            return (leftSheet ?? string.Empty) + "\t" + (rightSheet ?? string.Empty);
        }

        /// <summary>
        /// そのペアの内容比較が済んでいるか。
        /// </summary>
        public static bool IsPairCompared(DiffResult result, string leftSheet, string rightSheet)
        {
            if (result == null || result.ComparedPairKeys == null)
            {
                return false;
            }

            string key = MakePairKey(leftSheet, rightSheet);
            for (int i = 0; i < result.ComparedPairKeys.Count; i++)
            {
                if (string.Equals(result.ComparedPairKeys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 既存結果に 1 ペア分の内容比較を追加する（遅延シート切替用）。
        /// </summary>
        public void CompareSheetPair(
            DiffResult result,
            string leftPath,
            string rightPath,
            SheetPair pair,
            CompareOptions options,
            IProgress<string> progress)
        {
            if (result == null || pair == null)
            {
                return;
            }

            if (IsPairCompared(result, pair.LeftSheet, pair.RightSheet))
            {
                return;
            }

            ValidateXlsx(leftPath, "左");
            ValidateXlsx(rightPath, "右");
            Report(progress, "シートを読み込んでいます...");

            string cacheRoot = result.CacheDirectory;
            if (string.IsNullOrEmpty(cacheRoot))
            {
                cacheRoot = Path.Combine(
                    AppPaths.CacheDir,
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                    + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                result.CacheDirectory = cacheRoot;
            }

            string leftMediaDir = Path.Combine(cacheRoot, "media", "left");
            string rightMediaDir = Path.Combine(cacheRoot, "media", "right");
            string maskDir = Path.Combine(cacheRoot, "masks");
            Directory.CreateDirectory(leftMediaDir);
            Directory.CreateDirectory(rightMediaDir);
            Directory.CreateDirectory(maskDir);

            var sw = Stopwatch.StartNew();
            using (XlsxPackageReader leftReader = XlsxPackageReader.Open(leftPath))
            using (XlsxPackageReader rightReader = XlsxPackageReader.Open(rightPath))
            {
                var only = new List<SheetPair> { pair };
                List<string> leftNeed = CollectSheetNames(only, left: true);
                List<string> rightNeed = CollectSheetNames(only, left: false);
                var swRead = Stopwatch.StartNew();
                WorkbookContent leftPart = BuildWorkbookContent(
                    leftReader, leftPath, leftMediaDir, leftReader.GetSheetNames(), leftNeed);
                WorkbookContent rightPart = BuildWorkbookContent(
                    rightReader, rightPath, rightMediaDir, rightReader.GetSheetNames(), rightNeed);
                swRead.Stop();
                MergePopulatedSheets(result.LeftContent, leftPart);
                MergePopulatedSheets(result.RightContent, rightPart);

                SheetContent leftSheet = FindSheet(IndexSheets(result.LeftContent), pair.LeftSheet)
                    ?? new SheetContent { Name = pair.LeftSheet };
                SheetContent rightSheet = FindSheet(IndexSheets(result.RightContent), pair.RightSheet)
                    ?? new SheetContent { Name = pair.RightSheet };

                RemoveContentItemsForPair(result, pair);
                int pairIndex = 1;
                if (result.SheetPairs != null)
                {
                    for (int i = 0; i < result.SheetPairs.Count; i++)
                    {
                        if (SamePair(result.SheetPairs[i], pair))
                        {
                            pairIndex = i + 1;
                            break;
                        }
                    }
                }

                long pairTable;
                long pairImage;
                AppendPairContentDiffs(
                    result, leftSheet, rightSheet, pair, maskDir, pairIndex,
                    out pairTable, out pairImage);
                RememberCompared(result, pair);
                result.Timings.ReadMs = swRead.ElapsedMilliseconds;
                result.Timings.TableMs = pairTable;
                result.Timings.ImageMs = pairImage;
            }

            var swLayout = Stopwatch.StartNew();
            DiffResultLinker.AttachExpandedLayouts(result);
            DiffResultLinker.MergeOneSidedTextsOnSamePair(result);
            swLayout.Stop();
            result.Timings.LayoutMs = swLayout.ElapsedMilliseconds;
            sw.Stop();
            result.Timings.TotalMs = sw.ElapsedMilliseconds;
            result.Elapsed = sw.Elapsed;
            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "比較段階(追加 {0}↔{1}): 読込={2}ms 表={3}ms 画像={4}ms 配置={5}ms 合計={6}ms",
                pair.LeftSheet,
                pair.RightSheet,
                result.Timings.ReadMs,
                result.Timings.TableMs,
                result.Timings.ImageMs,
                result.Timings.LayoutMs,
                result.Timings.TotalMs));
        }

        /// <summary>
        /// Reader から SheetContent を構築する。populateNames のみ実読込、他は名前スタブ。
        /// </summary>
        private static WorkbookContent BuildWorkbookContent(
            XlsxPackageReader reader,
            string path,
            string mediaDir,
            IReadOnlyList<string> allSheetNames,
            IList<string> populateNames)
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
            var populate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (populateNames != null)
            {
                foreach (string n in populateNames)
                {
                    if (!string.IsNullOrEmpty(n))
                    {
                        populate.Add(n);
                    }
                }
            }

            IReadOnlyList<string> sheetNames = allSheetNames ?? reader.GetSheetNames();
            bool populateAll = populate.Count == 0;
            List<string> extractFilter = populateAll ? null : populate.ToList();

            List<EmbeddedImage> allImages;
            try
            {
                allImages = reader.ExtractImages(extractFilter, mediaDir).ToList();
            }
            catch (Exception ex)
            {
                Log.Debug("ExtractImages 失敗: " + ex.Message);
                allImages = new List<EmbeddedImage>();
            }

            for (int si = 0; si < sheetNames.Count; si++)
            {
                string sheetName = sheetNames[si];
                bool fill = populateAll || populate.Contains(sheetName);
                if (!fill)
                {
                    wb.Sheets.Add(new SheetContent
                    {
                        Name = sheetName,
                        IsPopulated = false
                    });
                    continue;
                }

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
                    Shapes = shapes != null ? shapes.ToList() : new List<ShapeContent>(),
                    IsPopulated = true
                });
            }

            return wb;
        }

        private static void AppendPairContentDiffs(
            DiffResult result,
            SheetContent leftSheet,
            SheetContent rightSheet,
            SheetPair pair,
            string maskDir,
            int pairIndex,
            out long tableMs,
            out long imageMs)
        {
            double baseHint = pairIndex * 1000.0;
            var sw = Stopwatch.StartNew();
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

            sw.Stop();
            tableMs = sw.ElapsedMilliseconds;

            sw.Restart();
            AddImageDiffItems(
                leftSheet.Images,
                rightSheet.Images,
                pair,
                maskDir,
                result.Items,
                pairIndex);
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

            sw.Stop();
            imageMs = sw.ElapsedMilliseconds;
        }

        private static List<SheetPair> ResolvePairsToCompare(
            List<SheetPair> allPairs,
            CompareOptions options,
            bool lazy)
        {
            var list = new List<SheetPair>();
            if (allPairs == null)
            {
                return list;
            }

            if (!lazy)
            {
                list.AddRange(allPairs);
                return list;
            }

            SheetPair focus = options != null ? options.FocusPair : null;
            if (focus != null)
            {
                foreach (SheetPair p in allPairs)
                {
                    if (SamePair(p, focus))
                    {
                        list.Add(p);
                        return list;
                    }
                }
            }

            if (allPairs.Count > 0)
            {
                list.Add(allPairs[0]);
            }

            return list;
        }

        private static List<string> CollectSheetNames(IList<SheetPair> pairs, bool left)
        {
            var names = new List<string>();
            if (pairs == null)
            {
                return names;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SheetPair p in pairs)
            {
                if (p == null)
                {
                    continue;
                }

                string n = left ? p.LeftSheet : p.RightSheet;
                if (!string.IsNullOrEmpty(n) && seen.Add(n))
                {
                    names.Add(n);
                }
            }

            return names;
        }

        private static bool ContainsPair(IList<SheetPair> pairs, SheetPair pair)
        {
            if (pairs == null || pair == null)
            {
                return false;
            }

            foreach (SheetPair p in pairs)
            {
                if (SamePair(p, pair))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SamePair(SheetPair a, SheetPair b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return string.Equals(a.LeftSheet, b.LeftSheet, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.RightSheet, b.RightSheet, StringComparison.OrdinalIgnoreCase);
        }

        private static void RememberCompared(DiffResult result, SheetPair pair)
        {
            if (result == null || pair == null)
            {
                return;
            }

            if (result.ComparedPairKeys == null)
            {
                result.ComparedPairKeys = new List<string>();
            }

            string key = MakePairKey(pair.LeftSheet, pair.RightSheet);
            if (!IsPairCompared(result, pair.LeftSheet, pair.RightSheet))
            {
                result.ComparedPairKeys.Add(key);
            }
        }

        private static void MergePopulatedSheets(WorkbookContent dest, WorkbookContent src)
        {
            if (dest == null || src == null || src.Sheets == null)
            {
                return;
            }

            if (dest.Sheets == null)
            {
                dest.Sheets = new List<SheetContent>();
            }

            foreach (SheetContent incoming in src.Sheets)
            {
                if (incoming == null || !incoming.IsPopulated || string.IsNullOrEmpty(incoming.Name))
                {
                    continue;
                }

                int idx = dest.Sheets.FindIndex(s => s != null
                    && string.Equals(s.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    dest.Sheets[idx] = incoming;
                }
                else
                {
                    dest.Sheets.Add(incoming);
                }
            }
        }

        private static void RemoveContentItemsForPair(DiffResult result, SheetPair pair)
        {
            if (result == null || result.Items == null || pair == null)
            {
                return;
            }

            result.Items.RemoveAll(item =>
                item != null
                && item.Kind != DiffKind.Structure
                && string.Equals(item.SheetLeft, pair.LeftSheet, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.SheetRight, pair.RightSheet, StringComparison.OrdinalIgnoreCase));
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
