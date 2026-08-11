# エビデンス_20260811_084012

## 概要

| 項目 | 内容 |
|------|------|
| 対象 | DiffXL (`40_リリース/DiffXL.exe`) |
| 実施 | 2026-08-11 09:00 頃（JST） |
| 結果 | **PASS 25 / FAIL 0 / BLOCKED 0** |
| 方式 | 実機 UI 操作（UI Automation）+ 画面キャプチャ + `--auto-live-test` |

## 参照

- テストケース: [../テストケース一覧.md](../テストケース一覧.md)
- 結果詳細: [test-results.md](test-results.md)
- 画面キャプチャ: [screenshots/](screenshots/)
- 自動ライブ: [auto-live-report.txt](auto-live-report.txt)

## 備考

- 本機に Orca computer-use CLI が未インストールだったため、同等の実操作を PowerShell UI Automation で実施した。
- 比較結果: 差分 69 件（テキスト 63 / 画像 2 / 構造 4）。MiniMap・シート同期・設定保存も auto-live で検証済み。
