# Changelog

本プロジェクトの注目すべき変更はすべてこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に準拠し、
バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/)（SemVer 2.0.0）に従います。

リリース前の作業・AI 向け手順は `docs/release-procedure.md`、日常の変更記録ルールは `docs/versioning.md` を参照してください。

## [Unreleased]

## [0.2.0-beta.1] - 2026-08-15

### Added

- 内容ビューの「すべて / 表 / 画像 / セル」チップで、次の差分（F8）の対象を種類で絞る（本文ストリームは隠さない）
- MiniMap の黄マーカーをクリックするとその差分へジャンプ（ドラッグは従来のスクラブ）。タイトルを「差分マップ」に変更
- 左右ペインのシートコンボをツールバーのシート対応と同じペアで連動
- 現在シートの前後差分へ移動（F8 / Shift+F8、ツールバー「前の差分」「次の差分」）。端では循環
- MIT `LICENSE`（Copyright (c) 2026 DiffXL contributors）
- 製品の一致定義を README・起動画面に明記（同じ内容が同じ個数・表は枠／Excel 表の行・画像は出現順と見た目。数式・マクロ・ピボット・チャートは対象外）
- 内容ベース比較の必須シナリオ用サンプル `content_diff_left.xlsx` / `content_diff_right.xlsx` と生成スクリプト
- 内容ベース比較の設計書・実装計画（`docs/superpowers/specs|plans/2026-08-12-content-based-diff*`）
- 長大シート向け内容ストリーム仮想化（高さマップ＋ビューポート Realize）
- MiniMap 青帯を可視範囲に比例（下限 16px）し、スクロールバー同様の掴み／ジャンプ操作
- MiniMap スクラブ中のフレーム統合と軽量 Realize
- 画像ペアのオーバーレイ比較（位置合わせ＋重ね表示）
- ストレステストサンプル `stress_suite_left.xlsx` / `stress_suite_right.xlsx`

### Changed

- 表検出で Excel の表定義（`xl/tables`）を罫線 flood より優先し、ヘッダーに検出元（Excel 表 / 罫線）を表示する
- README の配布表記を「Release で Costura 確認時のみ原則 1 exe」に修正（単一 exe の断定をやめる）
- **比較・表示方針を内容ベースへ統一**（セル位置・画像アンカーを比較キーにしない）
- 表示を自前の内容ビュー（WPF）に変更。Excel COM 埋め込みは廃止
- 要件定義を版 0.5 に改訂（Excel ビュー最優先 → 内容差分の正確さ・分かりやすさ・安定性）
- 全体ロードマップを内容ベース計画参照付きに更新（計画 02 は歴史的位置づけ）
- ルート README / リリース README: **Microsoft Excel インストール不要** を明記
- 単一 exe 配布向けに OpenCV ネイティブ（x64 の `OpenCvSharpExtern.dll` のみ）を exe へ埋め込み、起動時に `%AppData%\Roaming\DiffXL\native` へ展開するよう変更
- 動画用 `opencv_videoio_ffmpeg*` は配布・埋め込み対象外（静止画比較のみのため不要）
- 画像ハイライト領域を比較用縮小空間ではなく **元画像ピクセル座標** に変換して返すよう修正（max-side &gt; 1024 での枠ずれを解消）
- ツールバー「画像対応」（手動ピン）を非表示・無効化。内容比較時の画像シーケンス対応は自動のみ

### Fixed

- 大画像（最大辺 &gt; 1024）でハイライト矩形が ImagePairView 上にずれて表示される問題
- 表内の対応行で列数が違うとき、余列を `TableCellChange` として残す（`min` 切り捨てをやめる）
- 表内の塗り分け（zebra）は差分にしない

### Removed

- Excel COM 埋め込みレイヤおよび起動時 Excel 必須チェック
- リポジトリから OpenCV 等の `.dll` を除外（NuGet / ビルド成果物として取得。Git に載せない）

## [0.1.0-beta.1] - 2026-08-12

### Added

- 初回パブリック公開（**Beta / 開発中**）
- 2 つの `.xlsx` を左右分割で比較表示する WPF デスクトップアプリ
- セル／表／図形／画像を内容シーケンスとして扱うコンテンツベース比較
- OpenCV による画像・領域差分の強調表示
- 同期スクロール、MiniMap、シート対応
- 差分強調色・不透明度の設定（YAML / `%AppData%\Roaming\DiffXL`）
- 単一 exe 配布の骨組み（Costura.Fody + native の AppData 展開）
- バージョン管理ルール（`docs/versioning.md`）と AI 向けリリース手順（`docs/release-procedure.md`）

### Notes

- 本バージョンは **ベータ版** です。API・比較結果・UI は今後変更される可能性があります。
- 対象は Windows x64 / `.xlsx` のみです（後続の 0.2.0-beta.1 で Excel 必須を撤廃）。

[Unreleased]: https://github.com/Samek86/DiffXL/compare/v0.2.0-beta.1...HEAD
[0.2.0-beta.1]: https://github.com/Samek86/DiffXL/releases/tag/v0.2.0-beta.1
[0.1.0-beta.1]: https://github.com/Samek86/DiffXL/releases/tag/v0.1.0-beta.1
