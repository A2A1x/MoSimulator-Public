#!/bin/bash
# Installs a SpectrumMod (GammaRay) release zip into MoSimulator's Mods folder on macOS or Linux, replacing any older version.
# Windows: use install-mod.bat instead.
# Usage, in Terminal:
#   bash ~/Downloads/install-mod.sh               # uses the newest SpectrumMod zip for this OS in Downloads
#   bash ~/Downloads/install-mod.sh path/to.zip   # or a specific zip
set -e

case "$(uname -s)" in
    Darwin)
        os=MacOS
        # The macOS build's data folder is named after its bundle identifier, not CascadeStudios/MoSimulator
        mods="$HOME/Library/Application Support/com.Unity-Technologies.com.unity.template.urp-blank/Mods" ;;
    Linux)
        os=Linux
        mods="${XDG_CONFIG_HOME:-$HOME/.config}/unity3d/CascadeStudios/MoSimulator/Mods" ;;
    *)
        echo "Unsupported OS $(uname -s). On Windows, run install-mod.bat."
        exit 1 ;;
esac

zip="${1:-$(ls -t "$HOME"/Downloads/SpectrumMod*"$os"*.zip 2>/dev/null | head -1)}"
if [ ! -f "$zip" ]; then
    echo "Couldn't find a SpectrumMod $os zip in Downloads. Pass its path: bash install-mod.sh /path/to/zip"
    exit 1
fi

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
