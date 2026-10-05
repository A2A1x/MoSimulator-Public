# GammaRay (3847) Changelog

## v2.0.0-beta (in testing)

### Scoring
- Coral briefly clears the robot after every score, improving scoring consistency on all levels
- Coral intake stays off for 0.35 s after scoring, so a quick switch to intake won't regrab what you just scored
- Ejecting coral from stow pops it up and clear of the robot
- Scoring L1 coral with auto-align is more consistent at making the pyramid

### Intake
- Coral intake no longer grabs coral it shouldn't (slower pull, shorter reach)
- Coral intake pulls a bit harder

### Arm
- Wrist 50% faster
- Elevator sits higher for low and high reef algae intake
- Wrist turns toward the picked branch during the drive up, not only once auto-align arrives

## v1.0.0 (2026-10-02)

First release.

### Robot
- Elevator, shoulder/elbow/twist arm and climber, driven by the poses and scoring sequence from Spectrum's 2025 robot code
- Scores L1–L4 off the front or back, whichever side faces the reef
- Wrist turns toward the chosen left/right branch and flips over the poles between them
- Auto-align stops at the center of the reef face
- Shoulder and elbow avoid swinging through the robot
- Arm speeds: shoulder 4, elbow 5, wrist 7
- Lowered center of mass so it doesn't tip on quick direction changes
- Climber holds position while the robot is disabled

### Coral
- Ground intake by default; Robot Special toggles human player station intake
- Station intake comes off whichever side faces the nearest station
- Coral being pulled in ignores the robot so it can't jam against the arm and get flung

### Algae
- Algae intake from the ground, stack and low/high reef
- Separate stow pose while holding algae
- Barge: wrist turns to face the barge, stronger throw
- Spring-loaded algae pincher opens while holding algae

### Look and sound
- Spinning intake roller with sound, algae stall sound, elevator audio
- LED strips ported from Spectrum's LED states
- Inverted logo, bumper numbers in the logo font, alliance-colored bumpers
- Main menu model with LEDs
- Tutorial cards for station intake, two-sided scoring and climb

### Platforms
- Windows, macOS and Linux builds
