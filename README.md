# InariIX.MbxHubPlugin

Macro Deck 3 plugin controlling MusicBee through [MBXHub](https://mbxhub.com) (docs:
https://mbxhub.com/docs.html, API: https://mbxhub.com/api.html).

**Just want to install and use this?** See [SETUP.md](SETUP.md) instead — this file covers the
architecture, build process, and the API assumptions behind it.

## What it does

This plugin controls MusicBee only through MBXHub. It provides basic playback controls, a Loved
toggle, and playlist selection. Party Mode and AutoQ are not implemented. The plugin's code was
developed with AI assistance.

The Loved toggle is a standalone action and variable, not part of the Music Player widget. For it to
work, make sure **Tag edits** and **Modify** are writable (not read-only) in MBXHub's **API Access**
settings.

The Music Player widget provides album art, playback progress, transport controls, and playlist
browsing.

## Setup

1. Build the Windows x64 plugin with `./build.ps1` or `./build.sh`. The scripts pack a
   `.macroDeckPlugin` artifact via the `macrodeck-plugin` CLI (or fall back to plain `dotnet publish`
   if the CLI isn't installed). The plugin is framework-dependent (Macro Deck ships its own .NET 10
   runtime with the host), not self-contained. The project defaults to Macro Deck 3 SDK
   `3.0.0-beta.17`; override `MacroDeckSdkVersion` at build time to test another SDK version. The CLI
   fills in `files[]` and `signature` itself from the packed artifact — never hand-author those in
   `manifest.json`.
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
  accepted values are `"none"` / `"one"` / `"all"` (also exposed through the `mbxhub-repeat` variable).
  These map directly to the SDK beta.17 `RepeatMode.Off`, `Track`, and `Context` values.
- **`GET /playlists` item field name** — confirmed (`name`, not `title`) via HALRAD's own MCP sample
  in `halrad-com/mbxhub`; `title` is kept only as a defensive fallback, not because it's in doubt.

None of these will crash the plugin if wrong — they'll just no-op or read back an unexpected value —
but worth a quick check against a live MBXHub instance (e.g. `curl http://localhost:8082/player/volume`)
before you rely on volume/shuffle/seek/repeat.

## SDK capabilities

The project pins the Macro Deck packages to `3.0.0-beta.17`. The standalone volume action uses the
SDK's native slider parameter, and the Loved action reports live Loved and Not loved states through
`IStateProviderActionDefinition`. It reports no state when MBXHub is disconnected or no track is
playing. The Music Player widget continues to provide its own transport, volume, shuffle, repeat, and
playlist controls.
