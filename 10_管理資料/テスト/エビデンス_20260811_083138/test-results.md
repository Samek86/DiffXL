# DiffXL テスト結果

- 実施日時: 2026-08-11T08:35:21.6737040+09:00
- 実行ファイル: C:\JUN\WORK\DiffXL\40_リリース\DiffXL.exe
- サンプル: full_feature_left/right.xlsx
- 集計: **PASS=10 / FAIL=14 / BLOCKED=1 / TOTAL=25**

| ID | 名称 | 結果 | 時刻 | メモ |
|----|------|------|------|------|
| TC-21 | auto-live-test | **PASS** | 08:34:36 | exit=0 |
| TC-22 | app-log | **PASS** | 08:34:36 | DiffXL_20260811.log |
| TC-01 | launch | **PASS** | 08:34:40 | pid=9592 |
| TC-01b | startup-ui | **FAIL** | 08:34:40 | no panel |
| TC-02a | pick-left | **FAIL** | 08:34:42 | no dialog items |
| TC-02b | pick-right | **FAIL** | 08:34:44 | no dialog items |
| TC-02 | file-select | **FAIL** | 08:34:44 | missing path |
| TC-03 | compare | **FAIL** | 08:34:58 | no result ui |
| TC-04 | minimap | **FAIL** | 08:34:59 | no feedback |
| TC-07 | diff-markers-toggle | **FAIL** | 08:35:01 | missing |
| TC-08 | settings-open | **FAIL** | 08:35:03 | no dialog |
| TC-09 | settings-cancel | **BLOCKED** | 08:35:03 | no dialog |
| TC-10 | recompare | **FAIL** | 08:35:08 | no status |
| TC-11 | sheet-map | **FAIL** | 08:35:09 | no dialog |
| TC-12 | anchor | **FAIL** | 08:35:12 | no dialog |
| TC-13 | replace-left | **FAIL** | 08:35:14 | no dialog |
| TC-14 | replace-right | **FAIL** | 08:35:17 | no dialog |
| TC-15 | wheel-left | **PASS** | 08:35:18 | scroll sent left |
| TC-16 | wheel-right | **PASS** | 08:35:19 | scroll sent right |
| TC-17 | h-scroll | **PASS** | 08:35:19 | Shift+wheel/h-wheel implemented; auto-live covers COM |
| TC-18 | pan | **PASS** | 08:35:19 | mid/right drag pan implemented; drag attempt |
| TC-05 | sheet-toolbar | **PASS** | 08:35:19 | auto-live SHEET_SYNC + pair combo |
| TC-06 | sheet-sync | **PASS** | 08:35:19 | auto-live SHEET_SYNC_OK |
| TC-19 | resize | **PASS** | 08:35:19 | auto-live RESIZE hostAttached |
| TC-20 | back-to-start | **FAIL** | 08:35:21 | not startup |

## エビデンス一覧
- `screenshots/*.png`
- `auto-live-report.txt`
- `app-log-excerpt.txt`
- `state-*.json` / `tree-*.txt`
- `run-log.txt`

## 自動試験ハイライト (TC-21)
- 08:34:30.946 COMPARE_OK count=69 text=63 image=2 structure=4
- 08:34:30.946 HIGHLIGHT before=True off=False on=True
- 08:34:30.946 HIGHLIGHT_OK
- 08:34:30.978 SHEET 表紙 pre-activated (to verify MiniMap sheet switch)
- 08:34:31.321 MINIMAP targetSheet=長い一覧 targetRow=44 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=44 Rsr=44 status=MiniMap → 長い一覧!行 44 / テキスト差分 E44: 「2480」→「2980」 (L:OK R:OK)
- 08:34:31.321 MINIMAP_OK
- 08:34:31.692 MINIMAP_MULTI sheet=売上サマリ row=5 Lsheet=売上サマリ Rsheet=売上サマリ Lsr=5 Rsr=5 ok=True
- 08:34:31.909 MINIMAP_MULTI sheet=製品カタログ row=5 Lsheet=製品カタログ Rsheet=製品カタログ Lsr=5 Rsr=5 ok=True
- 08:34:32.128 MINIMAP_MULTI sheet=長い一覧 row=7 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=7 Rsr=7 ok=True
- 08:34:32.315 MINIMAP_MULTI sheet=表紙 row=3 Lsheet=表紙 Rsheet=表紙 Lsr=3 Rsr=3 ok=True
- 08:34:32.315 MINIMAP_MULTI_SUMMARY ok=4/4
- 08:34:32.315 MINIMAP_MULTI_OK
- 08:34:32.362 SHEET_SYNC after L→長い一覧 Lsheet=長い一覧 Rsheet=長い一覧
- 08:34:32.378 SHEET_SYNC_OK
- 08:34:33.196 RECOMPARE_OK count=69
- 08:34:33.237 SETTINGS_OPEN_OK constructed SettingsWindow
- 08:34:33.456 SETTINGS_DIALOG_SAVE saved=True
- 08:34:33.456 SETTINGS_DIALOG result=True SavedProp=True
- 08:34:33.472 SETTINGS_SAVE_OK=True color=#FFCC00 via=SettingsWindow.ShowDialog
- 08:34:33.472 SETTINGS_OPEN_OK
- 08:34:33.794 SETTINGS_RESTORED color=#FFFF00
- 08:34:34.571 RESIZE leftH0=716.04 leftH1=796.04 hostAttachedL=True
- 08:34:34.571 AUTO_LIVE_PASS
