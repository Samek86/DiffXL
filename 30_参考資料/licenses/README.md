# 第三者ライブラリ / OSS ライセンス

DiffXL が利用する主なオープンソースコンポーネントです。  
各パッケージの正式なライセンス全文は NuGet パッケージ同梱の `LICENSE` / プロジェクト公式リポジトリを正とします。

| ライブラリ | 用途 | ライセンス（概要） | 参照 |
|------------|------|-------------------|------|
| YamlDotNet | 設定 YAML 読み書き | MIT | https://github.com/aaubry/YamlDotNet |
| Costura.Fody | マネージ DLL の exe 埋め込み | MIT | https://github.com/Fody/Costura |
| Fody | ビルド時 weaver 基盤 | MIT | https://github.com/Fody/Fody |
| OpenCvSharp4 | 画像差分（OpenCV の .NET ラッパ） | Apache-2.0 | https://github.com/shimat/opencvsharp |
| OpenCvSharp4.runtime.win | OpenCV ネイティブ（Windows） | Apache-2.0 等（OpenCV 由来） | 同上 / OpenCV |

## 改変について

要件方針として、必要に応じて OSS ソースを取得・改修してプロジェクトに取り込む場合があります。  
改修した場合は `20_ソース/third_party/` 等に由来と差分を残します（現状は NuGet 利用が中心）。

## Microsoft Excel

本アプリは **Excel のインストールや COM 連携を必要としません**。  
`.xlsx` を Open XML（ZIP）として直接読み取り、内容ベースで比較します。  
Excel デスクトップ製品を同梱・再配布するものではありません。
