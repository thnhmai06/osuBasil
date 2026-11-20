using Mediator;

namespace Basil.Application.Shared.Diagnostics;

public sealed record CollectDiagnostics(IDiagnosticsCollector Collector) : INotification;

public interface IDiagnosticsCollector
{
	void Add(DiagnosticsValue value);
}

public readonly record struct DiagnosticsValue(string Category, string Name, long Value);