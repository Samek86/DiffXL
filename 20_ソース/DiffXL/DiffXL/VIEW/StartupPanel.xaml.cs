using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiffXL.COMMON;
using Microsoft.Win32;

namespace DiffXL.VIEW
{
    /// <summary>
    /// 起動時の左右ファイル選択パネル（SCR-01）。
    /// </summary>
    public partial class StartupPanel : UserControl
    {
        /// <summary>
        /// 左パス。
        /// </summary>
        public string LeftPath { get; private set; }

        /// <summary>
        /// 右パス。
        /// </summary>
        public string RightPath { get; private set; }

        /// <summary>
        /// 比較開始が押された。
        /// </summary>
        public event Action<string, string> StartCompareRequested;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public StartupPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// パス表示をリセットする。
        /// </summary>
        public void Reset()
        {
            LeftPath = null;
            RightPath = null;
            LeftPathText.Text = "（未選択）";
            RightPathText.Text = "（未選択）";
            LeftPathText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            RightPathText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            UpdateStartEnabled();
        }

        private void BtnPickLeft_Click(object sender, RoutedEventArgs e)
        {
            string path = PickXlsx("左の Excel ファイルを選択");
            if (path == null)
            {
                return;
            }

            LeftPath = path;
            LeftPathText.Text = path;
            LeftPathText.Foreground = Brushes.Black;
            UpdateStartEnabled();
        }

        private void BtnPickRight_Click(object sender, RoutedEventArgs e)
        {
            string path = PickXlsx("右の Excel ファイルを選択");
            if (path == null)
            {
                return;
            }

            RightPath = path;
            RightPathText.Text = path;
            RightPathText.Foreground = Brushes.Black;
            UpdateStartEnabled();
        }

        private void BtnStartCompare_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(LeftPath) || string.IsNullOrEmpty(RightPath))
            {
                return;
            }

            StartCompareRequested?.Invoke(LeftPath, RightPath);
        }

        private void UpdateStartEnabled()
        {
            BtnStartCompare.IsEnabled =
                !string.IsNullOrEmpty(LeftPath)
                && !string.IsNullOrEmpty(RightPath)
                && File.Exists(LeftPath)
                && File.Exists(RightPath);
        }

        private static string PickXlsx(string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel ブック (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                CheckFileExists = true,
                Title = title
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
