using System;
using System.IO;
using System.Linq;

namespace DiffXL.COMMON
{
    /// <summary>
    /// ネイティブ DLL を AppData\native へ展開し、読み込み可能にする。
    /// </summary>
    public static class NativeBootstrap
    {
        /// <summary>
        /// 初期化済みフラグ。
        /// </summary>
        private static bool _initialized;

        /// <summary>
        /// ロック。
        /// </summary>
        private static readonly object Sync = new object();

        /// <summary>
        /// ネイティブ配置先を準備し、OpenCV 等の DLL を展開する。
        /// </summary>
        public static void EnsureNativeBinaries()
        {
            lock (Sync)
            {
                AppPaths.EnsureDirectories();
                if (_initialized)
                {
                    EnsurePath();
                    return;
                }

                try
                {
                    CopyNativeFromAppBase();
                    EnsurePath();
                    _initialized = true;
                    Log.Debug("Native dir ready: " + AppPaths.NativeDir);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
            }
        }

        /// <summary>
        /// PATH の先頭に native ディレクトリを追加する。
        /// </summary>
        private static void EnsurePath()
        {
            string native = AppPaths.NativeDir;
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(p => string.Equals(p.Trim(), native, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Environment.SetEnvironmentVariable("PATH", native + ";" + path);
        }

        /// <summary>
        /// 実行ファイル周辺から OpenCV 系 DLL を AppData へコピーする。
        /// </summary>
        private static void CopyNativeFromAppBase()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] searchDirs =
            {
                baseDir,
                Path.Combine(baseDir, "dll", "x64"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native"),
                Path.Combine(baseDir, "x64")
            };

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(dir, "*.dll"))
                {
                    string name = Path.GetFileName(file);
                    if (!IsNativeCandidate(name))
                    {
                        continue;
                    }

                    string dest = Path.Combine(AppPaths.NativeDir, name);
                    try
                    {
                        FileInfo srcInfo = new FileInfo(file);
                        FileInfo destInfo = new FileInfo(dest);
                        if (!destInfo.Exists || destInfo.Length != srcInfo.Length || destInfo.LastWriteTimeUtc < srcInfo.LastWriteTimeUtc)
                        {
                            File.Copy(file, dest, true);
                            Log.Debug("Native DLL を展開: " + name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Native コピー失敗 " + name + ": " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// OpenCV / OpenCvSharp 関連の DLL か判定する。
        /// </summary>
        private static bool IsNativeCandidate(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            string n = fileName.ToLowerInvariant();
            return n.Contains("opencv")
                || n.Contains("opencvsharp")
                || n == "opencvsharpextern.dll";
        }
    }
}
