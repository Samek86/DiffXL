using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DiffXL.COMMON;
using DiffXL.LOGIC.Excel;
using DiffXL.VIEW.Controls;

class DualEmbed {
  [STAThread] static int Main() {
    AppPaths.EnsureDirectories();
    Log.Info("=== DualEmbed start ===");
    string L = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";
    string R = @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_right.xlsx";
    var app = new Application();
    var win = new Window { Title="DualEmbed", Width=1200, Height=800 };
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition());
    grid.ColumnDefinitions.Add(new ColumnDefinition());
    var h1 = new ExcelHostControl(); var h2 = new ExcelHostControl();
    Grid.SetColumn(h1,0); Grid.SetColumn(h2,1);
    grid.Children.Add(h1); grid.Children.Add(h2);
    win.Content = grid;
    ExcelWorkbookSession s1=null,s2=null; int fail=0;
    win.Loaded += (s,e) => {
      try {
        s1 = new ExcelWorkbookSession(); s1.OpenReadOnly(L);
        s2 = new ExcelWorkbookSession(); s2.OpenReadOnly(R);
        h1.Attach(s1.GetMainWindowHandle()); h2.Attach(s2.GetMainWindowHandle());
        h1.ResizeExcelToHost(); h2.ResizeExcelToHost();
        try { s1.ActivateSheet("長い一覧"); s2.ActivateSheet("長い一覧"); } catch {}
        win.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
          h1.ResizeExcelToHost(); h2.ResizeExcelToHost();
          Console.WriteLine("SIZE1=" + h1.ActualWidth + "x" + h1.ActualHeight);
          Console.WriteLine("SIZE2=" + h2.ActualWidth + "x" + h2.ActualHeight);
          foreach (int row in new[]{11,25,40}) {
            bool a=s1.TryGotoRow(row); bool b=s2.TryGotoRow(row);
            int sr1,sc1,sr2,sc2; s1.TryGetScroll(out sr1,out sc1); s2.TryGetScroll(out sr2,out sc2);
            Console.WriteLine("DUAL row="+row+" L="+a+"/"+sr1+" R="+b+"/"+sr2);
            Log.Info("DUAL row="+row+" L="+a+"/"+sr1+" R="+b+"/"+sr2);
            if (!a||!b||sr1!=row||sr2!=row) fail++;
          }
          // resize window and check host grows
          win.Width = 1400; win.Height = 900;
          win.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
            h1.ResizeExcelToHost(); h2.ResizeExcelToHost();
            Console.WriteLine("RESIZE1=" + h1.ActualWidth + "x" + h1.ActualHeight);
            Console.WriteLine("RESIZE2=" + h2.ActualWidth + "x" + h2.ActualHeight);
            Log.Info("RESIZE1=" + h1.ActualWidth + "x" + h1.ActualHeight);
            bool sizeOk = h1.ActualWidth > 400 && h1.ActualHeight > 400;
            Console.WriteLine("SIZE_OK=" + sizeOk + " FAIL="+fail);
            Log.Info("SIZE_OK=" + sizeOk + " FAIL="+fail);
            win.Close();
          }));
        }));
      } catch(Exception ex) { Console.WriteLine(ex); fail=99; win.Close(); }
    };
    win.Closed += (s,e)=>{ try{h1.Detach();h2.Detach();}catch{} try{s1?.Dispose();s2?.Dispose();}catch{} app.Shutdown(); };
    app.Run(win);
    return fail==0 ? 0 : 1;
  }
}
