namespace Basil.Server.Shared.Configuration;

/// <summary>
///     Settings controlling whether the server looks for a newer release and where it looks.
/// </summary>
public sealed class UpdateCheckOptions
{
	/// <summary>The configuration section these settings are bound from.</summary>
	public const string SectionName = "Basil:Update";

	/// <summary>
	///     Whether the server checks for a newer release when it starts. A check never installs
	///     anything: it only reports what it found.
	/// </summary>
	public bool CheckOnStartup { get; init; } = true;

	/// <summary>The release feed to check. A GitHub repository URL or a plain feed URL.</summary>
	public string Source { get; init; } = "https://github.com/thnhmai06/osuBasil";
}
