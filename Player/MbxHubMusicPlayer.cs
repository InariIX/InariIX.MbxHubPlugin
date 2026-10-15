using InariIX.MbxHubPlugin.Mbx;
using MacroDeck.Sdk.MusicPlayer;

namespace InariIX.MbxHubPlugin.Player;

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

		// MBXHub exposes player status across separate endpoints, so fetch them concurrently.
		var nowPlayingTask = client.GetNowPlayingAsync(cancellationToken);
		var volumeTask = client.GetVolumeAsync(cancellationToken);
		var shuffleTask = client.GetShuffleAsync(cancellationToken);
		var repeatTask = client.GetRepeatRawAsync(cancellationToken);
		await Task.WhenAll(nowPlayingTask, volumeTask, shuffleTask, repeatTask);

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
			ShuffleEnabled = await shuffleTask,
			RepeatMode = ParseRepeatMode(await repeatTask)
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
		var mbxValue = mode switch
		{
			RepeatMode.Off => "none",
			RepeatMode.Track => "one",
			RepeatMode.Context => "all",
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown repeat mode.")
		};
		return RequireClient().SetRepeatAsync(mbxValue, cancellationToken);
	}

	private static RepeatMode ParseRepeatMode(string? value)
		=> value?.Trim().Trim('"').ToLowerInvariant() switch
		{
			"one" => RepeatMode.Track,
			"all" => RepeatMode.Context,
			_ => RepeatMode.Off
		};

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
