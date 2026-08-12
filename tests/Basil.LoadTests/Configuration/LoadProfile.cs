namespace Basil.LoadTests.Configuration;

/// <summary>The root configuration for one load-test run, bound from a <c>profiles/*.json</c> file.</summary>
public sealed class LoadProfile
{
	/// <summary>The profile's name, used in report file names.</summary>
	public required string Name { get; init; }

	/// <summary>How the target server instance is started, reached, and torn down.</summary>
	public required ServerHostSettings ServerHost { get; init; }

	/// <summary>How virtual users connect to the server.</summary>
	public ClientSettings Client { get; init; } = new();

	/// <summary>The pool of seeded accounts virtual users draw from.</summary>
	public AccountPoolSettings Accounts { get; init; } = new();

	/// <summary>Resource-sampling settings, shared by every scenario in the run.</summary>
	public MetricsSettings Metrics { get; init; } = new();

	/// <summary>Where and in what formats reports are written.</summary>
	public ReportSettings Report { get; init; } = new();
}