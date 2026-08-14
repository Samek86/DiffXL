# DiffXL 内容ストリーム パフォーマンス改善 設計

| 項目 | 内容 |
|------|------|
| 日付 | 2026-08-12 |
| 状態 | 承認済み（会話上・実装着手） |
| 対象 | 長大シート（stress_suite 長大一覧 約 1000 行）の描画・操作 |

## 方針（確定）

1. **データは全量・Visual はビューポートのみ**（ストリーム全体仮想化）
2. **表は 1 ブロックではなく行単位にストリーム展開**
3. **左右で Align / 高さマップを 1 回だけ共有**
4. **全ツリー `UpdateLayout` による `SyncPairHeights` は廃止**（推定高＋実測補正）

## 成功基準

- stress_suite「長大一覧」表示: 体感 2 秒以内
- スクロール・同期スクロールが滑らか
- 差分意味・左右対応は変えない

## 構成

```
SheetContent L/R
  → ContentStreamBuilder.GetOrBuildLayout
  → ContentStreamLayout { Pairs (expanded), Heights[] }
  → ContentPane (virtualized spacer host) × 左右共有 Layout
```

## ブロック種別追加

- `TableHeader` … 表タイトル帯
- `TableRow` … 1 表示行（Align 済み）
