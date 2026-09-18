using System.Text.Json.Serialization;

namespace MacroDeck.MbxHubPlugin.Mbx;

/// <summary>
/// Most MBXHub endpoints wrap their payload as <c>{ "success": true, "data": {...} }</c> - confirmed
/// by the documented /nowplaying, /queue, /library/* and /playlists/{url}/files examples. The
/// /dashboard/* routes (used only for the love toggle here) answer a different shape - see
/// <see cref="MbxHubClient.ToggleLoveAsync"/>.
/// </summary>
internal sealed class MbxEnvelope<T>
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("data")]
	public T? Data { get; set; }
}

/// <summary>Shape of GET /nowplaying's "data" object, per the documented example response.</summary>
internal sealed class MbxNowPlaying
{
	[JsonPropertyName("playing")]
	public bool Playing { get; set; }

	[JsonPropertyName("url")]
	public string? Url { get; set; }

	[JsonPropertyName("title")]
	public string? Title { get; set; }

	[JsonPropertyName("artist")]
	public string? Artist { get; set; }

	[JsonPropertyName("album")]
	public string? Album { get; set; }

	[JsonPropertyName("love")]
	public bool Love { get; set; }

	/// <summary>Milliseconds, per the documented example.</summary>
	[JsonPropertyName("duration")]
	public long DurationMs { get; set; }

	/// <summary>Milliseconds, per the documented example.</summary>
	[JsonPropertyName("position")]
	public long PositionMs { get; set; }
}

/// <summary>
/// Shape of GET/PUT /player/volume's "data" object. The PUT body is documented
/// (<c>{"volume":50}</c> absolute or <c>{"delta":-5}</c> relative); the GET response shape is not
/// shown in the docs, so this assumes symmetry with the PUT body's "volume" field. Verify against a
/// live GET /player/volume call if this comes back empty.
/// </summary>
internal sealed class MbxVolume
{
	[JsonPropertyName("volume")]
	public int Volume { get; set; }
}

/// <summary>
/// Shape of GET/PUT /player/shuffle's "data" object. Field name assumed by symmetry with the
/// documented pattern elsewhere in the API (the value named after the resource); verify against a
/// live GET /player/shuffle call if this comes back empty.
/// </summary>
internal sealed class MbxShuffle
{
	[JsonPropertyName("shuffle")]
	public bool Shuffle { get; set; }
}

/// <summary>One entry from GET /playlists. Confirmed field names ("url", "name") from HALRAD's own
/// MCP sample (halrad-com/mbxhub, samples/mcp/index.ts): "Each entry has a url (its identifier) and a
/// name." "title" kept as a defensive fallback only - the field itself is no longer a guess.</summary>
internal sealed class MbxPlaylist
{
	[JsonPropertyName("url")]
	public string Url { get; set; } = "";

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("title")]
	public string? Title { get; set; }

	[JsonIgnore]
	public string DisplayName => Name ?? Title ?? Url;
}

internal sealed class MbxPlaylistsData
{
	[JsonPropertyName("playlists")]
	public List<MbxPlaylist> Playlists { get; set; } = [];
}

/// <summary>The dual-audience response shape documented for /dashboard/love and /dashboard/rate when
/// called with a JSON-accepting client: <c>{ result, message }</c> on success.</summary>
internal sealed class MbxDashboardResult
{
	[JsonPropertyName("result")]
	public bool Result { get; set; }

	[JsonPropertyName("message")]
	public string? Message { get; set; }
}
