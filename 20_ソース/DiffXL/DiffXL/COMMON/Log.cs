using System;
using System.IO;
using System.Text;

namespace DiffXL.COMMON
{
    /// <summary>
    /// ファイルへ 1 日 1 ログで出力する。
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// ファイル書き込み用の排他ロック。
        /// </summary>
        private static readonly object Sync = new object();

        /// <summary>
        /// 情報レベルのログを出力する。
        /// </summary>
        /// <param name="message">メッセージ</param>
        public static void Info(string message)
        {
            Write("INFO", message);
        }

        /// <summary>
        /// デバッグレベルのログを出力する。
        /// </summary>
        /// <param name="message">メッセージ</param>
        public static void Debug(string message)
        {
            Write("DEBUG", message);
        }

        /// <summary>
        /// エラーレベルのログを出力する。
        /// </summary>
        /// <param name="message">メッセージ</param>
        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        /// <summary>
        /// 例外の内容をエラーとして出力する。
        /// </summary>
        /// <param name="ex">例外</param>
        public static void Exception(Exception ex)
        {
            if (ex == null)
            {
                Write("ERROR", "(null exception)");
                return;
            }

            Write("ERROR", ex.ToString());
        }

        /// <summary>
        /// 指定レベルでログ行をファイルへ追記する。
        /// </summary>
        /// <param name="level">ログレベル文字列</param>
        /// <param name="message">メッセージ</param>
        private static void Write(string level, string message)
        {
            try
            {
                AppPaths.EnsureDirectories();
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    + " [" + level + "] "
                    + (message ?? string.Empty)
                    + Environment.NewLine;
                lock (Sync)
                {
                    File.AppendAllText(AppPaths.TodayLogFile, line, Encoding.UTF8);
                }
            }
            catch
            {
                // ログ失敗でアプリを落とさない
            }
        }
    }
}
