namespace Basil.LoadTests.Configuration;

/// <summary>Where and in what formats a run's reports are written.</summary>
public sealed class ReportSettings
{
	/// <summary>The folder each run writes a timestamped subfolder into.</summary>
	public string Folder { get; init; } = ".loadtest/reports";

	/// <summary>NBomber report formats to produce, in addition to this project's own <c>run.json</c>/<c>summary.md</c>.</summary>
	public string[] Formats { get; init; } = ["Html", "Csv", "Md", "Txt"];
}