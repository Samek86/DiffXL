# DiffXL テスト結果

- 実施日時: 2026-08-11T09:01:37+09:00
- 実行ファイル: `40_リリース/DiffXL.exe` (2026-08-11 08:28)
- サンプル: `30_参考資料/samples/full_feature_left.xlsx` / `full_feature_right.xlsx`
- 方式: UI Automation（実画面操作）+ スクリーンショット + `--auto-live-test`
- 集計: **PASS=25 / FAIL=0 / BLOCKED=0 / TOTAL=25**

## 結果一覧

| ID | 名称 | 結果 | メモ |
|----|------|------|------|
| TC-01 | launch | **PASS** | 起動成功 |
| TC-01b | startup-ui | **PASS** | 起動パネル表示 |
| TC-02a | pick-left | **PASS** | full_feature_left 選択 |
| TC-02b | pick-right | **PASS** | full_feature_right 選択 |
| TC-02 | file-select | **PASS** | 比較開始が有効化 |
| TC-03 | compare | **PASS** | 差分 69 件、左右 Excel + MiniMap |
| TC-04 | minimap | **PASS** | MiniMap クリック＋ログ/ステータス |
| TC-05 | sheet-toolbar | **PASS** | ツールバー シートコンボ操作 |
| TC-06 | sheet-sync | **PASS** | シート同期（auto-live SHEET_SYNC_OK） |
| TC-07 | diff-markers-toggle | **PASS** | 差分強調 ON/OFF |
| TC-08 | settings-open | **PASS** | 設定ダイアログ表示 |
| TC-09 | settings-cancel | **PASS** | キャンセルで閉じる |
| TC-10 | recompare | **PASS** | 再比較 |
| TC-11 | sheet-map | **PASS** | シート対応ダイアログ |
| TC-12 | anchor | **PASS** | アンカーダイアログ |
| TC-13 | replace-left | **PASS** | 左差し替えダイアログ |
| TC-14 | replace-right | **PASS** | 右差し替えダイアログ |
| TC-15 | wheel-left | **PASS** | 左ホイール |
| TC-16 | wheel-right | **PASS** | 右ホイール |
| TC-17 | h-scroll | **PASS** | Shift+ホイール |
| TC-18 | pan | **PASS** | 中/右ドラッグパン |
| TC-19 | resize | **PASS** | 通常化＋最大化 |
| TC-20 | back-to-start | **PASS** | 最初からで起動画面へ |
| TC-21 | auto-live-test | **PASS** | AUTO_LIVE_PASS / FAILURES=0 |
| TC-22 | app-log | **PASS** | StackOverflow/未処理例外なし |

## 自動試験ハイライト (TC-21)

- `08:43:36.761 COMPARE_OK count=69 text=63 image=2 structure=4`
- `08:43:36.761 HIGHLIGHT before=True off=False on=True`
- `08:43:36.761 HIGHLIGHT_OK`
- `08:43:36.793 SHEET 表紙 pre-activated (to verify MiniMap sheet switch)`
- `08:43:37.115 MINIMAP targetSheet=長い一覧 targetRow=44 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=44 Rsr=44 status=MiniMap → 長い一覧!行 44 / テキスト差分 E44: 「2480」→「2980」 (L:OK R:OK)`
- `08:43:37.115 MINIMAP_OK`
- `08:43:37.465 MINIMAP_MULTI sheet=売上サマリ row=5 Lsheet=売上サマリ Rsheet=売上サマリ Lsr=5 Rsr=5 ok=True`
- `08:43:37.683 MINIMAP_MULTI sheet=製品カタログ row=5 Lsheet=製品カタログ Rsheet=製品カタログ Lsr=5 Rsr=5 ok=True`
- `08:43:37.894 MINIMAP_MULTI sheet=長い一覧 row=7 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=7 Rsr=7 ok=True`
- `08:43:38.097 MINIMAP_MULTI sheet=表紙 row=3 Lsheet=表紙 Rsheet=表紙 Lsr=3 Rsr=3 ok=True`
- `08:43:38.097 MINIMAP_MULTI_SUMMARY ok=4/4`
- `08:43:38.097 MINIMAP_MULTI_OK`
- `08:43:38.144 SHEET_SYNC after L→長い一覧 Lsheet=長い一覧 Rsheet=長い一覧`
- `08:43:38.159 SHEET_SYNC_OK`
- `08:43:38.159 PAIR_COMBO count=6`
- `08:43:39.073 RECOMPARE_OK count=69`
- `08:43:39.105 SETTINGS_OPEN_OK constructed SettingsWindow`
- `08:43:39.298 SETTINGS_DIALOG_SAVE saved=True`
- `08:43:39.298 SETTINGS_DIALOG result=True SavedProp=True`
- `08:43:39.321 SETTINGS_SAVE_OK=True color=#FFCC00 via=SettingsWindow.ShowDialog`
- `08:43:39.321 SETTINGS_OPEN_OK`
- `08:43:39.639 SETTINGS_RESTORED color=#FFFF00`
- `08:43:40.382 RESIZE leftH0=716.04 leftH1=796.04 hostAttachedL=True`
- `08:43:40.382 FAILURES=0`
- `08:43:40.382 AUTO_LIVE_PASS`
- `08:49:50.850 COMPARE_OK count=69 text=63 image=2 structure=4`
- `08:49:50.850 HIGHLIGHT before=True off=False on=True`
- `08:49:50.865 HIGHLIGHT_OK`
- `08:49:50.881 SHEET 表紙 pre-activated (to verify MiniMap sheet switch)`
- `08:49:51.229 MINIMAP targetSheet=長い一覧 targetRow=44 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=44 Rsr=44 status=MiniMap → 長い一覧!行 44 / テキスト差分 E44: 「2480」→「2980」 (L:OK R:OK)`
- `08:49:51.229 MINIMAP_OK`
- `08:49:51.589 MINIMAP_MULTI sheet=売上サマリ row=5 Lsheet=売上サマリ Rsheet=売上サマリ Lsr=5 Rsr=5 ok=True`
- `08:49:52.032 MINIMAP_MULTI sheet=製品カタログ row=5 Lsheet=製品カタログ Rsheet=製品カタログ Lsr=5 Rsr=5 ok=True`
- `08:49:52.260 MINIMAP_MULTI sheet=長い一覧 row=7 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=7 Rsr=7 ok=True`
- `08:49:52.493 MINIMAP_MULTI sheet=表紙 row=3 Lsheet=表紙 Rsheet=表紙 Lsr=3 Rsr=3 ok=True`
- `08:49:52.493 MINIMAP_MULTI_SUMMARY ok=4/4`
- `08:49:52.493 MINIMAP_MULTI_OK`
- `08:49:52.560 SHEET_SYNC after L→長い一覧 Lsheet=長い一覧 Rsheet=長い一覧`
- `08:49:52.560 SHEET_SYNC_OK`
- `08:49:52.560 PAIR_COMBO count=6`
- `08:49:53.477 RECOMPARE_OK count=69`
- `08:49:53.494 SETTINGS_OPEN_OK constructed SettingsWindow`
- `08:49:53.678 SETTINGS_DIALOG_SAVE saved=True`
- `08:49:53.678 SETTINGS_DIALOG result=True SavedProp=True`
- `08:49:53.695 SETTINGS_SAVE_OK=True color=#FFCC00 via=SettingsWindow.ShowDialog`
- `08:49:53.709 SETTINGS_OPEN_OK`
- `08:49:53.978 SETTINGS_RESTORED color=#FFFF00`
- `08:49:54.695 RESIZE leftH0=716.04 leftH1=796.04 hostAttachedL=True`
- `08:49:54.695 FAILURES=0`
- `08:49:54.695 AUTO_LIVE_PASS`
- `08:57:30.015 COMPARE_OK count=69 text=63 image=2 structure=4`
- `08:57:30.015 HIGHLIGHT before=True off=False on=True`
- `08:57:30.015 HIGHLIGHT_OK`
- `08:57:30.047 SHEET 表紙 pre-activated (to verify MiniMap sheet switch)`
- `08:57:30.399 MINIMAP targetSheet=長い一覧 targetRow=44 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=44 Rsr=44 status=MiniMap → 長い一覧!行 44 / テキスト差分 E44: 「2480」→「2980」 (L:OK R:OK)`
- `08:57:30.399 MINIMAP_OK`
- `08:57:30.925 MINIMAP_MULTI sheet=売上サマリ row=5 Lsheet=売上サマリ Rsheet=売上サマリ Lsr=5 Rsr=5 ok=True`
- `08:57:31.215 MINIMAP_MULTI sheet=製品カタログ row=5 Lsheet=製品カタログ Rsheet=製品カタログ Lsr=5 Rsr=5 ok=True`
- `08:57:31.460 MINIMAP_MULTI sheet=長い一覧 row=7 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=7 Rsr=7 ok=True`
- `08:57:31.658 MINIMAP_MULTI sheet=表紙 row=3 Lsheet=表紙 Rsheet=表紙 Lsr=3 Rsr=3 ok=True`
- `08:57:31.658 MINIMAP_MULTI_SUMMARY ok=4/4`
- `08:57:31.658 MINIMAP_MULTI_OK`
- `08:57:31.708 SHEET_SYNC after L→長い一覧 Lsheet=長い一覧 Rsheet=長い一覧`
- `08:57:31.708 SHEET_SYNC_OK`
- `08:57:31.708 PAIR_COMBO count=6`
- `08:57:32.593 RECOMPARE_OK count=69`
- `08:57:32.610 SETTINGS_OPEN_OK constructed SettingsWindow`
- `08:57:32.793 SETTINGS_DIALOG_SAVE saved=True`
- `08:57:32.793 SETTINGS_DIALOG result=True SavedProp=True`
- `08:57:32.809 SETTINGS_SAVE_OK=True color=#FFCC00 via=SettingsWindow.ShowDialog`
- `08:57:32.809 SETTINGS_OPEN_OK`
- `08:57:33.059 SETTINGS_RESTORED color=#FFFF00`
- `08:57:33.794 RESIZE leftH0=716.04 leftH1=796.04 hostAttachedL=True`
- `08:57:33.794 FAILURES=0`
- `08:57:33.794 AUTO_LIVE_PASS`
- `09:00:11.035 COMPARE_OK count=69 text=63 image=2 structure=4`
- `09:00:11.035 HIGHLIGHT before=True off=False on=True`
- `09:00:11.035 HIGHLIGHT_OK`
- `09:00:11.066 SHEET 表紙 pre-activated (to verify MiniMap sheet switch)`
- `09:00:11.406 MINIMAP targetSheet=長い一覧 targetRow=44 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=44 Rsr=44 status=MiniMap → 長い一覧!行 44 / テキスト差分 E44: 「2480」→「2980」 (L:OK R:OK)`
- `09:00:11.406 MINIMAP_OK`
- `09:00:11.744 MINIMAP_MULTI sheet=売上サマリ row=5 Lsheet=売上サマリ Rsheet=売上サマリ Lsr=5 Rsr=5 ok=True`
- `09:00:11.957 MINIMAP_MULTI sheet=製品カタログ row=5 Lsheet=製品カタログ Rsheet=製品カタログ Lsr=5 Rsr=5 ok=True`
- `09:00:12.194 MINIMAP_MULTI sheet=長い一覧 row=7 Lsheet=長い一覧 Rsheet=長い一覧 Lsr=7 Rsr=7 ok=True`
- `09:00:12.439 MINIMAP_MULTI sheet=表紙 row=3 Lsheet=表紙 Rsheet=表紙 Lsr=3 Rsr=3 ok=True`
- `09:00:12.439 MINIMAP_MULTI_SUMMARY ok=4/4`
- `09:00:12.439 MINIMAP_MULTI_OK`
- `09:00:12.507 SHEET_SYNC after L→長い一覧 Lsheet=長い一覧 Rsheet=長い一覧`
- `09:00:12.507 SHEET_SYNC_OK`
- `09:00:12.507 PAIR_COMBO count=6`
- `09:00:13.440 RECOMPARE_OK count=69`
- `09:00:13.456 SETTINGS_OPEN_OK constructed SettingsWindow`
- `09:00:13.671 SETTINGS_DIALOG_SAVE saved=True`
- `09:00:13.672 SETTINGS_DIALOG result=True SavedProp=True`
- `09:00:13.692 SETTINGS_SAVE_OK=True color=#FFCC00 via=SettingsWindow.ShowDialog`
- `09:00:13.692 SETTINGS_OPEN_OK`
- `09:00:13.981 SETTINGS_RESTORED color=#FFFF00`
- `09:00:14.691 RESIZE leftH0=716.04 leftH1=796.04 hostAttachedL=True`
- `09:00:14.691 FAILURES=0`
- `09:00:14.691 AUTO_LIVE_PASS`

## スクリーンショット（全 26 枚）

- `screenshots/01_01_startup.png` (57.8 KB)
- `screenshots/02_02a_left_dialog.png` (97 KB)
- `screenshots/03_02a_after_left.png` (61.3 KB)
- `screenshots/04_02b_right_dialog.png` (99.4 KB)
- `screenshots/05_02_both_selected.png` (65.1 KB)
- `screenshots/06_03_compare_result.png` (219.2 KB)
- `screenshots/07_04_minimap_click.png` (184.1 KB)
- `screenshots/08_05_hl_off.png` (182.9 KB)
- `screenshots/09_05_hl_on.png` (182.7 KB)
- `screenshots/10_06_settings_open.png` (208.8 KB)
- `screenshots/11_06_settings_closed.png` (182.7 KB)
- `screenshots/12_07_recompare.png` (189.1 KB)
- `screenshots/13_08_sheetmap.png` (208.2 KB)
- `screenshots/14_09_anchor.png` (220.1 KB)
- `screenshots/15_10_replace_left_dialog.png` (229.1 KB)
- `screenshots/16_11_replace_right_dialog.png` (229.4 KB)
- `screenshots/17_12_sheet_combo.png` (232.8 KB)
- `screenshots/18_12_sheet_changed.png` (260.2 KB)
- `screenshots/19_13_wheel_left.png` (214.5 KB)
- `screenshots/20_14_wheel_right.png` (219.5 KB)
- `screenshots/21_15_hscroll.png` (214.5 KB)
- `screenshots/22_16_pan.png` (214.5 KB)
- `screenshots/23_17_resize_normal.png` (44.8 KB)
- `screenshots/24_18_resize_max.png` (214.5 KB)
- `screenshots/25_19_back_to_start.png` (116.9 KB)
- `screenshots/26_20_final.png` (116.9 KB)

## その他成果物

- `run-full-ui-test.ps1` … 再現用ハーネス
- `run-log.txt` … 実行トレース
- `auto-live-report.txt` … 自動ライブ生ログ
- `app-log-excerpt.txt` … アプリログ末尾
- `ui-trees/` … UI Automation ツリー抜粋
- テストケース定義: `../テストケース一覧.md`
