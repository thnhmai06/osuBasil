using Basil.Application.Diagnostics.Objects;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;

namespace Basil.Application.Diagnostics.Capabilities;

public readonly struct ScopedDiagnostics
{
	private readonly ServerDiagnostics _diagnostics;
	private readonly UserPrivileges _privileges;

	internal ScopedDiagnostics(ServerDiagnostics diagnostics, UserPrivileges privileges)
	{
		_diagnostics = diagnostics;
		_privileges = privileges;
	}

	public DiagnosticsCommandResult Access()
	{
		return Denied() ?? DiagnosticsCommandResult.Ok;
	}

	public (DiagnosticsCommandResult Result, int Deleted) ClearCaches()
	{
		return Denied() is { } denied ? (denied, 0) : (DiagnosticsCommandResult.Ok, _diagnostics.ClearCaches());
	}

	public DiagnosticsCommandResult ResetExceptions()
	{
		if (Denied() is { } denied) return denied;

		_diagnostics.ResetExceptions();
		return DiagnosticsCommandResult.Ok;
	}

	public DiagnosticsCommandResult CollectGarbage()
	{
		if (Denied() is { } denied) return denied;

		ServerDiagnostics.CollectGarbage();
		return DiagnosticsCommandResult.Ok;
	}

	private DiagnosticsCommandResult? Denied()
	{
		return ScopedPrivileges.Lacks(_privileges, UserPrivileges.Developer)
			? DiagnosticsCommandResult.Denied
			: null;
	}
}