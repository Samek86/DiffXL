# バージョン管理ルール

DiffXL の変更を **必ず記録し**、リリース時にバージョンと説明を一貫して更新するためのルールです。  
人間・AI エージェントの両方がこの文書に従います。

---

## 1. 目的

| 目的 | 手段 |
|------|------|
| 何が変わったか後から追える | Git コミット + `CHANGELOG.md` |
| 配布物の識別 | `VERSION` + アセンブリ版 + Git タグ |
| リリース説明の再利用 | CHANGELOG の該当セクションを GitHub Release 本文に転記 |
| AI が同じ手順でリリースできる | `docs/release-procedure.md` |

---

## 2. バージョン番号（SemVer）

形式: `MAJOR.MINOR.PATCH` に、必要ならプレリリース接尾辞を付けます。

| 種類 | いつ上げるか | 例 |
|------|----------------|-----|
| **MAJOR** | 互換性のない大きな変更（比較結果方針の破壊的変更、配布前提の断絶など） | `1.0.0` → `2.0.0` |
| **MINOR** | 後方互換のある機能追加 | `0.1.0` → `0.2.0` |
| **PATCH** | 後方互換のある不具合修正・小さな改善 | `0.1.0` → `0.1.1` |
| **プレリリース** | 開発中・ベータ | `0.1.0-beta.1`, `0.1.0-rc.1` |

現在の方針:

- **0.x** はベータ／開発中。破壊的変更があっても MAJOR を 1 に上げず、`0.x` 内で MINOR を積極的に上げてよい。
- 安定版（本番推奨）を初めて出すとき **1.0.0** とする。

単一の正本:

| ファイル | 役割 |
|----------|------|
| `VERSION` | 人間可読な現在バージョン（1 行。例: `0.1.0-beta.1`） |
| `CHANGELOG.md` | ユーザー向け変更履歴 |
| `20_ソース/DiffXL/DiffXL/Properties/AssemblyInfo.cs` | `AssemblyVersion` / `AssemblyFileVersion`（数値 4 部。プレリリース接尾辞は付けない） |
| Git タグ | `v` + `VERSION` の内容（例: `v0.1.0-beta.1`） |

`VERSION` の `0.1.0-beta.1` は Assembly では `0.1.0.0` のように **数値部分のみ** に写します（Build/Revision は原則 `0`）。

---

## 3. 変更は必ず記録する

### 3.1 Git コミット（必須）

- 意味のある変更単位でコミットする。
- メッセージは [Conventional Commits](https://www.conventionalcommits.org/) を推奨する。

```
<type>(optional-scope): <短い要約>

[本文: なぜ・何を]
```

よく使う `type`:

| type | 用途 |
|------|------|
| `feat` | 新機能 |
| `fix` | 不具合修正 |
| `docs` | ドキュメントのみ |
| `refactor` | 振る舞いを変えない整理 |
| `test` | テスト・検証スクリプト |
| `chore` | 雑務・設定・ビルド周辺 |
| `build` | ビルドシステム・パッケージ |
| `perf` | 性能改善 |

例:

```
feat(diff): align images by content sequence before regional compare

fix(scroll): keep minimap viewport in sync after sheet change

docs: add AI release procedure
```

### 3.2 CHANGELOG（必須・リリース単位で確定）

- 作業中は **`## [Unreleased]`** に追記する。
- ユーザーに見える変更（機能・修正・破壊的変更）は **コミットと同じタイミング、または PR／作業完了時に必ず Unreleased へ書く**。
- 内部だけの typo 修正など、ユーザー影響が無いものは省略してよい。ただし迷ったら書く。

カテゴリ（Keep a Changelog）:

- **Added** — 新機能
- **Changed** — 既存の振る舞い変更
- **Deprecated** — 将来削除予定
- **Removed** — 削除
- **Fixed** — 不具合修正
- **Security** — 脆弱性対応

日本語で、利用者目線の一文にする（実装詳細の羅列だけにしない）。

### 3.3 コードコメント

- ソースのクラス・メソッド・変数の直上に **日本語コメント** を付ける（プロジェクト方針）。
- バージョン番号そのものをソース各所にハードコードしない。版情報は `VERSION` / AssemblyInfo に集約する。

---

## 4. ブランチ運用（簡易）

| ブランチ | 役割 |
|----------|------|
| `main` | 公開の既定ブランチ。リリース可能な（または最新のベータ）状態 |
| `feature/*` | 機能開発 |
| `fix/*` | 修正 |
| `release/*` | 必要ならリリース準備専用 |

- 小さな修正は `main` 直でも可（ベータ期）。
- 大きな機能は `feature/*` で進め、完了後に `main` へマージする。

---

## 5. リリース時に上げるもの（チェックリスト要約）

詳細手順は **`docs/release-procedure.md`** を正とする。最低限:

1. `CHANGELOG.md` の `[Unreleased]` を新バージョン見出しへ移す
2. `VERSION` を更新
3. `AssemblyInfo.cs` の版を更新
4. 必要なら `40_リリース/README.md` の版表記を更新
5. コミット → タグ `vX.Y.Z[-prerelease]` → push → GitHub Release

---

## 6. AI / エージェントへの指示（抜粋）

- 機能・修正を入れたら **同じ作業の中で** `CHANGELOG.md` の `[Unreleased]` を更新する。
- 「ちょっとした修正だから記録不要」と自己判断してスキップしない。ユーザー影響が無いかだけを判断基準にする。
- リリース作業は **`docs/release-procedure.md` を上から実行**し、手順の省略・順序入れ替えをしない。
- タグの打ち直し（force）や履歴改変が必要な場合は **必ずユーザー確認**を取る。

---

## 7. 関連ファイル

| パス | 内容 |
|------|------|
| `VERSION` | 現在バージョン |
| `CHANGELOG.md` | 変更履歴 |
| `docs/release-procedure.md` | リリース手順（AI 実行用） |
| `docs/versioning.md` | 本ファイル |
| `40_リリース/README.md` | 配布物同梱の利用者向け README |

## 8. 単一 exe とネイティブ DLL（補足）

| 種別 | 扱い |
|------|------|
| マネージ（YamlDotNet 等） | Costura.Fody で `DiffXL.exe` に埋め込み |
| OpenCV ネイティブ x64 | NuGet からビルド時に **EmbeddedResource** として exe へ埋め込み |
| 実行時 | `NativeBootstrap` が `%AppData%\Roaming\DiffXL\native` へ展開し PATH に追加 |
| Git | `*.dll` はコミットしない（NuGet restore が正本） |

OpenCvSharp の版を上げるときは `DiffXL.csproj` の `OpenCvSharpPackageVersion` を更新する。  
ネイティブ埋め込みは **`OpenCvSharpExtern.dll`（x64）のみ**（動画用 `opencv_videoio_ffmpeg*` は同梱しない）。
