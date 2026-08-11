using System;
using System.Collections.Generic;
using System.Windows.Threading;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// 左右 Excel のスクロール位置を同期する。
    /// 主経路: ホイール等の <see cref="ApplyDrivenByLeft"/> / <see cref="ApplyDrivenByRight"/>（イベント駆動）。
    /// 保険: DispatcherTimer ポーリング（既定 250ms / <c>Ui.SyncPollFallbackMs</c>）。
    /// 横: セル位置 1:1。縦: 内容対応マップ。連続失敗時は自動停止。
    /// </summary>
    public sealed class ScrollSyncService : IDisposable
    {
        /// <summary>
        /// 連続失敗で停止する閾値。
        /// </summary>
        private const int MaxConsecutiveFailures = 30;

        /// <summary>
        /// 保険ポーリング（主経路はイベント駆動）。
        /// </summary>
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// ApplyDriven 連打の coalesce（16ms one-shot・最後の要求のみ COM 適用）。
        /// </summary>
        private readonly DispatcherTimer _coalesceTimer;

        /// <summary>
        /// 保留中の駆動側（None=なし）。
        /// </summary>
        private SyncDriveSide _pendingDrive = SyncDriveSide.None;

        /// <summary>
        /// 保留 Apply の行。
        /// </summary>
        private int _pendingRow = 1;

        /// <summary>
        /// 保留 Apply の列。
        /// </summary>
        private int _pendingCol = 1;

        /// <summary>
        /// coalesce COM 用: イージング起点（状態更新前のフォロワー行）。
        /// </summary>
        private int _pendingFromFollowerRow = 1;

        /// <summary>
        /// coalesce COM 用: フォロワー目標行。
        /// </summary>
        private int _pendingToFollowerRow = 1;

        /// <summary>
        /// coalesce COM 用: フォロワー列。
        /// </summary>
        private int _pendingFollowerCol = 1;

        /// <summary>
        /// coalesce COM 用: 直前セグメント（イージング判定）。
        /// </summary>
        private SyncSegmentKind _pendingPrevKind = SyncSegmentKind.Identity;

        /// <summary>
        /// coalesce COM 用: 今回セグメント。
        /// </summary>
        private SyncSegmentKind _pendingNextKind = SyncSegmentKind.Identity;

        /// <summary>
        /// 比較中など Apply を無視するフラグ（UI が設定）。
        /// </summary>
        private bool _isBusy;

        /// <summary>
        /// 左右シートがマップ上で未対応。
        /// </summary>
        private bool _sheetsUnpaired;

        /// <summary>
        /// 左セッション。
        /// </summary>
        private ExcelWorkbookSession _left;

        /// <summary>
        /// 右セッション。
        /// </summary>
        private ExcelWorkbookSession _right;

        /// <summary>
        /// 再入防止。
        /// </summary>
        private bool _syncing;

        /// <summary>
        /// 有効フラグ。
        /// </summary>
        private bool _enabled = true;

        /// <summary>
        /// 連続失敗回数。
        /// </summary>
        private int _failCount;

        /// <summary>
        /// COM スクロールが使えないと判断したか。
        /// </summary>
        private bool _scrollUnavailable;

        /// <summary>
        /// 前回の左スクロール。
        /// </summary>
        private int _lastLeftRow = -1;

        /// <summary>
        /// 前回の左列。
        /// </summary>
        private int _lastLeftCol = -1;

        /// <summary>
        /// 前回の右スクロール。
        /// </summary>
        private int _lastRightRow = -1;

        /// <summary>
        /// 前回の右列。
        /// </summary>
        private int _lastRightCol = -1;

        /// <summary>
        /// 破棄済み。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// シート対応ごとの内容スクロールマップ。
        /// </summary>
        private ContentScrollMapSet _mapSet;

        /// <summary>
        /// 現在有効なマップ。
        /// </summary>
        private ContentScrollMap _activeMap = ContentScrollMap.Identity;

        /// <summary>
        /// 現在の左シート名（マップ解決用）。
        /// </summary>
        private string _leftSheet;

        /// <summary>
        /// 現在の右シート名。
        /// </summary>
        private string _rightSheet;

        /// <summary>
        /// スクロール位置が変わった（または安定取得できた）ときの通知。
        /// 引数: 左行, 右行。
        /// </summary>
        public event Action<int, int> ViewportChanged;

        /// <summary>
        /// 同期セッション状態が更新されたときの通知。
        /// </summary>
        public event Action<SyncSessionState> StateChanged;

        /// <summary>
        /// 直近に発行した同期状態。
        /// </summary>
        public SyncSessionState CurrentState { get; private set; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public ScrollSyncService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ResolvePollFallbackMs())
            };
            _timer.Tick += Timer_Tick;
            _coalesceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _coalesceTimer.Tick += CoalesceTimer_Tick;
            CurrentState = new SyncSessionState
            {
                Enabled = true,
                SegmentKind = SyncSegmentKind.Identity,
                DriveSide = SyncDriveSide.None,
                LeftRow = 1,
                RightRow = 1,
                LeftCol = 1,
                RightCol = 1,
                UtcUpdated = DateTime.UtcNow
            };
            CurrentState.RefreshStatusLine();
        }

        /// <summary>
        /// 比較中など。true のとき ApplyDriven は無視しキューもしない。
        /// </summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (_isBusy == value)
                {
                    return;
                }

                _isBusy = value;
                if (_isBusy)
                {
                    CancelPendingApply();
                }
            }
        }

        /// <summary>
        /// 左右シートが内容マップ上で未対応か。
        /// </summary>
        public bool SheetsUnpaired
        {
            get { return _sheetsUnpaired; }
        }

        /// <summary>
        /// 設定から保険ポーリング間隔（ms）を解決する。既定 250。
        /// </summary>
        private static int ResolvePollFallbackMs()
        {
            try
            {
                if (AppSettings.Current != null && AppSettings.Current.Ui != null
                    && AppSettings.Current.Ui.SyncPollFallbackMs > 0)
                {
                    int ms = AppSettings.Current.Ui.SyncPollFallbackMs;
                    if (ms < 100)
                    {
                        return 100;
                    }

                    if (ms > 1000)
                    {
                        return 1000;
                    }

                    return ms;
                }
            }
            catch
            {
                // ignore
            }

            return 250;
        }

        /// <summary>
        /// 保険ポーリング間隔を設定から再読込する（設定画面保存後など）。
        /// </summary>
        public void RefreshPollIntervalFromSettings()
        {
            if (_disposed)
            {
                return;
            }

            _timer.Interval = TimeSpan.FromMilliseconds(ResolvePollFallbackMs());
        }

        /// <summary>
        /// 同期が有効か（設定 Ui.SyncScroll と連動）。
        /// </summary>
        public bool Enabled
        {
            get { return _enabled && !_scrollUnavailable; }
            set
            {
                _enabled = value;
                // タイマーは MiniMap ビューポート追跡のため止めない
                if (_left != null && _right != null && !_timer.IsEnabled)
                {
                    _timer.Start();
                }

                PublishState(
                    SyncDriveSide.None,
                    Math.Max(1, _lastLeftRow),
                    Math.Max(1, _lastRightRow),
                    Math.Max(1, _lastLeftCol),
                    Math.Max(1, _lastRightCol));
            }
        }

        /// <summary>
        /// 現在の内容マップ（デバッグ・テスト用）。
        /// </summary>
        public ContentScrollMap ActiveMap
        {
            get { return _activeMap ?? ContentScrollMap.Identity; }
        }

        /// <summary>
        /// 左右セッションを接続して同期を開始する。
        /// </summary>
        public void Attach(ExcelWorkbookSession left, ExcelWorkbookSession right)
        {
            _left = left;
            _right = right;
            _failCount = 0;
            _scrollUnavailable = false;
            ResetLast();
            RefreshPollIntervalFromSettings();
            // 左右同期 OFF でも MiniMap 位置追跡のため常に起動（同期適用は Enabled 時のみ）
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }

            Log.Debug("ScrollSync Attach contentMap=" + (_activeMap != null && _activeMap.IsContentBased)
                + " pollMs=" + (int)_timer.Interval.TotalMilliseconds);
        }

        /// <summary>
        /// 同期を停止する。
        /// </summary>
        public void Detach()
        {
            CancelPendingApply();
            _timer.Stop();
            _left = null;
            _right = null;
            _failCount = 0;
            ResetLast();
            Log.Debug("ScrollSync Detach");
        }

        /// <summary>
        /// 比較結果の内容対応マップを設定する（縦のみ。横列は常に 1:1）。
        /// </summary>
        public void SetContentMaps(ContentScrollMapSet maps)
        {
            _mapSet = maps;
            ResolveActiveMap();
            Log.Debug("ScrollSync SetContentMaps count=" + (_mapSet != null ? _mapSet.Count : 0)
                + " active=" + (_activeMap != null ? _activeMap.Describe() : "null"));
        }

        /// <summary>
        /// <see cref="SheetAlignment"/> 一覧から縦マップを設定する。
        /// </summary>
        public void SetContentMapsFromAlignments(IEnumerable<SheetAlignment> alignments)
        {
            SetContentMaps(ContentScrollMapSet.FromAlignments(alignments));
        }

        /// <summary>
        /// 表示中シートが変わったときにマップを切り替える。
        /// 未対応時は Status「シート未対応」を Publish。
        /// </summary>
        public void SetActiveSheets(string leftSheet, string rightSheet)
        {
            _leftSheet = leftSheet;
            _rightSheet = rightSheet;
            ResolveActiveMap();
            PublishState(
                SyncDriveSide.None,
                Math.Max(1, _lastLeftRow),
                Math.Max(1, _lastRightRow),
                Math.Max(1, _lastLeftCol),
                Math.Max(1, _lastRightCol));
        }

        /// <summary>
        /// 指定比率（0〜1）で左右を縦スクロールする（MiniMap 用・内容マップ考慮）。
        /// </summary>
        public void ScrollBothToRatio(double ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            int row = 1 + (int)Math.Round(ratio * 500);
            ScrollBothToRow(row);
        }

        /// <summary>
        /// 指定行へ左右をスクロールする。
        /// 基準は左行とし、右は内容マップで対応行へ。
        /// </summary>
        public void ScrollBothToRow(int row)
        {
            if (row < 1)
            {
                row = 1;
            }

            int leftRow = row;
            int rightRow = MapLeftToRight(leftRow);

            _syncing = true;
            try
            {
                if (!_scrollUnavailable)
                {
                    if (_left != null && _left.IsOpen)
                    {
                        _left.TrySetScroll(leftRow, 1);
                    }

                    if (_right != null && _right.IsOpen)
                    {
                        _right.TrySetScroll(rightRow, 1);
                    }
                }

                _lastLeftRow = leftRow;
                _lastRightRow = rightRow;
                _lastLeftCol = 1;
                _lastRightCol = 1;
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// 左右それぞれの目標行へスクロールする（内容対応済みの行を渡す場合）。
        /// </summary>
        public void ScrollBothToRows(int leftRow, int rightRow, int leftCol = 1, int rightCol = 1)
        {
            if (leftRow < 1)
            {
                leftRow = 1;
            }

            if (rightRow < 1)
            {
                rightRow = 1;
            }

            if (leftCol < 1)
            {
                leftCol = 1;
            }

            if (rightCol < 1)
            {
                rightCol = 1;
            }

            _syncing = true;
            try
            {
                if (!_scrollUnavailable)
                {
                    if (_left != null && _left.IsOpen)
                    {
                        _left.TrySetScroll(leftRow, leftCol);
                    }

                    if (_right != null && _right.IsOpen)
                    {
                        _right.TrySetScroll(rightRow, rightCol);
                    }
                }

                _lastLeftRow = leftRow;
                _lastRightRow = rightRow;
                _lastLeftCol = leftCol;
                _lastRightCol = rightCol;
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// 外部（MiniMap 等）がスクロールした後、同期の基準位置を合わせる。
        /// これを呼ばないと直後のポーリングが位置を巻き戻すことがある。
        /// </summary>
        public void NotifyExternalScroll(int leftRow, int rightRow, int leftCol = 1, int rightCol = 1)
        {
            _lastLeftRow = Math.Max(1, leftRow);
            _lastRightRow = Math.Max(1, rightRow);
            _lastLeftCol = Math.Max(1, leftCol);
            _lastRightCol = Math.Max(1, rightCol);
        }

        /// <summary>
        /// 一時的に同期ポーリングを止める（ジャンプ中の上書き防止）。
        /// </summary>
        public void Suspend()
        {
            _syncing = true;
        }

        /// <summary>
        /// Suspend を解除する。
        /// </summary>
        public void Resume()
        {
            _syncing = false;
        }

        /// <summary>
        /// COM 連続失敗による Unavailable 停止を解除し、failCount をクリアしてタイマーを再開する（UI「再試行」）。
        /// </summary>
        public void RetryAfterUnavailable()
        {
            if (_disposed)
            {
                return;
            }

            _failCount = 0;
            _scrollUnavailable = false;
            if (_left != null && _right != null && !_timer.IsEnabled)
            {
                _timer.Start();
            }

            Log.Info("ScrollSync 再試行: Unavailable 解除・failCount=0・タイマー再開");
            PublishState(
                SyncDriveSide.None,
                Math.Max(1, _lastLeftRow),
                Math.Max(1, _lastRightRow),
                Math.Max(1, _lastLeftCol),
                Math.Max(1, _lastRightCol));
        }

        /// <summary>
        /// COM 失敗で同期停止中か。
        /// </summary>
        public bool IsUnavailable
        {
            get { return _scrollUnavailable; }
        }

        /// <summary>
        /// 左行 → 右行（公開: テスト用）。
        /// </summary>
        public int MapLeftToRight(int leftRow)
        {
            ContentScrollMap map = _activeMap ?? ContentScrollMap.Identity;
            return map.MapLeftToRight(leftRow);
        }

        /// <summary>
        /// 右行 → 左行（公開: テスト用）。
        /// </summary>
        public int MapRightToLeft(int rightRow)
        {
            ContentScrollMap map = _activeMap ?? ContentScrollMap.Identity;
            return map.MapRightToLeft(rightRow);
        }

        /// <summary>
        /// ギャップ→Equal かつ |Δrow|≥3 のとき再同期トースト文言を生成する（純関数）。
        /// </summary>
        /// <param name="prev">前回セグメント</param>
        /// <param name="next">今回セグメント</param>
        /// <param name="oldR">相手側の前回行</param>
        /// <param name="newR">相手側の今回行</param>
        /// <param name="fromRight">右駆動なら true（API 互換・文言には未使用）</param>
        /// <returns>ヒント文字列。条件外は null。</returns>
        public static string BuildJumpHint(
            SyncSegmentKind prev,
            SyncSegmentKind next,
            int oldR,
            int newR,
            bool fromRight)
        {
            bool wasGap = prev == SyncSegmentKind.LeftOnly || prev == SyncSegmentKind.RightOnly;
            if (wasGap && next == SyncSegmentKind.Equal && Math.Abs(newR - oldR) >= 3)
            {
                return string.Format("同じ内容で再同期しました（{0}行 → {1}行）", oldR, newR);
            }

            return null;
        }

        /// <summary>
        /// 左操作で同期を適用する（イベント駆動）。
        /// COM 接続時は 16ms coalesce（最後の行のみ）。未接続・スモークは即時。
        /// 縦は内容マップ、横は列 1:1。
        /// </summary>
        public void ApplyDrivenByLeft(int leftRow, int leftCol)
        {
            if (_disposed || _isBusy)
            {
                return;
            }

            leftRow = Math.Max(1, leftRow);
            leftCol = Math.Max(1, leftCol);
            QueueOrApply(SyncDriveSide.Left, leftRow, leftCol);
        }

        /// <summary>
        /// 右操作で同期を適用する（イベント駆動）。
        /// COM 接続時は 16ms coalesce（最後の行のみ）。未接続・スモークは即時。
        /// 縦は内容マップ、横は列 1:1。
        /// </summary>
        public void ApplyDrivenByRight(int rightRow, int rightCol)
        {
            if (_disposed || _isBusy)
            {
                return;
            }

            rightRow = Math.Max(1, rightRow);
            rightCol = Math.Max(1, rightCol);
            QueueOrApply(SyncDriveSide.Right, rightRow, rightCol);
        }

        /// <summary>
        /// 保留中の COM coalesce を即時フラッシュする（テスト用）。
        /// </summary>
        public void FlushPendingApply()
        {
            if (_disposed)
            {
                return;
            }

            if (_coalesceTimer != null && _coalesceTimer.IsEnabled)
            {
                _coalesceTimer.Stop();
            }

            if (_pendingDrive == SyncDriveSide.Left || _pendingDrive == SyncDriveSide.Right)
            {
                ApplyPendingCom();
            }
        }

        /// <summary>
        /// リソース解放。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelPendingApply();
            Detach();
            _timer.Tick -= Timer_Tick;
            if (_coalesceTimer != null)
            {
                _coalesceTimer.Tick -= CoalesceTimer_Tick;
            }
        }

        /// <summary>
        /// アクティブマップを解決する。両シート指定で完全一致なし → Unpaired。
        /// </summary>
        private void ResolveActiveMap()
        {
            _sheetsUnpaired = false;
            if (_mapSet == null || _mapSet.Count == 0)
            {
                _activeMap = ContentScrollMap.Identity;
                return;
            }

            if (!string.IsNullOrEmpty(_leftSheet) && !string.IsNullOrEmpty(_rightSheet))
            {
                ContentScrollMap exact = _mapSet.ResolveExact(_leftSheet, _rightSheet);
                if (exact != null)
                {
                    _activeMap = exact;
                    return;
                }

                // 両シート名があるのにペア無し → 誤マップを使わず同期しない
                _activeMap = ContentScrollMap.Identity;
                _sheetsUnpaired = true;
                return;
            }

            ContentScrollMap resolved = _mapSet.Resolve(_leftSheet, _rightSheet);
            _activeMap = resolved ?? ContentScrollMap.Identity;
            _sheetsUnpaired = resolved == null
                && !string.IsNullOrEmpty(_leftSheet)
                && !string.IsNullOrEmpty(_rightSheet);
        }

        /// <summary>
        /// 状態は即時 Publish。COM 接続時のみ 16ms one-shot で follower スクロールを coalesce。
        /// </summary>
        private void QueueOrApply(SyncDriveSide drive, int row, int col)
        {
            if (_disposed || _isBusy)
            {
                return;
            }

            // 連打の最初だけイージング起点を固定（途中の状態更新で潰さない）
            bool newBurst = _coalesceTimer == null || !_coalesceTimer.IsEnabled;
            int fromFollower = drive == SyncDriveSide.Left
                ? Math.Max(1, _lastRightRow > 0 ? _lastRightRow : 1)
                : Math.Max(1, _lastLeftRow > 0 ? _lastLeftRow : 1);
            SyncSegmentKind prevKind = CurrentState != null ? CurrentState.SegmentKind : SyncSegmentKind.Identity;

            // 状態・マップは常に即時（UI / NoteScroll / Smoke 用）
            ApplyDrivenStateOnly(drive, row, col);

            bool needsCom = _enabled
                && !_scrollUnavailable
                && !_sheetsUnpaired
                && ((drive == SyncDriveSide.Left && _right != null && _right.IsOpen)
                    || (drive == SyncDriveSide.Right && _left != null && _left.IsOpen));

            if (!needsCom)
            {
                CancelPendingApply();
                return;
            }

            int toFollower = drive == SyncDriveSide.Left
                ? Math.Max(1, _lastRightRow)
                : Math.Max(1, _lastLeftRow);
            int followerCol = drive == SyncDriveSide.Left
                ? Math.Max(1, _lastRightCol)
                : Math.Max(1, _lastLeftCol);
            SyncSegmentKind nextKind = CurrentState != null
                ? CurrentState.SegmentKind
                : SyncSegmentKind.Identity;

            _pendingDrive = drive;
            _pendingRow = row;
            _pendingCol = col;
            if (newBurst)
            {
                _pendingFromFollowerRow = fromFollower;
                _pendingPrevKind = prevKind;
            }

            _pendingToFollowerRow = toFollower;
            _pendingFollowerCol = followerCol;
            _pendingNextKind = nextKind;
            _coalesceTimer.Stop();
            _coalesceTimer.Start();
        }

        private void CoalesceTimer_Tick(object sender, EventArgs e)
        {
            _coalesceTimer.Stop();
            if (_disposed || _isBusy)
            {
                _pendingDrive = SyncDriveSide.None;
                return;
            }

            if (_pendingDrive == SyncDriveSide.Left || _pendingDrive == SyncDriveSide.Right)
            {
                ApplyPendingCom();
            }
        }

        private void CancelPendingApply()
        {
            _pendingDrive = SyncDriveSide.None;
            if (_coalesceTimer != null && _coalesceTimer.IsEnabled)
            {
                _coalesceTimer.Stop();
            }
        }

        /// <summary>
        /// 保留中の follower COM スクロールを適用する。
        /// </summary>
        private void ApplyPendingCom()
        {
            SyncDriveSide drive = _pendingDrive;
            int fromRow = _pendingFromFollowerRow;
            int toRow = _pendingToFollowerRow;
            int col = _pendingFollowerCol;
            SyncSegmentKind prevKind = _pendingPrevKind;
            SyncSegmentKind nextKind = _pendingNextKind;
            _pendingDrive = SyncDriveSide.None;

            if (_disposed || _isBusy || !_enabled || _scrollUnavailable || _sheetsUnpaired)
            {
                return;
            }

            _syncing = true;
            try
            {
                if (drive == SyncDriveSide.Left && _right != null && _right.IsOpen)
                {
                    if (!TrySetFollowerScroll(_right, fromRow, toRow, col, prevKind, nextKind))
                    {
                        RegisterFailure("ApplyDrivenByLeft 右スクロール設定失敗");
                    }
                }
                else if (drive == SyncDriveSide.Right && _left != null && _left.IsOpen)
                {
                    if (!TrySetFollowerScroll(_left, fromRow, toRow, col, prevKind, nextKind))
                    {
                        RegisterFailure("ApplyDrivenByRight 左スクロール設定失敗");
                    }
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// マップ結果を last* に反映し State を Publish（COM なし）。
        /// </summary>
        private void ApplyDrivenStateOnly(SyncDriveSide drive, int row, int col)
        {
            if (drive == SyncDriveSide.Left)
            {
                ApplyDrivenByLeftState(row, col);
            }
            else if (drive == SyncDriveSide.Right)
            {
                ApplyDrivenByRightState(row, col);
            }
        }

        private void ApplyDrivenByLeftState(int leftRow, int leftCol)
        {
            if (!_enabled)
            {
                PublishState(SyncDriveSide.Left, leftRow, Math.Max(1, _lastRightRow), leftCol, Math.Max(1, _lastRightCol));
                return;
            }

            if (_scrollUnavailable)
            {
                PublishState(SyncDriveSide.Left, leftRow, Math.Max(1, _lastRightRow), leftCol, Math.Max(1, _lastRightCol));
                return;
            }

            if (_sheetsUnpaired)
            {
                _lastLeftRow = leftRow;
                _lastLeftCol = leftCol;
                PublishState(SyncDriveSide.Left, leftRow, Math.Max(1, _lastRightRow), leftCol, Math.Max(1, _lastRightCol));
                return;
            }

            int rightRow = MapLeftToRight(leftRow);
            int rightCol = leftCol;
            _lastLeftRow = leftRow;
            _lastLeftCol = leftCol;
            _lastRightRow = rightRow;
            _lastRightCol = rightCol;
            PublishState(SyncDriveSide.Left, leftRow, rightRow, leftCol, rightCol);
        }

        private void ApplyDrivenByRightState(int rightRow, int rightCol)
        {
            if (!_enabled)
            {
                PublishState(SyncDriveSide.Right, Math.Max(1, _lastLeftRow), rightRow, Math.Max(1, _lastLeftCol), rightCol);
                return;
            }

            if (_scrollUnavailable)
            {
                PublishState(SyncDriveSide.Right, Math.Max(1, _lastLeftRow), rightRow, Math.Max(1, _lastLeftCol), rightCol);
                return;
            }

            if (_sheetsUnpaired)
            {
                _lastRightRow = rightRow;
                _lastRightCol = rightCol;
                PublishState(SyncDriveSide.Right, Math.Max(1, _lastLeftRow), rightRow, Math.Max(1, _lastLeftCol), rightCol);
                return;
            }

            int leftRow = MapRightToLeft(rightRow);
            int leftCol = rightCol;
            _lastRightRow = rightRow;
            _lastRightCol = rightCol;
            _lastLeftRow = leftRow;
            _lastLeftCol = leftCol;
            PublishState(SyncDriveSide.Right, leftRow, rightRow, leftCol, rightCol);
        }

        /// <summary>
        /// 保険ポーリング本体（主経路は ApplyDriven）。
        /// 位置が前回 Apply と一致しているときは何もしない。変化検知時のみマップ適用。
        /// 横: 列 1:1。縦: 内容マップ。
        /// </summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_syncing || _disposed)
            {
                return;
            }

            bool hasLeft = _left != null && _left.IsOpen;
            bool hasRight = _right != null && _right.IsOpen;
            if (!hasLeft && !hasRight)
            {
                return;
            }

            int leftRow = _lastLeftRow > 0 ? _lastLeftRow : 1;
            int leftCol = _lastLeftCol > 0 ? _lastLeftCol : 1;
            int rightRow = _lastRightRow > 0 ? _lastRightRow : 1;
            int rightCol = _lastRightCol > 0 ? _lastRightCol : 1;
            bool leftOk = false;
            bool rightOk = false;

            try
            {
                if (hasLeft)
                {
                    leftOk = _left.TryGetScroll(out leftRow, out leftCol);
                }

                if (hasRight)
                {
                    rightOk = _right.TryGetScroll(out rightRow, out rightCol);
                }
            }
            catch (Exception ex)
            {
                RegisterFailure(ex.Message);
                return;
            }

            // MiniMap 用: 取得できた側の位置を通知（変化時）
            if (leftOk || rightOk)
            {
                int reportL = leftOk ? leftRow : Math.Max(1, _lastLeftRow > 0 ? _lastLeftRow : leftRow);
                int reportR = rightOk ? rightRow : Math.Max(1, _lastRightRow > 0 ? _lastRightRow : rightRow);
                bool changed = reportL != _lastLeftRow || reportR != _lastRightRow || _lastLeftRow < 0;
                if (changed)
                {
                    try
                    {
                        ViewportChanged?.Invoke(reportL, reportR);
                    }
                    catch
                    {
                        // ignore subscriber errors
                    }
                }
            }

            // 左右同期は両方取れて有効・未対応でないときだけ
            if (!_enabled || _scrollUnavailable || _sheetsUnpaired || _isBusy
                || !hasLeft || !hasRight || !leftOk || !rightOk)
            {
                if (leftOk)
                {
                    _lastLeftRow = leftRow;
                    _lastLeftCol = leftCol;
                }

                if (rightOk)
                {
                    _lastRightRow = rightRow;
                    _lastRightCol = rightCol;
                }

                if (_sheetsUnpaired && (leftOk || rightOk))
                {
                    PublishState(
                        SyncDriveSide.None,
                        leftOk ? leftRow : Math.Max(1, _lastLeftRow),
                        rightOk ? rightRow : Math.Max(1, _lastRightRow),
                        leftOk ? leftCol : Math.Max(1, _lastLeftCol),
                        rightOk ? rightCol : Math.Max(1, _lastRightCol));
                }

                if (!leftOk && !rightOk)
                {
                    RegisterFailure("ScrollRow/Column を取得できません");
                }

                return;
            }

            _failCount = 0;

            bool leftChanged = leftRow != _lastLeftRow || leftCol != _lastLeftCol;
            bool rightChanged = rightRow != _lastRightRow || rightCol != _lastRightCol;
            if (!leftChanged && !rightChanged)
            {
                if (_lastLeftRow < 0)
                {
                    _lastLeftRow = leftRow;
                    _lastLeftCol = leftCol;
                    _lastRightRow = rightRow;
                    _lastRightCol = rightCol;
                }

                return;
            }

            _syncing = true;
            try
            {
                SyncSegmentKind prevKind = CurrentState != null ? CurrentState.SegmentKind : SyncSegmentKind.Identity;
                ContentScrollMap map = _activeMap ?? ContentScrollMap.Identity;

                if (leftChanged && !rightChanged)
                {
                    // 左が動いた → 右の縦は内容マップ、横は列そのまま
                    int targetRightRow = MapLeftToRight(leftRow);
                    int targetRightCol = leftCol; // 横はセル位置同期
                    int oldRight = Math.Max(1, _lastRightRow > 0 ? _lastRightRow : targetRightRow);
                    SyncSegmentKind nextKind = map.ProbeFromLeft(leftRow).Kind;
                    if (!TrySetFollowerScroll(_right, oldRight, targetRightRow, targetRightCol, prevKind, nextKind))
                    {
                        RegisterFailure("右スクロール設定失敗");
                        return;
                    }

                    _lastLeftRow = leftRow;
                    _lastLeftCol = leftCol;
                    _lastRightRow = targetRightRow;
                    _lastRightCol = targetRightCol;
                    PublishState(SyncDriveSide.Left, leftRow, targetRightRow, leftCol, targetRightCol);
                }
                else if (rightChanged && !leftChanged)
                {
                    int targetLeftRow = MapRightToLeft(rightRow);
                    int targetLeftCol = rightCol;
                    int oldLeft = Math.Max(1, _lastLeftRow > 0 ? _lastLeftRow : targetLeftRow);
                    SyncSegmentKind nextKind = map.ProbeFromRight(rightRow).Kind;
                    if (!TrySetFollowerScroll(_left, oldLeft, targetLeftRow, targetLeftCol, prevKind, nextKind))
                    {
                        RegisterFailure("左スクロール設定失敗");
                        return;
                    }

                    _lastRightRow = rightRow;
                    _lastRightCol = rightCol;
                    _lastLeftRow = targetLeftRow;
                    _lastLeftCol = targetLeftCol;
                    PublishState(SyncDriveSide.Right, targetLeftRow, rightRow, targetLeftCol, rightCol);
                }
                else
                {
                    // 同時変化: 左を優先（縦はマップ、横は左列）
                    int targetRightRow = MapLeftToRight(leftRow);
                    int targetRightCol = leftCol;
                    int oldRight = Math.Max(1, _lastRightRow > 0 ? _lastRightRow : targetRightRow);
                    SyncSegmentKind nextKind = map.ProbeFromLeft(leftRow).Kind;
                    if (!TrySetFollowerScroll(_right, oldRight, targetRightRow, targetRightCol, prevKind, nextKind))
                    {
                        RegisterFailure("同時変化時の右スクロール設定失敗");
                        return;
                    }

                    _lastLeftRow = leftRow;
                    _lastLeftCol = leftCol;
                    _lastRightRow = targetRightRow;
                    _lastRightCol = targetRightCol;
                    PublishState(SyncDriveSide.Both, leftRow, targetRightRow, leftCol, targetRightCol);
                }

                _failCount = 0;
            }
            catch (Exception ex)
            {
                RegisterFailure(ex.Message);
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// SyncSessionState を組み立てて StateChanged を発火する。
        /// ギャップ→Equal かつ相手行 |Δ|≥3 のとき <see cref="SyncSessionState.JumpHint"/> をセットする。
        /// </summary>
        private void PublishState(
            SyncDriveSide drive,
            int leftRow,
            int rightRow,
            int leftCol,
            int rightCol)
        {
            leftRow = Math.Max(1, leftRow);
            rightRow = Math.Max(1, rightRow);
            leftCol = Math.Max(1, leftCol);
            rightCol = Math.Max(1, rightCol);

            SyncSegmentKind prevKind = CurrentState != null ? CurrentState.SegmentKind : SyncSegmentKind.Identity;
            int prevLeft = CurrentState != null ? CurrentState.LeftRow : leftRow;
            int prevRight = CurrentState != null ? CurrentState.RightRow : rightRow;

            SyncSegmentKind kind;
            if (!_enabled)
            {
                kind = SyncSegmentKind.Disabled;
            }
            else if (_scrollUnavailable)
            {
                kind = SyncSegmentKind.Unavailable;
            }
            else if (_sheetsUnpaired)
            {
                kind = SyncSegmentKind.Unpaired;
            }
            else
            {
                // 駆動側を基準にプローブ（Both/External は左優先）
                ContentScrollMap map = _activeMap ?? ContentScrollMap.Identity;
                ScrollMapProbe probe = drive == SyncDriveSide.Right
                    ? map.ProbeFromRight(rightRow)
                    : map.ProbeFromLeft(leftRow);
                kind = probe.Kind;
            }

            // 相手側（フォロワー）の行ジャンプをヒント化
            bool fromRight = drive == SyncDriveSide.Right;
            int oldR = fromRight ? prevLeft : prevRight;
            int newR = fromRight ? leftRow : rightRow;
            string jumpHint = BuildJumpHint(prevKind, kind, oldR, newR, fromRight);

            var state = new SyncSessionState
            {
                Enabled = _enabled && !_scrollUnavailable,
                SegmentKind = kind,
                DriveSide = drive,
                LeftRow = leftRow,
                RightRow = rightRow,
                LeftCol = leftCol,
                RightCol = rightCol,
                LeftSheet = _leftSheet,
                RightSheet = _rightSheet,
                JumpHint = jumpHint,
                UtcUpdated = DateTime.UtcNow
            };
            state.RefreshStatusLine(unavailable: _scrollUnavailable);
            CurrentState = state;

            try
            {
                StateChanged?.Invoke(state);
            }
            catch
            {
                // ignore subscriber errors
            }
        }

        /// <summary>
        /// Ui.ReduceMotion を読む（失敗時 false）。
        /// </summary>
        private static bool IsReduceMotion()
        {
            try
            {
                return AppSettings.Current != null
                    && AppSettings.Current.Ui != null
                    && AppSettings.Current.Ui.ReduceMotion;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 相手側スクロール設定。ギャップ→Equal かつ |Δ|≥3 で ReduceMotion=false のとき mid 経由（各 50ms）。
        /// </summary>
        private static bool TrySetFollowerScroll(
            ExcelWorkbookSession session,
            int fromRow,
            int toRow,
            int col,
            SyncSegmentKind prevKind,
            SyncSegmentKind nextKind)
        {
            if (session == null)
            {
                return false;
            }

            bool wantEase = !IsReduceMotion()
                && (prevKind == SyncSegmentKind.LeftOnly || prevKind == SyncSegmentKind.RightOnly)
                && nextKind == SyncSegmentKind.Equal
                && Math.Abs(toRow - fromRow) >= 3;

            if (!wantEase)
            {
                return session.TrySetScroll(toRow, col);
            }

            int mid = (fromRow + toRow) / 2;
            if (mid != fromRow && mid != toRow)
            {
                if (!session.TrySetScroll(mid, col))
                {
                    return session.TrySetScroll(toRow, col);
                }

                System.Threading.Thread.Sleep(50);
            }

            bool ok = session.TrySetScroll(toRow, col);
            if (ok)
            {
                System.Threading.Thread.Sleep(50);
            }

            return ok;
        }

        /// <summary>
        /// 失敗を数え、閾値で同期を止める。
        /// </summary>
        private void RegisterFailure(string message)
        {
            _failCount++;
            if (_failCount == 1 || _failCount == MaxConsecutiveFailures)
            {
                Log.Debug("ScrollSync 失敗 (" + _failCount + "): " + message);
            }

            if (_failCount >= MaxConsecutiveFailures)
            {
                _scrollUnavailable = true;
                _timer.Stop();
                Log.Info("同期スクロールを停止しました（Excel から Scroll を安定取得できないため）。MiniMap ジャンプは可能な場合があります。");
                PublishState(
                    SyncDriveSide.None,
                    Math.Max(1, _lastLeftRow),
                    Math.Max(1, _lastRightRow),
                    Math.Max(1, _lastLeftCol),
                    Math.Max(1, _lastRightCol));
            }
        }

        /// <summary>
        /// 前回値をリセットする。
        /// </summary>
        private void ResetLast()
        {
            _lastLeftRow = -1;
            _lastLeftCol = -1;
            _lastRightRow = -1;
            _lastRightCol = -1;
        }
    }
}
