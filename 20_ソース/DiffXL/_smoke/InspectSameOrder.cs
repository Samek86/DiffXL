using System;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;
class P {
  static void Main() {
    string L = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\content_scroll_left.xlsx";
    string R = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\content_scroll_right.xlsx";
    AppPaths.EnsureDirectories();
    NativeBootstrap.EnsureNativeBinaries();
    var r = new DiffEngine().Compare(L,R);
    foreach(var a in r.Alignments.Where(x => x.LeftSheet != null && x.LeftSheet.Contains("同順"))) {
      Console.WriteLine("=== " + a.LeftSheet + " ===");
      Console.WriteLine(a.ScrollMap.Describe());
      if (a.Images != null) foreach(var c in a.Images) {
        string la = FormatAnchor(c.Left);
        string ra = FormatAnchor(c.Right);
        Console.WriteLine("  corr pair="+c.IsPaired+" LO="+c.IsLeftOnly+" RO="+c.IsRightOnly
          +" exact="+c.IsExactHashMatch+" dr="+c.DiffRatio);
        Console.WriteLine("    Left:  "+la);
        Console.WriteLine("    Right: "+ra);
      }
      for (int i=1;i<=14;i++) {
        Console.WriteLine("  Map L"+i+"->R"+a.ScrollMap.MapLeftToRight(i)
          +"  R"+i+"->L"+a.ScrollMap.MapRightToLeft(i));
      }
    }
  }
  static string FormatAnchor(EmbeddedImage img) {
    if (img == null) return "null";
    if (img.Anchor != null)
      return "Anchor R"+img.Anchor.RowStart+"-"+img.Anchor.RowEnd
        +" C"+img.Anchor.ColStart+"-"+img.Anchor.ColEnd
        +" AnchorRow="+img.AnchorRow;
    return "Anchor=null AnchorRow="+img.AnchorRow;
  }
}
