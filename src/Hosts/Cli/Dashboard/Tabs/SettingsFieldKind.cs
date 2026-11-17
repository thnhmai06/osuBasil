namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>What a setting holds, which decides how its text is written back.</summary>
internal enum SettingsFieldKind : byte
{
	/// <summary>Text.</summary>
	Text,

	/// <summary>A number.</summary>
	Number,

	/// <summary>On or off.</summary>
	Bool,

	/// <summary>Comma-separated text that is a list.</summary>
	List,

	/// <summary>Text, or nothing.</summary>
	Optional
}
