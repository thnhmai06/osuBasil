using Mediator;

namespace Basil.Application.Common.Diagnostics;

/// <summary>
///     Asks every module to report its own live counts into a collector; each module answers it with one handler.
/// </summary>
/// <param name="Collector">The collector the modules add their counts to.</param>
/// <remarks>
///     It is a question, not an event: the diagnostics module hands it directly to the handlers and waits for the
///     answers, so nothing publishes it.
/// </remarks>
public sealed record CollectDiagnostics(IDiagnosticsCollector Collector) : INotification;