namespace Basil.Application.Shared.Diagnostics;

/// <summary>Receives the counts that the modules report when a diagnostics sample is taken.</summary>
public interface IDiagnosticsCollector
{
	/// <summary>Adds a count to the sample being taken; a count reported twice keeps the last value.</summary>
	/// <param name="value">The count.</param>
	void Add(DiagnosticsValue value);
}