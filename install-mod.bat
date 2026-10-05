@echo off
rem Installs a SpectrumMod (GammaRay) release zip into MoSimulator's Mods folder on Windows, replacing any older version.
rem macOS/Linux: use install-mod.sh instead.
rem Double-click to use the newest SpectrumMod Windows zip in Downloads, or drag a zip onto this file.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "$zip = '%~1';" ^
  "if (-not $zip) { $zip = Get-ChildItem \"$env:USERPROFILE\Downloads\SpectrumMod*Windows*.zip\" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName };" ^
  "if (-not $zip -or -not (Test-Path $zip)) { Write-Host 'Could not find a SpectrumMod Windows zip in Downloads. Drag the zip onto install-mod.bat.'; exit 1 };" ^
  "$tmp = Join-Path $env:TEMP ('spectrummod-' + [guid]::NewGuid());" ^
  "try {" ^
  "  Expand-Archive $zip $tmp;" ^
  "  if (-not (Test-Path \"$tmp\SpectrumMod\catalog.json\")) { Write-Host \"$zip doesn't look like a SpectrumMod build (no SpectrumMod\catalog.json inside).\"; exit 1 };" ^
  "  $mods = \"$env:USERPROFILE\AppData\LocalLow\CascadeStudios\MoSimulator\Mods\";" ^
  "  New-Item -ItemType Directory $mods -Force | Out-Null;" ^
  "  if (Test-Path \"$mods\SpectrumMod\") { Remove-Item \"$mods\SpectrumMod\" -Recurse -Force };" ^
  "  Move-Item \"$tmp\SpectrumMod\" $mods;" ^
  "  Write-Host \"Installed $(Split-Path $zip -Leaf) to $mods\SpectrumMod\";" ^
  "  Write-Host \"Restart MoSimulator if it's open.\"" ^
  "} finally { if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force } }"
pause
