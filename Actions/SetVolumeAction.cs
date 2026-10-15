using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace InariIX.MbxHubPlugin.Actions;

internal sealed class SetVolumeAction(MbxHubIntegration integration) : IActionDefinition
{
	private const double MinVolume = 0;
	private const double MaxVolume = 100;

	public string Id => "set-volume";

	public LocalizedText Name => "Set volume";

	public LocalizedText Description => "Sets MusicBee's playback volume.";

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Slider("volume", MinVolume, MaxVolume,
			label: "Volume (%)",
			description: "Sets MusicBee's playback volume.",
			step: 1,
			defaultValue: 60)
	];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	private sealed class Executor(MbxHubIntegration integration) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (integration.Client is not { } client)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, "MBXHub isn't connected yet. Finish the plugin's configuration first.");
			}

			if (context.Parameters.GetValueOrDefault("volume") is not double volume
				|| !double.IsFinite(volume)
				|| volume < MinVolume
				|| volume > MaxVolume
				|| volume != Math.Truncate(volume))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.InvalidValue("Volume (%)"));
			}

			await client.SetVolumeAsync((int)volume, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
