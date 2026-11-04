using Basil.Application.Common.Diagnostics;
using Basil.Application.Diagnostics.Services;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Services;

/// <summary>Merges the process measurements with the counts every module reports about itself.</summary>
internal sealed class DiagnosticsIntake(
	RuntimeSampler runtime,
	IEnumerable<INotificationHandler<CollectDiagnostics>> modules) : IDiagnosticsIntake
{
	/// <inheritdoc />
	public async ValueTask<DiagnosticsSample> TakeSampleAsync(CancellationToken cancellationToken = default)
	{
		var reported = new Reported();
		var question = new CollectDiagnostics(reported);
		foreach (var module in modules) await module.Handle(question, cancellationToken);

		var values = new Dictionary<string, object>(runtime.Sample())
		{
			["database"] = new { available = false },
			["events"] = new { available = false }
		};
		foreach (var category in reported.Counts.GroupBy(count => count.Key.Category))
			values[category.Key] = category.ToDictionary(count => count.Key.Name, count => count.Value);

		return new DiagnosticsSample(DiagnosticsSample.Categories.Where(values.ContainsKey)
			.Select(category => new KeyValuePair<string, object>(category, values[category])));
	}

	private sealed class Reported : IDiagnosticsCollector
	{
		public Dictionary<(string Category, string Name), long> Counts { get; } = [];

		public void Add(DiagnosticsValue value)
		{
			Counts[(value.Category, value.Name)] = value.Value;
		}
	}
}
