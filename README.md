# InariIX.MbxHubPlugin

Macro Deck 3 plugin controlling MusicBee through [MBXHub](https://mbxhub.com) (docs:
https://mbxhub.com/docs.html, API: https://mbxhub.com/api.html).

**Just want to install and use this?** See [SETUP.md](SETUP.md) instead — this file covers the
architecture, build process, and the API assumptions behind it.

## What it does

- **Music Player widget** (album art, progress bar, transport) via `ICatalogMusicPlayer`
  (`Player/MbxHubMusicPlayer.cs`) — this is the SDK's built-in surface for exactly this, so no custom
  Macro Deck UI was needed for it.
- **Transport**: play / pause / toggle / next / previous / seek / volume, all through the same
  interface, and reachable from any button bound to it.
- **Shuffle**: part of the same interface, plus a standalone `Toggle shuffle` button action.
- **Love toggle**: a standalone `Toggle love` action + an `mbxhub_love` variable. Not part of the
  Music Player widget — see "Known gaps" below for why.
- **Playlist selection**: a `Play playlist` action with a picker populated live from MBXHub, plus
  playlist browsing built into the widget's own catalogue browser.

## Setup

1. Build with `./build.ps1` (Windows) or `./build.sh` (macOS/Linux) — see below — which packs a
   `.macroDeckPlugin` artifact per platform via the `macrodeck-plugin` CLI (or falls back to plain
   `dotnet publish` if the CLI isn't installed). Published framework-dependent per AGENTS.md's
   convention (Macro Deck ships its own .NET 10 runtime with the host), not self-contained. The
   CLI fills in `files[]` and `signature` itself from the packed artifact — never hand-author those
   in `manifest.json`.
2. Install in Macro Deck 3, then run the config flow: host (default `localhost`) and port (pre-filled
   `8082` since MBXHub's own default, 8080, is commonly already taken — check yours if unsure).
3. Add the "Music player" widget and pick the MBXHub instance, or wire the actions to buttons directly.

## Known gaps / things to verify before relying on this in production

MBXHub has no authentication, so this client is a plain unauthenticated `HttpClient` — fine on a
trusted LAN, worth knowing if you're exposing the host more broadly.

A few call shapes are best-effort inferences rather than confirmed against MBXHub's own docs, each
flagged with a comment at the point of use:

- **`GET /player/volume` and `GET /player/shuffle` response field names** (`Mbx/MbxHubModels.cs`) —
  the docs show the `PUT` body but not the matching `GET` response shape; assumed symmetric.
- **`PUT /player/position` body field name** (`Mbx/MbxHubClient.cs`, `SeekAsync`) — not shown in the
  docs at all; assumed to follow the same convention as volume.
- **Repeat mode SDK-enum mapping** (`Player/MbxHubMusicPlayer.cs`, `SetRepeatModeAsync`) — MBXHub's
  accepted values are now confirmed live: `"none"` / `"one"` / `"all"` (exposed read-only via the
  `mbxhub-repeat` variable too). What's *not* confirmed is the SDK's own `RepeatMode` enum member
  names for the non-`Off` cases (only `RepeatMode.Off` has ever been confirmed by exact name via
  reflection), so `SetRepeatModeAsync` fuzzy-matches `ToString()` for those rather than assuming exact
  spellings like `Track`/`All`. Good enough to drive playback correctly; not precise enough to
  populate `MusicPlayerState.RepeatMode` accurately when reporting state back (still omitted there).
- **`GET /playlists` item field name** — confirmed (`name`, not `title`) via HALRAD's own MCP sample
  in `halrad-com/mbxhub`; `title` is kept only as a defensive fallback, not because it's in doubt.

None of these will crash the plugin if wrong — they'll just no-op or read back an unexpected value —
but worth a quick check against a live MBXHub instance (e.g. `curl http://localhost:8082/player/volume`)
before you rely on volume/shuffle/seek/repeat.

## Restoring the volume slider (or fixing any other API drift)

`SetVolumeAction` was downgraded to a plain text-parameter action because `ISliderActionDefinition` and
`SliderActionState` don't exist under those names in the actually-published `MacroDeck.Sdk` package -
the `Macro-Deck-Sample-Plugins` repo's checked-out source (what this plugin was originally built
against) had drifted from what NuGet currently serves. `IVariableProvider` had the same kind of drift
(`ProvidedVariables`/`GetValueAsync` → `Variables`/`ReadAsync`), but the compiler's `CS0535` errors
spelled out the new names directly, so that one didn't need investigation - only the two vanished
slider types did.

To find what they're actually called now, inspect the real DLL you already have on disk (same
principle as using ILSpy on the Macro Deck app itself - the published package is the source of truth,
not any cached copy of sample source):

```powershell
$dll = Get-ChildItem "$env:USERPROFILE\.nuget\packages\macrodeck.sdk" -Recurse -Filter MacroDeck.Sdk.dll |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
[Reflection.Assembly]::LoadFrom($dll.FullName).GetTypes() |
    Where-Object { $_.Name -match 'Slider' } |
    Select-Object FullName
```

That lists every type with "Slider" in the name (interface, state record, whatever it's called now).
Once you have the real name(s), `SetVolumeAction.cs` can go back to the two-way-binding shape - same
structure as `MacroDeck.SampleMusicPlayerPlugin/Actions/SetVolumeAction.cs`, just with the corrected
type name(s) and members swapped in.

The same `[Reflection.Assembly]::LoadFrom(...).GetTypes()` approach works for any other CS0246 that
shows up later: swap the `-match` pattern for a keyword from the missing type's name.

**Live 2-state love button**: a heart icon that visually flips loved/not-loved on the button itself
(rather than a plain toggle) would use `IStateProviderActionDefinition`. No sample plugin in the
`Macro-Deck-Sample-Plugins` repo demonstrates that interface, so its exact members weren't confirmed
before writing this — worth checking with the reflection approach above, or `sdk-reference.md` in the
main Macro Deck 3 repo (private at the time this was written), before implementing it.

