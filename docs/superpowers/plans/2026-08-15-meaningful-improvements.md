# Meaningful Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 엔진·화면·문서가 같은 “같다”를 말하고, 표가 덜 거짓말하며, 차분을 키보드로 찾고, 베타를 배포할 수 있게 한다.

**Architecture:** 내용 베이스는 유지한다. 정렬의 정본은 `ContentStreamPair` 인덱스다. `DiffItem.StreamPairIndex`가 리스트·MiniMap·하이라이트를 묶는다. 표는 Excel 표(`xl/tables`)를 우선하고, 행 유사도는 빈 칸을 일치로 세지 않는다. COM 유령과 구 QA 문구는 삭제한다.

**Tech Stack:** .NET Framework 4.8 / WPF / 기존 `_smoke` + `csc /r:DiffXL.exe` / GitHub Actions `windows-latest`

**Spec:** `docs/superpowers/specs/2026-08-15-meaningful-improvements-roadmap.md`

## Global Constraints

- 내용 베이스（위치 무시）를 폐기하지 않는다. Excel COM / 1px 재현을 되돌리지 않는다.
- `.xls` / 수식 토글 / 셀 이동 검출 / 한국어 UI / MainWindow MVVM 전면 분해는 이 계획 밖이다.
- 소스 주석은 일본어（기존 규칙）. UI 문자열은 일본어.
- 스모크는 `20_ソース/DiffXL/DiffXL/bin/x64/Debug`에서 실행한다（어셈블리 해석）.
- 페이즈 순서 고정: H0 → H1 → H2 → H3 → H4. H3를 H1보다 먼저 하지 않는다.
- 라이선스는 **MIT**（앱 본문）. OSS 안내는 `30_参考資料/licenses/README.md`를 가리킨다.

**MSBuild / smoke 공통 명령**（모든 Task의 빌드·스모크에 사용）:

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$root = "C:\JUN\WORK\DiffXL"
$proj = "$root\20_ソース\DiffXL\DiffXL\DiffXL.csproj"
$bin = "$root\20_ソース\DiffXL\DiffXL\bin\x64\Debug"
$exe = "$bin\DiffXL.exe"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$smoke = "$root\20_ソース\DiffXL\_smoke"
function Build-DiffXL { & $msbuild $proj /p:Configuration=Debug /p:Platform=x64 /v:m }
function Invoke-Smoke([string]$name) {
  & $csc /nologo /target:exe /platform:x64 /r:$exe /out:"$bin\$name.exe" "$smoke\$name.cs"
  if ($LASTEXITCODE -ne 0) { throw "csc $name failed" }
  Push-Location $bin
  try { & ".\$name.exe"; if ($LASTEXITCODE -ne 0) { throw "$name failed" } }
  finally { Pop-Location }
}
```

---

## File Map

| 파일 | 역할 |
|------|------|
| `LICENSE` | 앱 MIT |
| `README.md` / `VERSION` / `CHANGELOG.md` | 정직한 제품 설명·버전 |
| `10_管理資料/要件定義.md` | V-01d 스트림, N-04 구현 후 갱신 |
| `10_管理資料/テスト/テストケース一覧.md` | 현행 앱 게이트 |
| `20_ソース/DiffXL/_smoke/README.md` | COM/Excel 전제 삭제 |
| `20_ソース/DiffXL/_smoke/SheetMatcherSmoke.cs` | 시트 매칭 |
| `20_ソース/DiffXL/_smoke/StreamPairLinkSmoke.cs` | 엔진=화면 pair |
| `20_ソース/DiffXL/_smoke/TableTruthSmoke.cs` | 표 열·유사도·xl/tables |
| `LOGIC/Diff/SheetMatcher.cs` | 수동 짝 + 잔여 동명 |
| `LOGIC/Diff/DiffModels.cs` | `StreamPairIndex` |
| `LOGIC/Diff/ContentStreamBuilder.cs` | pair 인덱스, 이미지 임계 통일 |
| `LOGIC/Diff/DiffResultLinker.cs` | DiffItem → pair 부착 |
| `LOGIC/Diff/TableDetector.cs` / `TableCompareService.cs` / `TableRowAligner.cs` | 표 정직함 |
| `LOGIC/Diff/XlsxPackageReader.cs` / `ContentModels.cs` | Excel 표 범위 |
| `VIEW/MainWindow.xaml(.cs)` | 유령 버튼 삭제, F8, 필터, 시트 한 줄 |
| `VIEW/Controls/MiniMapControl.xaml(.cs)` | 마커 클릭, 제목 |
| `VIEW/Controls/ContentPane.xaml.cs` | StreamPairIndex 점프, 종류 필터 |
| `VIEW/StartupPanel.xaml` | 한 문장 |
| `.github/workflows/smokes.yml` | CI |
| `10_管理資料/テスト/run-logic-smokes.ps1` | 로컬·CI 공통 |

---

### Task 1: H0 — LICENSE와 README 정직함

**Files:**
- Create: `LICENSE`
- Modify: `README.md`（「なにができるか」직후, 동작환경 표）
- Modify: `CHANGELOG.md` Unreleased
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/StartupPanel.xaml`（부제 Text）

**Interfaces:**
- Consumes: 로드맵 §0 한 문장
- Produces: 사용자에게 보이는 동일 문구（일본어）

- [ ] **Step 1: Add MIT LICENSE**

Create `LICENSE` with the standard MIT text. Copyright line: `Copyright (c) 2026 DiffXL contributors`.

- [ ] **Step 2: Put the product sentence on Startup and README**

`StartupPanel.xaml` 부제（현재 「比較する 2 つの Excel ファイル（.xlsx）を選択してください。」）를 다음 두 줄로 교체:

```xml
<TextBlock Margin="0,0,0,8" Foreground="{StaticResource Brush.TextMuted}" TextWrapping="Wrap"
    Text="比較する 2 つの .xlsx を選んでください。Excel のインストールは不要です。" />
<TextBlock Margin="0,0,0,20" Foreground="{StaticResource Brush.TextMuted}" TextWrapping="Wrap" FontSize="12"
    Text="画面は Excel を再現しません。同じ内容が同じ個数あれば一致、表は枠（または Excel 表）の行、画像は出現順と見た目で合わせます。数式・マクロ・ピボット・チャートは見ません。" />
```

`README.md` 「なにができるか」표 앞에 같은 문단을 넣고, 「WinMerge 的」은 조작감만이라고 한정한다. 「単一 exe」행은 다음으로 바꾼다:

```
| 配布 | Release で Costura 埋め込みを確認できた場合のみ「原則 1 exe」。横に DLL があるビルドではその旨を書く |
```

- [ ] **Step 3: Verify the sentence exists**

```powershell
Select-String -Path README.md, "20_ソース\DiffXL\DiffXL\VIEW\StartupPanel.xaml" -Pattern "数式・マクロ" | Measure-Object | Select-Object -ExpandProperty Count
```

Expected: 2 이상.

- [ ] **Step 4: Commit**

```bash
git add LICENSE README.md CHANGELOG.md "20_ソース/DiffXL/DiffXL/VIEW/StartupPanel.xaml"
git commit -m "docs: state what DiffXL considers the same"
```

---

### Task 2: H0 — 요건·테스트 게이트를 현행 앱에 맞춘다

**Files:**
- Modify: `10_管理資料/要件定義.md`（V-01d, N-04, 화면 스케치 탭）
- Modify: `10_管理資料/テスト/テストケース一覧.md`
- Modify: `10_管理資料/計画/06_リリース配布_検証.md`（「Excel 必須」「左右 Excel 表示」）
- Modify: `20_ソース/DiffXL/_smoke/README.md`

**Interfaces:**
- Consumes: Task 1 제품 문장
- Produces: 게이트 문서가 COM/탭/앵커를 요구하지 않음

- [ ] **Step 1: Rewrite V-01d and N-04**

`要件定義.md` V-01d를 다음으로 교체:

```
| V-01d | 内容ストリーム | セル／表／画像／図形を文書順の 1 本のストリームで表示する。種別はフィルタで絞る（タブ分割はしない） |
```

N-04 행 끝에 `（H3 で実装。未実装の間は MiniMap とリストで移動）`를 붙인다.

화면 스케치의 「図形 タブ」는 「内容ストリーム」으로 바꾼다.

- [ ] **Step 2: Rewrite the live test-case table**

`テストケース一覧.md` TC-03 / TC-04 / TC-06 / TC-12 / TC-15 / TC-19를 다음 기대로 고친다:

| ID | 기대（신） |
|----|------------|
| TC-03 | 내용 스트림이 좌우에 나오고 MiniMap에 차분. Excel 창 없음 |
| TC-04 | MiniMap 클릭으로 본문 스크롤. 상태줄은 비율 또는 pair |
| TC-06 | 툴바 시트 페어가 좌우를 같이 바꿈. 페인 콤보는 H3까지 「이 칸만」이거나 페어를 따름 |
| TC-12 | **삭제**（앵커 다이얼로그 없음）. 번호를 비우거나 「廃止」 |
| TC-15 | 왼쪽 **내용 뷰** 휠 → MiniMap 청대 추종 |
| TC-19 | 창 리사이즈 → 내용 뷰가 영역을 채움 |

- [ ] **Step 3: Fix smoke README and release plan 06**

`_smoke/README.md` 전문을 다음으로 교체:

```markdown
# DiffXL smoke harnesses

These tools reference types inside the built `DiffXL.exe` (`DiffEngine`, `ContentStreamBuilder`, `AppSettings`).

## Prerequisites

- Built `DiffXL\bin\x64\Debug\DiffXL.exe`
- Microsoft Excel is **not** required

## Logic smokes (current)

Compile with `csc /r:DiffXL.exe` and run from the Debug output directory.

- `ContentDiffSmoke` — content-based compare scenarios
- `ContentStreamSmoke` — stream align + layout
- `MiniMapViewportBandSmoke` — MiniMap thumb math
- `ImageOverlayAlignSmoke` — overlay aligner

Excel COM embed smokes (`SmokeGoto`, `SmokeEmbedGoto`, `DualEmbed`) are **retired**. Do not compile them.
```

`06_リリース配布_検証.md`에서 「Excel 必須」와 「左右に Excel 表示」를 「内容ビュー表示（Excel 不要）」로 치환한다.

- [ ] **Step 4: Grep the ghosts out of live gates**

```powershell
Select-String -Path "10_管理資料\テスト\テストケース一覧.md","10_管理資料\計画\06_リリース配布_検証.md","20_ソース\DiffXL\_smoke\README.md" -Pattern "左右 Excel|ExcelWorkbookSession|アンカーダイアログ|Excel 必須"
```

Expected: 매치 0（역사 계획 01–05는 손대지 않음）.

- [ ] **Step 5: Commit**

```bash
git add "10_管理資料/要件定義.md" "10_管理資料/テスト/テストケース一覧.md" "10_管理資料/計画/06_リリース配布_検証.md" "20_ソース/DiffXL/_smoke/README.md"
git commit -m "docs: align requirements and QA gates with content view"
```

---

### Task 3: H0 — COM 유령 UI·스모크 제거

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml`（`BtnImageLink` 블록 삭제）
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs`（`BtnImageLink_Click` 삭제）
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`（`DiffOverlayLayer` Page/Compile 삭제）
- Delete: `20_ソース/DiffXL/DiffXL/VIEW/Controls/DiffOverlayLayer.xaml`
- Delete: `20_ソース/DiffXL/DiffXL/VIEW/Controls/DiffOverlayLayer.xaml.cs`
- Delete: `20_ソース/DiffXL/_smoke/SmokeGoto.cs`, `SmokeEmbedGoto.cs`, `DualEmbed.cs`

**Interfaces:**
- Consumes: 없음
- Produces: 솔루션이 임베드 타입 없이 빌드됨

- [ ] **Step 1: Confirm ghosts**

```powershell
Select-String -Path "20_ソース\DiffXL\DiffXL\VIEW\MainWindow.xaml" -Pattern "BtnImageLink"
Select-String -Path "20_ソース\DiffXL\DiffXL\DiffXL.csproj" -Pattern "DiffOverlayLayer"
```

Expected: 현재는 매치 있음.

- [ ] **Step 2: Remove the button, handler, overlay control, and retired smokes**

`MainWindow.xaml` 95–109행 `BtnImageLink` 전체 삭제.  
`MainWindow.xaml.cs` `BtnImageLink_Click` 메서드 전체 삭제.  
csproj에서 `DiffOverlayLayer.xaml` Page와 `.xaml.cs` Compile 삭제 후 두 파일 삭제.  
세 COM 스모크 `.cs` 삭제（`.exe`는 gitignore）.

- [ ] **Step 3: Build**

```powershell
Build-DiffXL
```

Expected: 0 error. `BtnImageLink` / `ExcelHostControl` 미해결 없음.

- [ ] **Step 4: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml" "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs" "20_ソース/DiffXL/DiffXL/DiffXL.csproj"
git add -u "20_ソース/DiffXL/DiffXL/VIEW/Controls/DiffOverlayLayer.xaml" "20_ソース/DiffXL/DiffXL/VIEW/Controls/DiffOverlayLayer.xaml.cs" "20_ソース/DiffXL/_smoke/SmokeGoto.cs" "20_ソース/DiffXL/_smoke/SmokeEmbedGoto.cs" "20_ソース/DiffXL/_smoke/DualEmbed.cs"
git commit -m "chore: remove COM embed ghosts and unused image-link button"
```

---

### Task 4: H1 — 수동 시트 짝이 동명 자동 매칭을 끄지 않게

**Files:**
- Create: `20_ソース/DiffXL/_smoke/SheetMatcherSmoke.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/SheetMatcher.cs`（수동 루프 뒤 `return` 전에 잔여 동명 매칭）

**Interfaces:**
- Consumes: `SheetMatcher.Match(left, right, manual)`
- Produces: 수동에 안 쓰인 동명 시트는 `result.Pairs`에 `IsManual=false`로 추가. LeftOnly/RightOnly에는 안 남음

- [ ] **Step 1: Write SheetMatcherSmoke**

```csharp
using System;
using System.Collections.Generic;
using DiffXL.LOGIC.Diff;

internal static class SheetMatcherSmoke
{
    private static int _fails;
    private static void Expect(bool c, string n)
    {
        Console.WriteLine((c ? "OK " : "FAIL ") + n);
        if (!c) { _fails++; }
    }

    private static int Main()
    {
        var manual = new List<SheetPair>
        {
            new SheetPair { LeftSheet = "Cover", RightSheet = "表紙", IsManual = true }
        };
        SheetMatchResult r = SheetMatcher.Match(
            new[] { "Cover", "Data" },
            new[] { "表紙", "Data" },
            manual);
        Expect(r.Pairs.Count == 2, "pairs=2");
        Expect(r.Pairs.Exists(p => p.IsManual
            && p.LeftSheet == "Cover" && p.RightSheet == "表紙"), "manual cover");
        Expect(r.Pairs.Exists(p => !p.IsManual
            && string.Equals(p.LeftSheet, "Data", StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.RightSheet, "Data", StringComparison.OrdinalIgnoreCase)), "auto data");
        Expect(r.LeftOnlySheets.Count == 0 && r.RightOnlySheets.Count == 0, "no leftovers");

        SheetMatchResult none = SheetMatcher.Match(
            new[] { "A" }, new[] { "A" }, null);
        Expect(none.Pairs.Count == 1 && !none.Pairs[0].IsManual, "null manual still autos");

        Console.WriteLine(_fails == 0 ? "ALL PASS" : "FAILED " + _fails);
        return _fails == 0 ? 0 : 1;
    }
}
```

- [ ] **Step 2: Compile and run — must FAIL**（Data가 LeftOnly/RightOnly）

```powershell
Build-DiffXL
Invoke-Smoke SheetMatcherSmoke
```

Expected: `FAIL pairs=2` 또는 `FAIL auto data`.

- [ ] **Step 3: Match leftovers by name after manuals**

`SheetMatcher.cs` 수동 분기에서 `result.LeftOnlySheets.AddRange(...)` / `RightOnly` / `return`을 다음으로 교체한다. 수동으로 `used*`에 넣은 시트는 건너뛰고, 남은 이름은 기존 자동 루프와 동일하게 짝짓는다.

```csharp
foreach (string name in left)
{
    if (usedLeft.Contains(name))
    {
        continue;
    }

    string match = right.FirstOrDefault(r =>
        !usedRight.Contains(r)
        && string.Equals(r, name, StringComparison.OrdinalIgnoreCase));
    if (match != null)
    {
        result.Pairs.Add(new SheetPair
        {
            LeftSheet = name,
            RightSheet = match,
            IsManual = false
        });
        usedLeft.Add(name);
        usedRight.Add(match);
    }
    else
    {
        result.LeftOnlySheets.Add(name);
        usedLeft.Add(name);
    }
}

foreach (string name in right)
{
    if (!usedRight.Contains(name))
    {
        result.RightOnlySheets.Add(name);
    }
}

return result;
```

기존 `AddRange(left.Where(!usedLeft))`는 넣지 않는다（그러면 Data가 Only로 남음）.

- [ ] **Step 4: Re-run smoke**

```powershell
Build-DiffXL
Invoke-Smoke SheetMatcherSmoke
```

Expected: `ALL PASS`

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/LOGIC/Diff/SheetMatcher.cs" "20_ソース/DiffXL/_smoke/SheetMatcherSmoke.cs"
git commit -m "fix: keep same-name sheet auto-match after one manual pair"
```

---

### Task 5: H1 — StreamPairIndex를 모델에 넣고 링커 스모크를 먼저 쓴다

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs`（`DiffItem`에 속성 추가）
- Create: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffResultLinker.cs`
- Create: `20_ソース/DiffXL/_smoke/StreamPairLinkSmoke.cs`
- Modify: `20_ソース/DiffXL/DiffXL/DiffXL.csproj`（`DiffResultLinker.cs` Compile. `MiniMapViewportBand.cs` 근처에 추가）

**Interfaces:**
- Consumes: `DiffItem`, `ContentStreamLayout` / `IList<ContentStreamPair>`
- Produces:
  - `DiffItem.StreamPairIndex` (`int`, 미부착은 `-1`)
  - `public static class DiffResultLinker`
  - `public static void Attach(DiffResult result, IList<ContentStreamPair> pairs)`
  - `public static int CountUnlinkedContentItems(DiffResult result)` — Structure 제외, `StreamPairIndex < 0` 개수

- [ ] **Step 1: Write StreamPairLinkSmoke that needs Attach**

시나리오 A: 좌우 loose 셀 `Hello`(A1) / `Hello`(Z9) — 스트림 1 Match, 차분 0건, unlinked 0.  
시나리오 B: 좌 `検証: L10` / 우 `検証: 挿入行` 를 `ContentStreamBuilder.Align`이 Match로 묶는 경우 — `DiffEngine` 또는 수작업 Text 편측 2건을 Attach한 뒤 **같은 `StreamPairIndex`** 를 갖고, `CountUnlinkedContentItems == 0`.  
시나리오 C: 인덱스가 없으면 Attach 전 `StreamPairIndex == -1`.

`ContentStreamSmoke`의 검증 노트 블록 구성（`ContentStreamSmoke.cs` 124행 근처）을 재사용한다.

```csharp
// 핵심 단언
DiffResultLinker.Attach(result, pairs);
Expect(leftNote.StreamPairIndex == rightNote.StreamPairIndex
    && leftNote.StreamPairIndex >= 0, "same pair");
Expect(DiffResultLinker.CountUnlinkedContentItems(result) == 0, "all linked");
```

전문은 `ContentStreamSmoke`와 같은 `Expect` 패턴으로 200줄 이내.

- [ ] **Step 2: Run — compile fail or FAIL unlinked**

`DiffResultLinker`가 없으면 csc 실패가 올바른 실패다.

- [ ] **Step 3: Add StreamPairIndex and DiffResultLinker**

`DiffItem`에:

```csharp
/// <summary>内容ストリームのペア index。未割当は -1。</summary>
public int StreamPairIndex { get; set; } = -1;
```

`DiffResultLinker.Attach`:

1. `result` / `pairs` null이면 return.
2. 각 `DiffItem`（`Kind == Structure`는 건너뜀）에 대해:
   - 이미지: `ContentPane`의 `BlockMatchesImage`와 같은 경로 — `LeftImagePath`/`RightImagePath` 또는 파일명이 `ContentStreamBlock.Image`와 일치하는 첫 pair.
   - 표: `TableIdLeft`/`TableIdRight` + `RowIndexLeft`/`RowIndexRight`가 `TableRow` 블록과 일치하는 pair. 없으면 같은 TableId의 `TableHeader`.
   - 그 외: `AddressLeft`/`AddressRight`의 행이 `LooseRow` 블록 행과 일치. 없으면 `OrderHint`가 가장 가까운 pair.
3. 같은 pair에 **편측 Text 두 건**만 있으면 둘 다 그 index를 넣는다（병합은 Task 6）.

`CountUnlinkedContentItems`: `Kind != Structure && StreamPairIndex < 0`.

- [ ] **Step 4: Run smoke**

```powershell
Build-DiffXL
Invoke-Smoke StreamPairLinkSmoke
```

Expected: `ALL PASS`（시나리오 B는 주소 매칭만으로도 통과 가능）.

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffModels.cs" "20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffResultLinker.cs" "20_ソース/DiffXL/DiffXL/DiffXL.csproj" "20_ソース/DiffXL/_smoke/StreamPairLinkSmoke.cs"
git commit -m "feat: attach DiffItem to content-stream pair index"
```

---

### Task 6: H1 — 비교 직후 Attach하고, 같은 pair의 편측 Text를 하나의 변경으로 본다

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffEngine.cs`（`Compare` 반환 직전, 레이아웃이 있으면 Attach. 레이아웃이 엔진 밖에 있으면 `CompareSession` / `MainWindow` 비교 완료 경로）
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/CompareSession.cs` 또는 비교를 끝내는 `MainWindow` 메서드
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs` `FindPairIndexForDiffItem` — `item.StreamPairIndex >= 0`이면 그 인덱스를 **최우선**
- Modify: `20_ソース/DiffXL/_smoke/StreamPairLinkSmoke.cs` — 「같은 pair 편측 2건 → 호출 후 1건의 Text（또는 둘 다 동일 index + Summary가 대응을 말함）」

**Interfaces:**
- Consumes: `DiffResultLinker.Attach`
- Produces: `DiffResultLinker.MergeOneSidedTextsOnSamePair(DiffResult result)`  
  같은 `StreamPairIndex`에 `DiffKind.Text`가 왼쪽만 주소 / 오른쪽만 주소로 2건이면 1건으로 합친다. `AddressLeft`+`AddressRight`를 채우고 Summary는 `テキスト変更` + 양쪽 40자.

비교 완료 후 호출 순서: 스트림 레이아웃 구축 → `Attach` → `MergeOneSidedTextsOnSamePair`.

`CompareSession`에 레이아웃이 있으면 거기서 호출. 없으면 `MainWindow`에서 `ContentStreamBuilder.GetOrBuildLayout` 직후:

```csharp
DiffResultLinker.Attach(result, layout.Pairs);
DiffResultLinker.MergeOneSidedTextsOnSamePair(result);
```

- [ ] **Step 1: Extend StreamPairLinkSmoke with merge**

같은 pair에 Text 2건을 넣고 `MergeOneSidedTextsOnSamePair` 후 `Items`의 해당 pair Text가 1건, 양쪽 Address가 비지 않음.

- [ ] **Step 2: Run — FAIL merge not found**

- [ ] **Step 3: Implement merge + call sites + FindPairIndexForDiffItem priority**

```csharp
if (item.StreamPairIndex >= 0 && item.StreamPairIndex < _pairs.Count)
{
    return item.StreamPairIndex;
}
```

기존 휴리스틱은 fallback.

- [ ] **Step 4: Run StreamPairLinkSmoke + ContentStreamSmoke + ContentDiffSmoke**

```powershell
Build-DiffXL
Invoke-Smoke StreamPairLinkSmoke
Invoke-Smoke ContentStreamSmoke
Invoke-Smoke ContentDiffSmoke
```

Expected: 전부 PASS. ContentDiffSmoke가 깨지면 병합이 Structure/Image를 건드린 것 — Text만 병합하도록 고친다.

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffResultLinker.cs" "20_ソース/DiffXL/DiffXL/LOGIC/CompareSession.cs" "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs" "20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs" "20_ソース/DiffXL/_smoke/StreamPairLinkSmoke.cs"
git commit -m "feat: resolve and merge diffs onto one stream pair"
```

`CompareSession.cs`를 안 고쳤으면 add하지 않는다.

---

### Task 7: H1 — 이미지 Match 임계를 스트림과 한 숫자로

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/COMMON/AppSettings.cs` — `ImageRejectDiffRatio` 기본값 `0.85` → `0.45`（유사도 하한 0.55）
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ImageCorrespondenceService.cs` — `RejectDiffRatio = 0.45`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentStreamBuilder.cs` `ImageSimilarity` — `[alignMin,1]→[0.55,1]` 스케일 **삭제**. `ComputeSimilarity`를 그대로 쓰고, `sim < MatchThreshold(0.55)`면 그 값（Match 안 됨）
- Modify: `20_ソース/DiffXL/_smoke/SettingsSmoke.cs` — 기본/복구 값이 `0.85`를 가정하면 `0.45`로

**Interfaces:**
- Consumes: `ContentStreamBuilder.MatchThreshold`（0.55）
- Produces: 엔진과 스트림이 같은 이미지에 대해 둘 다 Match 또는 둘 다 Skip

- [ ] **Step 1: Add assertion to StreamPairLinkSmoke or ImageSequenceSmoke**

유사도 0.20인 페어는 `ImageSequenceAligner`와 `ContentStreamBuilder.Align` 모두 Skip.  
해시 동일 페어는 둘 다 Match.

기존 `ImageSequenceSmoke`가 있으면 단언을 추가한다. 없으면 `StreamPairLinkSmoke`에 한 케이스.

- [ ] **Step 2: Run — likely FAIL**（스케일 때문에 0.20이 스트림 Match）

- [ ] **Step 3: Change defaults and delete the scale-up block**（`ContentStreamBuilder.cs` 1435–1471을 `return sim;`으로）

- [ ] **Step 4: Run ImageSequenceSmoke（있으면）+ ContentStreamSmoke + SettingsSmoke**

`SettingsSmoke`가 `0.85` 복구를 쓰면 그 리터럴을 `0.45`로.

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/COMMON/AppSettings.cs" "20_ソース/DiffXL/DiffXL/LOGIC/Diff/ImageCorrespondenceService.cs" "20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentStreamBuilder.cs" "20_ソース/DiffXL/_smoke"
git commit -m "fix: use one image match threshold for engine and stream"
```

---

### Task 8: H2 — 행 유사도가 빈 칸·1칸 일치를 Match로 만들지 않게

**Files:**
- Create: `20_ソース/DiffXL/_smoke/TableTruthSmoke.cs`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/TableRowAligner.cs` `RowSimilarity`

**Interfaces:**
- Consumes: `TableRowAligner.AlignRows`
- Produces: `RowSimilarity`가 (1) 양쪽 비어 있는 칸을 equal에 넣지 않음 (2) 비어 있지 않은 비교 칸이 2 미만이면 유사도 0 (3) 그 외 `equalNonEmpty / max(nonEmptyLeft, nonEmptyRight)`

- [ ] **Step 1: Write TableTruthSmoke cases**

```csharp
// 2열: [1,A] vs [2,A] → AlignRows 결과가 Match 가 아님（Skip 쌍）
// 3열: [1,A,x] vs [1,A,y] → Match（비공 2칸 일치）
// 빈칸 패딩: [1,"",""] vs [1,"",""] → 비공 1칸이면 Match 금지
```

`AlignStep` / `AlignOp` 기존 enum을 쓴다.

- [ ] **Step 2: Run — FAIL**（현재 `[1,A]` vs `[2,A]` sim=0.5 → Match）

- [ ] **Step 3: Replace RowSimilarity body**

```csharp
int compared = 0;
int equal = 0;
int maxCols = Math.Max(leftLen, rightLen);
for (int c = 0; c < maxCols; c++)
{
    string lt = c < leftLen ? GetText(leftRow[c]) : string.Empty;
    string rt = c < rightLen ? GetText(rightRow[c]) : string.Empty;
    bool le = string.IsNullOrEmpty(lt);
    bool re = string.IsNullOrEmpty(rt);
    if (le && re)
    {
        continue;
    }

    compared++;
    if (!le && !re && string.Equals(lt, rt, StringComparison.Ordinal))
    {
        equal++;
    }
}

if (compared < 2)
{
    return 0;
}

return (double)equal / compared;
```

완전 키 일치 분기는 그대로 1.0.

- [ ] **Step 4: Run TableTruthSmoke + 기존 TableRowDiffSmoke**（`12345` vs `1245` 유지）

```powershell
Build-DiffXL
Invoke-Smoke TableTruthSmoke
Invoke-Smoke TableRowDiffSmoke
```

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/LOGIC/Diff/TableRowAligner.cs" "20_ソース/DiffXL/_smoke/TableTruthSmoke.cs"
git commit -m "fix: do not match table rows on one shared cell or blanks"
```

---

### Task 9: H2 — 여분 열을 차분으로 남긴다

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/TableCompareService.cs` `EmitCellChanges`
- Modify: `20_ソース/DiffXL/_smoke/TableTruthSmoke.cs`

**Interfaces:**
- Consumes: `DiffKind.TableCellChange`
- Produces: `n = Max(leftLen, rightLen)`. 한쪽만 있는 칸은 빈 문자열 vs 텍스트로 `TableCellChange` 1건

- [ ] **Step 1: Add smoke**

왼쪽 행 `A B C`, 오른쪽 `A B C NEW` → `TableCellChange` ≥ 1, Summary/Address가 4번째 열을 가리킴.

`TableCompareService.Compare` 또는 `Emit`이 private이면 공개 `Compare`로 두 표（1행）를 넣는다.

- [ ] **Step 2: Run — FAIL**（0 cell changes）

- [ ] **Step 3: Change `int n = Math.Min` to `Math.Max` and null-safe GetText**

없는 쪽 `Address`는 null, Text는 `""`. 기존 「같으면 continue」유지. 배경만의 차는 계속 무시. README/표 헤더에 「表内の塗り分け（zebra）は差分にしない」한 줄을 `CHANGELOG` Unreleased에 적는다.

- [ ] **Step 4: Run TableTruthSmoke + TableRowDiffSmoke**

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/LOGIC/Diff/TableCompareService.cs" "20_ソース/DiffXL/_smoke/TableTruthSmoke.cs" CHANGELOG.md
git commit -m "fix: emit table cell change for extra columns"
```

---

### Task 10: H2 — Excel 표(`xl/tables`)를 테두리 flood보다 우선

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentModels.cs` — `TableBlock.DetectionSource` (`string`: `"ExcelTable"` / `"Border"`)
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/XlsxPackageReader.cs` — 시트별 `List<string>` 범위（`A1:D10`）추출
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/TableDetector.cs` — `Detect(cells, definedRanges)`
- Modify: `20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffEngine.cs` — Detect 호출에 범위 전달
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/TableDiffGrid.xaml.cs` 또는 표 헤더 생성부 — 툴팁 `検出: Excel 表` / `検出: 罫線`
- Modify: `20_ソース/DiffXL/_smoke/TableTruthSmoke.cs` / `TableDetectorSmoke.cs`

**Interfaces:**
- Consumes: OOXML `xl/tables/table*.xml` 의 `ref` / `displayName`
- Produces: `XlsxPackageReader.GetDefinedTableRefs(string sheetName)` → `IList<string>` A1 범위  
  `TableDetector.Detect(IList<CellContent> cells, IList<string> definedRefsOrNull)`  
  defined ref가 유효하면 그 bbox를 표로 만들고 `DetectionSource = "ExcelTable"`. 남은 칸만 기존 테두리 flood (`"Border"`).

범위 파서: 기존 `CellRefRegex`와 같은 `A1:D10` → (r1,c1,r2,c2). `XlsxPackageReader`에 `TryParseA1Range`를 public static으로 둔다.

- [ ] **Step 1: Smoke defined range wins**

셀 9개가 테두리 없이 3×3, defined `"B2:D4"` → 표 1개, `DetectionSource=="ExcelTable"`, Loose는 그 밖.

테두리만 있는 3×3（ref 없음）→ `"Border"`（기존 `TableDetectorSmoke` 유지）.

- [ ] **Step 2: Run — FAIL**（Detect가 ref를 모름）

- [ ] **Step 3: Implement parse + Detect overload + reader**

`table*.xml` 읽기: 시트 관계에서 `tableParts` → `xl/tables/tableN.xml` → attribute `ref`. 실패해도 예외를 삼키고 빈 리스트（기존 추출과 같은 방어）.

헤더 UI: `TableBlock.DetectionSource`를 `TableHeader` 툴팁에 `検出: Excel 表` / `検出: 罫線`으로 표시.

- [ ] **Step 4: Run TableTruthSmoke + TableDetectorSmoke + ContentDiffSmoke**

- [ ] **Step 5: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/LOGIC/Diff/ContentModels.cs" "20_ソース/DiffXL/DiffXL/LOGIC/Diff/XlsxPackageReader.cs" "20_ソース/DiffXL/DiffXL/LOGIC/Diff/TableDetector.cs" "20_ソース/DiffXL/DiffXL/LOGIC/Diff/DiffEngine.cs" "20_ソース/DiffXL/DiffXL/VIEW/Controls" "20_ソース/DiffXL/_smoke"
git commit -m "feat: prefer Excel table parts over border flood"
```

---

### Task 11: H3 — 이전/다음 차분（N-04）

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml` — 툴바 `BtnPrevDiff` / `BtnNextDiff`（`BtnHighlightToggle` 앞）
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs` — `Window_PreviewKeyDown`에 F8 / Shift+F8
- Modify: `10_管理資料/要件定義.md` — N-04에서 「未実装」삭제
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs` — `GetDiffPairIndices()` 공개

**Interfaces:**
- Consumes: `ContentStreamPair`, `DiffItem.StreamPairIndex`
- Produces:  
  `ContentPane.GetDiffPairIndices()` → `IList<int>` 오름차순. pair가 Skip이거나 `DiffKind`가 붙은 인덱스.  
  `MainWindow.MoveToDiff(int delta)` — 현재 `VerticalOffset`에 해당하는 pair 다음/이전으로 `ScrollToPairIndex` + `HighlightPairIndex` 좌우.

키:

```csharp
if (e.Key == Key.F8)
{
    bool prev = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
    MoveToDiff(prev ? -1 : 1);
    e.Handled = true;
    return;
}
```

버튼 툴팁: `前の差分 (Shift+F8)` / `次の差分 (F8)`.

차분 인덱스: `item.StreamPairIndex >= 0`인 모든 비-Structure 아이템 ∪ `AlignOp != Match`인 pair.

- [ ] **Step 1: Add GetDiffPairIndices and a tiny smoke if extractable**

인덱스는 UI 상태라 로직을 `ContentPane`에 두고, 리스트 구성은:

```csharp
public IList<int> GetDiffPairIndices(IEnumerable<DiffItem> items)
{
    var set = new SortedSet<int>();
    if (_pairs != null)
    {
        for (int i = 0; i < _pairs.Count; i++)
        {
            if (_pairs[i] != null && _pairs[i].Op != AlignOp.Match)
            {
                set.Add(i);
            }
        }
    }

    if (items != null)
    {
        foreach (DiffItem it in items)
        {
            if (it != null && it.Kind != DiffKind.Structure && it.StreamPairIndex >= 0)
            {
                set.Add(it.StreamPairIndex);
            }
        }
    }

    return set.ToList();
}
```

수동 확인: `content_diff` 비교 후 F8이 다음 노란 줄로 이동.

- [ ] **Step 2: Wire keys and buttons**

`MoveToDiff`가 리스트가 비면 Status `差分なし`. 끝에서 F8이면 처음으로 순환.

- [ ] **Step 3: Build**

```powershell
Build-DiffXL
```

- [ ] **Step 4: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml" "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs" "20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs" "10_管理資料/要件定義.md"
git commit -m "feat: next/previous diff with F8"
```

---

### Task 12: H3 — MiniMap 마커 클릭 + 제목, 시트 선택 한 줄

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapControl.xaml` — `Text="MiniMap"` → `Text="差分マップ"`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapControl.xaml.cs` — 마커 `IsHitTestVisible = true`. MouseDown: 마커 히트면 그 `DiffItem`으로 `NavigateRequested`（grab 없이 점프）. 마커가 아니면 기존 `CaptureGrab` 스크럽
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs` — `OnLeftSheetChangedByUser` / `OnRightSheetChangedByUser`가 **툴바 페어와 같은 짝**으로 상대 시트를 맞춘다（테스트 TC-06）. 독립 보기 모드는 넣지 않는다
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/WorkbookPane.xaml` — 시트 콤보 ToolTip을 `ツールバーのシート対応に合わせて左右が切り替わります`

**Interfaces:**
- Consumes: Task 6 `StreamPairIndex`
- Produces: 마커 클릭 → `ScrollToDiffItem` + highlight. 드래그는 스크럽 유지

마커 vs 스크럽: `e.OriginalSource`가 `Rectangle`이고 `Tag is MiniMapMarkerTag`이면 점프만 하고 `_dragging`을 시작하지 않는다.

- [ ] **Step 1: Implement hit-test branch in both MouseDown handlers**

- [ ] **Step 2: Sync pane sheet combos to the pair**

`OnLeftSheetChangedByUser`: 현재 `SheetPairs`에서 왼쪽 이름에 맞는 오른쪽을 찾아 `RightPane` 시트를 설정. 반대도 동일. 무한 루프는 기존 `_syncingSheets` 플래그.

- [ ] **Step 3: Build**

- [ ] **Step 4: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapControl.xaml" "20_ソース/DiffXL/DiffXL/VIEW/Controls/MiniMapControl.xaml.cs" "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs" "20_ソース/DiffXL/DiffXL/VIEW/Controls/WorkbookPane.xaml"
git commit -m "feat: click MiniMap markers and keep sheet combos paired"
```

---

### Task 13: H3 — 스트림 종류 필터 칩

**Files:**
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml` — 스크롤 위쪽에 `StackPanel` 칩: `すべて` `表` `画像` `セル`
- Modify: `20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs` — `_kindFilter` (`null` / `Table` / `Image` / `LooseRow`). Realize 시 필터에 안 맞는 pair는 **높이 0이 아니라** 숨기지 않고, 스크롤 점프만 필터 대상으로?  

로드맵: 「탭 복원이 아님. 필터」. 구현은 **점프/목록만 필터**하면 높이 맵이 안 흔들린다.

더 단순한 정본: 필터는 MiniMap 마커와 `GetDiffPairIndices`와 상태줄에만 적용. 본문은 전체 스트림을 유지한다. 칩은 「次の差分」가 그 종류만 순회하게 한다.

`ContentPane.KindFilter` (`enum StreamKindFilter { All, Table, Image, Cell }`)  
`GetDiffPairIndices`가 필터를 존중.

`MainWindow` 칩은 좌우 `ContentPane`에 같은 필터를 넣는다.

- [ ] **Step 1: Add enum + filter to GetDiffPairIndices**

Table: `TableHeader`/`TableRow` 또는 Table* DiffKind.  
Image: `Image` 블록.  
Cell: `LooseRow`.

- [ ] **Step 2: Add four toggle buttons in ContentPane.xaml, Mutual exclusive, default All**

- [ ] **Step 3: Build**

- [ ] **Step 4: Commit**

```bash
git add "20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml" "20_ソース/DiffXL/DiffXL/VIEW/Controls/ContentPane.xaml.cs" "20_ソース/DiffXL/DiffXL/VIEW/MainWindow.xaml.cs"
git commit -m "feat: filter next-diff by table, image, or cell"
```

---

### Task 14: H4 — 스모크 한 스크립트 + CI

**Files:**
- Create: `10_管理資料/テスト/run-logic-smokes.ps1`
- Create: `.github/workflows/smokes.yml`

**Interfaces:**
- Consumes: Task 공통 `Build-DiffXL` / `Invoke-Smoke`
- Produces: exit 0 = 아래 목록 전부 PASS

스크립트가 돌릴 스모크（존재하는 것만, 없으면 skip하지 말고 파일이 있을 때 실행）:

1. `ContentDiffSmoke`
2. `ContentStreamSmoke`
3. `MiniMapViewportBandSmoke`
4. `ImageOverlayAlignSmoke`
5. `SheetMatcherSmoke`
6. `StreamPairLinkSmoke`
7. `TableTruthSmoke`
8. `TableRowDiffSmoke`
9. `TableDetectorSmoke`

`run-logic-smokes.ps1`는 위 함수를 그대로 넣고, 실패 시 `$fail++` 후 마지막에 `exit $fail`.

`.github/workflows/smokes.yml`:

```yaml
name: logic-smokes
on:
  push:
    branches: [main]
  pull_request:
jobs:
  smokes:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup MSBuild
        uses: microsoft/setup-msbuild@v2
      - name: Run logic smokes
        shell: pwsh
        run: ./10_管理資料/テスト/run-logic-smokes.ps1
```

CI의 MSBuild 경로는 스크립트 안에서 `vswhere`로 찾게 한다:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
```

- [ ] **Step 1: Write the script and run it locally**

```powershell
pwsh -File "10_管理資料\テスト\run-logic-smokes.ps1"
```

Expected: exit 0.

- [ ] **Step 2: Add the workflow file**

- [ ] **Step 3: Commit**

```bash
git add "10_管理資料/テスト/run-logic-smokes.ps1" .github/workflows/smokes.yml
git commit -m "ci: run logic smokes on Windows"
```

---

### Task 15: H4 — 버전 0.2.0-beta.1 과 배포 문구 일치

**Files:**
- Modify: `VERSION` → `0.2.0-beta.1`
- Modify: `20_ソース/DiffXL/DiffXL/Properties/AssemblyInfo.cs` — `AssemblyVersion` / `FileVersion` `0.2.0.0`, Copyright `Copyright © 2026`
- Modify: `CHANGELOG.md` — Unreleased에서 H0–H4 항목을 `## [0.2.0-beta.1] - 2026-08-15`（날짜는 태그 당일）로 옮김. 비교 링크 갱신
- Modify: `README.md` badge version
- Modify: `.gitignore` — `_smoke/*.exe` 이미 있음. `**/opencv_videoio_ffmpeg*` 추가

**Interfaces:**
- Consumes: H0–H4가 커밋된 상태
- Produces: 버전 파일 3곳이 일치

이 Task는 Task 14까지 끝난 뒤에만 한다.

Release 산출물을 열어 `YamlDotNet.dll`이 exe 옆에 있으면 README 배포 행을 「Debug는 DLL 병치, Release Costura는 절차서 확인」으로 유지한다. 옆 DLL을 지우는 작업은 이 Task에서 **하지 않는다**（별 빌드 작업）. 주장만 사실에 맞춘다.

- [ ] **Step 1: Update VERSION, AssemblyInfo, CHANGELOG, README badge**

- [ ] **Step 2: Commit**

```bash
git add VERSION "20_ソース/DiffXL/DiffXL/Properties/AssemblyInfo.cs" CHANGELOG.md README.md .gitignore
git commit -m "chore: release notes for 0.2.0-beta.1"
```

태그와 GitHub Release는 `docs/release-procedure.md`를 따른다. 이 계획의 커밋 범위는 여기까지. 태그 푸시는 사람이 확인한 뒤에 한다.

---

## Self-review

| 로드맵 ID | Task |
|-----------|------|
| H0 한 문장·LICENSE·단일 exe 문구 | 1 |
| H0 요건 V-01d·N-04 상태·QA 유령 | 2 |
| H0 COM/버튼/오버레이 | 3 |
| H1 SheetMatcher | 4 |
| H1 pair 정본·Attach | 5–6 |
| H1 이미지 임계 통일 | 7 |
| H2 행 유사도·여분 열·xl/tables | 8–10 |
| H3 N-04·마커·시트·필터 | 11–13 |
| H4 CI·버전 | 14–15 |
| 하지 않음（.xls, 수식, MVVM…） | 계획에 없음 |

AutoLive를 MainWindow에서 분리하는 것은 H4에서 **빼 두었다**. 1400행 이동은 회귀가 크고, CI 스모크가 배포 가능 조건을 이미 채운다. 다음 로드맵 항목으로 남긴다.
