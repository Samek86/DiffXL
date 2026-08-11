# DiffXL Marathon Evidence（3h+）

| 項目 | 内容 |
|------|------|
| 開始 | 2026-08-11 09:42:58 JST |
| 目標終了 | 2026-08-11 12:47:58 JST（185 分） |
| 実行 | `run-marathon-test.ps1` |
| サンプル | `full_feature_*` と `large_image_*` を交互 |
| 実行ファイル | `40_リリース/DiffXL.exe`（途中で改善ビルドを差し替え） |

## 成果物

| ファイル | 内容 |
|----------|------|
| `marathon-log.txt` | ラウンドごとの開始/終了 |
| `status.json` | 最新スナップショット |
| `marathon-summary.md` | 終了後の合否集計 |
| `runs/*/` | 各 auto-live の report / meta / app-log-tail |
| `IMPROVEMENTS.md` | 試験中に入れた改善メモ |
| `cache-janitor.log` | キャッシュ掃除ログ |

## 途中で入れた改善

1. **大画像サンプル** `large_image_left/right.xlsx`（数十 MB・FHD〜4K）
2. **画像対応付け** … 汎用 `imageN` 名を無視、ピクセル寸法で対応、面積比が大きい誤ペアを片側のみへ
3. **auto-live** … `imageOnlyL/R`・`elapsedMs`・`IMAGE_DIFFS_OK` ログ
4. **キャッシュ整理** … `AppPaths.PurgeCompareCache`（起動時・比較時）＋ janitor

## 代表結果（改善後 large_image）

```
COMPARE_OK count=31 text=26 image=3 imageOnlyL=1 imageOnlyR=1 structure=0
IMAGE_DIFFS_OK related=5
MINIMAP_OK / MINIMAP_MULTI 4/4
AUTO_LIVE_PASS
```
