using Basil.Application.Diagnostics.Objects;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

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

	public DiagnosticsOutcome Access()
	{
		return Denied() ?? DiagnosticsOutcome.Ok;
	}

	public (DiagnosticsOutcome Result, int Deleted) ClearCaches()
	{
		return Denied() is { } denied ? (denied, 0) : (DiagnosticsOutcome.Ok, _diagnostics.ClearCaches());
	}

	public DiagnosticsOutcome ResetExceptions()
	{
		if (Denied() is { } denied) return denied;

		_diagnostics.ResetExceptions();
		return DiagnosticsOutcome.Ok;
	}

	public DiagnosticsOutcome CollectGarbage()
	{
		if (Denied() is { } denied) return denied;

		ServerDiagnostics.CollectGarbage();
		return DiagnosticsOutcome.Ok;
	}

	private DiagnosticsOutcome? Denied()
	{
		return ScopedPrivileges.Lacks(_privileges, UserPrivileges.Developer)
			? DiagnosticsOutcome.Denied
			: null;
	}
}