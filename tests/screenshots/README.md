# Chinese-client extraction panel fixtures

Real frames from a Simplified Chinese *Escape from Tarkov* client, 3440x1440, captured with the
in-game extraction panel open. They are the reference material for
`docs/localization-ocr-chinese-backlog.md` and the only way to check a change against the real thing:
every judgement call in `Vision/ExtractPanelParser.cs`, `Vision/NameMatcher.cs` and
`Vision/ExitAvailability.cs` came out of reading these.

**These are not used by the automated tests** — with one exception, added later: the *filenames*
are (see "The same frames are floor fixtures" below). The OCR tests feed the parser *captured OCR
text* — the lines `WindowsOcrTextReader` actually returned — because that is what the parser really
consumes, and a text fixture fails loudly instead of failing by image-diff. The images are for a
human or an agent to run end-to-end.

## Running one

```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; $env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"
dotnet run --project src/TarkovMapCompanion -- --read-exits "<file.png>" <map> "ocr:zh-Hans"
```

Use the **default crop** (omit `whole`) — that is the shipping path. Add `ocr:en-US` to see the
failure this whole feature exists to fix.

Map names are `MapCatalog` normalized names, so it is `streets-of-tarkov`, not `streets`.

Expected results, all `panel found: True`:

| # | File | Map | Side | Exits | Matched |
|---|---|---|---|---|---|
| 1 | `2026-10-12[20-52]_618.08, 1.63, -102.96_…png` | Customs | PMC | 6 + 4 transit | 9 / 26 |
| 2 | `2026-10-13[19-48]_121.68, 24.31, -1.07_…png` | Ground Zero | PMC | 4 + 1 transit | 5 / 10 |
| 3 | `2026-10-16[18-14]_-31.86, -1.12, 32.79_…png` | Factory | Scav | 3 + 3 transit | 6 / 12 |
| 4 | `2026-10-17[11-32]_-150.62, 2.56, 500.42_…png` | Streets of Tarkov | PMC | 6 + 3 transit | 9 / 22 |
| 5 | `2026-10-18[15-16]_41.46, -1.13, -8.61_…png` | Factory | PMC | 5 + 3 transit | 7 / 12 |

The filenames carry the player position, compass heading and camera orientation, which is why they
look like noise.

## Which frame was kept, and why

Eleven frames were captured. Four pairs and two extra frames showed an **identical exit list**, and
were dropped; for each surviving group the representative was chosen to be the one that is hardest
for the matcher, not the one that reads cleanest:

- **Factory / Scav** — one frame read `3号门` perfectly, three read it as `弓号门`. The `弓` frame was
  kept, because that misreading is what established `弓` as a `3` rather than as a `0`.
- **Streets of Tarkov** — one frame read `Pinewood` clean, one read it damaged enough to score 0.500
  and fail, and one scored exactly 0.750: just above the 0.70 floor. **The boundary frame was kept**,
  because a threshold change that nudges `CjkFloor` above 0.75 breaks it.
- **Factory / PMC** — both frames read the real `0号门` as `2号门`, so either represents the case.

The dropped variants lose no test coverage: their damaged readings are already pinned as text
fixtures in `tests/TarkovMapCompanion.Tests/ExtractPanelParserTests.cs`.

## The two known misses

Both are deliberate. A missed exit the player can still see on their own screen is recoverable; a
marker placed on the wrong side of the map is not.

| Frame | Read | Truth | Why it fails |
|---|---|---|---|
| 1 | `Z日一121弓` | ZB-1013 | 0.571. At 1x the `zh-Hans` engine cannot resolve the Latin-and-digit glyphs in the panel font. A 2x upscale reads `ZB-1?13`, at ~2.5x the OCR time — see the backlog. |
| 5 | `2号门` | **0号门** | Score 1.000, margin 0.000. The reader reports the panel's slashed zero as `2`, so this is byte-identical to a genuine `2号门`. Refused on purpose. |

Frame 4 also carries `前往头室` for `前往实验室` in one earlier capture; the kept frame reads it
cleanly, and the damaged form is pinned as a text fixture.

## The same frames are floor fixtures

The names carry a real position and height, so the five frames are also the fixtures for
floor-following: `RealScreenshotFloorTests` puts each name through `ScreenshotNameParser` — the app's
only input — and pins the map and the floor it resolves to. Only the *names* are read, never the
pixels, which is what makes it safe to run in CI.

```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; $env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"
dotnet run --project src/TarkovMapCompanion -- --floor-test
```

`base` below means no floor claims the position, which is the ground layer:

| # | Fixture | Map | y | Floor |
|---|---|---|---|---|
| 1 | `2026-10-12[20-52]…` | Customs | 1.63 | base |
| 2 | `2026-10-13[19-48]…` | Ground Zero | 24.31 | base — between Garage `[-1000,21]` and 2nd Floor `[26,32.3]` |
| 3 | `2026-10-16[18-14]…` | Factory | -1.12 | **Tunnels** |
| 4 | `2026-10-17[11-32]…` | Streets of Tarkov | 2.56 | base — the lowest band starts at y=10 |
| 5 | `2026-10-18[15-16]…` | Factory | -1.13 | **Tunnels** |

The probe then replays the five in order through `FloorFollow.Decide` and prints each decision, so
"a floor is switched to, the same state again is a no-op, the ground comes back" is visible rather
than asserted. Pass a folder and a map to try it on your own screenshots —
`--floor-test "<folder>" factory` — because a position alone does not name a map: Factory's capture
sits inside twelve of the thirteen maps' bounds.

The half a test cannot cover is the app *visibly* switching:
`scripts\floor-follow-demo.ps1 -Folder "<the app's screenshot folder>"` drops re-named copies of
frame 5 into that folder with a scripted walk of heights (ground → 2nd → 3rd → ground → Tunnels) and
deletes them again. The app has to be running and watching that folder first.

## Provenance

Captured by the repository owner on a Chinese client, 2026-10-12 to 2026-10-18. Game footage, kept
for the same reason the repo keeps `docs/screenshot-dark.png`: it is the only record of what the
thing being built actually has to read.