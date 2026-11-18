namespace Basil.Application.Diagnostics.Services;

public interface IDiagnosticsIntake
{
	ValueTask<DiagnosticsSample> TakeSampleAsync(CancellationToken ct = default);
}