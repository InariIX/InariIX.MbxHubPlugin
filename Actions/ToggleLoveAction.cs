using MacroDeck.Localization;
using InariIX.MbxHubPlugin.Mbx;
using MacroDeck.Sdk.Actions;

namespace InariIX.MbxHubPlugin.Actions;

/// <summary>
/// Toggles the love tag on whatever MusicBee is currently playing. MusicPlayerState (see
/// MbxHubMusicPlayer) has no Loved field - confirmed against both the SDK's music player contract and
/// MacroDeck.SampleMusicPlayerPlugin, which doesn't touch one either - so this lives as an ordinary
/// action rather than something the built-in media widget can show a heart icon for.
///
/// Kept as a plain IActionDefinition rather than IStateProviderActionDefinition (a live two-state
/// button reflecting loved/not-loved): no sample plugin in the SDK repo demonstrates that interface,
/// so its exact members aren't confirmed. A plain toggle button plus the mbxhub_love variable (see
/// MbxHubIntegration) covers the same need without guessing at an unverified contract.
/// </summary>
internal sealed class ToggleLoveAction(MbxHubIntegration integration) : IActionDefinition
{
	public string Id => "toggle-love";

	public LocalizedText Name => "Toggle love";

	public LocalizedText Description => "Toggles the love tag on the track currently playing in MusicBee.";

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

			try
			{
				await client.ToggleLoveAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (MbxHubException exception)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, $"MBXHub reported: {exception.Message}");
			}
		}
	}
}
