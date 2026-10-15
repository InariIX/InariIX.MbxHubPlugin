# InariIX.MbxHubPlugin contributor notes

This is an out-of-process Macro Deck 3 plugin that controls one MusicBee instance through MBXHub.
Keep changes specific to this integration and avoid copying unrelated Macro Deck template guidance
into this repository.

## Project map

- `Program.cs` configures the plugin host and registers `MbxHubIntegration`.
- `MbxHubIntegration.cs` owns plugin lifecycle, configuration, actions, variables, and the single
  Music Player instance.
- `Player/MbxHubMusicPlayer.cs` implements the Music Player capability and playlist catalogue.
- `Actions/` contains the user-triggered actions; `Config/` contains the MBXHub connection flow.
- `Mbx/` contains the MBXHub HTTP client and response models.
- `manifest.json` is the source of plugin identity and package metadata.
- `Assets/icon.png` is the package icon declared by the manifest. The project copies it beside the
  built executable. `GetInstances()` currently supplies only the player ID and display name; do not
  add a separate player icon without confirming the SDK and host behavior.

## Compatibility and dependencies

`Directory.Packages.props` pins the Macro Deck SDK package set through `MacroDeckSdkVersion`.
Keep all Macro Deck packages on the same version and check the current Store publishing requirements
before changing dependencies or preparing a release.

The plugin expects one MBXHub configuration and one MusicBee player. Keep configuration reads in
`InitializeAsync`, make repeated initialization safe, and dispose replaced clients.

## Build and verification

Use `./build.ps1` on Windows or `./build.sh` where Bash is available. Both scripts build and package
with `macrodeck-plugin` when it is installed, and otherwise fall back to an unpacked `dotnet publish`.
The fallback output is not a Store-ready plugin artifact.

Run `dotnet build` after code changes. Run `dotnet test` when tests are present or added, and verify
the built package contains the manifest-declared icon when changing package assets or build inputs.

Do not commit generated `bin/`, `obj/`, or `artifacts/` output.
