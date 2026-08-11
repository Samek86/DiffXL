using System;
using System.Windows;
using DiffXL.COMMON;
using DiffXL.LOGIC.Excel;

namespace DiffXL
{
    /// <summary>
    /// アプリケーション入口。起動時に AppData・ログ・設定を初期化する。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// ライブ自動テスト設定（コマンドライン）。
        /// </summary>
        public static AutoLiveTestOptions AutoTest { get; private set; } = new AutoLiveTestOptions();

        /// <summary>
        /// アプリ起動時の初期化処理。
        /// </summary>
        /// <param name="sender">イベント送信元</param>
        /// <param name="e">起動引数</param>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            AppPaths.EnsureDirectories();
            try
            {
                int purged = AppPaths.PurgeCompareCache(keepNewest: 3, maxAge: TimeSpan.FromHours(2), maxTotalBytes: 512L * 1024 * 1024);
                if (purged > 0)
                {
                    Log.Info("起動時キャッシュ整理: 削除 " + purged + " 件");
                }
            }
            catch
            {
                // 起動阻害しない
            }
            NativeBootstrap.EnsureNativeBinaries();
            Log.Info("DiffXL 起動");
            AppSettings.Load();
            Log.Info("設定を読み込み: " + AppPaths.SettingsFile);

            AutoTest = AutoLiveTestOptions.Parse(e.Args);
            if (AutoTest.Enabled)
            {
                Log.Info("AutoLiveTest 有効 left=" + AutoTest.LeftPath + " right=" + AutoTest.RightPath);
            }

            if (!ExcelAvailability.IsExcelInstalled())
            {
                Log.Error(ExcelAvailability.GetDiagnosticMessage());
            }
            else
            {
                Log.Info(ExcelAvailability.GetDiagnosticMessage());
            }

            new MainWindow().Show();
        }

        /// <summary>
        /// UI スレッドの未処理例外を捕捉し、ログへ記録する。
        /// </summary>
        /// <param name="sender">イベント送信元</param>
        /// <param name="e">例外情報</param>
        private void Application_DispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Exception(e.Exception);
            MessageBox.Show(
                "予期しないエラーが発生しました。\n詳細はログを確認してください。\n\n" + e.Exception.Message,
                Common.AppDisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
