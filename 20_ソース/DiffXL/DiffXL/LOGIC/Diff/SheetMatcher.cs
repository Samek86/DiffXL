using System;
using System.Collections.Generic;
using System.Linq;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 左右のシート名を対応付ける。
    /// </summary>
    public static class SheetMatcher
    {
        /// <summary>
        /// シート対応を決定する。manual があれば優先し、なければ同名同士。
        /// </summary>
        /// <param name="leftSheets">左シート名</param>
        /// <param name="rightSheets">右シート名</param>
        /// <param name="manualOrNull">手動対応（null 可）</param>
        /// <returns>対応結果</returns>
        public static SheetMatchResult Match(
            IList<string> leftSheets,
            IList<string> rightSheets,
            List<SheetPair> manualOrNull)
        {
            var result = new SheetMatchResult();
            var left = (leftSheets ?? Array.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var right = (rightSheets ?? Array.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();

            if (manualOrNull != null && manualOrNull.Count > 0)
            {
                var usedLeft = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var usedRight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (SheetPair pair in manualOrNull)
                {
                    if (pair == null)
                    {
                        continue;
                    }

                    bool hasLeft = !string.IsNullOrEmpty(pair.LeftSheet);
                    bool hasRight = !string.IsNullOrEmpty(pair.RightSheet);

                    // 両側あり → 比較ペア
                    if (hasLeft && hasRight)
                    {
                        result.Pairs.Add(new SheetPair
                        {
                            LeftSheet = pair.LeftSheet,
                            RightSheet = pair.RightSheet,
                            IsManual = true
                        });
                        usedLeft.Add(pair.LeftSheet);
                        usedRight.Add(pair.RightSheet);
                        continue;
                    }

                    // 片側明示 → Structure 対象
                    if (hasLeft && !hasRight)
                    {
                        if (!usedLeft.Contains(pair.LeftSheet))
                        {
                            result.LeftOnlySheets.Add(pair.LeftSheet);
                            usedLeft.Add(pair.LeftSheet);
                        }

                        continue;
                    }

                    if (hasRight && !hasLeft)
                    {
                        if (!usedRight.Contains(pair.RightSheet))
                        {
                            result.RightOnlySheets.Add(pair.RightSheet);
                            usedRight.Add(pair.RightSheet);
                        }
                    }
                }

                foreach (string name in left)
                {
                    if (usedLeft.Contains(name))
                    {
                        continue;
                    }

                    string match = right.FirstOrDefault(r =>
                        !usedRight.Contains(r)
                        && string.Equals(r, name, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        result.Pairs.Add(new SheetPair
                        {
                            LeftSheet = name,
                            RightSheet = match,
                            IsManual = false
                        });
                        usedLeft.Add(name);
                        usedRight.Add(match);
                    }
                    else
                    {
                        result.LeftOnlySheets.Add(name);
                        usedLeft.Add(name);
                    }
                }

                foreach (string name in right)
                {
                    if (!usedRight.Contains(name))
                    {
                        result.RightOnlySheets.Add(name);
                    }
                }

                return result;
            }

            var rightSet = new HashSet<string>(right, StringComparer.OrdinalIgnoreCase);
            var matchedRight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in left)
            {
                string match = right.FirstOrDefault(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    result.Pairs.Add(new SheetPair
                    {
                        LeftSheet = name,
                        RightSheet = match,
                        IsManual = false
                    });
                    matchedRight.Add(match);
                }
                else
                {
                    result.LeftOnlySheets.Add(name);
                }
            }

            foreach (string name in right)
            {
                if (!matchedRight.Contains(name))
                {
                    result.RightOnlySheets.Add(name);
                }
            }

            return result;
        }
    }
}
