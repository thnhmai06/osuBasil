namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One line of a settings page: a value, the group it is shown under and how it is written back.</summary>
/// <param name="Category">The group at the top of the fold tree: a settings category, a configuration section or the environment.</param>
/// <param name="Group">The group of the row inside its category, or <see langword="null" /> when it sits on the category itself.</param>
/// <param name="Path">The full name of the setting.</param>
/// <param name="Label">The name the operator reads.</param>
/// <param name="Value">The text of the value.</param>
/// <param name="Kind">What the value is.</param>
/// <param name="Raw">Whether the names of the groups are shown as they are, not turned into words.</param>
internal sealed record SettingsRow(string Category, string? Group, string Path, string Label, string Value,
	SettingsFieldKind Kind, bool Raw = false);
