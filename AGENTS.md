# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A garden maze game in the style of Universal's 1981 arcade game Lady Bug, in C# on .NET 10 and Avalonia 12. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code,
and the sounds and music are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVALadyBug.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVALadyBug.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVALadyBug          # play it; --no-audio for silence
LADYBUG_SHOTS=$PWD/Images dotnet test tests/AVALadyBug.Tests --filter ScreenshotTests   # refresh the README's pictures
LADYBUG_PROBE=1 dotnet test tests/AVALadyBug.Tests --filter PilotProbe   # how the computer player gets on, for tuning
python3 tools/make_sounds.py                 # rebuild src/AVALadyBug/Sounds (standard-library Python only)
```

`LADYBUG_PROBE=lazy` runs a player that turns at random now and then. Without `LADYBUG_PROBE` the probe test does
nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVALadyBug/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` is the rules within a garden: building it, the edge timer that lets the bugs out, the
    gates, eating, the SPECIAL and EXTRA letters, hearts, skulls and the vegetable.
  - `Entities.cs` has `Controls`, the screen measures (`Field`), `Gate`, the `Maze` of hedges and gates, the
    runners (`Ladybug`, `Bug`) and the items.
  - `LadybugPilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVALadyBug/Views/`: `GameCanvas` (split over two partial files) draws the screen and runs the 60 Hz timer.
- `src/AVALadyBug/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVALadyBug.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay tests, screenshot tests and the
  pilot probe. The tests see the engine's internals (`InternalsVisibleTo`), such as `Calm`, which keeps the bugs
  where a test puts them and stops the timer.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the game in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`
  (`new Random(seed)`). A garden's items are laid out from `new Random(1981 + level * 211)`, so the same garden is
  the same every time. Never use `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** `Controls` is `dx` and `dy`, each -1, 0 or 1, and -1 is up the screen, on
  keys and stick alike (`LeftStickY < -0.4` is up). Keep them agreeing when adding controls.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVALadyBug`); tests load the
  table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
