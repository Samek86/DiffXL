using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// DiffItem を内容ストリームのペア index に結びつける。
    /// </summary>
    public static class DiffResultLinker
    {
        /// <summary>
        /// 非 Structure の DiffItem に <see cref="DiffItem.StreamPairIndex"/> を付ける。
        /// </summary>
        /// <param name="result">比較結果（null 可）</param>
        /// <param name="pairs">内容ストリームのペア列（null 可）</param>
        public static void Attach(DiffResult result, IList<ContentStreamPair> pairs)
        {
            if (result == null || pairs == null || result.Items == null)
            {
                return;
            }

            for (int i = 0; i < result.Items.Count; i++)
            {
                DiffItem item = result.Items[i];
                if (item == null || item.Kind == DiffKind.Structure)
                {
                    continue;
                }

                item.StreamPairIndex = FindPairIndex(item, pairs);
            }
        }

        /// <summary>
        /// Structure を除き、未割当（<see cref="DiffItem.StreamPairIndex"/> &lt; 0）の件数。
        /// </summary>
        /// <param name="result">比較結果（null 可）</param>
        /// <returns>未リンクの内容差分件数</returns>
        public static int CountUnlinkedContentItems(DiffResult result)
        {
            if (result == null || result.Items == null)
            {
                return 0;
            }

            int n = 0;
            for (int i = 0; i < result.Items.Count; i++)
            {
                DiffItem item = result.Items[i];
                if (item == null || item.Kind == DiffKind.Structure)
                {
                    continue;
                }

                if (item.StreamPairIndex < 0)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// ContentPane.FindPairIndexForDiffItem と同じキーで pair を探す。
        /// </summary>
        private static int FindPairIndex(DiffItem item, IList<ContentStreamPair> pairs)
        {
            if (item.Kind == DiffKind.Image
                || item.Kind == DiffKind.ImageOnlyLeft
                || item.Kind == DiffKind.ImageOnlyRight)
            {
                for (int i = 0; i < pairs.Count; i++)
                {
                    ContentStreamPair p = pairs[i];
                    if (p == null)
                    {
                        continue;
                    }

                    if (BlockMatchesImage(p.Left, item) || BlockMatchesImage(p.Right, item))
                    {
                        return i;
                    }
                }
            }

            if (item.Kind == DiffKind.TableRowDelete
                || item.Kind == DiffKind.TableRowInsert
                || item.Kind == DiffKind.TableCellChange)
            {
                int firstHeader = -1;
                for (int i = 0; i < pairs.Count; i++)
                {
                    ContentStreamPair p = pairs[i];
                    if (p == null)
                    {
                        continue;
                    }

                    if (!BlockMatchesTable(p.Left, item) && !BlockMatchesTable(p.Right, item))
                    {
                        continue;
                    }

                    if (BlockMatchesTableRowIndex(p.Left, item) || BlockMatchesTableRowIndex(p.Right, item))
                    {
                        return i;
                    }

                    if (firstHeader < 0
                        && ((p.Left != null && p.Left.Kind == ContentBlockKind.TableHeader)
                            || (p.Right != null && p.Right.Kind == ContentBlockKind.TableHeader)))
                    {
                        firstHeader = i;
                    }
                }

                if (firstHeader >= 0)
                {
                    return firstHeader;
                }
            }

            int row = TextDiffService.ParseAnchorRow(item.AddressLeft);
            if (row <= 0)
            {
                row = TextDiffService.ParseAnchorRow(item.AddressRight);
            }

            if (row > 0)
            {
                for (int i = 0; i < pairs.Count; i++)
                {
                    ContentStreamPair p = pairs[i];
                    if (p == null)
                    {
                        continue;
                    }

                    if ((p.Left != null && p.Left.Kind == ContentBlockKind.LooseRow && p.Left.Row == row)
                        || (p.Right != null && p.Right.Kind == ContentBlockKind.LooseRow && p.Right.Row == row))
                    {
                        return i;
                    }
                }
            }

            if (item.OrderHint > 0)
            {
                return ContentStreamBuilder.FindNearestPairIndex(pairs, item.OrderHint);
            }

            return -1;
        }

        /// <summary>
        /// ContentPane.BlockMatchesImage と同じパス一致に加え、ファイル名でも照合する。
        /// </summary>
        private static bool BlockMatchesImage(ContentStreamBlock block, DiffItem item)
        {
            if (block == null || block.Kind != ContentBlockKind.Image || block.Image == null || item == null)
            {
                return false;
            }

            string path = block.Image.ExtractedPath;
            if (!string.IsNullOrEmpty(path)
                && (string.Equals(path, item.LeftImagePath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, item.RightImagePath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            string fileName = block.Image.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            return FileNameEquals(fileName, item.LeftImagePath)
                || FileNameEquals(fileName, item.RightImagePath);
        }

        /// <summary>
        /// ContentPane.BlockMatchesTable と同じ TableId 照合。
        /// </summary>
        private static bool BlockMatchesTable(ContentStreamBlock block, DiffItem item)
        {
            if (block == null || block.Table == null || item == null)
            {
                return false;
            }

            if (block.Kind != ContentBlockKind.Table
                && block.Kind != ContentBlockKind.TableHeader
                && block.Kind != ContentBlockKind.TableRow)
            {
                return false;
            }

            string id = block.Table.Id;
            if (string.IsNullOrEmpty(id))
            {
                return true;
            }

            return string.Equals(id, item.TableIdLeft, StringComparison.Ordinal)
                || string.Equals(id, item.TableIdRight, StringComparison.Ordinal);
        }

        /// <summary>
        /// TableRow ブロックの行 index が DiffItem の RowIndexLeft / RowIndexRight と一致するか。
        /// </summary>
        private static bool BlockMatchesTableRowIndex(ContentStreamBlock block, DiffItem item)
        {
            if (block == null || block.Kind != ContentBlockKind.TableRow || item == null)
            {
                return false;
            }

            int? idx = TryGetTableRowIndex(block);
            if (!idx.HasValue)
            {
                return false;
            }

            return (item.RowIndexLeft.HasValue && item.RowIndexLeft.Value == idx.Value)
                || (item.RowIndexRight.HasValue && item.RowIndexRight.Value == idx.Value);
        }

        private static int? TryGetTableRowIndex(ContentStreamBlock block)
        {
            if (block.Table != null && block.Table.Rows != null && block.Cells != null)
            {
                for (int i = 0; i < block.Table.Rows.Count; i++)
                {
                    if (object.ReferenceEquals(block.Table.Rows[i], block.Cells))
                    {
                        return i;
                    }
                }
            }

            if (string.IsNullOrEmpty(block.Id))
            {
                return null;
            }

            int colon = block.Id.LastIndexOf(':');
            int parsed;
            if (colon >= 0
                && colon < block.Id.Length - 1
                && int.TryParse(
                    block.Id.Substring(colon + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool FileNameEquals(string fileName, string pathOrName)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(pathOrName))
            {
                return false;
            }

            if (string.Equals(fileName, pathOrName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string leaf;
            try
            {
                leaf = Path.GetFileName(pathOrName);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return !string.IsNullOrEmpty(leaf)
                && string.Equals(fileName, leaf, StringComparison.OrdinalIgnoreCase);
        }
    }
}
