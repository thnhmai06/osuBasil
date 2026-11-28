using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;

namespace Basil.Tests.LoadTests;

/// <summary>
///     Shared contract implemented by every load test module so each module declares only its load shape, thresholds
///     and report name.
/// </summary>
public interface ILoadTest
{
	/// <summary>
	///     Gets the CLI name of this test (e.g. <c>load</c>, <c>stress</c>, <c>spike</c>, <c>soak</c>, <c>capacity</c>,
	///     <c>scalability</c>, <c>smoke</c>).
	/// </summary>
	string Name { get; }

	/// <summary>Gets the report file prefix for this run.</summary>
	string ReportName(LoadTestOptions options);

	/// <summary>
	///     Lays out the load levels of the run on its timeline, so reports can be split per step and the runner can tell
	///     how long the run lasts.
	/// </summary>
	RunPlan Plan(LoadTestOptions options);

	/// <summary>Builds the NBomber scenarios, load simulations and SLO thresholds for this test.</summary>
	IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor);
}