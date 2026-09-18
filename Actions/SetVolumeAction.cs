using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.MbxHubPlugin.Actions;

/// <summary>
/// TEMPORARY: a plain action rather than a slider. MacroDeck.SampleMusicPlayerPlugin's SetVolumeAction
/// implements ISliderActionDefinition for two-way binding (a Slider widget shows and sets MBXHub's
/// live volume), but those types didn't resolve against the actually-restored MacroDeck.Sdk package -
/// the sample repo's checked-out source had drifted from what's published. Rather than guess at
/// renamed types, this sets volume via a plain text parameter so the plugin builds; see README.md for
/// how to find the real slider contract and restore two-way binding.
/// </summary>
internal sealed class SetVolumeAction(MbxHubIntegration integration) : IActionDefinition
{
	public string Id => "set-volume";

	public LocalizedText Name => "Set volume";

	public LocalizedText Description => "Sets MusicBee's playback volume.";

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text("volume", label: "Volume (%)", defaultValue: "60", required: true)
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

			if (context.Parameters.GetValueOrDefault("volume") is not string { Length: > 0 } volumeText
				|| !int.TryParse(volumeText, out var volume))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.InvalidValue("Volume (%)"));
			}

			await client.SetVolumeAsync(volume, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
