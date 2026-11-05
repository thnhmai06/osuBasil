using Basil.Application.Common;
using Basil.Application.Diagnostics.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Diagnostics.Capabilities;

/// <summary>Who may look at the server's diagnostics and run its maintenance actions.</summary>
public readonly struct ScopedDiagnostics
{
	private readonly ServerDiagnostics _diagnostics;
	private readonly UserPermissions _rights;

	internal ScopedDiagnostics(ServerDiagnostics diagnostics, UserPermissions rights)
	{
		_diagnostics = diagnostics;
		_rights = rights;
	}

	/// <summary>Checks that the caller may use the diagnostics.</summary>
	/// <returns>Ok, or the permissions the caller lacks.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerManageDiagnostics" />.</remarks>
	public Result<DiagnosticsOutcome> Access()
	{
		return Denied() ?? DiagnosticsOutcome.Ok;
	}

	/// <summary>Empties the cache folder of derived files.</summary>
	/// <returns>The outcome and how many files were deleted.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerManageDiagnostics" />.</remarks>
	public (Result<DiagnosticsOutcome> Result, int Deleted) ClearCaches()
	{
		return Denied() is { } denied ? (denied, 0) : (DiagnosticsOutcome.Ok, _diagnostics.ClearCaches());
	}

	/// <summary>Forgets the counted exceptions.</summary>
	/// <returns>The outcome.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerManageDiagnostics" />.</remarks>
	public Result<DiagnosticsOutcome> ResetExceptions()
	{
		if (Denied() is { } denied) return denied;

		_diagnostics.ResetExceptions();
		return DiagnosticsOutcome.Ok;
	}

	/// <summary>Collects the garbage now.</summary>
	/// <returns>The outcome.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerManageDiagnostics" />.</remarks>
	public Result<DiagnosticsOutcome> CollectGarbage()
	{
		if (Denied() is { } denied) return denied;

		ServerDiagnostics.CollectGarbage();
		return DiagnosticsOutcome.Ok;
	}

	private Result<DiagnosticsOutcome>? Denied()
	{
		return ScopedRights.Require(_rights, UserPermissions.ServerManageDiagnostics) is { } missing
			? Result<DiagnosticsOutcome>.Denied(missing)
			: null;
	}
}