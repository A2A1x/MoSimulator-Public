# MoSimulator Public Repository
Welcome to the official repository for the public release of the MoSimulator source code. This source code will allow you to get started on building your first robot mod. For more information, please visit the [MoSim Website](https://mosimulator.com/).

## Modding Documentation
All of the modding documentation is located on the website here: [Modding Documentation](https://docs.mosimulator.com/).

## SpectrumMod: GammaRay (3847)
This fork adds **SpectrumMod**, a modpack with Spectrum 3847's 2025 robot, GammaRay, driven by the poses and scoring sequence from Spectrum's robot code. See [GAMMARAY_CHANGELOG.md](GAMMARAY_CHANGELOG.md) for what's in each version.

[![GammaRay teaser](https://img.youtube.com/vi/1Y1DCv1BIts/hqdefault.jpg)](https://youtu.be/1Y1DCv1BIts)

[Watch the teaser on YouTube](https://youtu.be/1Y1DCv1BIts)

### Install
Download the zip for your OS from [Releases](https://github.com/A2A1x/MoSimulator-Public/releases), then install it with the script for your OS. Each one finds the newest SpectrumMod zip in your Downloads folder (or takes a zip you give it) and replaces any older version:

- **Windows:** double-click [install-mod.bat](install-mod.bat), or drag the zip onto it
- **macOS / Linux:** run `bash install-mod.sh` in Terminal ([install-mod.sh](install-mod.sh)), or `bash install-mod.sh path/to.zip`

Restart the game after installing. To install by hand, put the `SpectrumMod` folder from inside the zip into the game's `Mods` folder:

| OS | Mods folder |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\CascadeStudios\MoSimulator\Mods` |
| macOS | `~/Library/Application Support/com.Unity-Technologies.com.unity.template.urp-blank/Mods` |
| Linux | `~/.config/unity3d/CascadeStudios/MoSimulator/Mods` |

### Playing GammaRay
- Scores L1–L4 off the front or back, whichever side faces the reef
- Auto-align left/right picks the branch; the wrist turns toward it
- Ground coral intake by default; **Robot Special** toggles human player station intake
- Algae from the ground, stack and low/high reef; barge and processor scoring

### Building
The mod lives in `Assets/Prefabs/Reefscape/Robots/Mods/SpectrumMod` (robot code: `3847/GammaRay.cs`). In Unity, **Tools > Build Mod (all platforms)** builds the Addressables for Windows, macOS and Linux, zips each as `SpectrumMod-v<version>-<OS>.zip` in the project root, and installs the Windows build into your local game. The version comes from `modpackVersion` in `ReefscapeModpack.asset`. To build one platform by hand, see [install-mod.ps1](install-mod.ps1) (a build tool; the install scripts above are for players).

### Branches
- `SpectrumMod`: the latest released version
- `GammaRay-beta-v*`: work on the next beta

<br>
Note: MoSimulator's source code is protected by a custom proprietary license. By downloading or interacting with this repository, you agree to the terms outlined in the LICENSE file.
