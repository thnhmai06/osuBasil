namespace Basil.Application.Diagnostics.Services;

/// <summary>
///     Receives the measurements of the running server and turns them into a sample: the instruments the process
///     publishes, the counts the modules report about themselves and the time the messages take.
/// </summary>
public interface IDiagnosticsIntake
{
	/// <summary>Takes the sample of this moment; the process measurements are shared within the same second.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The value of every category.</returns>
	ValueTask<DiagnosticsSample> TakeSampleAsync(CancellationToken ct = default);
}