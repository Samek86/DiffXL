using System;
using System.Linq;
using DiffXL.LOGIC.Diff;

class P {
  static void Main() {
    string left = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";
    string right = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_right.xlsx";
    using (var lr = XlsxPackageReader.Open(left))
    using (var rr = XlsxPackageReader.Open(right)) {
      var lc = lr.EnumerateCells("ずれ試験").ToList();
      var rc = rr.EnumerateCells("ずれ試験").ToList();
      Console.WriteLine("LEFT cells "+lc.Count);
      foreach (var c in lc) Console.WriteLine("  "+c.Address+" r="+c.Row+" c="+c.Column+" t=["+c.Text+"]");
      Console.WriteLine("RIGHT cells "+rc.Count);
      foreach (var c in rc) Console.WriteLine("  "+c.Address+" r="+c.Row+" c="+c.Column+" t=["+c.Text+"]");
      var map = ContentScrollMap.Build("ずれ試験","ずれ試験", lc, rc, null, null);
      Console.WriteLine(map.Describe());
      Console.WriteLine("L14->R"+map.MapLeftToRight(14)+" R14->L"+map.MapRightToLeft(14)+" R16->L"+map.MapRightToLeft(16));
    }

    // image pair debug via Build with raw images
    using (var lr = XlsxPackageReader.Open(left))
    using (var rr = XlsxPackageReader.Open(right)) {
      string cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dxl_img2");
      var li = lr.ExtractImages("製品カタログ", System.IO.Path.Combine(cache,"L")).ToList();
      var ri = rr.ExtractImages("製品カタログ", System.IO.Path.Combine(cache,"R")).ToList();
      Console.WriteLine("L img count="+li.Count+" R="+ri.Count);
      foreach (var i in li) Console.WriteLine("L row="+i.AnchorRow+" "+i.PixelWidth+"x"+i.PixelHeight+" h="+i.ContentHash);
      foreach (var i in ri) Console.WriteLine("R row="+i.AnchorRow+" "+i.PixelWidth+"x"+i.PixelHeight+" h="+i.ContentHash);
      var map = ContentScrollMap.Build("製品カタログ","製品カタログ", null, null, li, ri);
      Console.WriteLine(map.Describe());
      for (int row=4; row<=8; row++) Console.WriteLine("L"+row+"->"+map.MapLeftToRight(row)+" R"+row+"->"+map.MapRightToLeft(row));
    }
  }
}
