# MiniMap Viewport Band Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MiniMap 青帯の高さを可視範囲 / 内容全体に比例させ、下限 16px とスクロールバー同等の掴み／ジャンプ操作にする。

**Architecture:** 数式は WPF 非依存の `MiniMapViewportBand` に閉じる。`ContentPane` が `ViewportHeight` / `ExtentHeight` から可視比率を出し、`MainWindow` が比率＋可視比率を MiniMap へ渡す。ドラッグは `grabOffset` を保持する。

**Tech Stack:** WPF (.NET Framework 4.8)、C#、既存 `_smoke` + `csc /r:DiffXL.exe`

## Global Constraints

- 青帯最小高さは **16px（WPF DIP）**。帯内 % ラベルは帯高さ **22px 未満で非表示**。下部ヒントは残す。
- 可視比率の唯一の定義は `viewport / extent`。行数推定は使わない。
- 帯外クリックは即ジャンプ（ページ送りしない）。帯内ドラッグは掴み位置を維持する。
- スクラブ性能経路（`ScrubStarted` / フレーム統合 / `ScrubEnd`）は維持する。
- 比較エンジン・黄マーカー配置・height map の意味は変えない。
- 新規ヘルパーは `DiffXL.VIEW.Controls.MiniMapViewportBand`。公開静的メソッドのみ。

---

## File Map

- Create: `20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapViewportBand.cs` — 数式
- Create: `20_ソース/DiffXL/_smoke/MiniMapViewportBandSmoke.cs` — 数式スモーク
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj` — Compile 追加
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapControl.xaml.cs` — 描画・ヒット・grabOffset
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs` — `GetVisibleFraction`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/WorkbookPane.xaml.cs` — `GetContentVisibleFraction`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs` — `PushMiniMapViewport`

---

### Task 1: MiniMapViewportBand 数式とスモーク

**Files:**
- Create: `20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapViewportBand.cs`
- Create: `20_ソース/DiffXL/_smoke/MiniMapViewportBandSmoke.cs`
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`（`MiniMapControl.xaml.cs` の直前に Compile を追加）

**Interfaces:**
- Consumes: なし
- Produces:
  - `public static class MiniMapViewportBand` in `DiffXL.VIEW.Controls`
  - `public const double MinHeightPx = 16`
  - `public const double LabelMinBandHeightPx = 22`
  - `public static double Clamp01(double value)`
  - `public static double VisibleFraction(double viewport, double extent)`
  - `public static double BandHeight(double bodyH, double visibleFraction)`
  - `public static double BandTop(double bodyTop, double bodyH, double bandH, double ratio)`
  - `public static bool HitTestThumb(double y, double bandTop, double bandH)`
  - `public static double RatioFromPointer(double pointerY, double grabOffset, double bodyTop, double bodyH, double bandH)`

- [ ] **Step 1: Write the failing smoke**

Create `20_ソース/DiffXL/_smoke/MiniMapViewportBandSmoke.cs`:

```csharp
using System;
using DiffXL.VIEW.Controls;

internal static class MiniMapViewportBandSmoke
{
    private static int _fails;

    private static void Expect(bool cond, string name)
    {
        if (cond)
        {
            Console.WriteLine("OK " + name);
        }
        else
        {
            Console.WriteLine("FAIL " + name);
            _fails++;
        }
    }

    private static void ExpectNear(double actual, double expected, string name)
    {
        Expect(Math.Abs(actual - expected) < 0.0001, name + " actual=" + actual);
    }

    private static int Main()
    {
        Console.WriteLine("MiniMapViewportBandSmoke");

        ExpectNear(MiniMapViewportBand.VisibleFraction(400, 400), 1, "fit-exact");
        ExpectNear(MiniMapViewportBand.VisibleFraction(500, 400), 1, "fit-larger-viewport");
        ExpectNear(MiniMapViewportBand.VisibleFraction(200, 400), 0.5, "half");
        ExpectNear(MiniMapViewportBand.VisibleFraction(0, 400), 1, "viewport-zero");
        ExpectNear(MiniMapViewportBand.VisibleFraction(20, 4000), 0.005, "tiny-fraction");

        ExpectNear(MiniMapViewportBand.BandHeight(400, 1), 400, "band-full");
        ExpectNear(MiniMapViewportBand.BandHeight(400, 0.5), 200, "band-half");
        ExpectNear(MiniMapViewportBand.BandHeight(400, 0.005), 16, "band-min-16");
        ExpectNear(MiniMapViewportBand.BandHeight(10, 0.005), 10, "band-cap-body");

        ExpectNear(MiniMapViewportBand.BandTop(0, 400, 80, 0), 0, "top-ratio0");
        ExpectNear(MiniMapViewportBand.BandTop(0, 400, 80, 1), 320, "top-ratio1");
        ExpectNear(MiniMapViewportBand.BandTop(0, 400, 400, 0.7), 0, "no-travel");

        Expect(MiniMapViewportBand.HitTestThumb(10, 10, 16), "hit-top-edge");
        Expect(MiniMapViewportBand.HitTestThumb(26, 10, 16), "hit-bottom-edge");
        Expect(!MiniMapViewportBand.HitTestThumb(9.9, 10, 16), "miss-above");
        Expect(!MiniMapViewportBand.HitTestThumb(26.1, 10, 16), "miss-below");

        ExpectNear(MiniMapViewportBand.RatioFromPointer(40, 40, 0, 400, 80), 0, "grab-at-top-no-jump");
        ExpectNear(MiniMapViewportBand.RatioFromPointer(200, 40, 0, 400, 80), 0.5, "grab-mid");
        ExpectNear(MiniMapViewportBand.RatioFromPointer(200, 40, 0, 400, 400), 0, "no-scroll-ratio");
        ExpectNear(MiniMapViewportBand.RatioFromPointer(40, 8, 0, 400, 16), 0.0833333, "track-center-near-top");

        if (_fails > 0)
        {
            Console.WriteLine("FAILED " + _fails);
            return 1;
        }

        Console.WriteLine("ALL PASS");
        return 0;
    }
}
```

- [ ] **Step 2: Confirm the smoke does not compile yet**

From `20_ソース/DiffXL`:

```powershell
$exe = "DiffXL\bin\x64\Debug\DiffXL.exe"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:exe /platform:x64 /r:$exe /out:_smoke\MiniMapViewportBandSmoke.exe _smoke\MiniMapViewportBandSmoke.cs
```

Expected: compile error, `MiniMapViewportBand` が存在しない。

- [ ] **Step 3: Implement MiniMapViewportBand and add it to the csproj**

Create `20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapViewportBand.cs`:

```csharp
using System;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// MiniMap 青帯の高さ・位置・ヒットの数式（WPF 非依存）。
    /// </summary>
    public static class MiniMapViewportBand
    {
        public const double MinHeightPx = 16;
        public const double LabelMinBandHeightPx = 22;

        public static double Clamp01(double value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 1)
            {
                return 1;
            }

            return value;
        }

        public static double VisibleFraction(double viewport, double extent)
        {
            if (viewport <= 0)
            {
                return 1;
            }

            if (extent <= viewport)
            {
                return 1;
            }

            return Clamp01(viewport / extent);
        }

        public static double BandHeight(double bodyH, double visibleFraction)
        {
            if (bodyH <= 0)
            {
                return 0;
            }

            double raw = Clamp01(visibleFraction) * bodyH;
            double h = Math.Max(MinHeightPx, raw);
            if (h > bodyH)
            {
                h = bodyH;
            }

            return h;
        }

        public static double BandTop(double bodyTop, double bodyH, double bandH, double ratio)
        {
            double travel = Math.Max(0, bodyH - bandH);
            return bodyTop + Clamp01(ratio) * travel;
        }

        public static bool HitTestThumb(double y, double bandTop, double bandH)
        {
            return y >= bandTop && y <= bandTop + bandH;
        }

        public static double RatioFromPointer(
            double pointerY,
            double grabOffset,
            double bodyTop,
            double bodyH,
            double bandH)
        {
            if (bodyH <= bandH)
            {
                return 0;
            }

            double travel = bodyH - bandH;
            return Clamp01((pointerY - grabOffset - bodyTop) / travel);
        }
    }
}
```

In `DiffXL.csproj`, immediately before the `MiniMapControl.xaml.cs` Compile item, insert:

```xml
    <Compile Include="VIEW\Controls\MiniMapViewportBand.cs" />
```

- [ ] **Step 4: Build DiffXL and run the smoke**

```powershell
msbuild DiffXL\DiffXL.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
$exe = "DiffXL\bin\x64\Debug\DiffXL.exe"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:exe /platform:x64 /r:$exe /out:_smoke\MiniMapViewportBandSmoke.exe _smoke\MiniMapViewportBandSmoke.cs
& _smoke\MiniMapViewportBandSmoke.exe
```

Expected: `ALL PASS`

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapViewportBand.cs" "20_ソース/DiffXL/DiffXL/DiffXL.csproj" "20_ソース/DiffXL/_smoke/MiniMapViewportBandSmoke.cs" docs/superpowers/specs/2026-08-15-minimap-viewport-band-design.md docs/superpowers/plans/2026-08-15-minimap-viewport-band.md
git commit -m "feat: MiniMap viewport band math and smoke"
```

---

### Task 2: MiniMapControl が比例帯＋掴み操作を使う

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapControl.xaml.cs`

**Interfaces:**
- Consumes: `MiniMapViewportBand.*`（Task 1）
- Produces:
  - `public void SetContentViewport(double ratio, double visibleFraction)`
  - 既存 `SetContentViewportRatio(double ratio)` は比率のみ更新し、高さは `_visibleFraction` を使う
  - フィールド `_visibleFraction`（初期値 1）、`_grabOffset`、`_lastBandTop`、`_lastBandH`

- [ ] **Step 1: Add state and SetContentViewport**

After `_contentViewportRatio`, add:

```csharp
private double _visibleFraction = 1;
private double _grabOffset;
private double _lastBandTop;
private double _lastBandH;
```

Replace `SetContentViewportRatio` body usage by adding:

```csharp
public void SetContentViewport(double ratio, double visibleFraction)
{
    _visibleFraction = MiniMapViewportBand.Clamp01(visibleFraction);
    SetContentViewportRatio(ratio);
}
```

Keep `SetContentViewportRatio` as the ratio setter that calls `UpdateViewportVisuals` / `UpdateHintText`.

- [ ] **Step 2: Draw the band from the helper**

In `UpdateViewportVisuals`, replace the fixed `bandH` / `y` block:

```csharp
double bodyTop = h * SheetHeaderRatio;
double bodyH = Math.Max(8, h * (1.0 - SheetHeaderRatio));
double bandH = MiniMapViewportBand.BandHeight(bodyH, _visibleFraction);
double y = MiniMapViewportBand.BandTop(bodyTop, bodyH, bandH, _contentViewportRatio);
_lastBandTop = y;
_lastBandH = bandH;
```

After updating `_viewportLabel.Text`, hide the in-band label when the band is too short:

```csharp
_viewportLabel.Visibility = bandH >= MiniMapViewportBand.LabelMinBandHeightPx
    ? Visibility.Visible
    : Visibility.Collapsed;
```

Do not change colors, stroke, or hint text.

- [ ] **Step 3: Replace PointToContentRatio and mouse-down grab**

Replace `PointToContentRatio` with:

```csharp
private double PointToContentRatio(Point p)
{
    double h = Math.Max(1, MapBorder.ActualHeight);
    double bodyTop = h * SheetHeaderRatio;
    double bodyH = Math.Max(1, h * (1.0 - SheetHeaderRatio));
    double bandH = MiniMapViewportBand.BandHeight(bodyH, _visibleFraction);
    return MiniMapViewportBand.RatioFromPointer(p.Y, _grabOffset, bodyTop, bodyH, bandH);
}
```

In `BeginScrub` callers, set grab **before** `RaiseNavigate`. Extract:

```csharp
private void CaptureGrab(Point p)
{
    double h = Math.Max(1, MapBorder.ActualHeight);
    double bodyTop = h * SheetHeaderRatio;
    double bodyH = Math.Max(1, h * (1.0 - SheetHeaderRatio));
    double bandH = MiniMapViewportBand.BandHeight(bodyH, _visibleFraction);
    double bandTop = MiniMapViewportBand.BandTop(bodyTop, bodyH, bandH, _contentViewportRatio);
    _lastBandTop = bandTop;
    _lastBandH = bandH;
    if (MiniMapViewportBand.HitTestThumb(p.Y, bandTop, bandH))
    {
        _grabOffset = p.Y - bandTop;
    }
    else
    {
        _grabOffset = bandH * 0.5;
    }
}
```

Call `CaptureGrab(p)` at the start of both MouseDown handlers (`MiniMapControl_PreviewMouseLeftButtonDown` and `MapBorder_PreviewMouseLeftButtonDown`) after the hit-area check and **before** `BeginScrub` / `RaiseNavigate`. Do not recompute grab on Move / Up.

- [ ] **Step 4: Build to confirm compile**

```powershell
msbuild DiffXL\DiffXL.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
```

Expected: 0 error.

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapControl.xaml.cs"
git commit -m "feat: MiniMap thumb scales with viewport and keeps grab offset"
```

---

### Task 3: ContentPane / WorkbookPane が可視比率を出す

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs`（`GetVerticalScrollRatio` の直後）
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/WorkbookPane.xaml.cs`（`GetContentScrollRatio` の直後）

**Interfaces:**
- Consumes: `MiniMapViewportBand.VisibleFraction`
- Produces:
  - `ContentPane.GetVisibleFraction(): double`
  - `WorkbookPane.GetContentVisibleFraction(): double`（ホストなしは `1`）

- [ ] **Step 1: Add GetVisibleFraction on ContentPane**

Insert after `GetVerticalScrollRatio`:

```csharp
/// <summary>
/// ビューポートが高さ全体に占める割合 0..1（スクロール不能なら 1）。
/// </summary>
public double GetVisibleFraction()
{
    if (StreamScroll == null)
    {
        return 1;
    }

    double viewport = StreamScroll.ViewportHeight;
    double extent = StreamScroll.ExtentHeight;
    if (extent <= 0.5 && _layout != null && _layout.TotalHeight > 1)
    {
        extent = _layout.TotalHeight;
    }

    return MiniMapViewportBand.VisibleFraction(viewport, extent);
}
```

- [ ] **Step 2: Add GetContentVisibleFraction on WorkbookPane**

Insert after `GetContentScrollRatio`:

```csharp
/// <summary>
/// 内容ビューの可視比率 0..1（MiniMap 青帯高さ用）。
/// </summary>
public double GetContentVisibleFraction()
{
    return ContentHost != null ? ContentHost.GetVisibleFraction() : 1;
}
```

- [ ] **Step 3: Build**

```powershell
msbuild DiffXL\DiffXL.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
```

Expected: 0 error.

- [ ] **Step 4: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs" "20_ソース/DiffXL/DiffXL/VIEW/Controls/WorkbookPane.xaml.cs"
git commit -m "feat: expose content visible fraction for MiniMap thumb"
```

---

### Task 4: MainWindow が比率と可視比率を一緒に渡す

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `WorkbookPane.GetContentScrollRatio()`, `WorkbookPane.GetContentVisibleFraction()`, `MiniMapControl.SetContentViewport(double, double)`, `MiniMapControl.SetContentViewportRatio(double)`
- Produces: `private void PushMiniMapViewport()`

- [ ] **Step 1: Add PushMiniMapViewport**

Near the other MiniMap helpers (around `OnLeftContentScrollRatioChanged`):

```csharp
private void PushMiniMapViewport()
{
    if (MiniMap == null)
    {
        return;
    }

    double ratio = 0;
    double fraction = 1;
    if (LeftPane != null)
    {
        ratio = LeftPane.GetContentScrollRatio();
        fraction = LeftPane.GetContentVisibleFraction();
    }
    else if (RightPane != null)
    {
        ratio = RightPane.GetContentScrollRatio();
        fraction = RightPane.GetContentVisibleFraction();
    }

    MiniMap.SetContentViewport(ratio, fraction);
}
```

- [ ] **Step 2: Replace content-origin SetContentViewportRatio calls**

Replace these with `PushMiniMapViewport()`:

- `OnLeftContentScrollRatioChanged` 内の 2 箇所（相手が近い早期 return、および同期後）
- `OnRightContentScrollRatioChanged` 内の 2 箇所
- 比較完了／シート反映で `MiniMap.SetContentViewportRatio(scrollRatio)` している箇所（`SetCurrentSheet` の直後）

Keep `SetContentViewportRatio` **only** on the MiniMap-driven path:

- `OnMiniMapNavigate`（ドラッグ中の青帯即時）
- `ApplyMiniMapTarget` 内の青帯再設定

After `OnMiniMapScrubEnded` → `ApplyMiniMapTarget(..., ScrubEnd)` returns, call `PushMiniMapViewport()` so height refreshes if extent settled.

- [ ] **Step 3: Build**

```powershell
msbuild DiffXL\DiffXL.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
```

Expected: 0 error.

- [ ] **Step 4: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs"
git commit -m "feat: push MiniMap thumb height from content viewport"
```

---

### Task 5: 検証

**Files:**
- Test: `20_ソース/DiffXL/_smoke/MiniMapViewportBandSmoke.cs`

- [ ] **Step 1: Rebuild and rerun math smoke**

```powershell
msbuild DiffXL\DiffXL.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
$exe = "DiffXL\bin\x64\Debug\DiffXL.exe"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:exe /platform:x64 /r:$exe /out:_smoke\MiniMapViewportBandSmoke.exe _smoke\MiniMapViewportBandSmoke.cs
& _smoke\MiniMapViewportBandSmoke.exe
```

Expected: `ALL PASS`

- [ ] **Step 2: Manual check (same session if UI available)**

- 短いシート: 青帯がマップの大部分
- `stress_suite` 長大一覧: 青帯が細く、16px 未満にならない
- 帯を掴んでドラッグ: 相対位置が跳ねない
- 帯の外クリック: 中心がそこへジャンプ
- ウィンドウを縦にリサイズ: 帯の高さが変わる
- 本文ホイール: 帯が追従（スクラブ退行なし）

- [ ] **Step 3: Commit remaining docs if needed**

```bash
git add docs/superpowers/specs/2026-08-15-minimap-viewport-band-design.md docs/superpowers/plans/2026-08-15-minimap-viewport-band.md
git commit -m "docs: MiniMap viewport band spec and plan"
```
