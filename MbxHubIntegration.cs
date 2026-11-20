using System.Diagnostics;
using System.Globalization;
using InariIX.MbxHubPlugin.Actions;
using InariIX.MbxHubPlugin.Config;
using InariIX.MbxHubPlugin.Mbx;
using InariIX.MbxHubPlugin.Player;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace InariIX.MbxHubPlugin;

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

	// SDK beta.17 supports text, numeric, and boolean variables. Album artwork stays on the music
	// player capability because it is not a variable value type.
	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("mbxhub-track", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-artist", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-album", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-is-playing", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-love", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-shuffle", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-repeat", VariableType.Text, null, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub_volume", VariableType.Numeric, 0, TimeSpan.FromSeconds(2)) with
		{
			Id = "volume",
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			Write = new VariableWriteCapability()
		},
		VariableDefinition.Eager("mbxhub-duration-seconds", VariableType.Numeric, 0, TimeSpan.FromSeconds(2)),
		VariableDefinition.Eager("mbxhub-position-seconds", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)),
		VariableDefinition.Eager("mbxhub-position-text", VariableType.Text, null, TimeSpan.FromSeconds(1)),
		VariableDefinition.Eager("mbxhub-version", VariableType.Text, null, TimeSpan.FromMinutes(5)),
		VariableDefinition.Eager("mbxhub-musicbee-version", VariableType.Text, null, TimeSpan.FromMinutes(5))
	];

	/// <summary>An unresolved client (not configured yet, or the config flow hasn't run) answers
	/// VariableReading.Of(null) rather than throwing - the host renders a null Value as "unavailable".</summary>
	public async ValueTask<VariableReading> ReadAsync(string name, CancellationToken cancellationToken)
	{
		if (_client is null)
		{
			return VariableReading.Of(null);
		}

		if (name is "mbxhub-version" or "mbxhub-musicbee-version")
		{
			var version = await _client.GetVersionAsync(cancellationToken);
			var versionValue = name == "mbxhub-version"
				? version?.DisplayVersion ?? version?.Version
				: GetLocalMusicBeeVersion(version?.Host);
			return VariableReading.Of(versionValue);
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

		if (name == "volume")
		{
			var volume = await _client.GetVolumeAsync(cancellationToken);
			return VariableReading.Of(Math.Clamp(volume, 0, 100), 0, 100, 1);
		}

		var nowPlaying = await _client.GetNowPlayingAsync(cancellationToken);
		if (nowPlaying is null)
		{
			return VariableReading.Of(null);
		}

		var positionSeconds = nowPlaying.PositionMs / 1000.0;
		var durationSeconds = nowPlaying.DurationMs / 1000.0;

		// The current track's duration determines the progress variable's maximum.
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

	public async ValueTask<VariableWriteResult> SetValueAsync(
		string name,
		object? value,
		CancellationToken cancellationToken = default)
	{
		if (!string.Equals(name, "volume", StringComparison.Ordinal))
		{
			return VariableWriteResult.NotFound();
		}

		if (_client is not { } client)
		{
			return VariableWriteResult.Unavailable();
		}

		if (value is not (double or int or long))
		{
			return VariableWriteResult.InvalidValue();
		}

		var numericValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
		if (!double.IsFinite(numericValue) || numericValue < 0 || numericValue > 100)
		{
			return VariableWriteResult.InvalidValue();
		}

		var volume = (int)Math.Round(numericValue, MidpointRounding.AwayFromZero);
		await client.SetVolumeAsync(volume, cancellationToken);
		return VariableWriteResult.Applied();
	}

	private static string? GetLocalMusicBeeVersion(string? mbxHubHost)
	{
		if (!string.Equals(mbxHubHost, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		string? version = null;
		foreach (var process in Process.GetProcessesByName("MusicBee"))
		{
			using (process)
			{
				if (version is null && process.MainModule?.FileName is { } executablePath)
				{
					var fileVersion = FileVersionInfo.GetVersionInfo(executablePath);
					version = fileVersion.ProductVersion ?? fileVersion.FileVersion;
				}
			}
		}

		return version;
	}

	private static string FormatTime(double totalSeconds)
	{
		var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
		return span.Hours > 0
			? $"{span.Hours}:{span.Minutes:D2}:{span.Seconds:D2}"
			: $"{span.Minutes}:{span.Seconds:D2}";
	}
}
