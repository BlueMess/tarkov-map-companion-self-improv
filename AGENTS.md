# Tarkov Map Companion — agent notes

## What this is

A Windows desktop companion for Escape from Tarkov. It reads the **filenames** of screenshots the
game already writes, finds the player on a map from them, and draws a guide line to an exit. It
never reads memory, hooks the process, or talks to the game, and it uploads nothing.

## Running it

The .NET 8 SDK here is user-local and **not on PATH** — every session needs:

```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"
dotnet test
dotnet run --project src/TarkovMapCompanion     # or: Run.bat
scripts\publish.ps1                             # single-file publish -> publish\
```

`global.json` pins 8.0.421 (`rollForward: latestFeature`); without it nothing builds. To diagnose
without a window: `--render-test`, `--read-exits`, `--find-screenshots`, `--find-logs`.

**Run those diagnostics through `dotnet run`, not `publish\TarkovMapCompanion.exe`.** The project is
`OutputType=WinExe` (rightly — no console window when the user double-clicks it), and a GUI-subsystem
process started with PowerShell's `&` operator is neither connected to a pipe nor waited for: you get
no output *and* no `$LASTEXITCODE`. The app is fine — it writes to the standard handles correctly.
Measured, all four ways, same 584.6 KB PNG:

| Invocation | stdout | exit code |
|---|---|---|
| `dotnet run --project src\TarkovMapCompanion` (what CI and `Run.bat` use) | full | `0` |
| `& publish\TarkovMapCompanion.exe` | **empty** | **unset** |
| `Start-Process -Wait -PassThru` | full | `0` |
| `Start-Process -RedirectStandardOutput` | full | — |

So the published exe *is* scriptable — just not with `&`. Use `Start-Process -Wait -PassThru`, or
verify the shipped build by running its managed dll: `dotnet publish\…` → the `TarkovMapCompanion.dll`
under `bin\Release\net8.0-windows10.0.19041.0\win-x64\`, which is the same IL through a console host.
CI is unaffected: it already uses `dotnet run`, and asserts both the exit code and the output file.

**Re-run `scripts\publish.ps1` after every change that should be visible in the app.** `publish\` is
gitignored, so it silently goes stale and the user keeps launching a binary from before the fix — a
WebSync LAN-address fix was verified in the Debug build while the shipped `publish\` exe still had
the old behaviour. The user runs the published exe for hands-on testing, so a green `dotnet test`
does not mean what they are running is current.

## Releasing

A release is a `v*` tag; `.github/workflows/release.yml` builds it and creates the GitHub release.

```
git tag -a v0.13.1 -m "..."     # on the commit you want to ship
git push origin v0.13.1        # -> Release workflow -> GitHub release with the exe
```

- The workflow stamps the version from the tag, so a binary built without it reports the csproj
  version (0.12.2) in the About window and the crash log.
- It takes a `tag` input, so `workflow_dispatch` can release a tag that is already pushed — no need
  to delete and re-push it.
- The repo's default `GITHUB_TOKEN` permission is read-only, but `release.yml` declares
  `permissions: contents: write` for itself, which is what lets it create the release.
- `scripts\release.ps1 -Version v0.13.1 [-Publish]` does the same thing from this machine (tests,
  publish, then `gh release create`). It needs the GitHub CLI, which is not installed here.
- **A tag push does nothing at all if Actions are off for this repo.** See the trap below.

## Stack

.NET 8 · Avalonia 11 · SkiaSharp · Svg.Skia. Windows-only (uses the built-in Windows OCR).
CI runs on `windows-latest` and includes a real `--render-test` smoke test.

## Conventions

- `src/TarkovMapCompanion/{Data,Views,Localization,Tools,Settings}` + `tests/TarkovMapCompanion.Tests/`.
- **Control `Name`s in `PreferencesWindow.axaml` are load-bearing.** The code-behind finds controls by
  name, so renaming one breaks the binding silently — the setting still saves but the control never
  updates. The 2026-10-14 rebuild into six category pages moved thirteen panels and changed no `Name`
  for exactly this reason. The dialog also applies every edit live; there is no OK/Cancel.
- **Localization keys are the contract.** `StringsEn.json` and `StringsZh.json` must have identical
  key sets. `Localizer.Pick(english, chinese)` is the one place a language decision is made; call
  sites must not branch on language themselves. Data that is *always* English (POI names, map keys,
  wiki note keys) must be reached through the English accessor — comparing it against a display
  string only works while the UI happens to be in English. The remaining contract debt is in
  `docs/localization-ocr-chinese-backlog.md`.
- **Chinese OCR now works end-to-end, and the traps that guard it are written up in
  `docs/localization-ocr-chinese-backlog.md`.** Five real Chinese-client frames live in
  `tests/screenshots/` with a README; they are **not** used by the automated tests (those feed the
  parser captured OCR *text*, which is what it actually consumes and which fails loudly instead of
  by image-diff). Run them with
  `--read-exits "<png>" <map> "ocr:zh-Hans"`; expected counts are in the README. Four things there
  are easy to re-invent the hard way:
  - **`ExitAvailability` stores the English canonical, never the matched surface string.** `Includes`
    looks up the English `Name`, so candidates have to be a surface→canonical map. Adding the
    Chinese names to the existing flat `HashSet` looks like it works, lights nothing, and still
    passes `dotnet test`.
  - **`NameMatcher.Normalize` drops *all* spaces when the string contains a CJK ideograph.** The
    `zh-Hans` engine emits a space between every Han character (`找 到 撤 离 点`), so without this
    nothing in a Chinese panel ever matches. `Normalize` also rewrites **no glyphs** — the reader's
    confusions (`弓`→`3`, and the `0`/`2`/`6`/`匠`/`亿`/`@` family) are corrected at scoring time in
    `CorrectGlyphs`/`FoldConfusables`, because `Normalize` output is stored and a future snapshot
    with 弓 in a name would be silently corrupted.
  - **A wrong map argument and a broken matcher produce identical output.** The first draft of that
    doc mislabelled 9 of 11 frames by guessing from landmarks. Resolve a name through
    `translationsZh` to a key, then check which map has that key.
  - **The tight crop is a stress harness, not a proposal.** Losslessly cropping a fixture to
    `(0.76,0)–(1.0,0.60)` and running `--read-exits <crop> <map> whole ocr:zh-Hans` shifts the
    effective OCR scale, which is how the greedy id-stripper, a countdown-regex gap and a damaged
    transit token were all found. Adopting it at runtime was measured and rejected; keep using it
    to test.
- **Do not trust the vendored data.** `Data/Snapshots/` holds a json.tarkov.dev payload and a wiki
  scrape, and it has shipped real defects. `PoiBuilder` normalizes three (mis-assigned switch ids,
  coincident same-name extracts, code-named extracts), each pinned in `PoiTests.cs` /
  `UpstreamDataFixesTests.cs`. A refresh that changes the shape should fail the suite, not silently
  reintroduce one. `season-documents.json` is a third source with a different trust profile: its
  coordinates come from the retired pipeline's per-map formula, are exact only for spots on the main
  drawing (see the season-documents section below), and its per-point `confirmed` flag is the only
  confidence signal there is — never present an unconfirmed point as if it had been surveyed.
- `.gitattributes` is load-bearing: `*.sln`/`*.bat`/`*.ps1` CRLF, everything else LF.
- PowerShell mangles UTF-8 here. Read or compare JSON with `python`, not `ConvertFrom-Json`.

## Current state

Fork of `Battleroid/tarkov-map-companion`, pushed to `BlueMess/tarkov-map-companion-self-improv`.
**All work lands on `master`; there are no feature branches left.** It has delivered WebSync, the
vendored-data fixes, Simplified Chinese (`40613e8`), the preferences dialog rebuilt as a
six-category rail, Simplified Chinese as the client default, **Chinese-client OCR end-to-end**
(separate game-client language setting, CJK parsing, surface→canonical candidates, measured
thresholds), and **floor-bound season documents with hand-calibrated the-lab and interchange
coordinates** (784 tests green). Build clean.

The fork's first release is **v0.13.0** (2026-10-19, `7f14ad8`), published by GitHub Actions.
Actions are enabled for this repo now — see the trap below, because that was not true when the
first tag was pushed.

The UI language defaults to Simplified Chinese, set in three places that must move together —
`AppSettings.Language`, the `Normalize()` fallback for a hand-edited file, and the `Localizer`
static initializer. An existing `settings.json` that names a language keeps it, so only new installs
move.

`AppSettings.OcrLanguage` — the *game client's* language, not the UI's — now defaults to Simplified
Chinese too, again in two places that must move together: the property initializer and its own
`Normalize()` guard. It is still an independent setting (a Chinese client with an English UI is the
case it exists for) and it is **applied live** — changing it drops the cached OCR reader, so a raid
in progress picks it up — whereas the UI language needs a restart. The control lives on the
**General / 常规** page under Appearance, not on Display; its `Name`s (`OcrLanguageLabel`,
`OcrLanguageBox`, `OcrLanguageHint`) are load-bearing and moving a panel must not rename them.
Because `SettingsStore.Save` writes the whole `AppSettings` (enums as names — `UseStringEnumConverter`,
so a file reads `"ocrLanguage": "English"`, not `0`), an install that has run the app since the field
shipped already names a language and keeps it, i.e. stays on English; only fresh installs and
pre-feature files that never had the key move.

WebSync is an embedded, read-only LAN mirror of the map, built as
M1-M4 (skeleton + snapshot + token, basemap endpoints, WebSocket + client page, settings pane + QR)
and then hardened against a real phone. `src/TarkovMapCompanion/WebSync/` holds
a Kestrel server (`WebSyncServer`, all host work on a null-`SynchronizationContext` worker behind a
serialized reconciler, 2 s bounded stop), a `StateBroadcaster` (snapshot builder + 2 Hz player
throttle + WS client table), the DTOs (`WebSyncState`), `LanAddress`, `QrCode`, and the
single-file browser client (`Assets/index.html`). Spec and its corrections:
`docs/websync-implementation-spec.md`. Routes: `GET /`, `/api/state`, `/map/{id}.svg`,
`/tiles/{id}/{z}/{x}/{y}.png`, `WS /ws`; every route but `/` and `/assets/*` requires `?key=`, else
404. Wire the enable switch through `MapSession.ApplyWebSyncSettings()`. Tiles are cached under
`Path.Combine("tiles", NormalizedName)` to match `TileMapSource` (the bare `"tiles"` category is
wrong). Publish is now ~62 MB (the ASP.NET Core shared framework).

**Season documents ship as one layer, bound to the floor toggles.** `PoiKind.SeasonDocuments` draws
season-1 ("KORD BREACH") TerraGroup document spawns from `Data/Snapshots/season-documents.json` —
schema v2, **242 points over 11 maps**, drawn as a solid amber dot when the source has verified one
and as a hollow amber ring with a dashed halo when it has not (no "?" glyph: at 9 px it was
illegible and read as a blot). It rides the existing
`Enum.GetValues<PoiKind>()` switch generation and is independent of quest selection.
`AppSettings` went to version 3 so an older `poiLayers` map backfills the new key instead of leaving
the layer dark.

**A season point is only drawn while its floor is switched on.** `MapPoi.FloorName` holds the map's
own `MapFloor.Name`; `null` means the ground layer, which is the **地面** (base-layer) toggle. The
filter lives in `PoiOverlay.OnActiveFloor` and is applied in `DrawOne` *and* `HitTest` — a marker on
a switched-off floor must not answer a click. `MapSession.ApplyFloorState` pushes the canvas's
`ActiveFloors`/`ShowBaseLayer` into the overlay on every toggle and on every map load. **Only the
season layer is filtered**: a `null` floor on an extract, loot or spawn must not be read as "ground",
or switching to an underground level would empty the map. The WebSync client mirrors the same
predicate against its own floor selection.

**The 334-point v1 snapshot is frozen at `_work/season-docs/season-documents.v1.json` — do not
delete it.** Its ids encode each entry's index in the scrape it was built from
(`season_doc_<map>_<NNN>`), which is the only join back to the source pixels, and that join is how a
regenerated export recovers the world coordinates the app already ships. Reading the file being
regenerated would lose the join on the second run.

**`--render-test` must tick every floor** (`RenderTest.cs` does, deliberately): with the defaults the
harness would draw only the ground layer, and the `SeasonDocuments=<n>` smoke check would stop
matching what is on the image — which is the one thing that harness exists to catch.

**Regenerating the snapshot: `_work/season-docs/rebuild_season_docs.py` + `inset_regions.json`.**
Neither is version-controlled. It re-fetches `/data/s1/<map>.json` from tarkovdoc.com, then:
- drops transit spots (the app draws its own `Transit` POIs);
- drops every spot whose pixel falls on a **detached floor plan** — the source posters draw upper
  floors, interiors and basements as separate panels in the empty space around the main drawing, so
  those pixel coordinates belong to a smaller drawing and cannot be transformed. `inset_regions.json`
  encodes them per map as either `keepOnly` (playable drawing is a subset of the canvas) or `insets`
  (full-bleed poster, plans painted into corners) — and **ground-zero needs both**, because eight
  plans float *inside* the drawing's bounding box;
- drops every spot whose floor the app has no toggle for (lighthouse 2F/3F, interchange B1);
- keeps the existing coordinates where it has them, and places the new spots with a per-map affine
  fitted on the kept points. If that fit does not reproduce the kept coordinates (RMS > 15 world
  units) the new spots are reported and skipped rather than guessed.

The old `scrape → namefit → merge → export_companion.mjs` chain produced the v1 file and is
superseded as a coordinate source. Its calibration is the reason so many v1 points were unusable: the
"anchors" it fitted were named `formula-*`, i.e. their poster pixels were **derived from a formula**,
never measured, and it then snapped 90-odd points onto building labels (two of them onto
`(38.7, 264.7)`). `floor-building-inventory.md` in the same directory records what the source data
actually carries (floor yes; building only in free text).

**Two maps' coordinates are measured by hand, not inherited: the-lab and interchange.**
`panel_calibration.json` holds one affine per poster panel, fitted from control points a human picked
in `_work/season-docs/calibrate/index.html` (poster ↔ the app's own rendered basemap; the page exports
the pairs as JSON). `rebuild_season_docs.py` uses those affines for every spot inside the panel's bbox
and nothing else — no fallback to the old formula. Both posters split into panels that are *separate
frames*: the lab draws FIRST/SECOND/TECHNICAL level side by side (TECHNICAL carries no spawns), and
interchange's mall plans are a frame of their own — the outdoor aerial is a different one, so that map
needs both panels calibrated before its points are complete. Sample sizes are small (6–8 pairs), so
treat a panel's RMS as the honest error bar: 0.3–3.7 world units across the four panels so far.

**Every `PoiBuilder.Build` call site must pass the store.** The layer has no other entry point, so a
call site that omits `seasonDocuments` builds a map with the layer silently missing — no error, no
warning, just absent markers. `--render-test` prints per-kind POI counts and `SeasonDocuments=<n>` in
that line is the smoke check; it caught exactly this after the main path was already wired.

### Traps this branch paid for (do not rediscover them)

- **`_work/season-docs/maps/*/map.png` is not the source poster.** It is the retired project's own
  artwork (interchange 3000×2506 against the poster's 7680×4320; ground-zero's copy is a tarkov.dev
  SVG raster). `inset_bboxes.json`, written against it, is therefore wrong for the real posters and
  was replaced by `inset_regions.json`, which was drawn against `posters/<map>.webp` and verified by
  plotting every spot back onto the poster.
- **A `keepOnly` box cannot exclude a plan that floats inside the drawing's bounding box.**
  ground-zero needs eight extra `insets` entries for 2F/3F cards that sit inside it; without them a
  spawn on one of those cards is silently kept at the wrong scale.
- **On full-bleed posters the plans are painted on top of the terrain** (customs, factory), so "the
  pixel is inside the drawing" does not mean "the pixel is in map space". Judge by the line-work —
  room numbers, `1F`/`2F` labels, cream page borders — not by the background colour.
- **Do not classify a poster as "flat" from the picture alone — check the sibling site's floor
  alignment.** tarkovdocsmap.com stores every map's floors/interiors as separate images plus an
  `overlayX/overlayY` telling where each sits on the main canvas: `(0,0)` for all four factory floors
  means that map is *stacked*, and treating it as a collage deleted three of its four B1 spawns.
  Offset entries (customs' four buildings, interchange's three floors, streets' and reserve's cards)
  are panels. Even this is only evidence, not proof: lab/ground-zero/shoreline/lighthouse ship no
  sub-frames at all and were classified from the drawing, so a report of "this one is not flat" from
  someone looking at the real page outranks the mask.
- **A clean affine fit does not mean the coordinates are right.** Every map's kept point set fits at
  RMS 0.0 both before and after removing panel points, because the retired pipeline applied one
  formula to *every* spot, panel space included. Decide with the mask and the spot titles, never with
  the fit residual alone.

- **A tag push to a fork whose upstream already had workflow files does nothing, silently.** This
  repo is a fork, and GitHub starts forks with Actions *disabled* when the source repo shipped
  `.github/workflows`. Nothing signals it: the API lists both workflows as `active`, and
  Settings → Actions → General already says "Allow all actions". The only visible sign is a banner
  on the Actions tab — "Workflows aren't being run on this forked repository" — and
  `GET /repos/{owner}/{repo}/actions/runs` returning **0 runs** after pushes that should have run
  CI. A `v*` tag pushed in that state creates no workflow, no release, and no error anywhere; the
  tag just sits there. Fixed on 2026-10-19 by clicking "I understand my workflows, go ahead and
  enable them" on the Actions tab (it is *not* in the settings pages), after which the tag push
  worked. **Check the run count before believing a release happened.**
- **The GitHub API's anonymous rate limit is 60 requests an hour, and a polling watcher burns it
  in minutes.** A release wait loop that polls `/actions/runs` every 45 s gets 403'd and then reads
  "no run yet" for the rest of its life, which looks exactly like a broken workflow. Poll the
  releases page, or HEAD the asset URL
  (`…/releases/download/v0.13.0/TarkovMapCompanion-v0.13.0-win-x64.exe`, which redirects to
  `release-assets.githubusercontent.com` and answers with the real `Content-Length`).
- **The client page had no cache headers and nobody noticed for several rounds.** `GET /` serves an
  embedded resource whose URL never changes, so browsers heuristically cached the HTML and every
  fix appeared to do nothing on the phone while local testing looked green. It now sends
  `Cache-Control: no-store, must-revalidate` + `Pragma: no-cache`. If a client change "does not take
  effect", check this first, then the published exe, before reading any drawing code.
- **`position:fixed; inset:0` sizes to the layout viewport, not the visual one.** On a phone the URL
  bar makes them differ, so a canvas sized by `inset:0` is displayed taller than the `innerHeight`
  its backing store and `setTransform(dpr,…)` assumed — every marker stretched while the DOM
  artwork was not. `#stage` and `#cv` are now pinned to the measured `vw`/`vh` in `measure()`.
  Desktop never reproduces this: the two viewports coincide.
- **Headless Chrome is not a phone.** `--window-size=390,844 --force-device-scale-factor=2` yields a
  780x1688 PNG while the page lays out at `innerWidth=491` — the screenshot silently crops the page,
  which invents "the artwork is clipped" findings. Always confirm `innerWidth`/`innerHeight`/`dpr`
  from the page before trusting a headless capture, and prefer the real device for anything visual.
- **`document.importNode` copies; `appendChild` moves.** Draining a parsed document with
  `while(root.firstChild) f.appendChild(document.importNode(root.firstChild,true))` never
  terminates and freezes the main thread — timers stop, so *no* timeout fires and the page just looks
  stuck on "loading".
- **Canvas 2D state is cumulative.** `save()/restore()` pairs can be balanced and still leak
  `globalAlpha`/`lineWidth`/`font`/`fillStyle`. Everything drawn on the marker layer now runs inside a
  `scoped()` save/restore, and `draw()` resets its transform and paint baseline first.
- **Sizes copied from the desktop are not phone sizes.** `PoiOverlay`'s radii are CSS pixels on a
  ~1920 px window; on a 366 px phone they read ~5x too large. The client scales marker geometry by
  `clamp(vw/1000, 0.6, 1.0)`.
- **The client has no JS test coverage at all.** The tests exercise the server; a single line of
  client JavaScript froze the page for several releases and nothing caught it. `?selfcheck=1` asserts
  the canvas transform survives a rotate and a dashed ring; that is the only automated guard, and it
  is thin.
- **Round-trip the map data, not just the formulas.** `gameToBase` reproduces `baseRect` exactly from
  the snapshot's `bounds`, so a coordinate bug is not where a misalignment lives. What can differ is
  the rect the artwork is stretched onto, the layer's own sizing, and the viewport the two layers are
  measured against.

Rendering cost is a known, **unaddressed** problem: measured 15-43 ms per frame (headless Skia CPU,
1600x1100; shoreline full overlays 43.13, bare 14.82), and the 20 fps `MapClock` runs continuously
whenever `WaypointOverlay.Advance()` sees >= 2 waypoints (`AnimateArrows` defaults true), i.e. ~86%
of a core for the whole raid. Idle is ~0 (event-driven repaints). WebSync is *not* the cost driver.
Findings and the prioritized optimization list (P1 arrow animation, P2 per-frame overlay allocs +
O(n^2) `LabelPlacer`, P3 re-rasterize on zoom, P4 memory, P5 background chores) are written up in
`docs/render-performance-backlog.md` — that file is the brief for a future optimization pass, not
finished work. Re-measure with `--render-test <map> <out.png> 1600 1100 [bare]` before and after.

## Open questions

- **Trailer Park** — two coincident records carry different Chinese names (拖车停车场 /
  拖车停车场工人棚屋) and research says they are two exits, but the merge folds them into one. Owner
  chose to keep current behaviour.
- **`Gate Near Medblock` -> `17号仓库`**, **`Reserve Checkpoint` -> `Scav检查站`** — upstream's English
  and Chinese disagree for those keys; left as-is because the in-game Chinese could not be verified
  without a game install. Needs someone who can read the client.