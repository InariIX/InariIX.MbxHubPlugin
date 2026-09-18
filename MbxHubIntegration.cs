using InariIx.MbxHubPlugin.Actions;
using InariIx.MbxHubPlugin.Config;
using InariIx.MbxHubPlugin.Mbx;
using InariIx.MbxHubPlugin.Player;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace InariIx.MbxHubPlugin;

/// <summary>
/// One MBXHub connection per config entry. MBXHub talks to exactly one MusicBee process, so
/// AllowsMultipleConfigurations is false and there is exactly one MusicPlayerInstance - modeled after
/// MacroDeck.SampleWeatherPlugin's single-station provider rather than the music sample's two-instance
/// one, which exists specifically to demonstrate multi-instance wiring this plugin doesn't need.
/// </summary>
public sealed class MbxHubIntegration : IPluginIntegration, IMusicPlayerProvider, IConfigFlowProvider, IVariableProvider
{
	internal const string InstanceId = "mbxhub";

	private MbxHubClient? _client;
	private readonly MbxHubMusicPlayer _player;

	public MbxHubIntegration()
	{
		// Constructed immediately, not once config resolves - see the class doc on MbxHubMusicPlayer
		// for why: GetInstances() below needs a real instance to hand out from the very first host
		// query, before InitializeAsync's async config read has had a chance to run.
		_player = new MbxHubMusicPlayer(this);

		Actions =
		[
			new PlayAction(this),
			new PauseAction(this),
			new TogglePlayPauseAction(this),
			new NextTrackAction(this),
			new PreviousTrackAction(this),
			new CycleRepeatAction(this),
			new ToggleLoveAction(this),
			new ToggleShuffleAction(this),
			new SetVolumeAction(this),
			new PlayPlaylistAction(this)
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public async Task InitializeAsync(IIntegrationContext context)
	{
		var entries = await context.Config.GetEntriesAsync();

		// AGENTS.md: InitializeAsync isn't a once-per-process call - it re-runs after a non-resume
		// reconnect and whenever the host reports a configuration change, so it must be safe to run
		// repeatedly against an already-initialized process. Disposing any existing client first is
		// what makes a second call idempotent instead of leaking the previous HttpClient.
		_client?.Dispose();
		_client = null;

		if (entries.Count == 0)
		{
			// No configured connection yet (plugin just installed, flow not completed). Actions and
			// the music player provider degrade to "not connected" until the user runs the config
			// flow - GetPlayer/InternalClient return null until then rather than throwing.
			return;
		}

		var entryId = entries[0].Id;
		var host = await context.Config.GetStringAsync(entryId, MbxHubConfigFlow.HostFieldName) ?? "localhost";
		var portText = await context.Config.GetStringAsync(entryId, MbxHubConfigFlow.PortFieldName);
		var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 8082;

		_client = new MbxHubClient(host, port);
	}

	public Task ShutdownAsync()
	{
		_client?.Dispose();
		_client = null;
		return Task.CompletedTask;
	}

	// ---- IConfigFlowProvider ---------------------------------------------------------------------

	public IConfigFlow CreateConfigFlow() => new MbxHubConfigFlow();

	public bool AllowsMultipleConfigurations => false;

	// ---- IMusicPlayerProvider --------------------------------------------------------------------

	public IReadOnlyList<MusicPlayerInstance> GetInstances() => [new MusicPlayerInstance(InstanceId, "MBXHub")];

	public IMusicPlayer? GetPlayer(string instanceId)
		=> string.Equals(instanceId, InstanceId, StringComparison.Ordinal) ? _player : null;

	/// <summary>Internal accessor the plain button actions use - they call the same client the widget
	/// does, so a button press and the widget can never disagree about what "now" means.</summary>
	internal MbxHubClient? Client => _client;

	// ---- IVariableProvider ------------------------------------------------------------------------

	// Confirmed by reflecting the actual restored MacroDeck.Sdk.dll (3.0.0-preview.10): VariableDefinition
	// isn't the simple record ProvidedVariable was - it's built via VariableDefinition.OnDemand(id, type)
	// or .Eager(id, type, decimalPlaces, refreshInterval). VariableType has exactly three members (also
	// confirmed via reflection): Text, Numeric, Boolean - no image/binary type, so album art stays
	// widget-only (GetArtworkAsync on MbxHubMusicPlayer); there's no variable equivalent for it.
	//
	// Everything below is Eager rather than OnDemand: only the two Eager variables (position-seconds/
	// position-text) ever showed as available in the host's variable list, while every OnDemand one
	// showed "unavailable" - OnDemand most likely only populates once something actually binds to it,
	// so nothing had ever triggered a ReadAsync call for them. Eager gets them refreshed on a timer
	// regardless of whether anything is bound yet, matching the two that were already working.
	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("mbxhub-track", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-artist", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-album", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-is-playing", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-love", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-shuffle", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-repeat", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-duration-seconds", VariableType.Numeric, 0, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-position-seconds", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)),
		VariableDefinition.Eager("mbxhub-position-text", VariableType.Text, null, TimeSpan.FromSeconds(1))
	];

	/// <summary>An unresolved client (not configured yet, or the config flow hasn't run) answers
	/// VariableReading.Of(null) rather than throwing - the host renders a null Value as "unavailable".</summary>
	public async ValueTask<VariableReading> ReadAsync(string name, CancellationToken cancellationToken)
	{
		if (_client is null)
		{
			return VariableReading.Of(null);
		}

		// Shuffle and repeat live on different MBXHub endpoints than everything else here, so they're
		// handled before the /nowplaying fetch below rather than needing a second round trip after it.
		if (name == "mbxhub-shuffle")
		{
			var shuffleEnabled = await _client.GetShuffleAsync(cancellationToken);
			return VariableReading.Of(shuffleEnabled);
		}

		if (name == "mbxhub-repeat")
		{
			var repeatRaw = await _client.GetRepeatRawAsync(cancellationToken);
			return VariableReading.Of(repeatRaw);
		}

		var nowPlaying = await _client.GetNowPlayingAsync(cancellationToken);
		if (nowPlaying is null)
		{
			return VariableReading.Of(null);
		}

		var positionSeconds = nowPlaying.PositionMs / 1000.0;
		var durationSeconds = nowPlaying.DurationMs / 1000.0;

		// The progress variable carries Min/Max on the reading itself (VariableReading.Of(value, min,
		// max, step), confirmed via reflection) rather than on the static VariableDefinition - Max has
		// to track each track's own duration, which VariableDefinition can't express since it's built
		// once at startup.
		if (name == "mbxhub-position-seconds")
		{
			return VariableReading.Of(positionSeconds, 0, durationSeconds, 1);
		}

		object? value = name switch
		{
			"mbxhub-track" => nowPlaying.Title,
			"mbxhub-artist" => nowPlaying.Artist,
			"mbxhub-album" => nowPlaying.Album,
			"mbxhub-is-playing" => nowPlaying.Playing,
			"mbxhub-love" => nowPlaying.Love,
			"mbxhub-duration-seconds" => durationSeconds,
			"mbxhub-position-text" => $"{FormatTime(positionSeconds)} / {FormatTime(durationSeconds)}",
			_ => null
		};

		return VariableReading.Of(value);
	}

	private static string FormatTime(double totalSeconds)
	{
		var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
		return span.Hours > 0
			? $"{span.Hours}:{span.Minutes:D2}:{span.Seconds:D2}"
			: $"{span.Minutes}:{span.Seconds:D2}";
	}
}
