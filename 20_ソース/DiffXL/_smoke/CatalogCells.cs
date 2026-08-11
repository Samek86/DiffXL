using System;
using System.Linq;
using DiffXL.LOGIC.Diff;
class P {
  static void Main() {
    string left = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";
    string right = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_right.xlsx";
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
}
