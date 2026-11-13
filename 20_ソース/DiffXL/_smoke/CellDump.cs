using System;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

class CellDump {
  static void Main() {
    string L = Sample("full_feature_left.xlsx");
    string R = Sample("full_feature_right.xlsx");
    using (var l = XlsxPackageReader.Open(L))
    using (var r = XlsxPackageReader.Open(R)) {
      Console.WriteLine("Lsheets=" + string.Join("|", l.GetSheetNames()));
      var lc = l.EnumerateCells("売上サマリ").ToDictionary(c => c.Address, c => c.Text, StringComparer.OrdinalIgnoreCase);
      var rc = r.EnumerateCells("売上サマリ").ToDictionary(c => c.Address, c => c.Text, StringComparer.OrdinalIgnoreCase);
      foreach (var a in new[]{"E5","F5","G5","E7","F7","B16","B17","E10"}) {
        string lv, rv; lc.TryGetValue(a, out lv); rc.TryGetValue(a, out rv);
        Console.WriteLine(a + " L=[" + lv + "] R=[" + rv + "] eq=" + (lv==rv));
      }
      int diffs = 0;
      foreach (var kv in lc) {
        string rv; rc.TryGetValue(kv.Key, out rv);
        if ((kv.Value ?? "") != (rv ?? "")) { diffs++; if (diffs<=10) Console.WriteLine("DIFF " + kv.Key + " " + kv.Value + " => " + rv); }
      }
      Console.WriteLine("TOTAL_TEXT_DIFFS_IN_MAP=" + diffs + " Lcount=" + lc.Count + " Rcount=" + rc.Count);
    }
  }

    static string Sample(string name)
    {
        string rel = System.IO.Path.Combine("30_参考資料", "samples", name);
        string[] starts = new string[]
        {
            System.Environment.CurrentDirectory,
            System.AppDomain.CurrentDomain.BaseDirectory
        };
        for (int i = 0; i < starts.Length; i++)
        {
            System.IO.DirectoryInfo dir = new System.IO.DirectoryInfo(starts[i]);
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, rel);
                if (System.IO.File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return rel;
    }

}
