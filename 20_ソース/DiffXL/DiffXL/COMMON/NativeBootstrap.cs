using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DiffXL.COMMON
{
    /// <summary>
    /// ネイティブ DLL を AppData\native へ展開し、読み込み可能にする。
    /// 単一 exe 配布時は埋め込みリソースから展開し、開発時はビルド出力横のファイルも利用する。
    /// </summary>
    public static class NativeBootstrap
    {
        /// <summary>
        /// 埋め込みリソース名の接頭辞（csproj の LogicalName と一致させる）。
        /// </summary>
        private const string EmbeddedPrefix = "DiffXL.native.";

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
                    // 1) 単一 exe 用: アセンブリ埋め込みから AppData へ
                    ExtractNativeFromEmbeddedResources();
                    // 2) 開発用フォールバック: 出力ディレクトリ横（NuGet がコピーした dll\x64 等）
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
        /// 埋め込みリソース（DiffXL.native.*）から OpenCV 系 DLL を AppData へ展開する。
        /// </summary>
        private static void ExtractNativeFromEmbeddedResources()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names;
            try
            {
                names = asm.GetManifestResourceNames();
            }
            catch (Exception ex)
            {
                Log.Debug("埋め込みリソース列挙失敗: " + ex.Message);
                return;
            }

            foreach (string resourceName in names)
            {
                if (string.IsNullOrEmpty(resourceName)
                    || !resourceName.StartsWith(EmbeddedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fileName = resourceName.Substring(EmbeddedPrefix.Length);
                if (!IsNativeCandidate(fileName))
                {
                    continue;
                }

                string dest = Path.Combine(AppPaths.NativeDir, fileName);
                try
                {
                    using (Stream stream = asm.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            continue;
                        }

                        if (File.Exists(dest))
                        {
                            FileInfo destInfo = new FileInfo(dest);
                            if (destInfo.Length == stream.Length)
                            {
                                // 同一サイズなら展開済みとみなす（起動高速化）
                                continue;
                            }
                        }

                        using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            stream.CopyTo(fs);
                        }

                        Log.Debug("Native DLL を埋め込みから展開: " + fileName);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("埋め込み展開失敗 " + fileName + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 実行ファイル周辺から OpenCV 系 DLL を AppData へコピーする（開発・非単一exe時）。
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
                            Log.Debug("Native DLL をファイルから展開: " + name);
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
