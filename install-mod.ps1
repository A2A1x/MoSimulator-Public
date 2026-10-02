# Assembles the built SpectrumMod and installs it into the real game's Mods folder, or zips it for testers.
# Run after Unity: Addressables Groups -> Build -> New Build -> Default Build Script.
# Mac/Linux: switch the build target (File -> Build Settings) to that platform before building, then pass -Platform.
#   .\install-mod.ps1                    # install the Windows build locally
#   .\install-mod.ps1 -Zip               # SpectrumMod-Windows.zip
#   .\install-mod.ps1 -Platform OSX      # SpectrumMod-OSX.zip (non-Windows always zips)
param([string]$Group = "SpectrumMod", [string]$Dll = "GammaRay",
      [ValidateSet("Windows", "OSX", "Linux")][string]$Platform = "Windows", [switch]$Zip)
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$aa = "$root\Library\com.unity.addressables\aa\$Platform"
if (-not (Test-Path "$aa\catalog.json")) { throw "No $Platform catalog in $aa - build the mod in Unity with the $Platform target first." }

# Take the bundles this platform's catalog references: every platform builds into the same Mods\<Group> folder,
# and old builds leave stale bundles behind there.
$names = [regex]::Matches((Get-Content "$aa\catalog.json" -Raw), '[\w-]+\.bundle') | ForEach-Object Value | Sort-Object -Unique
$bundles = $names | ForEach-Object { "$root\Mods\$Group\$_" }
$missing = $bundles | Where-Object { -not (Test-Path $_) }
if (-not $names -or $missing) { throw "Bundle(s) missing from $root\Mods\$Group - rebuild the mod in Unity: $missing" }

$install = $Platform -eq "Windows" -and -not $Zip
$dest = if ($install) { "$env:USERPROFILE\AppData\LocalLow\CascadeStudios\MoSimulator\Mods\$Group" } else { "$env:TEMP\mod-zip\$Group" }

# Replace, never merge (bundle name changes every build).
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory $dest | Out-Null

Copy-Item $bundles $dest
Copy-Item "$aa\catalog.json", "$aa\catalog.hash", "$aa\settings.json" $dest
Copy-Item "$root\Library\ScriptAssemblies\$Dll.dll" $dest

Get-ChildItem $dest | Format-Table Name, Length, LastWriteTime
if ($install) { Write-Host "Installed to $dest"; return }

$zipPath = "$root\$Group-$Platform.zip"
Compress-Archive -Path $dest -DestinationPath $zipPath -Force
Remove-Item (Split-Path $dest) -Recurse -Force
Write-Host "Wrote $zipPath"
