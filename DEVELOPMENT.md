# Developer Notes

This plugin controls MusicBee through [MBXHub](https://mbxhub.com) and its [API](https://mbxhub.com/api.html).
For end-user installation and configuration, see [README.md](README.md).

## Build

Use `./build.ps1` on Windows or `./build.sh` where Bash is available. Both scripts build and package a
`.macroDeckPlugin` artifact with the `macrodeck-plugin` CLI when it is installed. Otherwise, they fall
back to an unpacked `dotnet publish`, which is not a Store-ready artifact.

The plugin is framework-dependent and uses the .NET 10 runtime shipped with Macro Deck. The project
pins Macro Deck packages to stable `3.0.2`; the release workflow keeps the plugin CLI on its tested
`3.0.0` release. Keep all Macro Deck packages aligned with the current Store publishing requirements.

## Architecture

`Program.cs` configures the plugin host and registers `MbxHubIntegration`.
`MbxHubIntegration.cs` owns lifecycle, configuration, actions, variables, and the music player.
`Player/MbxHubMusicPlayer.cs` implements the Music Player capability and playlist catalogue.
`Actions/` contains user-triggered actions, `Config/` contains the MBXHub connection flow, and
`Mbx/` contains the HTTP client and response models.

## API assumptions

MBXHub has no authentication, so the client uses an unauthenticated `HttpClient`. See the network
warning in [README.md](README.md).

Confirmed against MBXHub 0.5.5.1-RC4 and the current MBXHub API reference:

- `GET /system/version` returns the MBXHub version and server host. MusicBee's own version is not
  exposed by the API, so the plugin reads the local MusicBee executable version only when the
  reported MBXHub host matches the Macro Deck computer; remote MusicBee version is unavailable.
- `GET /player/volume` and `GET /player/shuffle` return `data.volume` and `data.shuffle`.
- `PUT /player/position` accepts `{ "position": milliseconds }`.
- `GET /player/repeat` returns `data.repeat`; `none`, `one`, and `all` map to the SDK's
  `RepeatMode.Off`, `Track`, and `Context`.
- `/playlists` uses the `name` item field, with `title` retained as a fallback.

The volume action uses the SDK's slider parameter. The love action reports loved and not-loved
states through `IStateProviderActionDefinition`; it reports no state when MBXHub is disconnected or
no track is playing.

The writable `mbxhub_volume` numeric variable is also exposed for Macro Deck's Slider widget. Its
read range is 0-100%, and writes are rounded to the nearest whole MusicBee volume percentage.
