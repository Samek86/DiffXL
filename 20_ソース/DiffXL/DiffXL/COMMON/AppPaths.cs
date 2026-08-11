using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiffXL.COMMON
{
    /// <summary>
    /// DiffXL が利用する AppData 配下のパスを解決する。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>
        /// アプリ名（Roaming 配下フォルダ名）。
        /// </summary>
        public const string AppFolderName = "DiffXL";

        /// <summary>
        /// %AppData%\Roaming\DiffXL
        /// </summary>
        public static string Root
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AppFolderName);
            }
        }

        /// <summary>
        /// 設定ファイル（settings.yaml）のフルパス。
        /// </summary>
        public static string SettingsFile
        {
            get { return Path.Combine(Root, "settings.yaml"); }
        }

        /// <summary>
        /// ログ出力ディレクトリ。
        /// </summary>
        public static string LogsDir
        {
            get { return Path.Combine(Root, "logs"); }
        }

        /// <summary>
        /// キャッシュディレクトリ。
        /// </summary>
        public static string CacheDir
        {
            get { return Path.Combine(Root, "cache"); }
        }

        /// <summary>
        /// ネイティブ DLL 展開ディレクトリ。
        /// </summary>
        public static string NativeDir
        {
            get { return Path.Combine(Root, "native"); }
        }

        /// <summary>
        /// 当日分のログファイルパスを返す。
        /// </summary>
        public static string TodayLogFile
        {
            get
            {
                return Path.Combine(LogsDir, "DiffXL_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
        }

        /// <summary>
        /// 必要なディレクトリをすべて作成する。
        /// </summary>
        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(CacheDir);
            Directory.CreateDirectory(NativeDir);
        }

        /// <summary>
        /// 比較キャッシュを整理する。新しい順に keepNewest 個を残し、
        /// それ以外で maxAge を超えたもの、または総容量が maxTotalBytes を超えた古いものを削除。
        /// </summary>
        /// <param name="keepNewest">最低限残す最新ディレクトリ数</param>
        /// <param name="maxAge">これより古いものは候補</param>
        /// <param name="maxTotalBytes">キャッシュ合計の目安上限（0 で無制限）</param>
        /// <returns>削除したディレクトリ数</returns>
        public static int PurgeCompareCache(int keepNewest = 3, TimeSpan? maxAge = null, long maxTotalBytes = 512L * 1024 * 1024)
        {
            EnsureDirectories();
            if (!Directory.Exists(CacheDir))
            {
                return 0;
            }

            TimeSpan ageLimit = maxAge ?? TimeSpan.FromMinutes(30);
            DateTime cutoff = DateTime.Now - ageLimit;
            List<DirectoryInfo> dirs;
            try
            {
                dirs = new DirectoryInfo(CacheDir)
                    .GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .ToList();
            }
            catch
            {
                return 0;
            }

            if (dirs.Count == 0)
            {
                return 0;
            }

            int removed = 0;
            // 1) keepNewest を超える古いものを age 条件で削除
            for (int i = 0; i < dirs.Count; i++)
            {
                if (i < keepNewest)
                {
                    continue;
                }

                DirectoryInfo d = dirs[i];
                if (d.LastWriteTime > cutoff && maxTotalBytes <= 0)
                {
                    continue;
                }

                if (TryDeleteDirectory(d.FullName))
                {
                    removed++;
                }
            }

            if (maxTotalBytes <= 0)
            {
                return removed;
            }

            // 2) 総容量超過なら keepNewest を守りつつ古い順に削除
            try
            {
                dirs = new DirectoryInfo(CacheDir)
                    .GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .ToList();
            }
            catch
            {
                return removed;
            }

            long total = 0;
            var sizes = new List<long>();
            foreach (DirectoryInfo d in dirs)
            {
                long sz = SafeDirSize(d);
                sizes.Add(sz);
                total += sz;
            }

            for (int i = dirs.Count - 1; i >= keepNewest && total > maxTotalBytes; i--)
            {
                if (TryDeleteDirectory(dirs[i].FullName))
                {
                    total -= sizes[i];
                    removed++;
                }
            }

            return removed;
        }

        private static long SafeDirSize(DirectoryInfo d)
        {
            try
            {
                return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("cache purge skip: " + path + " " + ex.Message);
                return false;
            }
        }
    }
}
