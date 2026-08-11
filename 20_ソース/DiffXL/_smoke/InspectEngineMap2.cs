using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;
using DiffXL.COMMON;
// Replicate DiffEngine image extract + map for catalog
class P {
  static void Main() {
    string left = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";
    string right = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_right.xlsx";
    string cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"dxl_dbg");
    using (var lr = XlsxPackageReader.Open(left))
    using (var rr = XlsxPackageReader.Open(right)) {
      var leftAll = lr.ExtractImages(null, System.IO.Path.Combine(cache,"L")).ToList();
      var rightAll = rr.ExtractImages(null, System.IO.Path.Combine(cache,"R")).ToList();
      Console.WriteLine("ALL L="+leftAll.Count+" R="+rightAll.Count);
      foreach (var i in leftAll) Console.WriteLine("L sheet="+i.SheetName+" row="+i.AnchorRow+" "+i.FileName+" "+i.PixelWidth+"x"+i.PixelHeight);
      foreach (var i in rightAll) Console.WriteLine("R sheet="+i.SheetName+" row="+i.AnchorRow+" "+i.FileName+" "+i.PixelWidth+"x"+i.PixelHeight);
      var leftImages = leftAll.Where(i => string.Equals(i.SheetName, "製品カタログ", StringComparison.OrdinalIgnoreCase)).ToList();
      var rightImages = rightAll.Where(i => string.Equals(i.SheetName, "製品カタログ", StringComparison.OrdinalIgnoreCase)).ToList();
      Console.WriteLine("FILTERED L="+leftImages.Count+" R="+rightImages.Count);
      var leftCells = lr.EnumerateCells("製品カタログ").ToList();
      var rightCells = rr.EnumerateCells("製品カタログ").ToList();
      Console.WriteLine("CELLS L="+leftCells.Count+" R="+rightCells.Count);
      foreach (var c in leftCells.Take(20)) Console.WriteLine("  "+c.Address+" "+c.Text);
      var map = ContentScrollMap.Build("製品カタログ","製品カタログ", leftCells, rightCells, leftImages, rightImages);
      Console.WriteLine(map.Describe());
      for (int i=4;i<=8;i++) Console.WriteLine("L"+i+"->R"+map.MapLeftToRight(i)+" R"+i+"->L"+map.MapRightToLeft(i));
    }
  }
}
