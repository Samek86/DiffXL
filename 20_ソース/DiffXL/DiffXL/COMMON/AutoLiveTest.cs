using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DiffXL.COMMON
{
    /// <summary>
    /// ライブ自動テスト用コマンドライン引数（出荷 UI 経路を駆動する）。
    /// </summary>
    public sealed class AutoLiveTestOptions
    {
        /// <summary>自動テストを実行するか。</summary>
        public bool Enabled { get; set; }

        /// <summary>左 xlsx。</summary>
        public string LeftPath { get; set; }

        /// <summary>右 xlsx。</summary>
        public string RightPath { get; set; }

        /// <summary>結果レポート出力パス。</summary>
        public string ReportPath { get; set; }

        /// <summary>終了コード用の失敗フラグをファイルに書くか。</summary>
        public bool QuitWhenDone { get; set; } = true;

        /// <summary>
        /// コマンドラインを解析する。
        /// </summary>
        public static AutoLiveTestOptions Parse(string[] args)
        {
            var o = new AutoLiveTestOptions();
            if (args == null || args.Length == 0)
            {
                return o;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--auto-live-test", StringComparison.OrdinalIgnoreCase))
                {
                    o.Enabled = true;
                }
                else if (string.Equals(a, "--left", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.LeftPath = args[++i];
                }
                else if (string.Equals(a, "--right", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.RightPath = args[++i];
                }
                else if (string.Equals(a, "--report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.ReportPath = args[++i];
                }
                else if (string.Equals(a, "--no-quit", StringComparison.OrdinalIgnoreCase))
                {
                    o.QuitWhenDone = false;
                }
            }

            return o;
        }

        /// <summary>
        /// レポート1行を追記する。
        /// </summary>
        public void WriteLine(string line)
        {
            Log.Info("[AUTO] " + line);
            if (string.IsNullOrEmpty(ReportPath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? ".");
                File.AppendAllText(ReportPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Debug("report write fail: " + ex.Message);
            }
        }
    }
}
