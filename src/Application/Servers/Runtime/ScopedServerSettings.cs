using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Runtime;

/// <summary>Reads and changes the policy settings of the running server.</summary>
public sealed class ScopedServerSettings
{
	private readonly ScopedServer _scoped;

	internal ScopedServerSettings(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Reads the settings in force.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the settings.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerManageSettings" />.</remarks>
	public async Task<(Result<ServerOutcome> Result, ServerSettings? Settings)> GetAsync(
		CancellationToken cancellationToken = default)
	{
		return _scoped.Needs(UserPermissions.ServerManageSettings) is { } missing
			? (Result<ServerOutcome>.Denied(missing), null)
			: (ServerOutcome.Ok, await _scoped.Server.Context.Settings.GetAsync(cancellationToken));
	}

	/// <summary>Replaces the settings; the change applies at once and survives a restart.</summary>
	/// <param name="settings">The new settings.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of storing the settings.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerManageSettings" />.</remarks>
	public async Task<Result<ServerOutcome>> UpdateAsync(ServerSettings settings,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Needs(UserPermissions.ServerManageSettings) is { } missing)
			return Result<ServerOutcome>.Denied(missing);

		await _scoped.Server.Context.Settings.CreateOrUpdateAsync(settings, cancellationToken);
		return ServerOutcome.Ok;
	}
}
