# DiffXL

**2 つの Excel（`.xlsx`）を、見た目ごと並べて差分を見抜く** Windows デスクトップアプリです。

[![Status](https://img.shields.io/badge/status-beta-yellow)](https://github.com/HandaJun/DiffXL)
[![Version](https://img.shields.io/badge/version-0.1.0--beta.1-orange)](./VERSION)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](#動作環境)
[![Excel](https://img.shields.io/badge/Excel-required-red)](#動作環境)

> **Beta / 開発中**  
> 現在はベータ版です。比較ロジック・UI・設定は今後変更されることがあります。  
> 本番業務での利用は、挙動を十分に確認したうえで自己責任でお願いします。

---

## なにができるか

DiffXL は、セルの値だけでなく **表・図形・埋め込み画像** まで含めて「どこが違うか」を視覚的に示します。

| できること | 説明 |
|------------|------|
| **左右分割ビュー** | 2 ブックを並べて同時表示（WinMerge 的な操作感） |
| **Excel そのまま表示** | 行高・列幅・フォント・図形を Excel 本体描画で再現 |
| **コンテンツベース比較** | 位置だけに頼らず、内容の並び（シーケンス）で突き合わせ |
| **画像差分** | OpenCV で埋め込み画像の変化を検出し強調 |
| **同期スクロール** | 片方のスクロールともう片方を内容に合わせて連動 |
| **MiniMap** | 差分位置の俯瞰とジャンプ |
| **差分色のカスタム** | 色・不透明度を設定画面から変更（YAML 永続化） |

### 設計の核心

表示品質を最優先にしています。自前グリッドでは保証しにくい **1px 単位のレイアウト再現** のため、**インストール済み Microsoft Excel をビューアとして埋め込み**、差分はその上に半透明オーバーレイで重ねます。

```
┌──────────────── DiffXL ────────────────┐
│  [左 .xlsx]     MiniMap     [右 .xlsx] │
│  Excel 埋め込み  ←同期→   Excel 埋め込み │
│  + 差分オーバーレイ（黄 50% など）        │
└────────────────────────────────────────┘
         │                    │
    .xlsx 解析 / 内容抽出      OpenCV 画像比較
```

---

## 動作環境

| 項目 | 要件 |
|------|------|
| OS | **Windows 64bit** |
| ランタイム | .NET Framework **4.8** 以降 |
| Excel | **デスクトップ版 Microsoft Excel**（COM 利用。必須） |
| 対象ファイル | **`.xlsx` のみ**（`.xls` / `.xlsm` は非対応） |
| 配布形態 | 原則 **単一 `DiffXL.exe`**（マネージ依存は内包） |

ユーザー設定・ログ・キャッシュ・OpenCV native は exe 横ではなく次に保存されます。

```
%AppData%\Roaming\DiffXL\
  settings.yaml
  logs\
  cache\
  native\
```

---

## 使い方（ベータ）

1. [Releases](https://github.com/HandaJun/DiffXL/releases) から `DiffXL.exe` を取得（または下記ビルド）
2. 起動し、左右の `.xlsx` を選んで比較開始
3. 差分は既定で **黄色・不透明度 50%** のオーバーレイ
4. ツールバーやショートカットで **差分強調の ON/OFF**（再比較不要）
5. **MiniMap** で差分へジャンプ
6. シート対応・アンカー・片側差し替え・再比較で条件を調整
7. **設定** で色・不透明度・同期などを変更

サンプルファイルは `30_参考資料/samples/` にあります。

---

## リポジトリ構成

```
DiffXL/
├── 20_ソース/DiffXL/          # ソリューション・WPF アプリ本体
├── 40_リリース/               # 配布用 README（exe は Release アセット）
├── 10_管理資料/               # 要件定義・計画・テスト記録
├── 30_参考資料/               # サンプル xlsx、OSS ライセンスメモ
├── docs/
│   ├── versioning.md          # 変更記録・SemVer ルール
│   ├── release-procedure.md   # AI/人間向けリリース手順書
│   └── superpowers/           # 設計メモ・実装プラン
├── VERSION                    # 現在バージョン（正本のひとつ）
├── CHANGELOG.md               # ユーザー向け変更履歴
└── README.md                  # 本ファイル
```

技術スタックの概要: **.NET Framework 4.8 / WPF / Excel COM / OpenCV (OpenCvSharp) / YamlDotNet / Costura.Fody**

---

## ビルド（開発者向け）

Visual Studio または MSBuild で **Release | x64** をビルドします。

```powershell
cd 20_ソース\DiffXL
msbuild DiffXL.sln /t:Clean,Build /p:Configuration=Release /p:Platform=x64
```

出力の目安:

```
20_ソース\DiffXL\DiffXL\bin\x64\Release\DiffXL.exe
```

スモーク用コードは `20_ソース/DiffXL/DiffXL/_smoke/` を参照してください。

---

## バージョン管理とリリース

このプロジェクトでは **修正があれば変更内容を記録し**、リリース時にバージョンを上げてその内容を公開します。

| 文書 | 内容 |
|------|------|
| [`docs/versioning.md`](./docs/versioning.md) | SemVer、コミット規約、CHANGELOG への追記義務 |
| [`docs/release-procedure.md`](./docs/release-procedure.md) | **AI が同じ手順でリリースするためのチェックリスト** |
| [`CHANGELOG.md`](./CHANGELOG.md) | リリース単位の変更履歴 |
| [`VERSION`](./VERSION) | 現在のバージョン文字列 |

現在のバージョン: **`0.1.0-beta.1`**（開発中ベータ）

リリースの流れ（要約）:

1. `CHANGELOG` の Unreleased を整理し版を決定  
2. `VERSION` / AssemblyInfo を更新してコミット  
3. Release\|x64 ビルド → 動作確認  
4. タグ `vX.Y.Z[-beta.N]` を push  
5. GitHub Release を作成（ベータは prerelease、exe をアセット添付）

詳細は必ず **`docs/release-procedure.md`** に従ってください。

---

## 現状とロードマップ

| 領域 | 状態（ベータ時点） |
|------|-------------------|
| Excel 埋め込み左右ビュー | 利用可能 |
| 差分オーバーレイ / 色設定 | 利用可能 |
| 同期スクロール / MiniMap | 利用可能 |
| コンテンツベース比較（表・図形・画像） | 開発・改善中 |
| 単一 exe 配布 | 骨組みあり（継続検証中） |
| 安定版 (1.0.0) | 未到達 |

計画・要件の詳細は `10_管理資料/要件定義.md` と `10_管理資料/計画/` を参照してください。

---

## フィードバック

- 不具合・要望: [GitHub Issues](https://github.com/HandaJun/DiffXL/issues)
- 大きな変更を入れる場合は、Issue または議論のうえで `feature/*` ブランチを推奨

---

## ライセンス・第三者コンポーネント

- アプリ本体のライセンス表記は整備中です（ベータ公開段階）。
- 利用 OSS の概要は [`30_参考資料/licenses/README.md`](./30_参考資料/licenses/README.md) を参照してください。
- Microsoft Excel はユーザー環境の製品ライセンスに従います。DiffXL は Excel 自体を再配布しません。

---

## 注意事項

- **Beta** のため、比較結果の網羅性・性能・UI は未完成部分があります。
- 表示の完全再現は **Excel 本体** に依存します。Excel 未インストール環境では動作しません。
- 大きなブックや高解像度画像では比較に時間がかかることがあります。
- テスト用エビデンスや大容量サンプルがリポジトリに含まれる場合があります（クローン時のサイズに注意）。

---

<p align="center">
  <b>DiffXL</b> — See the difference, as Excel sees it.<br>
  <sub>Beta · Under active development · Windows x64</sub>
</p>
