using InariIX.MbxHubPlugin;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;

// Identity, description and icon come from manifest.json at the content root, not from code.
// No localization catalog is registered; user-facing text is supplied as plain strings for this
// single-language plugin.
var plugin = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.RegisterIntegration<MbxHubIntegration>()
	.Build();

await plugin.RunAsync();
