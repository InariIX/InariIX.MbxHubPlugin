using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace InariIx.MbxHubPlugin.Config;

/// <summary>
/// Single-step flow collecting the host:port of the machine running MusicBee + MBXHub.
/// Modeled directly on MacroDeck.SampleWeatherPlugin's LocationConfigFlow, the SDK's documented
/// minimal example of a one-step config flow.
/// </summary>
internal sealed class MbxHubConfigFlow : IConfigFlow
{
	internal const string HostFieldName = "host";
	internal const string PortFieldName = "port";

	private const string StepId = "connection";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(BuildStep()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(stepId, StepId, StringComparison.Ordinal))
		{
			return Task.FromResult(ConfigFlowResult.Error(BuildStep(), "Unknown configuration step."));
		}

		if (input.GetValueOrDefault(HostFieldName) is not string { Length: > 0 } host)
		{
			LocalizedText required = MacroDeckStrings.Validation.Required("Host");
			return Task.FromResult(ConfigFlowResult.Error(BuildStep(),
				required,
				new Dictionary<string, LocalizedText> { [HostFieldName] = required }));
		}

		// Kept as a Text field rather than ActionParameter.Number: the samples only show Number used
		// for an unconstrained event-payload value, never with min/max/default on a config step, so a
		// plain parsed string avoids guessing at an unconfirmed overload.
		// Default 8082, not MBXHub's own 8080 default: 8080 is commonly already in use (confirmed on
		// this very machine), so an actual-environment-matched default saves a step more often than a
		// generic one would.
		var port = input.GetValueOrDefault(PortFieldName) is string { Length: > 0 } portText
			&& int.TryParse(portText, out var parsedPort)
				? parsedPort
				: 8082;

		// Plain string, not LocalizedText: the host stores this as the config entry's title and the
		// user can rename it afterward, so it only needs to be written once, in the plugin's own
		// language - same convention the weather sample follows.
		return Task.FromResult(ConfigFlowResult.Complete($"MBXHub ({host}:{port})"));
	}

	private static ConfigFlowStep BuildStep() => new()
	{
		StepId = StepId,
		Title = "Connect to MBXHub",
		Description = "Enter the address of the machine running MusicBee with the MBXHub plugin installed.",
		Fields =
		[
			ActionParameter.Text(HostFieldName,
				label: "Host",
				description: "Hostname or IP address of the machine running MBXHub, e.g. localhost or 192.168.1.20.",
				defaultValue: "localhost",
				required: true),
			ActionParameter.Text(PortFieldName,
				label: "Port",
				description: "MBXHub's port. Pre-filled as 8082 since 8080 is commonly already taken; check yours if unsure.",
				defaultValue: "8082",
				required: true)
		]
	};
}
