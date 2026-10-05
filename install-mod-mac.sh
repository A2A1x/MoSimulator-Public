#!/bin/bash
# Installs the SpectrumMod (GammaRay) macOS zip into MoSimulator's Mods folder, replacing any older version.
# Usage, in Terminal:
#   bash ~/Downloads/install-mod-mac.sh               # uses the newest SpectrumMod MacOS zip in Downloads
#   bash ~/Downloads/install-mod-mac.sh path/to.zip   # or a specific zip
set -e

zip="${1:-$(ls -t "$HOME"/Downloads/SpectrumMod*MacOS*.zip 2>/dev/null | head -1)}"
if [ ! -f "$zip" ]; then
    echo "Couldn't find a SpectrumMod MacOS zip in Downloads. Pass its path: bash install-mod-mac.sh /path/to/zip"
    exit 1
fi

# The macOS build's data folder is named after its bundle identifier, not CascadeStudios/MoSimulator
mods="$HOME/Library/Application Support/com.Unity-Technologies.com.unity.template.urp-blank/Mods"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# The zip is made on Windows with backslash paths; unzip converts them and exits 1 (warning) when it does
unzip -q "$zip" -d "$tmp" || [ $? -eq 1 ]
if [ ! -f "$tmp/SpectrumMod/catalog.json" ]; then
    echo "$zip doesn't look like a SpectrumMod build (no SpectrumMod/catalog.json inside)."
    exit 1
fi

mkdir -p "$mods"
rm -rf "$mods/SpectrumMod"
mv "$tmp/SpectrumMod" "$mods/"
echo "Installed $(basename "$zip") to $mods/SpectrumMod"
echo "Restart MoSimulator if it's open."
