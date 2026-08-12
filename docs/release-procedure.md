# DiffXL リリース手順書（AI / 人間共通）

この文書は、**同じ品質で繰り返しリリースするための実行手順**です。  
AI エージェントは本手順を上から順に実行し、途中で省略・順序変更をしないでください。  
不明点や破壊的操作が必要な場合は **ユーザーに確認してから** 進めてください。

関連ルール: [`versioning.md`](./versioning.md)  
変更履歴の正本: リポジトリ直下の `CHANGELOG.md`  
現在バージョンの正本: リポジトリ直下の `VERSION`

---

## 0. 前提

### 0.1 権限・ツール

| 項目 | 要件 |
|------|------|
| Git | ローカルリポジトリがクリーン、またはリリース用変更のみ未コミット |
| GitHub | `origin` が `https://github.com/Samek86/DiffXL.git`（または同等） |
| CLI | `gh` がログイン済み（`gh auth status`） |
| ビルド | Visual Studio 用 MSBuild が使える（.NET Framework 4.8 / WPF / x64） |
| 環境 | Windows x64。検証時は Microsoft Excel（デスクトップ版）が必要 |

### 0.2 リリースの種類

| 種別 | バージョン例 | いつ使うか |
|------|----------------|------------|
| ベータ | `0.x.y-beta.N` | 開発中の公開、機能追加の途中公開 |
| RC | `0.x.y-rc.N` | 安定版直前の候補 |
| 安定 | `1.0.0` 以降（原則プレリリース無し） | 本番推奨 |
| ホットフィックス | `X.Y.Z+1` | 直前リリースの不具合のみ直す |

現フェーズは **Beta（0.x）** です。

### 0.3 禁止事項（ユーザー確認なしでやらない）

- `git push --force` / 既に公開したタグの付け直し
- リモートの履歴改変（rebase して force push 等）
- 秘密情報・個人データのコミット
- 未検証バイナリを「安定版」として表記すること

---

## 1. リリース前チェック

作業ディレクトリ: リポジトリルート（例: `C:\JUN\WORK\DiffXL`）

### 1.1 状態確認

```powershell
git status
git branch --show-current
git log --oneline -10
Get-Content VERSION
```

確認すること:

- [ ] リリース対象ブランチは原則 `main`（ユーザー指定があればそれに従う）
- [ ] 取り込みたい feature がすべてマージ済み
- [ ] 作業ツリーに **リリースと無関係な未コミット変更が無い**（あればstash または別コミットで整理）

### 1.2 変更内容の洗い出し

前回タグから現在までのコミットを確認する。

```powershell
$prev = git describe --tags --abbrev=0 2>$null
if ($prev) { git log "$prev..HEAD" --oneline } else { git log --oneline -30 }
```

- [ ] `CHANGELOG.md` の `[Unreleased]` に、ユーザー向けの Added / Changed / Fixed 等が揃っている
- [ ] コミットメッセージと CHANGELOG の内容に大きな抜けが無い
- [ ] 破壊的変更がある場合、CHANGELOG に明記し、必要なら MINOR 以上を上げる（0.x では MINOR 推奨）

### 1.3 バージョン決定

`docs/versioning.md` の SemVer ルールに従い、**新しいバージョン文字列** を決める。

例:

- 機能追加ベータ: `0.1.0-beta.1` → `0.2.0-beta.1`
- 同じベータ系列の修正: `0.1.0-beta.1` → `0.1.0-beta.2`
- パッチ: `0.2.0` → `0.2.1`

- [ ] 新バージョン `NEW_VERSION` を決定した
- [ ] Git タグ名は `v` + `NEW_VERSION`（例: `v0.1.0-beta.1`）
- [ ] Assembly 用の数値版 `ASSEMBLY_VERSION` を決めた（例: `0.1.0.0`。プレリリース接尾辞は付けない）

---

## 2. バージョンと CHANGELOG の更新

### 2.1 `VERSION`

`VERSION` ファイルを **1 行だけ** 新バージョンにする。

```
0.2.0-beta.1
```

### 2.2 `CHANGELOG.md`

1. `## [Unreleased]` の直下にあった項目を、新しい見出しへ移す。

```markdown
## [Unreleased]

### Added
- （空でよい。次の開発用）

## [0.2.0-beta.1] - YYYY-MM-DD

### Added
- ...

### Fixed
- ...
```

2. 日付は **リリース日（ローカル日付）** の `YYYY-MM-DD`。
3. ファイル末尾の比較リンクを更新する。

```markdown
[Unreleased]: https://github.com/Samek86/DiffXL/compare/v0.2.0-beta.1...HEAD
[0.2.0-beta.1]: https://github.com/Samek86/DiffXL/releases/tag/v0.2.0-beta.1
```

（前回タグがある場合、前回分の compare リンクも一貫させる。）

### 2.3 アセンブリ版

`20_ソース/DiffXL/DiffXL/Properties/AssemblyInfo.cs`:

```csharp
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]
```

- [ ] `VERSION` / CHANGELOG / AssemblyInfo が同じリリースを指している

### 2.4 配布 README（任意だが推奨）

`40_リリース/README.md` にバージョンやベータ注意があれば同期する。

### 2.5 コミット

```powershell
git add VERSION CHANGELOG.md "20_ソース/DiffXL/DiffXL/Properties/AssemblyInfo.cs"
# 他にリリース関連ファイルがあれば追加
git commit -m "chore(release): prepare v0.2.0-beta.1"
```

（実際のバージョン番号に置き換える。）

---

## 3. ビルドと配布物

### 3.1 クリーンビルド

```powershell
cd "20_ソース\DiffXL"
msbuild DiffXL.sln /t:Clean,Build /p:Configuration=Release /p:Platform=x64
```

成功すること。出力の目安:

- `20_ソース\DiffXL\DiffXL\bin\x64\Release\DiffXL.exe` が存在する
- マネージ依存 DLL が横に並ばない（Costura 想定）
- ビルド出力に `dll\x64\*.dll` が残っていても **配布物には含めない**（exe 内埋め込み + 実行時 AppData 展開）

### 3.2 `40_リリース` への配置（単一 exe）

```powershell
# リポジトリルートから — 配布は DiffXL.exe のみ（+ 利用者向け README）
Copy-Item "20_ソース\DiffXL\DiffXL\bin\x64\Release\DiffXL.exe" "40_リリース\DiffXL.exe" -Force
# config が必要な場合のみ
# Copy-Item "20_ソース\DiffXL\DiffXL\bin\x64\Release\DiffXL.exe.config" "40_リリース\" -Force
```

注意:

- **配布単位は原則 `DiffXL.exe` のみ**（ネイティブ OpenCV は exe 埋め込み → 初回起動で `%AppData%\Roaming\DiffXL\native`）。
- `*.exe` は `.gitignore` 対象のため、**バイナリは Git にコミットしない**（原則）。
- 配布は **GitHub Release のアセット**として添付する。
- ビルド前に NuGet が復元されていること（`OpenCvSharp4.runtime.win` の x64 が埋め込み元）。

- [ ] Release|x64 の `DiffXL.exe` を `40_リリース` に配置した
- [ ] `dll\` フォルダを Release アセットに付けていない
- [ ] 同梱 README（`40_リリース/README.md`）の前提条件が現状と一致している

### 3.3 最低限の動作確認（ベータでも実施）

可能な範囲で:

1. クリーンな状態に近い環境で `DiffXL.exe` を起動
2. `%AppData%\Roaming\DiffXL\` に settings / logs が作られる
3. サンプル（`30_参考資料/samples/` の左右 xlsx）で比較が完了する
4. 差分強調トグル、MiniMap、シート切替の基本操作ができる

失敗した場合:

- [ ] リリースを中止し、`fix` コミット → CHANGELOG Unreleased に記載 → 本手順の 1 からやり直し

---

## 4. タグ付けと push

### 4.1 タグ

注釈付きタグを推奨する。

```powershell
$ver = (Get-Content VERSION -Raw).Trim()
git tag -a "v$ver" -m "Release v$ver"
```

### 4.2 push

```powershell
git push origin HEAD
git push origin "v$ver"
```

- [ ] コミットがリモートに載った
- [ ] タグがリモートに載った

---

## 5. GitHub Release 作成

`CHANGELOG.md` の当該バージョン節を本文に使う。

```powershell
$ver = (Get-Content VERSION -Raw).Trim()
# CHANGELOG から該当節を抽出して release_notes.md を作るか、直接 -n で渡す

gh release create "v$ver" `
  --title "DiffXL v$ver" `
  --notes-file release_notes.md `
  "40_リリース/DiffXL.exe" `
  "40_リリース/README.md"
```

ベータの場合:

```powershell
gh release create "v$ver" `
  --title "DiffXL v$ver (Beta)" `
  --notes-file release_notes.md `
  --prerelease `
  "40_リリース/DiffXL.exe" `
  "40_リリース/README.md"
```

Release 本文のテンプレ:

```markdown
## DiffXL vX.Y.Z-beta.N

**ステータス: Beta（開発中）** — 仕様・比較結果・UI は変更される可能性があります。

### 変更点

（CHANGELOG の該当セクションを転記）

### 前提条件

- Windows 64bit
- .NET Framework 4.8 以降
- Microsoft Excel（デスクトップ版）
- 対象ファイル: `.xlsx` のみ

### ダウンロード

- `DiffXL.exe` を取得して実行
- 設定・ログ: `%AppData%\Roaming\DiffXL\`

### フィードバック

Issue で報告してください: https://github.com/Samek86/DiffXL/issues
```

- [ ] `--prerelease` をベータ／RC で付けた
- [ ] exe と README をアセット添付した
- [ ] ブラウザで Release ページを確認した

作業用の `release_notes.md` を作った場合はコミットせず削除する。

```powershell
Remove-Item release_notes.md -ErrorAction SilentlyContinue
```

---

## 6. リリース後

### 6.1 記録

- [ ] `CHANGELOG.md` の `[Unreleased]` が空のテンプレ状態に戻っている
- [ ] GitHub の Releases / Tags に当該版がある
- [ ] ユーザーへ共有する文言があれば、バージョン番号と既知の制限を含める

### 6.2 次開発の開始

通常はそのまま `main` で開発を続ける。大きな機能は `feature/*` を切る。

以降のコード変更では:

1. コミットする
2. **同じ作業で** `CHANGELOG.md` の `[Unreleased]` を更新する

---

## 7. トラブルシューティング

| 症状 | 対処 |
|------|------|
| ビルド失敗 | エラーを修正してから版上げコミットを作り直す。壊れたタグを公開済みならユーザー確認 |
| タグを打ち間違えた（未 push） | `git tag -d vX.Y.Z` のあと正しいタグを作り直す |
| タグを打ち間違えた（push 済み） | **force しない**。ユーザーに確認し、必要なら次のパッチ／ベータ番号で出し直す |
| `gh` 未認証 | `gh auth login` をユーザーに依頼 |
| 大きなバイナリで push 失敗 | exe を Git に入れていないか確認。Release アセット経由にする |
| CHANGELOG と実挙動が不一致 | リリースを止め、記載を直してからタグ／Release を作成 |

---

## 8. AI 向け短縮チェックリスト（コピー用）

```
[ ] 1. git status / 対象ブランチ確認
[ ] 2. 前回タグ..HEAD の変更を確認
[ ] 3. NEW_VERSION を決定（SemVer + 必要なら -beta.N）
[ ] 4. VERSION 更新
[ ] 5. CHANGELOG: Unreleased → 新バージョン節へ移動 + 日付 + リンク
[ ] 6. AssemblyInfo の AssemblyVersion / FileVersion 更新
[ ] 7. chore(release) コミット
[ ] 8. MSBuild Release|x64
[ ] 9. 40_リリース に DiffXL.exe 配置（Git には載せない）
[ ] 10. 最低限の起動・比較確認
[ ] 11. git tag -a vNEW_VERSION
[ ] 12. git push origin HEAD && git push origin vNEW_VERSION
[ ] 13. gh release create（beta なら --prerelease）+ アセット
[ ] 14. Release ページ目視確認
[ ] 15. Unreleased を空テンプレに戻済みか最終確認
```

---

## 9. 初回公開時のメモ（履歴）

| 項目 | 値 |
|------|----|
| 初回ベータ | `0.1.0-beta.1` |
| 日付 | 2026-08-12 |
| リポジトリ | https://github.com/Samek86/DiffXL |
| 備考 | リポジトリ公開とドキュメント整備を含む。以降のリリースはこの手順書に従う |
