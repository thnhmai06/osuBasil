namespace Basil.Application.Shared.Diagnostics;

public interface IDiagnosticsCollector
{
	void Add(DiagnosticsValue value);
}