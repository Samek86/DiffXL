using System;
using System.IO;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

class SettingsSmoke {
  static int Main() {
    AppPaths.EnsureDirectories();
    AppSettings.Load();
    var before = AppSettings.Current.Diff.HighlightColor;
    var op = AppSettings.Current.Diff.HighlightOpacity;
    Console.WriteLine("BEFORE color=" + before + " op=" + op + " enabled=" + AppSettings.Current.Diff.HighlightEnabled);

    // Diff ラウンドトリップ
    AppSettings.Current.Diff.HighlightColor = "#00FF00";
    AppSettings.Current.Diff.HighlightOpacity = 0.75;
    AppSettings.Current.Diff.HighlightEnabled = false;

    // Ui 同期キー ラウンドトリップ（Task 8）
    var ui = AppSettings.Current.Ui ?? (AppSettings.Current.Ui = new UiSettings());
    bool prevSync = ui.SyncScroll;
    bool prevGap = ui.ShowSyncGapOverlay;
    bool prevToast = ui.ShowSyncToastOnJump;
    bool prevMotion = ui.ReduceMotion;
    int prevPoll = ui.SyncPollFallbackMs;

    ui.SyncScroll = false;
    ui.ShowSyncGapOverlay = false;
    ui.ShowSyncToastOnJump = false;
    ui.ReduceMotion = true;
    ui.SyncPollFallbackMs = 500;

    AppSettings.Save();
    AppSettings.Load();

    bool okDiff = AppSettings.Current.Diff.HighlightColor.IndexOf("00FF00", StringComparison.OrdinalIgnoreCase)>=0
      && Math.Abs(AppSettings.Current.Diff.HighlightOpacity - 0.75)<0.001
      && AppSettings.Current.Diff.HighlightEnabled==false;
    Console.WriteLine("AFTER color=" + AppSettings.Current.Diff.HighlightColor + " op=" + AppSettings.Current.Diff.HighlightOpacity + " enabled=" + AppSettings.Current.Diff.HighlightEnabled);
    Console.WriteLine("SETTINGS_OK=" + okDiff);

    var u = AppSettings.Current.Ui;
    bool okUi = u != null
      && u.SyncScroll == false
      && u.ShowSyncGapOverlay == false
      && u.ShowSyncToastOnJump == false
      && u.ReduceMotion == true
      && u.SyncPollFallbackMs == 500;
    Console.WriteLine("UI_ROUNDTRIP SyncScroll=" + u.SyncScroll
      + " Gap=" + u.ShowSyncGapOverlay
      + " Toast=" + u.ShowSyncToastOnJump
      + " ReduceMotion=" + u.ReduceMotion
      + " PollMs=" + u.SyncPollFallbackMs);
    Console.WriteLine("UI_ROUNDTRIP_OK=" + okUi);

    // clamp 100–1000
    AppSettings.Current.Ui.SyncPollFallbackMs = 50;
    AppSettings.Save();
    AppSettings.Load();
    bool clampLow = AppSettings.Current.Ui.SyncPollFallbackMs == 100;
    AppSettings.Current.Ui.SyncPollFallbackMs = 5000;
    AppSettings.Save();
    AppSettings.Load();
    bool clampHigh = AppSettings.Current.Ui.SyncPollFallbackMs == 1000;
    Console.WriteLine("CLAMP_OK low=" + clampLow + " high=" + clampHigh
      + " gotLowThenHigh=" + AppSettings.Current.Ui.SyncPollFallbackMs);

    // restore
    AppSettings.Current.Diff.HighlightColor = "#FFFF00";
    AppSettings.Current.Diff.HighlightOpacity = 0.5;
    AppSettings.Current.Diff.HighlightEnabled = true;
    AppSettings.Current.Ui.SyncScroll = prevSync;
    AppSettings.Current.Ui.ShowSyncGapOverlay = prevGap;
    AppSettings.Current.Ui.ShowSyncToastOnJump = prevToast;
    AppSettings.Current.Ui.ReduceMotion = prevMotion;
    AppSettings.Current.Ui.SyncPollFallbackMs = prevPoll > 0 ? prevPoll : 250;
    AppSettings.Save();

    var style = DiffHighlightStyle.FromSettings();
    Console.WriteLine("STYLE rgb=" + style.R + "," + style.G + "," + style.B + " op=" + style.Opacity);
    bool styleOk = style.R==255 && style.G==255 && style.B==0 && Math.Abs(style.Opacity-0.5)<0.01;
    Console.WriteLine("STYLE_OK=" + styleOk);
    bool all = okDiff && okUi && clampLow && clampHigh && styleOk;
    Console.WriteLine("SETTINGS_SMOKE_PASS=" + all);
    Log.Info("SettingsSmoke ok=" + all);
    return all ? 0 : 1;
  }
}
