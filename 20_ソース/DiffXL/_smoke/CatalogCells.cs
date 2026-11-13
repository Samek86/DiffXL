using System;
using System.Linq;
using DiffXL.LOGIC.Diff;
class P {
  static void Main() {
    string left = Sample("full_feature_left.xlsx");
    string right = Sample("full_feature_right.xlsx");
    using (var lr = XlsxPackageReader.Open(left))
    using (var rr = XlsxPackageReader.Open(right)) {
      Console.WriteLine("LEFT");
      foreach (var c in lr.EnumerateCells("製品カタログ").OrderBy(c=>c.Row).ThenBy(c=>c.Column))
        Console.WriteLine(c.Address+" ["+c.Text+"]");
      Console.WriteLine("RIGHT");
      foreach (var c in rr.EnumerateCells("製品カタログ").OrderBy(c=>c.Row).ThenBy(c=>c.Column))
        Console.WriteLine(c.Address+" ["+c.Text+"]");
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
