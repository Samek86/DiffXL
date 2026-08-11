using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Dialogs
{
    /// <summary>
    /// シート手動対応ダイアログ（SCR-03）。
    /// 同名リセット・異名ペア・片側明示に対応。
    /// </summary>
    public partial class SheetMapDialog : Window
    {
        private const string NoneLabel = "（なし）";

        private readonly List<string> _leftSheets = new List<string>();
        private readonly List<string> _rightSheets = new List<string>();
        private readonly List<PairRow> _pairs = new List<PairRow>();

        /// <summary>
        /// 適用された対応ペア。同名リセット時は null。
        /// </summary>
        public List<SheetPair> ResultPairs { get; private set; }

        /// <summary>
        /// 同名自動対応へ戻す場合 true（ManualSheetPairs をクリア）。
        /// </summary>
        public bool ResetToSameName { get; private set; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="leftSheets">左シート名</param>
        /// <param name="rightSheets">右シート名</param>
        /// <param name="existingPairs">既存の手動／結果ペア（任意）</param>
        public SheetMapDialog(
            IList<string> leftSheets,
            IList<string> rightSheets,
            IList<SheetPair> existingPairs = null)
        {
            InitializeComponent();

            if (leftSheets != null)
            {
                _leftSheets.AddRange(leftSheets.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            }

            if (rightSheets != null)
            {
                _rightSheets.AddRange(rightSheets.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            }

            LeftList.Items.Add(NoneLabel);
            foreach (string s in _leftSheets)
            {
                LeftList.Items.Add(s);
            }

            RightList.Items.Add(NoneLabel);
            foreach (string s in _rightSheets)
            {
                RightList.Items.Add(s);
            }

            if (LeftList.Items.Count > 1)
            {
                LeftList.SelectedIndex = 1;
            }
            else
            {
                LeftList.SelectedIndex = 0;
            }

            if (RightList.Items.Count > 1)
            {
                RightList.SelectedIndex = 1;
            }
            else
            {
                RightList.SelectedIndex = 0;
            }

            if (existingPairs != null && existingPairs.Count > 0)
            {
                foreach (SheetPair p in existingPairs)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    TryAddPair(p.LeftSheet, p.RightSheet, showError: false);
                }
            }
            else
            {
                // 既定: 同名候補を初期表示（適用は手動追加後 or 同名リセット）
                SeedSameNamePairs();
            }

            RefreshPairList();
            UpdateHint();
        }

        private void SeedSameNamePairs()
        {
            var rightSet = new HashSet<string>(_rightSheets, StringComparer.OrdinalIgnoreCase);
            foreach (string left in _leftSheets)
            {
                string match = _rightSheets.FirstOrDefault(r =>
                    string.Equals(r, left, StringComparison.OrdinalIgnoreCase));
                if (match != null && rightSet.Contains(match))
                {
                    TryAddPair(left, match, showError: false);
                }
            }
        }

        private void BtnAddPair_Click(object sender, RoutedEventArgs e)
        {
            string left = NormalizeSelection(LeftList.SelectedItem as string);
            string right = NormalizeSelection(RightList.SelectedItem as string);
            if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
            {
                MessageBox.Show(
                    "左右のどちらか（または両方）のシートを選択してください。",
                    "DiffXL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!TryAddPair(left, right, showError: true))
            {
                return;
            }

            RefreshPairList();
            UpdateHint();
        }

        private void BtnRemovePair_Click(object sender, RoutedEventArgs e)
        {
            var row = PairList.SelectedItem as PairRow;
            if (row == null)
            {
                return;
            }

            _pairs.Remove(row);
            RefreshPairList();
            UpdateHint();
        }

        private void BtnResetSameName_Click(object sender, RoutedEventArgs e)
        {
            ResetToSameName = true;
            ResultPairs = null;
            DialogResult = true;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 一覧が空なら、現在の選択を 1 ペアとして採用
            if (_pairs.Count == 0)
            {
                string left = NormalizeSelection(LeftList.SelectedItem as string);
                string right = NormalizeSelection(RightList.SelectedItem as string);
                if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
                {
                    MessageBox.Show(
                        "対応を 1 件以上追加するか、「同名にリセット」を使ってください。",
                        "DiffXL",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                TryAddPair(left, right, showError: true);
            }

            if (_pairs.Count == 0)
            {
                return;
            }

            ResetToSameName = false;
            ResultPairs = _pairs.Select(p => new SheetPair
            {
                LeftSheet = p.LeftSheet,
                RightSheet = p.RightSheet,
                IsManual = true
            }).ToList();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool TryAddPair(string left, string right, bool showError)
        {
            left = string.IsNullOrWhiteSpace(left) ? null : left.Trim();
            right = string.IsNullOrWhiteSpace(right) ? null : right.Trim();
            if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
            {
                return false;
            }

            // 重複（同じ左右）は拒否
            if (_pairs.Any(p =>
                string.Equals(p.LeftSheet ?? string.Empty, left ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.RightSheet ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
            {
                if (showError)
                {
                    MessageBox.Show("同じ対応が既にあります。", "DiffXL", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return false;
            }

            // 同じ左／右シートの二重使用を防ぐ
            if (!string.IsNullOrEmpty(left)
                && _pairs.Any(p => string.Equals(p.LeftSheet, left, StringComparison.OrdinalIgnoreCase)))
            {
                if (showError)
                {
                    MessageBox.Show("左シート「" + left + "」は既に対応済みです。", "DiffXL", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return false;
            }

            if (!string.IsNullOrEmpty(right)
                && _pairs.Any(p => string.Equals(p.RightSheet, right, StringComparison.OrdinalIgnoreCase)))
            {
                if (showError)
                {
                    MessageBox.Show("右シート「" + right + "」は既に対応済みです。", "DiffXL", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return false;
            }

            _pairs.Add(new PairRow(left, right));
            return true;
        }

        private void RefreshPairList()
        {
            PairList.Items.Clear();
            foreach (PairRow row in _pairs)
            {
                PairList.Items.Add(row);
            }
        }

        private void UpdateHint()
        {
            int both = _pairs.Count(p => !string.IsNullOrEmpty(p.LeftSheet) && !string.IsNullOrEmpty(p.RightSheet));
            int leftOnly = _pairs.Count(p => !string.IsNullOrEmpty(p.LeftSheet) && string.IsNullOrEmpty(p.RightSheet));
            int rightOnly = _pairs.Count(p => string.IsNullOrEmpty(p.LeftSheet) && !string.IsNullOrEmpty(p.RightSheet));
            HintText.Text = string.Format(
                "ペア {0} / 左のみ {1} / 右のみ {2}",
                both,
                leftOnly,
                rightOnly);
        }

        private static string NormalizeSelection(string selected)
        {
            if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, NoneLabel, StringComparison.Ordinal))
            {
                return null;
            }

            return selected.Trim();
        }

        private sealed class PairRow
        {
            public PairRow(string left, string right)
            {
                LeftSheet = left;
                RightSheet = right;
                if (string.IsNullOrEmpty(LeftSheet))
                {
                    Display = "（左なし）↔ " + RightSheet + "  [片側]";
                }
                else if (string.IsNullOrEmpty(RightSheet))
                {
                    Display = LeftSheet + " ↔（右なし）  [片側]";
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
        }
    }
}
