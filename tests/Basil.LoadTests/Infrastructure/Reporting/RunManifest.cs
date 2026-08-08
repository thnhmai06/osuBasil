using Basil.LoadTests.Configuration;
using Basil.LoadTests.Hosting;

namespace Basil.LoadTests.Infrastructure.Reporting;

/// <summary>
///     Everything about a run that isn't already in NBomber's own report: hardware/OS/runtime,
///     the resolved profile, and what the chosen <see cref="IServerHost" /> could actually observe.
///     Written verbatim to <c>run.json</c> so a report can never be misread as having measured more
///     than it did.
/// </summary>
public sealed class RunManifest
{
	/// <summary>The profile name this run used.</summary>
	public required string ProfileName { get; init; }

	/// <summary>When the run started, in UTC.</summary>
	public required DateTimeOffset StartedUtc { get; init; }

	/// <summary>When the run finished, in UTC. Set once the run completes.</summary>
	public DateTimeOffset? FinishedUtc { get; set; }

	/// <summary>Logical processor count on the machine that ran the load generator.</summary>
	public int ProcessorCount { get; init; } = Environment.ProcessorCount;

	/// <summary>The OS description, e.g. <c>Microsoft Windows 10.0.26200</c>.</summary>
	public required string OsDescription { get; init; }

	/// <summary>The OS architecture, e.g. <c>X64</c>.</summary>
	public required string OsArchitecture { get; init; }

	/// <summary>The .NET runtime description, e.g. <c>.NET 10.0.0</c>.</summary>
	public required string FrameworkDescription { get; init; }

	/// <summary>The git commit the repository was at when this run started, or <see langword="null" /> if it could not be determined.</summary>
	public string? GitCommit { get; init; }

	/// <summary>The fully resolved profile this run used.</summary>
	public required LoadProfile Profile { get; init; }

	/// <summary>What the chosen <see cref="IServerHost" /> could observe for this run.</summary>
	public required ServerHostCapabilities Capabilities { get; init; }

	/// <summary>The server's measured startup time, when the host started the server itself.</summary>
	public TimeSpan? StartupTime { get; set; }

	/// <summary>Free-form notes surfaced in <c>summary.md</c> — degradations, generator-ceiling calls, assumptions.</summary>
	public List<string> Notes { get; } = [];
}
