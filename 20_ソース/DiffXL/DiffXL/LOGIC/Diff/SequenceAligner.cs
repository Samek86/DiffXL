using System;
using System.Collections.Generic;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 左右系列の汎用 DP アラインメント（Match / SkipLeft / SkipRight）。
    /// 類似度行列と閾値に基づき、挿入・削除を許した最適対応を求める。
    /// </summary>
    public static class SequenceAligner
    {
        /// <summary>
        /// 左系列長 leftCount × 右系列長 rightCount の類似度（0..1）と閾値で編集アラインメントを行う。
        /// Match の報酬は similarity、Skip は固定コスト skipCost。
        /// similarity &lt; matchThreshold のペアは Match 不可。
        /// </summary>
        /// <param name="leftCount">左系列の要素数（0 以上）</param>
        /// <param name="rightCount">右系列の要素数（0 以上）</param>
        /// <param name="similarity">(leftIndex, rightIndex) → 類似度 0..1</param>
        /// <param name="matchThreshold">この値未満の類似度では Match しない</param>
        /// <param name="skipCost">SkipLeft / SkipRight 1 回あたりのコスト（既定 0.4）</param>
        /// <returns>先頭から末尾への AlignStep 列</returns>
        public static IList<AlignStep> Align(
            int leftCount,
            int rightCount,
            Func<int, int, double> similarity,
            double matchThreshold,
            double skipCost = 0.4)
        {
            if (leftCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(leftCount));
            }

            if (rightCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rightCount));
            }

            if (similarity == null)
            {
                throw new ArgumentNullException(nameof(similarity));
            }

            int n = leftCount;
            int m = rightCount;

            // score[i,j] = 左 0..i-1 と右 0..j-1 をアラインした最大スコア
            var score = new double[n + 1, m + 1];
            // バックトラック用: 0=開始, 1=Match, 2=SkipLeft, 3=SkipRight
            var prev = new byte[n + 1, m + 1];

            score[0, 0] = 0.0;
            prev[0, 0] = 0;

            for (int i = 1; i <= n; i++)
            {
                score[i, 0] = -skipCost * i;
                prev[i, 0] = 2; // SkipLeft
            }

            for (int j = 1; j <= m; j++)
            {
                score[0, j] = -skipCost * j;
                prev[0, j] = 3; // SkipRight
            }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    // 既定は SkipLeft（同点時は Match を優先するため後で上書き）
                    double best = score[i - 1, j] - skipCost;
                    byte bestOp = 2; // SkipLeft

                    double skipRightScore = score[i, j - 1] - skipCost;
                    if (skipRightScore > best)
                    {
                        best = skipRightScore;
                        bestOp = 3; // SkipRight
                    }

                    double sim = similarity(i - 1, j - 1);
                    if (sim >= matchThreshold)
                    {
                        // matchReward = similarity
                        double matchScore = score[i - 1, j - 1] + sim;
                        // 同点では Match を優先（安定した対応を残す）
                        if (matchScore >= best)
                        {
                            best = matchScore;
                            bestOp = 1; // Match
                        }
                    }

                    score[i, j] = best;
                    prev[i, j] = bestOp;
                }
            }

            // バックトラック（末尾から先頭へ）
            var reverse = new List<AlignStep>(n + m);
            int ci = n;
            int cj = m;
            while (ci > 0 || cj > 0)
            {
                byte op = prev[ci, cj];
                if (op == 1)
                {
                    reverse.Add(new AlignStep
                    {
                        Op = AlignOp.Match,
                        LeftIndex = ci - 1,
                        RightIndex = cj - 1
                    });
                    ci--;
                    cj--;
                }
                else if (op == 2)
                {
                    reverse.Add(new AlignStep
                    {
                        Op = AlignOp.SkipLeft,
                        LeftIndex = ci - 1,
                        RightIndex = -1
                    });
                    ci--;
                }
                else if (op == 3)
                {
                    reverse.Add(new AlignStep
                    {
                        Op = AlignOp.SkipRight,
                        LeftIndex = -1,
                        RightIndex = cj - 1
                    });
                    cj--;
                }
                else
                {
                    // 防御: 到達不能だがループ防止
                    break;
                }
            }

            reverse.Reverse();
            return reverse;
        }
    }
}
