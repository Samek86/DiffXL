using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Dialogs
{
    /// <summary>
    /// 画像手動対応（ピン留め）ダイアログ。
    /// </summary>
    public partial class ImageLinkDialog : Window
    {
        private readonly string _leftSheet;
        private readonly string _rightSheet;
        private readonly List<ImageItemVm> _leftItems = new List<ImageItemVm>();
        private readonly List<ImageItemVm> _rightItems = new List<ImageItemVm>();
        private readonly List<ManualImagePin> _pins = new List<ManualImagePin>();

        /// <summary>
        /// 確定した手動ピン（シート全体の最終リスト）。
        /// </summary>
        public List<ManualImagePin> ResultPins { get; private set; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="leftSheet">左シート名</param>
        /// <param name="rightSheet">右シート名</param>
        /// <param name="leftImages">左画像</param>
        /// <param name="rightImages">右画像</param>
        /// <param name="existingPins">既存ピン（当該シート分）</param>
        public ImageLinkDialog(
            string leftSheet,
            string rightSheet,
            IList<EmbeddedImage> leftImages,
            IList<EmbeddedImage> rightImages,
            IList<ManualImagePin> existingPins)
        {
            InitializeComponent();
            _leftSheet = leftSheet ?? string.Empty;
            _rightSheet = rightSheet ?? string.Empty;

            if (leftImages != null)
            {
                foreach (EmbeddedImage img in leftImages.Where(i => i != null))
                {
                    _leftItems.Add(ImageItemVm.From(img, "L"));
                }
            }

            if (rightImages != null)
            {
                foreach (EmbeddedImage img in rightImages.Where(i => i != null))
                {
                    _rightItems.Add(ImageItemVm.From(img, "R"));
                }
            }

            LeftList.ItemsSource = _leftItems;
            RightList.ItemsSource = _rightItems;

            if (existingPins != null)
            {
                foreach (ManualImagePin p in existingPins)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(p.LeftImageHash) && !string.IsNullOrEmpty(p.RightImageHash))
                    {
                        _pins.Add(new ManualImagePin
                        {
                            LeftSheet = _leftSheet,
                            RightSheet = _rightSheet,
                            LeftImageHash = p.LeftImageHash,
                            RightImageHash = p.RightImageHash
                        });
                    }
                }
            }

            RefreshPinList();
        }

        private void LeftList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 選択のみ。ペアはボタンまたは右選択後の「ペアにする」
        }

        private void RightList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 左右とも選択済みなら即ペア（操作を短く）
            if (LeftList.SelectedItem != null && RightList.SelectedItem != null)
            {
                TryAddPinFromSelection(showMessage: false);
            }
        }

        private void BtnPair_Click(object sender, RoutedEventArgs e)
        {
            TryAddPinFromSelection(showMessage: true);
        }

        private void TryAddPinFromSelection(bool showMessage)
        {
            var left = LeftList.SelectedItem as ImageItemVm;
            var right = RightList.SelectedItem as ImageItemVm;
            if (left == null || right == null)
            {
                if (showMessage)
                {
                    MessageBox.Show(
                        "左右の画像をそれぞれ選択してください。",
                        "DiffXL",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            if (string.IsNullOrEmpty(left.Hash) || string.IsNullOrEmpty(right.Hash))
            {
                if (showMessage)
                {
                    MessageBox.Show(
                        "ハッシュが無い画像はピン留めできません。",
                        "DiffXL",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            // 同一ハッシュ側は上書き（1 画像は 1 ピン）
            _pins.RemoveAll(p =>
                string.Equals(p.LeftImageHash, left.Hash, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.RightImageHash, right.Hash, StringComparison.OrdinalIgnoreCase));

            _pins.Add(new ManualImagePin
            {
                LeftSheet = _leftSheet,
                RightSheet = _rightSheet,
                LeftImageHash = left.Hash,
                RightImageHash = right.Hash
            });
            RefreshPinList();
        }

        private void BtnUnpair_Click(object sender, RoutedEventArgs e)
        {
            var left = LeftList.SelectedItem as ImageItemVm;
            var right = RightList.SelectedItem as ImageItemVm;
            int removed = 0;
            if (left != null && !string.IsNullOrEmpty(left.Hash))
            {
                removed += _pins.RemoveAll(p =>
                    string.Equals(p.LeftImageHash, left.Hash, StringComparison.OrdinalIgnoreCase));
            }

            if (right != null && !string.IsNullOrEmpty(right.Hash))
            {
                removed += _pins.RemoveAll(p =>
                    string.Equals(p.RightImageHash, right.Hash, StringComparison.OrdinalIgnoreCase));
            }

            if (removed == 0 && PinList.SelectedItem is PinVm pinVm)
            {
                _pins.RemoveAll(p =>
                    string.Equals(p.LeftImageHash, pinVm.LeftHash, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.RightImageHash, pinVm.RightHash, StringComparison.OrdinalIgnoreCase));
            }

            RefreshPinList();
        }

        private void BtnClearPins_Click(object sender, RoutedEventArgs e)
        {
            _pins.Clear();
            RefreshPinList();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ResultPins = _pins
                .Select(p => new ManualImagePin
                {
                    LeftSheet = _leftSheet,
                    RightSheet = _rightSheet,
                    LeftImageHash = p.LeftImageHash,
                    RightImageHash = p.RightImageHash
                })
                .ToList();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RefreshPinList()
        {
            var items = new List<PinVm>();
            foreach (ManualImagePin p in _pins)
            {
                string leftCap = FindCaption(_leftItems, p.LeftImageHash) ?? ShortHash(p.LeftImageHash);
                string rightCap = FindCaption(_rightItems, p.RightImageHash) ?? ShortHash(p.RightImageHash);
                items.Add(new PinVm
                {
                    Display = leftCap + "  ↔  " + rightCap,
                    LeftHash = p.LeftImageHash,
                    RightHash = p.RightImageHash
                });
            }

            PinList.ItemsSource = items;
        }

        private static string FindCaption(IList<ImageItemVm> items, string hash)
        {
            if (items == null || string.IsNullOrEmpty(hash))
            {
                return null;
            }

            ImageItemVm vm = items.FirstOrDefault(i =>
                string.Equals(i.Hash, hash, StringComparison.OrdinalIgnoreCase));
            return vm != null ? vm.Caption : null;
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "?";
            }

            return hash.Length <= 8 ? hash : hash.Substring(0, 8);
        }

        /// <summary>
        /// 一覧用 VM。
        /// </summary>
        private sealed class ImageItemVm
        {
            public EmbeddedImage Source { get; set; }
            public string Hash { get; set; }
            public string Caption { get; set; }
            public string Detail { get; set; }
            public BitmapImage Thumb { get; set; }

            public static ImageItemVm From(EmbeddedImage img, string side)
            {
                int row = 0;
                if (img.Anchor != null && img.Anchor.RowStart >= 1)
                {
                    row = img.Anchor.RowStart;
                }
                else if (img.AnchorRow > 0)
                {
                    row = img.AnchorRow;
                }

                string name = !string.IsNullOrEmpty(img.FileName)
                    ? img.FileName
                    : Path.GetFileName(img.ExtractedPath ?? string.Empty);
                if (string.IsNullOrEmpty(name))
                {
                    name = "image";
                }

                string hash = img.ContentHash ?? string.Empty;
                string size = img.PixelWidth > 0 && img.PixelHeight > 0
                    ? img.PixelWidth + "×" + img.PixelHeight
                    : "";

                return new ImageItemVm
                {
                    Source = img,
                    Hash = hash,
                    Caption = side + " 行" + (row > 0 ? row.ToString() : "?") + " · " + name,
                    Detail = (size.Length > 0 ? size + "  " : "")
                        + (hash.Length > 0 ? ShortHash(hash) : "hashなし"),
                    Thumb = LoadThumb(img.ExtractedPath)
                };
            }

            private static BitmapImage LoadThumb(string path)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.DecodePixelWidth = 64;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch
                {
                    return null;
                }
            }
        }

        private sealed class PinVm
        {
            public string Display { get; set; }
            public string LeftHash { get; set; }
            public string RightHash { get; set; }

            public override string ToString()
            {
                return Display ?? string.Empty;
            }
        }
    }
}
