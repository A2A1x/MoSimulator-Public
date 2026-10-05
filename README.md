# MoSimulator Public Repository
Welcome to the official repository for the public release of the MoSimulator source code. This source code will allow you to get started on building your first robot mod. For more information, please visit the [MoSim Website](https://mosimulator.com/).

## Modding Documentation
All of the modding documentation is located on the website here: [Modding Documentation](https://docs.mosimulator.com/).

## SpectrumMod: GammaRay (3847)
This fork adds **SpectrumMod**, a modpack with Spectrum 3847's 2025 robot, GammaRay, driven by the poses and scoring sequence from Spectrum's robot code. See [GAMMARAY_CHANGELOG.md](GAMMARAY_CHANGELOG.md) for what's in each version.

### Install
Download the zip for your OS from [Releases](https://github.com/A2A1x/MoSimulator-Public/releases), then put the `SpectrumMod` folder from inside it into the game's `Mods` folder, replacing any older version:

| OS | Mods folder |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\CascadeStudios\MoSimulator\Mods` |
| macOS | `~/Library/Application Support/com.Unity-Technologies.com.unity.template.urp-blank/Mods` |
| Linux | `~/.config/unity3d/CascadeStudios/MoSimulator/Mods` |

On macOS, [install-mod-mac.sh](install-mod-mac.sh) does this for you: `bash install-mod-mac.sh` installs the newest SpectrumMod zip in Downloads. Restart the game after installing.

### Playing GammaRay
- Scores L1–L4 off the front or back, whichever side faces the reef
- Auto-align left/right picks the branch; the wrist turns toward it
- Ground coral intake by default; **Robot Special** toggles human player station intake
- Algae from the ground, stack and low/high reef; barge and processor scoring

### Building
The mod lives in `Assets/Prefabs/Reefscape/Robots/Mods/SpectrumMod` (robot code: `3847/GammaRay.cs`). In Unity, **Tools > Build Mod (all platforms)** builds the Addressables for Windows, macOS and Linux, zips each as `SpectrumMod-v<version>-<OS>.zip` in the project root, and installs the Windows build into your local game. The version comes from `modpackVersion` in `ReefscapeModpack.asset`. To build one platform by hand, see [install-mod.ps1](install-mod.ps1).

### Branches
- `SpectrumMod`: the latest released version
- `GammaRay-beta-v*`: work on the next beta

<br>
Note: MoSimulator's source code is protected by a custom proprietary license. By downloading or interacting with this repository, you agree to the terms outlined in the LICENSE file.
