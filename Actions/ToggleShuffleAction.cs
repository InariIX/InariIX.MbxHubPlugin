using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace InariIX.MbxHubPlugin.Actions;

/// <summary>
/// Plain-button shuffle toggle. Shuffle is also part of MusicPlayerState/IMusicPlayer
/// (see MbxHubMusicPlayer), so the built-in media widget already exposes it - this exists for anyone
/// who wants a standalone button instead, the same relationship TogglePlaybackAction has to
/// IMusicPlayer.TogglePlayPauseAsync in the sample plugin.
/// </summary>
internal sealed class ToggleShuffleAction(MbxHubIntegration integration) : IActionDefinition
{
	public string Id => "toggle-shuffle";

	public LocalizedText Name => "Toggle shuffle";

	public LocalizedText Description => "Turns shuffle on or off in MusicBee.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	private sealed class Executor(MbxHubIntegration integration) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (integration.Client is not { } client)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, "MBXHub isn't connected yet. Finish the plugin's configuration first.");
			}

			var current = await client.GetShuffleAsync(context.CancellationToken);
			await client.SetShuffleAsync(!current, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
