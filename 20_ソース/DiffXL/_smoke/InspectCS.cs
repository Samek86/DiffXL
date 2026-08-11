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
    Console.WriteLine("err="+r.ErrorMessage+" items="+r.Items.Count+" align="+ (r.Alignments!=null?r.Alignments.Count:0));
    if (r.Alignments!=null) foreach(var a in r.Alignments) {
      Console.WriteLine("--- "+a.LeftSheet);
      if (a.ScrollMap!=null) Console.WriteLine(a.ScrollMap.Describe());
      if (a.Images!=null) foreach(var c in a.Images) {
        int lr = c.Left!=null ? (c.Left.Anchor!=null?c.Left.Anchor.RowStart:c.Left.AnchorRow) : -1;
        int rr = c.Right!=null ? (c.Right.Anchor!=null?c.Right.Anchor.RowStart:c.Right.AnchorRow) : -1;
        Console.WriteLine("  img L"+lr+" R"+rr+" exact="+c.IsExactHashMatch+" LO="+c.IsLeftOnly+" RO="+c.IsRightOnly+" pair="+c.IsPaired+" dr="+c.DiffRatio);
      }
      if (a.ScrollMap!=null) {
        int[] rowsL={5,8,10,12}; int[] rowsR={5,8,9,12};
        foreach(int x in rowsL) Console.WriteLine("  MapL"+x+"->R"+a.ScrollMap.MapLeftToRight(x));
        foreach(int x in rowsR) Console.WriteLine("  MapR"+x+"->L"+a.ScrollMap.MapRightToLeft(x));
      }
    }
  }
}
