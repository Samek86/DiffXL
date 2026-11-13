using System;
using System.Linq;
using DiffXL.LOGIC.Diff;
class P {
  static void Main() {
    var r = new DiffEngine().Compare(
      Sample("full_feature_left.xlsx"),
      Sample("full_feature_right.xlsx"));
    var m = r.ScrollMaps.Resolve("製品カタログ","製品カタログ");
    Console.WriteLine(m.Describe());
    for (int i=4;i<=8;i++) Console.WriteLine("L"+i+"->R"+m.MapLeftToRight(i)+" R"+i+"->L"+m.MapRightToLeft(i));
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
