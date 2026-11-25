using Basil.Tests.LoadTests.Capacity;
using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;

namespace Basil.Tests.LoadTests.Scalability;

/// <summary>Scalability test: runs the Capacity profile with Basil pinned to 1, 2 or 4 cores so scaling efficiency can be compared across runs.</summary>
public sealed class ScalabilityTest : ILoadTest
{
	private readonly CapacityTest _capacity = new();

	public string Name => "scalability";

	public string ReportName(LoadTestOptions options)
	{
		return $"scalability-c{options.BasilCores}-d{options.StepDelta}-x{options.StepCount}-s{options.Seed}";
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		return _capacity.BuildScenarios(options, workload, monitor);
	}
}
