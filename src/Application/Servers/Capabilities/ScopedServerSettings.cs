using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Reads and changes the policy settings of the running server.</summary>
public readonly struct ScopedServerSettings
{
	private readonly ScopedServer _scoped;

	internal ScopedServerSettings(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Reads the settings in force.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the settings.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Developer" />.</remarks>
	public async Task<(Result<ServerOutcome> Result, ServerSettings? Settings)> GetAsync(
		CancellationToken ct = default)
	{
		return _scoped.Needs(UserPrivileges.Developer) is { } missing
			? (Result<ServerOutcome>.Denied(missing), null)
			: (ServerOutcome.Ok, await _scoped.Server.GetSettingsAsync(ct));
	}

	/// <summary>Replaces the settings; the change applies at once and survives a restart.</summary>
	/// <param name="settings">The new settings.</param>
	/// <returns>The outcome of storing the settings.</returns>
	/// <remarks>
	///     Requires <see cref="UserPrivileges.Developer" />. Changing the privileges that new accounts
	///     receive also requires every privilege of the new set to be in effect for the caller, as granting does.
	/// </remarks>
	public async Task<Result<ServerOutcome>> UpdateAsync(ServerSettings settings)
	{
		if (_scoped.Needs(UserPrivileges.Developer) is { } missing)
			return Result<ServerOutcome>.Denied(missing);

		var current = await _scoped.Server.GetSettingsAsync();
		if (settings.Accounts.DefaultPrivileges != current.Accounts.DefaultPrivileges &&
		    _scoped.Needs(settings.Accounts.DefaultPrivileges) is { } ungrantable)
			return Result<ServerOutcome>.Denied(ungrantable);

		return await _scoped.Server.UpdateSettingsAsync(settings);
	}
}