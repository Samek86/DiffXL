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
                foreach (SheetPair pair in manualOrNull)
                {
                    if (pair == null || string.IsNullOrEmpty(pair.LeftSheet) || string.IsNullOrEmpty(pair.RightSheet))
                    {
                        continue;
                    }

                    result.Pairs.Add(new SheetPair
                    {
                        LeftSheet = pair.LeftSheet,
                        RightSheet = pair.RightSheet,
                        IsManual = true
                    });
                }

                var usedLeft = new HashSet<string>(result.Pairs.Select(p => p.LeftSheet), StringComparer.OrdinalIgnoreCase);
                var usedRight = new HashSet<string>(result.Pairs.Select(p => p.RightSheet), StringComparer.OrdinalIgnoreCase);
                result.LeftOnlySheets.AddRange(left.Where(s => !usedLeft.Contains(s)));
                result.RightOnlySheets.AddRange(right.Where(s => !usedRight.Contains(s)));
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
