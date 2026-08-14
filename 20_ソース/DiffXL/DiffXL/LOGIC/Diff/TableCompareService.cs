using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// テーブル系列の対応と、対応テーブル内の行 LCS による差分生成。
    /// SkipLeft 行 → TableRowDelete、SkipRight 行 → TableRowInsert、
    /// Match 行内の Text/Bg 差 → TableCellChange。
    /// </summary>
    public static class TableCompareService
    {
        /// <summary>
        /// テーブル間マッチの粗類似度しきい値。
        /// </summary>
        private const double TableMatchThreshold = 0.3;

        /// <summary>
        /// テーブル Skip コスト。
        /// </summary>
        private const double TableSkipCost = 0.4;

        /// <summary>
        /// 左右テーブル一覧を比較し、行削除・挿入・セル変更の DiffItem を返す。
        /// </summary>
        /// <param name="leftTables">左シートのテーブル一覧</param>
        /// <param name="rightTables">右シートのテーブル一覧</param>
        /// <param name="pair">シート対応</param>
        /// <returns>差分一覧（空でも null ではない）</returns>
        public static IList<DiffItem> Compare(
            IList<TableBlock> leftTables,
            IList<TableBlock> rightTables,
            SheetPair pair)
        {
            var items = new List<DiffItem>();

            IList<TableBlock> left = leftTables ?? Array.Empty<TableBlock>();
            IList<TableBlock> right = rightTables ?? Array.Empty<TableBlock>();

            string sheetL = pair != null ? pair.LeftSheet : null;
            string sheetR = pair != null ? pair.RightSheet : null;

            IList<AlignStep> tableSteps = SequenceAligner.Align(
                left.Count,
                right.Count,
                (i, j) => TableSimilarity(left[i], right[j]),
                TableMatchThreshold,
                TableSkipCost);

            foreach (AlignStep step in tableSteps)
            {
                if (step.Op == AlignOp.Match)
                {
                    TableBlock lt = left[step.LeftIndex];
                    TableBlock rt = right[step.RightIndex];
                    CompareMatchedTables(lt, rt, sheetL, sheetR, items);
                }
                else if (step.Op == AlignOp.SkipLeft)
                {
                    TableBlock lt = left[step.LeftIndex];
                    EmitAllRowsAsDelete(lt, sheetL, sheetR, items);
                }
                else if (step.Op == AlignOp.SkipRight)
                {
                    TableBlock rt = right[step.RightIndex];
                    EmitAllRowsAsInsert(rt, sheetL, sheetR, items);
                }
            }

            return items;
        }

        /// <summary>
        /// 対応済みテーブル内で行をアラインし、削除・挿入・セル変更を出す。
        /// </summary>
        private static void CompareMatchedTables(
            TableBlock leftTable,
            TableBlock rightTable,
            string sheetL,
            string sheetR,
            List<DiffItem> items)
        {
            IList<IList<CellContent>> leftRows =
                leftTable != null && leftTable.Rows != null
                    ? leftTable.Rows
                    : Array.Empty<IList<CellContent>>();
            IList<IList<CellContent>> rightRows =
                rightTable != null && rightTable.Rows != null
                    ? rightTable.Rows
                    : Array.Empty<IList<CellContent>>();

            string tableIdL = leftTable != null ? leftTable.Id : null;
            string tableIdR = rightTable != null ? rightTable.Id : null;

            IList<AlignStep> rowSteps = TableRowAligner.AlignRows(leftRows, rightRows);

            foreach (AlignStep step in rowSteps)
            {
                if (step.Op == AlignOp.SkipLeft)
                {
                    IList<CellContent> row = leftRows[step.LeftIndex];
                    items.Add(CreateRowDelete(
                        row,
                        sheetL,
                        sheetR,
                        tableIdL,
                        tableIdR,
                        step.LeftIndex));
                }
                else if (step.Op == AlignOp.SkipRight)
                {
                    IList<CellContent> row = rightRows[step.RightIndex];
                    items.Add(CreateRowInsert(
                        row,
                        sheetL,
                        sheetR,
                        tableIdL,
                        tableIdR,
                        step.RightIndex));
                }
                else if (step.Op == AlignOp.Match)
                {
                    IList<CellContent> lrow = leftRows[step.LeftIndex];
                    IList<CellContent> rrow = rightRows[step.RightIndex];
                    EmitCellChanges(
                        lrow,
                        rrow,
                        sheetL,
                        sheetR,
                        tableIdL,
                        tableIdR,
                        step.LeftIndex,
                        step.RightIndex,
                        items);
                }
            }
        }

        /// <summary>
        /// 未対応左テーブルの全行を TableRowDelete として出す。
        /// </summary>
        private static void EmitAllRowsAsDelete(
            TableBlock table,
            string sheetL,
            string sheetR,
            List<DiffItem> items)
        {
            if (table == null || table.Rows == null)
            {
                return;
            }

            for (int i = 0; i < table.Rows.Count; i++)
            {
                items.Add(CreateRowDelete(
                    table.Rows[i],
                    sheetL,
                    sheetR,
                    table.Id,
                    null,
                    i));
            }
        }

        /// <summary>
        /// 未対応右テーブルの全行を TableRowInsert として出す。
        /// </summary>
        private static void EmitAllRowsAsInsert(
            TableBlock table,
            string sheetL,
            string sheetR,
            List<DiffItem> items)
        {
            if (table == null || table.Rows == null)
            {
                return;
            }

            for (int i = 0; i < table.Rows.Count; i++)
            {
                items.Add(CreateRowInsert(
                    table.Rows[i],
                    sheetL,
                    sheetR,
                    null,
                    table.Id,
                    i));
            }
        }

        /// <summary>
        /// Match 行を min 列で zip し、Text が異なれば TableCellChange。
        /// 交互行の塗りなど Bg だけの差は差分にしない（表示が全面黄になるのを防ぐ）。
        /// </summary>
        private static void EmitCellChanges(
            IList<CellContent> leftRow,
            IList<CellContent> rightRow,
            string sheetL,
            string sheetR,
            string tableIdL,
            string tableIdR,
            int rowIndexL,
            int rowIndexR,
            List<DiffItem> items)
        {
            int leftLen = leftRow != null ? leftRow.Count : 0;
            int rightLen = rightRow != null ? rightRow.Count : 0;
            int n = Math.Min(leftLen, rightLen);

            for (int c = 0; c < n; c++)
            {
                CellContent lc = leftRow[c];
                CellContent rc = rightRow[c];
                string lt = GetText(lc);
                string rt = GetText(rc);

                if (string.Equals(lt, rt, StringComparison.Ordinal))
                {
                    continue;
                }

                items.Add(new DiffItem
                {
                    Kind = DiffKind.TableCellChange,
                    SheetLeft = sheetL,
                    SheetRight = sheetR,
                    AddressLeft = lc != null ? lc.Address : null,
                    AddressRight = rc != null ? rc.Address : null,
                    TableIdLeft = tableIdL,
                    TableIdRight = tableIdR,
                    RowIndexLeft = rowIndexL,
                    RowIndexRight = rowIndexR,
                    BackgroundLeft = lc != null ? lc.BackgroundArgb : null,
                    BackgroundRight = rc != null ? rc.BackgroundArgb : null,
                    Summary = string.Format(
                        CultureInfo.InvariantCulture,
                        "テーブルセル変更 [{0}/{1}] 「{2}」→「{3}」",
                        tableIdL ?? "?",
                        tableIdR ?? "?",
                        Truncate(lt, 40),
                        Truncate(rt, 40)),
                    OrderHint = PickOrderHint(lc, rc, rowIndexL, rowIndexR)
                });
            }
        }

        /// <summary>
        /// TableRowDelete DiffItem を生成する。
        /// </summary>
        private static DiffItem CreateRowDelete(
            IList<CellContent> row,
            string sheetL,
            string sheetR,
            string tableIdL,
            string tableIdR,
            int rowIndex)
        {
            string key = TableRowAligner.MakeRowKey(row);
            CellContent first = FirstCell(row);

            return new DiffItem
            {
                Kind = DiffKind.TableRowDelete,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = first != null ? first.Address : null,
                AddressRight = null,
                TableIdLeft = tableIdL,
                TableIdRight = tableIdR,
                RowIndexLeft = rowIndex,
                RowIndexRight = null,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "テーブル行削除 [{0}] 「{1}」",
                    tableIdL ?? "?",
                    Truncate(key.Replace('\t', ' '), 60)),
                OrderHint = first != null && first.Row > 0 ? first.Row : rowIndex + 1
            };
        }

        /// <summary>
        /// TableRowInsert DiffItem を生成する。
        /// </summary>
        private static DiffItem CreateRowInsert(
            IList<CellContent> row,
            string sheetL,
            string sheetR,
            string tableIdL,
            string tableIdR,
            int rowIndex)
        {
            string key = TableRowAligner.MakeRowKey(row);
            CellContent first = FirstCell(row);

            return new DiffItem
            {
                Kind = DiffKind.TableRowInsert,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = null,
                AddressRight = first != null ? first.Address : null,
                TableIdLeft = tableIdL,
                TableIdRight = tableIdR,
                RowIndexLeft = null,
                RowIndexRight = rowIndex,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "テーブル行挿入 [{0}] 「{1}」",
                    tableIdR ?? "?",
                    Truncate(key.Replace('\t', ' '), 60)),
                OrderHint = first != null && first.Row > 0 ? first.Row : rowIndex + 1
            };
        }

        /// <summary>
        /// 2 テーブルの粗類似度。
        /// 行キー多重集合 Jaccard と行 LCS ソフト類似度の大きい方（ContentStreamBuilder と揃える）。
        /// </summary>
        private static double TableSimilarity(TableBlock left, TableBlock right)
        {
            List<string> leftKeys = CollectRowKeys(left);
            List<string> rightKeys = CollectRowKeys(right);

            if (leftKeys.Count == 0 && rightKeys.Count == 0)
            {
                return 1.0;
            }

            if (leftKeys.Count == 0 || rightKeys.Count == 0)
            {
                return 0.0;
            }

            // 多重集合 Jaccard: 各キー min 出現の和 / max 出現の和
            var leftCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string k in leftKeys)
            {
                int c;
                leftCount.TryGetValue(k, out c);
                leftCount[k] = c + 1;
            }

            var rightCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string k in rightKeys)
            {
                int c;
                rightCount.TryGetValue(k, out c);
                rightCount[k] = c + 1;
            }

            int inter = 0;
            int union = 0;

            var allKeys = new HashSet<string>(leftCount.Keys, StringComparer.Ordinal);
            foreach (string k in rightCount.Keys)
            {
                allKeys.Add(k);
            }

            foreach (string k in allKeys)
            {
                int lc;
                int rc;
                leftCount.TryGetValue(k, out lc);
                rightCount.TryGetValue(k, out rc);
                inter += Math.Min(lc, rc);
                union += Math.Max(lc, rc);
            }

            double jaccard = union == 0 ? 1.0 : (double)inter / union;
            double soft = TableRowAligner.SoftTableSimilarity(left, right);
            return Math.Max(jaccard, soft);
        }

        /// <summary>
        /// テーブルの行キー一覧を収集する。
        /// </summary>
        private static List<string> CollectRowKeys(TableBlock table)
        {
            var keys = new List<string>();
            if (table == null || table.Rows == null)
            {
                return keys;
            }

            for (int i = 0; i < table.Rows.Count; i++)
            {
                keys.Add(TableRowAligner.MakeRowKey(table.Rows[i]));
            }

            return keys;
        }

        /// <summary>
        /// 行の先頭セル。
        /// </summary>
        private static CellContent FirstCell(IList<CellContent> row)
        {
            if (row == null || row.Count == 0)
            {
                return null;
            }

            return row[0];
        }

        /// <summary>
        /// セル Text（null 安全）。
        /// </summary>
        private static string GetText(CellContent cell)
        {
            return cell != null && cell.Text != null ? cell.Text : string.Empty;
        }

        /// <summary>
        /// OrderHint 用の代表値。
        /// </summary>
        private static double PickOrderHint(
            CellContent left,
            CellContent right,
            int rowIndexL,
            int rowIndexR)
        {
            if (left != null && left.Row > 0)
            {
                return left.Row;
            }

            if (right != null && right.Row > 0)
            {
                return right.Row;
            }

            return Math.Max(rowIndexL, rowIndexR) + 1;
        }

        /// <summary>
        /// 要約用に文字列を切り詰める。
        /// </summary>
        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            if (s.Length <= maxLen)
            {
                return s;
            }

            return s.Substring(0, maxLen) + "…";
        }
    }
}
