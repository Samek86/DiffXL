using System;
using System.Linq;
using DiffXL.LOGIC.Diff;
class P {
  static void Main() {
    var r = new DiffEngine().Compare(
      @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx",
      @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_right.xlsx");
    var m = r.ScrollMaps.Resolve("製品カタログ","製品カタログ");
    Console.WriteLine(m.Describe());
    for (int i=4;i<=8;i++) Console.WriteLine("L"+i+"->R"+m.MapLeftToRight(i)+" R"+i+"->L"+m.MapRightToLeft(i));
  }
}
