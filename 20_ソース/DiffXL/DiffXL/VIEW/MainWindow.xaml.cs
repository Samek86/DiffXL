using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DiffXL.COMMON;
using DiffXL.LOGIC;
using DiffXL.LOGIC.Diff;
using DiffXL.VIEW;
using DiffXL.VIEW.Controls;
using DiffXL.VIEW.Dialogs;
using Microsoft.Win32;
using CompareOptions = DiffXL.LOGIC.Diff.CompareOptions;

namespace DiffXL
{
    /// <summary>
    /// メインウィンドウ。起動選択・比較・同期・MiniMap を統合する。
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 比較セッション。
        /// </summary>
        private readonly CompareSession _session = new CompareSession();

        /// <summary>
        /// 差分強調。
        /// </summary>
        private DiffHighlightController _highlightController;

        /// <summary>
        /// スクロール同期。
        /// </summary>
        private ScrollSyncService _scrollSync;

        /// <summary>
        /// 再同期トースト非表示用タイマー（1800ms）。
        /// </summary>
        private DispatcherTimer _syncToastHideTimer;

        /// <summary>
        /// シート左右同期の再入防止。
        /// </summary>
        private bool _syncingSheets;

        /// <summary>
        /// 種類フィルタ左右同期の再入防止。
        /// </summary>
        private bool _syncingKindFilter;

        /// <summary>
        /// ツールバーのシート対応コンボ更新中。
        /// </summary>
        private bool _suppressPairComboEvent;

        /// <summary>
        /// 差分強調トグルの再入防止。
        /// </summary>
        private bool _updatingHighlightToggle;

        /// <summary>
        /// ホイールメッセージフィルタ登録済み。
        /// </summary>
        private bool _wheelFilterAttached;

        /// <summary>
        /// パン中（中ボタン or Alt+左ドラッグ）。
        /// </summary>
        private bool _isPanning;

        /// <summary>
        /// パン開始時のスクリーン座標。
        /// </summary>
        private Point _panLastScreen;

        /// <summary>
        /// パン対象ペイン（null なら左右両方）。
        /// </summary>
        private WorkbookPane _panPrimaryPane;

        /// <summary>
        /// MiniMap 用ビューポート定期ポーリング。
        /// </summary>
        private DispatcherTimer _viewportTimer;

        /// <summary>
        /// 低レベル マウスフック（クリックなしホイール用）。
        /// </summary>
        private IntPtr _mouseHook = IntPtr.Zero;

        /// <summary>
        /// フックコールバック保持（GC 防止）。
        /// </summary>
        private NativeInput.LowLevelMouseProc _mouseHookProc;

        /// <summary>
        /// 前回 MiniMap に出した左行（重複更新抑制）。
        /// </summary>
        private int _lastMiniMapLeftRow = -1;

        /// <summary>
        /// 前回 MiniMap に出した右行（重複更新抑制）。
        /// </summary>
        private int _lastMiniMapRightRow = -1;

        /// <summary>
        /// 前回 MiniMap に出したシート。
        /// </summary>
        private string _lastMiniMapSheet = string.Empty;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            LeftPane.OpenFailed += OnPaneOpenFailed;
            RightPane.OpenFailed += OnPaneOpenFailed;
            LeftPane.OpenSucceeded += OnPaneOpened;
            RightPane.OpenSucceeded += OnPaneOpened;
            LeftPane.SheetChangedByUser += OnLeftSheetChangedByUser;
            RightPane.SheetChangedByUser += OnRightSheetChangedByUser;
            // イベント駆動の即時同期（ポーリングは保険）
            LeftPane.ScrollInteracted += OnLeftPaneScrollInteracted;
            RightPane.ScrollInteracted += OnRightPaneScrollInteracted;
            // 内容ストリーム（統一リスト）の左右スクロール同期
            LeftPane.ContentScrollRatioChanged += OnLeftContentScrollRatioChanged;
            RightPane.ContentScrollRatioChanged += OnRightContentScrollRatioChanged;
            if (LeftPane.ContentHostControl != null)
            {
                LeftPane.ContentHostControl.KindFilterChanged += OnContentKindFilterChanged;
            }

            if (RightPane.ContentHostControl != null)
            {
                RightPane.ContentHostControl.KindFilterChanged += OnContentKindFilterChanged;
            }

            Startup.StartCompareRequested += OnStartCompareRequested;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        /// <summary>
        /// 起動時初期化。
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _scrollSync = new ScrollSyncService
            {
                Enabled = AppSettings.Current.Ui == null || AppSettings.Current.Ui.SyncScroll
            };
            _scrollSync.StateChanged += OnScrollSyncStateChanged;
            ApplySyncStatusUi(_scrollSync.CurrentState);

            // 差分印は MiniMap のみ（左右ガターは廃止）
            _highlightController = new DiffHighlightController(MiniMap);

            bool highlightOn = AppSettings.Current.Diff == null || AppSettings.Current.Diff.HighlightEnabled;
            _updatingHighlightToggle = true;
            try
            {
                BtnHighlightToggle.IsChecked = highlightOn;
            }
            finally
            {
                _updatingHighlightToggle = false;
            }

            _highlightController.SetVisible(highlightOn);
            UpdateHighlightToggleCaption();
            ApplyImageHighlightVisible(highlightOn);
            _highlightController.VisibilityChanged += visible =>
            {
                // 画像ペアの枠・塗りも再比較なしで ON/OFF
                ApplyImageHighlightVisible(visible);
                if (visible)
                {
                    RefreshMiniMapForCurrentSheet();
                }

                if (_updatingHighlightToggle)
                {
                    return;
                }

                _updatingHighlightToggle = true;
                try
                {
                    if (BtnHighlightToggle.IsChecked != visible)
                    {
                        BtnHighlightToggle.IsChecked = visible;
                    }

                    UpdateHighlightToggleCaption();
                }
                finally
                {
                    _updatingHighlightToggle = false;
                }
            };

            MiniMap.NavigateRequested += OnMiniMapNavigate;
            MiniMap.ScrubStarted += OnMiniMapScrubStarted;
            MiniMap.ScrubEnded += OnMiniMapScrubEnded;
            AttachMouseWheelFilter();
            AttachLowLevelMouseHook();
            StartViewportTimer();

            // Excel は必須ではない（内容ビューで比較結果を表示）
            ShowStartup();

            if (App.AutoTest != null && App.AutoTest.Enabled)
            {
                // 起動 UI 経路を自動で駆動
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(async () => await RunAutoLiveTestAsync()));
            }
        }

        /// <summary>
        /// コマンドライン --auto-live-test によるライブ検証（比較・MiniMap・強調・設定）。
        /// </summary>
        private async System.Threading.Tasks.Task RunAutoLiveTestAsync()
        {
            var auto = App.AutoTest;
            int failures = 0;
            try
            {
                auto.WriteLine("BEGIN auto-live-test");
                string left = auto.LeftPath;
                string right = auto.RightPath;
                if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)
                    || !File.Exists(left) || !File.Exists(right))
                {
                    auto.WriteLine("FAIL missing sample paths");
                    failures++;
                }
                else
                {
                    auto.WriteLine("COMPARE start");
                    await OpenAndCompareAsync(left, right, resetOptions: true);
                    var result = _session.LastResult;
                    if (result == null || !string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        auto.WriteLine("FAIL compare: " + (result != null ? result.ErrorMessage : "null result"));
                        failures++;
                    }
                    else
                    {
                        int text = result.Items.Count(i => i.Kind == DiffKind.Text);
                        int image = result.Items.Count(i => i.Kind == DiffKind.Image);
                        int imageOnlyL = result.Items.Count(i => i.Kind == DiffKind.ImageOnlyLeft);
                        int imageOnlyR = result.Items.Count(i => i.Kind == DiffKind.ImageOnlyRight);
                        int structure = result.Items.Count(i => i.Kind == DiffKind.Structure);
                        auto.WriteLine("COMPARE_OK count=" + result.Items.Count
                            + " text=" + text + " image=" + image
                            + " imageOnlyL=" + imageOnlyL + " imageOnlyR=" + imageOnlyR
                            + " structure=" + structure
                            + " elapsedMs=" + (int)result.Elapsed.TotalMilliseconds);
                        if (result.Items.Count == 0)
                        {
                            auto.WriteLine("FAIL empty compare result");
                            failures++;
                        }

                        // 大画像サンプル向け: 画像系差分が1件以上あることをログ（ハード失敗にはしない）
                        int imageRelated = image + imageOnlyL + imageOnlyR;
                        if (imageRelated > 0)
                        {
                            auto.WriteLine("IMAGE_DIFFS_OK related=" + imageRelated);
                        }
                    }

                    // 差分強調トグル（IsChecked 変更で Checked/Unchecked が走る）
                    bool before = _highlightController != null && _highlightController.IsVisible;
                    BtnHighlightToggle.IsChecked = false;
                    bool mid = _highlightController != null && _highlightController.IsVisible;
                    BtnHighlightToggle.IsChecked = true;
                    bool after = _highlightController != null && _highlightController.IsVisible;
                    auto.WriteLine("HIGHLIGHT before=" + before + " off=" + mid + " on=" + after);
                    if (mid)
                    {
                        auto.WriteLine("FAIL highlight did not turn off");
                        failures++;
                    }
                    else if (!after)
                    {
                        auto.WriteLine("FAIL highlight did not turn on");
                        failures++;
                    }
                    else
                    {
                        auto.WriteLine("HIGHLIGHT_OK");
                    }

                    bool contentScrollSample = IsContentScrollSamplePath(left) || IsContentScrollSamplePath(right);
                    bool contentDiffSample = IsContentDiffSamplePath(left) || IsContentDiffSamplePath(right);
                    auto.WriteLine("SAMPLE_MODE " + (contentDiffSample
                        ? "content_diff"
                        : (contentScrollSample ? "content_scroll" : "full_feature")));

                    // content_diff: 内容ストリーム + MiniMap 専用検証（スクリーンショット付き）
                    if (contentDiffSample)
                    {
                        failures += await VerifyContentDiffMiniMapLiveAsync(auto);
                        auto.WriteLine(failures == 0 ? "AUTO_LIVE_PASS" : "AUTO_LIVE_FAIL count=" + failures);
                        return;
                    }

                    // MiniMap ジャンプ: わざと別シートにいる状態から、差分アイテムのシートへ飛ぶ
                    DiffItem jumpItem = null;
                    if (_session.LastResult != null)
                    {
                        if (!contentScrollSample)
                        {
                            // 長い一覧のテキスト差分を優先（行番号が大きく変化が分かりやすい）
                            jumpItem = _session.LastResult.Items
                                .FirstOrDefault(i => i.Kind == DiffKind.Text
                                    && string.Equals(i.SheetLeft, "長い一覧", StringComparison.OrdinalIgnoreCase)
                                    && TextDiffService.ParseAnchorRow(i.AddressLeft ?? i.AddressRight) >= 10);
                        }

                        if (jumpItem == null)
                        {
                            jumpItem = _session.LastResult.Items
                                .FirstOrDefault(i => i.Kind == DiffKind.Text
                                    && TextDiffService.ParseAnchorRow(i.AddressLeft ?? i.AddressRight) >= 5);
                        }

                        if (jumpItem == null)
                        {
                            jumpItem = _session.LastResult.Items.FirstOrDefault(i => i.Kind == DiffKind.Text);
                        }
                    }

                    // わざと別シート（表紙）に寄せてから MiniMap にシート切替させる
                    try
                    {
                        LeftPane.TrySelectSheet("表紙");
                        RightPane.TrySelectSheet("表紙");
                        auto.WriteLine("SHEET 表紙 pre-activated (to verify MiniMap sheet switch)");
                    }
                    catch (Exception ex)
                    {
                        auto.WriteLine("SHEET_ACTIVATE_WARN " + ex.Message);
                    }

                    int targetLeftRow = jumpItem != null
                        ? Math.Max(1, TextDiffService.ParseAnchorRow(jumpItem.AddressLeft))
                        : 5;
                    if (targetLeftRow < 2)
                    {
                        targetLeftRow = Math.Max(1, TextDiffService.ParseAnchorRow(
                            jumpItem != null ? (jumpItem.AddressLeft ?? jumpItem.AddressRight) : null));
                        if (targetLeftRow < 2)
                        {
                            targetLeftRow = 5;
                        }
                    }

                    int targetRightRow = jumpItem != null
                        ? TextDiffService.ParseAnchorRow(jumpItem.AddressRight)
                        : 0;
                    if (targetRightRow <= 0 && _scrollSync != null)
                    {
                        targetRightRow = _scrollSync.MapLeftToRight(targetLeftRow);
                    }

                    if (targetRightRow <= 0)
                    {
                        targetRightRow = targetLeftRow;
                    }

                    string expectSheet = jumpItem != null
                        ? (jumpItem.SheetLeft ?? jumpItem.SheetRight ?? string.Empty)
                        : (contentScrollSample ? "SC_画像ギャップ" : "長い一覧");

                    OnMiniMapNavigate(0.55, jumpItem);

                    // MiniMap 経由の Goto 結果を確認（再 Goto はせず、状態を読む）
                    int lsr = 0, rsr = 0, sc;
                    bool lOk = LeftPane.IsOpen
                        && LeftPane.TryGetScroll(out lsr, out sc);
                    bool rOk = RightPane.IsOpen
                        && RightPane.TryGetScroll(out rsr, out sc);

                    // シートが切り替わっているか（Combo 表示）
                    string leftSheetUi = LeftPane.SelectedSheetName ?? string.Empty;
                    string rightSheetUi = RightPane.SelectedSheetName ?? string.Empty;

                    int mapExpectR = _scrollSync != null ? _scrollSync.MapLeftToRight(lsr) : lsr;
                    auto.WriteLine("MINIMAP targetSheet=" + expectSheet
                        + " targetL=" + targetLeftRow + " targetR=" + targetRightRow
                        + " Lsheet=" + leftSheetUi + " Rsheet=" + rightSheetUi
                        + " Lsr=" + lsr + " Rsr=" + rsr
                        + " mapR=" + mapExpectR
                        + " status=" + StatusText.Text);

                    bool sheetOk = string.IsNullOrEmpty(expectSheet)
                        || (string.Equals(leftSheetUi, expectSheet, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(rightSheetUi, expectSheet, StringComparison.OrdinalIgnoreCase));
                    bool rowOk;
                    if (contentScrollSample)
                    {
                        // content_scroll: 左右が Map 整合（±2）＋左がターゲット付近
                        bool mapAligned = lOk && rOk
                            && Math.Abs(mapExpectR - rsr) <= 2;
                        bool nearTarget = Math.Abs(lsr - targetLeftRow) <= 3
                            && Math.Abs(rsr - targetRightRow) <= 3;
                        rowOk = mapAligned && nearTarget;
                        if (mapAligned)
                        {
                            auto.WriteLine("MINIMAP_MAP_ALIGN ok |MapL2R(Lsr)-Rsr|="
                                + Math.Abs(mapExpectR - rsr));
                        }
                        else
                        {
                            auto.WriteLine("MINIMAP_MAP_ALIGN fail |MapL2R(Lsr)-Rsr|="
                                + Math.Abs(mapExpectR - rsr)
                                + " (expect<=2)");
                        }
                    }
                    else
                    {
                        // full_feature 回帰: 従来 ±3 行
                        rowOk = lOk && rOk
                            && Math.Abs(lsr - targetLeftRow) <= 3
                            && Math.Abs(rsr - targetLeftRow) <= 3;
                    }

                    if (!sheetOk)
                    {
                        auto.WriteLine("FAIL minimap sheet switch expect=" + expectSheet
                            + " got L=" + leftSheetUi + " R=" + rightSheetUi);
                        failures++;
                    }
                    else if (!rowOk)
                    {
                        auto.WriteLine("FAIL minimap/goto accuracy");
                        failures++;
                    }
                    else
                    {
                        auto.WriteLine("MINIMAP_OK");
                    }

                    // 追加: 複数シートの差分マーカーを順にジャンプ（対話クリック相当）
                    if (_session.LastResult != null)
                    {
                        string[] probeSheets = contentScrollSample
                            ? new[] { "SC_画像ギャップ", "SC_テキスト挿入", "SC_大画像span", "SC_同順異内容", "表紙" }
                            : new[] { "売上サマリ", "製品カタログ", "長い一覧", "表紙" };
                        string parkSheet = contentScrollSample ? "表紙" : "レイアウト確認";
                        int multiOk = 0;
                        int multiTry = 0;
                        foreach (string sheetName in probeSheets)
                        {
                            DiffItem it = _session.LastResult.Items.FirstOrDefault(i =>
                                i.Kind == DiffKind.Text
                                && string.Equals(i.SheetLeft, sheetName, StringComparison.OrdinalIgnoreCase)
                                && TextDiffService.ParseAnchorRow(i.AddressLeft ?? i.AddressRight) > 0);
                            if (it == null)
                            {
                                continue;
                            }

                            multiTry++;
                            // 毎回別シートから飛ぶ
                            LeftPane.TrySelectSheet(parkSheet);
                            RightPane.TrySelectSheet(parkSheet);
                            OnMiniMapNavigate(0.4, it);
                            int expectL = TextDiffService.ParseAnchorRow(it.AddressLeft);
                            if (expectL <= 0)
                            {
                                expectL = TextDiffService.ParseAnchorRow(it.AddressRight);
                            }

                            int expectR = TextDiffService.ParseAnchorRow(it.AddressRight);
                            if (expectR <= 0 && _scrollSync != null)
                            {
                                expectR = _scrollSync.MapLeftToRight(expectL);
                            }

                            if (expectR <= 0)
                            {
                                expectR = expectL;
                            }

                            int gl = 0, gr = 0, gc;
                            LeftPane.TryGetScroll(out gl, out gc);
                            RightPane.TryGetScroll(out gr, out gc);
                            string ls = LeftPane.SelectedSheetName ?? string.Empty;
                            string rs = RightPane.SelectedSheetName ?? string.Empty;
                            bool okSheet = string.Equals(ls, sheetName, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(rs, sheetName, StringComparison.OrdinalIgnoreCase);
                            bool okRow;
                            if (contentScrollSample)
                            {
                                int mapped = _scrollSync != null ? _scrollSync.MapLeftToRight(gl) : gl;
                                okRow = Math.Abs(gl - expectL) <= 3
                                    && Math.Abs(gr - expectR) <= 3
                                    && Math.Abs(mapped - gr) <= 2;
                            }
                            else
                            {
                                okRow = Math.Abs(gl - expectL) <= 3 && Math.Abs(gr - expectL) <= 3;
                            }

                            auto.WriteLine("MINIMAP_MULTI sheet=" + sheetName
                                + " expectL=" + expectL + " expectR=" + expectR
                                + " Lsheet=" + ls + " Rsheet=" + rs + " Lsr=" + gl + " Rsr=" + gr
                                + " ok=" + (okSheet && okRow));
                            if (okSheet && okRow)
                            {
                                multiOk++;
                            }
                        }

                        auto.WriteLine("MINIMAP_MULTI_SUMMARY ok=" + multiOk + "/" + multiTry);
                        if (multiTry > 0 && multiOk < multiTry)
                        {
                            auto.WriteLine("FAIL minimap multi-sheet coverage");
                            failures++;
                        }
                        else if (multiTry > 0)
                        {
                            auto.WriteLine("MINIMAP_MULTI_OK");
                        }
                    }

                    // シート同期: 左だけターゲットシートに変えると右も追従するか
                    string syncTarget = contentScrollSample ? "SC_画像ギャップ" : "長い一覧";
                    try
                    {
                        LeftPane.TrySelectSheet("表紙");
                        RightPane.TrySelectSheet("表紙");
                        // ユーザー操作相当: SheetChangedByUser を経由
                        OnLeftSheetChangedByUser(syncTarget);
                        // Combo も合わせる（TrySelectSheet はイベントを飛ばないので明示）
                        LeftPane.TrySelectSheet(syncTarget);
                        string ls = LeftPane.SelectedSheetName ?? string.Empty;
                        string rs = RightPane.SelectedSheetName ?? string.Empty;
                        auto.WriteLine("SHEET_SYNC after L→" + syncTarget + " Lsheet=" + ls + " Rsheet=" + rs);
                        if (!string.Equals(ls, syncTarget, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(rs, syncTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            auto.WriteLine("FAIL sheet sync L→R");
                            failures++;
                        }
                        else
                        {
                            auto.WriteLine("SHEET_SYNC_OK");
                        }

                        // ツールバーコンボが埋まること
                        int pairCount = PairSheetCombo != null ? PairSheetCombo.Items.Count : 0;
                        auto.WriteLine("PAIR_COMBO count=" + pairCount);
                        if (pairCount < 1)
                        {
                            auto.WriteLine("FAIL pair sheet combo empty");
                            failures++;
                        }
                    }
                    catch (Exception exSheet)
                    {
                        auto.WriteLine("FAIL sheet sync " + exSheet.Message);
                        failures++;
                    }

                    // 再比較（実ボタン経路）
                    try
                    {
                        await RunCompareOnlyAsync();
                        int afterCount = _session.LastResult != null ? _session.LastResult.Items.Count : 0;
                        auto.WriteLine("RECOMPARE_OK count=" + afterCount);
                        if (afterCount == 0)
                        {
                            auto.WriteLine("FAIL recompare empty");
                            failures++;
                        }
                    }
                    catch (Exception exRe)
                    {
                        auto.WriteLine("FAIL recompare " + exRe.Message);
                        failures++;
                    }

                    // 設定: 実際の SettingsWindow + ShowDialog 経路（BtnSettings と同型）
                    string prevColor = AppSettings.Current.Diff != null
                        ? AppSettings.Current.Diff.HighlightColor
                        : "#FFFF00";
                    double prevOpacity = AppSettings.Current.Diff != null
                        ? AppSettings.Current.Diff.HighlightOpacity
                        : 0.5;
                    try
                    {
                        var settingsWin = new SettingsWindow { Owner = this };
                        auto.WriteLine("SETTINGS_OPEN_OK constructed SettingsWindow");
                        Log.Info("SETTINGS_OPEN_OK SettingsWindow constructed for ShowDialog");

                        // ContentRendered 後に UI 値を変え、保存ボタンと同じ経路で閉じる
                        settingsWin.ContentRendered += (s, ev) =>
                        {
                            try
                            {
                                settingsWin.SetHighlightUi("#FFCC00", 0.6);
                                bool saved = settingsWin.TrySaveAndClose();
                                auto.WriteLine("SETTINGS_DIALOG_SAVE saved=" + saved);
                            }
                            catch (Exception exSave)
                            {
                                auto.WriteLine("SETTINGS_DIALOG_SAVE_ERR " + exSave.Message);
                                Log.Exception(exSave);
                                try { settingsWin.Close(); } catch { /* ignore */ }
                            }
                        };

                        bool? dlgResult = settingsWin.ShowDialog();
                        auto.WriteLine("SETTINGS_DIALOG result=" + dlgResult
                            + " SavedProp=" + settingsWin.Saved);

                        // BtnSettings_Click 後処理と同じ反映
                        if (dlgResult == true || settingsWin.Saved)
                        {
                            if (_highlightController != null)
                            {
                                _highlightController.RefreshStyleFromSettings();
                                bool on = AppSettings.Current.Diff != null && AppSettings.Current.Diff.HighlightEnabled;
                                BtnHighlightToggle.IsChecked = on;
                                _highlightController.SetVisible(on);
                                ApplyImageHighlightVisible(on);
                                RefreshImageHighlightStyleFromSettings();
                                UpdateHighlightToggleCaption();
                            }

                            if (_scrollSync != null)
                            {
                                _scrollSync.Enabled = AppSettings.Current.Ui == null || AppSettings.Current.Ui.SyncScroll;
                                _scrollSync.RefreshPollIntervalFromSettings();

                            }

                            MiniMap.RefreshStyle();
                            StatusText.Text = "設定を保存しました。";
                        }

                        AppSettings.Load();
                        bool setOk = AppSettings.Current.Diff != null
                            && AppSettings.Current.Diff.HighlightColor != null
                            && AppSettings.Current.Diff.HighlightColor.IndexOf("FFCC00", StringComparison.OrdinalIgnoreCase) >= 0
                            && (dlgResult == true || settingsWin.Saved);
                        auto.WriteLine("SETTINGS_SAVE_OK=" + setOk
                            + " color=" + (AppSettings.Current.Diff != null ? AppSettings.Current.Diff.HighlightColor : "null")
                            + " via=SettingsWindow.ShowDialog");
                        if (!setOk)
                        {
                            auto.WriteLine("FAIL settings dialog open/save");
                            failures++;
                        }
                        else
                        {
                            auto.WriteLine("SETTINGS_OPEN_OK");
                        }
                    }
                    catch (Exception exSettings)
                    {
                        auto.WriteLine("FAIL SETTINGS_OPEN " + exSettings.Message);
                        Log.Exception(exSettings);
                        failures++;
                    }

                    // 復元（API ではなく再度ダイアログ保存と同じ Save 経路）
                    try
                    {
                        var restoreWin = new SettingsWindow { Owner = this };
                        restoreWin.ContentRendered += (s, ev) =>
                        {
                            restoreWin.SetHighlightUi(prevColor ?? "#FFFF00", prevOpacity > 0 ? prevOpacity : 0.5);
                            restoreWin.TrySaveAndClose();
                        };
                        restoreWin.ShowDialog();
                        auto.WriteLine("SETTINGS_RESTORED color=" + (AppSettings.Current.Diff != null ? AppSettings.Current.Diff.HighlightColor : "?"));
                    }
                    catch (Exception exRestore)
                    {
                        auto.WriteLine("SETTINGS_RESTORE_WARN " + exRestore.Message);
                        AppSettings.Current.Diff.HighlightColor = prevColor ?? "#FFFF00";
                        AppSettings.Current.Diff.HighlightOpacity = 0.5;
                        AppSettings.Save();
                    }

                    // リサイズ
                    double h0 = LeftPane.ActualHeight;
                    Width = Math.Max(Width + 80, 1200);
                    Height = Math.Max(Height + 80, 800);
                    await System.Threading.Tasks.Task.Delay(400);
                    LeftPane.UpdateLayout();
                    auto.WriteLine("RESIZE leftH0=" + h0 + " leftH1=" + LeftPane.ActualHeight
                        + " hostAttachedL=" + (LeftPane.IsOpen));

                    // ---- 内容ベース縦スクロール同期の検証 ----
                    try
                    {
                        failures += await VerifyContentScrollSyncAsync(auto);
                    }
                    catch (Exception exScroll)
                    {
                        auto.WriteLine("FAIL content-scroll " + exScroll.Message);
                        Log.Exception(exScroll);
                        failures++;
                    }
                }

                auto.WriteLine("FAILURES=" + failures);
                auto.WriteLine(failures == 0 ? "AUTO_LIVE_PASS" : "AUTO_LIVE_FAIL");
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                auto.WriteLine("EXCEPTION " + ex);
                failures = 99;
                auto.WriteLine("AUTO_LIVE_FAIL");
            }
            finally
            {
                if (auto.QuitWhenDone)
                {
                    try
                    {
                        CloseCompareWorkspace();
                    }
                    catch
                    {
                        // ignore
                    }

                    Application.Current.Shutdown(failures == 0 ? 0 : 1);
                }
            }
        }

        /// <summary>
        /// content_scroll 専用サンプルかどうか（パス名で判定）。
        /// </summary>
        private static bool IsContentScrollSamplePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.IndexOf("content_scroll", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// content_diff 専用サンプルかどうか（パス名で判定）。
        /// </summary>
        private static bool IsContentDiffSamplePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.IndexOf("content_diff", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// エビデンス用スクリーンショットを保存する。
        /// </summary>
        private void CaptureEvidenceShot(string evidenceDir, string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(evidenceDir))
                {
                    return;
                }

                Directory.CreateDirectory(evidenceDir);
                UpdateLayout();
                Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
                double w = Math.Max(1, ActualWidth);
                double h = Math.Max(1, ActualHeight);
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(w),
                    (int)Math.Ceiling(h),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                rtb.Render(this);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                string path = Path.Combine(evidenceDir, fileName);
                using (var fs = File.Create(path))
                {
                    encoder.Save(fs);
                }

                Log.Info("screenshot saved " + path);
            }
            catch (Exception ex)
            {
                Log.Debug("screenshot fail: " + ex.Message);
            }
        }

        /// <summary>
        /// content_diff 向け: 統一ストリーム・左右スクロール同期・MiniMap ジャンプをライブ検証する。
        /// </summary>
        private async System.Threading.Tasks.Task<int> VerifyContentDiffMiniMapLiveAsync(AutoLiveTestOptions auto)
        {
            int failures = 0;
            string evidenceDir = null;
            if (!string.IsNullOrEmpty(auto.ReportPath))
            {
                evidenceDir = Path.Combine(
                    Path.GetDirectoryName(auto.ReportPath) ?? ".",
                    "screenshots");
                Directory.CreateDirectory(evidenceDir);
            }

            auto.WriteLine("CONTENT_DIFF_VERIFY begin");
            // レイアウトを十分に取る（ScrollableHeight=0 を避ける）
            try
            {
                WindowState = WindowState.Maximized;
                Width = Math.Max(Width, 1400);
                Height = Math.Max(Height, 900);
            }
            catch
            {
                // ignore
            }

            await System.Threading.Tasks.Task.Delay(300);
            UpdateLayout();
            CaptureEvidenceShot(evidenceDir, "01_after_compare.png");

            DiffResult result = _session != null ? _session.LastResult : null;
            if (result == null || result.Items == null || result.Items.Count == 0)
            {
                auto.WriteLine("FAIL no result items");
                return 1;
            }

            // 1) シート切替: 差分のあるシートへ
            // 画像が多いシートを優先（ストリームが長くスクロール可能）
            string[] probeSheets = { "S_Img8v9", "S_ImgPartial", "S_TableDel", "S_Bg", "S_TableCell", "S_Common" };
            string workSheet = null;
            DiffItem workItem = null;
            foreach (string sn in probeSheets)
            {
                DiffItem it = result.Items.FirstOrDefault(i =>
                    i != null
                    && (string.Equals(i.SheetLeft, sn, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(i.SheetRight, sn, StringComparison.OrdinalIgnoreCase)));
                if (it != null)
                {
                    workSheet = sn;
                    workItem = it;
                    break;
                }
            }

            if (workSheet == null)
            {
                workItem = result.Items.FirstOrDefault(i => i != null && i.Kind != DiffKind.Structure);
                workSheet = workItem != null
                    ? (workItem.SheetLeft ?? workItem.SheetRight)
                    : null;
            }

            if (string.IsNullOrEmpty(workSheet))
            {
                auto.WriteLine("FAIL no probe sheet");
                return 1;
            }

            LeftPane.TrySelectSheet(workSheet);
            RightPane.TrySelectSheet(workSheet);
            RefreshMiniMapForCurrentSheet();
            await System.Threading.Tasks.Task.Delay(400);
            Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
            await System.Threading.Tasks.Task.Delay(200);

            string ls = LeftPane.SelectedSheetName ?? string.Empty;
            string rs = RightPane.SelectedSheetName ?? string.Empty;
            auto.WriteLine("SHEET work=" + workSheet + " L=" + ls + " R=" + rs);
            if (!string.Equals(ls, workSheet, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(rs, workSheet, StringComparison.OrdinalIgnoreCase))
            {
                auto.WriteLine("FAIL sheet select");
                failures++;
            }

            CaptureEvidenceShot(evidenceDir, "02_sheet_" + workSheet + ".png");

            // 2) 内容ストリームが左右に載っているか
            int leftPairs = LeftPane != null && LeftPane.ContentHostControl != null
                ? LeftPane.ContentHostControl.PairCount
                : 0;
            int rightPairs = RightPane != null && RightPane.ContentHostControl != null
                ? RightPane.ContentHostControl.PairCount
                : 0;
            auto.WriteLine("STREAM pairs L=" + leftPairs + " R=" + rightPairs);
            if (leftPairs <= 0 || rightPairs <= 0)
            {
                auto.WriteLine("FAIL empty content stream");
                failures++;
            }

            // 3) 左右スクロール同期（スクロール可能な場合）と選択 index 同期
            int maxPairs = Math.Max(leftPairs, rightPairs);
            int midIndex = Math.Max(0, maxPairs / 2);
            int lastIndex = Math.Max(0, maxPairs - 1);
            bool leftScrollable = LeftPane != null
                && LeftPane.ContentHostControl != null
                && LeftPane.ContentHostControl.PairCount > 3;
            auto.WriteLine("SCROLL_CAPABLE pairs=" + maxPairs + " mid=" + midIndex);

            // index ジャンプで左右が同じ SelectedPairIndex になること
            bool idxJumpL = LeftPane != null && LeftPane.ScrollContentToPairIndex(lastIndex);
            bool idxJumpR = RightPane != null && RightPane.ScrollContentToPairIndex(lastIndex);
            await System.Threading.Tasks.Task.Delay(250);
            int selL = LeftPane != null && LeftPane.ContentHostControl != null
                ? LeftPane.ContentHostControl.SelectedPairIndex
                : -1;
            int selR = RightPane != null && RightPane.ContentHostControl != null
                ? RightPane.ContentHostControl.SelectedPairIndex
                : -1;
            auto.WriteLine("PAIR_SELECT last=" + lastIndex
                + " selL=" + selL + " selR=" + selR
                + " jumpL=" + idxJumpL + " jumpR=" + idxJumpR);
            if (selL != lastIndex || selR != lastIndex)
            {
                auto.WriteLine("FAIL pair index select");
                failures++;
            }
            else
            {
                auto.WriteLine("PAIR_SELECT_OK");
            }

            // 比率同期（スクロール可能なとき）
            _syncingContentScroll = false;
            if (LeftPane != null)
            {
                LeftPane.SetContentScrollRatio(0.0);
            }

            if (RightPane != null)
            {
                RightPane.SetContentScrollRatio(0.0);
            }

            await System.Threading.Tasks.Task.Delay(100);
            if (LeftPane != null)
            {
                LeftPane.SetContentScrollRatio(0.75);
                OnLeftContentScrollRatioChanged(0.75);
            }

            await System.Threading.Tasks.Task.Delay(200);
            double lr = LeftPane != null ? LeftPane.GetContentScrollRatio() : -1;
            double rr = RightPane != null ? RightPane.GetContentScrollRatio() : -1;
            auto.WriteLine("SCROLL_SYNC L=" + lr.ToString("0.###", CultureInfo.InvariantCulture)
                + " R=" + rr.ToString("0.###", CultureInfo.InvariantCulture)
                + " scrollable=" + leftScrollable);
            if (leftScrollable)
            {
                if (Math.Abs(lr - rr) > 0.1)
                {
                    auto.WriteLine("FAIL scroll sync |L-R|="
                        + Math.Abs(lr - rr).ToString("0.###", CultureInfo.InvariantCulture));
                    failures++;
                }
                else if (lr < 0.3)
                {
                    auto.WriteLine("FAIL scroll did not move L=" + lr.ToString("0.###", CultureInfo.InvariantCulture));
                    failures++;
                }
                else
                {
                    auto.WriteLine("SCROLL_SYNC_OK");
                }
            }
            else
            {
                // 短いシートは index 同期で代替済み
                auto.WriteLine("SCROLL_SYNC_SKIP short stream (pair select covers sync)");
            }

            CaptureEvidenceShot(evidenceDir, "03_scroll_sync.png");

            // 4) MiniMap: 現在シート差分が 1 件以上あること
            var sheetItems = result.Items
                .Where(i => i != null && ItemBelongsToFocusSheet(i, workSheet))
                .ToList();
            auto.WriteLine("MINIMAP_ITEMS sheet=" + workSheet + " count=" + sheetItems.Count);
            if (sheetItems.Count == 0)
            {
                // Structure のみのシートを避けて別シートを探す
                foreach (string sn in probeSheets)
                {
                    var alt = result.Items.Where(i => i != null && ItemBelongsToFocusSheet(i, sn)).ToList();
                    if (alt.Count > 0)
                    {
                        workSheet = sn;
                        sheetItems = alt;
                        LeftPane.TrySelectSheet(workSheet);
                        RightPane.TrySelectSheet(workSheet);
                        RefreshMiniMapForCurrentSheet();
                        await System.Threading.Tasks.Task.Delay(300);
                        auto.WriteLine("MINIMAP_ITEMS fallback sheet=" + workSheet + " count=" + sheetItems.Count);
                        break;
                    }
                }
            }

            if (sheetItems.Count == 0)
            {
                auto.WriteLine("FAIL minimap no items for sheet");
                failures++;
            }

            // 5) MiniMap ジャンプ: 各差分へ飛んで左右のストリーム比率が揃うこと
            int jumpOk = 0;
            int jumpTry = 0;
            int shot = 0;
            foreach (DiffItem it in sheetItems.Take(6))
            {
                jumpTry++;
                // 一度端へ
                if (LeftPane != null)
                {
                    LeftPane.SetContentScrollRatio(0.0);
                }

                if (RightPane != null)
                {
                    RightPane.SetContentScrollRatio(0.0);
                }

                await System.Threading.Tasks.Task.Delay(100);

                OnMiniMapNavigate(0.5, it);
                // BeginInvoke Loaded 待ち
                await System.Threading.Tasks.Task.Delay(500);
                Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                await System.Threading.Tasks.Task.Delay(200);

                double jl = LeftPane != null ? LeftPane.GetContentScrollRatio() : -1;
                double jr = RightPane != null ? RightPane.GetContentScrollRatio() : -1;
                int idxL = LeftPane != null ? LeftPane.FindContentPairIndex(it) : -1;
                int idxR = RightPane != null ? RightPane.FindContentPairIndex(it) : -1;
                int selL2 = LeftPane != null && LeftPane.ContentHostControl != null
                    ? LeftPane.ContentHostControl.SelectedPairIndex
                    : -1;
                int selR2 = RightPane != null && RightPane.ContentHostControl != null
                    ? RightPane.ContentHostControl.SelectedPairIndex
                    : -1;
                bool ratioAligned = Math.Abs(jl - jr) <= 0.15;
                // 成功条件: 左右の選択 index が一致し、かつ解決した index と一致（または両方 -1 で比率一致）
                bool selectionOk = selL2 >= 0 && selL2 == selR2
                    && (idxL < 0 || selL2 == idxL || Math.Abs(selL2 - idxL) <= 1);
                bool ok = ratioAligned && (selectionOk || (idxL >= 0 && idxR >= 0 && idxL == idxR));
                auto.WriteLine("MINIMAP_JUMP kind=" + it.Kind
                    + " summary=" + (it.Summary ?? string.Empty).Replace('\n', ' ')
                    + " idxL=" + idxL + " idxR=" + idxR
                    + " selL=" + selL2 + " selR=" + selR2
                    + " L=" + jl.ToString("0.###", CultureInfo.InvariantCulture)
                    + " R=" + jr.ToString("0.###", CultureInfo.InvariantCulture)
                    + " ok=" + ok);
                if (ok)
                {
                    jumpOk++;
                }

                shot++;
                CaptureEvidenceShot(evidenceDir, string.Format(
                    CultureInfo.InvariantCulture,
                    "04_minimap_jump_{0:00}_{1}.png",
                    shot,
                    it.Kind));
            }

            auto.WriteLine("MINIMAP_JUMP_SUMMARY ok=" + jumpOk + "/" + jumpTry);
            if (jumpTry == 0 || jumpOk < Math.Max(1, jumpTry - 1))
            {
                // 1 件までは許容、大半失敗は NG
                auto.WriteLine("FAIL minimap jump coverage");
                failures++;
            }
            else
            {
                auto.WriteLine("MINIMAP_JUMP_OK");
            }

            // 6) 比率クリック相当（item null）
            OnMiniMapNavigate(0.85, null);
            await System.Threading.Tasks.Task.Delay(400);
            Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
            await System.Threading.Tasks.Task.Delay(150);
            double rl = LeftPane != null ? LeftPane.GetContentScrollRatio() : -1;
            double rr2 = RightPane != null ? RightPane.GetContentScrollRatio() : -1;
            auto.WriteLine("MINIMAP_RATIO_CLICK L=" + rl.ToString("0.###", CultureInfo.InvariantCulture)
                + " R=" + rr2.ToString("0.###", CultureInfo.InvariantCulture));
            if (Math.Abs(rl - rr2) > 0.12)
            {
                auto.WriteLine("FAIL minimap ratio click sync");
                failures++;
            }
            else if (rl < 0.5)
            {
                // 0.85 へ飛んだはず
                auto.WriteLine("WARN ratio click may not have moved far enough L=" + rl.ToString("0.###", CultureInfo.InvariantCulture));
            }
            else
            {
                auto.WriteLine("MINIMAP_RATIO_OK");
            }

            CaptureEvidenceShot(evidenceDir, "05_minimap_ratio.png");

            // 7) ハイライト ON/OFF（画像領域）
            BtnHighlightToggle.IsChecked = false;
            await System.Threading.Tasks.Task.Delay(100);
            CaptureEvidenceShot(evidenceDir, "06_highlight_off.png");
            BtnHighlightToggle.IsChecked = true;
            await System.Threading.Tasks.Task.Delay(100);
            CaptureEvidenceShot(evidenceDir, "07_highlight_on.png");
            CaptureEvidenceShot(evidenceDir, "99_final.png");

            auto.WriteLine("CONTENT_DIFF_VERIFY done failures=" + failures
                + " evidence=" + (evidenceDir ?? "(none)"));
            return failures;
        }

        /// <summary>
        /// 内容ベース縦スクロール同期と横の 1:1 同期を検証する。
        /// content_scroll サンプル時は expected 駆動の SC_画像ギャップ ライブ検証へ分岐する。
        /// </summary>
        private async System.Threading.Tasks.Task<int> VerifyContentScrollSyncAsync(AutoLiveTestOptions auto)
        {
            if (_scrollSync == null || _session.LastResult == null)
            {
                auto.WriteLine("FAIL content-scroll: scrollSync or result null");
                return 1;
            }

            bool contentScroll = IsContentScrollSamplePath(auto.LeftPath)
                || IsContentScrollSamplePath(auto.RightPath);
            if (contentScroll)
            {
                return await VerifyContentScrollPerfectLiveAsync(auto);
            }

            return await VerifyContentScrollFullFeatureAsync(auto);
        }

        /// <summary>
        /// content_scroll 専用: Alignments >= 4、SC_画像ギャップ の hold/resync 実スクロール、横 1:1。
        /// </summary>
        private async System.Threading.Tasks.Task<int> VerifyContentScrollPerfectLiveAsync(AutoLiveTestOptions auto)
        {
            int failures = 0;
            int alignments = _session.LastResult.Alignments != null
                ? _session.LastResult.Alignments.Count
                : 0;
            ContentScrollMapSet maps = ContentScrollMapSet.FromAlignments(_session.LastResult.Alignments);
            int mapCount = maps != null ? maps.Count : 0;
            auto.WriteLine("CONTENT_SCROLL_PERFECT maps=" + mapCount + " alignments=" + alignments);
            if (alignments < 4)
            {
                auto.WriteLine("FAIL content_scroll Alignments.Count expected >= 4 got " + alignments);
                failures++;
            }

            const string gapSheet = "SC_画像ギャップ";
            LeftPane.TrySelectSheet(gapSheet);
            RightPane.TrySelectSheet(gapSheet);
            RefreshScrollSyncActiveSheets();
            await System.Threading.Tasks.Task.Delay(300);

            ContentScrollMap gapMap = _scrollSync.ActiveMap;
            auto.WriteLine("GAP_MAP " + (gapMap != null ? gapMap.Describe() : "null"));
            if (gapMap == null || !gapMap.IsContentBased)
            {
                auto.WriteLine("FAIL SC_画像ギャップ map not content-based");
                failures++;
            }
            else
            {
                // expected: L5→5, R9 max≤7, L8→12, R12→8
                int mL5 = gapMap.MapLeftToRight(5);
                int mR9 = gapMap.MapRightToLeft(9);
                int mL8 = gapMap.MapLeftToRight(8);
                int mR12 = gapMap.MapRightToLeft(12);
                auto.WriteLine("GAP_MAP_POINTS L5→R" + mL5 + " R9→L" + mR9
                    + " L8→R" + mL8 + " R12→L" + mR12);
                if (mL5 != 5)
                {
                    auto.WriteLine("FAIL map L5 expect 5 got " + mL5);
                    failures++;
                }

                if (mR9 > 7)
                {
                    auto.WriteLine("FAIL map R9 hold expectOtherMax=7 got " + mR9);
                    failures++;
                }

                if (mL8 != 12)
                {
                    auto.WriteLine("FAIL map L8 expect 12 got " + mL8);
                    failures++;
                }

                if (mR12 != 8)
                {
                    auto.WriteLine("FAIL map R12 expect 8 got " + mR12);
                    failures++;
                }
            }

            // 画像対応: L5↔R5 exact, L8↔R12 exact, rightOnly R8
            SheetAlignment gapAl = _session.LastResult.Alignments != null
                ? _session.LastResult.Alignments.FirstOrDefault(a =>
                    string.Equals(a.LeftSheet, gapSheet, StringComparison.OrdinalIgnoreCase))
                : null;
            if (gapAl == null || gapAl.Images == null)
            {
                auto.WriteLine("FAIL SC_画像ギャップ Images missing");
                failures++;
            }
            else
            {
                bool p5 = gapAl.Images.Any(c => c.IsPaired && c.IsExactHashMatch
                    && ImageRowStart(c.Left) == 5 && ImageRowStart(c.Right) == 5);
                bool p812 = gapAl.Images.Any(c => c.IsPaired && c.IsExactHashMatch
                    && ImageRowStart(c.Left) == 8 && ImageRowStart(c.Right) == 12);
                bool onlyR8 = gapAl.Images.Any(c => c.IsRightOnly && ImageRowStart(c.Right) == 8);
                auto.WriteLine("GAP_IMAGES p5=" + p5 + " p8_12=" + p812 + " onlyR8=" + onlyR8);
                if (!p5 || !p812 || !onlyR8)
                {
                    auto.WriteLine("FAIL SC_画像ギャップ imagePairs mismatch");
                    failures++;
                }
            }

            // 実スクロール: 右 only 画像 RowStart=8（中央）→ 左 hold <=7
            // expected R9 max<=7; ライブは R8 付近でも hold
            _scrollSync.Suspend();
            try
            {
                const int rightOnlyRow = 8;
                const int expectLeftMax = 7;
                if (RightPane.IsOpen)
                {
                    RightPane.TrySetScroll(rightOnlyRow, 1);
                }

                await System.Threading.Tasks.Task.Delay(200);
                int mappedLeft = _scrollSync.MapRightToLeft(rightOnlyRow);
                if (LeftPane.IsOpen)
                {
                    LeftPane.TrySetScroll(mappedLeft, 1);
                }

                await System.Threading.Tasks.Task.Delay(400);
                int lr = 0, rr = 0, c;
                bool lok = LeftPane.TryGetScroll(out lr, out c);
                bool rok = RightPane.TryGetScroll(out rr, out c);
                auto.WriteLine("GAP_SCROLL_RIGHT_ONLY Lsr=" + lr + " Rsr=" + rr
                    + " mappedL=" + mappedLeft + " lok=" + lok + " rok=" + rok);
                if (!rok || Math.Abs(rr - rightOnlyRow) > 2)
                {
                    auto.WriteLine("FAIL right scroll to only-image row " + rightOnlyRow);
                    failures++;
                }
                else if (!lok || lr > expectLeftMax)
                {
                    auto.WriteLine("FAIL left should hold <= " + expectLeftMax
                        + " while right at only image (L=" + lr + ")");
                    failures++;
                }
                else
                {
                    auto.WriteLine("GAP_RIGHT_ONLY_SCROLL_OK");
                }

                // same_B: 右 row12 → 左 row8（±2）
                const int sameBRight = 12;
                const int sameBLeft = 8;
                if (RightPane.IsOpen)
                {
                    RightPane.TrySetScroll(sameBRight, 1);
                }

                int mappedLeftB = _scrollSync.MapRightToLeft(sameBRight);
                if (LeftPane.IsOpen)
                {
                    LeftPane.TrySetScroll(mappedLeftB, 1);
                }

                await System.Threading.Tasks.Task.Delay(400);
                lok = LeftPane.TryGetScroll(out lr, out c);
                rok = RightPane.TryGetScroll(out rr, out c);
                auto.WriteLine("GAP_SCROLL_SAME_B Lsr=" + lr + " Rsr=" + rr + " mappedL=" + mappedLeftB);
                if (!lok || !rok
                    || Math.Abs(lr - sameBLeft) > 2
                    || Math.Abs(rr - sameBRight) > 2)
                {
                    auto.WriteLine("FAIL resync at same_B L" + sameBLeft + "↔R" + sameBRight
                        + " got L=" + lr + " R=" + rr);
                    failures++;
                }
                else
                {
                    auto.WriteLine("GAP_SAME_B_SCROLL_OK");
                }
            }
            finally
            {
                _scrollSync.NotifyExternalScroll(
                    LeftPane != null ? LeftPane.LastKnownScrollRow : 1,
                    RightPane != null ? RightPane.LastKnownScrollRow : 1);
                _scrollSync.Resume();
            }

            // UX light: StatusLine 非空 + JumpHint 経路（ギャップ→Equal Apply）
            try
            {
                await System.Threading.Tasks.Task.Delay(150);
                // 右のみ帯 → same_B で Publish（COM なしでも State 更新）
                _scrollSync.ApplyDrivenByRight(9, 1);
                await System.Threading.Tasks.Task.Delay(50);
                SyncSessionState gapState = _scrollSync.CurrentState;
                auto.WriteLine("STATUS_LINE_GAP " + (gapState != null
                    ? (gapState.StatusLine ?? "(null)")
                    : "(no state)"));
                if (gapState == null || string.IsNullOrEmpty(gapState.StatusLine))
                {
                    auto.WriteLine("FAIL StatusLine empty after right-only Apply");
                    failures++;
                }
                else
                {
                    auto.WriteLine("STATUS_LINE_OK");
                }

                _scrollSync.ApplyDrivenByRight(12, 1);
                await System.Threading.Tasks.Task.Delay(50);
                SyncSessionState jumpState = _scrollSync.CurrentState;
                string jh = jumpState != null ? jumpState.JumpHint : null;
                auto.WriteLine("JUMP_HINT_PATH JumpHint=" + (jh ?? "(null)")
                    + " Status=" + (jumpState != null ? (jumpState.StatusLine ?? "(null)") : "n/a")
                    + " Kind=" + (jumpState != null ? jumpState.SegmentKind.ToString() : "n/a"));
                if (jumpState == null || string.IsNullOrEmpty(jumpState.StatusLine))
                {
                    auto.WriteLine("FAIL StatusLine empty after resync Apply");
                    failures++;
                }
                else
                {
                    auto.WriteLine("JUMP_HINT_PATH_OK status_nonempty");
                    if (!string.IsNullOrEmpty(jh))
                    {
                        auto.WriteLine("JUMP_HINT_SET " + jh);
                    }
                }
            }
            catch (Exception ex)
            {
                auto.WriteLine("FAIL StatusLine/JumpHint light check: " + ex.Message);
                failures++;
            }

            // 横: SC_横同期 で col=5 1:1
            LeftPane.TrySelectSheet("SC_横同期");
            RightPane.TrySelectSheet("SC_横同期");
            RefreshScrollSyncActiveSheets();
            await System.Threading.Tasks.Task.Delay(200);
            _scrollSync.Suspend();
            try
            {
                if (LeftPane.IsOpen)
                {
                    LeftPane.TrySetScroll(5, 5);
                }

                await System.Threading.Tasks.Task.Delay(200);
                int mappedR = _scrollSync.MapLeftToRight(5);
                if (RightPane.IsOpen)
                {
                    RightPane.TrySetScroll(mappedR, 5);
                }

                await System.Threading.Tasks.Task.Delay(400);
                int lr2 = 0, lc2 = 0, rr2 = 0, rc2 = 0;
                bool lok2 = LeftPane.TryGetScroll(out lr2, out lc2);
                bool rok2 = RightPane.TryGetScroll(out rr2, out rc2);
                auto.WriteLine("HSCROLL L=" + lr2 + "," + lc2 + " R=" + rr2 + "," + rc2 + " mappedR=" + mappedR);
                if (!lok2 || !rok2 || lc2 != 5 || rc2 != 5)
                {
                    auto.WriteLine("FAIL horizontal scroll columns not 1:1");
                    failures++;
                }
                else
                {
                    auto.WriteLine("HSCROLL_OK");
                }
            }
            finally
            {
                _scrollSync.NotifyExternalScroll(
                    LeftPane != null ? LeftPane.LastKnownScrollRow : 1,
                    RightPane != null ? RightPane.LastKnownScrollRow : 1);
                _scrollSync.Resume();
            }

            auto.WriteLine(failures == 0
                ? "CONTENT_SCROLL_PERFECT_OK"
                : "CONTENT_SCROLL_PERFECT_FAIL failures=" + failures);
            return failures;
        }

        private static int ImageRowStart(EmbeddedImage img)
        {
            if (img == null)
            {
                return -1;
            }

            if (img.Anchor != null && img.Anchor.RowStart >= 1)
            {
                return img.Anchor.RowStart;
            }

            return img.AnchorRow > 0 ? img.AnchorRow : -1;
        }

        /// <summary>
        /// full_feature 向け: 製品カタログ / ずれ試験 / 横 1:1。
        /// </summary>
        private async System.Threading.Tasks.Task<int> VerifyContentScrollFullFeatureAsync(AutoLiveTestOptions auto)
        {
            int failures = 0;

            // マップが比較結果に載っていること（Alignments 優先）
            ContentScrollMapSet maps = ContentScrollMapSet.FromAlignments(_session.LastResult.Alignments);
            if (maps.Count == 0)
            {
                maps = _session.LastResult.ScrollMaps;
            }

            int mapCount = maps != null ? maps.Count : 0;
            auto.WriteLine("CONTENT_SCROLL maps=" + mapCount
                + " alignments=" + (_session.LastResult.Alignments != null ? _session.LastResult.Alignments.Count : 0));
            if (mapCount < 1)
            {
                auto.WriteLine("FAIL content-scroll maps empty");
                failures++;
            }

            // --- 製品カタログ: 画像対応（左 row4↔右4, 左8↔右8、片側のみはギャップ）---
            LeftPane.TrySelectSheet("製品カタログ");
            RightPane.TrySelectSheet("製品カタログ");
            RefreshScrollSyncActiveSheets();
            await System.Threading.Tasks.Task.Delay(300);

            ContentScrollMap catalogMap = _scrollSync.ActiveMap;
            auto.WriteLine("CATALOG_MAP " + (catalogMap != null ? catalogMap.Describe() : "null"));
            if (catalogMap == null || !catalogMap.IsContentBased)
            {
                auto.WriteLine("FAIL catalog map not content-based");
                failures++;
            }
            else
            {
                // 同一画像: 左4 → 右4、左8 → 右8
                int r4 = catalogMap.MapLeftToRight(4);
                int r8 = catalogMap.MapLeftToRight(8);
                int l4 = catalogMap.MapRightToLeft(4);
                int l8 = catalogMap.MapRightToLeft(8);
                auto.WriteLine("CATALOG_MAP_POINTS L4→R" + r4 + " L8→R" + r8 + " R4→L" + l4 + " R8→L" + l8);

                if (r4 != 4 || l4 != 4)
                {
                    auto.WriteLine("FAIL catalog map first image not aligned L4↔R4");
                    failures++;
                }

                if (r8 != 8 || l8 != 8)
                {
                    auto.WriteLine("FAIL catalog map last image not aligned L8↔R8");
                    failures++;
                }

                // 右のみ画像 (row7): 左はホールド（再同期 row8 より前。理想は 5〜6）
                int holdAtRightOnly = catalogMap.MapRightToLeft(7);
                auto.WriteLine("CATALOG_RIGHT_ONLY R7→L" + holdAtRightOnly);
                if (holdAtRightOnly >= 7)
                {
                    auto.WriteLine("FAIL right-only image should hold left BEFORE row7 (got " + holdAtRightOnly + ")");
                    failures++;
                }

                // 左のみ画像 (row6): 右はホールド（row7 の右のみより前）
                int holdAtLeftOnly = catalogMap.MapLeftToRight(6);
                auto.WriteLine("CATALOG_LEFT_ONLY L6→R" + holdAtLeftOnly);
                if (holdAtLeftOnly >= 7)
                {
                    auto.WriteLine("FAIL left-only image should hold right BEFORE row7 (got " + holdAtLeftOnly + ")");
                    failures++;
                }
            }

            // 実スクロール: 右を row7（片側のみ）へ → 左は 8 未満に留まる
            _scrollSync.Suspend();
            try
            {
                if (RightPane.IsOpen)
                {
                    RightPane.TrySetScroll(7, 1);
                }

                await System.Threading.Tasks.Task.Delay(200);
                int mappedLeft = _scrollSync.MapRightToLeft(7);
                if (LeftPane.IsOpen)
                {
                    LeftPane.TrySetScroll(mappedLeft, 1);
                }

                await System.Threading.Tasks.Task.Delay(400);
                int lr = 0, rr = 0, c;
                bool lok = LeftPane.TryGetScroll(out lr, out c);
                bool rok = RightPane.TryGetScroll(out rr, out c);
                auto.WriteLine("CATALOG_SCROLL_RIGHT_ONLY Lsr=" + lr + " Rsr=" + rr + " mappedL=" + mappedLeft
                    + " lok=" + lok + " rok=" + rok);
                if (!rok || Math.Abs(rr - 7) > 2)
                {
                    auto.WriteLine("FAIL right scroll to row7");
                    failures++;
                }
                else if (!lok || lr >= 7)
                {
                    // 右だけ画像のあいだ、左は行7未満に留まり「空白が続く」状態
                    auto.WriteLine("FAIL left should hold before row7 while right at right-only image (L=" + lr + ")");
                    failures++;
                }
                else
                {
                    auto.WriteLine("CATALOG_RIGHT_ONLY_SCROLL_OK");
                }

                // 同一画像 row8 で再同期
                if (RightPane.IsOpen)
                {
                    RightPane.TrySetScroll(8, 1);
                }

                int mappedLeft8 = _scrollSync.MapRightToLeft(8);
                if (LeftPane.IsOpen)
                {
                    LeftPane.TrySetScroll(mappedLeft8, 1);
                }

                await System.Threading.Tasks.Task.Delay(400);
                lok = LeftPane.TryGetScroll(out lr, out c);
                rok = RightPane.TryGetScroll(out rr, out c);
                auto.WriteLine("CATALOG_SCROLL_MATCH Lsr=" + lr + " Rsr=" + rr + " mappedL=" + mappedLeft8);
                if (!lok || !rok || Math.Abs(lr - 8) > 2 || Math.Abs(rr - 8) > 2)
                {
                    auto.WriteLine("FAIL resync at matching image row8");
                    failures++;
                }
                else
                {
                    auto.WriteLine("CATALOG_MATCH_SCROLL_OK");
                }
            }
            finally
            {
                _scrollSync.NotifyExternalScroll(
                    LeftPane != null ? LeftPane.LastKnownScrollRow : 1,
                    RightPane != null ? RightPane.LastKnownScrollRow : 1);
                _scrollSync.Resume();
            }

            // --- ずれ試験: 右に挿入行あり。S10「結果を記録する」は 左14 ↔ 右16 ---
            LeftPane.TrySelectSheet("ずれ試験");
            RightPane.TrySelectSheet("ずれ試験");
            RefreshScrollSyncActiveSheets();
            await System.Threading.Tasks.Task.Delay(300);
            ContentScrollMap shiftMap = _scrollSync.ActiveMap;
            auto.WriteLine("SHIFT_MAP " + (shiftMap != null ? shiftMap.Describe() : "null"));
            if (shiftMap != null && shiftMap.IsContentBased)
            {
                // 左 row14 = S10 → 右は挿入のぶん下がって 16
                int rForL14 = shiftMap.MapLeftToRight(14);
                int lForR16 = shiftMap.MapRightToLeft(16);
                // 右の挿入行 (S03a at row8) では左をホールド
                int lForR8 = shiftMap.MapRightToLeft(8);
                auto.WriteLine("SHIFT_MAP_POINTS L14→R" + rForL14 + " R16→L" + lForR16 + " R8→L" + lForR8);
                if (Math.Abs(rForL14 - 16) > 1 || Math.Abs(lForR16 - 14) > 1)
                {
                    auto.WriteLine("FAIL shift map S10 should align L14↔R16 (content)");
                    failures++;
                }
                else if (lForR8 >= 14)
                {
                    auto.WriteLine("FAIL shift map insert row should hold left before late anchors");
                    failures++;
                }
                else
                {
                    auto.WriteLine("SHIFT_MAP_OK");
                }
            }
            else
            {
                auto.WriteLine("FAIL shift map not content-based");
                failures++;
            }

            // --- 横スクロール: 列は 1:1 ---
            LeftPane.TrySelectSheet("長い一覧");
            RightPane.TrySelectSheet("長い一覧");
            RefreshScrollSyncActiveSheets();
            await System.Threading.Tasks.Task.Delay(200);
            _scrollSync.Suspend();
            try
            {
                if (LeftPane.IsOpen)
                {
                    LeftPane.TrySetScroll(10, 5);
                }

                await System.Threading.Tasks.Task.Delay(200);
                // 同期サービス相当: 横は同列、縦はマップ
                int mappedR = _scrollSync.MapLeftToRight(10);
                if (RightPane.IsOpen)
                {
                    RightPane.TrySetScroll(mappedR, 5);
                }

                await System.Threading.Tasks.Task.Delay(400);
                int lr2 = 0, lc2 = 0, rr2 = 0, rc2 = 0;
                bool lok2 = LeftPane.TryGetScroll(out lr2, out lc2);
                bool rok2 = RightPane.TryGetScroll(out rr2, out rc2);
                auto.WriteLine("HSCROLL L=" + lr2 + "," + lc2 + " R=" + rr2 + "," + rc2 + " mappedR=" + mappedR);
                if (!lok2 || !rok2 || lc2 != 5 || rc2 != 5)
                {
                    auto.WriteLine("FAIL horizontal scroll columns not 1:1");
                    failures++;
                }
                else
                {
                    auto.WriteLine("HSCROLL_OK");
                }
            }
            finally
            {
                _scrollSync.NotifyExternalScroll(
                    LeftPane != null ? LeftPane.LastKnownScrollRow : 1,
                    RightPane != null ? RightPane.LastKnownScrollRow : 1);
                _scrollSync.Resume();
            }

            auto.WriteLine(failures == 0 ? "CONTENT_SCROLL_OK" : "CONTENT_SCROLL_FAIL failures=" + failures);
            return failures;
        }

        /// <summary>
        /// 起動画面から比較開始。
        /// </summary>
        private async void OnStartCompareRequested(string leftPath, string rightPath)
        {
            await OpenAndCompareAsync(leftPath, rightPath, resetOptions: true);
        }

        /// <summary>
        /// 再比較。
        /// </summary>
        private async void BtnRecompare_Click(object sender, RoutedEventArgs e)
        {
            if (!_session.HasBothPaths)
            {
                MessageBox.Show("比較対象ファイルがありません。", Common.AppDisplayName);
                return;
            }

            await RunCompareOnlyAsync();
        }

        /// <summary>
        /// シート対応。
        /// </summary>
        private async void BtnSheetMap_Click(object sender, RoutedEventArgs e)
        {
            if (!LeftPane.IsOpen || !RightPane.IsOpen)
            {
                MessageBox.Show("左右のブックを開いてから実行してください。", Common.AppDisplayName);
                return;
            }

            IReadOnlyList<string> leftSheets = LeftPane.GetSheetNames();
            IReadOnlyList<string> rightSheets = RightPane.GetSheetNames();
            if ((leftSheets == null || leftSheets.Count == 0) && _session.LastResult != null && _session.LastResult.LeftContent != null)
            {
                leftSheets = _session.LastResult.LeftContent.Sheets
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                    .Select(s => s.Name)
                    .ToList();
            }

            if ((rightSheets == null || rightSheets.Count == 0) && _session.LastResult != null && _session.LastResult.RightContent != null)
            {
                rightSheets = _session.LastResult.RightContent.Sheets
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                    .Select(s => s.Name)
                    .ToList();
            }

            if (leftSheets == null || leftSheets.Count == 0 || rightSheets == null || rightSheets.Count == 0)
            {
                MessageBox.Show("シート一覧を取得できません。先に比較を実行してください。", Common.AppDisplayName);
                return;
            }

            IList<SheetPair> existing = null;
            if (_session.Options != null && _session.Options.ManualSheetPairs != null
                && _session.Options.ManualSheetPairs.Count > 0)
            {
                existing = _session.Options.ManualSheetPairs;
            }
            else if (_session.LastResult != null)
            {
                existing = GetActiveSheetPairs(_session.LastResult);
            }

            var dialog = new SheetMapDialog(leftSheets.ToList(), rightSheets.ToList(), existing) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (dialog.ResetToSameName)
                {
                    _session.Options.ManualSheetPairs = null;
                }
                else if (dialog.ResultPairs != null)
                {
                    _session.Options.ManualSheetPairs = dialog.ResultPairs;
                }

                await RunCompareOnlyAsync();
            }
        }

        /// <summary>
        /// 左右ペインの現在選択を ManualSheetPairs にして再比較する。
        /// </summary>
        private async void BtnCompareCurrentPair_Click(object sender, RoutedEventArgs e)
        {
            if (!_session.HasBothPaths)
            {
                MessageBox.Show("比較対象ファイルがありません。", Common.AppDisplayName);
                return;
            }

            string left = LeftPane != null ? LeftPane.SelectedSheetName : null;
            string right = RightPane != null ? RightPane.SelectedSheetName : null;
            if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
            {
                MessageBox.Show("左右のどちらかのシートを選択してください。", Common.AppDisplayName);
                return;
            }

            // 片側のみも許可（Structure + 片側表示）
            _session.Options.ManualSheetPairs = new List<SheetPair>
            {
                new SheetPair
                {
                    LeftSheet = left,
                    RightSheet = right,
                    IsManual = true
                }
            };

            StatusText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "手動ペアで再比較: {0} ↔ {1}",
                string.IsNullOrEmpty(left) ? "（なし）" : left,
                string.IsNullOrEmpty(right) ? "（なし）" : right);
            Log.Info("この組み合わせで比較 L=" + (left ?? "") + " R=" + (right ?? ""));
            await RunCompareOnlyAsync();
        }

        /// <summary>
        /// 手動ピン適用後、指定シートの Match + SheetAlignment のみ再構築する。
        /// </summary>
        private void RebuildSheetAlignmentForPins(
            string leftSheet,
            string rightSheet,
            IList<EmbeddedImage> leftImages,
            IList<EmbeddedImage> rightImages)
        {
            DiffResult result = _session.LastResult;
            if (result == null)
            {
                return;
            }

            IList<ManualImagePin> sheetPins = GetPinsForSheet(leftSheet, rightSheet);
            IList<ImageCorrespondence> corr = ImageCorrespondenceService.Match(
                leftImages, rightImages, sheetPins);

            List<CellValue> leftCells = new List<CellValue>();
            List<CellValue> rightCells = new List<CellValue>();
            try
            {
                if (!string.IsNullOrEmpty(_session.LeftPath) && File.Exists(_session.LeftPath))
                {
                    using (var reader = XlsxPackageReader.Open(_session.LeftPath))
                    {
                        leftCells = reader.EnumerateCells(leftSheet).ToList();
                    }
                }

                if (!string.IsNullOrEmpty(_session.RightPath) && File.Exists(_session.RightPath))
                {
                    using (var reader = XlsxPackageReader.Open(_session.RightPath))
                    {
                        rightCells = reader.EnumerateCells(rightSheet).ToList();
                    }
                }
            }
            catch (Exception cellEx)
            {
                Log.Debug("ピン後セル再読込スキップ: " + cellEx.Message);
            }

            SheetAlignment rebuilt = SheetAlignmentBuilder.Build(
                leftSheet, rightSheet, leftCells, rightCells, corr);

            if (result.Alignments == null)
            {
                result.Alignments = new List<SheetAlignment>();
            }

            bool replaced = false;
            for (int i = 0; i < result.Alignments.Count; i++)
            {
                SheetAlignment a = result.Alignments[i];
                if (a == null)
                {
                    continue;
                }

                if (string.Equals(a.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase))
                {
                    result.Alignments[i] = rebuilt;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                result.Alignments.Add(rebuilt);
            }

            // ScrollMaps も Alignments から再構成
            result.ScrollMaps = ContentScrollMapSet.FromAlignments(result.Alignments)
                ?? new ContentScrollMapSet();

            ApplyContentScrollMaps(result);
            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "画像ピン適用: {0}/{1} pins={2} corr={3}",
                leftSheet,
                rightSheet,
                sheetPins != null ? sheetPins.Count : 0,
                corr != null ? corr.Count : 0));
        }

        private List<ManualImagePin> GetPinsForSheet(string leftSheet, string rightSheet)
        {
            if (_session.Options == null || _session.Options.ManualImagePins == null)
            {
                return new List<ManualImagePin>();
            }

            return _session.Options.ManualImagePins
                .Where(p => p != null
                    && string.Equals(p.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static List<EmbeddedImage> CollectSideImages(
            IList<ImageCorrespondence> images,
            bool left)
        {
            var list = new List<EmbeddedImage>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (images == null)
            {
                return list;
            }

            foreach (ImageCorrespondence c in images)
            {
                if (c == null)
                {
                    continue;
                }

                EmbeddedImage img = left ? c.Left : c.Right;
                if (img == null)
                {
                    continue;
                }

                string key = !string.IsNullOrEmpty(img.ContentHash)
                    ? "h:" + img.ContentHash
                    : "p:" + (img.ExtractedPath ?? img.FileName ?? Guid.NewGuid().ToString());
                if (seen.Add(key))
                {
                    list.Add(img);
                }
            }

            return list;
        }

        /// <summary>
        /// 左差し替え。
        /// </summary>
        private async void BtnReplaceLeft_Click(object sender, RoutedEventArgs e)
        {
            string path = PickXlsx("左のファイルを差し替え");
            if (path == null)
            {
                return;
            }

            _session.LeftPath = path;
            LeftPane.OpenFile(path);
            if (LeftPane.IsOpen)
            {
                await RunCompareOnlyAsync();
            }
        }

        /// <summary>
        /// 右差し替え。
        /// </summary>
        private async void BtnReplaceRight_Click(object sender, RoutedEventArgs e)
        {
            string path = PickXlsx("右のファイルを差し替え");
            if (path == null)
            {
                return;
            }

            _session.RightPath = path;
            RightPane.OpenFile(path);
            if (RightPane.IsOpen)
            {
                await RunCompareOnlyAsync();
            }
        }

        /// <summary>
        /// 差分強調トグル（Checked/Unchecked のみ。Click 併用は再入で StackOverflow になり得る）。
        /// MiniMap と画像ハイライト（赤枠＋黄塗り）を再比較なしで切替。
        /// </summary>
        private void BtnHighlightToggle_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingHighlightToggle || _highlightController == null)
            {
                return;
            }

            _updatingHighlightToggle = true;
            try
            {
                bool on = BtnHighlightToggle.IsChecked == true;
                _highlightController.SetVisible(on);
                ApplyImageHighlightVisible(on);
                if (on)
                {
                    RefreshMiniMapForCurrentSheet();
                }

                UpdateHighlightToggleCaption();
                StatusText.Text = on
                    ? "差分印: MiniMap（現在シート）/ 画像ハイライト表示"
                    : "差分印: 非表示（結果・画像は保持）";
            }
            finally
            {
                _updatingHighlightToggle = false;
            }
        }

        /// <summary>
        /// 左右 ContentPane の差分強調（画像枠・セル黄ハイライト）を切替（再比較不要）。
        /// </summary>
        /// <param name="visible">枠・塗り・セル黄を出すなら true</param>
        private void ApplyImageHighlightVisible(bool visible)
        {
            try
            {
                if (LeftPane != null && LeftPane.ContentHostControl != null)
                {
                    LeftPane.ContentHostControl.SetHighlightVisible(visible);
                }

                if (RightPane != null && RightPane.ContentHostControl != null)
                {
                    RightPane.ContentHostControl.SetHighlightVisible(visible);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ApplyImageHighlightVisible: " + ex.Message);
            }
        }

        /// <summary>
        /// 設定変更後に画像ハイライト色・線幅を再適用する。
        /// </summary>
        private void RefreshImageHighlightStyleFromSettings()
        {
            try
            {
                if (LeftPane != null && LeftPane.ContentHostControl != null)
                {
                    LeftPane.ContentHostControl.RefreshImageHighlightStyle();
                }

                if (RightPane != null && RightPane.ContentHostControl != null)
                {
                    RightPane.ContentHostControl.RefreshImageHighlightStyle();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("RefreshImageHighlightStyleFromSettings: " + ex.Message);
            }
        }

        /// <summary>
        /// 埋め込み Excel 上のホイールを COM スクロールへ（HwndHost は WPF イベントを受け取らない）。
        /// </summary>
        private void AttachMouseWheelFilter()
        {
            if (_wheelFilterAttached)
            {
                return;
            }

            ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
            _wheelFilterAttached = true;
        }

        private void DetachMouseWheelFilter()
        {
            if (!_wheelFilterAttached)
            {
                return;
            }

            ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
            _wheelFilterAttached = false;
        }

        private void OnThreadFilterMessage(ref MSG msg, ref bool handled)
        {
            if (handled)
            {
                return;
            }

            // 比較画面で左右が開いているときだけ
            if (MainCompareRoot == null || MainCompareRoot.Visibility != Visibility.Visible)
            {
                return;
            }

            if ((LeftPane == null || !LeftPane.IsOpen) && (RightPane == null || !RightPane.IsOpen))
            {
                return;
            }

            // --- 中ボタン / Alt+左 でドラッグパン ---
            if (TryHandlePanMessage(ref msg, ref handled))
            {
                return;
            }

            bool isVWheel = msg.message == NativeInput.WM_MOUSEWHEEL;
            bool isHWheel = msg.message == NativeInput.WM_MOUSEHWHEEL;
            if (!isVWheel && !isHWheel)
            {
                return;
            }

            int wheelDelta = unchecked((short)((msg.wParam.ToInt64() >> 16) & 0xFFFF));
            if (wheelDelta == 0)
            {
                return;
            }

            // Shift+縦ホイール → 横スクロール（一般的な UI 慣習）
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool horizontal = isHWheel || (isVWheel && shift);

            NativeInput.POINT pt;
            if (!NativeInput.GetCursorPos(out pt))
            {
                return;
            }

            var screen = new Point(pt.X, pt.Y);
            try
            {
                WorkbookPane pane = HitTestPane(screen);
                if (pane != null)
                {
                    // ScrollInteracted → ApplyDrivenBy* が同一 UI スレッドで相手側をマップ同期
                    if (pane.TryScrollByWheelDelta(wheelDelta, horizontal))
                    {
                        handled = true;
                        UpdateMiniMapViewportFromScroll(
                            LeftPane != null ? LeftPane.LastKnownScrollRow : 1,
                            RightPane != null ? RightPane.LastKnownScrollRow : 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("wheel filter: " + ex.Message);
            }
        }

        /// <summary>
        /// スレッド メッセージ経路のパン（LL フックと併用）。
        /// </summary>
        private bool TryHandlePanMessage(ref MSG msg, ref bool handled)
        {
            int m = msg.message;
            NativeInput.POINT pt;
            if (!NativeInput.GetCursorPos(out pt))
            {
                return false;
            }

            // 中 / 右 / Alt+左
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            if (m == NativeInput.WM_MBUTTONDOWN || m == NativeInput.WM_RBUTTONDOWN
                || (m == NativeInput.WM_LBUTTONDOWN && alt))
            {
                BeginPanAtScreen(pt.X, pt.Y, m == NativeInput.WM_RBUTTONDOWN);
                if (_isPanning)
                {
                    handled = true;
                    return true;
                }

                return false;
            }

            if (!_isPanning)
            {
                return false;
            }

            if (m == NativeInput.WM_MBUTTONUP || m == NativeInput.WM_RBUTTONUP || m == NativeInput.WM_LBUTTONUP)
            {
                EndPan();
                handled = true;
                return true;
            }

            if (m == NativeInput.WM_MOUSEMOVE)
            {
                ContinuePanAtScreen(pt.X, pt.Y);
                handled = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 本文スクロール位置を MiniMap の青帯（表示範囲）へ反映する。
        /// 左右行は内容マップ対応のまま渡し、ラベルは L{n} · R{m}。
        /// </summary>
        private void UpdateMiniMapViewportFromScroll(int leftRow, int rightRow)
        {
            if (MiniMap == null)
            {
                return;
            }

            int lr = leftRow > 0 ? leftRow : rightRow;
            int rr = rightRow > 0 ? rightRow : leftRow;
            if (lr < 1)
            {
                lr = 1;
            }

            if (rr < 1)
            {
                rr = lr;
            }

            string sheet = LeftPane != null && !string.IsNullOrEmpty(LeftPane.SelectedSheetName)
                ? LeftPane.SelectedSheetName
                : (RightPane != null ? RightPane.SelectedSheetName : string.Empty);
            sheet = sheet ?? string.Empty;

            // 同じ位置の連続更新はスキップ（描画負荷低減）
            if (lr == _lastMiniMapLeftRow
                && rr == _lastMiniMapRightRow
                && string.Equals(sheet, _lastMiniMapSheet, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastMiniMapLeftRow = lr;
            _lastMiniMapRightRow = rr;
            _lastMiniMapSheet = sheet;

            // シート帯 + 左右対応行で青帯を置く
            MiniMap.SetViewportMapped(sheet, lr, rr, visibleRows: 28);

            if (LeftPane != null && leftRow > 0)
            {
                LeftPane.NoteScrollRow(leftRow);
            }

            if (RightPane != null && rightRow > 0)
            {
                RightPane.NoteScrollRow(rightRow);
            }
        }

        /// <summary>
        /// シート名と行から MiniMap 上の OrderHint を推定する。
        /// </summary>
        private double EstimateOrderHintForViewport(string sheetName, int row)
        {
            row = Math.Max(1, row);
            if (_session.LastResult != null && _session.LastResult.Items != null && _session.LastResult.Items.Count > 0)
            {
                // 同じシートの差分の OrderHint からシート帯を推定
                var onSheet = _session.LastResult.Items
                    .Where(i => i != null
                        && (string.Equals(i.SheetLeft, sheetName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(i.SheetRight, sheetName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (onSheet.Count > 0)
                {
                    double minHint = onSheet.Min(i => Math.Abs(i.OrderHint));
                    // シート内オフセット（1000 刻みの下位）を行で上書き
                    double baseBand = Math.Floor(minHint / 1000.0) * 1000.0;
                    return baseBand + row;
                }
            }

            // シート対応のインデックスから推定
            IList<SheetPair> pairs = GetActiveSheetPairs(_session.LastResult);
            for (int i = 0; i < pairs.Count; i++)
            {
                SheetPair p = pairs[i];
                if (p == null)
                {
                    continue;
                }

                if (string.Equals(p.LeftSheet, sheetName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.RightSheet, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    return (i + 1) * 1000.0 + row;
                }
            }

            return row;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            DetachMouseWheelFilter();
            DetachLowLevelMouseHook();
            StopViewportTimer();
        }

        /// <summary>
        /// 本文 ScrollRow を定期取得して MiniMap 青帯を追従させる。
        /// </summary>
        private void StartViewportTimer()
        {
            if (_viewportTimer != null)
            {
                return;
            }

            _viewportTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _viewportTimer.Tick += ViewportTimer_Tick;
            _viewportTimer.Start();
        }

        private void StopViewportTimer()
        {
            if (_viewportTimer == null)
            {
                return;
            }

            _viewportTimer.Tick -= ViewportTimer_Tick;
            _viewportTimer.Stop();
            _viewportTimer = null;
        }

        private void ViewportTimer_Tick(object sender, EventArgs e)
        {
            if (MainCompareRoot == null || MainCompareRoot.Visibility != Visibility.Visible)
            {
                return;
            }

            if ((LeftPane == null || !LeftPane.IsOpen) && (RightPane == null || !RightPane.IsOpen))
            {
                return;
            }

            try
            {
                int lr = LeftPane != null ? LeftPane.LastKnownScrollRow : 1;
                int rr = RightPane != null ? RightPane.LastKnownScrollRow : 1;
                int lc = 1, rc = 1;
                bool any = false;

                if (LeftPane != null && LeftPane.IsOpen)
                {
                    if (LeftPane.TryGetScroll(out lr, out lc))
                    {
                        LeftPane.NoteScroll(lr, lc);
                        any = true;
                    }
                }

                if (RightPane != null && RightPane.IsOpen)
                {
                    if (RightPane.TryGetScroll(out rr, out rc))
                    {
                        RightPane.NoteScroll(rr, rc);
                        any = true;
                    }
                }

                if (any)
                {
                    UpdateMiniMapViewportFromScroll(lr, rr);
                }


            }
            catch (Exception ex)
            {
                Log.Debug("ViewportTimer: " + ex.Message);
            }
        }

        /// <summary>
        /// クリックなしでもホイールを拾う低レベル フック。
        /// </summary>
        private void AttachLowLevelMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                return;
            }

            _mouseHookProc = LowLevelMouseHookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                IntPtr hMod = NativeInput.GetModuleHandle(curModule != null ? curModule.ModuleName : null);
                _mouseHook = NativeInput.SetWindowsHookEx(NativeInput.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
            }

            if (_mouseHook == IntPtr.Zero)
            {
                Log.Debug("LowLevel mouse hook のインストールに失敗しました。");
            }
        }

        private void DetachLowLevelMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                NativeInput.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }

            _mouseHookProc = null;
        }

        private IntPtr LowLevelMouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode < NativeInput.HC_ACTION)
                {
                    return NativeInput.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                IntPtr fg = NativeInput.GetForegroundWindow();
                uint pid;
                NativeInput.GetWindowThreadProcessId(fg, out pid);
                if (pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                {
                    return NativeInput.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                var hs = (NativeInput.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                    lParam, typeof(NativeInput.MSLLHOOKSTRUCT));
                int x = hs.pt.X;
                int y = hs.pt.Y;
                int msg = wParam.ToInt32();

                // ホイール
                if (msg == NativeInput.WM_MOUSEWHEEL || msg == NativeInput.WM_MOUSEHWHEEL)
                {
                    int wheelDelta = unchecked((short)((hs.mouseData >> 16) & 0xFFFF));
                    bool horizontal = msg == NativeInput.WM_MOUSEHWHEEL
                        || (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        HandleWheelAtScreen(x, y, wheelDelta, horizontal);
                    }));
                }
                // 中ボタン パン / 右ボタンドラッグ パン（Excel セル選択と衝突しにくい）
                else if (msg == NativeInput.WM_MBUTTONDOWN || msg == NativeInput.WM_RBUTTONDOWN)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        BeginPanAtScreen(x, y, msg == NativeInput.WM_RBUTTONDOWN);
                    }));
                }
                else if (msg == NativeInput.WM_MOUSEMOVE && _isPanning)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ContinuePanAtScreen(x, y);
                    }));
                }
                else if (msg == NativeInput.WM_MBUTTONUP || msg == NativeInput.WM_RBUTTONUP || msg == NativeInput.WM_LBUTTONUP)
                {
                    if (_isPanning)
                    {
                        Dispatcher.BeginInvoke(new Action(EndPan));
                    }
                }
            }
            catch
            {
                // フック内で落とさない
            }

            return NativeInput.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        /// <summary>
        /// 左右どちらに乗っているか（両方ヒット時は X で判定）。
        /// </summary>
        private WorkbookPane HitTestPane(Point screen)
        {
            bool hitL = LeftPane != null && LeftPane.IsOpen && LeftPane.ContainsScreenPoint(screen);
            bool hitR = RightPane != null && RightPane.IsOpen && RightPane.ContainsScreenPoint(screen);
            if (hitL && hitR)
            {
                double mid = (LeftPane.GetScreenCenterX() + RightPane.GetScreenCenterX()) / 2.0;
                return screen.X <= mid ? LeftPane : RightPane;
            }

            if (hitL)
            {
                return LeftPane;
            }

            if (hitR)
            {
                return RightPane;
            }

            return null;
        }

        private void HandleWheelAtScreen(int screenX, int screenY, int wheelDelta, bool horizontal)
        {
            if (MainCompareRoot == null || MainCompareRoot.Visibility != Visibility.Visible)
            {
                return;
            }

            if (wheelDelta == 0)
            {
                return;
            }

            var screen = new Point(screenX, screenY);
            try
            {
                WorkbookPane pane = HitTestPane(screen);
                if (pane == null)
                {
                    return;
                }
                // ScrollInteracted → ApplyDrivenBy* が内容マップで相手側を即時同期
                if (pane.TryScrollByWheelDelta(wheelDelta, horizontal))
                {
                    UpdateMiniMapViewportFromScroll(
                        LeftPane != null ? LeftPane.LastKnownScrollRow : 1,
                        RightPane != null ? RightPane.LastKnownScrollRow : 1);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("HandleWheelAtScreen: " + ex.Message);
            }
        }

        /// <summary>
        /// 左ペインのホイール操作後: 内容マップで右へ即時同期。
        /// </summary>
        private void OnLeftPaneScrollInteracted(WorkbookPane pane, int row, int col, bool horizontal)
        {
            if (_scrollSync == null || (_session != null && _session.IsBusy))
            {
                return;
            }

            try
            {
                // 横も列 1:1 で Apply（マップは縦のみ）。COM 連打は Service 側 16ms coalesce
                _scrollSync.ApplyDrivenByLeft(row, col);
                if (RightPane != null && _scrollSync.CurrentState != null
                    && !_scrollSync.SheetsUnpaired)
                {
                    RightPane.NoteScroll(
                        _scrollSync.CurrentState.RightRow,
                        _scrollSync.CurrentState.RightCol);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("OnLeftPaneScrollInteracted: " + ex.Message);
            }
        }

        /// <summary>
        /// 右ペインのホイール操作後: 内容マップで左へ即時同期。
        /// </summary>
        private void OnRightPaneScrollInteracted(WorkbookPane pane, int row, int col, bool horizontal)
        {
            if (_scrollSync == null || (_session != null && _session.IsBusy))
            {
                return;
            }

            try
            {
                _scrollSync.ApplyDrivenByRight(row, col);
                if (LeftPane != null && _scrollSync.CurrentState != null
                    && !_scrollSync.SheetsUnpaired)
                {
                    LeftPane.NoteScroll(
                        _scrollSync.CurrentState.LeftRow,
                        _scrollSync.CurrentState.LeftCol);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("OnRightPaneScrollInteracted: " + ex.Message);
            }
        }

        private void BeginPanAtScreen(int screenX, int screenY, bool rightButton)
        {
            if (MainCompareRoot == null || MainCompareRoot.Visibility != Visibility.Visible)
            {
                return;
            }

            var screen = new Point(screenX, screenY);
            WorkbookPane pane = HitTestPane(screen);
            if (pane == null)
            {
                return;
            }

            _isPanning = true;
            _panLastScreen = screen;
            _panPrimaryPane = pane;
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    NativeInput.SetCapture(helper.Handle);
                }
            }
            catch
            {
                // ignore
            }

            StatusText.Text = rightButton
                ? "パン中…（右ドラッグで縦横移動）"
                : "パン中…（中ドラッグで縦横移動）";
        }

        private void ContinuePanAtScreen(int screenX, int screenY)
        {
            if (!_isPanning || _panPrimaryPane == null)
            {
                return;
            }

            var screen = new Point(screenX, screenY);
            double dx = screen.X - _panLastScreen.X;
            double dy = screen.Y - _panLastScreen.Y;
            if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
            {
                return;
            }

            _panLastScreen = screen;
            try
            {
                _panPrimaryPane.TryPanByPixels(dx, dy);
                if (_scrollSync != null && _scrollSync.Enabled)
                {
                    WorkbookPane other = _panPrimaryPane == LeftPane ? RightPane : LeftPane;
                    if (other != null && other.IsOpen)
                    {
                        other.TryPanByPixels(dx, dy);
                    }
                }

                UpdateMiniMapViewportFromScroll(
                    LeftPane != null ? LeftPane.LastKnownScrollRow : 1,
                    RightPane != null ? RightPane.LastKnownScrollRow : 1);
            }
            catch (Exception ex)
            {
                Log.Debug("ContinuePan: " + ex.Message);
            }
        }

        private void EndPan()
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            _panPrimaryPane = null;
            try { NativeInput.ReleaseCapture(); } catch { /* ignore */ }
            StatusText.Text = "パン終了";
        }

        /// <summary>
        /// 設定。
        /// </summary>
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                if (_highlightController != null)
                {
                    _highlightController.RefreshStyleFromSettings();
                    bool on = AppSettings.Current.Diff != null && AppSettings.Current.Diff.HighlightEnabled;
                    BtnHighlightToggle.IsChecked = on;
                    _highlightController.SetVisible(on);
                    ApplyImageHighlightVisible(on);
                    RefreshImageHighlightStyleFromSettings();
                    UpdateHighlightToggleCaption();
                }

                if (_scrollSync != null)
                {
                    _scrollSync.Enabled = AppSettings.Current.Ui == null || AppSettings.Current.Ui.SyncScroll;
                    _scrollSync.RefreshPollIntervalFromSettings();
                }

                MiniMap.RefreshStyle();
                StatusText.Text = "設定を保存しました。";
            }
        }

        /// <summary>
        /// ScrollSyncService の状態変化 → 再同期トースト + ステータス。
        /// </summary>
        private void OnScrollSyncStateChanged(SyncSessionState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowSyncToastIfNeeded(state);
                    ApplySyncStatusUi(state);
                }));
                return;
            }

            ShowSyncToastIfNeeded(state);
            ApplySyncStatusUi(state);
        }

        /// <summary>
        /// フッタ SyncStatusText を状態に合わせる。
        /// </summary>
        private void ApplySyncStatusUi(SyncSessionState state)
        {
            if (state == null)
            {
                return;
            }

            string line = string.IsNullOrEmpty(state.StatusLine) ? "同期 —" : state.StatusLine;
            if (SyncStatusText != null)
            {
                SyncStatusText.Text = line;
            }

            if (SyncStatusPanel != null)
            {
                string tip = line;
                if (!string.IsNullOrEmpty(state.JumpHint))
                {
                    tip = line + Environment.NewLine + state.JumpHint;
                }

                SyncStatusPanel.ToolTip = tip;
            }
        }

        /// <summary>
        /// JumpHint 非空かつ ShowSyncToastOnJump のとき下部トーストを 1800ms 表示する。
        /// </summary>
        private void ShowSyncToastIfNeeded(SyncSessionState state)
        {
            if (state == null || string.IsNullOrEmpty(state.JumpHint))
            {
                return;
            }

            bool show = true;
            try
            {
                if (AppSettings.Current != null && AppSettings.Current.Ui != null)
                {
                    show = AppSettings.Current.Ui.ShowSyncToastOnJump;
                }
            }
            catch
            {
                show = true;
            }

            if (!show || SyncToast == null || SyncToastText == null)
            {
                return;
            }

            SyncToastText.Text = state.JumpHint;
            SyncToast.Visibility = Visibility.Visible;

            if (_syncToastHideTimer == null)
            {
                _syncToastHideTimer = new DispatcherTimer();
                _syncToastHideTimer.Tick += (s, e) =>
                {
                    _syncToastHideTimer.Stop();
                    if (SyncToast != null)
                    {
                        SyncToast.Visibility = Visibility.Collapsed;
                    }
                };
            }

            _syncToastHideTimer.Stop();
            _syncToastHideTimer.Interval = TimeSpan.FromMilliseconds(1800);
            _syncToastHideTimer.Start();
        }

        /// <summary>
        /// 最初の画面へ戻る。
        /// </summary>
        private void BtnBackToStart_Click(object sender, RoutedEventArgs e)
        {
            CloseCompareWorkspace();
            ShowStartup();
        }

        /// <summary>
        /// ウィンドウサイズ変更（内容ビューは WPF レイアウトに追随）。
        /// </summary>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // no-op: ContentPane はレイアウト自動追従
        }

        /// <summary>
        /// 最大化／復元時。
        /// </summary>
        private void Window_StateChanged(object sender, EventArgs e)
        {
            // no-op: ContentPane はレイアウト自動追従
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F8)
            {
                bool prev = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                MoveToDiff(prev ? -1 : 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.H && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnHighlightToggle.IsChecked = BtnHighlightToggle.IsChecked != true;
                e.Handled = true;
                return;
            }

            // PageUp/PageDown / Ctrl+矢印 → スクロール + ScrollInteracted → ApplyDriven
            if (TryHandleKeyboardScroll(e))
            {
                return;
            }
        }

        private void BtnPrevDiff_Click(object sender, RoutedEventArgs e)
        {
            MoveToDiff(-1);
        }

        private void BtnNextDiff_Click(object sender, RoutedEventArgs e)
        {
            MoveToDiff(1);
        }

        /// <summary>
        /// 左右 ContentPane に同じ種類フィルタを入れる。
        /// </summary>
        private void OnContentKindFilterChanged(StreamKindFilter filter)
        {
            if (_syncingKindFilter)
            {
                return;
            }

            _syncingKindFilter = true;
            try
            {
                ContentPane left = LeftPane != null ? LeftPane.ContentHostControl : null;
                ContentPane right = RightPane != null ? RightPane.ContentHostControl : null;
                if (left != null)
                {
                    left.KindFilter = filter;
                }

                if (right != null)
                {
                    right.KindFilter = filter;
                }

                if (MiniMap != null)
                {
                    MiniMap.KindFilter = filter;
                }
            }
            finally
            {
                _syncingKindFilter = false;
            }
        }

        /// <summary>
        /// 現在の VerticalOffset に対応するペアから、次／前の差分へジャンプする。端では循環。
        /// </summary>
        private void MoveToDiff(int delta)
        {
            ContentPane left = LeftPane != null ? LeftPane.ContentHostControl : null;
            ContentPane right = RightPane != null ? RightPane.ContentHostControl : null;
            ContentPane host = left ?? right;
            if (host == null)
            {
                if (StatusText != null)
                {
                    StatusText.Text = "差分なし";
                }

                return;
            }

            IList<int> indices = host.GetDiffPairIndices();
            if (indices == null || indices.Count == 0)
            {
                if (StatusText != null)
                {
                    StatusText.Text = "差分なし";
                }

                return;
            }

            int current = host.GetPairIndexAtVerticalOffset();
            int next = DiffPairNavigator.PickNextDiffPairIndex(indices, current, delta);
            if (next < 0)
            {
                if (StatusText != null)
                {
                    StatusText.Text = "差分なし";
                }

                return;
            }

            _syncingContentScroll = true;
            try
            {
                if (left != null)
                {
                    left.ScrollToPairIndex(next);
                    left.HighlightPairIndex(next);
                }

                if (right != null)
                {
                    right.ScrollToPairIndex(next);
                    right.HighlightPairIndex(next);
                }

                PushMiniMapViewport();
            }
            finally
            {
                _syncingContentScroll = false;
            }

            if (StatusText != null)
            {
                int pos = 1;
                for (int i = 0; i < indices.Count; i++)
                {
                    if (indices[i] == next)
                    {
                        pos = i + 1;
                        break;
                    }
                }

                StatusText.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "差分 {0}/{1}",
                    pos,
                    indices.Count);
            }
        }

        /// <summary>
        /// PageUp/PageDown および Ctrl+矢印でペインをスクロールし、イベント駆動同期を走らせる。
        /// </summary>
        private bool TryHandleKeyboardScroll(KeyEventArgs e)
        {
            if (MainCompareRoot == null || MainCompareRoot.Visibility != Visibility.Visible)
            {
                return false;
            }

            // 入力欄にフォーカス中は横取りしない
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused != null)
            {
                if (focused is System.Windows.Controls.TextBox
                    || focused is System.Windows.Controls.PasswordBox
                    || focused is System.Windows.Controls.ComboBox
                    || focused is System.Windows.Controls.Primitives.TextBoxBase)
                {
                    return false;
                }
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            int wheelDelta = 0;
            bool horizontal = false;

            if (key == Key.PageDown)
            {
                // 約 15 行分下へ（ホイール 5 ノッチ × 3 行）
                wheelDelta = -120 * 5;
            }
            else if (key == Key.PageUp)
            {
                wheelDelta = 120 * 5;
            }
            else if (ctrl)
            {
                if (key == Key.Down)
                {
                    wheelDelta = -120 * 2;
                }
                else if (key == Key.Up)
                {
                    wheelDelta = 120 * 2;
                }
                else if (key == Key.Right)
                {
                    wheelDelta = -120 * 2;
                    horizontal = true;
                }
                else if (key == Key.Left)
                {
                    wheelDelta = 120 * 2;
                    horizontal = true;
                }
            }

            if (wheelDelta == 0)
            {
                return false;
            }

            WorkbookPane pane = ResolveKeyboardScrollPane();
            if (pane == null || !pane.IsOpen)
            {
                return false;
            }

            if (pane.TryScrollByWheelDelta(wheelDelta, horizontal))
            {
                e.Handled = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// キーボード操作対象ペイン（カーソル下 → 直近 DriveSide → 左 → 右）。
        /// </summary>
        private WorkbookPane ResolveKeyboardScrollPane()
        {
            try
            {
                NativeInput.POINT pt;
                if (NativeInput.GetCursorPos(out pt))
                {
                    WorkbookPane under = HitTestPane(new Point(pt.X, pt.Y));
                    if (under != null && under.IsOpen)
                    {
                        return under;
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (_scrollSync != null
                    && _scrollSync.CurrentState != null
                    && _scrollSync.CurrentState.DriveSide == SyncDriveSide.Right
                    && RightPane != null
                    && RightPane.IsOpen)
                {
                    return RightPane;
                }
            }
            catch
            {
                // ignore
            }

            if (LeftPane != null && LeftPane.IsOpen)
            {
                return LeftPane;
            }

            if (RightPane != null && RightPane.IsOpen)
            {
                return RightPane;
            }

            return null;
        }

        /// <summary>
        /// 内容ストリーム同期の再入防止。
        /// </summary>
        private bool _syncingContentScroll;

        /// <summary>MiniMap ドラッグ中。</summary>
        private bool _miniMapScrubbing;

        /// <summary>Rendering フック済み。</summary>
        private bool _miniMapFrameHooked;

        /// <summary>次フレームで本文へ反映する。</summary>
        private bool _miniMapApplyPending;

        /// <summary>MiniMap 目標スクロール比率。</summary>
        private double _miniMapTargetRatio;

        /// <summary>ドラッグ中の最寄り差分（確定時ハイライト用）。</summary>
        private DiffItem _miniMapTargetItem;

        /// <summary>
        /// 左右 ratio 同期の無視閾値（これ未満の差は再適用しない＝微小振動防止）。
        /// </summary>
        private const double ContentScrollRatioEpsilon = 0.0005;

        /// <summary>
        /// 本文のスクロール位置と可視比率を MiniMap 青帯へ渡す。
        /// </summary>
        private void PushMiniMapViewport()
        {
            if (MiniMap == null)
            {
                return;
            }

            double ratio = 0;
            double fraction = 1;
            if (LeftPane != null)
            {
                ratio = LeftPane.GetContentScrollRatio();
                fraction = LeftPane.GetContentVisibleFraction();
            }
            else if (RightPane != null)
            {
                ratio = RightPane.GetContentScrollRatio();
                fraction = RightPane.GetContentVisibleFraction();
            }

            MiniMap.SetContentViewport(ratio, fraction);
        }

        /// <summary>
        /// 左内容ストリームのスクロール → 右へ比率同期。
        /// </summary>
        private void OnLeftContentScrollRatioChanged(double ratio)
        {
            if (_syncingContentScroll)
            {
                return;
            }

            if (AppSettings.Current.Ui != null && !AppSettings.Current.Ui.SyncScroll)
            {
                PushMiniMapViewport();
                return;
            }

            if (RightPane != null
                && Math.Abs(RightPane.GetContentScrollRatio() - ratio) < ContentScrollRatioEpsilon)
            {
                // 相手は既に十分近い。MiniMap 青帯だけ追従。
                PushMiniMapViewport();
                return;
            }

            _syncingContentScroll = true;
            try
            {
                if (RightPane != null)
                {
                    RightPane.SetContentScrollRatio(ratio);
                }

                PushMiniMapViewport();
            }
            finally
            {
                _syncingContentScroll = false;
            }
        }

        /// <summary>
        /// 右内容ストリームのスクロール → 左へ比率同期。
        /// </summary>
        private void OnRightContentScrollRatioChanged(double ratio)
        {
            if (_syncingContentScroll)
            {
                return;
            }

            if (AppSettings.Current.Ui != null && !AppSettings.Current.Ui.SyncScroll)
            {
                PushMiniMapViewport();
                return;
            }

            if (LeftPane != null
                && Math.Abs(LeftPane.GetContentScrollRatio() - ratio) < ContentScrollRatioEpsilon)
            {
                PushMiniMapViewport();
                return;
            }

            _syncingContentScroll = true;
            try
            {
                if (LeftPane != null)
                {
                    LeftPane.SetContentScrollRatio(ratio);
                }

                PushMiniMapViewport();
            }
            finally
            {
                _syncingContentScroll = false;
            }
        }

        /// <summary>
        /// MiniMap ドラッグ開始。
        /// </summary>
        private void OnMiniMapScrubStarted()
        {
            _miniMapScrubbing = true;
        }

        /// <summary>
        /// MiniMap ドラッグ終了。最終位置をフル描画で確定する。
        /// </summary>
        private void OnMiniMapScrubEnded()
        {
            _miniMapScrubbing = false;
            _miniMapApplyPending = false;
            UnhookMiniMapScrubFrame();
            ApplyMiniMapTarget(
                ContentScrollApplyMode.ScrubEnd,
                applyHighlight: true,
                updateStatus: true);
            PushMiniMapViewport();
        }

        /// <summary>
        /// MiniMap クリック／ドラッグ。青帯は即時、本文はフレーム統合（ドラッグ中は Scrub）。
        /// マーカー指定かつ非スクラブなら ScrollToDiffItem で pair へジャンプ。
        /// </summary>
        private void OnMiniMapNavigate(double ratio, DiffItem item)
        {
            _miniMapTargetRatio = Math.Max(0, Math.Min(1, ratio));
            _miniMapTargetItem = item;

            if (_miniMapScrubbing)
            {
                // 青帯は軽量なので常に即時（リアルタイム感の主観）
                try
                {
                    if (MiniMap != null)
                    {
                        MiniMap.SetContentViewportRatio(_miniMapTargetRatio);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }

                _miniMapApplyPending = true;
                HookMiniMapScrubFrame();
                return;
            }

            if (item != null && TryJumpToMiniMapDiffItem(item))
            {
                return;
            }

            // スクラブ外の比率クリック
            ApplyMiniMapTarget(
                ContentScrollApplyMode.Normal,
                applyHighlight: true,
                updateStatus: true);
        }

        /// <summary>
        /// MiniMap マーカーから DiffItem の pair へジャンプする。失敗時は false（比率適用へ）。
        /// </summary>
        private bool TryJumpToMiniMapDiffItem(DiffItem item)
        {
            if (item == null)
            {
                return false;
            }

            _syncingContentScroll = true;
            try
            {
                bool leftOk = LeftPane != null && LeftPane.ScrollContentToDiffItem(item);
                bool rightOk = RightPane != null && RightPane.ScrollContentToDiffItem(item);
                if (!leftOk && !rightOk)
                {
                    return false;
                }

                int idx = -1;
                if (LeftPane != null)
                {
                    idx = LeftPane.FindContentPairIndex(item);
                }

                if (idx < 0 && RightPane != null)
                {
                    idx = RightPane.FindContentPairIndex(item);
                }

                if (idx >= 0)
                {
                    if (LeftPane != null && LeftPane.ContentHostControl != null)
                    {
                        LeftPane.ContentHostControl.HighlightPairIndex(idx);
                    }

                    if (RightPane != null && RightPane.ContentHostControl != null)
                    {
                        RightPane.ContentHostControl.HighlightPairIndex(idx);
                    }
                }

                PushMiniMapViewport();
                if (StatusText != null)
                {
                    string hint = item.Summary ?? item.Kind.ToString();
                    StatusText.Text = "MiniMap → " + hint;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                if (StatusText != null)
                {
                    StatusText.Text = "MiniMap 例外: " + ex.Message;
                }

                return false;
            }
            finally
            {
                _syncingContentScroll = false;
            }
        }

        private void HookMiniMapScrubFrame()
        {
            if (_miniMapFrameHooked)
            {
                return;
            }

            CompositionTarget.Rendering += OnMiniMapScrubFrame;
            _miniMapFrameHooked = true;
        }

        private void UnhookMiniMapScrubFrame()
        {
            if (!_miniMapFrameHooked)
            {
                return;
            }

            CompositionTarget.Rendering -= OnMiniMapScrubFrame;
            _miniMapFrameHooked = false;
        }

        private void OnMiniMapScrubFrame(object sender, EventArgs e)
        {
            if (!_miniMapApplyPending)
            {
                if (!_miniMapScrubbing)
                {
                    UnhookMiniMapScrubFrame();
                }

                return;
            }

            if (!_miniMapScrubbing)
            {
                // Ended が先に処理済み
                _miniMapApplyPending = false;
                UnhookMiniMapScrubFrame();
                return;
            }

            _miniMapApplyPending = false;
            ApplyMiniMapTarget(
                ContentScrollApplyMode.Scrub,
                applyHighlight: false,
                updateStatus: false);
        }

        /// <summary>
        /// 目標 ratio を左右内容へ適用する。
        /// </summary>
        private void ApplyMiniMapTarget(
            ContentScrollApplyMode mode,
            bool applyHighlight,
            bool updateStatus)
        {
            double r = _miniMapTargetRatio;
            _syncingContentScroll = true;
            try
            {
                if (LeftPane != null)
                {
                    LeftPane.SetContentScrollRatio(r, mode);
                }

                if (RightPane != null)
                {
                    RightPane.SetContentScrollRatio(r, mode);
                }

                if (MiniMap != null)
                {
                    MiniMap.SetContentViewportRatio(r);
                }

                if (applyHighlight && _miniMapTargetItem != null)
                {
                    int idx = -1;
                    if (LeftPane != null)
                    {
                        idx = LeftPane.FindContentPairIndex(_miniMapTargetItem);
                    }

                    if (idx < 0 && RightPane != null)
                    {
                        idx = RightPane.FindContentPairIndex(_miniMapTargetItem);
                    }

                    if (idx >= 0)
                    {
                        if (LeftPane != null && LeftPane.ContentHostControl != null)
                        {
                            LeftPane.ContentHostControl.HighlightPairIndex(idx);
                        }

                        if (RightPane != null && RightPane.ContentHostControl != null)
                        {
                            RightPane.ContentHostControl.HighlightPairIndex(idx);
                        }
                    }
                }

                if (updateStatus && StatusText != null)
                {
                    int pct = (int)Math.Round(r * 100);
                    DiffItem item = _miniMapTargetItem;
                    string hint = item != null ? (item.Summary ?? item.Kind.ToString()) : string.Empty;
                    StatusText.Text = "MiniMap スクロール " + pct + "%"
                        + (string.IsNullOrEmpty(hint) ? string.Empty : " · " + hint);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                if (StatusText != null)
                {
                    StatusText.Text = "MiniMap 例外: " + ex.Message;
                }
            }
            finally
            {
                _syncingContentScroll = false;
            }
        }

        /// <summary>
        /// ファイルを開いて比較する（Excel COM 不要。内容ビューへ表示）。
        /// </summary>
        private async Task OpenAndCompareAsync(string leftPath, string rightPath, bool resetOptions)
        {
            if (resetOptions)
            {
                _session.Options = new CompareOptions();
            }

            _session.LeftPath = leftPath;
            _session.RightPath = rightPath;

            ShowLoading("比較を準備しています...");
            try
            {
                ShowMainCompare();
                // パス設定のみ（Excel 埋め込みはしない）
                LeftPane.OpenFile(leftPath);
                RightPane.OpenFile(rightPath);
                if (!LeftPane.IsOpen || !RightPane.IsOpen)
                {
                    HideLoading();
                    return;
                }

                AttachScrollSync();
                await RunCompareOnlyAsync(showLoading: false);
            }
            finally
            {
                HideLoading();
            }
        }

        /// <summary>
        /// 比較エンジンのみ実行する。
        /// </summary>
        private async Task RunCompareOnlyAsync(bool showLoading = true)
        {
            if (_session.IsBusy || !_session.HasBothPaths)
            {
                return;
            }

            if (!File.Exists(_session.LeftPath) || !File.Exists(_session.RightPath))
            {
                MessageBox.Show("比較対象ファイルが見つかりません。", Common.AppDisplayName);
                return;
            }

            _session.IsBusy = true;
            if (_scrollSync != null)
            {
                _scrollSync.IsBusy = true;
            }

            if (showLoading)
            {
                ShowLoading("比較中...");
            }

            StatusText.Text = "比較中...";
            var progress = new Progress<string>(msg =>
            {
                LoadingDetail.Text = msg;
                StatusText.Text = msg;
            });

            DiffResult result = null;
            try
            {
                string left = _session.LeftPath;
                string right = _session.RightPath;
                CompareOptions options = _session.Options;
                result = await Task.Run(() => new DiffEngine().Compare(left, right, options, progress));
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                MessageBox.Show("比較に失敗しました: " + ex.Message, Common.AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _session.IsBusy = false;
                if (_scrollSync != null)
                {
                    _scrollSync.IsBusy = false;
                }

                if (showLoading)
                {
                    HideLoading();
                }
            }

            if (result == null)
            {
                return;
            }

            _session.LastResult = result;
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                StatusText.Text = "比較エラー: " + result.ErrorMessage;
                StatusDiffText.Text = "差分 —";
                MessageBox.Show(result.ErrorMessage, Common.AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_highlightController != null)
            {
                _highlightController.Apply(result);
                // 強調が OFF なら ON に戻して見えるようにする（初回比較時）
                if (!_highlightController.IsVisible)
                {
                    BtnHighlightToggle.IsChecked = true;
                    _highlightController.SetVisible(true);
                    UpdateHighlightToggleCaption();
                }
            }

            RebuildPairSheetCombo(result);
            BindContentPanes(result);
            ApplyContentScrollMaps(result);
            RefreshMiniMapForCurrentSheet();
            // レイアウト確定後にもう一度
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (_highlightController != null)
                    {
                        _highlightController.Apply(result);
                    }

                    SyncPairComboSelectionFromPanes();
                    BindContentPanes(result);
                    ApplyContentScrollMaps(result);
                    RefreshMiniMapForCurrentSheet();
                }));
            UpdateDiffStatus(result);
            StatusText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "比較完了 {0} ms / 強調:{1} / 同期:{2}{3}",
                (int)result.Elapsed.TotalMilliseconds,
                _highlightController != null && _highlightController.IsVisible ? "ON" : "OFF",
                _scrollSync != null && _scrollSync.Enabled ? "ON" : "OFF",
                (result.Alignments != null && result.Alignments.Count > 0)
                    || (result.ScrollMaps != null && result.ScrollMaps.Count > 0)
                    ? " / 内容対応"
                    : string.Empty);
        }

        /// <summary>
        /// 比較結果の LeftContent / RightContent を左右 ContentPane にバインドする。
        /// 既定はシート対応の先頭（または各ブックの先頭シート）。
        /// </summary>
        private void BindContentPanes(DiffResult result)
        {
            if (result == null)
            {
                return;
            }

            // 新旧シートで共有レイアウトが混ざらないようキャッシュを破棄
            ContentStreamBuilder.ClearLayoutCache();

            // 展開済みレイアウト構築 → Attach → 同一 pair の片側 Text を 1 件に
            DiffResultLinker.AttachExpandedLayouts(result);
            DiffResultLinker.MergeOneSidedTextsOnSamePair(result);

            string leftPreferred = null;
            string rightPreferred = null;
            IList<SheetPair> pairs = GetActiveSheetPairs(result);
            if (pairs != null && pairs.Count > 0 && pairs[0] != null)
            {
                leftPreferred = pairs[0].LeftSheet;
                rightPreferred = pairs[0].RightSheet;
            }

            // ツールバーコンボ選択があれば優先
            var comboItem = PairSheetCombo != null ? PairSheetCombo.SelectedItem as SheetPairComboItem : null;
            if (comboItem != null)
            {
                if (!string.IsNullOrEmpty(comboItem.LeftSheet))
                {
                    leftPreferred = comboItem.LeftSheet;
                }

                if (!string.IsNullOrEmpty(comboItem.RightSheet))
                {
                    rightPreferred = comboItem.RightSheet;
                }
            }

            if (LeftPane != null)
            {
                LeftPane.LoadWorkbookContent(
                    result.LeftContent,
                    result.Items,
                    isLeft: true,
                    leftPreferred,
                    result.RightContent,
                    rightPreferred);
            }

            if (RightPane != null)
            {
                RightPane.LoadWorkbookContent(
                    result.RightContent,
                    result.Items,
                    isLeft: false,
                    rightPreferred,
                    result.LeftContent,
                    leftPreferred);
            }

            // 現在のハイライトトグル状態を画像ペアに反映（再比較不要の前提を維持）
            bool hlOn = _highlightController == null || _highlightController.IsVisible;
            ApplyImageHighlightVisible(hlOn);

            // 「この側になし」行高を相手コンテンツ行に揃える（レイアウト確定後）
            ScheduleContentPairHeightSync();
        }

        /// <summary>
        /// 左右 ContentPane の同一ストリーム行の高さを max に揃える（次フレーム）。
        /// </summary>
        private void ScheduleContentPairHeightSync()
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    try
                    {
                        ContentPane left = LeftPane != null ? LeftPane.ContentHostControl : null;
                        ContentPane right = RightPane != null ? RightPane.ContentHostControl : null;
                        ContentPane.SyncPairHeights(left, right);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("SyncPairHeights: " + ex.Message);
                    }
                }));
        }

        /// <summary>
        /// 比較結果の内容スクロールマップを ScrollSync に適用する。
        /// 優先: Alignments[].ScrollMap。なければ ScrollMaps。
        /// </summary>
        private void ApplyContentScrollMaps(DiffResult result)
        {
            if (_scrollSync == null)
            {
                return;
            }

            ContentScrollMapSet maps = null;
            if (result != null && result.Alignments != null && result.Alignments.Count > 0)
            {
                maps = ContentScrollMapSet.FromAlignments(result.Alignments);
            }

            if ((maps == null || maps.Count == 0) && result != null && result.ScrollMaps != null)
            {
                maps = result.ScrollMaps;
            }

            _scrollSync.SetContentMaps(maps != null && maps.Count > 0 ? maps : null);
            RefreshScrollSyncActiveSheets();
        }

        /// <summary>
        /// 表示中シートに合わせて内容マップを切り替える。
        /// MiniMap にもアクティブ Alignment を渡す。
        /// </summary>
        private void RefreshScrollSyncActiveSheets()
        {
            string leftSheet = LeftPane != null ? LeftPane.SelectedSheetName : null;
            string rightSheet = RightPane != null ? RightPane.SelectedSheetName : null;
            if (_scrollSync != null)
            {
                _scrollSync.SetActiveSheets(leftSheet, rightSheet);
            }

            ApplyMiniMapAlignmentForSheets(leftSheet, rightSheet);
        }

        /// <summary>
        /// 指定シートペアの Alignment / ActiveMap を MiniMap に渡す。
        /// </summary>
        private void ApplyMiniMapAlignmentForSheets(string leftSheet, string rightSheet)
        {
            if (MiniMap == null)
            {
                return;
            }

            SheetAlignment alignment = FindAlignmentForSheets(leftSheet, rightSheet);
            if (alignment != null)
            {
                MiniMap.SetAlignment(alignment);
                return;
            }

            // Alignment が無い場合は ActiveMap を直接渡す
            if (_scrollSync != null && _scrollSync.ActiveMap != null)
            {
                MiniMap.SetScrollMap(_scrollSync.ActiveMap);
            }
            else
            {
                MiniMap.SetScrollMap(null);
            }
        }

        /// <summary>
        /// 比較結果からシートペアに対応する Alignment を探す。
        /// </summary>
        private SheetAlignment FindAlignmentForSheets(string leftSheet, string rightSheet)
        {
            IList<SheetAlignment> alignments = _session != null && _session.LastResult != null
                ? _session.LastResult.Alignments
                : null;
            if (alignments == null || alignments.Count == 0)
            {
                return null;
            }

            foreach (SheetAlignment a in alignments)
            {
                if (a == null)
                {
                    continue;
                }

                bool leftOk = string.IsNullOrEmpty(leftSheet)
                    || string.Equals(a.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase);
                bool rightOk = string.IsNullOrEmpty(rightSheet)
                    || string.Equals(a.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase);
                if (leftOk && rightOk)
                {
                    return a;
                }
            }

            if (!string.IsNullOrEmpty(leftSheet))
            {
                foreach (SheetAlignment a in alignments)
                {
                    if (a != null && string.Equals(a.LeftSheet, leftSheet, StringComparison.OrdinalIgnoreCase))
                    {
                        return a;
                    }
                }
            }

            if (!string.IsNullOrEmpty(rightSheet))
            {
                foreach (SheetAlignment a in alignments)
                {
                    if (a != null && string.Equals(a.RightSheet, rightSheet, StringComparison.OrdinalIgnoreCase))
                    {
                        return a;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 左ペインのシート変更。ツールバーと同じ SheetPairs で右も合わせる。
        /// </summary>
        private void OnLeftSheetChangedByUser(string leftSheet)
        {
            if (_syncingSheets || string.IsNullOrEmpty(leftSheet))
            {
                return;
            }

            string pairedRight = ResolvePairedSheet(leftSheet, fromLeft: true);
            _syncingSheets = true;
            try
            {
                if (!string.IsNullOrEmpty(pairedRight) && RightPane != null && RightPane.IsOpen)
                {
                    RightPane.TrySelectSheet(pairedRight);
                }

                string rightSheet = RightPane != null ? RightPane.SelectedSheetName : null;
                if (LeftPane != null)
                {
                    LeftPane.SetPartnerPreferredSheet(rightSheet);
                }

                if (RightPane != null)
                {
                    RightPane.SetPartnerPreferredSheet(leftSheet);
                }

                SyncPairComboSelectionFromPanes();
                RefreshScrollSyncActiveSheets();
                RefreshMiniMapForCurrentSheet();
                ScheduleContentPairHeightSync();

                if (!string.IsNullOrEmpty(rightSheet))
                {
                    StatusText.Text = "シート切替: " + leftSheet + " ↔ " + rightSheet;
                }
                else
                {
                    StatusText.Text = "シート切替: 左=" + leftSheet + "（片側）· MiniMap 更新";
                }

                Log.Info("シート同期 L: " + leftSheet + " (R=" + (rightSheet ?? "") + ")");
            }
            finally
            {
                _syncingSheets = false;
            }
        }

        /// <summary>
        /// 右ペインのシート変更。ツールバーと同じ SheetPairs で左も合わせる。
        /// </summary>
        private void OnRightSheetChangedByUser(string rightSheet)
        {
            if (_syncingSheets || string.IsNullOrEmpty(rightSheet))
            {
                return;
            }

            string pairedLeft = ResolvePairedSheet(rightSheet, fromLeft: false);
            _syncingSheets = true;
            try
            {
                if (!string.IsNullOrEmpty(pairedLeft) && LeftPane != null && LeftPane.IsOpen)
                {
                    LeftPane.TrySelectSheet(pairedLeft);
                }

                string leftSheet = LeftPane != null ? LeftPane.SelectedSheetName : null;
                if (RightPane != null)
                {
                    RightPane.SetPartnerPreferredSheet(leftSheet);
                }

                if (LeftPane != null)
                {
                    LeftPane.SetPartnerPreferredSheet(rightSheet);
                }

                SyncPairComboSelectionFromPanes();
                RefreshScrollSyncActiveSheets();
                RefreshMiniMapForCurrentSheet();
                ScheduleContentPairHeightSync();

                if (!string.IsNullOrEmpty(leftSheet))
                {
                    StatusText.Text = "シート切替: " + leftSheet + " ↔ " + rightSheet;
                }
                else
                {
                    StatusText.Text = "シート切替: 右=" + rightSheet + "（片側）· MiniMap 更新";
                }

                Log.Info("シート同期 R: " + rightSheet + " (L=" + (leftSheet ?? "") + ")");
            }
            finally
            {
                _syncingSheets = false;
            }
        }

        /// <summary>
        /// ツールバーの対応シートコンボ変更 → 左右まとめて切替 → MiniMap をそのシートのみ再構築。
        /// </summary>
        private void PairSheetCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressPairComboEvent || _syncingSheets)
            {
                return;
            }

            var item = PairSheetCombo.SelectedItem as SheetPairComboItem;
            if (item == null)
            {
                return;
            }

            _syncingSheets = true;
            try
            {
                if (!string.IsNullOrEmpty(item.LeftSheet) && LeftPane.IsOpen)
                {
                    LeftPane.TrySelectSheet(item.LeftSheet);
                }

                if (!string.IsNullOrEmpty(item.RightSheet) && RightPane.IsOpen)
                {
                    RightPane.TrySelectSheet(item.RightSheet);
                }

                // 片側ペア: 片方だけ選択
                if (LeftPane != null)
                {
                    LeftPane.SetPartnerPreferredSheet(item.RightSheet);
                }

                if (RightPane != null)
                {
                    RightPane.SetPartnerPreferredSheet(item.LeftSheet);
                }

                StatusText.Text = "シート切替: " + item.Display;
                Log.Info("シート同期 ツールバー: " + item.Display);
                RefreshScrollSyncActiveSheets();
                RefreshMiniMapForCurrentSheet();
                ScheduleContentPairHeightSync();
            }
            finally
            {
                _syncingSheets = false;
            }
        }

        /// <summary>
        /// MiniMap をフォーカス中シート（左優先、なければ右）の差分だけに差し替える。
        /// 左右が未対応の別シートでも、相手シートのマーカーは出さない。
        /// </summary>
        private void RefreshMiniMapForCurrentSheet()
        {
            if (MiniMap == null)
            {
                return;
            }

            if (_highlightController != null && !_highlightController.IsVisible)
            {
                MiniMap.SetDiffs(Enumerable.Empty<DiffItem>());
                return;
            }

            DiffResult result = _session != null ? _session.LastResult : null;
            if (result == null || result.Items == null)
            {
                MiniMap.Clear();
                return;
            }

            string leftSheet = LeftPane != null ? LeftPane.SelectedSheetName : null;
            string rightSheet = RightPane != null ? RightPane.SelectedSheetName : null;
            // フォーカスシート = 左があれば左、なければ右（現在シートのみ）
            string focusSheet = !string.IsNullOrEmpty(leftSheet)
                ? leftSheet
                : (rightSheet ?? string.Empty);

            var filtered = new List<DiffItem>();
            if (!string.IsNullOrEmpty(focusSheet))
            {
                foreach (DiffItem item in result.Items)
                {
                    if (item != null && ItemBelongsToFocusSheet(item, focusSheet))
                    {
                        filtered.Add(item);
                    }
                }
            }

            MiniMap.SetCurrentSheet(focusSheet, filtered);
            PushMiniMapViewport();
            _lastMiniMapSheet = focusSheet ?? string.Empty;
            _lastMiniMapLeftRow = -1;
            _lastMiniMapRightRow = -1;
        }

        /// <summary>
        /// 差分がフォーカスシートに属するか（SheetLeft または SheetRight が一致）。
        /// </summary>
        private static bool ItemBelongsToFocusSheet(DiffItem item, string focusSheet)
        {
            if (item == null || string.IsNullOrEmpty(focusSheet))
            {
                return false;
            }

            if (string.Equals(item.SheetLeft, focusSheet, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(item.SheetRight, focusSheet, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 比較結果のシート対応からツールバーコンボを組み立てる（片側 Structure も含む）。
        /// </summary>
        private void RebuildPairSheetCombo(DiffResult result)
        {
            _suppressPairComboEvent = true;
            try
            {
                PairSheetCombo.Items.Clear();
                var seenLeft = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenRight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                IList<SheetPair> pairs = GetActiveSheetPairs(result);
                foreach (SheetPair pair in pairs)
                {
                    if (pair == null)
                    {
                        continue;
                    }

                    string left = pair.LeftSheet ?? string.Empty;
                    string right = pair.RightSheet ?? string.Empty;
                    if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
                    {
                        continue;
                    }

                    PairSheetCombo.Items.Add(new SheetPairComboItem(left, right));
                    if (!string.IsNullOrEmpty(left))
                    {
                        seenLeft.Add(left);
                    }

                    if (!string.IsNullOrEmpty(right))
                    {
                        seenRight.Add(right);
                    }
                }

                // Structure（片側のみシート）をコンボへ
                if (result != null && result.Items != null)
                {
                    foreach (DiffItem item in result.Items)
                    {
                        if (item == null || item.Kind != DiffKind.Structure)
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(item.SheetLeft) && string.IsNullOrEmpty(item.SheetRight)
                            && !seenLeft.Contains(item.SheetLeft))
                        {
                            PairSheetCombo.Items.Add(new SheetPairComboItem(item.SheetLeft, string.Empty));
                            seenLeft.Add(item.SheetLeft);
                        }
                        else if (!string.IsNullOrEmpty(item.SheetRight) && string.IsNullOrEmpty(item.SheetLeft)
                            && !seenRight.Contains(item.SheetRight))
                        {
                            PairSheetCombo.Items.Add(new SheetPairComboItem(string.Empty, item.SheetRight));
                            seenRight.Add(item.SheetRight);
                        }
                    }
                }

                PairSheetCombo.IsEnabled = PairSheetCombo.Items.Count > 0;
                SyncPairComboSelectionFromPanes();
            }
            finally
            {
                _suppressPairComboEvent = false;
            }
        }

        /// <summary>
        /// 現在の左右選択に合わせてツールバーコンボの選択を合わせる。
        /// </summary>
        private void SyncPairComboSelectionFromPanes()
        {
            if (PairSheetCombo == null || PairSheetCombo.Items.Count == 0)
            {
                return;
            }

            string left = LeftPane.SelectedSheetName;
            string right = RightPane.SelectedSheetName;
            _suppressPairComboEvent = true;
            try
            {
                int found = -1;
                for (int i = 0; i < PairSheetCombo.Items.Count; i++)
                {
                    var item = PairSheetCombo.Items[i] as SheetPairComboItem;
                    if (item == null)
                    {
                        continue;
                    }

                    bool leftMatch = string.IsNullOrEmpty(left)
                        || string.Equals(item.LeftSheet, left, StringComparison.OrdinalIgnoreCase);
                    bool rightMatch = string.IsNullOrEmpty(right)
                        || string.Equals(item.RightSheet, right, StringComparison.OrdinalIgnoreCase);
                    if (leftMatch && rightMatch)
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0 && !string.IsNullOrEmpty(left))
                {
                    for (int i = 0; i < PairSheetCombo.Items.Count; i++)
                    {
                        var item = PairSheetCombo.Items[i] as SheetPairComboItem;
                        if (item != null && string.Equals(item.LeftSheet, left, StringComparison.OrdinalIgnoreCase))
                        {
                            found = i;
                            break;
                        }
                    }
                }

                if (found >= 0)
                {
                    PairSheetCombo.SelectedIndex = found;
                }
            }
            finally
            {
                _suppressPairComboEvent = false;
            }
        }

        /// <summary>
        /// シート対応一覧（比較結果 → 手動 → 同名自動）。
        /// </summary>
        private IList<SheetPair> GetActiveSheetPairs(DiffResult result)
        {
            if (result != null && result.SheetPairs != null && result.SheetPairs.Count > 0)
            {
                return result.SheetPairs;
            }

            if (_session.Options != null && _session.Options.ManualSheetPairs != null
                && _session.Options.ManualSheetPairs.Count > 0)
            {
                return _session.Options.ManualSheetPairs;
            }

            // 同名シートの交差（内容モデル）
            var pairs = new List<SheetPair>();
            IReadOnlyList<string> leftNames = LeftPane != null ? LeftPane.GetSheetNames() : null;
            IReadOnlyList<string> rightNames = RightPane != null ? RightPane.GetSheetNames() : null;

            if (leftNames != null && leftNames.Count > 0 && rightNames != null && rightNames.Count > 0)
            {
                var rightSet = new HashSet<string>(
                    rightNames,
                    StringComparer.OrdinalIgnoreCase);
                foreach (string name in leftNames)
                {
                    if (rightSet.Contains(name))
                    {
                        pairs.Add(new SheetPair { LeftSheet = name, RightSheet = name });
                    }
                }
            }

            return pairs;
        }

        /// <summary>
        /// 一方のシート名から対応するもう一方を解決する。
        /// </summary>
        private string ResolvePairedSheet(string sheetName, bool fromLeft)
        {
            if (string.IsNullOrEmpty(sheetName))
            {
                return null;
            }

            IList<SheetPair> pairs = GetActiveSheetPairs(_session.LastResult);
            foreach (SheetPair pair in pairs)
            {
                if (pair == null)
                {
                    continue;
                }

                if (fromLeft
                    && string.Equals(pair.LeftSheet, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.RightSheet;
                }

                if (!fromLeft
                    && string.Equals(pair.RightSheet, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.LeftSheet;
                }
            }

            // 同名が相手側にあれば使う
            WorkbookPane other = fromLeft ? RightPane : LeftPane;
            if (other != null)
            {
                foreach (string name in other.GetSheetNames())
                {
                    if (string.Equals(name, sheetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// ツールバー用シート対応アイテム。
        /// </summary>
        private sealed class SheetPairComboItem
        {
            public SheetPairComboItem(string leftSheet, string rightSheet)
            {
                LeftSheet = leftSheet ?? string.Empty;
                RightSheet = rightSheet ?? string.Empty;
                if (string.IsNullOrEmpty(LeftSheet))
                {
                    Display = "（右）" + RightSheet;
                }
                else if (string.IsNullOrEmpty(RightSheet))
                {
                    Display = "（左）" + LeftSheet;
                }
                else if (string.Equals(LeftSheet, RightSheet, StringComparison.OrdinalIgnoreCase))
                {
                    Display = LeftSheet;
                }
                else
                {
                    Display = LeftSheet + " ↔ " + RightSheet;
                }
            }

            public string LeftSheet { get; private set; }

            public string RightSheet { get; private set; }

            public string Display { get; private set; }

            public override string ToString()
            {
                return Display;
            }
        }

        /// <summary>
        /// 差分ステータス文言を更新する。
        /// </summary>
        private void UpdateDiffStatus(DiffResult result)
        {
            int textCount = result.Items.Count(i => i.Kind == DiffKind.Text);
            int imageCount = result.Items.Count(i => i.Kind == DiffKind.Image);
            int onlyCount = result.Items.Count(i =>
                i.Kind == DiffKind.ImageOnlyLeft || i.Kind == DiffKind.ImageOnlyRight);
            int structureCount = result.Items.Count(i => i.Kind == DiffKind.Structure);
            StatusDiffText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "差分 {0} 件（テキスト {1} / 画像 {2} / 片側のみ {3} / 構造 {4}）",
                result.Items.Count,
                textCount,
                imageCount,
                onlyCount,
                structureCount);
            FooterText.Text = "左:" + (LeftPane.IsOpen ? "開" : "閉") + " / 右:" + (RightPane.IsOpen ? "開" : "閉");
        }

        /// <summary>
        /// スクロール同期を左右内容ビューに接続する。
        /// </summary>
        private void AttachScrollSync()
        {
            if (_scrollSync == null)
            {
                return;
            }

            _scrollSync.ViewportChanged -= OnScrollViewportChanged;

            if (LeftPane.IsOpen && RightPane.IsOpen)
            {
                _scrollSync.Attach();
                _scrollSync.Enabled = AppSettings.Current.Ui == null || AppSettings.Current.Ui.SyncScroll;
                _scrollSync.ViewportChanged += OnScrollViewportChanged;
            }
            else
            {
                _scrollSync.Detach();
            }
        }

        /// <summary>
        /// 本文スクロール → MiniMap 同期。
        /// </summary>
        private void OnScrollViewportChanged(int leftRow, int rightRow)
        {
            try
            {
                UpdateMiniMapViewportFromScroll(leftRow, rightRow);
            }
            catch (Exception ex)
            {
                Log.Debug("OnScrollViewportChanged: " + ex.Message);
            }
        }

        private void OnPaneOpened()
        {
            AttachScrollSync();
            FooterText.Text = "左:" + (LeftPane.IsOpen ? "開" : "閉") + " / 右:" + (RightPane.IsOpen ? "開" : "閉");
        }

        private void OnPaneOpenFailed(string message)
        {
            HideLoading();
            StatusText.Text = message;
            MessageBox.Show(message, Common.AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ShowStartup()
        {
            Startup.Visibility = Visibility.Visible;
            Startup.Reset();
            MainCompareRoot.Visibility = Visibility.Collapsed;
            StatusText.Text = string.Empty;
            StatusDiffText.Text = "差分 —";
            FooterText.Text = "ファイルを選択してください";
        }

        private void ShowMainCompare()
        {
            Startup.Visibility = Visibility.Collapsed;
            MainCompareRoot.Visibility = Visibility.Visible;
        }

        private void CloseCompareWorkspace()
        {
            _suppressPairComboEvent = true;
            try
            {
                if (PairSheetCombo != null)
                {
                    PairSheetCombo.Items.Clear();
                    PairSheetCombo.IsEnabled = false;
                }
            }
            finally
            {
                _suppressPairComboEvent = false;
            }

            if (_scrollSync != null)
            {
                _scrollSync.Detach();
            }

            LeftPane.CloseFile();
            RightPane.CloseFile();
            if (_highlightController != null)
            {
                _highlightController.ClearResult();
            }

            MiniMap.Clear();
            _session.Reset();
        }

        private void ShowLoading(string detail)
        {
            LoadingDetail.Text = detail ?? string.Empty;
            LoadingMask.Visibility = Visibility.Visible;
        }

        private void HideLoading()
        {
            LoadingMask.Visibility = Visibility.Collapsed;
        }

        private void UpdateHighlightToggleCaption()
        {
            bool on = BtnHighlightToggle.IsChecked == true;
            // Content 全体を差し替えるとアイコンが消えるため、ラベル Text のみ更新する
            if (BtnHighlightToggleLabel != null)
            {
                BtnHighlightToggleLabel.Text = on ? "差分強調 ON" : "差分強調 OFF";
            }

            if (BtnHighlightToggleIcon != null)
            {
                BtnHighlightToggleIcon.Kind = on
                    ? MahApps.Metro.IconPacks.PackIconPhosphorIconsKind.HighlighterCircle
                    : MahApps.Metro.IconPacks.PackIconPhosphorIconsKind.EyeSlash;
            }
        }

        private static string PickXlsx(string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel ブック (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                CheckFileExists = true,
                Title = title
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                CloseCompareWorkspace();
                if (_scrollSync != null)
                {
                    _scrollSync.StateChanged -= OnScrollSyncStateChanged;
                    _scrollSync.Dispose();
                    _scrollSync = null;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }
    }
}
