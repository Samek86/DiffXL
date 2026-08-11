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
    /// 2 つの .xlsx を比較し DiffResult を返す。
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
        /// <returns>比較結果</returns>
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

                    Report(progress, "埋め込み画像を抽出しています...");
                    List<EmbeddedImage> leftAllImages = leftReader.ExtractImages(null, leftMediaDir).ToList();
                    List<EmbeddedImage> rightAllImages = rightReader.ExtractImages(null, rightMediaDir).ToList();
                    bool imagesHaveSheet =
                        leftAllImages.Any(i => !string.IsNullOrEmpty(i.SheetName))
                        || rightAllImages.Any(i => !string.IsNullOrEmpty(i.SheetName));

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

                        // 1) cells
                        List<CellValue> leftCells = leftReader.EnumerateCells(pair.LeftSheet).ToList();
                        List<CellValue> rightCells = rightReader.EnumerateCells(pair.RightSheet).ToList();

                        // 2) images（シート紐付けあり）
                        List<EmbeddedImage> leftImages = new List<EmbeddedImage>();
                        List<EmbeddedImage> rightImages = new List<EmbeddedImage>();
                        IList<ImageCorrespondence> imageCorr = new List<ImageCorrespondence>();
                        if (imagesHaveSheet)
                        {
                            leftImages = leftAllImages
                                .Where(i => string.Equals(i.SheetName, pair.LeftSheet, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            rightImages = rightAllImages
                                .Where(i => string.Equals(i.SheetName, pair.RightSheet, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            // 3) Match → DiffItems（手動ピンはコスト 0 強制ペア）
                            IList<ManualImagePin> sheetPins = FilterPinsForSheet(
                                options, pair.LeftSheet, pair.RightSheet);
                            imageCorr = ImageCorrespondenceService.Match(
                                leftImages, rightImages, sheetPins);
                            AddDiffItemsFromCorrespondence(
                                imageCorr, pair, maskDir, result.Items, pairIndex);
                        }

                        // 4) テキスト差分
                        foreach (DiffItem textItem in TextDiffService.Compare(leftCells, rightCells, pair, options))
                        {
                            if (textItem != null)
                            {
                                textItem.OrderHint = pairIndex * 1000 + Math.Max(0, textItem.OrderHint);
                                result.Items.Add(textItem);
                            }
                        }

                        // 5) SheetAlignment（ScrollMap は ImageCorrespondence から構築）
                        SheetAlignment alignment;
                        try
                        {
                            alignment = SheetAlignmentBuilder.Build(
                                pair.LeftSheet,
                                pair.RightSheet,
                                leftCells,
                                rightCells,
                                imageCorr);
                            if (alignment.ScrollMap != null)
                            {
                                result.ScrollMaps.Add(alignment.ScrollMap);
                                Log.Debug(alignment.ScrollMap.Describe());
                            }
                        }
                        catch (Exception mapEx)
                        {
                            Log.Debug("SheetAlignment 構築スキップ: " + mapEx.Message);
                            alignment = new SheetAlignment
                            {
                                LeftSheet = pair.LeftSheet,
                                RightSheet = pair.RightSheet,
                                Images = imageCorr,
                                ScrollMap = ContentScrollMap.CreateIdentity(pair.LeftSheet, pair.RightSheet)
                            };
                            result.ScrollMaps.Add(alignment.ScrollMap);
                        }

                        result.Alignments.Add(alignment);
                    }

                    // シート紐付けができない画像はブック単位で 1 回だけ比較
                    if (!imagesHaveSheet && (leftAllImages.Count > 0 || rightAllImages.Count > 0))
                    {
                        Report(progress, "画像を比較しています（ブック単位）...");
                        var bookPair = match.Pairs.FirstOrDefault() ?? new SheetPair
                        {
                            LeftSheet = leftSheets.FirstOrDefault(),
                            RightSheet = rightSheets.FirstOrDefault()
                        };
                        IList<ManualImagePin> bookPins =
                            options != null ? options.ManualImagePins : null;
                        IList<ImageCorrespondence> bookCorr =
                            ImageCorrespondenceService.Match(leftAllImages, rightAllImages, bookPins);
                        AddDiffItemsFromCorrespondence(
                            bookCorr, bookPair, maskDir, result.Items, 0);

                        // ブック単位画像のマップ（先頭シート対応に載せる）
                        SheetAlignment bookAlignment = null;
                        if (bookPair != null)
                        {
                            try
                            {
                                List<CellValue> lc = string.IsNullOrEmpty(bookPair.LeftSheet)
                                    ? new List<CellValue>()
                                    : leftReader.EnumerateCells(bookPair.LeftSheet).ToList();
                                List<CellValue> rc = string.IsNullOrEmpty(bookPair.RightSheet)
                                    ? new List<CellValue>()
                                    : rightReader.EnumerateCells(bookPair.RightSheet).ToList();
                                bookAlignment = SheetAlignmentBuilder.Build(
                                    bookPair.LeftSheet,
                                    bookPair.RightSheet,
                                    lc,
                                    rc,
                                    bookCorr);
                                if (bookAlignment.ScrollMap != null && result.ScrollMaps.Count == 0)
                                {
                                    result.ScrollMaps.Add(bookAlignment.ScrollMap);
                                }

                                if (bookAlignment.ScrollMap != null)
                                {
                                    Log.Debug(bookAlignment.ScrollMap.Describe());
                                }
                            }
                            catch (Exception mapEx)
                            {
                                Log.Debug("SheetAlignment(book) 構築スキップ: " + mapEx.Message);
                            }
                        }

                        // Alignments が空なら book 用を追加、既存があれば先頭に Images を載せる
                        if (result.Alignments.Count == 0)
                        {
                            result.Alignments.Add(bookAlignment ?? new SheetAlignment
                            {
                                LeftSheet = bookPair != null ? bookPair.LeftSheet : null,
                                RightSheet = bookPair != null ? bookPair.RightSheet : null,
                                Images = bookCorr,
                                ScrollMap = null
                            });
                        }
                        else
                        {
                            result.Alignments[0].Images = bookCorr;
                            if (bookAlignment != null && bookAlignment.ScrollMap != null)
                            {
                                result.Alignments[0].ScrollMap = bookAlignment.ScrollMap;
                            }
                        }
                    }
                }

                // 安定した並び
                result.Items = result.Items
                    .OrderBy(i => i.OrderHint)
                    .ThenBy(i => i.Kind)
                    .ThenBy(i => i.AddressLeft ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

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
        /// ImageCorrespondence から DiffItem を生成して items に追加する。
        /// ハッシュ完全一致はスキップ。片側のみは ImageOnly*。ペアは ComparePair。
        /// </summary>
        private static void AddDiffItemsFromCorrespondence(
            IList<ImageCorrespondence> correspondences,
            SheetPair pair,
            string maskDir,
            List<DiffItem> items,
            int pairIndex)
        {
            if (correspondences == null || items == null)
            {
                return;
            }

            pair = pair ?? new SheetPair();
            int index = 0;
            foreach (ImageCorrespondence c in correspondences)
            {
                if (c == null)
                {
                    continue;
                }

                if (c.IsExactHashMatch)
                {
                    index++;
                    continue;
                }

                if (c.IsLeftOnly)
                {
                    EmbeddedImage li = c.Left;
                    items.Add(new DiffItem
                    {
                        Kind = DiffKind.ImageOnlyLeft,
                        SheetLeft = pair.LeftSheet,
                        LeftImagePath = li != null ? li.ExtractedPath : null,
                        Summary = "左のみの画像: "
                            + (li != null ? li.FileName : "?")
                            + FormatDim(li),
                        OrderHint = pairIndex * 1000 + 800 + index
                    });
                    index++;
                    continue;
                }

                if (c.IsRightOnly)
                {
                    EmbeddedImage ri = c.Right;
                    items.Add(new DiffItem
                    {
                        Kind = DiffKind.ImageOnlyRight,
                        SheetRight = pair.RightSheet,
                        RightImagePath = ri != null ? ri.ExtractedPath : null,
                        Summary = "右のみの画像: "
                            + (ri != null ? ri.FileName : "?")
                            + FormatDim(ri),
                        OrderHint = pairIndex * 1000 + 900 + index
                    });
                    index++;
                    continue;
                }

                // paired: 内容比較（閾値未満なら DiffItem なし）
                if (c.IsPaired)
                {
                    CompareImagePairAndAdd(
                        c.Left,
                        c.Right,
                        pair,
                        maskDir,
                        items,
                        pairIndex,
                        index,
                        "corr");
                }

                index++;
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
        /// 1 画像ペアを比較して items に追加する。
        /// </summary>
        private static void CompareImagePairAndAdd(
            EmbeddedImage li,
            EmbeddedImage ri,
            SheetPair pair,
            string maskDir,
            List<DiffItem> items,
            int pairIndex,
            int indexHint,
            string tag)
        {
            if (li == null || ri == null)
            {
                return;
            }

            string maskPath = Path.Combine(
                maskDir,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "p{0}_{1}_{2}.png",
                    pairIndex,
                    tag,
                    Path.GetFileNameWithoutExtension(li.FileName ?? "img")));
            try
            {
                DiffItem diff = ImageDiffService.ComparePair(
                    li.ExtractedPath,
                    ri.ExtractedPath,
                    maskPath,
                    pair.LeftSheet,
                    pair.RightSheet,
                    pairIndex * 1000 + indexHint);
                if (diff != null)
                {
                    items.Add(diff);
                }
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
                    OrderHint = pairIndex * 1000 + indexHint
                });
            }
        }

        /// <summary>
        /// xlsx パスを検証する。
        /// </summary>
        /// <summary>
        /// 指定シートペア向けの手動画像ピンを抽出する。
        /// </summary>
        private static IList<ManualImagePin> FilterPinsForSheet(
            CompareOptions options,
            string leftSheet,
            string rightSheet)
        {
            if (options == null || options.ManualImagePins == null || options.ManualImagePins.Count == 0)
            {
                return null;
            }

            var filtered = options.ManualImagePins
                .Where(p => p != null
                    && (string.IsNullOrEmpty(p.LeftSheet)
                        || string.Equals(p.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrEmpty(p.RightSheet)
                        || string.Equals(p.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            return filtered.Count > 0 ? filtered : null;
        }

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
