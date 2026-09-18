using MacroDeck.Localization;
using InariIX.MbxHubPlugin.Mbx;
using MacroDeck.Sdk.Actions;

namespace InariIX.MbxHubPlugin.Actions;

/// <summary>
/// Shared shape for a no-parameter action that just calls one MbxHubClient method. The Music Player
/// widget already exposes transport through ICatalogMusicPlayer (see MbxHubMusicPlayer), but that
/// requires the widget to be on a page - these give the same functions as plain buttons, the same
/// relationship ToggleShuffleAction and ToggleLoveAction already have to their widget/dashboard
/// counterparts.
/// </summary>
internal abstract class SimpleMbxHubAction(MbxHubIntegration integration) : IActionDefinition
{
	public abstract string Id { get; }

	public abstract LocalizedText Name { get; }

	public abstract LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	protected abstract Task InvokeAsync(MbxHubClient client, CancellationToken cancellationToken);

	public IActionExecutor CreateExecutor() => new Executor(this, integration);

	private sealed class Executor(SimpleMbxHubAction definition, MbxHubIntegration integration) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (integration.Client is not { } client)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, "MBXHub isn't connected yet. Finish the plugin's configuration first.");
			}

			await definition.InvokeAsync(client, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}

internal sealed class PlayAction(MbxHubIntegration integration) : SimpleMbxHubAction(integration)
{
	public override string Id => "play";
	public override LocalizedText Name => "Play";
	public override LocalizedText Description => "Resumes playback in MusicBee.";
	protected override Task InvokeAsync(MbxHubClient client, CancellationToken cancellationToken) => client.PlayAsync(cancellationToken);
}

internal sealed class PauseAction(MbxHubIntegration integration) : SimpleMbxHubAction(integration)
{
	public override string Id => "pause";
	public override LocalizedText Name => "Pause";
	public override LocalizedText Description => "Pauses playback in MusicBee.";
	protected override Task InvokeAsync(MbxHubClient client, CancellationToken cancellationToken) => client.PauseAsync(cancellationToken);
}

internal sealed class TogglePlayPauseAction(MbxHubIntegration integration) : SimpleMbxHubAction(integration)
{
	public override string Id => "toggle-play-pause";
	public override LocalizedText Name => "Play / Pause";
	public override LocalizedText Description => "Toggles between play and pause in MusicBee.";
	protected override Task InvokeAsync(MbxHubClient client, CancellationToken cancellationToken) => client.TogglePlayPauseAsync(cancellationToken);
}

internal sealed class NextTrackAction(MbxHubIntegration integration) : SimpleMbxHubAction(integration)
{
	public override string Id => "next-track";
	public override LocalizedText Name => "Next track";
	public override LocalizedText Description => "Skips to the next track in MusicBee.";
	protected override Task InvokeAsync(MbxHubClient client, CancellationToken cancellationToken) => client.NextAsync(cancellationToken);
}

internal sealed class PreviousTrackAction(MbxHubIntegration integration) : SimpleMbxHubAction(integration)
{
	public override string Id => "previous-track";
	public override LocalizedText Name => "Previous track";
	public override LocalizedText Description => "Goes back to the previous track in MusicBee.";
	protected override Task InvokeAsync(MbxHubClient client, CancellationToken cancellationToken) => client.PreviousAsync(cancellationToken);
}

internal sealed class CycleRepeatAction(MbxHubIntegration integration) : SimpleMbxHubAction(integration)
{
	public override string Id => "cycle-repeat";
	public override LocalizedText Name => "Cycle repeat";
	public override LocalizedText Description => "Advances MusicBee's repeat mode to the next setting.";
	protected override Task InvokeAsync(MbxHubClient client, CancellationToken cancellationToken) => client.CycleRepeatAsync(cancellationToken);
}
