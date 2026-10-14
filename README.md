# Glider Suit

A chest-armor ("Outfit") modification for **7 Days to Die v3.3** that unfolds into
a homemade hang glider. Press Jump in mid-air, and a harness and a pair of
tarp-and-tent-pole wings open on your back.

> **Status: 0.5.0, pre-release.** Flight, crashes, landings, folding, water,
> ladders, weapons, dying in the air and the worn visuals have been tested in game
> in single-player v3.3.0 (b18). The schematic's trader and loot entries are
> confirmed in the running game but it has not yet been seen in a trader or a
> container (that needs a long playthrough), and the Parachute exclusion is
> untested. Results are in `plan.md`, section 15.

## What it does

- **Press Jump in mid-air** to open the wings: jump, then press Jump again. Press
  it while gliding to fold them.
- The first opening after leaving the ground gives the wings a push to 12 m/s
  (`launch_speed`), so even a standing jump glides a few metres. Opening again in
  the same fall gives nothing extra.
- **Left/Right** bank and turn; the mouse also steers.
- **Forward** lowers the nose (dive, gain speed). **Back** raises it (pull up,
  lose speed).
- The glide is an **energy model**: speed comes only from losing height. Run out
  of speed and the glider **stalls and nosedives**; hold the nose a little below
  level and it will not. The wings flatten out smoothly rather than letting you
  climb: **you can never get above the height you took off from.**
- Opening the wings eases you into the normal glide by itself; press Forward or
  Back and the nose is yours, and stays where you leave it. A key you were
  already holding when the wings opened (Forward from a running jump) does
  nothing until you let go of it.
- Landing gently does no fall damage. Hitting the ground or a wall fast injures,
  through the game's own fall-damage rules, so parkour, Impact Bracing and boots
  all still apply. A hit while gliding counts at half its speed
  (`crash_damage_scale`), so damage starts at about cruise speed. Folding the
  wings in mid-air hands you back to a normal fall with full fall damage.
- Flying onto a ladder folds the wings and costs nothing.
- A stall recovers into a glide by itself once speed returns; pulling Back helps
  rather than hinders, but hold it too long and you stall again.
- With the **Grappling Hook** mod: attaching a rope folds the wings, and the rope
  takes over. Press Jump to let go, then press Jump again to open the wings.
- Weapons stay usable while gliding.

The glider and the **Parachute** mod share a modifier tag, so only one of them
can be fitted in an outfit at a time.

## Install

1. Make sure EAC is off (the mod's `ModInfo.xml` sets `SkipWithAntiCheat`).
2. Copy the `GliderSuit` folder into `7 Days to Die/Mods/`.
3. Launch without EAC.

Get it in game with the workbench recipe (60 cloth, 8 metal pipe, 6 duct tape,
10 leather, 1 sewing kit, 4 mechanical parts, 2 springs) after reading the
schematic, or with the test commands below.

## Test commands

Open the console (F1) and use `glider ...`:

| Command | Effect |
|---|---|
| `glider give` | outfit + schematic + mod |
| `glider outfit [item] [quality]` | a chest outfit, default `armorScavengerOutfit` Q6 |
| `glider mod [n]` / `glider schematic [n]` | the items |
| `glider kit` | an outfit with the mod already installed |
| `glider up [m]` | lift the player straight up (default 80 m) for a drop test |
| `glider open` / `glider fold` | force the state (needs the harness buff) |
| `glider speed <m/s>` / `glider pitch <deg>` | set the flight state directly |
| `glider impact <m/s>` | apply a crash at that speed and log the health lost |
| `glider ceiling` | log the takeoff ceiling and the height below it |
| `glider status` | state, config values and patch status |
| `glider bones` | dump the character's bone catalogs |
| `glider foldpose [0..1|off]` | hold the wings at one fold fraction |
| `glider reload` | re-read the config and re-apply the worn placement |
| `glider debug [on|off]` | on-screen readout and verbose log |
| `glider heal` | restore health and clear leg injuries |

Every command logs a `[GliderSuit]` line; the in-game console cannot be copied
from, so read `7DaysToDie_Data/output_log.txt`.

## Configuration

Everything tunable lives in `Config/GliderSuitConfig.json` (JSON with `//`
comments). Each key has a default, an allowed range, and a comment; an invalid
value logs a warning and falls back to the default. `glider reload` applies
changes without restarting.

The interesting ones: `cruise_speed` (25 m/s), `glide_ratio` (12:1),
`stall_speed` (8 m/s), `crash_damage_scale` (1.0 - set it to 0 for no crash
damage), `wind_loop`, and the `worn_*` / `fp_*` placement offsets.

## Building

Needs the .NET SDK, **Blender 5.2**, **Unity 2022.3.62f2** (not Unity 6), and
Python 3 with numpy and Pillow.

```
build-deploy.cmd              # compile the DLL and mirror it into the game's Mods folder
build-assets.ps1              # Blender -> FBX/textures/previews, then Unity -> prefab, icon, bundle
build-assets.ps1 -SkipBlender # Unity only
tools/make_sounds.py          # regenerate the three WAVs
tools/make_icon.py            # trim the Unity icon master to the 160x160 item icon
package.ps1                   # dist/GliderSuit-<version>.zip
```

The game must be closed while deploying. Blender previews land in `unity/preview`.

## Testing

Read `output_log.txt` for `[GliderSuit]`, `Exception` and XML patch warnings.
The phase-by-phase test plan (T1-T26) is in `plan.md` section 9.

A quick first pass:

1. `glider status` - expect `patches=ok`.
2. `glider kit`, wear the outfit, `glider bones` - paste the bone names into
   `plan.md` section 3.4 (shoulder bone names are still unconfirmed).
3. `glider up 80`, press Jump - the wings should open without a jolt.
4. Hands off: settle near 25 m/s with a ~2.1 m/s sink.
5. Hold Back until the nose drops - that is the stall.
6. `glider up 80`, dive straight in - severe injury or death.
7. Land trimmed on flat ground - no damage, wings fold.
8. Jump off a roof and never open the wings - exactly vanilla.
