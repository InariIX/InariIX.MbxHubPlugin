# Developer Notes

This plugin controls MusicBee through [MBXHub](https://mbxhub.com) and its [API](https://mbxhub.com/api.html).
For end-user installation and configuration, see [README.md](README.md).

## Build

Use `./build.ps1` on Windows or `./build.sh` where Bash is available. Both scripts build and package a
`.macroDeckPlugin` artifact with the `macrodeck-plugin` CLI when it is installed. Otherwise, they fall
back to an unpacked `dotnet publish`, which is not a Store-ready artifact.

The plugin is framework-dependent and uses the .NET 10 runtime shipped with Macro Deck. The project
pins Macro Deck packages to `3.0.0-beta.17`; keep the package set aligned with the current Store
publishing requirements.

## Architecture

`Program.cs` configures the plugin host and registers `MbxHubIntegration`.
`MbxHubIntegration.cs` owns lifecycle, configuration, actions, variables, and the music player.
`Player/MbxHubMusicPlayer.cs` implements the Music Player capability and playlist catalogue.
`Actions/` contains user-triggered actions, `Config/` contains the MBXHub connection flow, and
`Mbx/` contains the HTTP client and response models.

## API assumptions

MBXHub has no authentication, so the client uses an unauthenticated `HttpClient`. See the network
warning in [README.md](README.md).

Some response and request shapes are assumptions based on the documented API:

- `GET /player/volume` and `GET /player/shuffle` response field names are assumed to match their
  corresponding `PUT` request fields.
- `PUT /player/position` uses a `position` field in milliseconds; the API documentation does not
  specify this request body.
- Repeat values `"none"`, `"one"`, and `"all"` map to the SDK's `RepeatMode.Off`, `Track`, and
  `Context`.
- `/playlists` uses the `name` item field, with `title` retained as a fallback.

Verify these behaviors against a live MBXHub instance before relying on them. Incorrect API
assumptions can result in controls or displayed state not working as expected.

The volume action uses the SDK's slider parameter. The love action reports loved and not-loved
states through `IStateProviderActionDefinition`; it reports no state when MBXHub is disconnected or
no track is playing.
