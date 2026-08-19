# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

A live assistant for **Yu-Gi-Oh! Forbidden Memories** (PlayStation 1, 2002 — *not* PS2, despite the game often being played on PS2 hardware). It watches an emulator on screen, identifies the cards in the player's hand, and suggests the best available fusion and what it produces.

Status: early. Screen capture and the region cut tool are built and verified to compile and launch. The fusion engine, text recognition, artwork matching, and overlay do not exist yet.

## Commands

```powershell
dotnet build YgoFm.slnx                                     # build everything
dotnet run --project src\YgoFm.Calibrator                   # launch the cut tool
```

**The solution file is `YgoFm.slnx`, not `.sln`** — .NET 10's `dotnet new sln` emits the newer XML solution format. `dotnet build YgoFm.sln` fails with MSB1009.

There is **no test project yet**. When adding one, it belongs on the fusion engine first (see below) — that is where correctness actually matters and where the data is most likely to be wrong.

## Hard constraint: emulator-agnostic

The tool must work with **any** PlayStation 1 emulator — DuckStation, ePSXe, RetroArch cores, Mednafen, etc.

This rules out reading the emulator's process memory, which would otherwise be the most accurate way to obtain card identities. Every emulator (and every version of each) maps the simulated console RAM (Random Access Memory) at a different address under a different process name, so that approach would lock users to one build. **Do not propose or add `ReadProcessMemory`, emulator debugger hooks, Lua scripting, or savestate parsing as the input path.** Screen capture plus image recognition is the only sanctioned source.

The consequence is that recognition is ~98-99% reliable rather than perfect, so every input path needs a "not confident — ask the user" fallback rather than silently guessing.

## Architecture

Four stages in a chain, each ignorant of the others' internals:

```
emulator window  →  YgoFm.Vision  →  card ids  →  YgoFm.Core  →  suggestions  →  YgoFm.Overlay
                    (capture, cut,                (fusion search,               (transparent
                     recognise)                    scoring)                      always-on-top)
```

Only `YgoFm.Vision` (built) exists today. `YgoFm.Core` and `YgoFm.Overlay` are planned.

### The two-level normalized layout — the central idea

Nothing is ever stored in screen pixels. [CaptureLayout.cs](src/YgoFm.Vision/CaptureLayout.cs) holds two levels of [NormRect](src/YgoFm.Vision/NormRect.cs) (a box expressed as 0..1 proportions of a parent box):

- **`Viewport`** — proportions of the captured frame. Where the game picture sits inside the window, excluding borders, menu bars and letterbox black bars. Emulator- and window-size-specific.
- **`Regions`** — proportions of the *viewport*. Hand slots, card-name panel, later the field slots. These are properties of the game rather than of any window, so **these values are portable across emulators and scales, and can ship as defaults.**

That split is why resizing the emulator window only invalidates one box, and why one user's calibration is useful to every other user. Preserve it. Region names live in [RegionNames.cs](src/YgoFm.Vision/RegionNames.cs), which also drives the cut tool's checklist order and hint text — add new regions there, not in the UI.

### Capture

[ScreenCapture.cs](src/YgoFm.Vision/ScreenCapture.cs) copies pixels from the composited desktop (`Graphics.CopyFromScreen`). Two consequences worth knowing:

- The target window must be raised first (`BringToFront`), because anything overlapping it would be captured instead.
- Some graphics backends refuse to be read this way and yield a solid colour. `LooksBlank` detects that and the UI reports it, rather than letting the user calibrate against nothing. **If this turns out to fail on common emulators, the escalation is `Windows.Graphics.Capture` (WinRT), which asks the compositor for a window's contents** — that needs the target framework moved to `net10.0-windows10.0.19041.0`.

[WindowFinder.cs](src/YgoFm.Vision/WindowFinder.cs) enumerates visible windows. It contains a list of known emulator process names used **only for sort order** — never for behaviour. Keep it that way.

### Recognition strategy (not yet built)

Two paths, and the interesting part is how they relate:

1. **Text recognition of the card-name panel** — the game prints the hovered card's name; read it and fuzzy-match against the 722 card names. Robust, but only reveals the one card under the cursor.
2. **Artwork template matching of the hand slots** — reads all five at once, instantly.

Emulator smoothing filters, shaders and internal upscaling change artwork pixels, so a shipped template library would not match every setup. The plan is therefore that **path 1 teaches path 2**: while the player scrolls their hand, pair the recognized name with the artwork crop under the cursor and save it. The library gets built on the user's machine, from their own emulator's rendering, automatically. Text survives filters far better than artwork, which is why the text path is the teacher.

### Fusion engine (not yet built)

Two data files: card stats for all 722 cards, and the fusion table (material pair → result).

**The game's fusion rule is sequential**, so the engine must search *orderings*, not combinations: cards are played left to right, and each new card tries to fuse with what is already on the field. A five-card hand is 320 orderings — small enough to brute-force exhaustively every frame, so do that rather than being clever.

These specific mechanics are **assumed and not yet verified against the game** — confirm before building on them:

- If the fusion table has no entry for the pair, the fusion fails and the newly placed card becomes the field monster; the earlier one is lost.
- Equipment cards do not fuse; they add attack points to the monster they land on.
- Fusion material pairs are order-independent (`A+B` == `B+A`), so the table should be stored both ways.

"Best" is not simply highest attack. Scoring must weigh attack (plus equipment bonuses), **Guardian Star matchup against the opponent's face-up monsters** (a favourable star can beat a stronger monster), cards spent, and an outright-win check that overrides everything. Keep scoring in its own unit with adjustable weights, separate from fusion resolution.

Community fusion lists contain transcription errors. Test the engine against fusions from published guides — a disagreement means bad data, and finding it in a test beats finding it mid-duel. The authoritative alternative is extracting the tables from the user's own disc image.

## Windows Forms / WPF interop gotchas

`YgoFm.Vision` sets `UseWindowsForms` (for `Screen.AllScreens` and `System.Drawing`). `YgoFm.Calibrator` deliberately does **not** — enabling both would import global usings whose `Application`, `MouseEventArgs` and `KeyEventArgs` collide with WPF's. Both flags resolve to the same `Microsoft.WindowsDesktop.App` framework reference, so `System.Drawing` remains available either way.

In WPF files, **do not `using System.Drawing;`** — its `Brushes`, `Color` and `Rectangle` collide with the WPF types. Alias the few needed types instead, as [MainWindow.xaml.cs](src/YgoFm.Calibrator/MainWindow.xaml.cs) does.

The Calibrator ships an [app.manifest](src/YgoFm.Calibrator/app.manifest) declaring per-monitor DPI (Dots Per Inch) awareness, so screen pixel coordinates are not silently rescaled on displays running above 100% scaling. Any future capturing executable needs the same.

## Verification habit

This project is hard to unit-test end to end, so the cut tool exports every calibrated region as a PNG file (`data/captures/<timestamp>/`) for the user to eyeball. When adding vision features, add the equivalent visual escape hatch rather than relying on assertions alone — being able to look at what the code actually cut out is how calibration bugs get found.
