# Assembles the built SpectrumMod and installs it into the real game's Mods folder.
# Run after Unity: Addressables Groups -> Build -> New Build -> Default Build Script.
param([string]$Group = "SpectrumMod", [string]$Dll = "GammaRay")
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$dest = "$env:USERPROFILE\AppData\LocalLow\CascadeStudios\MoSimulator\Mods\$Group"

# Newest bundle only: old builds leave stale bundles behind in Mods\<Group>.
$bundle = Get-ChildItem "$root\Mods\$Group\*.bundle" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $bundle) { throw "No bundle in $root\Mods\$Group - build the mod in Unity first." }

# Replace, never merge (bundle name changes every build).
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory $dest | Out-Null

Copy-Item $bundle.FullName $dest
Copy-Item "$root\Library\com.unity.addressables\aa\Windows\catalog.json", "$root\Library\com.unity.addressables\aa\Windows\catalog.hash", "$root\Library\com.unity.addressables\aa\Windows\settings.json" $dest
Copy-Item "$root\Library\ScriptAssemblies\$Dll.dll" $dest

Get-ChildItem $dest | Format-Table Name, Length, LastWriteTime
Write-Host "Installed to $dest"
