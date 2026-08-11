using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

/// <summary>
/// T1/T2: content_scroll サンプルの DiffEngine + ScrollMap + 画像対応を expected.json で検証。
/// COM 不要。CLI: --left --right --expected（未指定時は samples 既定パス）。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        string root = FindRepoRoot();
        string samples = Path.Combine(root, "30_参考資料", "samples");
        string left = GetArg(args, "--left")
            ?? Path.Combine(samples, "content_scroll_left.xlsx");
        string right = GetArg(args, "--right")
            ?? Path.Combine(samples, "content_scroll_right.xlsx");
        string expectedPath = GetArg(args, "--expected")
            ?? Path.Combine(samples, "content_scroll_expected.json");

        Console.WriteLine("ContentScrollPerfectSmoke");
        Console.WriteLine("left=" + left);
        Console.WriteLine("right=" + right);
        Console.WriteLine("expected=" + expectedPath);

        if (!File.Exists(left) || !File.Exists(right))
        {
            Console.WriteLine("FAIL sample xlsx missing");
            Console.WriteLine("PERFECT_SCROLL_FAIL");
            return 1;
        }

        if (!File.Exists(expectedPath))
        {
            Console.WriteLine("FAIL expected.json missing");
            Console.WriteLine("PERFECT_SCROLL_FAIL");
            return 1;
        }

        ExpectedDoc expected;
        try
        {
            expected = ExpectedDoc.Parse(File.ReadAllText(expectedPath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL expected.json parse: " + ex.Message);
            Console.WriteLine("PERFECT_SCROLL_FAIL");
            return 1;
        }

        AppPaths.EnsureDirectories();
        NativeBootstrap.EnsureNativeBinaries();

        var engine = new DiffEngine();
        DiffResult result = engine.Compare(left, right);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine("FAIL DiffEngine: " + result.ErrorMessage);
            Console.WriteLine("PERFECT_SCROLL_FAIL");
            return 1;
        }

        int alignments = result.Alignments != null ? result.Alignments.Count : 0;
        Console.WriteLine("COMPARE_OK items=" + result.Items.Count
            + " alignments=" + alignments
            + " elapsedMs=" + (int)result.Elapsed.TotalMilliseconds);

        if (alignments < 4)
        {
            Console.WriteLine("FAIL Alignments.Count expected >= 4 got " + alignments);
            Console.WriteLine("PERFECT_SCROLL_FAIL");
            return 1;
        }

        int fail = 0;
        foreach (KeyValuePair<string, ExpectedSheet> kv in expected.Sheets)
        {
            string sheetName = kv.Key;
            ExpectedSheet exp = kv.Value;
            Console.WriteLine("=== sheet " + sheetName + " ===");

            SheetAlignment al = result.Alignments.FirstOrDefault(a =>
                string.Equals(a.LeftSheet, sheetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.RightSheet, sheetName, StringComparison.OrdinalIgnoreCase));
            if (al == null)
            {
                Console.WriteLine("FAIL no SheetAlignment for " + sheetName);
                fail++;
                continue;
            }

            if (al.ScrollMap == null)
            {
                Console.WriteLine("FAIL ScrollMap null for " + sheetName);
                fail++;
                continue;
            }

            Console.WriteLine(al.ScrollMap.Describe());
            fail += VerifyScrollSamples(sheetName, al.ScrollMap, exp.ScrollSamples);
            fail += VerifyImagePairs(sheetName, al.Images, exp);
        }

        if (fail == 0)
        {
            Console.WriteLine("PERFECT_SCROLL_PASS");
            return 0;
        }

        Console.WriteLine("PERFECT_SCROLL_FAIL failures=" + fail);
        return 1;
    }

    static int VerifyScrollSamples(string sheet, ContentScrollMap map, List<ScrollSample> samples)
    {
        int fail = 0;
        if (samples == null || samples.Count == 0)
        {
            Console.WriteLine("  (no scrollSamples)");
            return 0;
        }

        foreach (ScrollSample s in samples)
        {
            string from = (s.From ?? "L").Trim().ToUpperInvariant();
            int actual;
            string dir;
            if (from == "R")
            {
                actual = map.MapRightToLeft(s.Row);
                dir = "R" + s.Row + "->L";
            }
            else
            {
                actual = map.MapLeftToRight(s.Row);
                dir = "L" + s.Row + "->R";
            }

            if (s.ExpectOther.HasValue)
            {
                if (actual != s.ExpectOther.Value)
                {
                    Console.WriteLine("FAIL " + sheet + " " + dir + " got=" + actual
                        + " expectOther=" + s.ExpectOther.Value
                        + (string.IsNullOrEmpty(s.Note) ? "" : " (" + s.Note + ")"));
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK " + sheet + " " + dir + actual
                        + (string.IsNullOrEmpty(s.Note) ? "" : " // " + s.Note));
                }
            }
            else if (s.ExpectOtherMax.HasValue)
            {
                if (actual > s.ExpectOtherMax.Value)
                {
                    Console.WriteLine("FAIL " + sheet + " " + dir + " got=" + actual
                        + " expectOtherMax=" + s.ExpectOtherMax.Value
                        + (string.IsNullOrEmpty(s.Note) ? "" : " (" + s.Note + ")"));
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK " + sheet + " " + dir + actual
                        + " <= " + s.ExpectOtherMax.Value
                        + (string.IsNullOrEmpty(s.Note) ? "" : " // " + s.Note));
                }
            }
            else
            {
                Console.WriteLine("FAIL " + sheet + " sample missing expectOther/expectOtherMax row=" + s.Row);
                fail++;
            }
        }

        return fail;
    }

    static int VerifyImagePairs(string sheet, IList<ImageCorrespondence> images, ExpectedSheet exp)
    {
        int fail = 0;
        List<ImageCorrespondence> list = images != null
            ? images.ToList()
            : new List<ImageCorrespondence>();

        if (exp.ImagePairs != null)
        {
            foreach (ImagePairExpect p in exp.ImagePairs)
            {
                ImageCorrespondence hit = list.FirstOrDefault(c =>
                    c.IsPaired
                    && RowStart(c.Left) == p.LeftRowStart
                    && RowStart(c.Right) == p.RightRowStart);
                if (hit == null)
                {
                    Console.WriteLine("FAIL " + sheet + " imagePair L" + p.LeftRowStart
                        + "↔R" + p.RightRowStart + " not found in Alignments.Images");
                    fail++;
                    DumpImages(list);
                    continue;
                }

                string kind = (p.Kind ?? "exact").Trim().ToLowerInvariant();
                if (kind == "exact")
                {
                    if (!hit.IsExactHashMatch)
                    {
                        Console.WriteLine("FAIL " + sheet + " L" + p.LeftRowStart + "↔R" + p.RightRowStart
                            + " expected exact hash, DiffRatio=" + hit.DiffRatio);
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine("OK " + sheet + " imagePair L" + p.LeftRowStart
                            + "↔R" + p.RightRowStart + " exact");
                    }
                }
                else if (kind == "modified" || kind == "paired" || kind == "diff")
                {
                    if (!hit.IsPaired || hit.IsExactHashMatch)
                    {
                        Console.WriteLine("FAIL " + sheet + " L" + p.LeftRowStart + "↔R" + p.RightRowStart
                            + " expected modified pair, exact=" + hit.IsExactHashMatch
                            + " dr=" + hit.DiffRatio);
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine("OK " + sheet + " imagePair L" + p.LeftRowStart
                            + "↔R" + p.RightRowStart + " modified dr=" + hit.DiffRatio.ToString("0.###"));
                    }
                }
                else
                {
                    Console.WriteLine("OK " + sheet + " imagePair L" + p.LeftRowStart
                        + "↔R" + p.RightRowStart + " kind=" + kind);
                }
            }
        }

        if (exp.LeftOnly != null)
        {
            foreach (SideOnlyExpect o in exp.LeftOnly)
            {
                bool found = list.Any(c => c.IsLeftOnly && RowStart(c.Left) == o.LeftRowStart);
                if (!found)
                {
                    Console.WriteLine("FAIL " + sheet + " leftOnly L" + o.LeftRowStart + " missing");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK " + sheet + " leftOnly L" + o.LeftRowStart);
                }
            }
        }

        if (exp.RightOnly != null)
        {
            foreach (SideOnlyExpect o in exp.RightOnly)
            {
                bool found = list.Any(c => c.IsRightOnly && RowStart(c.Right) == o.RightRowStart);
                if (!found)
                {
                    Console.WriteLine("FAIL " + sheet + " rightOnly R" + o.RightRowStart + " missing");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK " + sheet + " rightOnly R" + o.RightRowStart);
                }
            }
        }

        return fail;
    }

    static int RowStart(EmbeddedImage img)
    {
        if (img == null)
        {
            return -1;
        }

        if (img.Anchor != null && img.Anchor.RowStart >= 1)
        {
            return img.Anchor.RowStart;
        }

        return img.AnchorRow > 0 ? img.AnchorRow : -1;
    }

    static void DumpImages(List<ImageCorrespondence> list)
    {
        foreach (ImageCorrespondence c in list)
        {
            Console.WriteLine("    actual L" + RowStart(c.Left) + " R" + RowStart(c.Right)
                + " exact=" + c.IsExactHashMatch + " LO=" + c.IsLeftOnly + " RO=" + c.IsRightOnly
                + " dr=" + c.DiffRatio);
        }
    }

    static string GetArg(string[] args, string name)
    {
        if (args == null)
        {
            return null;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    static string FindRepoRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "30_参考資料", "samples")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir) != null
                ? Directory.GetParent(dir).FullName
                : null;
        }

        return Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    // ---- expected.json 最小パーサ（既知スキーマ専用）----

    sealed class ExpectedDoc
    {
        public Dictionary<string, ExpectedSheet> Sheets { get; set; }

        public static ExpectedDoc Parse(string json)
        {
            var doc = new ExpectedDoc
            {
                Sheets = new Dictionary<string, ExpectedSheet>(StringComparer.OrdinalIgnoreCase)
            };

            // "sheets": { ... } ブロックを切り出す
            Match sheetsMatch = Regex.Match(json, "\"sheets\"\\s*:\\s*\\{", RegexOptions.CultureInvariant);
            if (!sheetsMatch.Success)
            {
                throw new InvalidOperationException("sheets object not found");
            }

            int start = sheetsMatch.Index + sheetsMatch.Length - 1; // at '{'
            string sheetsObj = ExtractObject(json, start);

            // 各シート: "NAME": { ... }
            int i = 1;
            while (i < sheetsObj.Length)
            {
                SkipWs(sheetsObj, ref i);
                if (i >= sheetsObj.Length || sheetsObj[i] == '}')
                {
                    break;
                }

                if (sheetsObj[i] == ',')
                {
                    i++;
                    continue;
                }

                string name = ReadString(sheetsObj, ref i);
                SkipWs(sheetsObj, ref i);
                if (i >= sheetsObj.Length || sheetsObj[i] != ':')
                {
                    throw new InvalidOperationException("expected ':' after sheet name " + name);
                }

                i++;
                SkipWs(sheetsObj, ref i);
                string body = ExtractObject(sheetsObj, i);
                i += body.Length;
                doc.Sheets[name] = ExpectedSheet.Parse(body);
            }

            return doc;
        }
    }

    sealed class ExpectedSheet
    {
        public List<ImagePairExpect> ImagePairs { get; set; }
        public List<SideOnlyExpect> LeftOnly { get; set; }
        public List<SideOnlyExpect> RightOnly { get; set; }
        public List<ScrollSample> ScrollSamples { get; set; }

        public static ExpectedSheet Parse(string obj)
        {
            var s = new ExpectedSheet
            {
                ImagePairs = new List<ImagePairExpect>(),
                LeftOnly = new List<SideOnlyExpect>(),
                RightOnly = new List<SideOnlyExpect>(),
                ScrollSamples = new List<ScrollSample>()
            };

            foreach (string item in ExtractArrayObjects(obj, "imagePairs"))
            {
                s.ImagePairs.Add(new ImagePairExpect
                {
                    LeftRowStart = ReadIntProp(item, "leftRowStart"),
                    RightRowStart = ReadIntProp(item, "rightRowStart"),
                    Kind = ReadStringProp(item, "kind")
                });
            }

            foreach (string item in ExtractArrayObjects(obj, "leftOnly"))
            {
                s.LeftOnly.Add(new SideOnlyExpect
                {
                    LeftRowStart = ReadIntProp(item, "leftRowStart"),
                    Kind = ReadStringProp(item, "kind")
                });
            }

            foreach (string item in ExtractArrayObjects(obj, "rightOnly"))
            {
                s.RightOnly.Add(new SideOnlyExpect
                {
                    RightRowStart = ReadIntProp(item, "rightRowStart"),
                    Kind = ReadStringProp(item, "kind")
                });
            }

            foreach (string item in ExtractArrayObjects(obj, "scrollSamples"))
            {
                var sample = new ScrollSample
                {
                    From = ReadStringProp(item, "from") ?? "L",
                    Row = ReadIntProp(item, "row"),
                    Note = ReadStringProp(item, "note")
                };
                if (HasProp(item, "expectOther"))
                {
                    sample.ExpectOther = ReadIntProp(item, "expectOther");
                }

                if (HasProp(item, "expectOtherMax"))
                {
                    sample.ExpectOtherMax = ReadIntProp(item, "expectOtherMax");
                }

                s.ScrollSamples.Add(sample);
            }

            return s;
        }
    }

    sealed class ImagePairExpect
    {
        public int LeftRowStart;
        public int RightRowStart;
        public string Kind;
    }

    sealed class SideOnlyExpect
    {
        public int LeftRowStart;
        public int RightRowStart;
        public string Kind;
    }

    sealed class ScrollSample
    {
        public string From;
        public int Row;
        public int? ExpectOther;
        public int? ExpectOtherMax;
        public string Note;
    }

    static string ExtractObject(string text, int openBraceIndex)
    {
        if (openBraceIndex < 0 || openBraceIndex >= text.Length || text[openBraceIndex] != '{')
        {
            throw new InvalidOperationException("ExtractObject expects '{'");
        }

        int depth = 0;
        bool inStr = false;
        bool esc = false;
        for (int i = openBraceIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (inStr)
            {
                if (esc)
                {
                    esc = false;
                }
                else if (c == '\\')
                {
                    esc = true;
                }
                else if (c == '"')
                {
                    inStr = false;
                }

                continue;
            }

            if (c == '"')
            {
                inStr = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(openBraceIndex, i - openBraceIndex + 1);
                }
            }
        }

        throw new InvalidOperationException("unbalanced object");
    }

    static List<string> ExtractArrayObjects(string parentObj, string arrayName)
    {
        var result = new List<string>();
        Match m = Regex.Match(parentObj, "\"" + Regex.Escape(arrayName) + "\"\\s*:\\s*\\[", RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return result;
        }

        int i = m.Index + m.Length;
        while (i < parentObj.Length)
        {
            SkipWs(parentObj, ref i);
            if (i >= parentObj.Length)
            {
                break;
            }

            if (parentObj[i] == ']')
            {
                break;
            }

            if (parentObj[i] == ',')
            {
                i++;
                continue;
            }

            if (parentObj[i] == '{')
            {
                string obj = ExtractObject(parentObj, i);
                result.Add(obj);
                i += obj.Length;
            }
            else
            {
                // スカラー要素はスキップ
                while (i < parentObj.Length && parentObj[i] != ',' && parentObj[i] != ']')
                {
                    i++;
                }
            }
        }

        return result;
    }

    static bool HasProp(string obj, string name)
    {
        return Regex.IsMatch(obj, "\"" + Regex.Escape(name) + "\"\\s*:", RegexOptions.CultureInvariant);
    }

    static int ReadIntProp(string obj, string name)
    {
        Match m = Regex.Match(obj, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return 0;
        }

        return int.Parse(m.Groups[1].Value);
    }

    static string ReadStringProp(string obj, string name)
    {
        Match m = Regex.Match(obj, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return null;
        }

        return Unescape(m.Groups[1].Value);
    }

    static string ReadString(string text, ref int i)
    {
        SkipWs(text, ref i);
        if (i >= text.Length || text[i] != '"')
        {
            throw new InvalidOperationException("expected string at " + i);
        }

        i++;
        var sb = new StringBuilder();
        bool esc = false;
        while (i < text.Length)
        {
            char c = text[i++];
            if (esc)
            {
                sb.Append(c);
                esc = false;
                continue;
            }

            if (c == '\\')
            {
                esc = true;
                continue;
            }

            if (c == '"')
            {
                return sb.ToString();
            }

            sb.Append(c);
        }

        throw new InvalidOperationException("unterminated string");
    }

    static string Unescape(string s)
    {
        return s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\t", "\t");
    }

    static void SkipWs(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }
    }
}
