# ux_sync evidence

- Generated: 2026-08-11 19:33:32
- Repo: C:\JUN\WORK\DiffXL
- OverallFail: 0

## Stages
1. sync-ux-smoke.txt — SYNC_UX_SMOKE_PASS
2. perfect-smoke.txt — PERFECT_SCROLL_PASS
3. auto-live-content_scroll.txt — AUTO_LIVE_PASS (+ STATUS_LINE / JUMP_HINT)
4. auto-live-full_feature.txt — AUTO_LIVE_PASS

## Manual
See UXシナリオ_内容同期.md (TC-UX-01..12). Fill ux-checklist.md in this folder.

## Re-run
```powershell
powershell -File "10_管理資料\テスト\run-ux-sync-test.ps1"
```
