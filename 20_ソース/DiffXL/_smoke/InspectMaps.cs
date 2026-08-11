using System;
using System.Linq;
using DiffXL.LOGIC.Diff;

class P {
  static int Main() {
    string left = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";
    string right = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_right.xlsx";
    var r = new DiffEngine().Compare(left, right);
    Console.WriteLine("items="+r.Items.Count+" maps="+r.ScrollMaps.Count);
    foreach (var it in r.Items.Where(i => i.Kind==DiffKind.Image || i.Kind==DiffKind.ImageOnlyLeft || i.Kind==DiffKind.ImageOnlyRight)) {
      Console.WriteLine(it.Kind+" L="+it.SheetLeft+" R="+it.SheetRight+" sum="+it.Summary);
    }
    var map = r.ScrollMaps.Resolve("製品カタログ","製品カタログ");
    Console.WriteLine(map.Describe());
    for (int row=1; row<=10; row++) {
      Console.WriteLine("L"+row+"->R"+map.MapLeftToRight(row)+"  R"+row+"->L"+map.MapRightToLeft(row));
    }
    var sm = r.ScrollMaps.Resolve("ずれ試験","ずれ試験");
    Console.WriteLine(sm.Describe());
    for (int row=5; row<=16; row++) {
      Console.WriteLine("shift L"+row+"->R"+sm.MapLeftToRight(row)+"  R"+row+"->L"+sm.MapRightToLeft(row));
    }

    // inspect images with anchors
    using (var lr = XlsxPackageReader.Open(left))
    using (var rr = XlsxPackageReader.Open(right)) {
      string cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dxl_img");
      System.IO.Directory.CreateDirectory(cache);
      var li = lr.ExtractImages(null, System.IO.Path.Combine(cache,"L"));
      var ri = rr.ExtractImages(null, System.IO.Path.Combine(cache,"R"));
      Console.WriteLine("LEFT IMAGES");
      foreach (var i in li) Console.WriteLine("  sheet="+i.SheetName+" row="+i.AnchorRow+" "+i.FileName+" "+i.PixelWidth+"x"+i.PixelHeight+" hash="+(i.ContentHash??"").Substring(0,Math.Min(10,(i.ContentHash??"").Length)));
      Console.WriteLine("RIGHT IMAGES");
      foreach (var i in ri) Console.WriteLine("  sheet="+i.SheetName+" row="+i.AnchorRow+" "+i.FileName+" "+i.PixelWidth+"x"+i.PixelHeight+" hash="+(i.ContentHash??"").Substring(0,Math.Min(10,(i.ContentHash??"").Length)));
    }
    return 0;
  }
}
