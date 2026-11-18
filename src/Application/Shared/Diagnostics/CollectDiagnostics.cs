using Mediator;

namespace Basil.Application.Shared.Diagnostics;

public sealed record CollectDiagnostics(IDiagnosticsCollector Collector) : INotification;