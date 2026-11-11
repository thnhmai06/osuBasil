using Basil.Application.Shared;
using Basil.Application.Diagnostics.Objects;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;

namespace Basil.Application.Diagnostics.Capabilities;

/// <summary>Who may look at the server's diagnostics and run its maintenance actions.</summary>
public readonly struct ScopedDiagnostics
{
	private readonly ServerDiagnostics _diagnostics;
	private readonly UserPrivileges _privileges;

	internal ScopedDiagnostics(ServerDiagnostics diagnostics, UserPrivileges privileges)
	{
		_diagnostics = diagnostics;
		_privileges = privileges;
	}

	/// <summary>Checks that the caller may use the diagnostics.</summary>
	/// <returns>Ok, or the privileges the caller lacks.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Developer" />.</remarks>
	public DiagnosticsOutcome Access()
	{
		return Denied() ?? DiagnosticsOutcome.Ok;
	}

	/// <summary>Empties the cache folder of derived files.</summary>
	/// <returns>The outcome and how many files were deleted.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Developer" />.</remarks>
	public (DiagnosticsOutcome Result, int Deleted) ClearCaches()
	{
		return Denied() is { } denied ? (denied, 0) : (DiagnosticsOutcome.Ok, _diagnostics.ClearCaches());
	}

	/// <summary>Forgets the counted exceptions.</summary>
	/// <returns>The outcome.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Developer" />.</remarks>
	public DiagnosticsOutcome ResetExceptions()
	{
		if (Denied() is { } denied) return denied;

		_diagnostics.ResetExceptions();
		return DiagnosticsOutcome.Ok;
	}

	/// <summary>Collects the garbage now.</summary>
	/// <returns>The outcome.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Developer" />.</remarks>
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