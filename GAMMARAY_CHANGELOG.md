# GammaRay (3847) Changelog

## v4.0.0-beta (in testing)

### Fixes
- Arm poses tuned for this mod now take effect in the game, not only in the editor: L1, L4 and low/high reef algae were running on older defaults since v1.0.0
- Reef algae elevator heights raised
- Elevator holds still while the robot is disabled (it could keep moving, e.g. dropping to stow after a score)

### Intake
- Ground coral intake reaches further and pulls harder

### Climb
- Climber is stronger, so it reaches its climbed position and lifts the robot the same way every climb (it used to stall partway, at a different angle each time)
- Climber swings out at a steady, capped speed, so it no longer clips through the cage
- Reworked climber colliders

## v3.0.0-beta (in testing)

### Intake
- Intake roller is a physical, motor-driven roller that grips and pulls game pieces, not just a spinning model
- Ground coral intake uses planar tolerancing and no longer pulls coral along its length
- Coral being pulled in collides with the robot instead of passing through it, so it no longer phases through the end effector
- Coral intake pulls slower and a bit softer, with the roller doing more of the work
- Elevator sits higher for low and high reef algae intake

### Arm
- Wrist turns toward the branch the driver picked during the drive up to the reef, not only once auto-align arrives

### Platforms
- install-mod-mac.sh installs the macOS zip into MoSimulator's Mods folder

## v2.0.0-beta (2026-10-03)

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
