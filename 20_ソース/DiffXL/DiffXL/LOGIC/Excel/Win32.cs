using System;
using System.Runtime.InteropServices;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// Excel ウィンドウ埋め込み用 Win32 API。
    /// </summary>
    internal static class Win32
    {
        /// <summary>ウィンドウスタイル取得用。</summary>
        public const int GWL_STYLE = -16;

        /// <summary>拡張スタイル。</summary>
        public const int GWL_EXSTYLE = -20;

        /// <summary>子ウィンドウスタイル。</summary>
        public const int WS_CHILD = 0x40000000;

        /// <summary>表示スタイル。</summary>
        public const int WS_VISIBLE = 0x10000000;

        /// <summary>クリップ。</summary>
        public const int WS_CLIPSIBLINGS = 0x04000000;

        /// <summary>子クリップ。</summary>
        public const int WS_CLIPCHILDREN = 0x02000000;

        /// <summary>枠。</summary>
        public const int WS_BORDER = 0x00800000;

        /// <summary>キャプション。</summary>
        public const int WS_CAPTION = 0x00C00000;

        /// <summary>サイズ変更枠。</summary>
        public const int WS_THICKFRAME = 0x00040000;

        /// <summary>ポップアップ。</summary>
        public const int WS_POPUP = unchecked((int)0x80000000);

        /// <summary>システムメニュー。</summary>
        public const int WS_SYSMENU = 0x00080000;

        /// <summary>最小化ボタン。</summary>
        public const int WS_MINIMIZEBOX = 0x00020000;

        /// <summary>最大化ボタン。</summary>
        public const int WS_MAXIMIZEBOX = 0x00010000;

        public const int WS_EX_DLGMODALFRAME = 0x00000001;
        public const int WS_EX_CLIENTEDGE = 0x00000200;
        public const int WS_EX_STATICEDGE = 0x00020000;
        public const int WS_EX_WINDOWEDGE = 0x00000100;

        /// <summary>
        /// 子ウィンドウの親を設定する。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        /// <summary>
        /// ウィンドウ位置とサイズを変更する。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        /// <summary>
        /// ウィンドウスタイルを取得する。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// ウィンドウスタイルを設定する。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// ウィンドウを表示・非表示にする。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// ウィンドウを破棄する。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        /// <summary>
        /// ウィンドウのスクリーン座標矩形を取得する。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        public const int SB_VERT = 1;
        public const int SIF_RANGE = 0x0001;
        public const int SIF_PAGE = 0x0002;
        public const int SIF_POS = 0x0004;
        public const int SIF_TRACKPOS = 0x0010;
        public const int SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS;
        public const int WM_VSCROLL = 0x0115;
        public const int WM_HSCROLL = 0x0114;
        public const int SB_LINELEFT = 0;
        public const int SB_LINERIGHT = 1;
        public const int SB_LINEUP = 0;
        public const int SB_LINEDOWN = 1;
        public const int SB_THUMBPOSITION = 4;
        public const int SB_THUMBTRACK = 5;

        [StructLayout(LayoutKind.Sequential)]
        public struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi, bool fRedraw);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        /// <summary>
        /// 子ホスト用ウィンドウを作成する。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        /// <summary>
        /// モジュールハンドルを取得する。
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        /// <summary>通常表示。</summary>
        public const int SW_SHOW = 5;

        /// <summary>最大化解除して表示。</summary>
        public const int SW_RESTORE = 9;

        /// <summary>非表示。</summary>
        public const int SW_HIDE = 0;

        /// <summary>最大化スタイル。</summary>
        public const int WS_MAXIMIZE = 0x01000000;

        /// <summary>最小化スタイル。</summary>
        public const int WS_MINIMIZE = 0x20000000;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int MK_LBUTTON = 0x0001;
        public const int MK_MBUTTON = 0x0010;
        public const int MK_RBUTTON = 0x0002;

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public const int WH_MOUSE_LL = 14;
        public const int HC_ACTION = 0;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// Win32 RECT。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            /// <summary>左。</summary>
            public int Left;

            /// <summary>上。</summary>
            public int Top;

            /// <summary>右。</summary>
            public int Right;

            /// <summary>下。</summary>
            public int Bottom;
        }
    }
}
