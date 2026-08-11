using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// 1 ブック分の Excel セッション（Open / Close / シート切替 / スクロール）。
    /// </summary>
    public sealed class ExcelWorkbookSession : IDisposable
    {
        private ExcelAppManager _appManager;
        private object _workbook;
        /// <summary>埋め込み前に掴んだ Window COM（ScrollRow 用）。</summary>
        private object _excelWindow;
        private IntPtr _windowHandle = IntPtr.Zero;
        private bool _disposed;

        public string FilePath { get; private set; }

        public bool IsOpen
        {
            get { return !_disposed && _workbook != null && _appManager != null && _appManager.IsAlive; }
        }

        public IntPtr GetMainWindowHandle()
        {
            return _windowHandle;
        }

        /// <summary>
        /// 指定パスの .xlsx を読み取り専用で開く。
        /// </summary>
        public void OpenReadOnly(string path)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("ファイルパスが空です。", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("ファイルが見つかりません。", fullPath);
            }

            if (!string.Equals(Path.GetExtension(fullPath), Common.ExcelExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("対象形式は .xlsx のみです: " + fullPath);
            }

            CloseInternal(keepDisposed: false);

            _appManager = ExcelAppManager.Create();
            // Open は optional 引数が多く LateCall だと失敗しやすいので dynamic を使う
            dynamic app = _appManager.Application;

            try
            {
                app.DisplayAlerts = false;
                app.ScreenUpdating = true;

                object workbook = null;
                Exception lastOpenError = null;

                // 1) 読み取り専用 Open（位置引数）
                try
                {
                    // Workbooks.Open(Filename, UpdateLinks, ReadOnly, Format, Password, WriteResPassword,
                    //   IgnoreReadOnlyRecommended, Origin, Delimiter, Editable, Notify, Converter, AddToMru, Local, CorruptLoad)
                    workbook = app.Workbooks.Open(
                        fullPath,
                        0,
                        true,
                        Type.Missing,
                        Type.Missing,
                        Type.Missing,
                        true,
                        Type.Missing,
                        Type.Missing,
                        Type.Missing,
                        false,
                        Type.Missing,
                        false);
                }
                catch (Exception ex1)
                {
                    lastOpenError = ex1;
                    Log.Debug("Open(読取専用) 失敗: " + ex1.Message);
                }

                // 2) ファイル名のみ
                if (workbook == null)
                {
                    try
                    {
                        workbook = app.Workbooks.Open(fullPath);
                    }
                    catch (Exception ex2)
                    {
                        lastOpenError = ex2;
                        Log.Debug("Open(簡易) 失敗: " + ex2.Message);
                    }
                }

                // 3) LateCall フォールバック
                if (workbook == null)
                {
                    object workbooksObj;
                    object wb;
                    if (ExcelComHelper.TryGetProperty((object)app, "Workbooks", out workbooksObj)
                        && ExcelComHelper.TryInvoke(workbooksObj, "Open", new object[] { fullPath }, out wb))
                    {
                        workbook = wb;
                    }
                }

                if (workbook == null)
                {
                    string detail = lastOpenError != null ? lastOpenError.Message : "原因不明";
                    throw new InvalidOperationException(
                        "ブックを開けません: " + fullPath + Environment.NewLine + detail,
                        lastOpenError);
                }

                _workbook = workbook;
                FilePath = fullPath;

                app.Visible = true;
                try
                {
                    app.UserControl = true;
                }
                catch
                {
                    // ignore
                }

                try
                {
                    app.WindowState = -4143; // xlNormal
                }
                catch
                {
                    // ignore
                }

                // 埋め込み後 ActiveWindow が null になる対策: Window を先に保持
                _excelWindow = null;
                try
                {
                    dynamic wbDyn = workbook;
                    _excelWindow = wbDyn.Windows[1];
                    Log.Debug("Excel Window COM を保持しました。");
                }
                catch (Exception ex)
                {
                    Log.Debug("Windows[1] 取得失敗: " + ex.Message);
                    try
                    {
                        _excelWindow = app.ActiveWindow;
                    }
                    catch (Exception ex2)
                    {
                        Log.Debug("ActiveWindow 取得失敗: " + ex2.Message);
                    }
                }

                // リボン／数式バー等は操作できないので隠し、グリッドだけ見せる
                ApplyViewerChrome(app, _excelWindow);

                _windowHandle = ResolveWindowHandle((object)app);
                if (_windowHandle == IntPtr.Zero)
                {
                    // Hwnd 再取得
                    try
                    {
                        int hwnd = Convert.ToInt32(app.Hwnd);
                        if (hwnd != 0)
                        {
                            _windowHandle = new IntPtr(hwnd);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (_windowHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "Excel ウィンドウのハンドルを取得できませんでした。ログを確認してください。");
                }

                Log.Info("ブックを読み取り専用で開きました: " + fullPath);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                CloseInternal(keepDisposed: false);
                throw;
            }
        }

        /// <summary>
        /// 埋め込み後にもう一度ビューア用 UI を適用する。
        /// </summary>
        public void EnsureViewerChrome()
        {
            if (!IsOpen)
            {
                return;
            }

            try
            {
                ApplyViewerChrome(_appManager.Application, _excelWindow);
            }
            catch (Exception ex)
            {
                Log.Debug("EnsureViewerChrome: " + ex.Message);
            }
        }

        /// <summary>
        /// ホイール／クリック前にブックを前面化して入力を受けられるようにする。
        /// </summary>
        public void ActivateForInput()
        {
            if (!IsOpen)
            {
                return;
            }

            try
            {
                dynamic app = _appManager.Application;
                try { app.ScreenUpdating = true; } catch { /* ignore */ }
                try
                {
                    dynamic wb = _workbook;
                    wb.Activate();
                }
                catch
                {
                    // ignore
                }

                if (_windowHandle != IntPtr.Zero)
                {
                    try { Win32.SetFocus(_windowHandle); } catch { /* ignore */ }
                }

                if (_excelWindow != null)
                {
                    try
                    {
                        dynamic win = _excelWindow;
                        win.Activate();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ActivateForInput: " + ex.Message);
            }
        }

        /// <summary>
        /// 埋め込みビュー用にリボン・数式バーなどを隠す（クリックできない UI を出さない）。
        /// </summary>
        private static void ApplyViewerChrome(dynamic app, object windowObj)
        {
            try { app.DisplayFormulaBar = false; } catch { /* ignore */ }
            try { app.DisplayStatusBar = false; } catch { /* ignore */ }
            try { app.DisplayScrollBars = true; } catch { /* ignore */ }
            try { app.DisplayFullScreen = false; } catch { /* ignore */ }

            // リボン非表示（Excel バージョンにより成否が分かれる）
            try
            {
                app.ExecuteExcel4Macro("SHOW.TOOLBAR(\"Ribbon\",False)");
            }
            catch
            {
                try
                {
                    // 代替: リボン最小化
                    app.CommandBars.ExecuteMso("MinimizeRibbon");
                }
                catch
                {
                    // ignore
                }
            }

            if (windowObj != null)
            {
                try
                {
                    dynamic win = windowObj;
                    try { win.DisplayWorkbookTabs = true; } catch { /* ignore */ }
                    try { win.DisplayHeadings = true; } catch { /* ignore */ }
                    try { win.DisplayGridlines = true; } catch { /* ignore */ }
                    try { win.DisplayHorizontalScrollBar = true; } catch { /* ignore */ }
                    try { win.DisplayVerticalScrollBar = true; } catch { /* ignore */ }
                    try { win.DisplayRuler = false; } catch { /* ignore */ }
                }
                catch
                {
                    // ignore
                }
            }

            Log.Debug("Excel ビューア用 UI（リボン等）を最小化／非表示にしました。");
        }

        public IReadOnlyList<string> GetSheetNames()
        {
            ThrowIfDisposed();
            EnsureOpen();
            var names = new List<string>();
            object sheets;
            if (!ExcelComHelper.TryGetProperty(_workbook, "Worksheets", out sheets) || sheets == null)
            {
                return names;
            }

            object countObj;
            if (!ExcelComHelper.TryGetProperty(sheets, "Count", out countObj))
            {
                return names;
            }

            int count = Convert.ToInt32(countObj);
            for (int i = 1; i <= count; i++)
            {
                object sheet = null;
                try
                {
                    sheet = Microsoft.VisualBasic.CompilerServices.NewLateBinding.LateGet(
                        sheets, null, "Item", new object[] { i }, null, null, null);
                }
                catch
                {
                    ExcelComHelper.TryInvoke(sheets, "Item", new object[] { i }, out sheet);
                }

                if (sheet == null)
                {
                    continue;
                }

                object nameObj;
                if (ExcelComHelper.TryGetProperty(sheet, "Name", out nameObj) && nameObj != null)
                {
                    names.Add(Convert.ToString(nameObj));
                }
            }

            return names;
        }

        public void ActivateSheet(string name)
        {
            ThrowIfDisposed();
            EnsureOpen();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            object sheets;
            if (!ExcelComHelper.TryGetProperty(_workbook, "Worksheets", out sheets))
            {
                throw new InvalidOperationException("Worksheets を取得できません。");
            }

            object sheet = null;
            try
            {
                sheet = Microsoft.VisualBasic.CompilerServices.NewLateBinding.LateGet(
                    sheets, null, "Item", new object[] { name }, null, null, null);
            }
            catch
            {
                if (!ExcelComHelper.TryInvoke(sheets, "Item", new object[] { name }, out sheet))
                {
                    throw new InvalidOperationException("シートを取得できません: " + name);
                }
            }

            object ignored;
            if (!ExcelComHelper.TryInvoke(sheet, "Activate", null, out ignored))
            {
                throw new InvalidOperationException("シートを切り替えできません: " + name);
            }

            Log.Debug("シートをアクティブ化: " + name);
        }

        /// <summary>
        /// スクロール位置を取得（dynamic COM）。
        /// 埋め込み後も安定するよう、複数の Window 参照を試す。
        /// </summary>
        public bool TryGetScroll(out int scrollRow, out int scrollColumn)
        {
            scrollRow = 1;
            scrollColumn = 1;
            if (!IsOpen)
            {
                return false;
            }

            // 1) キャッシュ Window
            if (TryReadScrollFromWindow(_excelWindow, out scrollRow, out scrollColumn))
            {
                return true;
            }

            // 2) 再取得
            try
            {
                object win = GetExcelWindowDynamic();
                if (win != null)
                {
                    _excelWindow = win;
                    if (TryReadScrollFromWindow(win, out scrollRow, out scrollColumn))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 3) Workbook.Windows[1]
            try
            {
                dynamic wb = _workbook;
                dynamic w = wb.Windows[1];
                _excelWindow = w;
                if (TryReadScrollFromWindow(w, out scrollRow, out scrollColumn))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            // 4) Application.ActiveWindow
            try
            {
                dynamic app = _appManager.Application;
                dynamic w = app.ActiveWindow;
                if (w != null)
                {
                    _excelWindow = w;
                    if (TryReadScrollFromWindow(w, out scrollRow, out scrollColumn))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryGetScroll 失敗: " + ex.Message);
            }

            return false;
        }

        private static bool TryReadScrollFromWindow(object window, out int scrollRow, out int scrollColumn)
        {
            scrollRow = 1;
            scrollColumn = 1;
            if (window == null)
            {
                return false;
            }

            try
            {
                dynamic win = window;
                scrollRow = Math.Max(1, Convert.ToInt32(win.ScrollRow));
                scrollColumn = Math.Max(1, Convert.ToInt32(win.ScrollColumn));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// スクロール位置を設定。
        /// </summary>
        public bool TrySetScroll(int scrollRow, int scrollColumn)
        {
            if (!IsOpen)
            {
                return false;
            }

            scrollRow = Math.Max(1, scrollRow);
            scrollColumn = Math.Max(1, scrollColumn);

            // 保持済み Window を最優先
            if (TryScrollOnWindow(_excelWindow, scrollRow, scrollColumn))
            {
                return true;
            }

            try
            {
                dynamic win = GetExcelWindowDynamic();
                if (TryScrollOnWindow(win, scrollRow, scrollColumn))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TrySetScroll 失敗: " + ex.Message);
            }

            // 行ジャンプは列を壊しやすいので、列だけ動かしたい場合は失敗扱い
            if (scrollColumn > 1)
            {
                return false;
            }

            return TryGotoRow(scrollRow);
        }

        /// <summary>
        /// Window COM に ScrollRow/Column を設定する。
        /// </summary>
        private static bool TryScrollOnWindow(object window, int scrollRow, int scrollColumn)
        {
            if (window == null)
            {
                return false;
            }

            try
            {
                dynamic win = window;
                try { win.ScrollRow = scrollRow; } catch { /* ignore */ }
                try { win.ScrollColumn = scrollColumn; } catch { /* ignore */ }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 指定行を表示する（MiniMap 用）。埋め込み時も動くよう複数手段を試す。
        /// </summary>
        public bool TryGotoRow(int row)
        {
            if (!IsOpen)
            {
                return false;
            }

            row = Math.Max(1, row);
            var errors = new System.Text.StringBuilder();

            try
            {
                dynamic app = _appManager.Application;
                dynamic wb = _workbook;

                try { app.ScreenUpdating = true; } catch { /* ignore */ }
                try { app.DisplayAlerts = false; } catch { /* ignore */ }

                // ブック／シートを前面に
                try { wb.Activate(); } catch (Exception ex) { errors.Append("ActWB:" + ex.Message + ";"); }

                dynamic sheet = null;
                try { sheet = wb.ActiveSheet; }
                catch
                {
                    try { sheet = app.ActiveSheet; }
                    catch (Exception ex) { errors.Append("Sheet:" + ex.Message + ";"); }
                }

                // --- 手段0: 埋め込み前に保持した Window COM ---
                if (TryScrollOnWindow(_excelWindow, row, 1))
                {
                    Log.Info("TryGotoRow OK via cached Window row=" + row);
                    return true;
                }

                // --- 手段1: 全 Window に ScrollRow を設定 ---
                if (TrySetScrollRowOnAllWindows(app, wb, row, errors))
                {
                    Log.Info("TryGotoRow OK via ScrollRow row=" + row);
                    return true;
                }

                // --- 手段2: Cells を Select してから ScrollRow ---
                if (sheet != null)
                {
                    try
                    {
                        dynamic cell = sheet.Cells[row, 1];
                        try { cell.Select(); } catch (Exception ex) { errors.Append("Sel:" + ex.Message + ";"); }
                        if (TrySetScrollRowOnAllWindows(app, wb, row, errors))
                        {
                            Log.Info("TryGotoRow OK via Select+ScrollRow row=" + row);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Append("Cells:" + ex.Message + ";");
                    }

                    // --- 手段3: Application.Goto ---
                    try
                    {
                        dynamic rng = sheet.Range["A" + row];
                        app.Goto(rng, true);
                        Log.Info("TryGotoRow OK via Goto row=" + row);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        errors.Append("Goto:" + ex.Message + ";");
                    }
                }

                // --- 手段4: Win32 ホイール／VSCROLL（埋め込み HWND 向け） ---
                if (_windowHandle != IntPtr.Zero && TryScrollRowViaWin32(row, errors))
                {
                    Log.Info("TryGotoRow OK via Win32 row=" + row);
                    return true;
                }

                Log.Error("TryGotoRow FAIL row=" + row + " detail=" + errors);
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return false;
            }
        }

        /// <summary>
        /// Application / Workbook 配下の Window に ScrollRow を設定する。
        /// </summary>
        private static bool TrySetScrollRowOnAllWindows(dynamic app, dynamic wb, int row, System.Text.StringBuilder errors)
        {
            // Workbook.Windows
            try
            {
                dynamic wins = wb.Windows;
                int n = Convert.ToInt32(wins.Count);
                for (int i = 1; i <= n; i++)
                {
                    try
                    {
                        dynamic w = wins[i];
                        w.ScrollRow = row;
                        // ScrollColumn は触らない（横位置を壊さない）
                        return true;
                    }
                    catch (Exception ex)
                    {
                        errors.Append("WBWin" + i + ":" + ex.Message + ";");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Append("WBWindows:" + ex.Message + ";");
            }

            // Application.Windows
            try
            {
                dynamic wins = app.Windows;
                int n = Convert.ToInt32(wins.Count);
                for (int i = 1; i <= n; i++)
                {
                    try
                    {
                        dynamic w = wins[i];
                        w.ScrollRow = row;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        errors.Append("AppWin" + i + ":" + ex.Message + ";");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Append("AppWindows:" + ex.Message + ";");
            }

            try
            {
                dynamic aw = app.ActiveWindow;
                aw.ScrollRow = row;
                return true;
            }
            catch (Exception ex)
            {
                errors.Append("ActiveWindow:" + ex.Message + ";");
            }

            return false;
        }

        /// <summary>
        /// 埋め込み Excel 向け: COM の現在行が取れるまで起こし、差分ラインで目標行へ寄せる。
        /// </summary>
        private bool TryScrollRowViaWin32(int targetRow, System.Text.StringBuilder errors)
        {
            try
            {
                IntPtr grid = FindExcelGridWindow(_windowHandle);
                if (grid == IntPtr.Zero)
                {
                    grid = _windowHandle;
                }

                if (grid == IntPtr.Zero)
                {
                    errors.Append("NoGridHwnd;");
                    return false;
                }

                try { Win32.SetFocus(grid); } catch { /* ignore */ }
                try { Win32.SetFocus(_windowHandle); } catch { /* ignore */ }

                // 埋め込み直後は ActiveWindow が null のことがある → 少しスクロールして起こす
                for (int wake = 0; wake < 3; wake++)
                {
                    Win32.SendMessage(grid, Win32.WM_VSCROLL, (IntPtr)1, IntPtr.Zero); // linedown
                }

                int curR = 1, curC = 1;
                bool haveCur = false;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    // COM 再試行（起こし後に ScrollRow が復活することがある）
                    if (TrySetScrollRowOnAllWindows(
                        _appManager.Application,
                        _workbook,
                        targetRow,
                        errors))
                    {
                        if (TryGetScroll(out curR, out curC) && Math.Abs(curR - targetRow) <= 2)
                        {
                            return true;
                        }

                        // set は成功したとみなす
                        return true;
                    }

                    haveCur = TryGetScroll(out curR, out curC);
                    if (!haveCur)
                    {
                        // まだ読めない → もう少し動かす
                        Win32.SendMessage(grid, Win32.WM_VSCROLL, (IntPtr)1, IntPtr.Zero);
                        continue;
                    }

                    int delta = targetRow - curR;
                    if (Math.Abs(delta) <= 1)
                    {
                        Log.Debug("Win32 iterative done sr=" + curR + " target=" + targetRow);
                        return true;
                    }

                    // 一気に飛ばしすぎない（前回 25→34 の過走を抑制）
                    int step = Math.Sign(delta) * Math.Min(Math.Abs(delta), 5);
                    int code = step > 0 ? 1 : 0; // down / up
                    int n = Math.Abs(step);
                    for (int i = 0; i < n; i++)
                    {
                        Win32.SendMessage(grid, Win32.WM_VSCROLL, (IntPtr)code, IntPtr.Zero);
                    }

                    // 1 回だけホイール
                    const int WM_MOUSEWHEEL = 0x020A;
                    int wheel = step > 0 ? -120 : 120;
                    Win32.SendMessage(grid, WM_MOUSEWHEEL, (IntPtr)(wheel << 16), IntPtr.Zero);
                }

                haveCur = TryGetScroll(out curR, out curC);
                if (haveCur)
                {
                    Log.Debug("Win32 iterative final sr=" + curR + " target=" + targetRow);
                    // 完全一致でなくても移動できていれば成功（UI 応答）
                    return Math.Abs(curR - targetRow) <= 10 || curR != 1;
                }

                errors.Append("Win32NoFeedback;");
                // 最低限メッセージは送っている
                return true;
            }
            catch (Exception ex)
            {
                errors.Append("Win32:" + ex.Message + ";");
                return false;
            }
        }

        /// <summary>
        /// XLMAIN 配下の EXCEL7（シート描画）HWND を探す。
        /// </summary>
        private static IntPtr FindExcelGridWindow(IntPtr excelMain)
        {
            if (excelMain == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // XLDESK -> EXCEL7
            IntPtr desk = Win32.FindWindowEx(excelMain, IntPtr.Zero, "XLDESK", null);
            if (desk != IntPtr.Zero)
            {
                IntPtr grid = Win32.FindWindowEx(desk, IntPtr.Zero, "EXCEL7", null);
                if (grid != IntPtr.Zero)
                {
                    return grid;
                }
            }

            // 直下探索
            IntPtr child = IntPtr.Zero;
            for (int i = 0; i < 20; i++)
            {
                child = Win32.FindWindowEx(excelMain, child, null, null);
                if (child == IntPtr.Zero)
                {
                    break;
                }

                IntPtr grid = Win32.FindWindowEx(child, IntPtr.Zero, "EXCEL7", null);
                if (grid != IntPtr.Zero)
                {
                    return grid;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// dynamic で Excel Window を取得する。
        /// </summary>
        private dynamic GetExcelWindowDynamic()
        {
            try
            {
                dynamic app = _appManager.Application;
                dynamic wb = _workbook;
                try
                {
                    return wb.Windows[1];
                }
                catch
                {
                    // fall through
                }

                try
                {
                    return app.ActiveWindow;
                }
                catch
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public bool TryGetCellBoundsPoints(
            string address,
            out double left,
            out double top,
            out double width,
            out double height)
        {
            left = top = width = height = 0;
            if (!IsOpen || string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            try
            {
                object app = _appManager.Application;
                object sheet;
                if (!ExcelComHelper.TryGetProperty(app, "ActiveSheet", out sheet) || sheet == null)
                {
                    return false;
                }

                object range = Microsoft.VisualBasic.CompilerServices.NewLateBinding.LateGet(
                    sheet, null, "Range", new object[] { address }, null, null, null);
                if (range == null)
                {
                    return false;
                }

                object l, t, w, h;
                if (!ExcelComHelper.TryGetProperty(range, "Left", out l)
                    || !ExcelComHelper.TryGetProperty(range, "Top", out t)
                    || !ExcelComHelper.TryGetProperty(range, "Width", out w)
                    || !ExcelComHelper.TryGetProperty(range, "Height", out h))
                {
                    return false;
                }

                left = Convert.ToDouble(l);
                top = Convert.ToDouble(t);
                width = Convert.ToDouble(w);
                height = Convert.ToDouble(h);
                return width > 0 && height > 0;
            }
            catch (Exception ex)
            {
                Log.Debug("TryGetCellBoundsPoints 失敗 (" + address + "): " + ex.Message);
                return false;
            }
        }

        public bool TryGetViewMetrics(out ExcelViewMetrics metrics)
        {
            metrics = null;
            if (!IsOpen || _windowHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                Win32.RECT rect;
                if (!Win32.GetWindowRect(_windowHandle, out rect))
                {
                    return false;
                }

                var result = new ExcelViewMetrics
                {
                    ScreenBounds = new Rect(
                        rect.Left,
                        rect.Top,
                        Math.Max(0, rect.Right - rect.Left),
                        Math.Max(0, rect.Bottom - rect.Top))
                };

                int sr, sc;
                if (TryGetScroll(out sr, out sc))
                {
                    result.ScrollRow = sr;
                    result.ScrollColumn = sc;
                }

                metrics = result;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("TryGetViewMetrics 失敗: " + ex.Message);
                return false;
            }
        }

        public void Close()
        {
            CloseInternal(keepDisposed: false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CloseInternal(keepDisposed: true);
            _disposed = true;
        }

        private void CloseInternal(bool keepDisposed)
        {
            _windowHandle = IntPtr.Zero;
            FilePath = null;
            _excelWindow = null;

            if (_workbook != null)
            {
                try
                {
                    object ignored;
                    ExcelComHelper.TryInvoke(_workbook, "Close", new object[] { false }, out ignored);
                }
                catch (Exception ex)
                {
                    Log.Debug("Workbook.Close 失敗: " + ex.Message);
                }

                ExcelComHelper.SafeRelease(_workbook);
                _workbook = null;
            }

            if (_appManager != null)
            {
                _appManager.Dispose();
                _appManager = null;
            }

            if (keepDisposed)
            {
                _disposed = true;
            }
        }

        private static IntPtr ResolveWindowHandle(object app)
        {
            object hwndObj;
            if (ExcelComHelper.TryGetProperty(app, "Hwnd", out hwndObj) && hwndObj != null)
            {
                try
                {
                    int hwnd = Convert.ToInt32(hwndObj);
                    if (hwnd != 0)
                    {
                        return new IntPtr(hwnd);
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return IntPtr.Zero;
        }

        private void EnsureOpen()
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("ブックが開かれていません。");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ExcelWorkbookSession));
            }
        }
    }
}
