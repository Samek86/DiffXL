using System;
using System.Runtime.InteropServices;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// Excel.Application COM インスタンスの生成と終了を管理する。
    /// </summary>
    public sealed class ExcelAppManager : IDisposable
    {
        /// <summary>
        /// Excel Application の COM オブジェクト。
        /// </summary>
        private object _application;

        /// <summary>
        /// 破棄済みフラグ。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Excel Application（dynamic で操作する）。
        /// </summary>
        public dynamic Application
        {
            get
            {
                ThrowIfDisposed();
                return _application;
            }
        }

        /// <summary>
        /// Application が有効か。
        /// </summary>
        public bool IsAlive
        {
            get { return !_disposed && _application != null; }
        }

        /// <summary>
        /// 新しい Excel Application を起動する。
        /// </summary>
        /// <returns>管理オブジェクト</returns>
        public static ExcelAppManager Create()
        {
            Type excelType = Type.GetTypeFromProgID(ExcelAvailability.ExcelProgId);
            if (excelType == null)
            {
                throw new InvalidOperationException(ExcelAvailability.GetDiagnosticMessage());
            }

            object app;
            try
            {
                app = Activator.CreateInstance(excelType);
            }
            catch (BadImageFormatException ex)
            {
                Log.Exception(ex);
                throw new InvalidOperationException(
                    "Excel のビット数が DiffXL（x64）と一致しません。64 ビット版のデスクトップ Excel をインストールしてください。",
                    ex);
            }
            catch (COMException ex)
            {
                Log.Exception(ex);
                if (unchecked((uint)ex.ErrorCode) == 0x800700C1)
                {
                    throw new InvalidOperationException(
                        "Excel のビット数が DiffXL（x64）と一致しません。64 ビット版のデスクトップ Excel をインストールしてください。",
                        ex);
                }

                throw new InvalidOperationException("Excel を起動できません: " + ex.Message, ex);
            }

            if (app == null)
            {
                throw new InvalidOperationException("Excel の起動に失敗しました。");
            }

            var manager = new ExcelAppManager();
            manager._application = app;

            try
            {
                dynamic d = app;
                d.Visible = false;
                d.DisplayAlerts = false;
                d.ScreenUpdating = true;
                try
                {
                    // 埋め込み後にユーザーがスクロールできるよう true
                    d.UserControl = true;
                }
                catch
                {
                    // 古い Excel では未サポートの場合あり
                }

                Log.Info("Excel Application を起動しました。Version=" + SafeGetVersion(d));
            }
            catch (Exception ex)
            {
                manager.Dispose();
                throw new InvalidOperationException("Excel の初期化に失敗しました: " + ex.Message, ex);
            }

            return manager;
        }

        /// <summary>
        /// バージョン文字列を安全に取得する。
        /// </summary>
        /// <param name="app">Application</param>
        /// <returns>バージョンまたは不明</returns>
        private static string SafeGetVersion(dynamic app)
        {
            try
            {
                return Convert.ToString(app.Version);
            }
            catch
            {
                return "(unknown)";
            }
        }

        /// <summary>
        /// Application のメイン HWND を取得する。
        /// </summary>
        /// <returns>HWND。失敗時は Zero</returns>
        public IntPtr GetHwnd()
        {
            ThrowIfDisposed();
            try
            {
                int hwnd = Convert.ToInt32(Application.Hwnd);
                return new IntPtr(hwnd);
            }
            catch (Exception ex)
            {
                Log.Debug("Application.Hwnd の取得に失敗: " + ex.Message);
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Excel を終了し COM を解放する。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_application != null)
            {
                try
                {
                    dynamic d = _application;
                    d.DisplayAlerts = false;
                    d.Quit();
                }
                catch (Exception ex)
                {
                    Log.Debug("Excel.Quit 失敗: " + ex.Message);
                }

                try
                {
                    Marshal.FinalReleaseComObject(_application);
                }
                catch (Exception ex)
                {
                    Log.Debug("Excel RCW 解放失敗: " + ex.Message);
                }

                _application = null;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Log.Debug("Excel Application を破棄しました。");
        }

        /// <summary>
        /// 破棄済みなら例外を投げる。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed || _application == null)
            {
                throw new ObjectDisposedException(nameof(ExcelAppManager));
            }
        }
    }
}
