using System;
using System.IO;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DiffXL.COMMON
{
    /// <summary>
    /// ユーザー設定のルートモデル。
    /// </summary>
    public class SettingsModel
    {
        /// <summary>
        /// 差分強調関連の設定。
        /// </summary>
        public DiffSettings Diff { get; set; } = new DiffSettings();

        /// <summary>
        /// UI 関連の設定。
        /// </summary>
        public UiSettings Ui { get; set; } = new UiSettings();

        /// <summary>
        /// ログ関連の設定。
        /// </summary>
        public LogSettings Log { get; set; } = new LogSettings();
    }

    /// <summary>
    /// 差分強調の色・表示・画像ハイライトに関する設定。
    /// </summary>
    public class DiffSettings
    {
        /// <summary>
        /// 差分強調の初期表示（true で表示）。既定 true。
        /// </summary>
        public bool HighlightEnabled { get; set; } = true;

        /// <summary>
        /// 差分色 RGB（例: #FFFF00）。セル等の既存強調用。
        /// </summary>
        public string HighlightColor { get; set; } = "#FFFF00";

        /// <summary>
        /// 不透明度 0.0〜1.0。既定 0.5（50%）。セル等の既存強調用。
        /// </summary>
        public double HighlightOpacity { get; set; } = 0.5;

        /// <summary>
        /// 画像差分領域の枠色（#AARRGGBB または #RRGGBB）。既定 不透明赤。
        /// 画像領域では既存 HighlightColor よりこちらを優先する。
        /// </summary>
        public string ImageHighlightBorderColor { get; set; } = "#FFFF0000";

        /// <summary>
        /// 画像差分領域の枠線幅（px）。既定 3。
        /// </summary>
        public int ImageHighlightBorderThickness { get; set; } = 3;

        /// <summary>
        /// 画像差分領域の塗り色（#AARRGGBB 推奨）。既定 黄・α 約 50%（#80FFFF00）。
        /// </summary>
        public string ImageHighlightFillColor { get; set; } = "#80FFFF00";

        /// <summary>
        /// 画像対応で割当禁止とする差分比率（0..1）。既定 0.45（Match 最小類似度 = 1 - 本値 = 0.55）。
        /// </summary>
        public double ImageRejectDiffRatio { get; set; } = 0.45;

        /// <summary>
        /// absdiff 後の二値化閾値。既定 15。
        /// </summary>
        public double ImageAbsDiffThreshold { get; set; } = 15.0;

        /// <summary>
        /// 有意差分として残す最小連結面積（px）。既定 25。
        /// </summary>
        public int ImageMinRegionArea { get; set; } = 25;
    }

    /// <summary>
    /// UI 関連の設定。
    /// </summary>
    public class UiSettings
    {
        /// <summary>
        /// 左右ビューの同期スクロールを行うか。
        /// </summary>
        public bool SyncScroll { get; set; } = true;

        /// <summary>
        /// 同期ポーリングの保険間隔（ms）。主経路はイベント駆動。既定 250。
        /// </summary>
        public int SyncPollFallbackMs { get; set; } = 250;

        /// <summary>
        /// 内容ギャップ時に左右ペインへ半透明オーバーレイを出すか。既定 true。
        /// </summary>
        public bool ShowSyncGapOverlay { get; set; } = true;

        /// <summary>
        /// 再同期ジャンプ時に短いトースト通知を出すか。既定 true。
        /// </summary>
        public bool ShowSyncToastOnJump { get; set; } = true;

        /// <summary>
        /// 動きを減らす（再同期の中間スクロール・近似イージングを行わない）。既定 false。
        /// </summary>
        public bool ReduceMotion { get; set; } = false;

        /// <summary>
        /// ウィンドウ位置・サイズを記憶するか。
        /// </summary>
        public bool RememberWindowBounds { get; set; } = true;
    }

    /// <summary>
    /// ログ関連の設定。
    /// </summary>
    public class LogSettings
    {
        /// <summary>
        /// ログレベル（Debug / Info / Error）。
        /// </summary>
        public string Level { get; set; } = "Info";
    }

    /// <summary>
    /// YAML で設定を読み書きする。
    /// </summary>
    public static class AppSettings
    {
        /// <summary>
        /// メモリ上の現在設定。
        /// </summary>
        public static SettingsModel Current { get; private set; } = new SettingsModel();

        /// <summary>
        /// settings.yaml を読み込む。無い場合は既定値を作成して保存する。
        /// </summary>
        public static void Load()
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.SettingsFile))
            {
                Current = CreateDefault();
                Save();
                return;
            }

            try
            {
                string yaml = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                Current = deserializer.Deserialize<SettingsModel>(yaml) ?? CreateDefault();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                Current = CreateDefault();
            }

            Normalize(Current);
        }

        /// <summary>
        /// 現在設定を settings.yaml へ保存する。
        /// </summary>
        public static void Save()
        {
            AppPaths.EnsureDirectories();
            Normalize(Current);
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            string yaml = serializer.Serialize(Current);
            File.WriteAllText(AppPaths.SettingsFile, yaml, Encoding.UTF8);
        }

        /// <summary>
        /// 既定の設定モデルを生成する。
        /// </summary>
        /// <returns>既定設定</returns>
        private static SettingsModel CreateDefault()
        {
            return new SettingsModel();
        }

        /// <summary>
        /// null や範囲外の値を補正する。
        /// </summary>
        /// <param name="model">設定モデル</param>
        private static void Normalize(SettingsModel model)
        {
            if (model == null)
            {
                return;
            }

            if (model.Diff == null)
            {
                model.Diff = new DiffSettings();
            }

            if (model.Ui == null)
            {
                model.Ui = new UiSettings();
            }

            if (model.Log == null)
            {
                model.Log = new LogSettings();
            }

            if (string.IsNullOrWhiteSpace(model.Diff.HighlightColor))
            {
                model.Diff.HighlightColor = "#FFFF00";
            }

            if (model.Diff.HighlightOpacity < 0)
            {
                model.Diff.HighlightOpacity = 0;
            }

            if (model.Diff.HighlightOpacity > 1)
            {
                model.Diff.HighlightOpacity = 1;
            }

            // YAML でカンマ小数になる環境差を避けるため、読み込み後に丸める
            model.Diff.HighlightOpacity = Math.Round(model.Diff.HighlightOpacity, 4, MidpointRounding.AwayFromZero);

            // 画像ハイライト既定（枠赤 3px・塗り黄 50%）
            if (string.IsNullOrWhiteSpace(model.Diff.ImageHighlightBorderColor))
            {
                model.Diff.ImageHighlightBorderColor = "#FFFF0000";
            }

            if (string.IsNullOrWhiteSpace(model.Diff.ImageHighlightFillColor))
            {
                model.Diff.ImageHighlightFillColor = "#80FFFF00";
            }

            if (model.Diff.ImageHighlightBorderThickness < 0)
            {
                model.Diff.ImageHighlightBorderThickness = 0;
            }
            else if (model.Diff.ImageHighlightBorderThickness > 32)
            {
                model.Diff.ImageHighlightBorderThickness = 32;
            }

            // 画像閾値（0..1 / 正の範囲にクランプ）
            if (model.Diff.ImageRejectDiffRatio < 0)
            {
                model.Diff.ImageRejectDiffRatio = 0;
            }
            else if (model.Diff.ImageRejectDiffRatio > 1)
            {
                model.Diff.ImageRejectDiffRatio = 1;
            }

            model.Diff.ImageRejectDiffRatio = Math.Round(
                model.Diff.ImageRejectDiffRatio, 4, MidpointRounding.AwayFromZero);

            if (model.Diff.ImageAbsDiffThreshold < 0)
            {
                model.Diff.ImageAbsDiffThreshold = 0;
            }
            else if (model.Diff.ImageAbsDiffThreshold > 255)
            {
                model.Diff.ImageAbsDiffThreshold = 255;
            }

            model.Diff.ImageAbsDiffThreshold = Math.Round(
                model.Diff.ImageAbsDiffThreshold, 4, MidpointRounding.AwayFromZero);

            if (model.Diff.ImageMinRegionArea < 1)
            {
                model.Diff.ImageMinRegionArea = 1;
            }
            else if (model.Diff.ImageMinRegionArea > 1000000)
            {
                model.Diff.ImageMinRegionArea = 1000000;
            }

            if (string.IsNullOrWhiteSpace(model.Log.Level))
            {
                model.Log.Level = "Info";
            }

            // 保険ポーリング間隔（イベント駆動が主経路）。UI 仕様 100–1000、既定 250
            if (model.Ui.SyncPollFallbackMs <= 0)
            {
                model.Ui.SyncPollFallbackMs = 250;
            }
            else if (model.Ui.SyncPollFallbackMs < 100)
            {
                model.Ui.SyncPollFallbackMs = 100;
            }
            else if (model.Ui.SyncPollFallbackMs > 1000)
            {
                model.Ui.SyncPollFallbackMs = 1000;
            }
        }
    }
}
