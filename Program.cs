using InariIX.MbxHubPlugin;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;

// Identity, description and icon come from manifest.json at the content root, not from code.
// No .UseLocalization() call: LocalizedText implicitly converts from a plain string (confirmed via
// SDK reflection), so every Name/Description/Label below is just a string literal - there's no
// generated Strings class or resx behind this plugin. Fine for a personal-use, single-language
// plugin; a real localization pass would reintroduce that machinery deliberately, not by default.
var plugin = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.RegisterIntegration<MbxHubIntegration>()
	.Build();

await plugin.RunAsync();
