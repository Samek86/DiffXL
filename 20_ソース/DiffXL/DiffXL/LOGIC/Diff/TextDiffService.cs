using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// セルテキストの差分を検出する。
    /// </summary>
    public static class TextDiffService
    {
        /// <summary>
        /// アドレス解析用（列文字 + 行番号）。
        /// </summary>
        private static readonly Regex AddressRegex = new Regex(
            @"^\s*([A-Za-z]+)(\d+)\s*$",
            RegexOptions.Compiled);

        /// <summary>
        /// 左右セル集合を比較する。
        /// </summary>
        /// <param name="left">左セル</param>
        /// <param name="right">右セル</param>
        /// <param name="pair">シート対応</param>
        /// <param name="opt">オプション</param>
        /// <returns>差分一覧</returns>
        public static IList<DiffItem> Compare(
            IEnumerable<CellValue> left,
            IEnumerable<CellValue> right,
            SheetPair pair,
            CompareOptions opt)
        {
            var items = new List<DiffItem>();
            int minLeftRow = ParseAnchorRow(opt != null ? opt.AnchorLeftAddress : null);
            int minRightRow = ParseAnchorRow(opt != null ? opt.AnchorRightAddress : null);

            var leftMap = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);
            foreach (CellValue cell in left ?? Enumerable.Empty<CellValue>())
            {
                if (cell == null || string.IsNullOrEmpty(cell.Address))
                {
                    continue;
                }

                if (minLeftRow > 0 && cell.Row > 0 && cell.Row < minLeftRow)
                {
                    continue;
                }

                leftMap[cell.Address] = cell;
            }

            var rightMap = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);
            foreach (CellValue cell in right ?? Enumerable.Empty<CellValue>())
            {
                if (cell == null || string.IsNullOrEmpty(cell.Address))
                {
                    continue;
                }

                if (minRightRow > 0 && cell.Row > 0 && cell.Row < minRightRow)
                {
                    continue;
                }

                rightMap[cell.Address] = cell;
            }

            var addresses = new HashSet<string>(leftMap.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (string key in rightMap.Keys)
            {
                addresses.Add(key);
            }

            foreach (string address in addresses.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
            {
                leftMap.TryGetValue(address, out CellValue l);
                rightMap.TryGetValue(address, out CellValue r);
                string lt = l != null ? (l.Text ?? string.Empty) : string.Empty;
                string rt = r != null ? (r.Text ?? string.Empty) : string.Empty;
                if (string.Equals(lt, rt, StringComparison.Ordinal))
                {
                    continue;
                }

                int row = l != null && l.Row > 0 ? l.Row : (r != null ? r.Row : 0);
                items.Add(new DiffItem
                {
                    Kind = DiffKind.Text,
                    SheetLeft = pair != null ? pair.LeftSheet : null,
                    SheetRight = pair != null ? pair.RightSheet : null,
                    AddressLeft = address,
                    AddressRight = address,
                    Summary = string.Format(
                        CultureInfo.InvariantCulture,
                        "テキスト差分 {0}: 「{1}」→「{2}」",
                        address,
                        Truncate(lt, 40),
                        Truncate(rt, 40)),
                    OrderHint = row
                });
            }

            return items;
        }

        /// <summary>
        /// アンカーアドレスから行番号を取る。失敗時 0。
        /// </summary>
        /// <param name="address">A1 形式</param>
        /// <returns>行番号または 0</returns>
        public static int ParseAnchorRow(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return 0;
            }

            Match m = AddressRegex.Match(address);
            if (!m.Success)
            {
                return 0;
            }

            int row;
            if (int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out row))
            {
                return row;
            }

            return 0;
        }

        /// <summary>
        /// 表示用に文字列を切り詰める。
        /// </summary>
        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, max) + "…";
        }
    }
}
