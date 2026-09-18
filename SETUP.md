# Setup Guide

MBXHub (MusicBee) is a Macro Deck 3 plugin that controls MusicBee through [MBXHub](https://mbxhub.com):
play/pause, next/previous, love, shuffle, repeat, volume, playlist selection, and a Music Player
widget showing album art and progress. This guide covers installing and configuring it — for the
technical/architecture writeup see [README.md](README.md).

## Requirements

- **Macro Deck 3** (beta), any version `3.0.0` or a prerelease of it.
- **MusicBee** with the **MBXHub** plugin installed and running. Get it from
  [mbxhub.com](https://mbxhub.com/download.html) if you don't have it yet.
- Macro Deck and MusicBee can be on the same machine or different machines on the same network — MBXHub
  just needs to be reachable over HTTP from wherever Macro Deck runs.

## 1. Install the plugin

1. Download the `.macroDeckPlugin` file for your platform from the
   [Releases](https://github.com/InariIX/mbxhub-plugin/releases) page.
2. In Macro Deck, open the plugin manager and install from the downloaded file.
3. Restart Macro Deck if prompted.

## 2. Connect it to MBXHub

The first time the plugin loads, it'll ask you to set up a connection:

| Field | What to enter |
|---|---|
| **Host** | `localhost` if MusicBee/MBXHub is on the same machine as Macro Deck, otherwise that machine's IP address or hostname. |
| **Port** | MBXHub's port. Pre-filled as `8082` since `8080` (MBXHub's own default) is commonly already taken by something else — check your MBXHub settings if you're not sure which port it's using. |

You can find MBXHub's actual port by opening `http://localhost:8080` (or whichever port you set it to)
in a browser on the MusicBee machine — if that doesn't load, try `8082` through `8098`.

## 3. Add controls to your deck

Once connected, you have two ways to build a page:

**Option A — the Music Player widget.** Add the "Music player" widget to a page and pick the MBXHub
instance. This gives you album art, a progress bar, and transport controls all in one widget, plus
built-in playlist browsing.

**Option B — individual buttons.** Assign any of these actions directly to buttons:

- Play, Pause, Play/Pause, Next track, Previous track, Cycle repeat
- Toggle shuffle, Set volume, Toggle love, Play playlist

Mix and match — a widget for the main "now playing" view plus a few dedicated buttons (like shuffle or
a specific playlist) works well.

## 4. Optional: live status on your buttons

Beyond the widget, the plugin exposes variables you can use to build multi-state buttons — for example,
a shuffle button that visually changes when shuffle is on:

- `mbxhub-track`, `mbxhub-artist`, `mbxhub-album` — current track info
- `mbxhub-is-playing`, `mbxhub-love`, `mbxhub-shuffle` — on/off state
- `mbxhub-repeat` — current repeat mode
- `mbxhub-duration-seconds`, `mbxhub-position-seconds`, `mbxhub-position-text` — playback progress

In Macro Deck 3, create a multi-state button, add a state for each value you care about, and map each
state to a rule on the matching variable (e.g. `mbxhub-shuffle == true`).

## Troubleshooting

**The plugin says it can't connect.** Double check MusicBee is running with MBXHub loaded, and that the
host/port you entered match what MBXHub is actually using (see step 2).

**Love toggle doesn't do anything.** This is a MusicBee/MBXHub setting, not the plugin — MBXHub ships
with library-tag writes (love, star ratings) turned off by default. Open MBXHub's settings page
(`http://<host>:<port>/pages/settings.html`) and turn off "read-only library tags."

**Everything else works but the player doesn't show up after restarting Macro Deck.** Try reconfiguring
the connection (step 2) — if it still doesn't appear, restart Macro Deck once more; the plugin needs a
moment to reconnect on cold start.

## Uninstalling

Remove the plugin from Macro Deck's plugin manager like any other plugin. No files are left behind
outside Macro Deck's own plugin-data directory.
