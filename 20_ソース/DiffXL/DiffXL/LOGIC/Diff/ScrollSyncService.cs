using System;
using System.Collections.Generic;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 左右内容ビューのスクロール位置を内容マップで同期する（COM / Excel 非依存）。
    /// 主経路: <see cref="ApplyDrivenByLeft"/> / <see cref="ApplyDrivenByRight"/>。
    /// 横: セル位置 1:1。縦: 内容対応マップ。
    /// </summary>
    public sealed class ScrollSyncService : IDisposable
    {
        /// <summary>比較中など Apply を無視するフラグ。</summary>
        private bool _isBusy;

        /// <summary>左右シートがマップ上で未対応。</summary>
        private bool _sheetsUnpaired;

        /// <summary>再入防止。</summary>
        private bool _syncing;

        /// <summary>有効フラグ。</summary>
        private bool _enabled = true;

        /// <summary>前回の左スクロール。</summary>
        private int _lastLeftRow = -1;

        /// <summary>前回の左列。</summary>
        private int _lastLeftCol = -1;

        /// <summary>前回の右スクロール。</summary>
        private int _lastRightRow = -1;

        /// <summary>前回の右列。</summary>
        private int _lastRightCol = -1;

        /// <summary>破棄済み。</summary>
        private bool _disposed;

        /// <summary>シート対応ごとの内容スクロールマップ。</summary>
        private ContentScrollMapSet _mapSet;

        /// <summary>現在有効なマップ。</summary>
        private ContentScrollMap _activeMap = ContentScrollMap.Identity;

        /// <summary>現在の左シート名。</summary>
        private string _leftSheet;

        /// <summary>現在の右シート名。</summary>
        private string _rightSheet;

        /// <summary>
        /// スクロール位置が変わったときの通知。引数: 左行, 右行。
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
        /// 比較中など。true のとき ApplyDriven は無視する。
        /// </summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            set { _isBusy = value; }
        }

        /// <summary>
        /// 左右シートが内容マップ上で未対応か。
        /// </summary>
        public bool SheetsUnpaired
        {
            get { return _sheetsUnpaired; }
        }

        /// <summary>
        /// 保険ポーリング間隔を設定から再読込する（内容ビューでは no-op。互換 API）。
        /// </summary>
        public void RefreshPollIntervalFromSettings()
        {
            // COM ポーリング廃止済み
        }

        /// <summary>
        /// 同期が有効か（設定 Ui.SyncScroll と連動）。
        /// </summary>
        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                _enabled = value;
                PublishState(
                    SyncDriveSide.None,
                    Math.Max(1, _lastLeftRow),
                    Math.Max(1, _lastRightRow),
                    Math.Max(1, _lastLeftCol),
                    Math.Max(1, _lastRightCol));
            }
        }

        /// <summary>
        /// 現在の内容マップ。
        /// </summary>
        public ContentScrollMap ActiveMap
        {
            get { return _activeMap ?? ContentScrollMap.Identity; }
        }

        /// <summary>
        /// 同期セッションを開始する（内容ビュー用。COM セッション不要）。
        /// </summary>
        public void Attach()
        {
            ResetLast();
            Log.Debug("ScrollSync Attach contentMap="
                + (_activeMap != null && _activeMap.IsContentBased));
        }

        /// <summary>
        /// 同期を停止する。
        /// </summary>
        public void Detach()
        {
            ResetLast();
            Log.Debug("ScrollSync Detach");
        }

        /// <summary>
        /// 比較結果の内容対応マップを設定する。
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
        /// 指定比率（0〜1）で左右を縦スクロールする。
        /// </summary>
        public void ScrollBothToRatio(double ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            int row = 1 + (int)Math.Round(ratio * 500);
            ScrollBothToRow(row);
        }

        /// <summary>
        /// 指定行へ左右をスクロールする（状態のみ。内容ビュー側は UI が追随）。
        /// </summary>
        public void ScrollBothToRow(int row)
        {
            if (row < 1)
            {
                row = 1;
            }

            int leftRow = row;
            int rightRow = MapLeftToRight(leftRow);
            ScrollBothToRows(leftRow, rightRow, 1, 1);
        }

        /// <summary>
        /// 左右それぞれの目標行へスクロール状態を合わせる。
        /// </summary>
        public void ScrollBothToRows(int leftRow, int rightRow, int leftCol = 1, int rightCol = 1)
        {
            leftRow = Math.Max(1, leftRow);
            rightRow = Math.Max(1, rightRow);
            leftCol = Math.Max(1, leftCol);
            rightCol = Math.Max(1, rightCol);

            _syncing = true;
            try
            {
                _lastLeftRow = leftRow;
                _lastRightRow = rightRow;
                _lastLeftCol = leftCol;
                _lastRightCol = rightCol;
                PublishState(SyncDriveSide.External, leftRow, rightRow, leftCol, rightCol);
                RaiseViewportChanged();
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// 外部（MiniMap 等）がスクロールした後、同期の基準位置を合わせる。
        /// </summary>
        public void NotifyExternalScroll(int leftRow, int rightRow, int leftCol = 1, int rightCol = 1)
        {
            _lastLeftRow = Math.Max(1, leftRow);
            _lastRightRow = Math.Max(1, rightRow);
            _lastLeftCol = Math.Max(1, leftCol);
            _lastRightCol = Math.Max(1, rightCol);
            RaiseViewportChanged();
        }

        /// <summary>
        /// 一時的に同期適用を止める。
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
        /// Unavailable 解除（内容ビューでは常に利用可能。互換 no-op + 状態再発行）。
        /// </summary>
        public void RetryAfterUnavailable()
        {
            if (_disposed)
            {
                return;
            }

            PublishState(
                SyncDriveSide.None,
                Math.Max(1, _lastLeftRow),
                Math.Max(1, _lastRightRow),
                Math.Max(1, _lastLeftCol),
                Math.Max(1, _lastRightCol));
        }

        /// <summary>
        /// 同期停止中か（内容ビューでは常に false）。
        /// </summary>
        public bool IsUnavailable
        {
            get { return false; }
        }

        /// <summary>
        /// 左行 → 右行。
        /// </summary>
        public int MapLeftToRight(int leftRow)
        {
            ContentScrollMap map = _activeMap ?? ContentScrollMap.Identity;
            return map.MapLeftToRight(leftRow);
        }

        /// <summary>
        /// 右行 → 左行。
        /// </summary>
        public int MapRightToLeft(int rightRow)
        {
            ContentScrollMap map = _activeMap ?? ContentScrollMap.Identity;
            return map.MapRightToLeft(rightRow);
        }

        /// <summary>
        /// ギャップ→Equal かつ |Δrow|≥3 のとき再同期トースト文言を生成する。
        /// </summary>
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
        /// 左操作で同期を適用する。
        /// </summary>
        public void ApplyDrivenByLeft(int leftRow, int leftCol)
        {
            if (_disposed || _isBusy || _syncing)
            {
                return;
            }

            leftRow = Math.Max(1, leftRow);
            leftCol = Math.Max(1, leftCol);
            ApplyDrivenByLeftState(leftRow, leftCol);
            RaiseViewportChanged();
        }

        /// <summary>
        /// 右操作で同期を適用する。
        /// </summary>
        public void ApplyDrivenByRight(int rightRow, int rightCol)
        {
            if (_disposed || _isBusy || _syncing)
            {
                return;
            }

            rightRow = Math.Max(1, rightRow);
            rightCol = Math.Max(1, rightCol);
            ApplyDrivenByRightState(rightRow, rightCol);
            RaiseViewportChanged();
        }

        /// <summary>
        /// 保留 Apply を即時フラッシュ（互換 no-op）。
        /// </summary>
        public void FlushPendingApply()
        {
            // 内容ビューは即時適用のため不要
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
            Detach();
        }

        /// <summary>
        /// アクティブマップを解決する。
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

                _activeMap = ContentScrollMap.Identity;
                _sheetsUnpaired = true;
                return;
            }

            ContentScrollMap resolved = _mapSet.Resolve(_leftSheet, _rightSheet);
            _activeMap = resolved ?? ContentScrollMap.Identity;
        }

        private void ApplyDrivenByLeftState(int leftRow, int leftCol)
        {
            if (!_enabled || _sheetsUnpaired)
            {
                _lastLeftRow = leftRow;
                _lastLeftCol = leftCol;
                PublishState(
                    SyncDriveSide.Left,
                    leftRow,
                    Math.Max(1, _lastRightRow),
                    leftCol,
                    Math.Max(1, _lastRightCol));
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
            if (!_enabled || _sheetsUnpaired)
            {
                _lastRightRow = rightRow;
                _lastRightCol = rightCol;
                PublishState(
                    SyncDriveSide.Right,
                    Math.Max(1, _lastLeftRow),
                    rightRow,
                    Math.Max(1, _lastLeftCol),
                    rightCol);
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
            else if (_sheetsUnpaired)
            {
                kind = SyncSegmentKind.Unpaired;
            }
            else
            {
                ContentScrollMap map = _activeMap ?? ContentScrollMap.Identity;
                ScrollMapProbe probe = drive == SyncDriveSide.Right
                    ? map.ProbeFromRight(rightRow)
                    : map.ProbeFromLeft(leftRow);
                kind = probe.Kind;
            }

            bool fromRight = drive == SyncDriveSide.Right;
            int oldR = fromRight ? prevLeft : prevRight;
            int newR = fromRight ? leftRow : rightRow;
            string jumpHint = BuildJumpHint(prevKind, kind, oldR, newR, fromRight);

            var state = new SyncSessionState
            {
                Enabled = _enabled,
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
            state.RefreshStatusLine(unavailable: false);
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

        private void RaiseViewportChanged()
        {
            try
            {
                ViewportChanged?.Invoke(
                    Math.Max(1, _lastLeftRow),
                    Math.Max(1, _lastRightRow));
            }
            catch
            {
                // ignore
            }
        }

        private void ResetLast()
        {
            _lastLeftRow = -1;
            _lastLeftCol = -1;
            _lastRightRow = -1;
            _lastRightCol = -1;
        }
    }
}
