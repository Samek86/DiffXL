using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// SequenceAligner のスモーク。
/// 左 8 / 右 9 で右 index 4 のみ余分 → Match×4, SkipRight, Match×4。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        // sim(i,j)=1 if i==j for j&lt;4; sim(i,j)=1 if i==j-1 for j&gt;=5; else 0
        Func<int, int, double> sim = (i, j) =>
        {
            if (j < 4)
            {
                return i == j ? 1.0 : 0.0;
            }

            if (j >= 5)
            {
                return i == j - 1 ? 1.0 : 0.0;
            }

            // j == 4: 余分な右要素。どの左とも一致しない
            return 0.0;
        };

        IList<AlignStep> steps = SequenceAligner.Align(
            leftCount: 8,
            rightCount: 9,
            similarity: sim,
            matchThreshold: 0.5,
            skipCost: 0.4);

        Console.WriteLine("steps=" + steps.Count);
        for (int k = 0; k < steps.Count; k++)
        {
            AlignStep s = steps[k];
            Console.WriteLine(string.Format(
                "  [{0}] {1} L={2} R={3}",
                k,
                s.Op,
                s.LeftIndex,
                s.RightIndex));
        }

        // 期待: Match x4, SkipRight, Match x4 → 計 9 ステップ
        var expectedOps = new[]
        {
            AlignOp.Match, AlignOp.Match, AlignOp.Match, AlignOp.Match,
            AlignOp.SkipRight,
            AlignOp.Match, AlignOp.Match, AlignOp.Match, AlignOp.Match
        };

        int fail = 0;

        if (steps.Count != expectedOps.Length)
        {
            Console.WriteLine("FAIL count expected=" + expectedOps.Length + " actual=" + steps.Count);
            fail++;
        }
        else
        {
            for (int k = 0; k < expectedOps.Length; k++)
            {
                if (steps[k].Op != expectedOps[k])
                {
                    Console.WriteLine("FAIL op[" + k + "] expected=" + expectedOps[k] + " actual=" + steps[k].Op);
                    fail++;
                }
            }
        }

        // インデックス検証: Match は (0,0)(1,1)(2,2)(3,3) / SkipRight R=4 / Match (4,5)(5,6)(6,7)(7,8)
        var expectedPairs = new[]
        {
            Tuple.Create(0, 0),
            Tuple.Create(1, 1),
            Tuple.Create(2, 2),
            Tuple.Create(3, 3),
            Tuple.Create(-1, 4),
            Tuple.Create(4, 5),
            Tuple.Create(5, 6),
            Tuple.Create(6, 7),
            Tuple.Create(7, 8)
        };

        if (steps.Count == expectedPairs.Length)
        {
            for (int k = 0; k < expectedPairs.Length; k++)
            {
                int el = expectedPairs[k].Item1;
                int er = expectedPairs[k].Item2;
                if (steps[k].LeftIndex != el || steps[k].RightIndex != er)
                {
                    Console.WriteLine(string.Format(
                        "FAIL index[{0}] expected L={1} R={2} actual L={3} R={4}",
                        k, el, er, steps[k].LeftIndex, steps[k].RightIndex));
                    fail++;
                }
            }
        }

        // 空系列
        IList<AlignStep> empty = SequenceAligner.Align(0, 0, (i, j) => 0, 0.5);
        if (empty.Count != 0)
        {
            Console.WriteLine("FAIL empty Align should be empty");
            fail++;
        }
        else
        {
            Console.WriteLine("OK empty");
        }

        // 片側のみ
        IList<AlignStep> leftOnly = SequenceAligner.Align(2, 0, (i, j) => 0, 0.5);
        if (leftOnly.Count != 2
            || leftOnly.Any(s => s.Op != AlignOp.SkipLeft)
            || leftOnly[0].LeftIndex != 0
            || leftOnly[1].LeftIndex != 1)
        {
            Console.WriteLine("FAIL left-only SkipLeft x2");
            fail++;
        }
        else
        {
            Console.WriteLine("OK left-only");
        }

        IList<AlignStep> rightOnly = SequenceAligner.Align(0, 2, (i, j) => 0, 0.5);
        if (rightOnly.Count != 2
            || rightOnly.Any(s => s.Op != AlignOp.SkipRight)
            || rightOnly[0].RightIndex != 0
            || rightOnly[1].RightIndex != 1)
        {
            Console.WriteLine("FAIL right-only SkipRight x2");
            fail++;
        }
        else
        {
            Console.WriteLine("OK right-only");
        }

        if (fail > 0)
        {
            Console.WriteLine("FAIL SequenceAlignerSmoke fails=" + fail);
            return 1;
        }

        Console.WriteLine("PASS SequenceAlignerSmoke");
        return 0;
    }
}
