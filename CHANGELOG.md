# Changelog

本プロジェクトの注目すべき変更はすべてこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に準拠し、
バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/)（SemVer 2.0.0）に従います。

リリース前の作業・AI 向け手順は `docs/release-procedure.md`、日常の変更記録ルールは `docs/versioning.md` を参照してください。

## [Unreleased]

### Added

- （次リリース向けの追加をここに追記）

### Changed

- （次リリース向けの変更をここに追記）

### Fixed

- （次リリース向けの修正をここに追記）

### Changed

- 単一 exe 配布向けに OpenCV ネイティブ（x64）を exe へ埋め込み、起動時に `%AppData%\Roaming\DiffXL\native` へ展開するよう変更

### Removed

- リポジトリから OpenCV 等の `.dll` を除外（NuGet / ビルド成果物として取得。Git に載せない）

## [0.1.0-beta.1] - 2026-08-12

### Added

- 初回パブリック公開（**Beta / 開発中**）
- 2 つの `.xlsx` を左右分割で比較表示する WPF デスクトップアプリ
- Excel 本体埋め込みによるビュー再現（行高・列幅・フォント・図形）
- セル／表／図形／画像を内容シーケンスとして扱うコンテンツベース比較
- OpenCV による画像・領域差分の強調表示
- 同期スクロール、MiniMap、シート対応、手動アンカー
- 差分強調色・不透明度の設定（YAML / `%AppData%\Roaming\DiffXL`）
- 単一 exe 配布の骨組み（Costura.Fody + native の AppData 展開）
- バージョン管理ルール（`docs/versioning.md`）と AI 向けリリース手順（`docs/release-procedure.md`）

### Notes

- 本バージョンは **ベータ版** です。API・比較結果・UI は今後変更される可能性があります。
- 対象は Windows x64 / Microsoft Excel（デスクトップ版）必須 / `.xlsx` のみです。

[Unreleased]: https://github.com/HandaJun/DiffXL/compare/v0.1.0-beta.1...HEAD
[0.1.0-beta.1]: https://github.com/HandaJun/DiffXL/releases/tag/v0.1.0-beta.1
