# エビデンス MiniMap 画面認識（2026-08-11）

- **計画書**: [../テスト計画_MiniMap完全動作_20260811.md](../テスト計画_MiniMap完全動作_20260811.md)
- **結果**: [test-results-minimap.md](test-results-minimap.md)
- **方式**: Orca computer-use（`orca-computer-use-windows`）＋画面キャプチャ読取

## ハイライト

実クリックでステータスに以下が確認された:

- `MiniMap → 長い一覧!行 79 … (L:OK R:OK)`
- `MiniMap → 売上サマリ!行 17 … (L:OK R:OK)`
- `MiniMap → 表紙!行 3 … (L:OK R:OK)`
- `MiniMap → レイアウト確認!行 12 … (L:OK R:OK)`

## 注意

`--app DiffXL` は Visual Studio のウィンドウタイトルと衝突することがある。**`pid:<DiffXLのPID>` を使うこと。**
