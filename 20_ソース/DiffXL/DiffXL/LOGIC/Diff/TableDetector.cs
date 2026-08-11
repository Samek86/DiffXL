using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 表検出結果（検出された表ブロックとテーブル外セル）。
    /// </summary>
    public sealed class TableDetectResult
    {
        /// <summary>
        /// 検出された表ブロック一覧（OrderIndex 昇順）。
        /// </summary>
        public List<TableBlock> Tables { get; set; } = new List<TableBlock>();

        /// <summary>
        /// いずれの表ボックスにも含まれない非空セル。
        /// </summary>
        public List<CellContent> LooseCells { get; set; } = new List<CellContent>();
    }

    /// <summary>
    /// セルのボーダー有無から表ブロックを検出する。
    /// HasAnyBorder のセルを格子点とし、4 近傍連結成分の bounding box を表とする。
    /// </summary>
    public static class TableDetector
    {
        /// <summary>
        /// 表とみなす最小サイズ（行数・列数ともにこの値以上）。
        /// </summary>
        private const int MinDimension = 2;

        /// <summary>
        /// 同一シートのセル一覧から表ブロックとテーブル外セルを検出する。
        /// </summary>
        /// <param name="cells">同一シートのセル内容一覧</param>
        /// <returns>検出結果</returns>
        public static TableDetectResult Detect(IList<CellContent> cells)
        {
            var result = new TableDetectResult();
            if (cells == null || cells.Count == 0)
            {
                return result;
            }

            // 位置 → セル（同一位置は後勝ち）
            var byPos = new Dictionary<long, CellContent>();
            foreach (CellContent c in cells)
            {
                if (c == null || c.Row < 1 || c.Column < 1)
                {
                    continue;
                }

                byPos[Pack(c.Row, c.Column)] = c;
            }

            // 1. HasAnyBorder のセルを格子点として収集
            var borderKeys = new HashSet<long>();
            foreach (KeyValuePair<long, CellContent> kv in byPos)
            {
                if (kv.Value.HasAnyBorder)
                {
                    borderKeys.Add(kv.Key);
                }
            }

            // 2. 4 近傍で連結成分
            var visited = new HashSet<long>();
            var components = new List<List<long>>();
            foreach (long key in borderKeys)
            {
                if (visited.Contains(key))
                {
                    continue;
                }

                var comp = new List<long>();
                var queue = new Queue<long>();
                queue.Enqueue(key);
                visited.Add(key);
                while (queue.Count > 0)
                {
                    long cur = queue.Dequeue();
                    comp.Add(cur);
                    int r, col;
                    Unpack(cur, out r, out col);
                    // 上下左右
                    TryEnqueueNeighbor(r - 1, col, borderKeys, visited, queue);
                    TryEnqueueNeighbor(r + 1, col, borderKeys, visited, queue);
                    TryEnqueueNeighbor(r, col - 1, borderKeys, visited, queue);
                    TryEnqueueNeighbor(r, col + 1, borderKeys, visited, queue);
                }

                components.Add(comp);
            }

            // 3. 成分の bounding box を TableBlock 候補にする（min 2x2）
            var tableBoxes = new List<int[]>(); // [rowStart, rowEnd, colStart, colEnd]
            foreach (List<long> comp in components)
            {
                int rMin = int.MaxValue, rMax = int.MinValue;
                int cMin = int.MaxValue, cMax = int.MinValue;
                foreach (long key in comp)
                {
                    int r, col;
                    Unpack(key, out r, out col);
                    if (r < rMin) rMin = r;
                    if (r > rMax) rMax = r;
                    if (col < cMin) cMin = col;
                    if (col > cMax) cMax = col;
                }

                int height = rMax - rMin + 1;
                int width = cMax - cMin + 1;
                if (height < MinDimension || width < MinDimension)
                {
                    // 閾値未満は表としない（格子点は Loose 側へ落ちうる）
                    continue;
                }

                tableBoxes.Add(new[] { rMin, rMax, cMin, cMax });
            }

            // 6. OrderIndex = RowStart, ColStart でソート
            tableBoxes.Sort((a, b) =>
            {
                int cmp = a[0].CompareTo(b[0]);
                return cmp != 0 ? cmp : a[2].CompareTo(b[2]);
            });

            // 表内に含まれる位置集合
            var inTable = new HashSet<long>();
            for (int ti = 0; ti < tableBoxes.Count; ti++)
            {
                int[] box = tableBoxes[ti];
                int rowStart = box[0];
                int rowEnd = box[1];
                int colStart = box[2];
                int colEnd = box[3];

                // 4. ボックス内の全セル（border なし含む）を Rows に行列配置
                var rows = new List<IList<CellContent>>();
                for (int r = rowStart; r <= rowEnd; r++)
                {
                    var rowCells = new List<CellContent>();
                    for (int col = colStart; col <= colEnd; col++)
                    {
                        long key = Pack(r, col);
                        inTable.Add(key);
                        CellContent existing;
                        if (byPos.TryGetValue(key, out existing) && existing != null)
                        {
                            rowCells.Add(existing);
                        }
                        else
                        {
                            // 入力に無い格子は空セルで埋める
                            rowCells.Add(new CellContent
                            {
                                Address = ToAddress(r, col),
                                Row = r,
                                Column = col,
                                Text = string.Empty,
                                BackgroundArgb = null,
                                HasAnyBorder = false
                            });
                        }
                    }

                    rows.Add(rowCells);
                }

                result.Tables.Add(new TableBlock
                {
                    Id = "T" + ti.ToString(),
                    OrderIndex = ti,
                    RowStart = rowStart,
                    RowEnd = rowEnd,
                    ColStart = colStart,
                    ColEnd = colEnd,
                    Rows = rows
                });
            }

            // 5. ボックス外の非空セル → LooseCells
            foreach (KeyValuePair<long, CellContent> kv in byPos)
            {
                if (inTable.Contains(kv.Key))
                {
                    continue;
                }

                CellContent c = kv.Value;
                if (c == null)
                {
                    continue;
                }

                if (IsNonEmpty(c.Text))
                {
                    result.LooseCells.Add(c);
                }
            }

            // LooseCells も行・列順で安定化
            result.LooseCells.Sort((a, b) =>
            {
                int cmp = a.Row.CompareTo(b.Row);
                return cmp != 0 ? cmp : a.Column.CompareTo(b.Column);
            });

            return result;
        }

        /// <summary>
        /// 非空テキストか（null / 空 / 空白のみは空とみなす）。
        /// </summary>
        private static bool IsNonEmpty(string text)
        {
            return !string.IsNullOrWhiteSpace(text);
        }

        /// <summary>
        /// 隣接格子を連結成分キューへ追加する。
        /// </summary>
        private static void TryEnqueueNeighbor(
            int row,
            int col,
            HashSet<long> borderKeys,
            HashSet<long> visited,
            Queue<long> queue)
        {
            if (row < 1 || col < 1)
            {
                return;
            }

            long key = Pack(row, col);
            if (!borderKeys.Contains(key) || visited.Contains(key))
            {
                return;
            }

            visited.Add(key);
            queue.Enqueue(key);
        }

        /// <summary>
        /// 行・列を 64bit キーにパックする。
        /// </summary>
        private static long Pack(int row, int col)
        {
            return ((long)row << 32) | (uint)col;
        }

        /// <summary>
        /// パック済みキーを行・列に戻す。
        /// </summary>
        private static void Unpack(long key, out int row, out int col)
        {
            row = (int)(key >> 32);
            col = (int)(key & 0xFFFFFFFFL);
        }

        /// <summary>
        /// 1 始まりの行・列から A1 形式アドレスを生成する。
        /// </summary>
        private static string ToAddress(int row, int col)
        {
            return ColumnLetters(col) + row.ToString();
        }

        /// <summary>
        /// 1 始まり列番号を Excel 列文字（A, B, …, Z, AA, …）へ変換する。
        /// </summary>
        private static string ColumnLetters(int col)
        {
            if (col < 1)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            int n = col;
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)('A' + (n % 26)));
                n /= 26;
            }

            return sb.ToString();
        }
    }
}
