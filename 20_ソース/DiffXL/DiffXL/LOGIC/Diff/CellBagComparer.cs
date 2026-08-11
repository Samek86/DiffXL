using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// セルの多重集合比較（位置非依存）。
    /// キー = Text + "\0" + (BackgroundArgb ?? "") で完全一致を消費し、
    /// 同 Text で背景のみ異なる残りは Background、さらに余りは Text 片側差分とする。
    /// Address は DiffItem の代表メタのみで、マッチには使わない。
    /// </summary>
    public static class CellBagComparer
    {
        /// <summary>
        /// キー区切り（Text と背景 ARGB の間）。
        /// </summary>
        private const char KeySep = '\0';

        /// <summary>
        /// 左右のセル多重集合を比較し、差分一覧を返す。
        /// </summary>
        /// <param name="left">左シートのセル（テーブル外など）</param>
        /// <param name="right">右シートのセル</param>
        /// <param name="pair">シート対応（メタ用）</param>
        /// <returns>差分一覧（完全一致ペアは含めない）</returns>
        public static IList<DiffItem> Compare(
            IEnumerable<CellContent> left,
            IEnumerable<CellContent> right,
            SheetPair pair)
        {
            var items = new List<DiffItem>();
            string sheetL = pair != null ? pair.LeftSheet : null;
            string sheetR = pair != null ? pair.RightSheet : null;

            // 空セルはトークン化しない
            List<CellContent> leftCells = Materialize(left);
            List<CellContent> rightCells = Materialize(right);

            // 完全キー多重集合: キューで消費（Address は先頭代表）
            var leftByFullKey = GroupByKey(leftCells, MakeFullKey);
            var rightByFullKey = GroupByKey(rightCells, MakeFullKey);

            // 1) 完全一致（Text+Bg）を個数分消費 → Diff なし
            foreach (string key in leftByFullKey.Keys.ToList())
            {
                Queue<CellContent> lq;
                Queue<CellContent> rq;
                if (!leftByFullKey.TryGetValue(key, out lq) || !rightByFullKey.TryGetValue(key, out rq))
                {
                    continue;
                }

                int match = Math.Min(lq.Count, rq.Count);
                for (int i = 0; i < match; i++)
                {
                    lq.Dequeue();
                    rq.Dequeue();
                }
            }

            // 残りを Text でグルーピングし、同 Text 同士を Background 差分に寄せる
            List<CellContent> leftRemain = FlattenQueues(leftByFullKey);
            List<CellContent> rightRemain = FlattenQueues(rightByFullKey);

            var leftByText = GroupByKey(leftRemain, c => c.Text ?? string.Empty);
            var rightByText = GroupByKey(rightRemain, c => c.Text ?? string.Empty);

            var allTexts = new HashSet<string>(leftByText.Keys, StringComparer.Ordinal);
            foreach (string t in rightByText.Keys)
            {
                allTexts.Add(t);
            }

            foreach (string text in allTexts.OrderBy(t => t, StringComparer.Ordinal))
            {
                Queue<CellContent> lq;
                Queue<CellContent> rq;
                leftByText.TryGetValue(text, out lq);
                rightByText.TryGetValue(text, out rq);
                if (lq == null)
                {
                    lq = new Queue<CellContent>();
                }

                if (rq == null)
                {
                    rq = new Queue<CellContent>();
                }

                // 2) 同 Text・異 Bg → Background（貪欲に 1:1）
                int bgPairs = Math.Min(lq.Count, rq.Count);
                for (int i = 0; i < bgPairs; i++)
                {
                    CellContent lc = lq.Dequeue();
                    CellContent rc = rq.Dequeue();
                    items.Add(CreateBackgroundDiff(lc, rc, sheetL, sheetR));
                }

                // 3) 余りは Text 片側差分
                while (lq.Count > 0)
                {
                    CellContent lc = lq.Dequeue();
                    items.Add(CreateTextOnlyLeft(lc, sheetL, sheetR));
                }

                while (rq.Count > 0)
                {
                    CellContent rc = rq.Dequeue();
                    items.Add(CreateTextOnlyRight(rc, sheetL, sheetR));
                }
            }

            // OrderHint 昇順で安定化（同一なら Summary）
            return items
                .OrderBy(d => d.OrderHint)
                .ThenBy(d => d.Summary ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 比較キー（Text + 区切り + 背景）。null 背景は空文字。
        /// </summary>
        internal static string MakeFullKey(CellContent cell)
        {
            string text = cell != null && cell.Text != null ? cell.Text : string.Empty;
            string bg = cell != null && cell.BackgroundArgb != null ? cell.BackgroundArgb : string.Empty;
            return text + KeySep + bg;
        }

        /// <summary>
        /// 非空テキストのセルだけをリスト化する。
        /// </summary>
        private static List<CellContent> Materialize(IEnumerable<CellContent> source)
        {
            var list = new List<CellContent>();
            if (source == null)
            {
                return list;
            }

            foreach (CellContent c in source)
            {
                if (c == null || string.IsNullOrEmpty(c.Text))
                {
                    continue;
                }

                list.Add(c);
            }

            return list;
        }

        /// <summary>
        /// キー関数で Queue にグルーピングする（出現順を保持）。
        /// </summary>
        private static Dictionary<string, Queue<CellContent>> GroupByKey(
            IEnumerable<CellContent> cells,
            Func<CellContent, string> keySelector)
        {
            var map = new Dictionary<string, Queue<CellContent>>(StringComparer.Ordinal);
            foreach (CellContent c in cells)
            {
                string key = keySelector(c);
                Queue<CellContent> q;
                if (!map.TryGetValue(key, out q))
                {
                    q = new Queue<CellContent>();
                    map[key] = q;
                }

                q.Enqueue(c);
            }

            return map;
        }

        /// <summary>
        /// キュー辞書の残りセルをフラットなリストにする。
        /// </summary>
        private static List<CellContent> FlattenQueues(Dictionary<string, Queue<CellContent>> map)
        {
            var list = new List<CellContent>();
            foreach (KeyValuePair<string, Queue<CellContent>> kv in map)
            {
                while (kv.Value.Count > 0)
                {
                    list.Add(kv.Value.Dequeue());
                }
            }

            return list;
        }

        /// <summary>
        /// 背景色のみ差分の DiffItem を作る。
        /// </summary>
        private static DiffItem CreateBackgroundDiff(
            CellContent left,
            CellContent right,
            string sheetL,
            string sheetR)
        {
            string addrL = left != null ? left.Address : null;
            string addrR = right != null ? right.Address : null;
            string bgL = left != null ? left.BackgroundArgb : null;
            string bgR = right != null ? right.BackgroundArgb : null;
            string text = left != null ? left.Text : (right != null ? right.Text : string.Empty);
            int row = PickRow(left, right);

            return new DiffItem
            {
                Kind = DiffKind.Background,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = addrL,
                AddressRight = addrR,
                BackgroundLeft = bgL,
                BackgroundRight = bgR,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "背景色差分 「{0}」 {1}/{2}: {3} → {4}",
                    Truncate(text, 40),
                    addrL ?? "?",
                    addrR ?? "?",
                    bgL ?? "(なし)",
                    bgR ?? "(なし)"),
                OrderHint = row
            };
        }

        /// <summary>
        /// 左のみに残ったセルの Text 差分。
        /// </summary>
        private static DiffItem CreateTextOnlyLeft(CellContent left, string sheetL, string sheetR)
        {
            string text = left != null ? (left.Text ?? string.Empty) : string.Empty;
            string addr = left != null ? left.Address : null;
            int row = left != null && left.Row > 0 ? left.Row : 0;

            return new DiffItem
            {
                Kind = DiffKind.Text,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = addr,
                AddressRight = null,
                BackgroundLeft = left != null ? left.BackgroundArgb : null,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "テキスト左のみ {0}: 「{1}」",
                    addr ?? "?",
                    Truncate(text, 40)),
                OrderHint = row
            };
        }

        /// <summary>
        /// 右のみに残ったセルの Text 差分。
        /// </summary>
        private static DiffItem CreateTextOnlyRight(CellContent right, string sheetL, string sheetR)
        {
            string text = right != null ? (right.Text ?? string.Empty) : string.Empty;
            string addr = right != null ? right.Address : null;
            int row = right != null && right.Row > 0 ? right.Row : 0;

            return new DiffItem
            {
                Kind = DiffKind.Text,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = null,
                AddressRight = addr,
                BackgroundRight = right != null ? right.BackgroundArgb : null,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "テキスト右のみ {0}: 「{1}」",
                    addr ?? "?",
                    Truncate(text, 40)),
                OrderHint = row
            };
        }

        /// <summary>
        /// OrderHint 用の代表行を選ぶ。
        /// </summary>
        private static int PickRow(CellContent left, CellContent right)
        {
            if (left != null && left.Row > 0)
            {
                return left.Row;
            }

            if (right != null && right.Row > 0)
            {
                return right.Row;
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
