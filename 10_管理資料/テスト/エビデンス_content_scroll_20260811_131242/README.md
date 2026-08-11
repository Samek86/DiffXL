# content_scroll evidence

- Generated: 2026-08-11 13:13:03
- Repo: C:\JUN\WORK\DiffXL
- Left: C:\JUN\WORK\DiffXL\30_参考資料\samples\content_scroll_left.xlsx
- Right: C:\JUN\WORK\DiffXL\30_参考資料\samples\content_scroll_right.xlsx
- Expected: C:\JUN\WORK\DiffXL\30_参考資料\samples\content_scroll_expected.json
- OverallFail: 0

## Stages
- T1/T2: perfect-smoke.txt (PERFECT_SCROLL_PASS required)
- T3: auto-live-report.txt (AUTO_LIVE_PASS; Excel COM)

## Re-run
```powershell
powershell -File "10_管理資料\テスト\run-content-scroll-test.ps1"
```
