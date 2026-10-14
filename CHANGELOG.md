# Changelog

## 0.5.1 - 2026-10-14

### Changed

- **Third person while gliding.** Half a second after the wings open the camera
  switches to third person, and folding puts it back to the view you last chose
  with the view key, the way vehicles do. Weapons still work. A view you pick
  mid-glide is never overridden. Set `auto_third_person` to `false` to stay in
  first person; `third_person_after_ms` (500) sets the delay.
- **The body lies nearly flat in third person.** `body_pitch_base` is 85 (was
  65), so in a normal glide the character is close to horizontal under the
  wings. The open wings and the first-person wings look as before.

## 0.5.0 - 2026-10-14

### Changed

- **One Jump press in mid-air opens the wings** (it was a double-tap): jump, then
  press Jump again. Pressing Jump while gliding still folds them.
- **A standing jump can glide.** The wings open with 0.5 m of clear air below
  (was 2 m), and the first opening after leaving the ground pushes you up to
  12 m/s (`launch_speed`) toward where you look, so even a hop off flat ground
  glides a good way. Opening again in the same fall gives nothing extra, and you
  still cannot climb above the height you took off from.
- A movement key you were already holding when the wings opened (Forward from a
  running jump) does nothing until you let go of it, so a running jump no longer
  dives straight into the ground.
- **Much flatter glide:** `glide_ratio` is 12 (was 4). The glide angle is 4.8°
  and the hands-off sink at 25 m/s is about 2.1 m/s (was 6.1), so 200 m of
  height is close to a minute in the air. A hop off flat ground now glides
  about 15-20 m.
- **New wind sound,** a recorded loop, and it now tells you what the nose is
  doing: louder and higher in a dive, quieter and lower as you pull up, nearly
  silent in a stall. New options `wind_dive_boost`, `wind_climb_volume` and
  `wind_stall_volume`.
- `double_tap_window_ms` is gone from the config; `launch_speed` is new.

### Fixed

- The wind kept playing after dying in the air, until the respawn.

### Tested

- Dying in the air, a teleport while gliding, a fall with Impact Bracing
  installed (the bracing applies), Forward cancelling a stall pull-out, and
  hooking a grappling rope at 24 m/s then reopening. The schematic's trader and
  loot entries, price and stage gate were checked in the running game.

### Not yet tested

- The schematic actually turning up in a trader or a container, the Parachute
  exclusion, Back held during the opening ease, and letting go of a rope that is
  swinging you at speed.

## 0.4.0 - 2026-10-12

### Changed

- New art. The grey-box wings are replaced by a bat wing in weathered tan
  canvas: a curved steel leading spar on each side, seven ribs, canvas with a
  scalloped trailing edge, and a leather harness with brass buckles and a rolled
  pack. Folded, each wing swings down the back and its ribs close against the
  spar.
- New inventory icon: the open wings, rendered in 3D like the game's own icons.
- The open wings fly level. They are built in the plane of the back, so they hang
  like a cape when you stand with them open, and as they open they turn to the
  flight attitude instead of following the torso.
- In first person the wings lie flat overhead and stay out of the view in hard
  turns.

### Fixed

- In third person, Left/Right and the mouse did not turn the view, and the game
  spun the character round to face the key pressed. Steering now turns the
  third-person camera and the character stays facing the way it flies.
- The harness hung 14 cm behind the spine, with the belt behind the back instead
  of round the waist.
- The open wings stood on end when the body lay down to fly, and one wing stood
  up whenever a weapon was aimed.
- The third-person body stood up in a dive instead of tipping further forward.

### Not yet tested

- Dying in the air, releasing from a fast grappling swing into a glide, trader
  stock and loot drops for the schematic, the Parachute exclusion, and a fall
  with Impact Bracing installed.

## 0.3.0 - 2026-10-12

### Changed

- A stall now recovers into a glide by itself: once speed returns, the nose
  pulls out to the glide angle unless you press Forward or Back. Before, it
  stayed at the stall's dive angle, so opening the wings at low speed was a
  dive into the ground. From a standstill it takes about 9 m of height.
- Pulling Back while the nose is already coming up no longer slows it down.
  The pull-out runs at full rate until the glide angle is reached, then Back
  takes over. Holding Back for too long still climbs into a second stall.
- With the Grappling Hook mod installed (it needs the version that publishes
  the rope signal), attaching a rope folds the wings with no crash and no
  damage, and they cannot be opened until the rope is let go. Before, the two
  mods fought over the player's movement and the glider read the rope's pull
  as a wall strike.
- The shipped config has `debug` on while testing continues.

### Fixed

- The Jump press that lets go of a grappling rope counted as the first tap of
  the double-tap, so a deliberate double-tap afterwards opened the wings and
  folded them again.

### Not yet tested

- Releasing from a fast swing into a glide, dying in the air, trader stock and
  loot drops for the schematic, the Parachute exclusion, and a fall with Impact
  Bracing installed.

## 0.2.0 - 2026-10-12

The first version tested in game. Results are in `plan.md`, section 15.

### Changed

- The default cruise speed is 25 m/s, up from 14. The glide ratio is still 4:1,
  so the sink rate at cruise is about 6 m/s and a full dive now tops out near
  49 m/s.
- A crash or landing while gliding counts at half its speed (`crash_damage_scale`
  0.5, was 1.0), so damage starts at about cruise speed. Folding the wings and
  falling is still full vanilla fall damage.
- Opening the wings starts along your real direction of travel and eases the nose
  to the glide angle by itself (`open_trim_rate`), until it gets there or you
  press Forward or Back. Before, a straight drop opened level and stalled, and
  any sideways drift opened in a 70 degree dive.
- Flying onto a ladder folds the wings and costs nothing, instead of counting as
  a wall strike.

### Fixed

- The wings folded by themselves in mid-air on a long glide, whenever the game
  re-centred its coordinate origin.
- A wall strike was charged for the speed lost in one physics step, which
  understated it: a 14 m/s strike counted as 9 m/s.
- A smaller vanilla impact arriving just after a crash could overwrite the
  crash before the game applied its damage. Not seen in game; guarded anyway.
- `glider open` wiped the remembered takeoff height.
- `glider reload` switched the debug log off.

### Added

- `glider climb`: tests the takeoff ceiling in one command.
- With `glider debug on`, the log records every fall impact with its source,
  the health before and after and any leg injury, the heading, and the speed
  and heights at each fold.

### Not yet tested

- Trader stock and loot drops for the schematic, the Parachute exclusion, a
  fall with Impact Bracing installed, dying in the air, and flying alongside the
  Grappling Hook mod.

## 0.1.0 - pre-release

First implementation, built from `plan.md`. Tagged before in-game testing was
finished.

### Added

- `modArmorGliderSuit` chest-armor modification: double-tap Jump in mid-air to
  open, a single Jump press to fold, Left/Right and the mouse to steer,
  Forward/Back to dive and pull up.
- Energy-model flight: speed only comes from losing height, the glider stalls
  below `stall_speed` and nosedives, and the wings flatten into the takeoff
  height so the player can never climb above it.
- Crash handling through the vanilla fall-damage pipeline (parkour, Impact
  Bracing and boots still apply), a landing grace so a crash is never charged
  twice, and a safety net on `EntityAlive.fallHitGround`.
- Workbench recipe, tier-2 schematic in the tier-2 loot group, schematic in the
  traders' mod-schematic stock at 3000 before bartering perks, inventory icon,
  localized name and description.
- Generated unfold, fold and wind sounds, played from the mod's own asset bundle
  with a vanilla fallback if the bundle is missing.
- A grey-box wing rig built by `unity/blender/build_assets.py`: harness, three
  spars per side, tarp panels, procedural tarp and pole textures, and the folded
  pose. Preview renders in `unity/preview`.
- Worn visuals on the third-person rig, the inventory preview and in first
  person, a third-person glide pose, and camera roll while banking.
- Twelve `glider` console commands for testing and tuning.
- `build-assets.ps1`, `build-deploy.cmd`, `package.ps1`, `tools/make_sounds.py`,
  `tools/make_icon.py`.

### Known gaps

- The wing art is the plan's **grey-box pass** (section 8, Phase 7): the
  hierarchy, folded pose, axes and shader are proven, but the detailed model,
  skin weights and the final textures are still to come.
- Shoulder bone names for the third-person pose are unconfirmed until
  `glider bones` is run in game.
- Single-player only. Multiplayer and dedicated servers are Phase 10.
