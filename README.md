# MBXHub for Macro Deck 3

Control MusicBee through MBXHub with playback actions, playlist selection, live status, and a music player widget.

## Requirements

- Windows x64.
- Macro Deck 3.
- MusicBee with the MBXHub plugin installed and running. Download MBXHub from [mbxhub.com](https://mbxhub.com/download.html).
- Network access from Macro Deck to the computer running MusicBee and MBXHub.

## Install and connect

1. Install the plugin from [Macro Deck Store: MbxHub Plugin](https://macro-deck.app/store/com.inariix.mbxhub-plugin).
2. When the plugin asks for a connection, enter the host name or IP address of the computer running MBXHub. Enter the port configured in MBXHub. The suggested port is `8082`; MBXHub may instead be using its default, `8080`.
3. Add the **Music player** widget and select the MBXHub player, or add the plugin's actions to buttons.

Macro Deck and MusicBee may be on different computers, as long as the MBXHub host and port are reachable from Macro Deck.

## Features

The Music player widget shows album art and playback progress and provides transport, volume, shuffle, repeat, and playlist controls. You can also use separate actions for play, pause, next and previous track, repeat, shuffle, volume, love, and playlist playback.

The plugin provides variables for track, artist, album, playback state, love, shuffle, repeat, and playback progress. Use them to show live status on your Macro Deck buttons.

To use the love action, open **API Access** in MBXHub settings and make sure **Tag edits** and **Modify** are not read-only.

## Limitations and network use

Party Mode and AutoQ are not supported. MBXHub does not provide authentication, so this plugin makes unauthenticated HTTP requests to the host and port you configure. Use it only on a trusted network and do not expose MBXHub to untrusted networks.

## Troubleshooting

- **The plugin cannot connect:** Check that MusicBee and MBXHub are running, and verify the configured host and port in MBXHub.
- **Love does not work:** Check that **Tag edits** and **Modify** are writable in MBXHub's **API Access** settings.
- **The player is unavailable after startup:** Recheck the connection settings and restart Macro Deck after MBXHub is running.

For implementation notes and build instructions, see [DEVELOPMENT.md](DEVELOPMENT.md).
