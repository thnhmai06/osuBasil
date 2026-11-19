namespace Basil.Application.Diagnostics.Services;

public interface IDiagnosticsFeed
{
	IAsyncEnumerable<DiagnosticsSample> Watch(string? category = null, CancellationToken ct = default);
}