using System;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.VIEW.Controls;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 比較結果の差分マーカーを MiniMap に反映し、画像ハイライト表示の
    /// トグル状態（VisibilityChanged）を UI に通知する。
    /// 画像の赤枠＋黄塗り自体は ImagePairView が描画し、本クラスは ON/OFF を伝播する。
    /// </summary>
    public sealed class DiffHighlightController
    {
        private readonly MiniMapControl _miniMap;
        private DiffResult _result;
        private bool _isVisible = true;

        public DiffHighlightController(MiniMapControl miniMap)
        {
            _miniMap = miniMap ?? throw new ArgumentNullException(nameof(miniMap));
            _isVisible = AppSettings.Current != null && AppSettings.Current.Diff != null
                ? AppSettings.Current.Diff.HighlightEnabled
                : true;
            SetVisible(_isVisible);
        }

        /// <summary>
        /// 差分強調（MiniMap・画像ハイライト）が表示中か。
        /// </summary>
        public bool IsVisible
        {
            get { return _isVisible; }
        }

        public DiffResult CurrentResult
        {
            get { return _result; }
        }

        /// <summary>
        /// 表示トグル変更（画像ハイライトは購読側で ImagePairView に伝播）。
        /// </summary>
        public event Action<bool> VisibilityChanged;

        public void Apply(DiffResult result)
        {
            _result = result;
            PushToMiniMap();
        }

        /// <summary>
        /// 差分強調の表示／非表示。OFF でも比較結果は保持（再比較不要）。
        /// </summary>
        /// <param name="visible">表示するなら true</param>
        public void SetVisible(bool visible)
        {
            bool changed = _isVisible != visible;
            _isVisible = visible;
            if (AppSettings.Current != null && AppSettings.Current.Diff != null)
            {
                AppSettings.Current.Diff.HighlightEnabled = visible;
                try
                {
                    AppSettings.Save();
                }
                catch (Exception ex)
                {
                    Log.Debug("HighlightEnabled 保存失敗: " + ex.Message);
                }
            }

            PushToMiniMap();
            if (changed)
            {
                VisibilityChanged?.Invoke(visible);
            }
        }

        /// <summary>
        /// 設定の色などを MiniMap に再適用する（画像側は UI が RefreshImageHighlightStyle）。
        /// </summary>
        public void RefreshStyleFromSettings()
        {
            PushToMiniMap();
        }

        public void ClearResult()
        {
            _result = null;
            if (_miniMap != null)
            {
                _miniMap.Clear();
            }
        }

        /// <summary>
        /// MiniMap は現在シートのみを MainWindow 側で載せる。
        /// ここは非表示時のクリアと結果保持のみ（全シート横断はしない）。
        /// </summary>
        private void PushToMiniMap()
        {
            if (_miniMap == null)
            {
                return;
            }

            if (!_isVisible)
            {
                _miniMap.SetDiffs(Enumerable.Empty<DiffItem>());
            }
            // 表示 ON 時は MainWindow.RefreshMiniMapForCurrentSheet が現在シート分をセットする
        }
    }
}
