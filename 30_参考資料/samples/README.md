# DiffXL サンプル Excel

このフォルダのサンプルは、DiffXL の主要機能を一通り確認するためのものです。

## 推奨ペア（フル機能）

| 左 (Left) | 右 (Right) |
|-----------|------------|
| `full_feature_left.xlsx` | `full_feature_right.xlsx` |

### シート対応と検証ポイント

| シート（左） | シート（右） | 自動対応 | 確認したいこと |
|--------------|--------------|----------|----------------|
| 表紙 | 表紙 | ○ 同名 | 基本表示、版番号テキスト差分 |
| 売上サマリ | 売上サマリ | ○ | **テキスト差分**（数量・金額・担当・備考・合計） |
| 製品カタログ | 製品カタログ | ○ | **画像差分**（同一 / 内容変更 / 左のみ / 右のみ） |
| 長い一覧 | 長い一覧 | ○ | **同期スクロール**・**MiniMap**（上下に散在する差分） |
| レイアウト確認 | レイアウト確認 | ○ | **表示忠実性**（行高・列幅・フォント・結合セル） |
| 仕様メモ_旧 | 仕様メモ_新 | × 別名 | **手動シート対応付け** → 再比較 |
| 左のみメモ | （なし） | — | **シート構成差分**（左のみ） |
| （なし） | 右のみメモ | — | **シート構成差分**（右のみ） |
| ずれ試験 | ずれ試験 | ○ | **行挿入によるずれ**・**アンカー設定** |

### 製品カタログの画像

| ID | 左 | 右 | 期待 |
|----|----|----|------|
| IMG-A | 共通ロゴ | 同一内容 | 画像差分なし |
| IMG-B | 基準バナー | 部分変更（赤 MOD + 黄三角） | 画像内容差分 |
| IMG-C | 左のみアイコン | なし | ImageOnlyLeft |
| IMG-D | なし | 右のみアイコン | ImageOnlyRight |
| IMG-E | サムネ | 同一内容 | 画像差分なし |

### その他の操作確認

- 差分強調トグル ON/OFF（再比較不要）
- 設定画面で差分色・不透明度変更
- 片側ファイル差し替え → 再比較
- 同一ファイルを左右に選んだ場合（差分ゼロに近い）

## 大画像ストレステスト

| 左 (Left) | 右 (Right) | 目安サイズ |
|-----------|------------|------------|
| `large_image_left.xlsx` | `large_image_right.xlsx` | 各 50MB 超 |

高解像度 PNG/JPEG（FHD〜4K）を複数埋め込み。画像抽出・OpenCV 比較・MiniMap・メモリ負荷の確認用。

| 画像ID | 左 | 右 | 期待 |
|--------|----|----|------|
| BIG-A | FHD PNG 同一 | 同一 | 画像差分なし |
| BIG-B | QHD PNG | MOD スタンプ＋ノイズ | 画像内容差分 |
| BIG-C | 4K PNG | なし | ImageOnlyLeft（右は非対応寸法） |
| BIG-D | なし | HD PNG (1280x720) | ImageOnlyRight（左 4K と誤ペアしない） |
| BIG-E | QHD JPEG 同一 | 同一 | 画像差分なし |
| BIG-F | ワイド PNG | CHG スタンプ | 画像内容差分 |
| BIG-G | 1600x900 同一 | 同一 | 画像差分なし |
| BIG-H | 1600x900 | DIFF スタンプ | 画像内容差分 |

シート名は `表紙` / `売上サマリ` / `製品カタログ` / `長い一覧` / `レイアウト確認` で full_feature と同様に `--auto-live-test` 互換。

再生成:

```text
python 30_参考資料/samples/_gen/create_large_image_samples.py
```

中間メディアは `_gen/media_large/`。

## 内容同期スクロール（完璧対応）

| 左 (Left) | 右 (Right) | 期待 JSON |
|-----------|------------|-----------|
| `content_scroll_left.xlsx` | `content_scroll_right.xlsx` | `content_scroll_expected.json` |

ContentScrollMap・画像ギャップホールド・再同期・横 1:1 の専用検証ペア。

| シート | 確認したいこと |
|--------|----------------|
| `SC_画像ギャップ` | 左 2 枚・右 3 枚。右のみ区間は左ホールド → same_B で再同期 |
| `SC_テキスト挿入` | 右に 2 行挿入。S01..S05 で再連結（L10↔R12） |
| `SC_大画像span` | twoCell 複数行スパン中の同一ペア内マッピング |
| `SC_横同期` | 縦同一・列幅広。横スクロール 1:1 |
| `SC_同順異内容` | 同順だが 2 枚目内容差分。decoy と誤ペアしない |

再生成:

```text
python 30_参考資料/samples/_gen/create_content_scroll_samples.py
```

中間メディアは `_gen/media_content_scroll/`。

## 既存の簡易サンプル

| ファイル | 用途 |
|----------|------|
| `text_only_left.xlsx` / `text_only_right.xlsx` | テキストのみの最小差分（スモーク） |
| `_smoke_plan02.xlsx` | 単体スモーク用 |

## 再生成

```text
python 30_参考資料/samples/_gen/create_samples.py
python 30_参考資料/samples/_gen/create_large_image_samples.py
python 30_参考資料/samples/_gen/create_content_scroll_samples.py
```

生成物は `30_参考資料/samples/` 直下に上書き出力されます。
中間 PNG は `_gen/media/` / `_gen/media_large/` / `_gen/media_content_scroll/` に置かれます。
