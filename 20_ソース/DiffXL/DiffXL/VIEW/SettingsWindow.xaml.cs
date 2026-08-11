using System;
using System.Globalization;
using System.Windows;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW
{
    /// <summary>
    /// 設定画面（差分色・不透明度・同期スクロールなど）。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>
        /// InitializeComponent / 初期ロード中は変更イベントを無視する。
        /// </summary>
        private bool _suppressChangeEvents = true;

        /// <summary>
        /// 保存されたか。
        /// </summary>
        public bool Saved { get; private set; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public SettingsWindow()
        {
            InitializeComponent();
            LoadFromSettings();
            UpdatePreview();
            _suppressChangeEvents = false;
        }

        /// <summary>
        /// 現在設定を UI に反映する。
        /// </summary>
        private void LoadFromSettings()
        {
            DiffSettings d = (AppSettings.Current != null && AppSettings.Current.Diff != null)
                ? AppSettings.Current.Diff
                : new DiffSettings();

            if (ColorTextBox != null)
            {
                ColorTextBox.Text = string.IsNullOrWhiteSpace(d.HighlightColor) ? "#FFFF00" : d.HighlightColor;
            }

            if (OpacitySlider != null)
            {
                OpacitySlider.Value = Math.Max(0, Math.Min(100, d.HighlightOpacity * 100.0));
            }

            if (HighlightEnabledCheck != null)
            {
                HighlightEnabledCheck.IsChecked = d.HighlightEnabled;
            }

            if (OpacityLabel != null && OpacitySlider != null)
            {
                OpacityLabel.Text = ((int)Math.Round(OpacitySlider.Value)).ToString(CultureInfo.InvariantCulture) + "%";
            }

            UiSettings ui = (AppSettings.Current != null && AppSettings.Current.Ui != null)
                ? AppSettings.Current.Ui
                : new UiSettings();

            if (SyncScrollCheck != null)
            {
                SyncScrollCheck.IsChecked = ui.SyncScroll;
            }

            if (ShowSyncGapOverlayCheck != null)
            {
                ShowSyncGapOverlayCheck.IsChecked = ui.ShowSyncGapOverlay;
            }

            if (ShowSyncToastOnJumpCheck != null)
            {
                ShowSyncToastOnJumpCheck.IsChecked = ui.ShowSyncToastOnJump;
            }

            if (ReduceMotionCheck != null)
            {
                ReduceMotionCheck.IsChecked = ui.ReduceMotion;
            }

            if (SyncPollFallbackMsBox != null)
            {
                int ms = ui.SyncPollFallbackMs;
                if (ms < 100)
                {
                    ms = 100;
                }
                else if (ms > 1000)
                {
                    ms = 1000;
                }

                SyncPollFallbackMsBox.Text = ms.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 色・不透明度変更時にプレビュー更新。
        /// InitializeComponent 中は ColorTextBox の TextChanged が先に走り OpacitySlider が null になり得る。
        /// </summary>
        private void ColorOrOpacity_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressChangeEvents)
            {
                return;
            }

            if (OpacitySlider == null || OpacityLabel == null || ColorTextBox == null || PreviewRect == null)
            {
                return;
            }

            OpacityLabel.Text = ((int)Math.Round(OpacitySlider.Value)).ToString(CultureInfo.InvariantCulture) + "%";
            UpdatePreview();
        }

        /// <summary>
        /// プレビュー矩形を更新する。
        /// </summary>
        private void UpdatePreview()
        {
            if (OpacitySlider == null || ColorTextBox == null || PreviewRect == null)
            {
                return;
            }

            var style = new DiffHighlightStyle
            {
                Opacity = OpacitySlider.Value / 100.0
            };
            byte r, g, b;
            DiffHighlightStyle.ParseHexRgbColor(ColorTextBox.Text, out r, out g, out b);
            style.R = r;
            style.G = g;
            style.B = b;
            PreviewRect.Fill = style.CreateBrush();
        }

        /// <summary>
        /// UI 上の色・不透明度を設定する（自動テスト／ダイアログ経路用）。
        /// </summary>
        public void SetHighlightUi(string colorHex, double opacity01)
        {
            if (ColorTextBox != null && !string.IsNullOrWhiteSpace(colorHex))
            {
                ColorTextBox.Text = colorHex;
            }

            if (OpacitySlider != null)
            {
                OpacitySlider.Value = Math.Max(0, Math.Min(100, opacity01 * 100.0));
            }

            UpdatePreview();
        }

        /// <summary>
        /// 現在の UI 値で保存して閉じる（BtnSave と同じ経路）。
        /// </summary>
        /// <returns>保存成功時 true</returns>
        public bool TrySaveAndClose()
        {
            return SaveFromUiAndClose(showErrorDialog: false);
        }

        /// <summary>
        /// 保存。
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveFromUiAndClose(showErrorDialog: true);
        }

        /// <summary>
        /// UI から AppSettings へ書き込み保存する（ダイアログ保存の本体）。
        /// </summary>
        private bool SaveFromUiAndClose(bool showErrorDialog)
        {
            if (AppSettings.Current == null)
            {
                return false;
            }

            if (AppSettings.Current.Diff == null)
            {
                AppSettings.Current.Diff = new DiffSettings();
            }

            if (AppSettings.Current.Ui == null)
            {
                AppSettings.Current.Ui = new UiSettings();
            }

            string colorText = ColorTextBox != null ? ColorTextBox.Text : "#FFFF00";
            double opacity = OpacitySlider != null ? OpacitySlider.Value / 100.0 : 0.5;

            byte r, g, b;
            DiffHighlightStyle.ParseHexRgbColor(colorText, out r, out g, out b);
            var style = new DiffHighlightStyle { R = r, G = g, B = b };
            AppSettings.Current.Diff.HighlightColor = style.ToHexRgb();
            AppSettings.Current.Diff.HighlightOpacity = Math.Max(0, Math.Min(1, opacity));
            AppSettings.Current.Diff.HighlightEnabled = HighlightEnabledCheck != null && HighlightEnabledCheck.IsChecked == true;
            AppSettings.Current.Ui.SyncScroll = SyncScrollCheck != null && SyncScrollCheck.IsChecked == true;
            AppSettings.Current.Ui.ShowSyncGapOverlay = ShowSyncGapOverlayCheck == null
                || ShowSyncGapOverlayCheck.IsChecked == true;
            AppSettings.Current.Ui.ShowSyncToastOnJump = ShowSyncToastOnJumpCheck == null
                || ShowSyncToastOnJumpCheck.IsChecked == true;
            AppSettings.Current.Ui.ReduceMotion = ReduceMotionCheck != null
                && ReduceMotionCheck.IsChecked == true;
            AppSettings.Current.Ui.SyncPollFallbackMs = ParseSyncPollFallbackMs();

            try
            {
                AppSettings.Save();
                Saved = true;
                try
                {
                    DialogResult = true;
                }
                catch
                {
                    // ShowDialog 外から呼ばれた場合は DialogResult 設定不可
                }

                Close();
                Log.Info("SettingsWindow 保存完了 color=" + AppSettings.Current.Diff.HighlightColor
                    + " opacity=" + AppSettings.Current.Diff.HighlightOpacity.ToString(CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                if (showErrorDialog)
                {
                    MessageBox.Show(
                        "設定の保存に失敗しました: " + ex.Message,
                        Common.AppDisplayName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return false;
            }
        }

        /// <summary>
        /// 保険ポーリング ms を UI から読む（100–1000、不正時 250）。
        /// </summary>
        private int ParseSyncPollFallbackMs()
        {
            int ms = 250;
            if (SyncPollFallbackMsBox != null
                && int.TryParse(
                    (SyncPollFallbackMsBox.Text ?? string.Empty).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ms))
            {
                // fall through to clamp
            }
            else
            {
                ms = 250;
            }

            if (ms < 100)
            {
                ms = 100;
            }
            else if (ms > 1000)
            {
                ms = 1000;
            }

            return ms;
        }

        /// <summary>
        /// キャンセル。
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
