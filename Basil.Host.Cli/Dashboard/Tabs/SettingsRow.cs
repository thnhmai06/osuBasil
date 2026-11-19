namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed record SettingsRow(
	string Category,
	string? Group,
	string Path,
	string Label,
	string Value,
	SettingsFieldKind Kind,
	bool Raw = false);