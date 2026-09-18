using InariIx.MbxHubPlugin.Mbx;
using MacroDeck.Sdk.MusicPlayer;

namespace InariIx.MbxHubPlugin.Player;

/// <summary>
/// The single MBXHub-backed player instance. Implements ICatalogMusicPlayer (transport + browsing)
/// rather than plain IMusicPlayer, matching MacroDeck.SampleMusicPlayerPlugin's LibraryMusicPlayer:
/// browsing and play-item are only offered to a player that declares the catalogue capability.
///
/// There is no device provider here (IMusicPlayerDeviceProvider) - MBXHub speaks to one MusicBee
/// instance, not a set of switchable output devices.
///
/// Takes the integration rather than a concrete MbxHubClient, and is constructed unconditionally and
/// immediately (see MbxHubIntegration's constructor) - not lazily once config resolves. Originally this
/// held a client directly and MbxHubIntegration only constructed it (and only listed the instance at
/// all in GetInstances()) after InitializeAsync's async config read finished. That raced against the
/// host's own startup query of GetInstances(): if the host asked before InitializeAsync finished (or
/// simply cached whatever GetInstances() returned once), the player permanently vanished from Macro
/// Deck's player list even though the plugin was running and fully configured - matching the reported
/// symptom exactly (disappears while enabled, and disabling just swaps that for the host's own
/// "unavailable" treatment of a stopped plugin). The instance itself is now always declared; only the
/// live state (GetStateAsync below) reflects whether MBXHub is actually reachable yet.
/// </summary>
internal sealed class MbxHubMusicPlayer(MbxHubIntegration integration) : ICatalogMusicPlayer
{
	public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		if (integration.Client is not { } client)
		{
			// MusicPlayerState.Unavailable(statusMessage) is the confirmed factory for this (see
			// md3-sdk-notes) - it surfaces a real message to the host/widget rather than the bare
			// IsConnected=false this originally used, which left the reader guessing why.
			return MusicPlayerState.Unavailable("MBXHub isn't connected yet - finish the plugin's configuration first.");
		}

		// /nowplaying carries track + position + love; volume and shuffle live on separate endpoints.
		// Three calls per poll - acceptable at the couple-second cadence a widget or button polls at,
		// and it keeps each concern reading the one endpoint documented for it rather than guessing at
		// a combined shape. Awaited individually after WhenAll rather than read via .Task.Result: AGENTS.md
		// (MDP3002) bans .Result/.Wait() anywhere in a type implementing IMusicPlayer, whole-type, even
		// where it's provably safe post-WhenAll - awaiting an already-completed task is just as cheap.
		var nowPlayingTask = client.GetNowPlayingAsync(cancellationToken);
		var volumeTask = client.GetVolumeAsync(cancellationToken);
		var shuffleTask = client.GetShuffleAsync(cancellationToken);
		await Task.WhenAll(nowPlayingTask, volumeTask, shuffleTask);

		var nowPlaying = await nowPlayingTask;
		if (nowPlaying is null)
		{
			return MusicPlayerState.Unavailable("MBXHub didn't return a now-playing state.");
		}

		return new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = nowPlaying.Playing ? PlaybackState.Playing : PlaybackState.Paused,
			TrackName = nowPlaying.Title,
			Artists = string.IsNullOrEmpty(nowPlaying.Artist) ? [] : [nowPlaying.Artist],
			AlbumName = nowPlaying.Album,
			// Artwork has no separate id in MBXHub - one endpoint always serves "whatever is playing
			// now" (/nowplaying/artwork). The track url doubles as a change-detection key for the host.
			ArtworkId = nowPlaying.Url,
			Position = TimeSpan.FromMilliseconds(nowPlaying.PositionMs),
			Duration = TimeSpan.FromMilliseconds(nowPlaying.DurationMs),
			VolumePercent = await volumeTask,
			ShuffleEnabled = await shuffleTask
			// RepeatMode intentionally still omitted here even though the string values are now
			// confirmed (see SetRepeatModeAsync) - populating this field needs the SDK's own RepeatMode
			// enum member names for "one"/"all", and only RepeatMode.Off has ever been confirmed by
			// exact name. Setting it correctly for playback control doesn't require that (a fuzzy
			// ToString() match is enough there); reporting it back accurately here would.
		};
	}

	public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
		=> GetArtworkInternalAsync(cancellationToken);

	private async Task<MusicPlayerArtwork?> GetArtworkInternalAsync(CancellationToken cancellationToken)
	{
		if (integration.Client is not { } client)
		{
			return null;
		}

		var bytes = await client.GetArtworkAsync(cancellationToken);
		// MBXHub always serves JPEG/PNG for embedded art; without a Content-Type surfaced separately
		// in the docs, image/jpeg is the safe default most embedded MusicBee art uses.
		return bytes is null ? null : new MusicPlayerArtwork(bytes, "image/jpeg");
	}

	public Task PlayAsync(CancellationToken cancellationToken = default) => RequireClient().PlayAsync(cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default) => RequireClient().PauseAsync(cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
		=> RequireClient().TogglePlayPauseAsync(cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default) => RequireClient().NextAsync(cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default) => RequireClient().PreviousAsync(cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
		=> RequireClient().SeekAsync(position, cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> RequireClient().SetVolumeAsync(volumePercent, cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
		=> RequireClient().SetShuffleAsync(enabled, cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
	{
		// GET /player/repeat confirmed live (Jaylen): MBXHub cycles through exactly "none", "one",
		// "all" - not the "off"/"track"/"all" originally guessed. RepeatMode.Off is the only SDK enum
		// member ever confirmed by exact name (via reflection on the sample plugins); the other two
		// weren't dumped the same way, so this matches Off with certainty and fuzzy-matches the rest
		// against ToString() rather than assuming exact spellings like "Track"/"All".
		var mbxValue = mode == RepeatMode.Off
			? "none"
			: mode.ToString().Contains("track", StringComparison.OrdinalIgnoreCase)
				|| mode.ToString().Contains("one", StringComparison.OrdinalIgnoreCase)
					? "one"
					: "all";
		return RequireClient().SetRepeatAsync(mbxValue, cancellationToken);
	}

	/// <summary>Catalogue scope kept to playlists for this MVP: MusicBee libraries can be huge, and
	/// MBXHub's /library/search needs a minimum query length, so an unfiltered track browse has no
	/// good MBXHub equivalent. Playlist browsing has one, and it's what the plugin brief asked for.</summary>
	public async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		if (kind != MusicPlayerCatalogItemKind.Playlist || integration.Client is not { } client)
		{
			return [];
		}

		var playlists = await client.GetPlaylistsAsync(cancellationToken);
		IEnumerable<MbxPlaylist> filtered = playlists;
		if (!string.IsNullOrWhiteSpace(filter))
		{
			filtered = filtered.Where(p => p.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
		}

		return [.. filtered.Select(p => new MusicPlayerCatalogItem(p.Url, p.DisplayName, MusicPlayerCatalogItemKind.Playlist))];
	}

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
		=> item.Kind == MusicPlayerCatalogItemKind.Playlist
			? RequireClient().PlayPlaylistAsync(item.Id, cancellationToken)
			: RequireClient().PlayTrackNowAsync(item.Id, cancellationToken);

	/// <summary>Mutating calls have no "not connected" result channel the way an action does
	/// (ActionResult.Failed) - IMusicPlayer's transport methods just return Task. Throwing here is the
	/// honest signal when MBXHub isn't configured yet; it's on the host to decide how to surface that
	/// to the user (a button pressed against an unconfigured player should fail loudly, not silently).</summary>
	private MbxHubClient RequireClient()
		=> integration.Client ?? throw new InvalidOperationException("MBXHub isn't connected yet - finish the plugin's configuration first.");
}
