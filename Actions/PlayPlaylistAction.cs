using MacroDeck.Localization;
using InariIx.MbxHubPlugin.Mbx;
using MacroDeck.Sdk.Actions;

namespace InariIx.MbxHubPlugin.Actions;

/// <summary>
/// Dynamic-choice playlist picker, matching MacroDeck.SampleMusicPlayerPlugin's PlayCatalogItemAction
/// pattern but simplified to just playlists: MBXHub has one player and no per-item shuffle-on-play
/// parameter, so there's no "kind" selector needed here.
///
/// The same result is reachable through the built-in Music Player widget's own catalogue browser
/// (ICatalogMusicPlayer.GetCatalogAsync in MbxHubMusicPlayer) - this action exists for people who want
/// a single dedicated "play this exact playlist" button instead.
/// </summary>
internal sealed class PlayPlaylistAction(MbxHubIntegration integration) : IActionDefinition, IDynamicOptionsActionDefinition
{
	public string Id => "play-playlist";

	public LocalizedText Name => "Play playlist";

	public LocalizedText Description => "Plays a MusicBee playlist chosen from MBXHub.";

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice("playlist", label: "Playlist", required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken)
	{
		if (integration.Client is not { } client)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		var playlists = await client.GetPlaylistsAsync(cancellationToken);
		IEnumerable<MbxPlaylist> matching = playlists;
		if (!string.IsNullOrWhiteSpace(context.Filter))
		{
			matching = matching.Where(p => p.DisplayName.Contains(context.Filter, StringComparison.OrdinalIgnoreCase));
		}

		// A playlist's name is content the user typed in MusicBee, not UI text, so it stays a literal.
		return new DynamicOptionsResult
		{
			Options = [.. matching.Select(p => new ActionParameterOption { Value = p.Url, Label = p.DisplayName })],
			CacheSeconds = 30
		};
	}

	private sealed class Executor(MbxHubIntegration integration) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (integration.Client is not { } client)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, "MBXHub isn't connected yet. Finish the plugin's configuration first.");
			}

			if (context.Parameters.GetValueOrDefault("playlist") is not string { Length: > 0 } playlistUrl)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required("Playlist"));
			}

			await client.PlayPlaylistAsync(playlistUrl, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
