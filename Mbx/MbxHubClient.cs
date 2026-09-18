using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace MacroDeck.MbxHubPlugin.Mbx;

/// <summary>
/// Thin wrapper over MBXHub's REST API (https://mbxhub.com/api.html). MBXHub has no authentication -
/// see the security note in the plugin README - so this is a plain unauthenticated HttpClient scoped
/// to one host:port.
///
/// Endpoints used here are confirmed either directly from the published API docs or from the
/// real-world usage pattern documented by the rlust/mbxhub-homeassistant integration (which polls
/// /player/status, /nowplaying and /nowplaying/artwork, and drives playlists through /playlists and
/// /playlists/{url}/play). Where the docs show a PUT body but not the matching GET response shape,
/// that's called out on the relevant DTO in MbxHubModels.cs.
/// </summary>
internal sealed class MbxHubClient : IDisposable
{
	private readonly HttpClient _http;

	public MbxHubClient(string host, int port)
	{
		_http = new HttpClient
		{
			BaseAddress = new Uri($"http://{host}:{port}/"),
			Timeout = TimeSpan.FromSeconds(10)
		};

		// The dual-audience /dashboard/* routes only answer JSON when the caller's Accept header
		// says so (a browser posting the no-JS <form> gets a 303 redirect instead) - see the
		// /dashboard/love documentation. Setting this globally keeps every call on the JSON path.
		_http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
	}

	public void Dispose() => _http.Dispose();

	// ---- Now playing -------------------------------------------------------------------------

	public async Task<MbxNowPlaying?> GetNowPlayingAsync(CancellationToken cancellationToken)
	{
		var envelope = await GetAsync<MbxEnvelope<MbxNowPlaying>>("nowplaying", cancellationToken);
		return envelope?.Data;
	}

	public async Task<byte[]?> GetArtworkAsync(CancellationToken cancellationToken)
	{
		using var response = await _http.GetAsync("nowplaying/artwork", cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			// A track with no artwork answers with a non-2xx here rather than an empty body in every
			// observed case; treat it as "no artwork" rather than a hard failure.
			return null;
		}

		return await response.Content.ReadAsByteArrayAsync(cancellationToken);
	}

	// ---- Transport -----------------------------------------------------------------------------

	public Task PlayAsync(CancellationToken cancellationToken) => PostAsync("player/play", cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken) => PostAsync("player/pause", cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken) => PostAsync("player/playpause", cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken) => PostAsync("player/next", cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken) => PostAsync("player/previous", cancellationToken);

	/// <summary>
	/// PUT /player/position's body isn't shown in the docs (only /player/volume's is). This assumes
	/// the same "field named after the resource, milliseconds" convention volume uses. If MBXHub
	/// rejects this, the most likely alternate field name is "positionMs".
	/// </summary>
	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken)
		=> PutAsync("player/position", new { position = (long)position.TotalMilliseconds }, cancellationToken);

	// ---- Volume ----------------------------------------------------------------------------------

	public async Task<int> GetVolumeAsync(CancellationToken cancellationToken)
	{
		var envelope = await GetAsync<MbxEnvelope<MbxVolume>>("player/volume", cancellationToken);
		return envelope?.Data?.Volume ?? 0;
	}

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken)
		=> PutAsync("player/volume", new { volume = Math.Clamp(volumePercent, 0, 100) }, cancellationToken);

	// ---- Shuffle -----------------------------------------------------------------------------------

	public async Task<bool> GetShuffleAsync(CancellationToken cancellationToken)
	{
		var envelope = await GetAsync<MbxEnvelope<MbxShuffle>>("player/shuffle", cancellationToken);
		return envelope?.Data?.Shuffle ?? false;
	}

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken)
		=> PutAsync("player/shuffle", new { shuffle = enabled }, cancellationToken);

	// ---- Repeat (read) ------------------------------------------------------------------------------

	/// <summary>GET /player/repeat's response shape isn't documented anywhere (unlike volume, whose PUT
	/// body IS shown) - and MBXHub itself describes repeat as a "cycle mode" (POST /dashboard/repeat),
	/// implying more than a simple on/off. Rather than guess a field name and a type the way shuffle's
	/// GetShuffleAsync did (which happened to work, but was still a guess), this reads back whatever
	/// MBXHub actually sends under "data" - a single scalar field if there is one, or the raw object
	/// otherwise - so a live instance's real value surfaces via the variable no matter its shape.</summary>
	public async Task<string?> GetRepeatRawAsync(CancellationToken cancellationToken)
	{
		using var response = await _http.GetAsync("player/repeat", cancellationToken);
		response.EnsureSuccessStatusCode();
		using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		if (!doc.RootElement.TryGetProperty("data", out var data))
		{
			return null;
		}

		if (data.ValueKind == JsonValueKind.Object)
		{
			var firstProperty = data.EnumerateObject().FirstOrDefault();
			if (firstProperty.Value.ValueKind is not JsonValueKind.Undefined)
			{
				return firstProperty.Value.ValueKind switch
				{
					JsonValueKind.String => firstProperty.Value.GetString(),
					JsonValueKind.True or JsonValueKind.False => firstProperty.Value.GetBoolean().ToString(),
					JsonValueKind.Number => firstProperty.Value.GetRawText(),
					_ => data.GetRawText()
				};
			}
		}

		return data.GetRawText();
	}

	// ---- Repeat ------------------------------------------------------------------------------------

	/// <summary>POST /dashboard/repeat - documented as "cycle mode": no explicit target state, just
	/// advances to the next repeat mode. More reliable than PUT /player/repeat, whose accepted mode
	/// strings the docs never enumerate (see the caveat on MbxHubMusicPlayer.SetRepeatModeAsync).</summary>
	public Task CycleRepeatAsync(CancellationToken cancellationToken)
		=> PostAsync("dashboard/repeat", cancellationToken);

	// ---- Repeat ------------------------------------------------------------------------------------

	/// <summary>PUT /player/repeat with a raw mode string. Accepted values confirmed live: "none",
	/// "one", "all" (see MbxHubMusicPlayer.SetRepeatModeAsync for how the SDK's RepeatMode enum maps
	/// onto these).</summary>
	public Task SetRepeatAsync(string mode, CancellationToken cancellationToken)
		=> PutAsync("player/repeat", new { mode }, cancellationToken);

	// ---- Love --------------------------------------------------------------------------------------

	/// <summary>
	/// Toggles the love tag on the current track. Unlike most of the API, /dashboard/love does not
	/// use the {success,data} envelope for a JSON-accepting caller - it answers {result, message} on
	/// success, or an error body with a stable "code" (e.g. NO_TRACK, LIBRARY_TAGS_READ_ONLY) on
	/// refusal. Throws MbxHubException with that code when the toggle is refused.
	/// </summary>
	public async Task ToggleLoveAsync(CancellationToken cancellationToken)
	{
		using var response = await _http.PostAsync("dashboard/love", content: null, cancellationToken);
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		string? code = null;
		try
		{
			using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
			if (doc.RootElement.TryGetProperty("code", out var codeElement))
			{
				code = codeElement.GetString();
			}
		}
		catch (JsonException)
		{
			// Body wasn't the documented error shape; fall through with a generic message below.
		}

		throw new MbxHubException(
			$"MBXHub refused the love toggle ({(int)response.StatusCode} {response.StatusCode}"
			+ (code is not null ? $", code={code}" : "") + ").");
	}

	// ---- Playlists -----------------------------------------------------------------------------------

	public async Task<IReadOnlyList<MbxPlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken)
	{
		var envelope = await GetAsync<MbxEnvelope<MbxPlaylistsData>>("playlists", cancellationToken);
		return envelope?.Data?.Playlists ?? [];
	}

	/// <summary>
	/// Playlist identity is a MusicBee playlist url/path (e.g. a .mbp file path), so it must be
	/// URL-encoded as a single path segment - the same convention the docs call out for
	/// /library/file/{url}.
	/// </summary>
	public Task PlayPlaylistAsync(string playlistUrl, CancellationToken cancellationToken)
		=> PostAsync($"playlists/{Uri.EscapeDataString(playlistUrl)}/play", cancellationToken);

	/// <summary>POST /queue/playnow - plays a single library track immediately, per the documented
	/// queue-actions example (<c>{"url": "..."}</c>).</summary>
	public Task PlayTrackNowAsync(string trackUrl, CancellationToken cancellationToken)
		=> PostAsync("queue/playnow", new { url = trackUrl }, cancellationToken);

	// ---- Plumbing ------------------------------------------------------------------------------------

	private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
	{
		using var response = await _http.GetAsync(path, cancellationToken);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
	}

	private async Task PostAsync(string path, CancellationToken cancellationToken)
	{
		using var response = await _http.PostAsync(path, content: null, cancellationToken);
		response.EnsureSuccessStatusCode();
	}

	private async Task PostAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
	{
		using var response = await _http.PostAsJsonAsync(path, body, cancellationToken);
		response.EnsureSuccessStatusCode();
	}

	private async Task PutAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
	{
		using var response = await _http.PutAsJsonAsync(path, body, cancellationToken);
		response.EnsureSuccessStatusCode();
	}
}

/// <summary>Raised when MBXHub explicitly refuses a write (read-only mode, party lock, no track playing).</summary>
internal sealed class MbxHubException(string message) : Exception(message);
