using System;
using System.Collections.Generic;
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

        private void PushToMiniMap()
        {
            if (_miniMap == null)
            {
                return;
            }

            // シート順を先に確定（比較 SheetPairs の順 = ブック順）
            IList<string> order = BuildSheetOrder(_result);
            if (order.Count > 0)
            {
                _miniMap.SetSheetOrder(order);
            }

            if (!_isVisible || _result == null || _result.Items == null || _result.Items.Count == 0)
            {
                _miniMap.SetDiffs(Enumerable.Empty<DiffItem>());
                if (order.Count > 0)
                {
                    _miniMap.SetSheetOrder(order);
                }

                return;
            }

            _miniMap.SetDiffs(_result.Items);
            // SetDiffs が内部で帯を作り直すので、再度シート順を適用
            if (order.Count > 0)
            {
                _miniMap.SetSheetOrder(order);
            }
        }

        private static List<string> BuildSheetOrder(DiffResult result)
        {
            var order = new List<string>();
            if (result == null)
            {
                return order;
            }

            if (result.SheetPairs != null)
            {
                foreach (SheetPair p in result.SheetPairs)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    // 表示名は左を優先（左右同名が基本）
                    string name = !string.IsNullOrEmpty(p.LeftSheet) ? p.LeftSheet : p.RightSheet;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (!order.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        order.Add(name);
                    }
                }
            }

            // 差分にあって pairs に無いシートを末尾に追加
            if (result.Items != null)
            {
                foreach (DiffItem item in result.Items.OrderBy(i => i.OrderHint))
                {
                    if (item == null)
                    {
                        continue;
                    }

                    string name = item.SheetLeft ?? item.SheetRight;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (!order.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        order.Add(name);
                    }
                }
            }

            return order;
        }
    }
}
