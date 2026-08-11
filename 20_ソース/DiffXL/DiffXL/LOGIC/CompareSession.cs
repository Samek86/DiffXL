using DiffXL.LOGIC.Diff;

namespace DiffXL.LOGIC
{
    /// <summary>
    /// 現在の比較セッション状態。
    /// </summary>
    public sealed class CompareSession
    {
        /// <summary>
        /// 左ファイルパス。
        /// </summary>
        public string LeftPath { get; set; }

        /// <summary>
        /// 右ファイルパス。
        /// </summary>
        public string RightPath { get; set; }

        /// <summary>
        /// 直近の比較結果。
        /// </summary>
        public DiffResult LastResult { get; set; }

        /// <summary>
        /// 比較オプション（シート対応・アンカー）。
        /// </summary>
        public CompareOptions Options { get; set; } = new CompareOptions();

        /// <summary>
        /// 比較処理中か。
        /// </summary>
        public bool IsBusy { get; set; }

        /// <summary>
        /// 左右パスが揃っているか。
        /// </summary>
        public bool HasBothPaths
        {
            get
            {
                return !string.IsNullOrWhiteSpace(LeftPath) && !string.IsNullOrWhiteSpace(RightPath);
            }
        }

        /// <summary>
        /// セッションをクリアする。
        /// </summary>
        public void Reset()
        {
            LeftPath = null;
            RightPath = null;
            LastResult = null;
            Options = new CompareOptions();
            IsBusy = false;
        }
    }
}
