# UX シナリオ: 内容同期（素敵な操作感）

| 項目 | 内容 |
|------|------|
| 対象 | DiffXL 内容ベース縦同期 UX（ギャップ可視化・Status・JumpHint・MiniMap・ピン） |
| サンプル | `content_scroll_left/right.xlsx` / `full_feature_left/right.xlsx` |
| 自動化 | `run-ux-sync-test.ps1`（SyncUxSmoke / PerfectSmoke / auto-live） |
| 計画 | [08_内容同期UX完成.md](../計画/08_内容同期UX完成.md) / [実装計画](../../docs/superpowers/plans/2026-08-11-delightful-content-sync-ux.md) |
| 作成日 | 2026-08-11 |

## 前提

1. Debug|x64 ビルド済み、Excel 導入済み
2. 設定既定: `Ui.SyncScroll=true` / `ShowSyncGapOverlay=true` / `ReduceMotion=false` / `ShowSyncToastOnJump=true`
3. 比較対象シートの起点: **SC_画像ギャップ**（content_scroll）

## 自動ハーネス（先に緑）

```powershell
powershell -File "10_管理資料\テスト\run-ux-sync-test.ps1"
```

期待: `UX_SYNC_HARNESS_PASS` / `SYNC_UX_SMOKE_PASS` / `PERFECT_SCROLL_PASS` / 双方 `AUTO_LIVE_PASS`

---

## 人手シナリオ（必須 12）

実施後、エビデンスフォルダに `ux-checklist.md`（各 ID の PASS/FAIL + メモ）を残す。

| ID | 操作 | 合格条件 | 不合格の例 |
|----|------|----------|------------|
| **TC-UX-01** | SC_画像ギャップ 通常域（same_A 付近）を**ゆっくり**ホイール | 左右が体感同時に追従。フッタ Status が **Equal 文言**（`同期ON · 内容対応 · L… ↔ R…`） | 片方だけ動く／Status が空・OFF のまま |
| **TC-UX-02** | 右のみ帯（only 画像 R8 付近）を通過 | **左オーバーレイ**に「待機」系キャプション。左行が固定（ホールド） | 左が右に引きずられる／オーバーレイなし |
| **TC-UX-03** | 帯を抜け same_B（R12↔L8）へ | **トースト**または Status/ヒントに再同期文言。L↔R 内容一致 | ワープの説明なし／内容不一致のまま |
| **TC-UX-04** | 左のみ帯を通過（テキスト挿入シート等） | **右**が待機オーバーレイ | 右が引きずられる／説明なし固まり |
| **TC-UX-05** | 横スクロール（Shift+ホイール等） | 列が 1:1 で一致。**ギャップ表示は出ない** | 横でギャップ UI が出る／列不一致 |
| **TC-UX-06** | MiniMap クリック | 左右行が Map 整合。青帯ラベル **L/R** | 片方だけジャンプ／行番号強制同期 |
| **TC-UX-07** | 設定で SyncScroll **OFF** | 追従なし、オーバーレイ消える。Status「同期OFF」 | OFF でも追従／オーバーレイ残存 |
| **TC-UX-08** | 高速フリック 約 20 ノッチ | **最終位置**が Map 整合（途中の波打ちは許容） | 最終行が大きくマップ外 |
| **TC-UX-09** | 画像ピン変更（画像対応ダイアログ） | マップが変わりホールド位置が変わる | ピン無効／再比較必須で反映されない |
| **TC-UX-10** | シート切替（コンボ） | Status が新シート文脈。**誤った旧ギャップが残らない** | 旧オーバーレイ残存 |
| **TC-UX-11** | ReduceMotion **ON** で TC-UX-03 相当 | トーストのみで中間ステップなし（イージング省略） | 中間ステップが残る |
| **TC-UX-12** | full_feature **製品カタログ** | 片側のみ画像で「固まった」が **説明付き**で許容（Status/オーバーレイ） | 説明なしに片側だけ止まる |

### チェックリスト雛形（`ux-checklist.md`）

```markdown
# UX checklist — YYYY-MM-DD

| ID | 結果 | メモ |
|----|------|------|
| TC-UX-01 | PASS/FAIL/SKIP | |
| TC-UX-02 | | |
| TC-UX-03 | | |
| TC-UX-04 | | |
| TC-UX-05 | | |
| TC-UX-06 | | |
| TC-UX-07 | | |
| TC-UX-08 | | |
| TC-UX-09 | | |
| TC-UX-10 | | |
| TC-UX-11 | | |
| TC-UX-12 | | |
```

## 自動でカバーされる近似

| 人手 ID | 自動 |
|---------|------|
| 01–03, 05 | content_scroll auto-live（hold/resync/hscroll + StatusLine 非空） |
| マップ数値 | ContentScrollPerfectSmoke |
| Probe/Status/JumpHint 文言 | SyncUxSmoke |
| 12 の回帰 | full_feature auto-live |

※ オーバーレイ見た目・体感遅延・ReduceMotion は人手必須。

## 合否

- **自動 PASS**: `UX_SYNC_HARNESS_PASS`
- **人手 PASS**: TC-UX-01..12 すべて PASS（環境不可は SKIP 明記、DoD 未達）
- **全体完了**: 自動緑 + 人手 12 PASS（計画 DoD）
