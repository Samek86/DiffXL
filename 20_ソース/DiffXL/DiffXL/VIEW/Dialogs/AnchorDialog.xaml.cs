using System.Windows;

namespace DiffXL.VIEW.Dialogs
{
    /// <summary>
    /// アンカー（比較開始位置）ダイアログ（SCR-04）。
    /// </summary>
    public partial class AnchorDialog : Window
    {
        /// <summary>
        /// 左アンカー。
        /// </summary>
        public string AnchorLeftAddress { get; private set; }

        /// <summary>
        /// 右アンカー。
        /// </summary>
        public string AnchorRightAddress { get; private set; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public AnchorDialog(string leftAnchor, string rightAnchor)
        {
            InitializeComponent();
            LeftAnchorBox.Text = string.IsNullOrWhiteSpace(leftAnchor) ? "A1" : leftAnchor;
            RightAnchorBox.Text = string.IsNullOrWhiteSpace(rightAnchor) ? "A1" : rightAnchor;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            AnchorLeftAddress = (LeftAnchorBox.Text ?? string.Empty).Trim();
            AnchorRightAddress = (RightAnchorBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(AnchorLeftAddress) || string.IsNullOrEmpty(AnchorRightAddress))
            {
                MessageBox.Show(
                    "左右のアンカーを入力してください。",
                    "DiffXL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
