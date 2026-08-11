using System;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// 同期を駆動した側。
    /// </summary>
    public enum SyncDriveSide
    {
        None,
        Left,
        Right,
        Both,
        External
    }

    /// <summary>
    /// 内容マップ上のセグメント種別（または同期停止状態）。
    /// </summary>
    public enum SyncSegmentKind
    {
        /// <summary>内容 1:1 対応。</summary>
        Equal,

        /// <summary>左のみ（右ホールド）。</summary>
        LeftOnly,

        /// <summary>右のみ（左ホールド）。</summary>
        RightOnly,

        /// <summary>マップなしフォールバック（行番号 1:1）。</summary>
        Identity,

        /// <summary>SyncScroll OFF。</summary>
        Disabled,

        /// <summary>COM 失敗で停止。</summary>
        Unavailable,

        /// <summary>左右シートがマップ上で未対応（同期しない）。</summary>
        Unpaired
    }

    /// <summary>
    /// ある行についての内容マップ照会結果。
    /// </summary>
    public sealed class ScrollMapProbe
    {
        public SyncSegmentKind Kind { get; set; }

        /// <summary>相手側へマップした行（ホールド時は HoldRow）。</summary>
        public int MappedRow { get; set; }

        /// <summary>ギャップ時のホールド行。Equal/Identity では 0。</summary>
        public int HoldRow { get; set; }

        /// <summary>照会側セグメント開始行。</summary>
        public int SegmentStart { get; set; }

        /// <summary>照会側セグメント終了行。</summary>
        public int SegmentEnd { get; set; }
    }

    /// <summary>
    /// 同期セッションの現在状態（UI ステータスバー／オーバーレイ用）。
    /// </summary>
    public sealed class SyncSessionState
    {
        public bool Enabled { get; set; }

        public SyncSegmentKind SegmentKind { get; set; }

        public SyncDriveSide DriveSide { get; set; }

        public int LeftRow { get; set; }

        public int RightRow { get; set; }

        public int LeftCol { get; set; }

        public int RightCol { get; set; }

        public string LeftSheet { get; set; }

        public string RightSheet { get; set; }

        /// <summary>ユーザー向け 1 行（フッター用）。</summary>
        public string StatusLine { get; set; }

        /// <summary>ギャップ時の短い理由（オーバーレイ用）。</summary>
        public string GapCaption { get; set; }

        /// <summary>再同期ジャンプ時の短いヒント（トースト用・Task 以降）。</summary>
        public string JumpHint { get; set; }

        public bool IsInGap
        {
            get
            {
                return SegmentKind == SyncSegmentKind.LeftOnly
                    || SegmentKind == SyncSegmentKind.RightOnly;
            }
        }

        public DateTime UtcUpdated { get; set; }

        /// <summary>
        /// ステータスバー用の 1 行文言を組み立てる（純関数・副作用なし）。
        /// </summary>
        public static string BuildStatusLine(
            bool enabled,
            bool unavailable,
            SyncSegmentKind kind,
            int leftRow,
            int rightRow)
        {
            // Unavailable は Enabled=false で Publish されるため、OFF より先に判定する
            if (unavailable || kind == SyncSegmentKind.Unavailable)
            {
                return "同期停止 · Excelスクロールを取得できません";
            }

            if (!enabled || kind == SyncSegmentKind.Disabled)
            {
                return "同期OFF";
            }

            switch (kind)
            {
                case SyncSegmentKind.RightOnly:
                    return string.Format("同期ON · 右のみの内容 · 左は行{0}で待機", leftRow);

                case SyncSegmentKind.LeftOnly:
                    return string.Format("同期ON · 左のみの内容 · 右は行{0}で待機", rightRow);

                case SyncSegmentKind.Identity:
                    return string.Format("同期ON · 行番号同期 · L{0} ↔ R{1}", leftRow, rightRow);

                case SyncSegmentKind.Unpaired:
                    return "同期ON · シート未対応";

                default:
                    return string.Format("同期ON · 内容対応 · L{0} ↔ R{1}", leftRow, rightRow);
            }
        }

        /// <summary>
        /// ギャップオーバーレイ用の短いキャプション。ギャップ外は null。
        /// </summary>
        public static string BuildGapCaption(SyncSegmentKind kind, int holdRow)
        {
            switch (kind)
            {
                case SyncSegmentKind.RightOnly:
                    return string.Format("右のみ · 左は行{0}で待機", holdRow);

                case SyncSegmentKind.LeftOnly:
                    return string.Format("左のみ · 右は行{0}で待機", holdRow);

                default:
                    return null;
            }
        }

        /// <summary>
        /// 状態オブジェクトから StatusLine を再計算して設定する。
        /// </summary>
        public void RefreshStatusLine(bool unavailable = false)
        {
            StatusLine = BuildStatusLine(Enabled, unavailable, SegmentKind, LeftRow, RightRow);
            GapCaption = BuildGapCaption(SegmentKind, IsInGap
                ? (SegmentKind == SyncSegmentKind.RightOnly ? LeftRow : RightRow)
                : 0);
        }
    }
}
