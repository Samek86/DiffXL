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
    AppSettings.Current.Diff.ImageHighlightBorderColor = "#FF00FF00";
    AppSettings.Current.Diff.ImageHighlightFillColor = "#40AABBCC";
    AppSettings.Current.Diff.ImageHighlightBorderThickness = 5;
    AppSettings.Current.Diff.ImageRejectDiffRatio = 0.77;
    AppSettings.Current.Diff.ImageAbsDiffThreshold = 20;
    AppSettings.Current.Diff.ImageMinRegionArea = 40;

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

    var dAfter = AppSettings.Current.Diff;
    bool okDiff = dAfter.HighlightColor.IndexOf("00FF00", StringComparison.OrdinalIgnoreCase)>=0
      && Math.Abs(dAfter.HighlightOpacity - 0.75)<0.001
      && dAfter.HighlightEnabled==false;
    bool okImg = dAfter.ImageHighlightBorderColor != null
      && dAfter.ImageHighlightBorderColor.IndexOf("00FF00", StringComparison.OrdinalIgnoreCase)>=0
      && dAfter.ImageHighlightFillColor != null
      && dAfter.ImageHighlightFillColor.IndexOf("AABBCC", StringComparison.OrdinalIgnoreCase)>=0
      && dAfter.ImageHighlightBorderThickness == 5
      && Math.Abs(dAfter.ImageRejectDiffRatio - 0.77)<0.001
      && Math.Abs(dAfter.ImageAbsDiffThreshold - 20)<0.001
      && dAfter.ImageMinRegionArea == 40;
    Console.WriteLine("AFTER color=" + dAfter.HighlightColor + " op=" + dAfter.HighlightOpacity + " enabled=" + dAfter.HighlightEnabled);
    Console.WriteLine("IMG border=" + dAfter.ImageHighlightBorderColor + " fill=" + dAfter.ImageHighlightFillColor
      + " th=" + dAfter.ImageHighlightBorderThickness
      + " reject=" + dAfter.ImageRejectDiffRatio
      + " abs=" + dAfter.ImageAbsDiffThreshold
      + " minArea=" + dAfter.ImageMinRegionArea);
    Console.WriteLine("SETTINGS_OK=" + okDiff);
    Console.WriteLine("IMAGE_SETTINGS_OK=" + okImg);

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
    AppSettings.Current.Diff.ImageHighlightBorderColor = "#FFFF0000";
    AppSettings.Current.Diff.ImageHighlightFillColor = "#80FFFF00";
    AppSettings.Current.Diff.ImageHighlightBorderThickness = 3;
    AppSettings.Current.Diff.ImageRejectDiffRatio = 0.45;
    AppSettings.Current.Diff.ImageAbsDiffThreshold = 15;
    AppSettings.Current.Diff.ImageMinRegionArea = 25;
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

    var imgStyle = DiffHighlightStyle.FromImageSettings();
    Console.WriteLine("IMG_STYLE borderARGB=" + imgStyle.ToHexArgbBorder()
      + " fillARGB=" + imgStyle.ToHexArgbFill()
      + " th=" + imgStyle.BorderThickness);
    bool imgStyleOk = imgStyle.BorderR==255 && imgStyle.BorderG==0 && imgStyle.BorderB==0
      && imgStyle.FillR==255 && imgStyle.FillG==255 && imgStyle.FillB==0
      && imgStyle.FillA==0x80 && imgStyle.BorderThickness==3;
    Console.WriteLine("IMG_STYLE_OK=" + imgStyleOk);

    bool all = okDiff && okImg && okUi && clampLow && clampHigh && styleOk && imgStyleOk;
    Console.WriteLine("SETTINGS_SMOKE_PASS=" + all);
    Log.Info("SettingsSmoke ok=" + all);
    return all ? 0 : 1;
  }
}
