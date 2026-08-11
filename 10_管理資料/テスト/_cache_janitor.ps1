$end = (Get-Date).AddMinutes(190)
$cache = Join-Path $env:APPDATA "DiffXL\cache"
$log = "C:\JUN\WORK\DiffXL\10_管理資料\テスト\エビデンス_marathon_20260811_094257\cache-janitor.log"
while ((Get-Date) -lt $end) {
  try {
    $dirs = @(Get-ChildItem $cache -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    $i=0; $rm=0
    foreach ($d in $dirs) {
      $i++
      if ($i -le 3) { continue }
      try { Remove-Item $d.FullName -Recurse -Force -ErrorAction Stop; $rm++ } catch {}
    }
    $size = 0
    Get-ChildItem $cache -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $size += $_.Length }
    $line = "[{0}] dirs={1} removed={2} sizeMB={3:N1}" -f (Get-Date -Format "HH:mm:ss"), $dirs.Count, $rm, ($size/1MB)
    Add-Content $log $line -Encoding UTF8
  } catch {
    Add-Content $log ("[{0}] err {1}" -f (Get-Date -Format "HH:mm:ss"), $_.Exception.Message) -Encoding UTF8
  }
  Start-Sleep -Seconds 90
}
Add-Content $log ("[{0}] janitor done" -f (Get-Date -Format "HH:mm:ss")) -Encoding UTF8
