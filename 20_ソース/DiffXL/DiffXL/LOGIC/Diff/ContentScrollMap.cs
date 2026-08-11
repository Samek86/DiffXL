using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DiffXL.LOGIC.Excel;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 左右シートの縦スクロール対応（内容ベース）。
    /// 横方向はセル位置 1:1 のまま、縦だけテキスト／画像の一致でマップする。
    /// 片側のみの内容では相手側の行をホールドし、再一致で同期する。
    /// 画像対応は <see cref="ImageCorrespondence"/> のみを使い、ここでは再マッチしない。
    /// </summary>
    public sealed class ContentScrollMap
    {
        private enum SegKind
        {
            Equal,
            LeftOnly,
            RightOnly
        }

        private sealed class Segment
        {
            public SegKind Kind;
            public int LeftStart;
            public int LeftEnd;
            public int RightStart;
            public int RightEnd;
            public int HoldRow;
        }

        /// <summary>読み順のテキスト内容トークン（LCS 単位）。</summary>
        private sealed class Token
        {
            public int Row;
            public string Signature;
        }

        /// <summary>左右の Equal レンジ（行スパン一致済み）。</summary>
        private sealed class EqualRange
        {
            public int LeftStart;
            public int LeftEnd;
            public int RightStart;
            public int RightEnd;
        }

        /// <summary>片側のみの行レンジ。</summary>
        private sealed class OnlyRange
        {
            public int Start;
            public int End;
        }

        public string LeftSheet { get; private set; }
        public string RightSheet { get; private set; }
        public bool IsContentBased { get; private set; }

        private readonly List<Segment> _segments = new List<Segment>();

        public static ContentScrollMap Identity { get; } = CreateIdentity(null, null);

        public static ContentScrollMap CreateIdentity(string leftSheet, string rightSheet)
        {
            var map = new ContentScrollMap
            {
                LeftSheet = leftSheet,
                RightSheet = rightSheet,
                IsContentBased = false
            };
            map._segments.Add(new Segment
            {
                Kind = SegKind.Equal,
                LeftStart = 1,
                LeftEnd = int.MaxValue / 4,
                RightStart = 1,
                RightEnd = int.MaxValue / 4
            });
            return map;
        }

        /// <summary>
        /// セルと画像対応から内容対応マップを構築する。
        /// 画像は <paramref name="images"/> の対応結果のみを使い、再マッチしない。
        /// </summary>
        public static ContentScrollMap Build(
            string leftSheet,
            string rightSheet,
            IEnumerable<CellValue> leftCells,
            IEnumerable<CellValue> rightCells,
            IList<ImageCorrespondence> images)
        {
            List<Token> leftTokens = CollectTextTokens(leftCells);
            List<Token> rightTokens = CollectTextTokens(rightCells);

            List<EqualRange> imageEquals;
            List<OnlyRange> leftOnlyRanges;
            List<OnlyRange> rightOnlyRanges;
            HashSet<int> leftImageRows;
            HashSet<int> rightImageRows;
            CollectImageLandmarks(
                images,
                out imageEquals,
                out leftOnlyRanges,
                out rightOnlyRanges,
                out leftImageRows,
                out rightImageRows);

            // 画像占有行のテキストはランドマークにしない（画像ギャップ優先）
            leftTokens = leftTokens.Where(t => !leftImageRows.Contains(t.Row)).ToList();
            rightTokens = rightTokens.Where(t => !rightImageRows.Contains(t.Row)).ToList();

            List<EqualRange> textEquals = BuildTextEqualRanges(leftTokens, rightTokens);

            if (imageEquals.Count == 0
                && textEquals.Count == 0
                && leftOnlyRanges.Count == 0
                && rightOnlyRanges.Count == 0)
            {
                return CreateIdentity(leftSheet, rightSheet);
            }

            var map = new ContentScrollMap
            {
                LeftSheet = leftSheet,
                RightSheet = rightSheet,
                IsContentBased = true
            };
            map.BuildSegments(imageEquals, textEquals, leftOnlyRanges, rightOnlyRanges);
            return map;
        }

        /// <summary>
        /// 互換: 生画像リストから Match して構築（テスト／旧呼び出し用）。
        /// </summary>
        public static ContentScrollMap Build(
            string leftSheet,
            string rightSheet,
            IEnumerable<CellValue> leftCells,
            IEnumerable<CellValue> rightCells,
            IEnumerable<EmbeddedImage> leftImages,
            IEnumerable<EmbeddedImage> rightImages)
        {
            IList<ImageCorrespondence> corr = ImageCorrespondenceService.Match(
                leftImages != null ? leftImages.ToList() : null,
                rightImages != null ? rightImages.ToList() : null);
            return Build(leftSheet, rightSheet, leftCells, rightCells, corr);
        }

        public int MapLeftToRight(int leftRow)
        {
            if (leftRow < 1)
            {
                leftRow = 1;
            }

            if (!IsContentBased || _segments.Count == 0)
            {
                return leftRow;
            }

            foreach (Segment seg in _segments)
            {
                if (seg.Kind == SegKind.RightOnly)
                {
                    continue;
                }

                if (leftRow >= seg.LeftStart && leftRow <= seg.LeftEnd)
                {
                    if (seg.Kind == SegKind.Equal)
                    {
                        return seg.RightStart + (leftRow - seg.LeftStart);
                    }

                    return Math.Max(1, seg.HoldRow);
                }
            }

            Segment last = _segments[_segments.Count - 1];
            if (last.Kind == SegKind.Equal)
            {
                return Math.Max(1, last.RightStart + (leftRow - last.LeftStart));
            }

            if (last.Kind == SegKind.LeftOnly)
            {
                return Math.Max(1, last.HoldRow);
            }

            return Math.Max(1, last.RightEnd);
        }

        public int MapRightToLeft(int rightRow)
        {
            if (rightRow < 1)
            {
                rightRow = 1;
            }

            if (!IsContentBased || _segments.Count == 0)
            {
                return rightRow;
            }

            foreach (Segment seg in _segments)
            {
                if (seg.Kind == SegKind.LeftOnly)
                {
                    continue;
                }

                if (rightRow >= seg.RightStart && rightRow <= seg.RightEnd)
                {
                    if (seg.Kind == SegKind.Equal)
                    {
                        return seg.LeftStart + (rightRow - seg.RightStart);
                    }

                    return Math.Max(1, seg.HoldRow);
                }
            }

            Segment last = _segments[_segments.Count - 1];
            if (last.Kind == SegKind.Equal)
            {
                return Math.Max(1, last.LeftStart + (rightRow - last.RightStart));
            }

            if (last.Kind == SegKind.RightOnly)
            {
                return Math.Max(1, last.HoldRow);
            }

            return Math.Max(1, last.LeftEnd);
        }

        /// <summary>
        /// 左行を照会し、セグメント種別・相手行・範囲を返す。
        /// </summary>
        public ScrollMapProbe ProbeFromLeft(int leftRow)
        {
            if (leftRow < 1)
            {
                leftRow = 1;
            }

            if (!IsContentBased || _segments.Count == 0)
            {
                return IdentityProbe(leftRow);
            }

            foreach (Segment seg in _segments)
            {
                if (seg.Kind == SegKind.RightOnly)
                {
                    continue;
                }

                if (leftRow >= seg.LeftStart && leftRow <= seg.LeftEnd)
                {
                    return ProbeFromSegmentLeft(seg, leftRow);
                }
            }

            Segment last = _segments[_segments.Count - 1];
            if (last.Kind == SegKind.Equal)
            {
                return new ScrollMapProbe
                {
                    Kind = SyncSegmentKind.Equal,
                    MappedRow = Math.Max(1, last.RightStart + (leftRow - last.LeftStart)),
                    HoldRow = 0,
                    SegmentStart = last.LeftStart,
                    SegmentEnd = last.LeftEnd
                };
            }

            if (last.Kind == SegKind.LeftOnly)
            {
                int hold = Math.Max(1, last.HoldRow);
                return new ScrollMapProbe
                {
                    Kind = SyncSegmentKind.LeftOnly,
                    MappedRow = hold,
                    HoldRow = hold,
                    SegmentStart = last.LeftStart,
                    SegmentEnd = last.LeftEnd
                };
            }

            // 末尾が RightOnly のときはその直前までの右端に相当
            return new ScrollMapProbe
            {
                Kind = SyncSegmentKind.RightOnly,
                MappedRow = Math.Max(1, last.RightEnd),
                HoldRow = Math.Max(1, last.HoldRow),
                SegmentStart = last.RightStart,
                SegmentEnd = last.RightEnd
            };
        }

        /// <summary>
        /// 右行を照会し、セグメント種別・相手行・範囲を返す。
        /// </summary>
        public ScrollMapProbe ProbeFromRight(int rightRow)
        {
            if (rightRow < 1)
            {
                rightRow = 1;
            }

            if (!IsContentBased || _segments.Count == 0)
            {
                return IdentityProbe(rightRow);
            }

            foreach (Segment seg in _segments)
            {
                if (seg.Kind == SegKind.LeftOnly)
                {
                    continue;
                }

                if (rightRow >= seg.RightStart && rightRow <= seg.RightEnd)
                {
                    return ProbeFromSegmentRight(seg, rightRow);
                }
            }

            Segment last = _segments[_segments.Count - 1];
            if (last.Kind == SegKind.Equal)
            {
                return new ScrollMapProbe
                {
                    Kind = SyncSegmentKind.Equal,
                    MappedRow = Math.Max(1, last.LeftStart + (rightRow - last.RightStart)),
                    HoldRow = 0,
                    SegmentStart = last.RightStart,
                    SegmentEnd = last.RightEnd
                };
            }

            if (last.Kind == SegKind.RightOnly)
            {
                int hold = Math.Max(1, last.HoldRow);
                return new ScrollMapProbe
                {
                    Kind = SyncSegmentKind.RightOnly,
                    MappedRow = hold,
                    HoldRow = hold,
                    SegmentStart = last.RightStart,
                    SegmentEnd = last.RightEnd
                };
            }

            return new ScrollMapProbe
            {
                Kind = SyncSegmentKind.LeftOnly,
                MappedRow = Math.Max(1, last.LeftEnd),
                HoldRow = Math.Max(1, last.HoldRow),
                SegmentStart = last.LeftStart,
                SegmentEnd = last.LeftEnd
            };
        }

        private static ScrollMapProbe IdentityProbe(int row)
        {
            return new ScrollMapProbe
            {
                Kind = SyncSegmentKind.Identity,
                MappedRow = row,
                HoldRow = 0,
                SegmentStart = row,
                SegmentEnd = row
            };
        }

        private static ScrollMapProbe ProbeFromSegmentLeft(Segment seg, int leftRow)
        {
            if (seg.Kind == SegKind.Equal)
            {
                return new ScrollMapProbe
                {
                    Kind = SyncSegmentKind.Equal,
                    MappedRow = seg.RightStart + (leftRow - seg.LeftStart),
                    HoldRow = 0,
                    SegmentStart = seg.LeftStart,
                    SegmentEnd = seg.LeftEnd
                };
            }

            int hold = Math.Max(1, seg.HoldRow);
            return new ScrollMapProbe
            {
                Kind = SyncSegmentKind.LeftOnly,
                MappedRow = hold,
                HoldRow = hold,
                SegmentStart = seg.LeftStart,
                SegmentEnd = seg.LeftEnd
            };
        }

        private static ScrollMapProbe ProbeFromSegmentRight(Segment seg, int rightRow)
        {
            if (seg.Kind == SegKind.Equal)
            {
                return new ScrollMapProbe
                {
                    Kind = SyncSegmentKind.Equal,
                    MappedRow = seg.LeftStart + (rightRow - seg.RightStart),
                    HoldRow = 0,
                    SegmentStart = seg.RightStart,
                    SegmentEnd = seg.RightEnd
                };
            }

            int hold = Math.Max(1, seg.HoldRow);
            return new ScrollMapProbe
            {
                Kind = SyncSegmentKind.RightOnly,
                MappedRow = hold,
                HoldRow = hold,
                SegmentStart = seg.RightStart,
                SegmentEnd = seg.RightEnd
            };
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("ContentScrollMap ")
                .Append(LeftSheet ?? "?")
                .Append(" ↔ ")
                .Append(RightSheet ?? "?")
                .Append(IsContentBased ? " content" : " identity")
                .Append(" segs=")
                .Append(_segments.Count);
            foreach (Segment s in _segments.Take(14))
            {
                sb.Append(" | ").Append(s.Kind)
                    .Append(" L").Append(s.LeftStart).Append("-").Append(s.LeftEnd)
                    .Append(" R").Append(s.RightStart).Append("-").Append(s.RightEnd)
                    .Append(" hold=").Append(s.HoldRow);
            }

            if (_segments.Count > 14)
            {
                sb.Append(" | ...");
            }

            return sb.ToString();
        }

        /// <summary>
        /// セルを読み順テキストトークンにする。弱いトークンは除外。
        /// </summary>
        private static List<Token> CollectTextTokens(IEnumerable<CellValue> cells)
        {
            var tokens = new List<Token>();

            foreach (CellValue cell in (cells ?? Enumerable.Empty<CellValue>())
                .Where(c => c != null && c.Row > 0 && !string.IsNullOrWhiteSpace(c.Text))
                .OrderBy(c => c.Row)
                .ThenBy(c => c.Column))
            {
                string text = cell.Text.Trim();
                if (text.Length == 0 || IsWeakTextToken(text))
                {
                    continue;
                }

                if (text.Length > 120)
                {
                    text = text.Substring(0, 120);
                }

                tokens.Add(new Token
                {
                    Row = cell.Row,
                    Signature = "T:" + text
                });
            }

            return tokens;
        }

        /// <summary>
        /// 単独では対応付けに使わない弱いテキストか。
        /// </summary>
        private static bool IsWeakTextToken(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            int n;
            if (text.Length <= 3 && int.TryParse(text, out n))
            {
                return true;
            }

            if (text.Length == 1)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// ImageCorrespondence から Equal / LeftOnly / RightOnly レンジを生成。
        /// <para>
        /// <b>IsPaired（exact / modified 問わず）</b>は必ず
        /// Left.Anchor.RowStart..RowEnd と Right.Anchor.RowStart..RowEnd から
        /// 短いスパン分の Equal を作る。長い側の余りだけ LeftOnly/RightOnly。
        /// Equal に入った行は leftOnly/rightOnly に入れない（Normalize でも再除去）。
        /// </para>
        /// </summary>
        private static void CollectImageLandmarks(
            IList<ImageCorrespondence> images,
            out List<EqualRange> equalRanges,
            out List<OnlyRange> leftOnlyRanges,
            out List<OnlyRange> rightOnlyRanges,
            out HashSet<int> leftImageRows,
            out HashSet<int> rightImageRows)
        {
            equalRanges = new List<EqualRange>();
            leftOnlyRanges = new List<OnlyRange>();
            rightOnlyRanges = new List<OnlyRange>();
            leftImageRows = new HashSet<int>();
            rightImageRows = new HashSet<int>();

            if (images == null)
            {
                return;
            }

            // ペア Equal 行を後で only から確実に除外するための占有集合
            var pairedLeftEqualRows = new HashSet<int>();
            var pairedRightEqualRows = new HashSet<int>();

            foreach (ImageCorrespondence c in images)
            {
                if (c == null)
                {
                    continue;
                }

                // exact / modified を問わず IsPaired なら Equal アンカーを必ず生成する
                // （modified を LeftOnly+RightOnly に落とすと L8→hold=5 のような回帰になる）
                if (c.IsPaired)
                {
                    int lStart, lEnd, rStart, rEnd;
                    if (!TryGetRowRange(c.Left, out lStart, out lEnd)
                        || !TryGetRowRange(c.Right, out rStart, out rEnd))
                    {
                        continue;
                    }

                    // 画像占有行全体（テキスト LCS から除外）
                    MarkRows(leftImageRows, lStart, lEnd);
                    MarkRows(rightImageRows, rStart, rEnd);

                    int lSpan = lEnd - lStart + 1;
                    int rSpan = rEnd - rStart + 1;
                    int common = Math.Min(lSpan, rSpan);
                    if (common > 0)
                    {
                        int eqL0 = lStart;
                        int eqL1 = lStart + common - 1;
                        int eqR0 = rStart;
                        int eqR1 = rStart + common - 1;
                        equalRanges.Add(new EqualRange
                        {
                            LeftStart = eqL0,
                            LeftEnd = eqL1,
                            RightStart = eqR0,
                            RightEnd = eqR1
                        });
                        MarkRows(pairedLeftEqualRows, eqL0, eqL1);
                        MarkRows(pairedRightEqualRows, eqR0, eqR1);
                    }

                    // 長い側の余りのみ only（Equal 本体は only に入れない）
                    if (lSpan > common)
                    {
                        leftOnlyRanges.Add(new OnlyRange
                        {
                            Start = lStart + common,
                            End = lEnd
                        });
                    }
                    else if (rSpan > common)
                    {
                        rightOnlyRanges.Add(new OnlyRange
                        {
                            Start = rStart + common,
                            End = rEnd
                        });
                    }
                }
                else if (c.IsLeftOnly)
                {
                    int lStart, lEnd;
                    if (!TryGetRowRange(c.Left, out lStart, out lEnd))
                    {
                        continue;
                    }

                    MarkRows(leftImageRows, lStart, lEnd);
                    leftOnlyRanges.Add(new OnlyRange { Start = lStart, End = lEnd });
                }
                else if (c.IsRightOnly)
                {
                    int rStart, rEnd;
                    if (!TryGetRowRange(c.Right, out rStart, out rEnd))
                    {
                        continue;
                    }

                    MarkRows(rightImageRows, rStart, rEnd);
                    rightOnlyRanges.Add(new OnlyRange { Start = rStart, End = rEnd });
                }
            }

            // 防御: ペア Equal 行が only に混入していたら削る
            leftOnlyRanges = SubtractRowsFromOnlyRanges(leftOnlyRanges, pairedLeftEqualRows);
            rightOnlyRanges = SubtractRowsFromOnlyRanges(rightOnlyRanges, pairedRightEqualRows);
        }

        /// <summary>
        /// only レンジから指定行を除去し、連続区間に再分割する。
        /// </summary>
        private static List<OnlyRange> SubtractRowsFromOnlyRanges(
            List<OnlyRange> ranges,
            HashSet<int> forbiddenRows)
        {
            var result = new List<OnlyRange>();
            if (ranges == null || ranges.Count == 0)
            {
                return result;
            }

            if (forbiddenRows == null || forbiddenRows.Count == 0)
            {
                result.AddRange(ranges);
                return result;
            }

            foreach (OnlyRange raw in ranges)
            {
                if (raw == null || raw.End < raw.Start)
                {
                    continue;
                }

                int runStart = -1;
                for (int r = raw.Start; r <= raw.End; r++)
                {
                    if (forbiddenRows.Contains(r))
                    {
                        if (runStart >= 0)
                        {
                            result.Add(new OnlyRange { Start = runStart, End = r - 1 });
                            runStart = -1;
                        }
                    }
                    else if (runStart < 0)
                    {
                        runStart = r;
                    }
                }

                if (runStart >= 0)
                {
                    result.Add(new OnlyRange { Start = runStart, End = raw.End });
                }
            }

            return result;
        }

        private static bool TryGetRowRange(EmbeddedImage img, out int start, out int end)
        {
            start = 0;
            end = 0;
            if (img == null)
            {
                return false;
            }

            if (img.Anchor != null && img.Anchor.RowStart >= 1)
            {
                start = img.Anchor.RowStart;
                end = Math.Max(img.Anchor.RowStart, img.Anchor.RowEnd > 0 ? img.Anchor.RowEnd : img.Anchor.RowStart);
                return true;
            }

            if (img.AnchorRow >= 1)
            {
                start = img.AnchorRow;
                end = img.AnchorRow;
                return true;
            }

            return false;
        }

        private static void MarkRows(HashSet<int> set, int start, int end)
        {
            for (int r = start; r <= end; r++)
            {
                set.Add(r);
            }
        }

        /// <summary>
        /// テキストトークン LCS → 単一行 Equal レンジ。
        /// </summary>
        private static List<EqualRange> BuildTextEqualRanges(List<Token> leftTokens, List<Token> rightTokens)
        {
            var result = new List<EqualRange>();
            int n = leftTokens.Count;
            int m = rightTokens.Count;
            if (n == 0 || m == 0)
            {
                return result;
            }

            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    if (string.Equals(leftTokens[i].Signature, rightTokens[j].Signature, StringComparison.Ordinal))
                    {
                        dp[i, j] = dp[i + 1, j + 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                    }
                }
            }

            var matchTokens = new List<Tuple<int, int>>();
            int a = 0, b = 0;
            while (a < n && b < m)
            {
                if (string.Equals(leftTokens[a].Signature, rightTokens[b].Signature, StringComparison.Ordinal))
                {
                    matchTokens.Add(Tuple.Create(a, b));
                    a++;
                    b++;
                }
                else if (dp[a + 1, b] >= dp[a, b + 1])
                {
                    a++;
                }
                else
                {
                    b++;
                }
            }

            foreach (Tuple<int, int> mt in matchTokens)
            {
                int lr = leftTokens[mt.Item1].Row;
                int rr = rightTokens[mt.Item2].Row;

                if (result.Count > 0
                    && result[result.Count - 1].LeftStart == lr
                    && result[result.Count - 1].RightStart == rr)
                {
                    continue;
                }

                // 単調性
                if (result.Count > 0
                    && (lr < result[result.Count - 1].LeftEnd
                        || rr < result[result.Count - 1].RightEnd))
                {
                    continue;
                }

                result.Add(new EqualRange
                {
                    LeftStart = lr,
                    LeftEnd = lr,
                    RightStart = rr,
                    RightEnd = rr
                });
            }

            return result;
        }

        /// <summary>
        /// 画像 Equal + テキスト Equal + 片側のみレンジからセグメント列を構築。
        /// 画像 Equal を硬アンカーとして先に単調採用し、テキスト Equal は
        /// 画像を壊さない場合のみ挿入する（テキストが後続の画像ペアを落とす回帰を防ぐ）。
        /// </summary>
        private void BuildSegments(
            List<EqualRange> imageEquals,
            List<EqualRange> textEquals,
            List<OnlyRange> leftOnlyRanges,
            List<OnlyRange> rightOnlyRanges)
        {
            // 1) 画像 Equal を先に単調フィルタ（交差する画像同士のみ落とす）
            var imageMono = FilterMonotonicEquals(
                (imageEquals ?? Enumerable.Empty<EqualRange>())
                    .OrderBy(e => Math.Min(e.LeftStart, e.RightStart))
                    .ThenBy(e => e.LeftStart)
                    .ThenBy(e => e.RightStart));

            // 2) テキスト Equal は画像アンカーを壊さないものだけ採用
            var textCandidates = (textEquals ?? Enumerable.Empty<EqualRange>())
                .OrderBy(e => Math.Min(e.LeftStart, e.RightStart))
                .ThenBy(e => e.LeftStart)
                .ThenBy(e => e.RightStart)
                .ToList();

            var monoEquals = new List<EqualRange>();
            monoEquals.AddRange(imageMono);
            foreach (EqualRange te in textCandidates)
            {
                if (te == null)
                {
                    continue;
                }

                // 画像 Equal と行が重なる／交差するテキストは捨てる
                if (ConflictsWithAny(te, imageMono))
                {
                    continue;
                }

                monoEquals.Add(te);
            }

            // 3) 画像+テキストをまとめて再ソートし、全体で単調化
            //    （画像は既に mono なので、テキスト挿入で壊れる場合はテキスト側が落ちる）
            monoEquals = FilterMonotonicEqualsPreferring(
                monoEquals
                    .OrderBy(e => Math.Min(e.LeftStart, e.RightStart))
                    .ThenBy(e => e.LeftStart)
                    .ThenBy(e => e.RightStart)
                    .ToList(),
                preferred: imageMono);

            // 片側のみレンジを開始行でソート。Equal と重なる部分は除外
            var leftOnly = NormalizeOnlyRanges(leftOnlyRanges, monoEquals, isLeft: true);
            var rightOnly = NormalizeOnlyRanges(rightOnlyRanges, monoEquals, isLeft: false);

            _segments.Clear();
            int prevL = 1;
            int prevR = 1;
            int ai = 0;
            int li = 0;
            int ri = 0;

            while (ai < monoEquals.Count || li < leftOnly.Count || ri < rightOnly.Count)
            {
                int nextAnchorKey = ai < monoEquals.Count
                    ? Math.Min(monoEquals[ai].LeftStart, monoEquals[ai].RightStart)
                    : int.MaxValue;
                int nextLeftKey = li < leftOnly.Count ? leftOnly[li].Start : int.MaxValue;
                int nextRightKey = ri < rightOnly.Count ? rightOnly[ri].Start : int.MaxValue;

                if (ai < monoEquals.Count && nextAnchorKey <= nextLeftKey && nextAnchorKey <= nextRightKey)
                {
                    EqualRange eq = monoEquals[ai];
                    EmitBetween(prevL, eq.LeftStart - 1, prevR, eq.RightStart - 1);
                    _segments.Add(new Segment
                    {
                        Kind = SegKind.Equal,
                        LeftStart = eq.LeftStart,
                        LeftEnd = eq.LeftEnd,
                        RightStart = eq.RightStart,
                        RightEnd = eq.RightEnd
                    });
                    prevL = eq.LeftEnd + 1;
                    prevR = eq.RightEnd + 1;
                    ai++;
                    while (li < leftOnly.Count && leftOnly[li].End < prevL)
                    {
                        li++;
                    }

                    while (ri < rightOnly.Count && rightOnly[ri].End < prevR)
                    {
                        ri++;
                    }

                    continue;
                }

                if (li < leftOnly.Count && nextLeftKey <= nextRightKey)
                {
                    OnlyRange lo = leftOnly[li];
                    if (lo.End >= prevL)
                    {
                        int start = Math.Max(lo.Start, prevL);
                        if (start <= lo.End)
                        {
                            EmitBetween(prevL, start - 1, prevR, prevR - 1);
                            int hold = Math.Max(1, prevR - 1);
                            _segments.Add(new Segment
                            {
                                Kind = SegKind.LeftOnly,
                                LeftStart = start,
                                LeftEnd = lo.End,
                                RightStart = hold,
                                RightEnd = hold,
                                HoldRow = hold
                            });
                            prevL = lo.End + 1;
                        }
                    }

                    li++;
                    continue;
                }

                if (ri < rightOnly.Count)
                {
                    OnlyRange ro = rightOnly[ri];
                    if (ro.End >= prevR)
                    {
                        int start = Math.Max(ro.Start, prevR);
                        if (start <= ro.End)
                        {
                            EmitBetween(prevL, prevL - 1, prevR, start - 1);
                            int hold = Math.Max(1, prevL - 1);
                            _segments.Add(new Segment
                            {
                                Kind = SegKind.RightOnly,
                                LeftStart = hold,
                                LeftEnd = hold,
                                RightStart = start,
                                RightEnd = ro.End,
                                HoldRow = hold
                            });
                            prevR = ro.End + 1;
                        }
                    }

                    ri++;
                    continue;
                }

                break;
            }

            // 末尾 1:1
            _segments.Add(new Segment
            {
                Kind = SegKind.Equal,
                LeftStart = Math.Max(1, prevL),
                LeftEnd = int.MaxValue / 4,
                RightStart = Math.Max(1, prevR),
                RightEnd = int.MaxValue / 4
            });

            MergeAdjacentEquals();
        }

        /// <summary>
        /// 開始順の Equal 列から単調（左右とも厳密前進）なものだけ残す。
        /// </summary>
        private static List<EqualRange> FilterMonotonicEquals(IEnumerable<EqualRange> ordered)
        {
            var mono = new List<EqualRange>();
            int lastL = 0;
            int lastR = 0;
            foreach (EqualRange e in ordered ?? Enumerable.Empty<EqualRange>())
            {
                if (e == null || e.LeftEnd < e.LeftStart || e.RightEnd < e.RightStart)
                {
                    continue;
                }

                if (e.LeftStart <= lastL || e.RightStart <= lastR)
                {
                    continue;
                }

                mono.Add(e);
                lastL = e.LeftEnd;
                lastR = e.RightEnd;
            }

            return mono;
        }

        /// <summary>
        /// 単調フィルタだが、preferred（画像 Equal）と衝突する候補は落とす。
        /// preferred 自体は必ず残す（テキストより画像を優先）。
        /// </summary>
        private static List<EqualRange> FilterMonotonicEqualsPreferring(
            List<EqualRange> ordered,
            List<EqualRange> preferred)
        {
            var preferredSet = new HashSet<EqualRange>(preferred ?? Enumerable.Empty<EqualRange>());
            var mono = new List<EqualRange>();
            int lastL = 0;
            int lastR = 0;

            // preferred を先に確定配置
            foreach (EqualRange e in (preferred ?? Enumerable.Empty<EqualRange>())
                .OrderBy(p => Math.Min(p.LeftStart, p.RightStart))
                .ThenBy(p => p.LeftStart)
                .ThenBy(p => p.RightStart))
            {
                if (e == null)
                {
                    continue;
                }

                if (e.LeftStart <= lastL || e.RightStart <= lastR)
                {
                    // 画像同士の交差は稀。先勝ち。
                    continue;
                }

                mono.Add(e);
                lastL = e.LeftEnd;
                lastR = e.RightEnd;
            }

            // テキスト等: preferred 確定区間と矛盾しなければ挿入
            foreach (EqualRange e in ordered ?? Enumerable.Empty<EqualRange>())
            {
                if (e == null || preferredSet.Contains(e))
                {
                    continue;
                }

                if (ConflictsWithAny(e, mono))
                {
                    continue;
                }

                mono.Add(e);
            }

            return mono
                .OrderBy(e => Math.Min(e.LeftStart, e.RightStart))
                .ThenBy(e => e.LeftStart)
                .ThenBy(e => e.RightStart)
                .ToList();
        }

        /// <summary>
        /// Equal が既存アンカーと左右いずれかで重なる／単調性を壊すか。
        /// </summary>
        private static bool ConflictsWithAny(EqualRange candidate, List<EqualRange> anchors)
        {
            if (candidate == null || anchors == null || anchors.Count == 0)
            {
                return false;
            }

            foreach (EqualRange a in anchors)
            {
                if (a == null)
                {
                    continue;
                }

                // 行区間の重なり（左右どちらか）
                bool leftOverlap = !(candidate.LeftEnd < a.LeftStart || candidate.LeftStart > a.LeftEnd);
                bool rightOverlap = !(candidate.RightEnd < a.RightStart || candidate.RightStart > a.RightEnd);
                if (leftOverlap || rightOverlap)
                {
                    return true;
                }

                // 交差（L は前なのに R は後、またはその逆）
                bool crosses =
                    (candidate.LeftStart < a.LeftStart && candidate.RightStart > a.RightStart)
                    || (candidate.LeftStart > a.LeftStart && candidate.RightStart < a.RightStart);
                if (crosses)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Equal と重なる only を削り、開始順に整列。
        /// </summary>
        private static List<OnlyRange> NormalizeOnlyRanges(
            List<OnlyRange> ranges,
            List<EqualRange> equals,
            bool isLeft)
        {
            var result = new List<OnlyRange>();
            if (ranges == null)
            {
                return result;
            }

            foreach (OnlyRange raw in ranges.OrderBy(r => r.Start))
            {
                int start = raw.Start;
                int end = raw.End;
                if (end < start)
                {
                    continue;
                }

                // Equal 占有区間を only から除外
                foreach (EqualRange eq in equals)
                {
                    int eqStart = isLeft ? eq.LeftStart : eq.RightStart;
                    int eqEnd = isLeft ? eq.LeftEnd : eq.RightEnd;
                    if (end < eqStart || start > eqEnd)
                    {
                        continue;
                    }

                    // 重なりがある場合は前後に分割して残す
                    if (start < eqStart)
                    {
                        result.Add(new OnlyRange { Start = start, End = eqStart - 1 });
                    }

                    start = eqEnd + 1;
                }

                if (start <= end)
                {
                    result.Add(new OnlyRange { Start = start, End = end });
                }
            }

            return result.OrderBy(r => r.Start).ToList();
        }

        /// <summary>
        /// アンカー間の空白行を Equal（共通長）+ 余剰は only で埋める。
        /// </summary>
        private void EmitBetween(int leftFrom, int leftTo, int rightFrom, int rightTo)
        {
            if (leftTo < leftFrom && rightTo < rightFrom)
            {
                return;
            }

            if (leftTo >= leftFrom && rightTo >= rightFrom)
            {
                int lCount = leftTo - leftFrom + 1;
                int rCount = rightTo - rightFrom + 1;
                int common = Math.Min(lCount, rCount);
                if (common > 0)
                {
                    _segments.Add(new Segment
                    {
                        Kind = SegKind.Equal,
                        LeftStart = leftFrom,
                        LeftEnd = leftFrom + common - 1,
                        RightStart = rightFrom,
                        RightEnd = rightFrom + common - 1
                    });
                }

                if (lCount > common)
                {
                    int hold = rightFrom + common - 1;
                    if (hold < 1)
                    {
                        hold = 1;
                    }

                    _segments.Add(new Segment
                    {
                        Kind = SegKind.LeftOnly,
                        LeftStart = leftFrom + common,
                        LeftEnd = leftTo,
                        RightStart = hold,
                        RightEnd = hold,
                        HoldRow = hold
                    });
                }
                else if (rCount > common)
                {
                    int hold = leftFrom + common - 1;
                    if (hold < 1)
                    {
                        hold = 1;
                    }

                    _segments.Add(new Segment
                    {
                        Kind = SegKind.RightOnly,
                        LeftStart = hold,
                        LeftEnd = hold,
                        RightStart = rightFrom + common,
                        RightEnd = rightTo,
                        HoldRow = hold
                    });
                }

                return;
            }

            if (leftTo >= leftFrom)
            {
                int hold = Math.Max(1, rightFrom - 1);
                _segments.Add(new Segment
                {
                    Kind = SegKind.LeftOnly,
                    LeftStart = leftFrom,
                    LeftEnd = leftTo,
                    RightStart = hold,
                    RightEnd = hold,
                    HoldRow = hold
                });
            }
            else if (rightTo >= rightFrom)
            {
                int hold = Math.Max(1, leftFrom - 1);
                _segments.Add(new Segment
                {
                    Kind = SegKind.RightOnly,
                    LeftStart = hold,
                    LeftEnd = hold,
                    RightStart = rightFrom,
                    RightEnd = rightTo,
                    HoldRow = hold
                });
            }
        }

        private void MergeAdjacentEquals()
        {
            if (_segments.Count < 2)
            {
                return;
            }

            var merged = new List<Segment>();
            Segment cur = _segments[0];
            for (int i = 1; i < _segments.Count; i++)
            {
                Segment next = _segments[i];
                bool canMerge = cur.Kind == SegKind.Equal && next.Kind == SegKind.Equal
                    && cur.LeftEnd + 1 == next.LeftStart
                    && cur.RightEnd + 1 == next.RightStart
                    && (cur.LeftEnd - cur.LeftStart) == (cur.RightEnd - cur.RightStart)
                    && (next.LeftEnd - next.LeftStart) == (next.RightEnd - next.RightStart);
                if (canMerge)
                {
                    cur = new Segment
                    {
                        Kind = SegKind.Equal,
                        LeftStart = cur.LeftStart,
                        LeftEnd = next.LeftEnd,
                        RightStart = cur.RightStart,
                        RightEnd = next.RightEnd
                    };
                }
                else
                {
                    merged.Add(cur);
                    cur = next;
                }
            }

            merged.Add(cur);
            _segments.Clear();
            _segments.AddRange(merged);
        }
    }

    /// <summary>
    /// シート対応ごとのスクロールマップ集合。
    /// </summary>
    public sealed class ContentScrollMapSet
    {
        private readonly List<ContentScrollMap> _maps = new List<ContentScrollMap>();

        public void Add(ContentScrollMap map)
        {
            if (map != null)
            {
                _maps.Add(map);
            }
        }

        /// <summary>
        /// 左右シート名の完全一致のみで解決する。未対応は null。
        /// </summary>
        public ContentScrollMap ResolveExact(string leftSheet, string rightSheet)
        {
            if (_maps.Count == 0
                || string.IsNullOrEmpty(leftSheet)
                || string.IsNullOrEmpty(rightSheet))
            {
                return null;
            }

            return _maps.FirstOrDefault(m =>
                m != null
                && string.Equals(m.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase)
                && string.Equals(m.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase));
        }

        public ContentScrollMap Resolve(string leftSheet, string rightSheet)
        {
            if (_maps.Count == 0)
            {
                return ContentScrollMap.Identity;
            }

            ContentScrollMap exact = ResolveExact(leftSheet, rightSheet);
            if (exact != null)
            {
                return exact;
            }

            // 片方のみ指定時の後方互換フォールバック
            ContentScrollMap hit = _maps.FirstOrDefault(m =>
                (string.IsNullOrEmpty(leftSheet) || string.Equals(m.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(rightSheet) || string.Equals(m.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase)));
            if (hit != null)
            {
                return hit;
            }

            if (!string.IsNullOrEmpty(leftSheet) && string.IsNullOrEmpty(rightSheet))
            {
                hit = _maps.FirstOrDefault(m =>
                    string.Equals(m.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                {
                    return hit;
                }
            }

            if (!string.IsNullOrEmpty(rightSheet) && string.IsNullOrEmpty(leftSheet))
            {
                hit = _maps.FirstOrDefault(m =>
                    string.Equals(m.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                {
                    return hit;
                }
            }

            // 両シート指定で完全一致なし → 誤った別シートへ落とさない
            if (!string.IsNullOrEmpty(leftSheet) && !string.IsNullOrEmpty(rightSheet))
            {
                return null;
            }

            return _maps[0];
        }

        public int Count
        {
            get { return _maps.Count; }
        }

        /// <summary>
        /// Alignments からマップ集合を構築する。
        /// </summary>
        public static ContentScrollMapSet FromAlignments(IEnumerable<SheetAlignment> alignments)
        {
            var set = new ContentScrollMapSet();
            if (alignments == null)
            {
                return set;
            }

            foreach (SheetAlignment a in alignments)
            {
                if (a != null && a.ScrollMap != null)
                {
                    set.Add(a.ScrollMap);
                }
            }

            return set;
        }
    }
}
