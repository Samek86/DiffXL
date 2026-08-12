# content_diff ライブ検証エビデンス（2026-08-12）

## 結果

| 項目 | 結果 |
|------|------|
| 終了コード | **0** |
| レポート | `AUTO_LIVE_PASS` / `failures=0` |
| サンプル | `content_diff_left.xlsx` / `content_diff_right.xlsx` |

## 検証項目

| チェック | 結果 | ログ要旨 |
|----------|------|----------|
| 比較 | OK | items=6（Image, ImageOnlyRight, Structure 等） |
| シート切替 | OK | `S_Img8v9` 左右一致 |
| 統一ストリーム | OK | pairs L=11 R=11 |
| ペア index 選択 | OK | last=10 → selL=10 selR=10 |
| 左右スクロール同期 | OK | L=0.75 R=0.75 |
| MiniMap ジャンプ | OK | ImageOnlyRight → selL=5 selR=5、比率 0.995 |
| MiniMap 比率クリック | OK | L=0.85 R=0.85 |
| ハイライト ON/OFF | OK | スクリーンショット 06/07 |

## スクリーンショット

| ファイル | 内容 |
|----------|------|
| `01_after_compare.png` | 比較直後 |
| `02_sheet_S_Img8v9.png` | 画像 8vs9 シート（統一ストリーム） |
| `03_scroll_sync.png` | 左右 75% 同期スクロール |
| `04_minimap_jump_01_ImageOnlyRight.png` | MiniMap から片側画像差分へジャンプ（青選択枠） |
| `05_minimap_ratio.png` | MiniMap 比率クリック 85% |
| `06_highlight_off.png` | 差分強調 OFF |
| `07_highlight_on.png` | 差分強調 ON |
| `99_final.png` | 最終 |

## 再実行

```powershell
$exe = "20_ソース\DiffXL\DiffXL\bin\x64\Debug\DiffXL.exe"
$left = "30_参考資料\samples\content_diff_left.xlsx"
$right = "30_参考資料\samples\content_diff_right.xlsx"
$report = "10_管理資料\テスト\エビデンス_content_diff_rerun\auto-live-report.txt"
& $exe --auto-live-test --left $left --right $right --report $report
```
