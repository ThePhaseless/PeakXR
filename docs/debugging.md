# Reporting a bug

PeakXR always keeps a short history of what just happened: its last 500 log lines, every game error with its stack, your last 50 refused actions and the last grip decision for each hand. When something goes wrong, save a bug report right away. The report holds that history, so you can send it later.

## Save a report

- In play, on any controller: click both sticks in and keep holding them. At 1 s you recenter; then the off hand feels a second hold fill, and 1.5 s later the report is saved. Letting go before that saves nothing.
- In play, where Menu reaches the game (not Quest or Rift under SteamVR, whose Menu goes to the SteamVR dashboard): hold Menu on one controller and click the other controller's stick, then keep both held for 1.5 s. The Menu hand feels the hold fill up. This one does not recenter first.
- In a menu: open the VR tab in the game's settings, go to the Debug section and press "Save bug report".

You hear the notice bell and feel a tick, a ring appears at your hand and "Bug report saved" shows next to it. While one report is still being written, a second try gets the wait cue.

The Menu gesture never pauses, recenters, sprints or crouches. Click the stick within a second of pressing Menu, though: once you have held Menu for a full second, the recenter has already happened. One-handed play has no gesture, so use the VR tab buttons there.

## Drop a marker

Let go of the same gesture before 1.5 s, or press "Drop a marker" in the VR tab. It notes the moment in the log and the report ("Marker 1", "Marker 2" and so on), so you can carry on playing and save the report later. Write down roughly what you saw at each marker number.

## Debug mode

`Debug.Mode` (off by default) records more for a bug that is hard to catch:

- the detailed traces: leaning and room-scale walking, every grip decision, rope and vine grip checks, hammer swings, hand-aimed tools, items floating off the hand, refresh rate and adaptive quality changes, and a frame-time line every 10 s;
- `BepInEx/PeakXR-trace.log`, written line by line, so it survives a crash;
- the last 30 s of head and hand poses, buttons, triggers, grips, finger curls, body state and held item, sampled 10 times a second and saved as `poses.csv` in each report.

`Debug.ShowFrameStats` (off) adds frames per second and frame time on your off-hand wrist while debug mode is on.

Debug mode costs a little frame time, so turn it on, reproduce the bug, save a report and turn it off again. You can switch it in the VR tab or in `BepInEx/config/com.thephaseless.peakxr.cfg` under `[Debug]`.

## Where the files go

Each report is a folder in `BepInEx/PeakXR-reports/`, named for the time you saved it, for example `20261023-140322`. PeakXR keeps the newest 20 and deletes older report folders when it saves a new one; move a report elsewhere to keep it. Each report folder holds:

| File | What it holds |
|---|---|
| `report.txt` | Mod version and build, game and Unity versions, VR runtime, headset and controllers, refresh rate, render scale, frame times, scene, your position and state, multiplayer state, one-handed, seated and comfort settings, any features that failed to load, recent refusals, grip decisions and markers |
| `log.txt` | The last PeakXR log lines and game errors, in time order |
| `poses.csv` | The pose recording (debug mode only) |
| `config.cfg` | A copy of your PeakXR settings |
| `screenshot.png` | The desktop mirror at the moment you saved |

## What to send

Zip the report folder and attach it to your bug report with a sentence or two: what you did, what you expected and what happened instead. If the game crashed, also send `BepInEx/LogOutput.log` and, if debug mode was on, `BepInEx/PeakXR-trace.log`.

Reports stay on your computer. PeakXR sends nothing over the network. Player names become "player 2" or "character 1", Steam IDs and the user name in folder paths are blanked, and only player counts are kept. `PeakXR-trace.log` gets the same treatment line by line. The screenshot can still show names floating above other players, so check it before you share it.
