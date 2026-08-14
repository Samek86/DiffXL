DiffXL stress suite samples
===========================
Files:
  stress_suite_left.xlsx
  stress_suite_right.xlsx

Sheets:
  表紙           version text diff
  長大一覧        ~1000 shared candidates with asymmetric clusters:
                   - multi-row delete clusters (side totals differ a lot)
                   - multi-row insert-only blocks (L-INS-* / R-INS-*)
                   + summary table + thumbs + cell-level diffs
  画面キャプチャ   5x 1600x900 screen-like captures (partial mods on right)
                   + SCR-LONLY (left only) / SCR-RONLY (right only)

Long-table plan seed: 20260813 (reproducible pseudo-random)

Regenerate:
  python 30_参考資料/samples/_gen/create_stress_samples.py
