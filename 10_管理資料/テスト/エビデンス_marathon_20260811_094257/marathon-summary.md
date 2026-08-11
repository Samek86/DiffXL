# DiffXL Marathon Test Summary（ユーザー指示により途中終了）

| 項目 | 値 |
|------|-----|
| 開始 | 2026-08-11 09:42:58 |
| 終了 | 2026-08-11 12:15:34 |
| 所要 | **152.6 分**（目標 185 分・途中停止） |
| 停止理由 | ユーザー指示「テストを終了してください」 |
| ラウンド開始数 | 109 |
| PASS 実行（END pass=True） | **217** |
| FAIL 実行（pass=False） | **1** |
| full_feature PASS/FAIL | 109 / 0 |
| large_image PASS/FAIL | 108 / 1 |
| runs/ フォルダ数 | 218 |
| サンプル | full_feature + large_image |

## 合否

- 失敗あり: fail=1
- 目標 185 分には未達だが、**約 152.6 分・217 回の連続回帰**を実施
- large_image（大画像数十 MB）を含む auto-live が安定して PASS
- 停止時プロセス: marathon / DiffXL / cache-janitor を終了済み

## 試験中の改善（実施済み・ビルド反映）

1. 大画像サンプル `large_image_left/right.xlsx` 追加
2. 画像対応付け（寸法ベース・ImageOnly 分離）
3. auto-live の imageOnlyL/R・elapsedMs ログ
4. 比較キャッシュ purge（肥大化対策）

## 成果物

- `marathon-log.txt` / `status.json` / `STOPPED.txt`
- `runs/*/auto-live-report.txt`
- `IMPROVEMENTS.md` / `progress-*.md`
- `FINAL-CHECK.md`

## 停止時の 1 FAIL について

最終の `END large_image exit=-1 pass=False` は、ユーザー停止時に **実行中の DiffXL を強制終了した**ことによるものです（製品不具合ではなく停止操作由来）。
停止直前までの連続 PASS は **217 回 / FAIL 0** でした。
