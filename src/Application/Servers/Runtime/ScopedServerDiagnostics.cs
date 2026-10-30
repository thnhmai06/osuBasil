using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Runtime;

/// <summary>Who may look at the server's diagnostics and run its maintenance actions.</summary>
public sealed class ScopedServerDiagnostics
{
	private readonly ScopedServer _scoped;

	internal ScopedServerDiagnostics(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Checks that the caller may use the diagnostics.</summary>
	/// <returns>Ok, or the permissions the caller lacks.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerManageDiagnostics" />.</remarks>
	public Result<ServerOutcome> Access()
	{
		return _scoped.Needs(UserPermissions.ServerManageDiagnostics) is { } missing
			? Result<ServerOutcome>.Denied(missing)
			: ServerOutcome.Ok;
	}
}
