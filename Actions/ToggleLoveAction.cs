using MacroDeck.Localization;
using InariIX.MbxHubPlugin.Mbx;
using MacroDeck.Sdk.Actions;

namespace InariIX.MbxHubPlugin.Actions;

internal sealed class ToggleLoveAction(MbxHubIntegration integration)
	: IActionDefinition, IStateProviderActionDefinition
{
	private const string LovedStateId = "loved";
	private const string NotLovedStateId = "not-loved";

	private static readonly IReadOnlyList<ActionStateDefinition> States =
	[
		new(LovedStateId, "Loved"),
		new(NotLovedStateId, "Not loved")
	];

	public string Id => "toggle-love";

	public LocalizedText Name => "Toggle love";

	public LocalizedText Description => "Toggles the love tag on the track currently playing in MusicBee.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public TimeSpan StatePollInterval => TimeSpan.FromSeconds(2);

	public IActionExecutor CreateExecutor() => new Executor(integration);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (integration.Client is not { } client)
		{
			return null;
		}

		var nowPlaying = await client.GetNowPlayingAsync(cancellationToken);
		return nowPlaying is null
			? null
			: new ActionStateSnapshot(States, nowPlaying.Love ? LovedStateId : NotLovedStateId);
	}

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
