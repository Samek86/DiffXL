using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Dialogs
{
    /// <summary>
    /// シート手動対応ダイアログ（SCR-03）。
    /// </summary>
    public partial class SheetMapDialog : Window
    {
        /// <summary>
        /// 適用された対応ペア。
        /// </summary>
        public List<SheetPair> ResultPairs { get; private set; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public SheetMapDialog(IList<string> leftSheets, IList<string> rightSheets)
        {
            InitializeComponent();
            if (leftSheets != null)
            {
                foreach (string s in leftSheets)
                {
                    LeftList.Items.Add(s);
                }
            }

            if (rightSheets != null)
            {
                foreach (string s in rightSheets)
                {
                    RightList.Items.Add(s);
                }
            }

            if (LeftList.Items.Count > 0)
            {
                LeftList.SelectedIndex = 0;
            }

            if (RightList.Items.Count > 0)
            {
                RightList.SelectedIndex = 0;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string left = LeftList.SelectedItem as string;
            string right = RightList.SelectedItem as string;
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                MessageBox.Show(
                    "左右のシートをそれぞれ選択してください。",
                    "DiffXL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ResultPairs = new List<SheetPair>
            {
                new SheetPair
                {
                    LeftSheet = left,
                    RightSheet = right,
                    IsManual = true
                }
            };
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
